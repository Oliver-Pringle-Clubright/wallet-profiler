# Wallet Profiler v1.1 — Technical Specifications

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
│       ├── appsettings.json              # Configuration (placeholder keys)
│       ├── appsettings.Development.json  # Real API keys (gitignored)
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
│           ├── WalletTaggingService.cs   # Wallet classification tags
│           ├── PortfolioQualityService.cs # Portfolio quality grading
│           ├── AcpTrustService.cs        # ACP trust score for agent commerce
│           ├── SummaryService.cs         # Natural language summary generation
│           └── ProfileCacheService.cs    # In-memory cache with tiered TTLs
├── acp-service/                          # TypeScript ACP proxy
│   ├── Dockerfile
│   ├── package.json
│   ├── offering.json                     # AGDP service listing (tiered)
│   └── handlers.ts                       # ACP job handler (proxy + batch)
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

### POST /profile/batch

**Request:**

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `addresses` | string[] | Yes | — | 1–50 wallet addresses or ENS names |
| `chain` | string | No | `"ethereum"` | `ethereum`, `base`, or `arbitrum` |
| `tier` | string | No | `"standard"` | `basic`, `standard`, or `premium` |

**Response:** `200 OK` — `BatchProfileResponse` JSON

**Error:** `400 Bad Request` — `{ "error": "..." }`

**Concurrency:** Up to 5 addresses processed in parallel. Cached profiles return instantly.

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
  topTokens: TokenBalance[];        // up to 15/30/50 by tier
  deFiPositions: DeFiPosition[];    // standard+
  risk: RiskAssessment;
  activity: WalletActivity | null;  // standard+
  tags: string[];                   // all tiers
  portfolioQuality: PortfolioQuality | null;  // standard+
  acpTrust: AcpTrustScore | null;   // standard+
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

interface PortfolioQuality {
  bluechipPct: number;       // % of value in ETH + blue-chip tokens
  stablecoinPct: number;     // % of value in stablecoins
  spamPct: number;           // % of tokens flagged as spam
  diversityScore: number;    // 0-100
  qualityScore: number;      // 0-100 composite
  grade: string;             // "A" | "B" | "C" | "D" | "F"
}

interface AcpTrustScore {
  score: number;             // 0-100
  level: string;             // "untrusted" | "low" | "moderate" | "high"
  factors: string[];         // contributing signals with point values
}

interface BatchProfileResponse {
  total: number;
  succeeded: number;
  failed: number;
  elapsedMs: number;
  results: BatchProfileResult[];
}

