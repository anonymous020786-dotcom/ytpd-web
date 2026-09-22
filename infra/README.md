# ytpd-web hosting infra (AWS + Cloudflare, scale-to-zero)

Cost-conscious deployment: the backend doesn't run 24/7. It's an EC2
instance that's **stopped by default**, woken on the first incoming
request, and automatically stopped again after a period of inactivity.

## Why this shape

- A hosted backend is needed at all because mobile/bot clients can't run
  the desktop app's local sidecar (ffmpeg + ASP.NET Core spawned as a
  child process) - see the desktop repo's README for why.
- Running that backend 24/7 on a always-on server costs money for a
  low-traffic personal tool; scale-to-zero avoids that without giving up
  a real Docker/ffmpeg-capable server (which a pure serverless/Lambda
  rewrite can't cleanly provide - see "why not Lambda" below).

## Architecture

```
User's browser
      |
      v
Cloudflare (ytpd.videodownloaders.cloud)
      |
      +-- Worker: tries to proxy to the origin over the Tunnel.
      |     - Success -> proxies through, done.
      |     - Origin unreachable -> calls the wake Lambda (SigV4-signed,
      |       IAM auth), shows a "waking up, retrying..." page, retries
      |       with backoff.
      |
      v
Cloudflare Tunnel (cloudflared, outbound-only from the EC2 instance -
no inbound ports are ever opened on its security group)
      |
      v
EC2 instance (t3.micro, off by default)
  - Docker Compose: api (YtpdWeb.Api) + web (Next.js) + cloudflared
  - Database: Supabase Postgres (see ../backend - ConnectionStrings:Postgres)

AWS Lambda (ytpd-start-instance)      AWS Lambda (ytpd-stop-if-idle)
  - Invoked by the Worker               - EventBridge schedule, every 10 min
  - Starts the EC2 instance if stopped  - Stops it if CloudWatch NetworkIn
  - IAM-authenticated (see below)         has been near-zero for 20 min
```

## Why not AWS Lambda for the backend itself

Seriously considered and rejected: the backend spawns `ffmpeg` as a real
OS child process and uses SignalR (persistent WebSocket connections) for
live download progress. Neither fits Lambda's model (no arbitrary
long-running child processes, 15-minute hard execution limit, no
persistent WebSocket support without a substantial rearchitecture). An
EC2 instance that's usually stopped gets the cost benefit of serverless
without giving up the parts of the app that need a real server.

## What's built and verified

- **EC2 instance** `i-03310166c27b0413a` (t3.micro, Ubuntu 24.04, default
  VPC/subnet, **zero inbound security group rules** - management is via
  SSM Session Manager, not SSH; app access will be via Cloudflare Tunnel,
  not a public IP). User-data (`ec2-user-data.sh`) installs Docker +
  Compose plugin + cloudflared on first boot - verified via SSM that all
  three are actually installed and working.
- **`ytpd-start-instance` Lambda** (`lambda/start-instance/`) - starts the
  instance if stopped. Verified end-to-end: stopped the instance for
  real, invoked the Lambda with the same restricted credentials the
  Cloudflare Worker will use, confirmed it started back up.
