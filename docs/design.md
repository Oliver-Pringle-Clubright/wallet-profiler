# Wallet Profiler v2.9 — Design Document

## 1. Problem Statement

AI agents operating on-chain need reliable counterparty intelligence before transacting. When an agent receives a payment request or considers a trade via the Agent Commerce Protocol (ACP), it needs to quickly assess:

- Is this wallet legitimate or a potential scammer?
- How much capital does this wallet control?
- How active and established is this wallet?
- What protocols does this wallet interact with?
- Can I trust this counterparty for agent-to-agent commerce?

Currently, no AGDP service provides this. Agents must either skip due diligence or build their own blockchain querying infrastructure. The Wallet Profiler fills this gap as a purchasable on-demand service.

## 2. Target Users

| User Type | Use Case |
|---|---|
| **AI agents on ACP** | Evaluate counterparties before agent-to-agent commerce |
| **Trading agents** | Assess whale wallets, track smart money |
| **Security agents** | Flag suspicious wallets before interacting |
| **Human users** | Quick portfolio overview via AGDP marketplace |
| **Agent orchestrators** | Batch-profile multiple counterparties in one call |

## 3. Architecture

### System Overview

```
                          ┌─────────────────────────┐
                          │   AGDP Marketplace       │
                          │   (agdp.io)              │
                          └──────────┬──────────────┘
                                     │ WebSocket (ACP)
                          ┌──────────▼──────────────┐
                          │   ACP Seller Runtime     │
                          │   (TypeScript)           │
                          │   handlers.ts            │
                          │   + batch support        │
                          └──────────┬──────────────┘
                                     │ HTTP POST /profile
                                     │ HTTP POST /profile/batch
                          ┌──────────▼──────────────┐
                          │   Wallet Profiler API    │
                          │   (C# / ASP.NET)         │
                          │   localhost:5000          │
                          │                          │
                          │   ┌──────────────────┐   │
                          │   │  In-Memory Cache  │   │
                          │   │  (IMemoryCache)   │   │
                          │   └──────────────────┘   │
                          └──────────┬──────────────┘
                                     │
         ┌───────────────────────────┼───────────────────────────┐
         │                           │                           │
┌────────▼────────┐        ┌────────▼────────┐        ┌────────▼────────┐
│  Alchemy RPC     │        │  Etherscan V2   │        │  DeFi Llama    │
│  (Nethereum +    │        │  (REST API)     │        │  (REST API)    │
│   Raw JSON-RPC)  │        │                  │        │                │
│                  │        │ • Tx history     │        │ • ETH price    │
│ • ETH balance    │        │ • Activity data  │        │ • Token prices │
│ • Tx count       │        │                  │        │ • USD values   │
│ • ENS resolve    │        │                  │        │                │
│ • Aave/Compound  │        │                  │        │                │
│ • Token balances │        │                  │        │                │
│   (all ERC-20s)  │        │                  │        │                │
│ • Token metadata │        │                  │        │                │
└─────────────────┘        └─────────────────┘        └────────────────┘
```

### Design Decisions

**1. C# backend with TypeScript ACP proxy (sidecar pattern)**

The ACP runtime requires TypeScript handlers. Rather than rewrite the entire profiler in TypeScript, we use a thin `handlers.ts` proxy that forwards jobs to a C# ASP.NET Minimal API. This gives us:
- Nethereum's mature Ethereum integration (12M+ NuGet downloads)
- C#'s strong typing and async/parallel capabilities
- Clean separation between ACP protocol concerns and business logic

**2. Alchemy `getTokenBalances` for token discovery**

Instead of discovering tokens from Etherscan transfer history (limited to recent 100 events), we use Alchemy's `alchemy_getTokenBalances` endpoint which returns ALL non-zero ERC-20 balances in a single RPC call. Token metadata (symbol, decimals) is fetched via `alchemy_getTokenMetadata` with tier-based caps for response time optimization.

**3. Tiered pricing model**

Three tiers allow buyers to pay only for what they need:
- **Basic (0.0005 ETH):** Balance + tokens (15) + risk + tags — fastest, cheapest
- **Standard (0.001 ETH):** + USD prices + DeFi + activity + portfolio quality + ACP trust — most popular
- **Premium (0.003 ETH):** + natural language summary + full token coverage (50) — for AI agents that need a human-readable interpretation

**4. Spam token detection**

