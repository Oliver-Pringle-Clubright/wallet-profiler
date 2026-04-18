#!/bin/bash
# Integration smoke test — requires .env with at least ACP_CHAIN set and a running Redis.
# Starts only profiler-api + redis, then hits deliverable endpoints.

set -e
cd "$(dirname "$0")/../../.."    # cwd = wallet-profiler/

docker compose -f deploy/docker-compose.yml up -d profiler-api redis

# Wait for profiler-api
for i in $(seq 1 30); do
  if curl -sf http://localhost:5000/health > /dev/null 2>&1; then
    echo "profiler-api up"
    break
  fi
  sleep 1
done

# Store a deliverable
RESP=$(curl -s -X POST http://localhost:5000/deliverables \
  -H "Content-Type: application/json" \
  -d '{"jobId":"smoke-1","payload":{"hello":"world","n":42}}')
echo "POST /deliverables -> $RESP"

ID=$(echo "$RESP" | python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])')

FETCH=$(curl -s http://localhost:5000/deliverables/$ID)
echo "GET  /deliverables/$ID -> $FETCH"

[ "$FETCH" = '{"hello":"world","n":42}' ] && echo "SMOKE OK" || { echo "SMOKE FAIL"; exit 1; }

echo ""
echo "Done. Leave containers up with: docker compose -f deploy/docker-compose.yml logs -f"
