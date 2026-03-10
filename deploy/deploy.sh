#!/bin/bash
# Deploy WalletProfiler to EC2
# Usage: bash deploy.sh <ec2-host> <key-file>
# Example: bash deploy.sh ec2-user@54.123.45.67 ~/.ssh/my-key.pem

set -e

EC2_HOST="$1"
KEY_FILE="$2"
REMOTE_DIR="/home/ubuntu/walletprofiler"

if [ -z "$EC2_HOST" ] || [ -z "$KEY_FILE" ]; then
  echo "Usage: bash deploy.sh <ec2-user@host> <key-file.pem>"
  echo "Example: bash deploy.sh ubuntu@54.123.45.67 ~/.ssh/walletprofiler-key.pem"
  exit 1
fi

SSH="ssh -i $KEY_FILE -o StrictHostKeyChecking=no"
SCP="scp -i $KEY_FILE -o StrictHostKeyChecking=no"

echo "=== Deploying WalletProfiler to $EC2_HOST ==="

# Create remote directory structure
$SSH $EC2_HOST "mkdir -p $REMOTE_DIR/{wallet-profiler/profiler-api,wallet-profiler/deploy,virtuals-protocol-acp}"

# Sync profiler-api
echo "Uploading profiler-api..."
$SCP -r ../profiler-api/ProfilerApi $EC2_HOST:$REMOTE_DIR/wallet-profiler/profiler-api/
$SCP ../profiler-api/Dockerfile $EC2_HOST:$REMOTE_DIR/wallet-profiler/profiler-api/

# Sync acp-service files
echo "Uploading acp-service..."
$SCP -r ../acp-service $EC2_HOST:$REMOTE_DIR/wallet-profiler/

# Sync deploy files
echo "Uploading deploy config..."
$SCP docker-compose.yml Dockerfile.acp-runtime $EC2_HOST:$REMOTE_DIR/wallet-profiler/deploy/

# Sync virtuals-protocol-acp (excluding node_modules and .git)
echo "Uploading ACP runtime..."
$SSH $EC2_HOST "mkdir -p $REMOTE_DIR/virtuals-protocol-acp/{src,bin}"
$SCP -r ../../virtuals-protocol-acp/package.json $EC2_HOST:$REMOTE_DIR/virtuals-protocol-acp/
$SCP -r ../../virtuals-protocol-acp/package-lock.json $EC2_HOST:$REMOTE_DIR/virtuals-protocol-acp/ 2>/dev/null || true
$SCP -r ../../virtuals-protocol-acp/src $EC2_HOST:$REMOTE_DIR/virtuals-protocol-acp/
$SCP -r ../../virtuals-protocol-acp/bin $EC2_HOST:$REMOTE_DIR/virtuals-protocol-acp/

# Check if .env exists on remote
$SSH $EC2_HOST "test -f $REMOTE_DIR/wallet-profiler/deploy/.env" || {
  echo ""
  echo "WARNING: No .env file found on remote."
  echo "Create $REMOTE_DIR/wallet-profiler/deploy/.env with:"
  echo "  ALCHEMY_API_KEY=your_key"
  echo "  ETHERSCAN_API_KEY=your_key"
  echo "  LITE_AGENT_API_KEY=your_key"
}

# Check if acp-config.json exists on remote
$SSH $EC2_HOST "test -f $REMOTE_DIR/wallet-profiler/deploy/acp-config.json" || {
  echo ""
  echo "WARNING: No acp-config.json found on remote."
  echo "Copy your local config: scp -i $KEY_FILE ../../virtuals-protocol-acp/config.json $EC2_HOST:$REMOTE_DIR/wallet-profiler/deploy/acp-config.json"
}

# Build and start
echo ""
echo "Building and starting containers..."
$SSH $EC2_HOST "cd $REMOTE_DIR/wallet-profiler/deploy && docker compose up -d --build"

echo ""
echo "=== Deployment complete ==="
echo "Check status: $SSH $EC2_HOST 'cd $REMOTE_DIR/wallet-profiler/deploy && docker compose ps'"
echo "View logs:    $SSH $EC2_HOST 'cd $REMOTE_DIR/wallet-profiler/deploy && docker compose logs -f'"