interface BatchProfileResult {
  address: string;
  profile: WalletProfile | null;
  error: string | null;
}
```

## 5. Service Specifications

### 5.1 TokenService (Alchemy-based)

**Token Discovery:** Uses `alchemy_getTokenBalances` — a single JSON-RPC call that returns ALL non-zero ERC-20 balances for a wallet.

**Metadata Resolution:** For each discovered token, calls `alchemy_getTokenMetadata` to get symbol, decimals, and name. Calls run in parallel with a concurrency limit of 10.

**Tier-based Metadata Caps:** To optimize response time, the number of metadata calls is capped per tier:

| Tier | Metadata Cap | Rationale |
|---|---|---|
| Basic | 15 | Fastest response, top tokens only |
| Standard | 30 | Balanced coverage |
| Premium | 50 | Full coverage |

Tokens beyond the cap retain their hex balance converted with default 18 decimals.

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

Summary includes: wallet classification, age, total value, top 3 assets breakdown, spam count, DeFi activity, transaction stats, wallet tags, portfolio quality grade, ACP trust level, and risk interpretation.

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

### 5.7 WalletTaggingService

Classifies wallets with descriptive tags based on on-chain data:

| Tag | Criteria |
|---|---|
| `whale` | TotalValueUsd > $1M |
| `high-roller` | TotalValueUsd > $100K |
| `mid-tier` | TotalValueUsd > $10K |
| `ens-holder` | Has ENS name |
| `defi-user` | Has DeFi positions |
| `multi-protocol` | Uses 2+ DeFi protocols |
| `veteran` | Wallet > 1 year old |
| `og` | Wallet > 5 years old |
| `fresh-wallet` | Wallet < 30 days old |
| `dormant` | Inactive > 6 months |
| `power-user` | 1000+ txs, 100+ active days |
| `active-trader` | 100+ transactions |
| `well-connected` | 50+ unique interactions |
| `diversified` | 20+ non-spam tokens |
| `concentrated` | 3 or fewer tokens |
| `stablecoin-heavy` | >50% value in stablecoins |
| `hodler` | Old wallet, low tx frequency |
| `spam-magnet` | 10+ spam tokens |

### 5.8 PortfolioQualityService

Evaluates portfolio composition quality on a 0–100 scale:

**Blue-chip tokens:** ETH, WETH, WBTC, LINK, UNI, AAVE, MKR, SNX, CRV, LDO, RPL, ENS, GRT, MATIC, ARB, OP, COMP, SUSHI, BAL, YFI, 1INCH, DYDX, PENDLE, ENA, EIGEN

**Stablecoins:** USDC, USDT, DAI, FRAX, BUSD, TUSD, USDP, GUSD, LUSD, crvUSD, PYUSD, GHO, sUSD

**Quality Score Composition:**
- 40% — Blue-chip allocation (ETH + known tokens)
- 20% — Token diversity (number of priced non-spam tokens)
- 20% — Low spam ratio
- 20% — Stablecoin balance (optimal range: 10–30%)

**Grading:** A (80+), B (60+), C (40+), D (20+), F (<20)

### 5.9 AcpTrustService

Pre-transaction trust evaluation for agent-to-agent commerce. Scores 0–100:

| Factor | Max Points | Criteria |
|---|---|---|
| Wallet age | 25 | 3+ years = 25, 1+ year = 20, 3+ months = 10, 1+ month = 5 |
| ETH balance | 20 | >$10K = 20, >$1K = 15, >$100 = 10, >0 = 5 |
| Transaction depth | 15 | 500+ = 15, 100+ = 10, 20+ = 5 |
| ENS identity | 10 | Has ENS name = 10 |
| DeFi participation | 10 | Multi-protocol = 10, single = 7 |
| Portfolio quality | 10 | Score 60+ = 10, 40+ = 5 |
| Interaction diversity | 10 | 50+ = 10, 20+ = 5 |

**Levels:** high (80+), moderate (60+), low (30+), untrusted (<30)

## 6. Tiered Request Flow

```
              ┌─────────┐    ┌──────────┐    ┌─────────┐
              │  BASIC   │    │ STANDARD │    │ PREMIUM │
              └────┬─────┘    └────┬─────┘    └────┬────┘
                   │               │               │
  ETH balance      ●               ●               ●
  Tx count         ●               ●               ●
  ENS resolve      ●               ●               ●
  Token balances   ● (15 max)      ● (30 max)      ● (50 max)
  Spam detection   ●               ●               ●
  Risk scoring     ●               ●               ●
  Wallet tags      ●               ●               ●
  DeFi positions   ○               ●               ●
  Activity         ○               ●               ●
  USD prices       ○               ●               ●
  Portfolio quality○               ●               ●
  ACP trust score  ○               ●               ●
  Summary          ○               ○               ●
                   │               │               │
  Response time    ~3s             ~8s             ~8s
  Cached           ~3ms            ~3ms            ~3ms
```

## 7. External API Dependencies

| API | Free Tier Limits | Used For |
|---|---|---|
| **Alchemy** | 300M compute units/month | RPC, token balances, token metadata, ENS, DeFi reads |
| **Etherscan V2** | 5 calls/sec | Transaction history (standard+ only) |
| **DeFi Llama** | Unlimited (fair use) | ETH + token USD prices |

## 8. Configuration

### appsettings.json (committed — placeholders only)

| Key | Required | Description |
|---|---|---|
| `Alchemy:ApiKey` | Yes | Alchemy API key for all RPC + token queries |
| `Etherscan:ApiKey` | Yes | Etherscan API key for activity data |
| `Basescan:ApiKey` | No | Separate key for Base chain |
| `Arbiscan:ApiKey` | No | Separate key for Arbitrum |

> **Security:** Real API keys go in `appsettings.Development.json` (gitignored). Never commit secrets.

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
| Batch address count > 50 | Returns 400 Bad Request |
| Individual batch address fails | Error captured per-address, other addresses still succeed |

The profiler follows a **graceful degradation** pattern — individual data source failures never crash the entire request.
