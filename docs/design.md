# Wallet Profiler — Design Document

## 1. Problem Statement

AI agents operating on-chain need reliable counterparty intelligence before transacting. When an agent receives a payment request or considers a trade via the Agent Commerce Protocol (ACP), it needs to quickly assess:

- Is this wallet legitimate or a potential scammer?
- How much capital does this wallet control?
- How active and established is this wallet?
- What protocols does this wallet interact with?

Currently, no AGDP service provides this. Agents must either skip due diligence or build their own blockchain querying infrastructure. The Wallet Profiler fills this gap as a purchasable on-demand service.

## 2. Target Users

| User Type | Use Case |
|---|---|
| **AI agents on ACP** | Evaluate counterparties before agent-to-agent commerce |
| **Trading agents** | Assess whale wallets, track smart money |
| **Security agents** | Flag suspicious wallets before interacting |
| **Human users** | Quick portfolio overview via AGDP marketplace |

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
                          └──────────┬──────────────┘
                                     │ HTTP POST /profile
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

Instead of discovering tokens from Etherscan transfer history (limited to recent 100 events), we use Alchemy's `alchemy_getTokenBalances` endpoint which returns ALL non-zero ERC-20 balances in a single RPC call. This catches tokens received long ago that would be missed by transfer history scanning. Token metadata (symbol, decimals) is fetched via `alchemy_getTokenMetadata`.

**3. Tiered pricing model**

Three tiers allow buyers to pay only for what they need:
- **Basic (0.0005 ETH):** Balance + tokens + risk — fastest, cheapest
- **Standard (0.001 ETH):** + USD prices + DeFi + activity — most popular
- **Premium (0.003 ETH):** + natural language summary — for AI agents that need a human-readable interpretation

This maximizes revenue by serving both quick-check and deep-analysis use cases.

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

Template-based summary generation produces human-readable wallet descriptions without requiring an LLM API call. This keeps latency low and costs zero. The summary classifies wallet size (micro/small/mid/high-value/whale), describes portfolio breakdown, flags spam, and interprets the risk score.

**7. DeFi Llama for pricing (not CoinGecko)**

CoinGecko's free tier limits token price lookups to 1 contract per call. DeFi Llama's API is free, supports unlimited batch lookups, and returns ETH + all token prices in a single request.

**8. Etherscan V2 API**

Etherscan deprecated their V1 API in favor of V2, which uses a unified endpoint (`api.etherscan.io/v2/api`) with a `chainid` parameter. A single API key works across all chains.

## 4. Data Flow

### Profile Request Lifecycle

```
1. Request arrives at POST /profile
2. Check in-memory cache → if HIT, return immediately (~3ms)
3. Resolve ENS if needed (cached separately, 1hr TTL)
4. Launch parallel data fetches (tier-dependent):
   ┌─ All tiers:
   │  a. Alchemy RPC → ETH balance
   │  b. Alchemy RPC → tx count
   │  c. Alchemy RPC → ENS reverse resolve
   │  d. Alchemy → getTokenBalances (all ERC-20s)
   │     └─ Alchemy → getTokenMetadata × N (parallel)
   │
   ├─ Standard + Premium:
   │  e. Alchemy RPC → Aave V3 getUserAccountData
   │  f. Alchemy RPC → Compound V3 balanceOf
   │  g. Etherscan V2 → transaction history
   │
   └─ All: DeFi Llama → ETH + token prices (single call)
5. Spam detection applied to all tokens
6. Risk scoring computed
7. [Premium only] Natural language summary generated
8. Result cached and returned
```

## 5. Revenue Model

| Revenue Source | Mechanism |
|---|---|
| **Per-profile fee** | 0.0005–0.003 ETH per request via ACP escrow |
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
