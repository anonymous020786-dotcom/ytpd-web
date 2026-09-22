/**
 * ytpd-web edge Worker.
 *
 * Public entrypoint for ytpd.videodownloaders.cloud. Proxies to the real
 * origin (ytpd-origin.videodownloaders.cloud, which is only reachable
 * through the Cloudflare Tunnel running on the EC2 instance - see
 * ../README.md for the full architecture). The EC2 instance is stopped
 * by default to save cost, so the origin fetch will fail whenever it's
 * asleep; on that failure this Worker calls the wake Lambda (SigV4/IAM
 * authenticated, not a shared secret) and returns a short "waking up"
 * page that retries itself.
 *
 * Required secrets (set via the Cloudflare API, never committed - see
 * ../worker_aws_key.tmp, which is gitignored):
 *   AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY  - the ytpd-worker IAM user,
 *     scoped to lambda:InvokeFunction on just ytpd-start-instance.
 *   CF_ACCESS_CLIENT_ID, CF_ACCESS_CLIENT_SECRET - a Cloudflare Access
 *     service token, the only thing allowed through Access's policy on
 *     ytpd-origin.videodownloaders.cloud (see ../README.md "security").
 *
 * No template literals are used in this file on purpose - it's deployed
 * by embedding this source as a string inside another script (see
 * ../README.md, "deploying the Worker"), and backticks would need
 * escaping there. Plain string concatenation avoids that entirely.
 */

var ORIGIN_HOST = "ytpd-origin.videodownloaders.cloud";
var AWS_REGION = "ap-south-1";
var LAMBDA_FUNCTION = "ytpd-start-instance";
var LAMBDA_HOST = "lambda." + AWS_REGION + ".amazonaws.com";
var LAMBDA_PATH = "/2015-03-31/functions/" + LAMBDA_FUNCTION + "/invocations";

// How long we're willing to wait for the origin before treating it as
// "asleep" and falling back to the wake+retry page. Generous, since a
// cold TCP connect to a stopped/unreachable tunnel can hang.
var ORIGIN_TIMEOUT_MS = 6000;

// Per-colo throttle so a burst of requests while the instance is booting
// doesn't fire a wake Lambda invocation per request. Lambda invoke is
// idempotent and cheap, but this keeps it tidy under this account's low
// concurrency limit (see ../README.md).
var WAKE_COOLDOWN_SECONDS = 20;

export default {
  async fetch(request, env, ctx) {
    var originUrl = new URL(request.url);
    originUrl.hostname = ORIGIN_HOST;

    var originRequest = new Request(originUrl, request);
    originRequest.headers.set("Host", ORIGIN_HOST);
    // ytpd-origin sits behind Cloudflare Access (service-token-only policy,
    // no interactive login) - direct requests without these get a 403 from
    // Access itself, confirmed live. Without this, anyone who found the
    // hostname (e.g. via public Certificate Transparency logs) could hit
    // the backend directly, bypassing this Worker entirely.
    originRequest.headers.set("CF-Access-Client-Id", env.CF_ACCESS_CLIENT_ID);
    originRequest.headers.set("CF-Access-Client-Secret", env.CF_ACCESS_CLIENT_SECRET);

    try {
      var response = await fetchWithTimeout(originRequest, ORIGIN_TIMEOUT_MS);
      // The tunnel is up but the instance/app behind it isn't actually
      // serving yet (Cloudflare's own 502/521/522/523/524/530 for a
      // connector with no live origin - 530/error 1033 is what an
      // entirely disconnected Tunnel actually returns, confirmed live)
      // - treat the same as unreachable.
      if ([502, 521, 522, 523, 524, 530].indexOf(response.status) !== -1) {
        ctx.waitUntil(wakeIfNeeded(env));
        return wakingPage();
      }
      return stripAccessCookie(response);
    } catch (err) {
      ctx.waitUntil(wakeIfNeeded(env));
      return wakingPage();
    }
  },
};

// Cloudflare Access sets a CF_Authorization session cookie on responses
// from ytpd-origin, tied to the Access app protecting it (see the fetch
// handler above) - confirmed harmless if replayed directly at the origin
// (Access still rejects it), but this app doesn't use cookies for its own
// auth (JWT bearer only) and there's no reason to hand an internal auth
// artifact to every browser that talks to this Worker.
function stripAccessCookie(response) {
  var copy = new Response(response.body, response);
  copy.headers.delete("set-cookie");
  return copy;
}

async function fetchWithTimeout(request, timeoutMs) {
  var controller = new AbortController();
  var timer = setTimeout(function () { controller.abort(); }, timeoutMs);
  try {
    return await fetch(request, { signal: controller.signal });
  } finally {
    clearTimeout(timer);
  }
}

async function wakeIfNeeded(env) {
  var cache = caches.default;
  var cooldownKey = new Request("https://ytpd-wake-cooldown.internal/");

  var cached = await cache.match(cooldownKey);
  if (cached) return; // already woke it recently, don't spam the Lambda

  await cache.put(
    cooldownKey,
    new Response("1", { headers: { "Cache-Control": "max-age=" + WAKE_COOLDOWN_SECONDS } })
  );

  try {
    await invokeWakeLambda(env);
  } catch (err) {
    // Best-effort - if this particular request's wake call fails, the
    // next request within the retry loop will try again once the
    // cooldown above expires.
    console.error("wake lambda invoke failed", err);
  }
}

