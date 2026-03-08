# Wallet Profiler v1.2 — User Guide

## Overview

The Wallet Profiler is an AGDP (Agent GDP) service agent that provides comprehensive on-chain wallet analysis for Ethereum-compatible blockchains. Given a wallet address or ENS name, it returns a detailed profile including token holdings with live USD valuations, DeFi positions, transaction history, risk assessment, spam detection, wallet tags, portfolio quality grading, ACP trust scoring, token approval risk scanning, and contract interaction labeling.

The service offers three pricing tiers (basic, standard, premium) and runs on the Agent Commerce Protocol (ACP) marketplace.

## Supported Chains

| Chain | Chain ID | Status |
|---|---|---|
| Ethereum Mainnet | 1 | Fully supported |
| Base | 8453 | Supported (requires Basescan API key) |
| Arbitrum One | 42161 | Supported (requires Arbiscan API key) |

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
| DeFi positions (Aave, Compound) | — | Yes | Yes |
| Transaction activity history | — | Yes | Yes |
| Portfolio quality score | — | Yes | Yes |
| ACP trust score | — | Yes | Yes |
| Token approval risk scan | — | Yes | Yes |
| Contract interaction labels | — | Yes | Yes |
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

Available on standard and premium tiers. Evaluates counterparty trustworthiness for agent-to-agent commerce:

| Score Range | Level | Meaning |
|---|---|---|
| 80–100 | High | Highly trustworthy — established, well-funded wallet |
| 60–79 | Moderate | Generally trustworthy — some history and assets |
| 30–59 | Low | Exercise caution — limited history or assets |
| 0–29 | Untrusted | Insufficient trust signals — new or empty wallet |

Trust factors include: wallet age, ETH balance, transaction depth, ENS ownership, DeFi participation, portfolio quality, and interaction diversity.

### Token Approval Risk Scan

Available on standard and premium tiers. Checks ERC-20 `allowance()` on top 10 non-spam tokens against known DEX routers and protocols:

| Field | Description |
|---|---|
| `totalApprovals` | Number of active non-zero approvals found |
| `unlimitedApprovals` | Approvals with effectively unlimited allowance |
| `highRiskApprovals` | Approvals to unverified or risky contracts |
| `riskLevel` | Overall risk: `safe`, `low`, `caution`, or `danger` |
| `approvals[]` | Individual approval details (token, spender, label, risk) |

**Checked spenders:** Uniswap V2/V3, SushiSwap, 1inch, 0x Exchange, OpenSea Seaport.

### Contract Interaction Labels

Available on standard and premium tiers. Labels the wallet's top 10 most-interacted-with addresses using a database of 45+ known Ethereum contracts:

| Category | Examples |
|---|---|
| `dex` | Uniswap, SushiSwap, 1inch, Curve |
| `nft` | OpenSea, Blur, LooksRare |
| `lending` | Aave V2/V3, Compound |
| `bridge` | Wormhole, Stargate, Arbitrum, Optimism |
| `staking` | Lido, Coinbase cbETH, Rocket Pool |
| `mixer` | Tornado Cash (flagged as security concern) |
| `governance` | UNI, AAVE, MKR, ENS |
| `identity` | ENS Registrar |

Unlabeled addresses show `label: null` — these are typically personal wallets or unlisted contracts.

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

### Health Check

```bash
curl http://localhost:5000/health
# Returns: {"status":"healthy"}
```

## Running as an AGDP Service

See [DEPLOY.md](../DEPLOY.md) for full deployment instructions.

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
- Only Aave V3 and Compound V3 are checked

**Portfolio quality / ACP trust / approval risk is null:**
- These are only included with `standard` and `premium` tiers

**Approval scan shows 0 approvals:**
- The wallet may not have interacted with the checked DEX routers
- Only the top 10 non-spam tokens are scanned against 7 known spenders
