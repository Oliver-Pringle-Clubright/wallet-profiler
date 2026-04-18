#!/bin/bash
# Deploy WalletProfiler (v2) to EC2
# Usage: bash deploy.sh <ec2-user@host> <key-file.pem>

set -e

EC2_HOST="$1"
KEY_FILE="$2"
REMOTE_DIR="/home/ubuntu/walletprofiler"

if [ -z "$EC2_HOST" ] || [ -z "$KEY_FILE" ]; then
  echo "Usage: bash deploy.sh <ec2-user@host> <key-file.pem>"
  exit 1
fi

SSH="ssh -i $KEY_FILE -o StrictHostKeyChecking=no"
SCP="scp -i $KEY_FILE -o StrictHostKeyChecking=no"

echo "=== Deploying WalletProfiler v2 to $EC2_HOST ==="

$SSH $EC2_HOST "mkdir -p $REMOTE_DIR/wallet-profiler/{profiler-api,deploy}"

echo "Uploading profiler-api (C# + acp-v2 sidecar)..."
$SCP -r ../profiler-api/ProfilerApi $EC2_HOST:$REMOTE_DIR/wallet-profiler/profiler-api/
$SCP ../profiler-api/Dockerfile $EC2_HOST:$REMOTE_DIR/wallet-profiler/profiler-api/
$SCP -r ../profiler-api/acp-v2 $EC2_HOST:$REMOTE_DIR/wallet-profiler/profiler-api/

echo "Uploading deploy config..."
$SCP docker-compose.yml $EC2_HOST:$REMOTE_DIR/wallet-profiler/deploy/

$SSH $EC2_HOST "test -f $REMOTE_DIR/wallet-profiler/deploy/.env" || {
  echo "WARNING: No .env on remote. Create $REMOTE_DIR/wallet-profiler/deploy/.env with:"
  echo "  ALCHEMY_API_KEY=..."
  echo "  ETHERSCAN_API_KEY=..."
  echo "  ACP_WALLET_ADDRESS=0x..."
  echo "  ACP_WALLET_ID=..."
  echo "  ACP_SIGNER_PRIVATE_KEY=0x..."
  echo "  ACP_CHAIN=baseSepolia|base"
  echo "  PUBLIC_BASE_URL=https://your.domain"
}

echo "Building and starting containers..."
$SSH $EC2_HOST "cd $REMOTE_DIR/wallet-profiler/deploy && docker compose up -d --build"

echo "=== Deployment complete ==="
echo "Check status: $SSH $EC2_HOST 'cd $REMOTE_DIR/wallet-profiler/deploy && docker compose ps'"
echo "View logs:    $SSH $EC2_HOST 'cd $REMOTE_DIR/wallet-profiler/deploy && docker compose logs -f'"
