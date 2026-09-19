# Playlist Grabber (web)

A web migration of [shaked6540/YoutubePlaylistDownloader](https://github.com/shaked6540/YoutubePlaylistDownloader), a Windows desktop app. Same download engine (YoutubeExplode + ffmpeg + TagLib#), reachable from a browser instead of a WPF UI.

> **Heads up:** downloading YouTube video content is against YouTube's Terms of Service. This app is built for **private, auth-gated, personal use** (see below) — it is not designed or intended as an open public service.

## Architecture

```
┌─────────────┐      ┌───────────────────┐      ┌──────────────┐
│  Next.js UI │◄────►│  ASP.NET Core API  │◄────►│  SQLite (job │
│ (App Router,│ REST │  + SignalR hub     │      │  history)    │
│  shadcn/ui) │  &   │                    │      └──────────────┘
└─────────────┘  WS  │  DownloadWorker    │      ┌──────────────┐
                      │  (background queue)│◄────►│ ffmpeg +     │
                      └───────────────────┘      │ YoutubeExplode│
                                                   └──────────────┘
```

- **backend/YtpdWeb.Api** — ASP.NET Core 8 Web API. Resolves video/playlist/channel URLs, queues downloads, drives `ffmpeg` for muxing/transcoding, tags audio files with TagLib#, and pushes live progress over SignalR. Single admin login via a JWT issued from a username + PBKDF2 password hash in config — no open signup.
- **frontend** — Next.js (App Router) + Tailwind + shadcn/ui. Paste a URL, pick videos, choose format/quality, watch live per-video progress, download individually or as a zip.
- Deploys as three containers behind Caddy (automatic HTTPS): `web`, `api`, `caddy`, sharing one domain so the frontend never needs CORS in production.

## Local development

Requires: .NET 8 SDK, Node 20+, `ffmpeg` on your PATH.

**Backend**

```bash
cd backend/YtpdWeb.Api
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)"
dotnet user-secrets set "Auth:Username" "admin"
dotnet run -- hash-password "your-password"   # prints a hash
dotnet user-secrets set "Auth:PasswordHash" "<paste the hash>"
dotnet user-secrets set "Storage:TempPath" "./.rundata/temp"
dotnet user-secrets set "Storage:DownloadsPath" "./.rundata/downloads"
dotnet user-secrets set "Database:Path" "./.rundata/ytpd.db"
dotnet run
```

API listens on `http://localhost:8080` (Swagger UI at `/swagger` in Development).

**Frontend**

```bash
cd frontend
cp .env.local.example .env.local   # NEXT_PUBLIC_API_URL=http://localhost:8080
npm install
npm run dev
```

Visit `http://localhost:3000`, sign in with the username/password you set above.

## Deploying to a Hostinger VPS

1. Provision a VPS (Ubuntu, Docker template or install Docker yourself) and point a domain's A record at it.
2. Clone this repo onto the VPS.
3. `cp .env.example .env` and fill in `DOMAIN`, `JWT_SECRET` (`openssl rand -base64 48`), `AUTH_USERNAME`.
4. Generate the password hash **before** the stack is up, using a one-off build:
   ```bash
   docker compose build api
   docker compose run --rm api hash-password 'your-password-here'
   ```
   (the image's `ENTRYPOINT` is already `dotnet YtpdWeb.Api.dll`, so only pass the extra args here)
   Paste the output into `AUTH_PASSWORD_HASH` in `.env`.
5. `docker compose up -d --build`
6. Caddy will automatically obtain a Let's Encrypt certificate for `DOMAIN` on first request — give it a minute, then visit `https://<your-domain>`.

Downloaded files live in the `ytpd_data` Docker volume and are auto-deleted after `Storage:RetentionHours` (default 48h) — this app is meant to hand you a file, not archive a media library.

## Supported formats

- Video: MP4, MKV (video-only + audio-only streams downloaded separately, muxed with `ffmpeg -c copy`)
- Audio: MP3, M4A, WAV (transcoded from the best available audio stream; MP3/M4A get auto-tagged title/artist/album from the video title, same heuristic as the original desktop app)

## What didn't carry over from the desktop app

- Multi-language UI (14 languages) — English only for now; the frontend is structured so `next-intl` could be added later without a rewrite.
- Auto-update mechanism — not applicable to a web app.
- Per-user themes beyond light/dark.

## Migration guide note

The AWS API MCP server used elsewhere in this workspace is entering end-of-development; see the [migration guide](https://github.com/awslabs/mcp/blob/main/src/aws-api-mcp-server/MIGRATION.md) if you later wire AWS services into this project instead of Hostinger.
