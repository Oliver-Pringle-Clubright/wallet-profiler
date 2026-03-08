# Wallet Profiler v1.1 — Design Document

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
