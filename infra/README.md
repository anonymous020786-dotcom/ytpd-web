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
  `/api/*` and `/hubs/*` to `http://localhost:8080` (the API container)
  and everything else to `http://localhost:3000` (the web container).
  `ytpd-origin.*` is an internal-only DNS name (CNAME to
  `<tunnel>.cfargotunnel.com`, proxied) - never given to users directly.
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

## What's still pending

- **Postgres connection string for the app** - needs the `ytpd_app` role's
  password reset (superuser role itself can't be altered directly, see
  above); this is a `ALTER ROLE ... WITH PASSWORD` statement, held for
  explicit user confirmation before running.
- **GHCR image pull access** - `ytpd-web-api`/`ytpd-web-frontend` images
  are built by `.github/workflows/build-images.yml` and pushed to GHCR
  (verified: both built successfully). The `ytpd-web` GitHub repo is
  private, so the packages default to private too - the EC2 instance
  needs either those packages made public, or a GitHub token installed on
  the instance for `docker login ghcr.io`. Held for explicit user
  confirmation before making anything public.
- **Actually running `docker compose up` on the instance** - `infra/docker-compose.yml`
  and `infra/deploy.sh` are ready; blocked only on the two items above
  (the `.env` file they need can't be finalized without the Postgres
  password).

## Resource inventory (for cleanup/reference)

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
| Supabase project | `hgeswxsxnzhfrzkqytuu` (org `ufagevmmfczcdvitlhfy`) |