- **`ytpd-stop-if-idle` Lambda** (`lambda/stop-if-idle/`) - on an
  EventBridge 10-minute schedule, stops the instance if `NetworkIn` has
  been under ~500KB for the last 20 minutes (with a 5-minute grace period
  after boot so it can't stop an instance that just woke up). Verified by
  manual invocation against the real, actively-busy instance (correctly
  chose not to stop it).
- **IAM**: `ytpd-worker` user, scoped to `lambda:InvokeFunction` on just
  `ytpd-start-instance`, for the Cloudflare Worker to authenticate as.

### A real gotcha hit and solved on this fresh AWS account

An identity-based policy on `ytpd-worker` granting `lambda:InvokeFunction`
was **not sufficient on its own** - calls failed with `AccessDeniedException`
even though IAM's own policy simulator said "allowed". Also hit: Lambda
Function URLs (`AuthType=NONE` *and* `AuthType=AWS_IAM`) returned a
generic "Forbidden" regardless of correct configuration. Both are
consistent with new-AWS-account anti-abuse restrictions (this account's
Lambda concurrency limit is 5, vs. the normal default of 1000 - a clear
sign of "new account" status).

Fixes:
- Don't use Function URLs at all - use the plain Lambda `Invoke` API
  (SigV4-signed), which wasn't subject to the same restriction.
- Add an **explicit resource-based policy** on the Lambda naming the
  exact calling principal ARN (`aws lambda add-permission --principal
  arn:aws:iam::...:user/ytpd-worker`), *in addition to* the identity-based
  policy. Once both existed, invocation worked.

If AWS lifts these restrictions as the account ages/gets used, none of
this needs to change - it's just an extra permission statement.

## What's built and verified (Cloudflare side)

- **Tunnel** `ytpd-web` (`a147301c-c42e-4ad4-96a0-d0ffea60411a`), remotely
  managed config. Ingress: `ytpd-origin.videodownloaders.cloud` routes
  `/api/*` and `/hubs/*` to `http://api:8080` and everything else to
  `http://web:3000` - the docker-compose *service names*, not `localhost`.
  cloudflared runs as its own container with its own network namespace, so
  `localhost` there means "the cloudflared container itself"; pointing
  ingress at `localhost:PORT` looked reasonable but silently couldn't
  reach the app containers at all ("connection refused" in cloudflared's
  logs) until fixed. `ytpd-origin.*` is an internal-only DNS name (CNAME
  to `<tunnel>.cfargotunnel.com`, proxied) - never given to users directly.
- **Worker** `ytpd-worker` (`infra/worker/ytpd-worker.js`) - the public
  entrypoint. Proxies to `ytpd-origin.*`; on a network error or a
  tunnel-down response (502/521/522/523/524/530 - 530/error 1033 is what
  an actually-disconnected Tunnel returns, confirmed live) it invokes the
  wake Lambda (SigV4-signed with the `ytpd-worker` IAM credentials, set as
  Worker secrets `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY`) and returns
  a self-refreshing "waking up" page.
- **Routing gotcha hit and solved**: this Cloudflare account is shared
  with other existing projects on `videodownloaders.cloud` (an existing
  wildcard zone-level Worker Route `*.videodownloaders.cloud/*` ->
  `mediadl-wake-shield`, unrelated to this project). That wildcard was
  silently shadowing `ytpd.videodownloaders.cloud` even with a Custom
  Domain attached for `ytpd-worker`. Fixed by adding a specific route
  `ytpd.videodownloaders.cloud/*` -> `ytpd-worker` (Routes-vs-Routes
  precedence is well-defined: most specific pattern wins - this is the
  same mechanism the existing project already relies on, so it wasn't
  touched). The Custom Domain binding was left in place too; harmless.
- **Verified end-to-end for real**: with the EC2 instance stopped, a real
  HTTPS request to `https://ytpd.videodownloaders.cloud/` got the "waking
  up" page, and the instance transitioned to `running` shortly after -
  confirmed via `describe_instances`, not just log output.

## Fully deployed and verified end-to-end (2026-09-22)

The whole stack is live at **https://ytpd.videodownloaders.cloud** and every
layer has been tested against the real, running system (not just read
back from logs):

- `POSTGRES_CONNECTION_STRING` uses the Supavisor pooler
  (`aws-0-ap-south-1.pooler.supabase.com:5432`, username
  `ytpd_app.hgeswxsxnzhfrzkqytuu`), not the direct `db.<ref>.supabase.co`
  hostname. The direct hostname resolves to IPv6 only, and this EC2
  instance's VPC has no outbound IPv6 route - the API container crash-
  looped with `Network is unreachable` until this was fixed. See
  `.env.example` for the full explanation.
- GHCR packages `ytpd-web-api`/`ytpd-web-frontend` are public (changed via
  the GitHub web UI - there is no REST API for package visibility).
  `docker compose pull` on the instance needs no credentials as a result.
- Verified live: `GET /` returns real rendered frontend HTML; `POST
  /api/auth/login` issues a real JWT; an authenticated `GET
  /api/downloads` call round-trips through the API, the Supavisor pooler,
  and Postgres and returns `[]`; `public.Jobs`/`public.JobItems` exist in
  Supabase with the EF Core migration history recorded.
- Login credentials for the hosted app: username `admin`, password
  handed to the user directly (not stored in this repo or in any `.tmp`
  file beyond `infra/app_secrets.tmp`, which is gitignored).
- RLS is disabled on `Jobs`/`JobItems`/`__EFMigrationsHistory` (Supabase
  advisor flagged this). Confirmed **not** currently exploitable: `anon`
  and `authenticated` have zero grants on these tables (checked via
  `information_schema.role_table_grants`), so Supabase's PostgREST data
  API can't reach them regardless of RLS. This app doesn't use Supabase's
  client libraries/Data API at all - it's a plain Npgsql connection from
  the `ytpd_app` role - so enabling RLS isn't actually needed here, only
  worth knowing about if that ever changes.

## Telegram bot

A bot living inside the existing `api` container (`Controllers/TelegramController.cs`,
`Bot/`) - not a separate app, so it reuses the same `YoutubeResolverService`,
`DownloadQueue`/`DownloadWorker`, and Postgres-backed job tracking the web
UI uses. Disabled by default (`Telegram:BotToken` empty).

**Why a self-hosted Bot API server**: the public `api.telegram.org` caps
bot uploads at 50MB, useless for actual video files. Running
`telegram-bot-api` (`github.com/tdlib/telegram-bot-api`, via the
`aiogram/telegram-bot-api` image) locally with `--local` raises that to
2GB. It shares the `api-data` volume with the `api` container, which the
upstream docs suggest lets you skip a real upload by passing a local path
or `file://` URI directly - tried both live against this image and both
get parsed as a remote URL and rejected ("invalid file HTTP URL
specified"), so that optimization doesn't actually work here regardless
of the docs. File delivery is a genuine multipart upload instead (one
hop over the docker-internal network to `telegram-bot-api`, not over the
real internet) - confirmed live: real file delivered with metadata
intact.

**Flow**: Telegram POSTs updates to `https://ytpd.videodownloaders.cloud/api/bot/webhook`
(routed through the same Worker/Tunnel as everything else - no new
Cloudflare config needed, it's under `/api/*`). The controller checks a
secret header (set via `setWebhook`, see `set-telegram-webhook.sh`),
resolves a pasted YouTube link, offers format buttons, creates a normal
`DownloadJob`/`DownloadJobItem` on button press, and polls for completion
in a background task before sending the finished file back.

**Access control, two tiers**:
- `Telegram:AllowedChatIds` (env, `.env`'s `TELEGRAM_ALLOWED_CHAT_IDS`) -
  fixed **admins**, requires editing `.env` and redeploying to change.
- `TelegramAllowedUser` (Postgres table) - everyone else, added live from
  inside Telegram by an admin, no redeploy needed:
  - `/adduser <chat_id> [label]` - approve a chat. Also best-effort DMs
    that chat to let them know (only works if they've messaged the bot
    before - Telegram won't let a bot cold-message someone).
  - `/removeuser <chat_id>` - revoke.
  - `/users` - list current admins + approved users.

**Setup** (all manual, needs the owner's own Telegram account - nothing
here can be automated from this side):
1. Create a bot via [@BotFather](https://t.me/BotFather) (`/newbot`) -> bot token.
2. Get `api_id`/`api_hash` from https://my.telegram.org/apps (only used by
   the local Bot API server, not the bot token itself).
3. Fill in `TELEGRAM_*` in the instance's `.env` (see `.env.example`),
   including `COMPOSE_PROFILES=bot` to actually start the
   `telegram-bot-api` container (it's profile-gated - without real
   `api_id`/`api_hash` it crash-loops, so it's off unless configured).
4. `docker compose up -d` (or re-run `deploy.sh`).
5. `TELEGRAM_BOT_TOKEN=... TELEGRAM_WEBHOOK_SECRET=... ./set-telegram-webhook.sh`
6. Message the bot from Telegram. With `TELEGRAM_ALLOWED_CHAT_IDS` still
   empty it'll reply with your chat ID instead of doing anything - put
   that in `.env`'s `TELEGRAM_ALLOWED_CHAT_IDS` (this makes you an admin)
   and redeploy once. After that, approve everyone else with `/adduser`
   from inside Telegram - no more redeploys needed for new users.

**Known limitation, by design**: if the EC2 instance is asleep when
Telegram delivers a webhook, the Worker returns its usual "waking up"
503 (see above) and triggers the wake Lambda, same as a browser request -
but there's no bot-specific handling to tell the user that via Telegram
itself (that would mean giving the Worker its own copy of the bot token
and Telegram-update parsing, which felt like real complexity for a corner
case). Telegram retries failed webhook deliveries on its own backoff, so
in practice a message sent to a sleeping bot often just works a few
seconds later - but the honest fallback is: if the bot doesn't respond,
resend once the instance has had ~30-60s to wake up.

## Security audit (2026-09-22)

A real pass across AWS, Cloudflare, Supabase, and GitHub - not just a
checklist. What was checked, what was fixed, and what's a conscious
tradeoff:

**Fixed:**
- **Internal origin was actually publicly reachable.** `ytpd-origin.videodownloaders.cloud`
  was a normal proxied DNS record - "internal" only by convention, not
  enforcement. Anyone who found it (it's logged in public Certificate
  Transparency logs, so "hidden" was never real protection) could hit the
  backend directly, bypassing the Worker's wake logic and any future
  Cloudflare-level protections entirely. Confirmed live: a direct `curl`
  reached the app with no Worker involved.
  Fixed by putting it behind **Cloudflare Access** with a service-token-only
  policy (`decision: non_identity` - no interactive login offered at all),
  matching the pattern this account already uses for two other projects
  (`app.videodownloaders.cloud`, `staging.dlcardgenerator.in`). The Worker
  now sends `CF-Access-Client-Id`/`CF-Access-Client-Secret` (a dedicated
  service token, `ytpd-worker-origin-access`) on every origin fetch.
  Verified live: direct requests to `ytpd-origin.*` now get a 403 from
  Access itself; the public hostname (through the Worker) still works.
- **RLS was disabled** on all four Postgres tables. Confirmed it wasn't
  currently exploitable (`anon`/`authenticated` had zero grants, so
  Supabase's PostgREST couldn't reach them regardless), but enabled it
  anyway as defense-in-depth against a future accidental `GRANT`. Verified
  safe first: `ytpd_app` (the app's connection role) owns all four tables,
  and Postgres never applies RLS to a table's owner by default - so this
  has zero effect on the running app (confirmed: login + an authenticated
  DB query both still worked immediately after).
- **No rate limiting existed anywhere on the zone.** Added a Cloudflare
  rate-limit rule scoped only to `ytpd.videodownloaders.cloud`'s
  `/api/auth/login` (5 requests / 10s per IP, the free plan's only
  allowed period - it doesn't touch other projects' traffic). Not a
  strong brute-force defense on its own, but the actual defense is the
  20-character random admin password; this just adds friction.
- **GitHub Dependabot alerts were off** on all three repos
  (`ytpd-web`, `ytpd-desktop`, `ytpd-mobile`) - free, no downside, now on.

**Checked and already fine:**
- EC2 security group has zero inbound rules (confirmed again); the
  instance does have a public IP, but nothing is reachable on it - Docker
  publishes no ports to the host, and the SG blocks everything anyway.
  Only path in is the outbound-only Cloudflare Tunnel.
- No SSH - management is SSM Session Manager only.
- IAM: `ytpd-worker` (used by the Worker) is scoped to exactly
  `lambda:InvokeFunction` on one function. `ytpd-lambda-role`'s EC2
  control policy is tag-conditioned. No console passwords on either IAM
  user (API keys only). No S3 buckets in the account.

**Known tradeoffs, not fixed (need your call, or need you personally -
can't be done from here):**
- **`ytpd-deploy` (the admin IAM user used for all deployment work) has
  no MFA.** Root does have MFA. Setting up MFA on an IAM user needs a
  live authenticator-app scan, which has to happen on your end -
  `aws iam create-virtual-mfa-device` gets you the QR seed, then you'd
  scan it and give me two consecutive TOTP codes to call
  `enable-mfa-device`. Worth doing given this key has `AdministratorAccess`.
- **No CloudTrail trail.** Zero audit logging beyond AWS's own 90-day
  Event History. A basic trail has a small ongoing S3 cost - didn't
  enable it unasked given the project's cost-consciousness; say the word
  and I will.
- **Zone-wide TLS settings weren't changed**: "Always Use HTTPS" is off
  and minimum TLS is 1.0 for the whole `videodownloaders.cloud` zone -
  both would be safe modern defaults, but they're zone-wide and this zone
  is shared with your other projects (`chat`, `app`, `staging.dlcardgenerator.in`),
  which I haven't tested against a TLS 1.2 floor. Didn't want to touch
  shared settings without asking first.
- **Branch protection isn't available** on `ytpd-web` without GitHub Pro
  (private repo) - not a priority for a solo-owner repo with no
  collaborators to protect against.

| Resource | ID/Name |
|---|---|
| EC2 instance | `i-03310166c27b0413a` |
| Security group | `sg-08c3612c87f8438a8` (zero inbound rules) |
| IAM role (EC2) | `ytpd-ec2-role` / instance profile `ytpd-ec2-profile` |
| IAM role (Lambdas) | `ytpd-lambda-role` |
| IAM user (Worker) | `ytpd-worker` |
| Lambda | `ytpd-start-instance` |
| Lambda | `ytpd-stop-if-idle` |
| EventBridge rule | `ytpd-stop-if-idle-schedule` (rate: 10 minutes) |
| Cloudflare Tunnel | `ytpd-web` (`a147301c-c42e-4ad4-96a0-d0ffea60411a`) |
| Cloudflare Worker | `ytpd-worker` |
| Cloudflare Worker route | `ytpd.videodownloaders.cloud/*` -> `ytpd-worker` |
| DNS (internal, tunnel origin) | `ytpd-origin.videodownloaders.cloud` |
| DNS (public) | `ytpd.videodownloaders.cloud` (Worker custom domain) |
| Cloudflare Access app | `ytpd-web internal origin` (protects `ytpd-origin.*`) |
| Cloudflare Access service token | `ytpd-worker-origin-access` |
| Cloudflare rate limit rule | login throttle on `/api/auth/login` |
| Supabase project | `hgeswxsxnzhfrzkqytuu` (org `ufagevmmfczcdvitlhfy`) |
