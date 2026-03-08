# Wallet Profiler v1.4 — Technical Specifications

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
│           ├── ApprovalScannerService.cs # ERC-20 approval risk scanning
│           ├── ContractLabelService.cs   # Known contract address labeling
│           ├── SummaryService.cs         # Natural language summary generation
│           ├── ProfileCacheService.cs    # In-memory cache with tiered TTLs
│           ├── NftService.cs             # Alchemy NFT API v3 (v1.3)
│           ├── ProfileOrchestrator.cs    # Shared profile building logic (v1.3)
│           ├── MonitorService.cs         # Whale movement webhook monitor (v1.3)
│           ├── TransferHistoryService.cs # Token transfer timeline (v1.4)
│           ├── WalletClusteringService.cs # Similar wallet discovery (v1.4)
│           └── RevokeRecommendationService.cs # Approval revocation advisor (v1.4)
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

### GET /trust/{address}

**Lightweight pre-transaction trust check.** No tier parameter — always returns minimal data for fast scoring.

| Field | Type | Description |
|---|---|---|
| `address` | string | Resolved wallet address |
| `ensName` | string? | ENS name if exists |
| `ethBalance` | decimal | ETH balance |
| `transactionCount` | int | Total tx count |
| `tokenCount` | int | Number of non-zero ERC-20 balances |
| `trustScore` | int | Quick trust score (0-100) |
| `trustLevel` | string | `untrusted`, `low`, `moderate`, `high` |
| `factors` | string[] | Scoring breakdown |

**Response time:** ~500ms (4 parallel RPC calls, no metadata resolution).

### POST /profile/multi-chain (v1.3)

**Request:**

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `address` | string | Yes | — | Wallet address (0x...) or ENS name |
| `chains` | string[] | No | `["ethereum","base","arbitrum"]` | Chains to profile (max 5) |
| `tier` | string | No | `"standard"` | `basic`, `standard`, or `premium` |

**Response:** `200 OK` — `MultiChainProfile` JSON with per-chain profiles aggregated.

### POST /monitor (v1.3)

Subscribe to wallet movement alerts via webhook.

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `address` | string | Yes | — | Wallet address to monitor |
| `chain` | string | No | `"ethereum"` | Chain to monitor |
| `webhookUrl` | string | Yes | — | URL to receive alert POSTs |
| `thresholdEth` | decimal | No | `10` | ETH threshold for alerts |

**Response:** `200 OK` — `MonitorSubscription` JSON with subscription ID.

### GET /monitor (v1.3)

Returns all active monitor subscriptions.

### DELETE /monitor/{id} (v1.3)

Removes a monitor subscription by ID.

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
  approvalRisk: ApprovalRisk | null; // standard+
  topInteractions: ContractInteraction[]; // standard+
  nfts: NftSummary | null;          // standard+
  transferHistory: TransferHistory | null; // standard+
  similarWallets: SimilarWallets | null;   // standard+
  revokeAdvice: RevokeRecommendations | null; // standard+
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

interface ApprovalRisk {
  totalApprovals: number;
  unlimitedApprovals: number;
  highRiskApprovals: number;
  riskLevel: string;            // "safe" | "low" | "caution" | "danger"
  approvals: TokenApproval[];
}

interface TokenApproval {
  tokenSymbol: string;
  tokenAddress: string;
  spenderAddress: string;
  spenderLabel: string;
  spenderCategory: string;
  isUnlimited: boolean;
  riskLevel: string;
}

interface ContractInteraction {
  address: string;
  label: string | null;         // null = unknown contract
  category: string | null;      // "dex" | "nft" | "lending" | "bridge" | "staking" | "mixer" | etc.
  transactionCount: number;
}

interface TrustCheckResponse {
  address: string;
  ensName: string | null;
  ethBalance: number;
  transactionCount: number;
  tokenCount: number;
  trustScore: number;           // 0-100
  trustLevel: string;           // "untrusted" | "low" | "moderate" | "high"
  factors: string[];
}