async function invokeWakeLambda(env) {
  var body = "{}";
  var signedHeaders = await signLambdaInvoke(env, body);

  var resp = await fetch("https://" + LAMBDA_HOST + LAMBDA_PATH, {
    method: "POST",
    headers: signedHeaders,
    body: body,
  });

  if (!resp.ok) {
    var text = await resp.text().catch(function () { return ""; });
    throw new Error("lambda invoke returned " + resp.status + ": " + text);
  }
}

function wakingPage() {
  var html = "<!doctype html>\n" +
    "<html>\n" +
    "<head>\n" +
    "  <meta charset=\"utf-8\">\n" +
    "  <title>Starting up...</title>\n" +
    "  <meta http-equiv=\"refresh\" content=\"5\">\n" +
    "  <style>\n" +
    "    body { font-family: system-ui, sans-serif; display: flex; align-items: center;\n" +
    "           justify-content: center; height: 100vh; margin: 0; background: #0b0b12; color: #eee; }\n" +
    "    .box { text-align: center; max-width: 26rem; padding: 2rem; }\n" +
    "    .spinner { width: 2.5rem; height: 2.5rem; margin: 0 auto 1.25rem; border-radius: 50%;\n" +
    "               border: 3px solid #333; border-top-color: #6ea8fe; animation: spin 0.8s linear infinite; }\n" +
    "    @keyframes spin { to { transform: rotate(360deg); } }\n" +
    "    p { opacity: 0.75; line-height: 1.5; }\n" +
    "  </style>\n" +
    "</head>\n" +
    "<body>\n" +
    "  <div class=\"box\">\n" +
    "    <div class=\"spinner\"></div>\n" +
    "    <h2>Waking up the server</h2>\n" +
    "    <p>This app sleeps when idle to save cost. It's starting back up now - " +
    "    this page will refresh automatically. Usually ready within a minute.</p>\n" +
    "  </div>\n" +
    "</body>\n" +
    "</html>";

  return new Response(html, {
    status: 503,
    headers: { "content-type": "text/html; charset=utf-8", "retry-after": "5" },
  });
}

// --- Minimal AWS SigV4 signer for a single fixed request shape (Lambda
// Invoke, POST, no query string, JSON body). Not a general-purpose
// signer - deliberately narrow to what this Worker actually sends. ---

async function signLambdaInvoke(env, body) {
  var accessKeyId = env.AWS_ACCESS_KEY_ID;
  var secretAccessKey = env.AWS_SECRET_ACCESS_KEY;

  var now = new Date();
  var amzDate = now.toISOString().replace(/[:-]|\.\d{3}/g, ""); // YYYYMMDDTHHMMSSZ
  var dateStamp = amzDate.slice(0, 8); // YYYYMMDD

  var payloadHash = await sha256Hex(body);

  var canonicalHeaders =
    "content-type:application/x-amz-json-1.0\n" +
    "host:" + LAMBDA_HOST + "\n" +
    "x-amz-content-sha256:" + payloadHash + "\n" +
    "x-amz-date:" + amzDate + "\n";
  var signedHeaders = "content-type;host;x-amz-content-sha256;x-amz-date";

  var canonicalRequest = [
    "POST",
    LAMBDA_PATH,
    "",
    canonicalHeaders,
    signedHeaders,
    payloadHash,
  ].join("\n");

  var credentialScope = dateStamp + "/" + AWS_REGION + "/lambda/aws4_request";
  var stringToSign = [
    "AWS4-HMAC-SHA256",
    amzDate,
    credentialScope,
    await sha256Hex(canonicalRequest),
  ].join("\n");

  var signingKey = await getSignatureKey(secretAccessKey, dateStamp, AWS_REGION, "lambda");
  var signature = toHex(await hmac(signingKey, stringToSign));

  var authorization =
    "AWS4-HMAC-SHA256 Credential=" + accessKeyId + "/" + credentialScope +
    ", SignedHeaders=" + signedHeaders + ", Signature=" + signature;

  return {
    "content-type": "application/x-amz-json-1.0",
    "x-amz-date": amzDate,
    "x-amz-content-sha256": payloadHash,
    authorization: authorization,
  };
}

async function sha256Hex(message) {
  var data = typeof message === "string" ? new TextEncoder().encode(message) : message;
  var hash = await crypto.subtle.digest("SHA-256", data);
  return toHex(hash);
}

async function hmac(key, message) {
  var cryptoKey = await crypto.subtle.importKey(
    "raw",
    key,
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  return crypto.subtle.sign("HMAC", cryptoKey, new TextEncoder().encode(message));
}

async function getSignatureKey(secretAccessKey, dateStamp, region, service) {
  var kDate = await hmac(new TextEncoder().encode("AWS4" + secretAccessKey), dateStamp);
  var kRegion = await hmac(new Uint8Array(kDate), region);
  var kService = await hmac(new Uint8Array(kRegion), service);
  var kSigning = await hmac(new Uint8Array(kService), "aws4_request");
  return new Uint8Array(kSigning);
}

function toHex(buffer) {
  return Array.prototype.map.call(new Uint8Array(buffer), function (b) {
    return b.toString(16).padStart(2, "0");
  }).join("");
}
