#!/bin/bash
# Deploys/updates the app on the EC2 instance. Run via SSM send-command,
# not SSH (this instance has no inbound ports open - see ../README.md).
#
# Expects /opt/ytpd/.env to already exist with real secrets (written
# separately, never committed - see ../.env.example for the shape).
set -euxo pipefail

mkdir -p /opt/ytpd
cd /opt/ytpd

# docker-compose.yml and .env are placed here by a separate SSM step
# before this script runs.
docker compose pull
docker compose up -d
docker compose ps
