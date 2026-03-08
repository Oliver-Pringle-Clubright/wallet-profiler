# Wallet Profiler — User Guide

## Overview

The Wallet Profiler is an AGDP (Agent GDP) service agent that provides comprehensive on-chain wallet analysis for Ethereum-compatible blockchains. Given a wallet address or ENS name, it returns a detailed profile including token holdings with live USD valuations, DeFi positions, transaction history, risk assessment, and spam detection.

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
| ERC-20 tokens with balances | Yes | Yes | Yes |
| ENS resolution | Yes | Yes | Yes |
| Risk score | Yes | Yes | Yes |
| Spam token detection | Yes | Yes | Yes |
| USD prices for tokens | — | Yes | Yes |
| Total portfolio value | — | Yes | Yes |
| DeFi positions (Aave, Compound) | — | Yes | Yes |
| Transaction activity history | — | Yes | Yes |
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

2. Configure API keys in `profiler-api/ProfilerApi/appsettings.json`:
   ```json
   {
     "Alchemy": { "ApiKey": "your_alchemy_key" },
     "Etherscan": { "ApiKey": "your_etherscan_key" }
   }
   ```

3. Build and run the C# API:
   ```bash
   cd profiler-api/ProfilerApi
   dotnet run
   ```

4. The API is now available at `http://localhost:5000`.

### Making a Request

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
  "summary": "This is a mid-range wallet (active since 2015 (10+ years)), holding $62.7K total. Portfolio breakdown: $62.5K in ETH, $179.31 in CULT. 1647 transactions across 217 unique addresses, active on 200 distinct days. Low risk — well-established wallet with no significant concerns.",
  "profiledAt": "2026-03-08T14:57:03Z"
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
| `topTokens` | array | All | ERC-20 tokens (up to 50) |
| `topTokens[].isSpam` | bool | All | Whether the token is flagged as spam |
| `deFiPositions` | array | Std+ | Active DeFi positions |
| `risk` | object | All | Risk assessment with score and flags |
| `activity` | object? | Std+ | Transaction history summary |
| `summary` | string? | Premium | Natural language wallet summary |

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