Whale wallets like Vitalik's hold hundreds of airdropped spam/phishing tokens. The profiler uses heuristic detection (non-ASCII symbols, URL patterns, phishing keywords) to flag these. Spam tokens are:
- Sorted to the bottom of the token list
- Excluded from `totalValueUsd` calculation
- Marked with `isSpam: true` for downstream filtering

**5. In-memory caching with tiered TTLs**

Different data types change at different rates:
- Profiles: 5-minute TTL (balances change)
- Prices: 1-minute TTL (prices are volatile)
- ENS: 1-hour TTL (rarely changes)

Cache reduces response time from ~10s to ~3ms for repeat queries and cuts API rate limit pressure.

**6. Natural language summary (premium tier)**

Template-based summary generation produces human-readable wallet descriptions without requiring an LLM API call. Now includes wallet tags, portfolio quality grade, and ACP trust level in the summary narrative.

**7. DeFi Llama for pricing (not CoinGecko)**

CoinGecko's free tier limits token price lookups to 1 contract per call. DeFi Llama's API is free, supports unlimited batch lookups, and returns ETH + all token prices in a single request.

**8. Etherscan V2 API**

Etherscan deprecated their V1 API in favor of V2, which uses a unified endpoint (`api.etherscan.io/v2/api`) with a `chainid` parameter. A single API key works across all chains.

**9. Wallet tags for quick classification (v1.1)**

Rather than requiring consumers to interpret raw numbers, the profiler assigns human-readable tags (whale, defi-user, veteran, dormant, etc.) that agents can use for quick decision-making. Tags are available on all tiers at zero extra cost since they're derived from data already fetched.

**10. Portfolio quality scoring (v1.1)**

Evaluates portfolio composition quality — blue-chip allocation, diversity, stablecoin balance, and spam ratio — into a single A-F grade. This helps trading agents distinguish quality portfolios from meme/spam-heavy wallets.

**11. ACP trust scoring (v1.1)**

Purpose-built for the AGDP ecosystem. Combines wallet age, balance (as collateral signal), transaction depth, ENS ownership, DeFi participation, and portfolio quality into a 0-100 trust score. Enables agents to set trust thresholds before engaging in commerce.

**12. Batch endpoint for efficiency (v1.1)**

The `/profile/batch` endpoint allows profiling up to 50 wallets in a single request, with 5-way parallelism. This is essential for agents that need to evaluate multiple counterparties, scan a list of wallets, or build leaderboards.

**13. Response time optimization (v1.1)**

Token metadata resolution is the main bottleneck. By capping metadata calls per tier (15/30/50) and limiting concurrency to 10 parallel calls, basic tier response time dropped from ~8s to ~3s while maintaining quality for premium users.

## 4. Data Flow

### Profile Request Lifecycle

```
1. Request arrives at POST /profile (or /profile/batch)
2. Check in-memory cache → if HIT, return immediately (~3ms)
3. Resolve ENS if needed (cached separately, 1hr TTL)
4. Launch parallel data fetches (tier-dependent):
   ┌─ All tiers:
   │  a. Alchemy RPC → ETH balance
   │  b. Alchemy RPC → tx count
   │  c. Alchemy RPC → ENS reverse resolve
   │  d. Alchemy → getTokenBalances (all ERC-20s)
   │     └─ Alchemy → getTokenMetadata × N (parallel, capped by tier)
   │
   ├─ Standard + Premium:
   │  e. Alchemy RPC → Aave V3 getUserAccountData
   │  f. Alchemy RPC → Compound V3 balanceOf
   │  g. Etherscan V2 → transaction history
   │
   └─ All: DeFi Llama → ETH + token prices (single call)
5. Spam detection applied to all tokens
6. Risk scoring computed
7. Wallet tags generated (all tiers)
8. [Standard+] Portfolio quality evaluated
9. [Standard+] ACP trust score computed
10. [Premium only] Natural language summary generated
11. Result cached and returned
```

## 5. Revenue Model

| Revenue Source | Mechanism |
|---|---|
| **Per-profile fee** | 0.0005–0.003 ETH per request via ACP escrow |
| **Batch premium** | Full per-profile fee × address count (volume) |
| **AGDP incentive pool** | Top 10 agents by jobs completed earn 30% of weekly pool |
| **Agent token** | Optional: launch a Virtuals Agent Token for capital formation |

## 6. Future Enhancements

