#!/bin/bash
# Bootstraps a fresh EC2 instance with everything needed to run the
# ytpd-web stack: Docker + Compose plugin, and cloudflared (for the
# outbound-only Cloudflare Tunnel - no inbound ports are ever opened on
# this instance's security group, see infra/README.md).
#
# This installs TOOLING only. It does not start the app - that happens
# separately via SSM once the Cloudflare Tunnel token and app secrets
# (.env) are in place, since those aren't baked into a public AMI/script.
set -euxo pipefail

apt-get update -y
apt-get install -y ca-certificates curl gnupg

# Docker Engine + Compose plugin (official repo, not the distro's older package)
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | tee /etc/apt/sources.list.d/docker.list > /dev/null
apt-get update -y
apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

systemctl enable --now docker
usermod -aG docker ubuntu

# cloudflared (Cloudflare Tunnel daemon)
curl -fsSL https://pkg.cloudflare.com/cloudflare-main.gpg -o /usr/share/keyrings/cloudflare-main.gpg
echo "deb [signed-by=/usr/share/keyrings/cloudflare-main.gpg] https://pkg.cloudflare.com/cloudflared $(. /etc/os-release && echo "$VERSION_CODENAME") main" | tee /etc/apt/sources.list.d/cloudflared.list
apt-get update -y
apt-get install -y cloudflared

mkdir -p /opt/ytpd
echo "bootstrap complete" > /opt/ytpd/bootstrap.done