interface NftSummary {
  totalCount: number;
  collectionCount: number;
  estimatedValueEth: number | null;
  estimatedValueUsd: number | null;
  topCollections: NftCollection[];
}

interface NftCollection {
  name: string;
  contractAddress: string;
  ownedCount: number;
  floorPriceEth: number | null;
  floorPriceUsd: number | null;
}

interface MultiChainProfile {
  address: string;
  ensName: string | null;
  totalValueUsd: number | null;      // sum across all chains
  chainProfiles: Record<string, WalletProfile>;
  activeChains: string[];
  profiledAt: string;
}

interface MonitorSubscription {
  id: string;
  address: string;
  chain: string;
  webhookUrl: string;
  thresholdEth: number;
  createdAt: string;
  active: boolean;
}

interface WalletAlert {
  subscriptionId: string;
  address: string;
  type: string;
  description: string;
  amountEth: number | null;
  txHash: string | null;
  detectedAt: string;
}

interface TransferHistory {
  totalTransfers: number;
  inboundCount: number;
  outboundCount: number;
  netFlowUsd: number | null;
  recentTransfers: TokenTransfer[];    // last 20 transfers
  timeline: TransferPeriod[];          // monthly breakdown (last 12 months)
}

interface TokenTransfer {
  txHash: string;
  tokenSymbol: string;
  tokenAddress: string;
  direction: string;                   // "in" or "out"
  counterparty: string;
  amount: number;
  valueUsd: number | null;
  timestamp: string;
}

interface TransferPeriod {
  period: string;                      // "2026-03"
  inboundCount: number;
  outboundCount: number;
  inboundValueUsd: number | null;
  outboundValueUsd: number | null;
}

interface SimilarWallets {
  candidatesAnalyzed: number;
  matches: SimilarWallet[];
}

interface SimilarWallet {
  address: string;
  similarityScore: number;             // 0-100
  commonTokens: string[];
  commonInteractions: string[];
  sharedProtocols: number;
}

interface RevokeRecommendations {
  totalRecommendations: number;
  highPriority: number;
  overallUrgency: string;              // "none" | "low" | "medium" | "high"
  recommendations: RevokeRecommendation[];
}