- **NFT holdings** — ERC-721/1155 balances and floor prices
- **Multi-chain aggregation** — single request across all supported chains
- **More DeFi protocols** — Uniswap V3 LP, Lido staking, Curve pools
- **Contract labels** — identify known contracts (Uniswap, OpenSea, bridges)
- **Redis caching** — persistent cache for multi-instance deployments
- **Historical snapshots** — portfolio value over time
- **Behavioral scoring** — ML-based risk assessment from transaction patterns
- **ERC-8004 agent verification** — check if wallet is a registered on-chain agent
- **Virtuals Agent Token detection** — identify wallets with launched agent tokens

## 7. v1.2 Features

(See v1.2 changelog for approval scanner, contract labels, and quick trust endpoint.)

## 8. v1.3 New Features

### Token Approval Risk Scanner

Checks ERC-20 `allowance()` on discovered tokens against known DEX routers (Uniswap, SushiSwap, 1inch, OpenSea). Identifies unlimited approvals that represent potential security risks. No equivalent exists on AGDP — this is a unique differentiator for security-focused agents.

### Contract Interaction Labels

Labels the wallet's top 10 most-interacted-with addresses using a database of 45+ known Ethereum contracts (DEXes, bridges, lending protocols, mixers, staking contracts). Turns raw addresses into actionable intelligence — agents can instantly see "this wallet uses Tornado Cash" or "heavy Uniswap trader."

### Quick Trust Endpoint

`GET /trust/{address}` — lightweight pre-transaction trust check returning only score + level in ~500ms. Uses 4 parallel RPC calls (balance, tx count, ENS, token count) without full metadata resolution. Designed for high-volume agents doing thousands of pre-transaction checks at a low price point.

### NFT Holdings & Floor Prices (v1.3)

Uses Alchemy NFT API v3 (`getNFTsForOwner`, `getFloorPrice`) to discover ERC-721/1155 holdings, group by collection, and fetch floor prices from OpenSea/LooksRare. Estimated portfolio value includes NFT floor values. Available on standard+ tiers.

### Whale Movement Monitor (v1.3)

Background webhook subscription system. Agents can subscribe to wallet addresses and receive POST alerts when new transactions are detected. Uses `BackgroundService` with 30-second polling interval and `ConcurrentDictionary` for in-memory subscription storage. Endpoints: `POST /monitor`, `GET /monitor`, `DELETE /monitor/{id}`.

### Cross-Chain Aggregated Profile (v1.3)

`POST /profile/multi-chain` profiles a wallet across multiple chains (Ethereum, Base, Arbitrum) in parallel and returns an aggregated view with per-chain breakdowns. Total portfolio value is summed across chains. ENS is resolved on Ethereum and propagated. Reuses `ProfileOrchestrator` to avoid code duplication.

### ProfileOrchestrator Refactor (v1.3)

## 9. v1.4 New Features

### Token Transfer History Timeline

Fetches the last 200 ERC-20 token transfers from Etherscan V2 and builds a timeline analysis. Groups transfers by month, calculates inbound vs outbound flows, and enriches with USD values when prices are available. Gives agents a clear picture of a wallet's recent token activity.

### Similar Wallet Clustering

Analyzes a wallet's top counterparties to find wallets with similar on-chain behavior. Uses Jaccard similarity on token sets — wallets that hold similar tokens are likely in the same community or strategy cohort. Useful for trading agents doing peer analysis.

### Revoke Recommendation Engine

Analyzes existing token approvals and generates prioritized recommendations for which approvals to revoke. Classifies by risk: unlimited approvals to NFT marketplaces (common phishing vector) are high priority, unlimited to trusted DEX routers are medium, and limited approvals to known protocols are low. Agents can use this for security advisory services.

## 10. v1.5 New Features

### API Key Authentication & Rate Limiting

Optional middleware that secures all endpoints with API key validation via `X-API-Key` header. Each key has a configurable rate limit using a sliding window algorithm. When no keys are configured (development mode), all requests pass through. This enables monetization beyond ACP — direct API access with per-key quotas.

### Redis Cache (L2)

Optional L2 cache layer using StackExchange.Redis. When configured, full wallet profiles are stored in both in-memory (L1) and Redis (L2). On L1 miss, L2 is checked and the result is promoted back to L1. This enables:
- Persistent cache across server restarts
- Shared cache across multiple API instances (horizontal scaling)
- Cache survival during deployments

ENS and price caches remain memory-only for speed.

