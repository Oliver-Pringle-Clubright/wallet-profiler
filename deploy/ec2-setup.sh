#!/bin/bash
# EC2 Instance Setup Script for WalletProfiler
# Run this on a fresh Ubuntu 24.04 EC2 instance
# Usage: ssh into EC2, then: bash ec2-setup.sh

set -e

echo "=== WalletProfiler EC2 Setup ==="

# Update system
sudo apt-get update -y
sudo apt-get upgrade -y

# Install Docker
echo "Installing Docker..."
sudo apt-get install -y ca-certificates curl gnupg
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt-get update -y
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin

# Add current user to docker group
sudo usermod -aG docker $USER

echo ""
echo "=== Docker installed ==="
echo "Log out and back in for docker group to take effect, or run:"
echo "  newgrp docker"
echo ""
echo "=== Next Steps ==="
echo "1. Copy your project files to this instance"
echo "2. Create deploy/.env with your API keys"
echo "3. Create deploy/acp-config.json with your ACP credentials"
echo "4. Run: cd deploy && docker compose up -d"
