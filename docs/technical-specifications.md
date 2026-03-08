# Wallet Profiler — Technical Specifications

## 1. Technology Stack

| Layer | Technology | Version |
|---|---|---|
| **Profiler API** | C# / ASP.NET Minimal APIs | .NET 10 |
| **Ethereum RPC** | Nethereum.Web3 | 5.8.0 |
| **ENS Resolution** | Nethereum.ENS | 5.8.0 |
| **Smart Contract Reads** | Nethereum.Contracts | 5.8.0 |
| **Caching** | Microsoft.Extensions.Caching.Memory | built-in |
| **ACP Runtime** | TypeScript / Node.js | Node 22+ |
| **Containerization** | Docker / Docker Compose | Latest |
| **Blockchain Data** | Alchemy JSON-RPC + Etherscan V2 | Latest |
| **Price Data** | DeFi Llama API | Free tier |

## 2. Project Structure

```
wallet-profiler/
├── profiler-api/                         # C# backend
│   ├── Dockerfile
│   └── ProfilerApi/
│       ├── Program.cs                    # Endpoints, DI, tier routing
│       ├── appsettings.json              # Configuration (API keys)
│       ├── Properties/
│       │   └── launchSettings.json       # Development server settings
│       ├── Models/
│       │   ├── ProfileRequest.cs         # Input model (address, chain, tier)
│       │   ├── WalletProfile.cs          # Output model + sub-models
│       │   ├── ChainConfig.cs            # Chain definitions and constants
│       │   └── EtherscanResponse.cs      # Etherscan V2 polymorphic response
│       └── Services/
│           ├── EthereumService.cs        # Nethereum RPC + ENS (cached)
│           ├── TokenService.cs           # Alchemy getTokenBalances + spam detection
│           ├── PriceService.cs           # DeFi Llama USD pricing (cached)
│           ├── DeFiService.cs            # Aave V3 + Compound V3
│           ├── ActivityService.cs        # Transaction history analysis
│           ├── RiskScoringService.cs     # Heuristic risk scoring
│           ├── SummaryService.cs         # Natural language summary generation
│           └── ProfileCacheService.cs    # In-memory cache with tiered TTLs
├── acp-service/                          # TypeScript ACP proxy
│   ├── Dockerfile
│   ├── package.json
│   ├── offering.json                     # AGDP service listing (tiered)
│   └── handlers.ts                       # ACP job handler (proxy)
├── docs/
│   ├── user-guide.md
│   ├── design.md
│   └── technical-specifications.md
├── docker-compose.yml
├── DEPLOY.md
└── .env.example
```

## 3. API Specification

### POST /profile

**Request:**

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `address` | string | Yes | — | Wallet address (0x...) or ENS name (.eth) |
| `chain` | string | No | `"ethereum"` | `ethereum`, `base`, or `arbitrum` |
| `tier` | string | No | `"standard"` | `basic`, `standard`, or `premium` |

**Response:** `200 OK` — `WalletProfile` JSON

**Error:** `400 Bad Request` — `{ "error": "..." }`

### GET /tiers

Returns the pricing and feature breakdown for all tiers.

### GET /health

Returns `{ "status": "healthy" }`.

## 4. Data Models

```typescript
interface WalletProfile {
  tier: string;
  address: string;
  ensName: string | null;
  ethBalance: number;
  ethPriceUsd: number | null;       // standard+
  ethValueUsd: number | null;       // standard+
  totalValueUsd: number | null;     // standard+ (excludes spam)
  transactionCount: number;
  topTokens: TokenBalance[];        // up to 50
  deFiPositions: DeFiPosition[];    // standard+
  risk: RiskAssessment;
  activity: WalletActivity | null;  // standard+
  summary: string | null;           // premium only
  profiledAt: string;
}

interface TokenBalance {
  symbol: string;
  contractAddress: string;
  balance: number;
  decimals: number;
  priceUsd: number | null;   // standard+
  valueUsd: number | null;   // standard+
  isSpam: boolean;
}

interface DeFiPosition {
  protocol: string;
  type: string;              // "lending" | "borrowing"
  asset: string;
  amount: number;
}

interface RiskAssessment {
  score: number;             // 0-100
  level: string;             // "low" | "medium" | "high" | "critical"
  flags: string[];
}

interface WalletActivity {
  firstTransaction: string | null;
  lastTransaction: string | null;
  daysActive: number;
  uniqueInteractions: number;
}
```

## 5. Service Specifications

### 5.1 TokenService (Alchemy-based)

**Token Discovery:** Uses `alchemy_getTokenBalances` — a single JSON-RPC call that returns ALL non-zero ERC-20 balances for a wallet. This replaces the previous Etherscan transfer-history approach and catches tokens that were received outside the most recent 100 transfers.

**Metadata Resolution:** For each discovered token, calls `alchemy_getTokenMetadata` to get symbol, decimals, and name. These calls run in parallel.