### Response Time SLA Tracking

Every endpoint request is automatically instrumented with a `RequestTracker` that records latency, success/failure, and SLA compliance. The `GET /sla` endpoint exposes a real-time report with p50/p95/p99 percentiles, breach counts, and compliance percentages. This gives operators and buyers visibility into service reliability.

Extracted ~100 lines of inline profile-building logic from `Program.cs` into a reusable `ProfileOrchestrator` service. Now shared by `/profile`, `/profile/batch`, and `/profile/multi-chain`, eliminating triple code duplication and making future features easier to add.

## 11. v1.6 New Features

### OFAC Sanctions Screening

Screens wallet addresses against a static OFAC SDN list (Tornado Cash contracts, Lazarus Group wallets). Also checks if any top interaction counterparties are sanctioned. Returns risk levels: `clear`, `caution` (interacted with sanctioned address), or `sanctioned` (address itself is sanctioned). Critical for compliance-focused agents.

### Smart Money Analysis

Analyzes wallet trading patterns using 6 factors: portfolio efficiency (value per transaction), token diversity, net flow direction, trading frequency, blue-chip allocation, and DeFi participation. Classifies wallets as: `smart_money`, `active_trader`, `whale`, or `retail`. Returns a profit score (0-100), recent trades, and estimated PnL percentage.

### Token Holder Analysis

`GET /token/{contract}/holders` — analyzes top holders of any ERC-20 token. Uses Etherscan V2 token transfer events to approximate holder balances, then profiles each holder with ETH balance, transaction count, ENS, and a trust score. Returns concentration metrics showing how much of the supply the top holders control.

### Historical Portfolio Snapshots

Automatically records portfolio snapshots when profiles are built (standard+ tiers). Deduplicates by 1-hour windows, caps at 100 snapshots per address. `GET /history/{address}` returns historical data with value change percentages. Enables agents to track portfolio trends over time.

### Recurring Webhook Subscription Plans

Added tiered subscription plans for the whale movement monitor: free (1 subscription, 60s poll), basic (10 subs, 30s poll, 0.01 ETH/month), and premium (100 subs, 15s poll, 0.05 ETH/month). `GET /monitor/plans` returns available plans. Creates a recurring revenue stream.

## 12. v1.7 New Features

### Freemium Tier

`POST /profile` with `tier: "free"` returns a lightweight profile (ETH balance, tx count, token count, risk level, basic tags) at zero cost. Designed as a funnel to convert agents and users to paid tiers. Includes an `upgradeHint` field suggesting the benefits of upgrading.

### Multi-Chain Expansion

Added support for Polygon (137), Optimism (10), Avalanche (43114), and BNB Chain (56) via Alchemy RPC. Each chain has its own native token symbol and DeFi Llama price key. `GET /chains` endpoint lists all supported chains with chain IDs and native tokens. Total supported chains: 7.

### MEV Detection

Analyzes a wallet's transaction history for MEV exposure — sandwich attacks, frontrunning, and backrunning. Checks transactions against a database of known MEV bot addresses. Returns risk level, incident count, and estimated losses. Available on standard+ tiers.

### On-Chain Reputation Badge

`GET /reputation/{address}` generates ERC-721 compatible metadata for a soulbound reputation NFT. Combines trust score, classification (whale, trader, defi_native, hodler, newcomer), wallet age, tags, and portfolio value into a badge with a base64-encoded JSON metadata URI. Enables on-chain identity verification.

### Bulk Enterprise Pricing

`GET /pricing/enterprise` returns three enterprise plans: starter (0.5 ETH/month, 1K profiles), growth (2 ETH/month, 5K profiles), and enterprise (10 ETH/month, 50K profiles). Each plan includes different rate limits, support levels, and features. Creates a high-value recurring revenue channel.

## 13. v1.8 New Features

### Social Identity Correlation

`GET /identity/{address}` analyzes a wallet's social identity signals by combining ENS ownership, ENS text records, governance participation, DAO membership, wallet maturity, interaction diversity, NFT ownership, and activity tags. Returns an identity score (0-100) and classification: `anonymous`, `pseudonymous`, or `identified`. Helps agents assess the social credibility of counterparties.

### Agent Referral Program

Two endpoints for a referral system: `POST /referral/register` generates a unique referral code for an agent, and `GET /referral/{address}` returns referral statistics. Agents earn 10% commission on profile fees from referred agents. Creates a network effect — agents are incentivized to promote the service, driving organic growth.

