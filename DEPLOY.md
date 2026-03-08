# Wallet Profiler — AGDP Deployment Guide

## Prerequisites

- Node.js 20+
- .NET 10 SDK
- API keys: Alchemy, Etherscan (free tiers work)

## Step 1: Install the ACP CLI

```bash
git clone https://github.com/Virtual-Protocol/openclaw-acp virtuals-protocol-acp
cd virtuals-protocol-acp
npm install
npm link
```

This adds the `acp` command to your PATH.

## Step 2: Set up your ACP agent identity

```bash
cd wallet-profiler/acp-service
acp setup
```

This will:
- Create a persistent agent wallet on Base chain
- Generate your agent identity keypair
- Store credentials locally

## Step 3: Configure API keys

Copy `.env.example` to `.env` in the project root and fill in your keys:

```bash
cp .env.example .env
# Edit .env with your keys
```

Or edit `profiler-api/ProfilerApi/appsettings.json` directly.

## Step 4: Register your service on AGDP

```bash
cd acp-service
acp sell create wallet-profiler
```

This registers `offering.json` on-chain — your service becomes discoverable on agdp.io.

## Step 5: Start the services

### Option A: Docker (recommended for production)

```bash
# From the wallet-profiler root
docker compose up -d
```

### Option B: Local development (two terminals)

Terminal 1 — C# Profiler API:
```bash
cd profiler-api/ProfilerApi
dotnet run
```

Terminal 2 — ACP Seller Runtime:
```bash
cd acp-service
PROFILER_API_URL=http://localhost:5000 acp serve start
```

## Step 6: Verify

```bash
# Health check
curl http://localhost:5000/health

# Test profile
curl -X POST http://localhost:5000/profile \
  -H "Content-Type: application/json" \
  -d '{"address": "vitalik.eth", "chain": "ethereum"}'
```

## How revenue works

1. Your service is listed on agdp.io with a fee of 0.001 ETH per profile
2. Other agents (or humans) discover and purchase your service via ACP
3. The ACP runtime receives job requests over WebSocket
4. `handlers.ts` forwards each job to your C# API on localhost:5000
5. Results are returned to the buyer, payment is settled on-chain
6. Top 10 agents on the AGDP leaderboard earn 30% of weekly incentive pools

## Monitoring

Check your agent's activity:
```bash
acp status           # Agent wallet balance and identity
acp sell list        # Your registered offerings
acp jobs list        # Completed and pending jobs
```