interface RevokeRecommendation {
  tokenSymbol: string;
  tokenAddress: string;
  spenderAddress: string;
  spenderLabel: string;
  priority: string;                    // "low" | "medium" | "high"
  reason: string;
  isUnlimited: boolean;
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

### 5.10 ApprovalScannerService

Checks ERC-20 `allowance(owner, spender)` on the top 10 non-spam tokens against 7 known DEX/protocol spenders. Uses Nethereum contract query handler with a concurrency limit of 10 parallel calls.

**Known spenders checked:**

| Contract | Label |
|---|---|
| `0x7a25...488D` | Uniswap V2 Router |
| `0xE592...1564` | Uniswap V3 Router |
| `0x3fC9...7FAD` | Uniswap Universal Router |
| `0xd9e1...378B` | SushiSwap Router |
| `0x1111...0582` | 1inch V5 Router |
| `0xDef1...25EfF` | 0x Exchange Proxy |
| `0x0000...14dC` | OpenSea Seaport |

**Risk classification:** Unlimited approval (> 10^30) to a known DEX = "caution". Any non-zero limited approval = "safe". Overall risk escalates with unlimited approval count.

### 5.11 ContractLabelService

Static dictionary of 45+ known Ethereum mainnet contracts with labels and categories. Used to enrich top counterparty addresses from ActivityService.

**Categories:** `dex`, `nft`, `lending`, `bridge`, `staking`, `mixer`, `token`, `governance`, `identity`, `system`.

### 5.12 NftService (v1.3)

Uses Alchemy NFT API v3 to fetch NFT holdings and floor prices. Available on standard+ tiers.

**NFT Discovery:** `GET /nft/v3/{key}/getNFTsForOwner?owner={address}&withMetadata=true&pageSize=100`

Returns up to 100 NFTs with collection metadata. Groups by collection, counts per collection.

**Floor Prices:** `GET /nft/v3/{key}/getFloorPrice?contractAddress={contract}`

Fetches floor prices from OpenSea and LooksRare for top 10 collections. Runs in parallel with concurrency limit of 5.

**Output:** NftSummary with total count, collection count, estimated floor value (ETH + USD), and top 10 collections.

### 5.13 ProfileOrchestrator (v1.3)

Extracts the profile-building logic from Program.cs into a reusable service. Used by `/profile`, `/profile/batch`, and `/profile/multi-chain` endpoints. Eliminates code duplication across all three endpoints.

### 5.14 MonitorService (v1.3)

`BackgroundService` that polls subscribed wallets every 30 seconds for new transactions. Sends webhook POST alerts when activity is detected.

**Storage:** `ConcurrentDictionary<string, MonitorSubscription>` (in-memory).

**Polling:** Checks tx count via `eth_getTransactionCount`. Compares with last known count. On new transactions, sends alert payload to the subscriber's webhook URL.

**Concurrency:** Up to 10 subscriptions checked in parallel per poll cycle.

### 5.15 TransferHistoryService (v1.4)

Fetches ERC-20 token transfer events from Etherscan V2 (`action=tokentx`) and builds a transfer timeline with flow analysis.

**Data fetched:** Last 200 token transfers sorted descending.

**Analysis:**
- Groups transfers into monthly periods (last 12 months)
- Classifies each transfer as inbound or outbound
- Calculates net USD flow when token prices are available
- Returns 20 most recent transfers with full details

### 5.16 WalletClusteringService (v1.4)

Finds wallets with similar on-chain behavior by analyzing the wallet's top counterparties.

**Algorithm:**
1. Filter top counterparties — exclude known contracts (DEXes, bridges, etc.)
2. Fetch token balances for each candidate (basic tier, max 5 parallel)
3. Compute Jaccard similarity coefficient on token sets
4. Add interaction frequency bonus (0-20 points)
5. Return top 5 matches with similarity > 5%

### 5.17 RevokeRecommendationService (v1.4)

Analyzes token approvals from ApprovalScannerService and generates prioritized revocation recommendations.

**Priority levels:**
- **High:** Unlimited approval to NFT marketplace (common phishing vector), unlimited to unknown spender, or flagged high-risk contract
- **Medium:** Unlimited approval to trusted DEX router (unnecessary for most users)
- **Low:** Limited approval to known protocol (safe but reclaimable)

**Overall urgency:** Based on count of high-priority recommendations.

### 5.18 Quick Trust Endpoint

Lightweight `GET /trust/{address}` endpoint that scores wallets without full profile analysis.

**Data fetched (4 parallel RPC calls):**
- ETH balance via `eth_getBalance`
- Transaction count via `eth_getTransactionCount`
- ENS reverse resolution (cached)
- Token count via `alchemy_getTokenBalances` (count only, no metadata)

**Scoring (max 100):**

| Factor | Max Points | Criteria |
|---|---|---|
| Transaction count | 35 | 1000+ = 35, 500+ = 30, 100+ = 25, 50+ = 20, 20+ = 15, 5+ = 10 |
| ETH balance | 25 | 10+ = 25, 1+ = 20, 0.1+ = 15, 0.01+ = 10, >0 = 5 |
| ENS identity | 20 | Has ENS name = 20 |
| Token diversity | 20 | 20+ = 20, 5+ = 10, >0 = 5 |

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
  Approval scan    ○               ●               ●
  Contract labels  ○               ●               ●
  ACP trust score  ○               ●               ●
  NFT holdings     ○               ●               ●
  Transfer history ○               ●               ●
  Similar wallets  ○               ●               ●
  Revoke advice    ○               ●               ●
  Summary          ○               ○               ●
                   │               │               │
  Response time    ~3s             ~8s             ~8s
  Cached           ~3ms            ~3ms            ~3ms
```

## 7. External API Dependencies

| API | Free Tier Limits | Used For |
|---|---|---|
| **Alchemy** | 300M compute units/month | RPC, token balances, token metadata, ENS, DeFi reads, NFT API v3 |
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