### Wallet Comparison

`POST /compare` takes 2-10 wallet addresses and builds profiles for each, then generates a side-by-side comparison with: value rankings, common tokens, risk comparison, DeFi participation comparison, smart money classification, trust score averages, and unique insights. Essential for agents doing competitive analysis, portfolio benchmarking, or counterparty evaluation.

## 14. v2.0 — Production Deployment & ACP Marketplace

### ACP Marketplace Integration

The WalletProfiler agent (ID 19462) is live on the ACP marketplace at app.virtuals.io. Registered as a seller with the `walletprofiler` offering at $0.01 USDC per job. The ACP seller runtime connects via WebSocket to `acpx.virtuals.io`, listens for incoming job requests, validates requirements (address format, chain, tier), and proxies execution to the C# Profiler API.

### AWS EC2 Deployment

Production runs on AWS EC2 using Docker Compose with two containers:

- **profiler-api**: .NET 10 ASP.NET Minimal API container (`mcr.microsoft.com/dotnet/aspnet:10.0`), exposed on port 5000, with health check and auto-restart.
- **acp-runtime**: Node.js 22 container running the ACP seller runtime (`src/seller/runtime/seller.ts`), maintains persistent WebSocket connection to ACP, forwards jobs to the profiler-api container via Docker internal networking.

Configuration is managed through environment variables (`.env` file for API keys) and a mounted `acp-config.json` for agent credentials. The deployment scripts handle tarball creation, SCP upload, and Docker Compose orchestration.

### Deployment Files

| File | Purpose |
|---|---|
| `deploy/docker-compose.yml` | Two-service orchestration with health checks |
| `deploy/Dockerfile.acp-runtime` | ACP seller runtime container build |
| `deploy/.env.example` | Template for API keys and ACP agent credentials |
| `deploy/ec2-setup.sh` | EC2 instance bootstrap (Docker installation) |
| `deploy/deploy.sh` | Local-to-EC2 deployment automation |
| `profiler-api/Dockerfile` | C# API multi-stage build |

### ACP Offering Schema (v2.0)

Updated from legacy `fee`/`requirements` format to ACP CLI v0.4.0 schema:

| Field | Value |
|---|---|
| `name` | `walletprofiler` |
| `jobFee` | `0.01` (USDC) |
| `jobFeeType` | `fixed` |
| `requiredFunds` | `false` |
| `slaMinutes` | `5` |
| `deliverable` | `string` (JSON-serialized profile) |

### Etherscan V2 Unified API

All chain queries use the Etherscan V2 unified endpoint with `chainid` parameter. A single Etherscan API key covers all 7 supported chains — no separate Basescan or Arbiscan keys required.

## 15. v2.2 — Discovery Offering (walletstatus)

Added ultra-fast `walletstatus` offering as the cheapest entry point ($0.01). `GET /status/{address}` returns address, chain, ETH balance, transaction count, and contract detection (isContract) in under 3 seconds. No scoring, no tokens — just raw chain data. Designed for agents doing high-volume pre-filtering before calling higher-tier offerings.

## 16. v2.3 — Cross-Chain Deep Analysis & NFT Differentiation

Upgraded `deepanalysis` offering to use cross-chain aggregation by default. When chain is omitted or set to "all", the handler calls `/profile/multi-chain` to profile the wallet across all 7 EVM chains in a single request. This provides a complete cross-chain portfolio view including total value across chains, active chain detection, and per-chain breakdowns — all at premium tier with AI summary. Updated walletprofiler offering description to emphasize NFT portfolio with floor prices and DeFi positions as key differentiators.

## 17. v2.4 — Response Time & Search Optimization

Updated all 6 ACP offering descriptions with:
- **Response time claims**: walletstatus (~200ms), quickcheck (~500ms), whalealerts (~3s), walletprofiler (~5s), tokenholders (~8s), deepanalysis (~15s)
- **Searchable keywords**: "due diligence", "counterparty risk", "AML compliance", "fraud detection", "smart money tracking", "market intelligence"
- **Chain enumeration**: all descriptions explicitly list supported chains for discovery

## 18. v2.5 — Revenue Priority: Enhanced deepanalysis