**Balance Conversion:** Hex balances from Alchemy (`0x...`) are converted to human-readable decimals using `BigInteger` parsing and decimal division by `10^decimals`.

**Spam Detection Rules:**

| Rule | Trigger |
|---|---|
| Empty/long symbol | Symbol is blank or > 20 characters |
| Non-ASCII symbol | Contains characters > 127 (Chinese, emoji, etc.) |
| URL in symbol | Contains `http`, `.com`, `.io`, `www` |
| Phishing keywords | Contains `visit`, `claim`, `airdrop`, `reward` |

### 5.2 PriceService (DeFi Llama)

**Single batch call:** `GET https://coins.llama.fi/prices/current/{coins}`

Fetches ETH price (`coingecko:ethereum`) and all token prices (`ethereum:0x...`) in one request. Cached for 1 minute.

### 5.3 ProfileCacheService

**Implementation:** `IMemoryCache` (Microsoft.Extensions.Caching.Memory)

| Cache Type | Key Pattern | TTL |
|---|---|---|
| Full profile | `profile:{address}:{chain}:{tier}` | 5 minutes |
| ENS forward | `ens-reverse:{ensName}` | 1 hour |
| ENS reverse | `ens:{address}` | 1 hour |
| Prices | `prices:{chain}:{contracts}` | 1 minute |

### 5.4 SummaryService

Template-based natural language generation. Classifies wallets by portfolio value:

| Total Value | Classification |
|---|---|
| > $1M | Whale wallet |
| > $100K | High-value wallet |
| > $10K | Mid-range wallet |
| > $1K | Small wallet |
| > $0 | Micro wallet |

Summary includes: wallet classification, age, total value, top 3 assets breakdown, spam count, DeFi activity, transaction stats, and risk interpretation.

### 5.5 DeFiService

**Aave V3:** `getUserAccountData(address)` → totalCollateralBase, totalDebtBase (USD, 8 decimals)

**Compound V3:** `balanceOf(address)` on cUSDC contract → USDC balance (6 decimals)

### 5.6 RiskScoringService

| Condition | Points |
|---|---|
| Wallet < 30 days old | +20 |
| Wallet < 90 days old | +10 |
| No transaction history | +25 |
| < 5 transactions | +15 |
| < 20 transactions | +5 |
| Zero ETH and zero tokens | +20 |
| < 3 unique interactions | +10 |
| DeFi debt/collateral > 80% | +15 |
| Inactive > 6 months | +10 |

Score clamped to 0–100. Levels: low (0–19), medium (20–49), high (50–74), critical (75–100).

## 6. Tiered Request Flow

```
              ┌─────────┐    ┌──────────┐    ┌─────────┐
              │  BASIC   │    │ STANDARD │    │ PREMIUM │
              └────┬─────┘    └────┬─────┘    └────┬────┘
                   │               │               │
  ETH balance      ●               ●               ●
  Tx count         ●               ●               ●
  ENS resolve      ●               ●               ●
  Token balances   ●               ●               ●
  Spam detection   ●               ●               ●
  Risk scoring     ●               ●               ●
  DeFi positions   ○               ●               ●
  Activity         ○               ●               ●
  USD prices       ○               ●               ●
  Summary          ○               ○               ●
                   │               │               │
  Response time    ~5s             ~8s             ~8s
  Cached           ~3ms            ~3ms            ~3ms
```

## 7. External API Dependencies

| API | Free Tier Limits | Used For |
|---|---|---|
| **Alchemy** | 300M compute units/month | RPC, token balances, token metadata, ENS, DeFi reads |
| **Etherscan V2** | 5 calls/sec | Transaction history (standard+ only) |
| **DeFi Llama** | Unlimited (fair use) | ETH + token USD prices |

## 8. Configuration

### appsettings.json

| Key | Required | Description |
|---|---|---|
| `Alchemy:ApiKey` | Yes | Alchemy API key for all RPC + token queries |
| `Etherscan:ApiKey` | Yes | Etherscan API key for activity data |
| `Basescan:ApiKey` | No | Separate key for Base chain |
| `Arbiscan:ApiKey` | No | Separate key for Arbitrum |

### ACP Offering (offering.json)

| Field | Value |
|---|---|
| `name` | `wallet-profiler` |
| `fee` | `0.001` (base fee, tier adjusts actual cost) |
| `requirements.tier` | `basic` / `standard` / `premium` |

## 9. Error Handling

| Scenario | Behavior |
|---|---|
| Alchemy getTokenBalances fails | Returns empty token list |
| Alchemy getTokenMetadata fails | Token listed with symbol "UNKNOWN" |
| DeFi Llama unavailable | All prices null, totalValueUsd null |
| Etherscan rate limited | Activity returns empty (graceful degradation) |
| ENS resolution fails | ensName returns null |
| Invalid tier parameter | Returns 400 Bad Request |
| Cache hit | Returns cached profile instantly (~3ms) |

The profiler follows a **graceful degradation** pattern — individual data source failures never crash the entire request.
