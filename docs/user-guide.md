# Wallet Profiler v3.0 — User Guide

## Overview

The Wallet Profiler is an AGDP (Agent GDP) service agent that provides comprehensive on-chain wallet analysis for Ethereum-compatible blockchains. Given a wallet address or ENS name, it returns a detailed profile including token holdings with live USD valuations, DeFi positions, transaction history, risk assessment, spam detection, wallet tags, portfolio quality grading, ACP trust scoring, token approval risk scanning, and contract interaction labeling.

The service offers three pricing tiers (basic, standard, premium) and runs on the Agent Commerce Protocol (ACP) marketplace.

## Supported Chains

| Chain | Chain ID | Native Token | Status |
|---|---|---|---|
| Ethereum Mainnet | 1 | ETH | Fully supported |
| Base | 8453 | ETH | Supported |
| Arbitrum One | 42161 | ETH | Supported |
| Polygon | 137 | MATIC | Supported (v1.7) |
| Optimism | 10 | ETH | Supported (v1.7) |
| Avalanche | 43114 | AVAX | Supported (v1.7) |
| BNB Chain | 56 | BNB | Supported (v1.7) |
| Solana | — | SOL | Supported (v2.6) — walletstatus & quickcheck |

## Service Tiers

| Feature | Basic (0.0005 ETH) | Standard (0.001 ETH) | Premium (0.003 ETH) |
|---|---|---|---|
| ETH balance | Yes | Yes | Yes |
| ERC-20 tokens with balances | Up to 15 | Up to 30 | Up to 50 |
| ENS resolution | Yes | Yes | Yes |
| Risk score | Yes | Yes | Yes |
| Spam token detection | Yes | Yes | Yes |
| Wallet tags | Yes | Yes | Yes |
| USD prices for tokens | — | Yes | Yes |
| Total portfolio value | — | Yes | Yes |
| DeFi positions (9 protocols) | — | Yes | Yes |
| Transaction activity history | — | Yes | Yes |
| Portfolio quality score | — | Yes | Yes |
| ACP trust score | — | Yes | Yes |
| Token approval risk scan | — | Yes | Yes |
| Contract interaction labels | — | Yes | Yes |
| NFT holdings & floor prices | — | Yes | Yes |
| Token transfer history timeline | — | Yes | Yes |
| Similar wallet clustering | — | Yes | Yes |
| Revoke recommendation engine | — | Yes | Yes |
| OFAC sanctions screening | — | Yes | Yes |
| Smart money analysis | — | Yes | Yes |
| Portfolio snapshots & history | — | Yes | Yes |
| Natural language summary | — | — | Yes |

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- [Node.js 20+](https://nodejs.org/) (for ACP runtime)
- An [Alchemy](https://www.alchemy.com/) API key (free tier works)
- An [Etherscan](https://etherscan.io/apis) API key (free tier works, used for activity data)

### Installation

1. Clone the repository:
   ```bash
   git clone <repo-url>
   cd wallet-profiler
   ```

2. Configure API keys in `profiler-api/ProfilerApi/appsettings.Development.json`:
   ```json
   {
     "Alchemy": { "ApiKey": "your_alchemy_key" },
     "Etherscan": { "ApiKey": "your_etherscan_key" }
   }
   ```

   > **Note:** Never commit real API keys. Use `appsettings.Development.json` (gitignored) for local development. The checked-in `appsettings.json` contains only placeholder values.

3. Build and run the C# API:
   ```bash
   cd profiler-api/ProfilerApi
   ASPNETCORE_ENVIRONMENT=Development dotnet run
   ```

4. The API is now available at `http://localhost:5000`.

### Single Profile Request

**Endpoint:** `POST /profile`

**Request body:**
```json
{
  "address": "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045",
  "chain": "ethereum",
  "tier": "premium"
}
```

**Using an ENS name:**
```json
{
  "address": "vitalik.eth",
  "tier": "standard"
}
```

Both `chain` (defaults to `"ethereum"`) and `tier` (defaults to `"standard"`) are optional.

### Wallet Status (Discovery)

**Endpoint:** `GET /status/{address}?chain=ethereum`

Ultra-fast wallet status check (~200ms). Returns raw chain data without scoring or token analysis. Ideal for high-volume pre-filtering.

```bash
curl http://localhost:5000/status/0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045
```

**Response:**
```json
{
  "address": "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045",
  "chain": "ethereum",
  "ethBalance": 32.13,
  "transactionCount": 1647,
  "isContract": false,
  "checkedAt": "2026-03-10T12:00:00Z"
}
```

### Quick Trust Check

**Endpoint:** `GET /trust/{address}`

Lightweight pre-transaction trust check (~500ms). Returns trust score without full profile analysis. Supports ENS names.

```bash
curl http://localhost:5000/trust/vitalik.eth
```

**Response:**
```json
{
  "address": "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045",
  "ensName": "vitalik.eth",
  "ethBalance": 32.13,
  "transactionCount": 1647,
  "tokenCount": 100,
  "trustScore": 100,
  "trustLevel": "high",
  "factors": [
    "Deep history 1647 txs (+35)",
    "Large ETH balance 32.13 (+25)",
    "ENS: vitalik.eth (+20)",
    "Diverse portfolio 100 tokens (+20)"
  ]
}
```

No tier required — always free/flat fee (0.0001 ETH suggested). Designed for high-volume pre-transaction checks.

### Batch Profile Request

**Endpoint:** `POST /profile/batch`

Profile up to 50 wallets in a single request. Addresses are processed in parallel (5 concurrent).

**Request body:**
```json
{
  "addresses": [
    "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045",
    "0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B",
    "vitalik.eth"
  ],
  "chain": "ethereum",
  "tier": "standard"
}
```

**Response:**
```json
{
  "total": 3,
  "succeeded": 3,
  "failed": 0,
  "elapsedMs": 1321,
  "results": [
    { "address": "0xd8d...045", "profile": { ... }, "error": null },
    { "address": "0xAb5...9B", "profile": { ... }, "error": null },
    { "address": "vitalik.eth", "profile": { ... }, "error": null }
  ]
}
```

### Cross-Chain Aggregated Profile (v1.3)

**Endpoint:** `POST /profile/multi-chain`

Profile a wallet across multiple chains in one request. Results are aggregated with per-chain breakdowns.

**Request body:**
```json
{
  "address": "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045",
  "chains": ["ethereum", "base", "arbitrum"],
  "tier": "standard"
}
```

**Response:**
```json
{
  "address": "0xd8d...045",
  "ensName": "vitalik.eth",
  "totalValueUsd": 65432.10,
  "chainProfiles": {
    "ethereum": { "ethBalance": 32.13, "topTokens": [...], ... },
    "base": { "ethBalance": 0.5, "topTokens": [...], ... },
    "arbitrum": { "ethBalance": 0.01, "topTokens": [...], ... }
  },
  "activeChains": ["ethereum", "base"],
  "profiledAt": "2026-03-08T16:30:00Z"
}
```

### Whale Movement Monitor (v1.3)

Subscribe to receive webhook alerts when a wallet has new transactions.

**Subscribe:** `POST /monitor`
```json
{
  "address": "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045",
  "chain": "ethereum",
  "webhookUrl": "https://your-agent.com/webhook",
  "thresholdEth": 10
}
```

**Check status:** `GET /monitor`

**Unsubscribe:** `DELETE /monitor/{subscriptionId}`

The monitor checks subscribed wallets every 30 seconds. When new transactions are detected, it sends a POST to your webhook URL with alert details.

### View Available Tiers

```bash
curl http://localhost:5000/tiers
```

### Example Response (Premium Tier)

```json
{
  "tier": "premium",
  "address": "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045",
  "ensName": "vitalik.eth",
  "ethBalance": 32.13,
  "ethPriceUsd": 1944.83,
  "ethValueUsd": 62489.91,
  "totalValueUsd": 62669.22,
  "transactionCount": 1647,
  "topTokens": [
    {
      "symbol": "CULT",
      "contractAddress": "0x000...eca4",
      "balance": 1150000,
      "decimals": 18,
      "priceUsd": 0.000156,
      "valueUsd": 179.31,
      "isSpam": false
    }
  ],
  "deFiPositions": [],
  "risk": {
    "score": 10,
    "level": "low",
    "flags": ["Inactive for 6+ months"]
  },
  "activity": {
    "firstTransaction": "2015-09-28T08:24:43Z",
    "lastTransaction": "2022-03-23T01:56:30Z",
    "daysActive": 200,
    "uniqueInteractions": 217
  },
  "tags": ["mid-tier", "ens-holder", "veteran", "og", "dormant", "power-user", "well-connected", "diversified"],
  "portfolioQuality": {
    "bluechipPct": 99.5,
    "stablecoinPct": 0,
    "spamPct": 0,
    "diversityScore": 15,
    "qualityScore": 72,
    "grade": "B"
  },
  "acpTrust": {
    "score": 90,
    "level": "high",
    "factors": [
      "Wallet age 3+ years (+25)",
      "Significant ETH balance >$10K (+20)",
      "Deep transaction history 500+ txs (+15)",
      "ENS name registered (+10)",
      "High portfolio quality (+10)",
      "Highly connected wallet 50+ interactions (+10)"
    ]
  },
  "summary": "This is a mid-range wallet (active since 2015 (10+ years)), holding $62.7K total. ...",
  "profiledAt": "2026-03-08T15:33:31Z"
}
```

### Response Fields

| Field | Type | Tier | Description |
|---|---|---|---|
| `tier` | string | All | Which tier was used |
| `address` | string | All | Resolved wallet address |
| `ensName` | string? | All | ENS name if one exists |
| `ethBalance` | decimal | All | Native ETH balance |
| `ethPriceUsd` | decimal? | Std+ | Current ETH price in USD |
| `ethValueUsd` | decimal? | Std+ | ETH holdings value in USD |
| `totalValueUsd` | decimal? | Std+ | Total portfolio value (ETH + priced tokens) |
| `transactionCount` | int | All | Total outbound transaction count |
| `topTokens` | array | All | ERC-20 tokens (up to 15/30/50 by tier) |
| `topTokens[].isSpam` | bool | All | Whether the token is flagged as spam |
| `deFiPositions` | array | Std+ | Active DeFi positions |
| `risk` | object | All | Risk assessment with score and flags |
| `activity` | object? | Std+ | Transaction history summary |
| `tags` | string[] | All | Classification tags (whale, defi-user, veteran, etc.) |
| `portfolioQuality` | object? | Std+ | Portfolio quality grade and metrics |
| `acpTrust` | object? | Std+ | ACP trust score for agent-to-agent commerce |
| `approvalRisk` | object? | Std+ | Token approval risk scan results |
| `topInteractions` | array | Std+ | Top 10 interacted-with contracts with labels |
| `nfts` | object? | Std+ | NFT holdings summary with floor prices |
| `transferHistory` | object? | Std+ | Token transfer timeline with flow analysis |
| `similarWallets` | object? | Std+ | Similar wallets by token overlap |
| `revokeAdvice` | object? | Std+ | Approval revocation recommendations |
| `sanctions` | object? | Std+ | OFAC sanctions screening results |
| `smartMoney` | object? | Std+ | Smart money classification and analysis |
| `mevExposure` | object? | Std+ | MEV exposure detection results |
| `summary` | string? | Premium | Natural language wallet summary |

### Wallet Tags

Wallets are automatically classified with tags based on their on-chain activity:

| Tag | Criteria |
|---|---|
| `whale` | Portfolio > $1M |
| `high-roller` | Portfolio > $100K |
| `mid-tier` | Portfolio > $10K |
| `ens-holder` | Has an ENS name |
| `defi-user` | Active DeFi positions |
| `multi-protocol` | Uses 2+ DeFi protocols |
| `veteran` | Wallet age > 1 year |
| `og` | Wallet age > 5 years |
| `fresh-wallet` | Wallet age < 30 days |
| `dormant` | Inactive > 6 months |
| `power-user` | 1000+ txs, 100+ active days |
| `active-trader` | 100+ transactions |
| `well-connected` | 50+ unique interactions |
| `diversified` | 20+ non-spam tokens |
| `concentrated` | 3 or fewer tokens |
| `stablecoin-heavy` | >50% value in stablecoins |
| `hodler` | Old wallet, infrequent transactions |
| `spam-magnet` | 10+ spam tokens received |

### Portfolio Quality Score

Available on standard and premium tiers. Evaluates portfolio composition:

| Field | Description |
|---|---|
| `bluechipPct` | Percentage of value in ETH + blue-chip tokens (WBTC, LINK, UNI, AAVE, etc.) |
| `stablecoinPct` | Percentage of value in stablecoins (USDC, USDT, DAI, etc.) |
| `spamPct` | Percentage of tokens flagged as spam |
| `diversityScore` | Token diversity (0-100) |
| `qualityScore` | Composite quality score (0-100) |
| `grade` | Letter grade: A (80+), B (60+), C (40+), D (20+), F (<20) |

### ACP Trust Score

Available on standard and premium tiers. Evaluates counterparty trustworthiness for agent-to-agent commerce using 9 positive and 5 negative factors:

| Score Range | Level | Meaning |
|---|---|---|
| 80–100 | High | Highly trustworthy — established, well-funded wallet |
| 60–79 | Good | Generally trustworthy — solid history and assets |
| 40–59 | Moderate | Some trust signals present — exercise caution |
| 20–39 | Low | Limited trust signals — limited history or assets |
| 0–19 | Untrusted | Insufficient trust signals — new, empty, or flagged wallet |

**Positive factors:** wallet age, ETH balance, transaction depth, ENS ownership, DeFi participation, portfolio quality, interaction diversity, NFT holdings, transfer history depth.

**Negative factors:** sanctioned address/interactions, risky token approvals, MEV exposure, critical risk level, dormancy.

### Token Approval Risk Scan

Available on standard and premium tiers. Checks ERC-20 `allowance()` on top 10 non-spam tokens against known DEX routers and protocols:

| Field | Description |
|---|---|
| `totalApprovals` | Number of active non-zero approvals found |
| `unlimitedApprovals` | Approvals with effectively unlimited allowance |
| `highRiskApprovals` | Approvals to unverified or risky contracts |
| `riskLevel` | Overall risk: `safe`, `low`, `caution`, or `danger` |
| `approvals[]` | Individual approval details (token, spender, label, risk) |

**Checked spenders:** Uniswap V2/V3/Universal Router/Permit2, SushiSwap, 1inch V6, 0x Exchange, CoW Protocol, Balancer V2, OpenSea Seaport.

### Contract Interaction Labels

Available on standard and premium tiers. Labels the wallet's top 10 most-interacted-with addresses using a database of 130+ known Ethereum contracts:

| Category | Examples |
|---|---|
| `dex` | Uniswap V2/V3/Permit2, SushiSwap, 1inch V6, Curve, Balancer V2, ParaSwap V6, CoW Protocol |
| `nft` | OpenSea Seaport 1.6, Blur Pool/Blend, LooksRare V2, Sudoswap |
| `lending` | Aave V2/V3, Compound V2/V3, Prisma mkUSD |
| `bridge` | Across V2, Hop, LayerZero, CCIP Router, Base Portal, Polygon zkEVM |
| `staking` | Lido stETH/wstETH, EtherFi eETH/weETH, Rocket Pool, Mantle mETH, Renzo ezETH, Swell rswETH, Kelp rsETH, Frax sfrxETH |
| `restaking` | EigenLayer Strategy Manager, Delegation Manager, stETH/cbETH/rETH strategies |
| `yield` | Ethena USDe/sUSDe, MakerDAO sDAI/DSR, Convex, Aura, Yearn |
| `exchange` | Binance (3 hot wallets), Coinbase (4 wallets), Kraken, Gemini |
| `mixer` | Tornado Cash (flagged as security concern) |
| `governance` | ENS Governor, Uniswap Governor, Aave Governor V2, MakerDAO, Gitcoin Grants, Nouns DAO |
| `token` | LINK, SNX, LDO, ARB, OP, RPL, PEPE, SHIB, ONDO, EIGEN, ENA, WLD, FET |
| `identity` | ENS Registrar |

Unlabeled addresses show `label: null` — these are typically personal wallets or unlisted contracts.

### NFT Holdings (v1.3)

Available on standard and premium tiers. Fetches NFT holdings via Alchemy NFT API v3 and floor prices from OpenSea/LooksRare.

| Field | Description |
|---|---|
| `nfts.totalCount` | Total NFTs owned |
| `nfts.collectionCount` | Number of distinct collections |
| `nfts.estimatedValueEth` | Estimated total floor value in ETH |
| `nfts.estimatedValueUsd` | Estimated total floor value in USD |
| `nfts.topCollections[]` | Top 10 collections by floor price/count |
| `nfts.topCollections[].floorPriceEth` | Current floor price in ETH |

### Token Transfer History (v1.4, enhanced v3.0)

Available on standard and premium tiers. Fetches both ERC-20 token transfers and native ETH/MATIC/AVAX/BNB transfers in parallel, merged into a unified timeline.

| Field | Description |
|---|---|
| `transferHistory.totalTransfers` | Total transfers analyzed |
| `transferHistory.inboundCount` | Number of incoming transfers |
| `transferHistory.outboundCount` | Number of outgoing transfers |
| `transferHistory.netFlowUsd` | Net USD flow (positive = inflow) |
| `transferHistory.recentTransfers[]` | Last 25 transfers with details (ERC-20 + native) |
| `transferHistory.timeline[]` | Monthly breakdown (up to 12 months) |

### Similar Wallet Clustering (v1.4, enhanced v3.0)

Available on standard and premium tiers. Finds wallets with similar token holdings, DeFi protocol usage, and interaction patterns by analyzing top 15 counterparties.

| Field | Description |
|---|---|
| `similarWallets.candidatesAnalyzed` | Number of counterparties analyzed |
| `similarWallets.matches[]` | Top 5 similar wallets |
| `similarWallets.matches[].similarityScore` | Combined similarity score (0-100): Jaccard token overlap + interaction frequency + shared DeFi protocols |
| `similarWallets.matches[].commonTokens` | Shared token symbols (up to 8) |
| `similarWallets.matches[].commonInteractions` | Shared DeFi protocol labels |
| `similarWallets.matches[].sharedProtocols` | Count of shared DeFi protocols |

### Revoke Recommendation Engine (v1.4, enhanced v3.0)

Available on standard and premium tiers. Analyzes token approvals against 18 known exploited/deprecated contracts and recommends which to revoke.

| Field | Description |
|---|---|
| `revokeAdvice.totalRecommendations` | Number of revocation recommendations |
| `revokeAdvice.highPriority` | Count of critical + high-priority revocations |
| `revokeAdvice.overallUrgency` | `none`, `low`, `medium`, `high`, or `critical` |
| `revokeAdvice.recommendations[]` | Individual recommendations with priority (`critical`, `high`, `medium`, `low`) and reason |

**Critical priority** is assigned to approvals on known exploited contracts (Multichain, Euler, Ronin Bridge, Wormhole, Nomad, BadgerDAO, etc.).

### OFAC Sanctions Screening (v1.6)

Available on standard and premium tiers. Screens the wallet address and its top interaction counterparties against OFAC SDN sanctioned addresses.

| Field | Description |
|---|---|
| `sanctions.isSanctioned` | Whether the wallet itself is sanctioned |
| `sanctions.hasSanctionedInteractions` | Whether any counterparties are sanctioned |
| `sanctions.riskLevel` | `clear`, `caution`, or `sanctioned` |
| `sanctions.flags` | List of specific flags/concerns |

### Smart Money Analysis (v1.6)

Available on standard and premium tiers. Analyzes trading patterns and classifies the wallet.

| Field | Description |
|---|---|
| `smartMoney.profitScore` | Trading proficiency score (0-100) |
| `smartMoney.classification` | `smart_money`, `active_trader`, `whale`, or `retail` |
| `smartMoney.recentTrades` | Last 10 trades with token, action, amount |
| `smartMoney.estimatedPnlPct` | Estimated profit/loss percentage |

### Gas Spend Analysis (v3.0)

**Endpoint:** `GET /gas/{address}?chain=ethereum`

Analyzes gas spending for a wallet.

```bash
curl "http://localhost:5000/gas/0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045"
```

Returns total gas spent in ETH, average gas price, transaction count breakdown, and spending patterns.

### Token Holder Analysis (v1.6, enhanced v3.0)

**Endpoint:** `GET /token/{contract}/holders?chain=ethereum&limit=20`

Analyzes top holders of any ERC-20 token with trust scoring and classification.

```bash
curl "http://localhost:5000/token/0xdac17f958d2ee523a2206206994597c13d831ec7/holders?limit=5"
```

| Field | Description |
|---|---|
| `tokenSymbol` | Token symbol |
| `holdersAnalyzed` | Number of holders profiled |
| `topHolderConcentration` | % of supply held by top 10 holders |
| `holders[].trustScore` | Trust score for each holder (0-100) |
| `holders[].tags` | Tags: `power-user`, `active-trader`, `whale`, `well-funded`, `ens-holder`, `major-holder`, `significant-holder`, `exchange`, `dust-holder`, `known:{category}` |
| `holders[].ensName` | ENS name or known contract label |

### Portfolio History (v1.6)

**Endpoint:** `GET /history/{address}?days=30`

Returns historical portfolio snapshots recorded when profiles are built.

```bash
curl "http://localhost:5000/history/0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045"
```

| Field | Description |
|---|---|
| `snapshotCount` | Number of snapshots in the period |
| `currentValueUsd` | Latest portfolio value |
| `oldestValueUsd` | Oldest portfolio value in period |
| `valueChangePct` | Percentage change over the period |
| `snapshots[]` | Individual snapshots with timestamps |

### Monitor Subscription Plans (v1.6)

**Endpoint:** `GET /monitor/plans`

Returns available subscription tiers for the whale movement monitor.

```bash
curl http://localhost:5000/monitor/plans
```

| Plan | Fee | Max Subs | Poll Interval | Balance Alerts |
|---|---|---|---|---|
| Free | 0 ETH/month | 1 | 60s | No |
| Basic | 0.01 ETH/month | 10 | 30s | Yes |
| Premium | 0.05 ETH/month | 100 | 15s | Yes |

### Freemium Tier (v1.7)

Use `tier: "free"` for a zero-cost lightweight profile:

```bash
curl -X POST http://localhost:5000/profile \
  -H "Content-Type: application/json" \
  -d '{"address":"vitalik.eth","tier":"free"}'
```

Returns: ETH balance, transaction count, token count, risk level, basic tags, and an upgrade hint. No token details, no USD prices.

### MEV Exposure Detection (v1.7)

Available on standard and premium tiers. Detects MEV attacks affecting the wallet.

| Field | Description |
|---|---|
| `mevExposure.sandwichAttacks` | Number of sandwich attacks detected |
| `mevExposure.frontrunTransactions` | Frontrun transactions detected |
| `mevExposure.estimatedLossUsd` | Estimated loss from MEV attacks |
| `mevExposure.riskLevel` | `none`, `low`, `moderate`, or `high` |
| `mevExposure.recentIncidents` | Last 10 MEV incidents |

### Reputation Badge (v1.7)

**Endpoint:** `GET /reputation/{address}`

Generates ERC-721 compatible metadata for an on-chain reputation NFT.

```bash
curl http://localhost:5000/reputation/vitalik.eth
```

Returns trust score, classification, wallet age, tags, and a `badgeUri` containing base64-encoded JSON metadata suitable for minting as a soulbound NFT.

### Supported Chains (v1.7)

**Endpoint:** `GET /chains`

Lists all supported chains with chain IDs and native token symbols.

### Enterprise Pricing (v1.7)

**Endpoint:** `GET /pricing/enterprise`

Returns available enterprise subscription plans for high-volume API access.

| Plan | Monthly Fee | Included Profiles | Support |
|---|---|---|---|
| Starter | 0.5 ETH | 1,000 | Email |
| Growth | 2 ETH | 5,000 | Priority |
| Enterprise | 10 ETH | 50,000 | Dedicated |

### Social Identity (v1.8)

**Endpoint:** `GET /identity/{address}`

Analyzes social identity signals for a wallet.

```bash
curl http://localhost:5000/identity/vitalik.eth
```

| Field | Description |
|---|---|
| `identityScore` | Social identity score (0-100) |
| `identityLevel` | `anonymous`, `pseudonymous`, or `identified` |
| `socialSignals` | List of identity signals found |
| `ensTextRecords` | ENS text records (name, avatar, social links) |
| `daoMemberships` | Number of governance protocols participated in |

### Wallet Comparison (v1.8)

**Endpoint:** `POST /compare`

Compare 2-10 wallets side-by-side with insights.

```json
{
  "addresses": [
    "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045",
    "0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B"
  ],
  "chain": "ethereum",
  "tier": "standard"
}
```

Returns: per-wallet stats, leader identification, common tokens, and unique insights about the group.

### Agent Referral Program (v1.8)

**Register:** `POST /referral/register`
```json
{ "agentAddress": "0xYourAgentAddress" }
```
Returns a unique referral code. Share it with other agents — earn 10% commission on their profile fees.

**Check stats:** `GET /referral/{address}`
Returns total referrals, total earnings, and recent referral records.

### Risk Score (v2.7)

**Endpoint:** `GET /risk/{address}?chain=ethereum`

Standalone risk assessment with verdict and flags. Fills the gap between quickcheck ($0.01) and walletprofiler ($0.03).

```bash
curl http://localhost:5000/risk/0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045
```

**Response:**
```json
{
  "address": "0xd8d...045",
  "chain": "ethereum",
  "riskScore": 35,
  "riskLevel": "medium",
  "verdict": "CAUTION",
  "riskFlags": ["No transaction history found", "Interacts with very few addresses"],
  "tags": ["ens-holder", "diversified"],
  "isSanctioned": false,
  "sanctionsRisk": "clear",
  "approvalRiskCount": 0,
  "unlimitedApprovals": 0,
  "checkedAt": "2026-03-11T22:43:49Z"
}
```

| Verdict | Score Range | Meaning |
|---|---|---|
| SAFE | 0-19 | Low risk, well-established wallet |
| CAUTION | 20-49 | Some risk indicators, proceed carefully |
| WARNING | 50-74 | Multiple risk factors present |
| DANGER | 75-100 | High risk, avoid transacting |

### Virtuals Ecosystem Intelligence (v2.8)

**Endpoint:** `GET /virtuals/ecosystem`

Live Virtuals Protocol ecosystem data. No parameters required. Cached for 5 minutes.

```bash
curl http://localhost:5000/virtuals/ecosystem
```

Returns VIRTUAL token price, top AI agent tokens ($AIXBT, $GAME, $LUNA, $VADER, $SEKOIA, $AIMONICA), ecosystem total market cap, 24h volume, health sentiment, and a natural language summary.

### Risk Score Interpretation

| Score Range | Level | Meaning |
|---|---|---|
| 0–19 | Low | Well-established, active wallet |
| 20–49 | Medium | Some caution warranted |
| 50–74 | High | Multiple risk indicators present |
| 75–100 | Critical | Strong indicators of risk |

### Spam Token Detection

Tokens are automatically flagged as spam (`isSpam: true`) if they match any of:
- No symbol or symbol longer than 20 characters
- Non-ASCII characters in symbol (e.g., Chinese/emoji spam tokens)
- URL-like patterns in symbol (http, .com, .io)
- Phishing keywords (visit, claim, airdrop, reward)

Spam tokens are sorted to the bottom of the token list and excluded from `totalValueUsd`.

### Caching

Responses are cached in-memory for performance:
- Full profiles: 5 minutes
- ENS resolution: 1 hour
- Token prices: 1 minute

Repeat requests for the same address/chain/tier within the TTL return instantly (~3ms vs ~10s uncached).

### API Key Authentication (v1.5)

When API keys are configured, all endpoints (except `/health`, `/tiers`, `/sla`) require an `X-API-Key` header:

```bash
curl -H "X-API-Key: your-key-here" \
  -X POST http://localhost:5000/profile \
  -H "Content-Type: application/json" \
  -d '{"address":"vitalik.eth"}'
```

**Rate limiting:** Each API key has a configurable rate limit. Exceeding it returns `429 Too Many Requests`.

**Development mode:** When no API keys are configured in appsettings, all requests pass through without authentication.

### Redis Cache (v1.5)

To enable Redis as an L2 cache, add the connection string to `appsettings.Development.json`:

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379"
  }
}
```

The health endpoint shows the active cache backend:
```bash
curl http://localhost:5000/health
# Returns: {"status":"healthy","cacheBackend":"redis"}
```

### SLA Monitoring (v1.5)

```bash
curl http://localhost:5000/sla
```

Returns per-endpoint latency percentiles (p50/p95/p99), request counts, error rates, and SLA compliance tracking. Use this to monitor service performance.

### Health Check

```bash
curl http://localhost:5000/health
# Returns: {"status":"healthy","cacheBackend":"memory"}
```

## Running as an AGDP Service

See [DEPLOY.md](../DEPLOY.md) for full deployment instructions.

### AWS Deployment (v2.0)

The service runs on AWS EC2 via Docker Compose. Two containers:

1. **profiler-api** — C# backend on port 5000
2. **acp-runtime** — ACP seller runtime (WebSocket to marketplace)

**Quick start:**
```bash
# On EC2 (Ubuntu 24.04)
cd ~/wallet-profiler/deploy
cp .env.example .env        # fill in API keys
# create acp-config.json    # agent credentials
docker-compose up -d --build
```

**Monitor:**
```bash
docker-compose ps            # container status
docker-compose logs -f       # live logs
docker-compose logs acp-runtime  # ACP connection status
```

**From local machine (ACP CLI):**
```bash
acp browse WalletProfiler   # verify marketplace listing
acp job active               # check incoming jobs
acp wallet balance           # check earnings
```

## Troubleshooting

**Empty token list:**
- Verify your Alchemy API key is valid — token discovery uses `alchemy_getTokenBalances`

**Null USD prices for some tokens:**
- DeFi Llama only tracks tokens with sufficient liquidity
- Obscure memecoins and spam tokens will show `null` for price
- The `totalValueUsd` only sums priced, non-spam tokens

**ENS resolution fails:**
- ENS only works on Ethereum mainnet
- Check your Alchemy API key

**Summary is null:**
- Summary is only included with the `premium` tier

**DeFi positions empty:**
- 9 protocols are checked: Aave V3, Compound V3, Lido (stETH/wstETH), Rocket Pool (rETH), Coinbase (cbETH), EtherFi (weETH), Frax (sfrxETH), MakerDAO (sDAI), EigenLayer (restaking), Ethena (sUSDe)
- Currently Ethereum-only (except Aave V3 which works cross-chain)

**Portfolio quality / ACP trust / approval risk is null:**
- These are only included with `standard` and `premium` tiers

**Approval scan shows 0 approvals:**
- The wallet may not have interacted with the checked DEX routers
- Only the top 10 non-spam tokens are scanned against 7 known spenders

**NFT data is empty or null:**
- NFTs are only included with `standard` and `premium` tiers
- Verify your Alchemy API key supports NFT API v3
- Some wallets may not hold any ERC-721/1155 tokens

**Multi-chain returns empty profiles for some chains:**
- The wallet may not have activity on that chain
- Ensure Alchemy supports the requested chain

**401 Unauthorized:**
- API keys are configured but no `X-API-Key` header was sent
- Check that your key matches one configured in appsettings

**429 Too Many Requests:**
- Rate limit exceeded for your API key
- Wait for the rate window to reset (default 60 seconds)

**Redis connection failed:**
- Service continues to work with memory-only cache
- Check Redis connection string in appsettings

**Monitor webhook not firing:**
- Verify the webhook URL is publicly accessible
- The monitor polls every 30 seconds — alerts are not instant
- Subscriptions are stored in-memory and do not survive server restarts