Deepanalysis is the highest-margin offering ($0.10/job). Enhanced with automatic wallet comparison when multiple addresses are provided — the batch response now includes a `comparison` object with common tokens, leader identification, and unique insights alongside individual profiles. This makes deepanalysis the go-to offering for comprehensive multi-wallet intelligence.

### ACP Offering Lineup (v2.9)

| Offering | Fee | Response | Use Case |
|---|---|---|---|
| walletstatus | $0.01 | ~200ms | Pre-filtering, address validation (8 chains incl. Solana) |
| quickcheck | $0.01 | ~500ms | Trust scoring, counterparty check (8 chains incl. Solana) |
| virtualsintel | $0.01 | ~2s | Virtuals ecosystem intelligence, AI agent token tracking |
| riskscore | $0.02 | ~3s | Risk assessment, AML, fraud detection (7 EVM chains) |
| whalealerts | $0.02 | ~3s | Exchange flow, whale tracking |
| walletprofiler | $0.03 | ~5s | Full profiling, batch analysis |
| tokenholders | $0.05 | ~8s | Token concentration, rug pull risk |
| deepanalysis | $0.10 | ~15s | Cross-chain, comparison, AI summary |

## 21. v2.8 — Virtuals Ecosystem Intelligence

Added `virtualsintel` offering at $0.01 — serves the Virtuals community directly. `GET /virtuals/ecosystem` fetches live data from CoinGecko (free API) for VIRTUAL token and top AI agent tokens ($AIXBT, $GAME, $LUNA, $VADER, $SEKOIA, $AIMONICA, $MISATO, $CONVO, $BIO). Returns prices, market caps, 24h volume, price changes, ecosystem totals, health sentiment, and natural language summary. Cached for 5 minutes to respect CoinGecko rate limits. Mirrors ChainScope's `virtuals_intel` offering — a key differentiator for marketplace relevance.

## 20. v2.7 — Risk Score Offering

Added standalone `riskscore` offering at $0.02 — competitive with ChainScope's `risk_score` ($0.05). `GET /risk/{address}` builds a basic profile and extracts risk-specific data: risk score (0-100), risk level, verdict (SAFE/CAUTION/WARNING/DANGER), risk flags, OFAC sanctions screening, token approval counts, and wallet classification tags. Fills a gap in the offering lineup between quickcheck ($0.01 trust score) and walletprofiler ($0.03 full profile).

## 22. v2.9 — Marketplace Optimization & Production Deployment

### Offering Description Refresh

All 8 ACP offerings were deleted and re-registered on the marketplace to push updated descriptions. Previous offerings created before v2.4 still showed stale descriptions without response times, keyword optimization, or Solana support. The refresh ensures marketplace listings match the local `offering.json` files:

- **walletstatus**: Now correctly shows "8 chains" and includes Solana in the chain enum
- **quickcheck**: Updated with "8 chains" and Solana support
- **deepanalysis**: Cross-chain aggregation description with "all 7 EVM chains"
- **tokenholders**: Response time and keyword-optimized description
- **whalealerts**: Response time and keyword-optimized description
- **walletprofiler**: Full feature list with response time claims

### Agent Profile Update

Updated the agent profile description via `acp profile update` to reflect current capabilities: 8 offerings across 8 chains including Solana, with comprehensive feature enumeration for marketplace discovery.

### CoinGecko API Fix

Added `User-Agent` header to `VirtualsIntelService` HTTP requests. CoinGecko's free API returns 403 Forbidden without a User-Agent header when called from server environments. Fix ensures the `virtualsintel` offering works reliably in production.

### EC2 Redeployment

Full rebuild of both Docker containers (profiler-api and acp-runtime) on EC2 with all v2.6-v2.9 changes. All endpoints verified:
- `/health` — healthy
- `/status/{address}` — working (including Solana)
- `/trust/{address}` — working
- `/risk/{address}` — working
- `/virtuals/ecosystem` — working (CoinGecko fix applied)

## 19. v2.6 — Solana Support

Added Solana as the 8th supported chain. Solana uses a separate `SolanaService` with JSON-RPC calls (`getBalance`, `getSignaturesForAddress`, `getTokenAccountsByOwner`, `getAccountInfo`) since it's non-EVM. Supported on `walletstatus` and `quickcheck` offerings — returns SOL balance, transaction count, SPL token count, and trust scoring. Uses Alchemy Solana RPC when configured, falls back to public `api.mainnet-beta.solana.com`. Solana addresses are base58-encoded (32-44 chars), automatically detected by validators.
