#!/bin/bash
# One-time (or re-run-anytime) step after the bot is deployed: tells
# Telegram where to POST updates. Run locally, not on the EC2 instance -
# only needs internet access to api.telegram.org, not anything on the box.
#
# Usage: TELEGRAM_BOT_TOKEN=... TELEGRAM_WEBHOOK_SECRET=... ./set-telegram-webhook.sh
set -euo pipefail

: "${TELEGRAM_BOT_TOKEN:?Set TELEGRAM_BOT_TOKEN}"
: "${TELEGRAM_WEBHOOK_SECRET:?Set TELEGRAM_WEBHOOK_SECRET}"

curl -sS "https://api.telegram.org/bot${TELEGRAM_BOT_TOKEN}/setWebhook" \
  -H "Content-Type: application/json" \
  -d "{\"url\":\"https://ytpd.videodownloaders.cloud/api/bot/webhook\",\"secret_token\":\"${TELEGRAM_WEBHOOK_SECRET}\"}"
echo
