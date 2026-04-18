Wallet Profiler v3.0
====================
AGDP Service Agent for On-Chain Wallet Analysis

An AI agent service on the Agent Commerce Protocol (ACP) marketplace that
provides comprehensive Ethereum wallet profiling. Given a wallet address or
ENS name, it returns token holdings, DeFi positions, risk scoring, trust
assessment, approval auditing, transfer history, and more.

ARCHITECTURE
------------
- Profiler API:  C# / ASP.NET Minimal APIs on .NET 10
- ACP Runtime:   TypeScript / Node.js seller agent
- Deployment:    Docker Compose on AWS EC2 (Ubuntu)
- Blockchain:    Alchemy JSON-RPC + Etherscan V2
- Pricing:       DeFi Llama (USD), CoinGecko (ecosystem)
- Caching:       In-memory L1 + optional Redis L2

SUPPORTED CHAINS
----------------
Ethereum, Base, Arbitrum, Polygon, Optimism, Avalanche, BNB Chain, Solana

ACP MARKETPLACE OFFERINGS (16)
------------------------------
  walletprofiler    Full wallet profile (basic/standard/premium)
  quickcheck        Ultra-fast wallet status
  trustscore        Pre-transaction trust check
  batchprofiler     Batch profile up to 50 wallets
  multichain        Cross-chain aggregated profile
  walletcompare     Compare 2-10 wallets side-by-side
  tokenholders      Token holder analysis with trust scoring
  whalemonitor      Whale movement webhook alerts
  reputation        On-chain reputation badge (ERC-721 metadata)
  identity          Social identity correlation
  gasspend          Gas spending analysis
  approvalaudit     Token approval risk scan + revoke advice
  portfoliohistory  Portfolio snapshot history
  tokenscreen       Token holder screening
  riskscore         Standalone risk assessment with verdict
  virtualsintel     Virtuals Protocol ecosystem intelligence

SERVICE HIGHLIGHTS (v3.0)
-------------------------
- 9-protocol DeFi scanner: Aave V3, Compound V3, Lido, Rocket Pool,
  Coinbase cbETH, EtherFi, Frax, MakerDAO, EigenLayer, Ethena
- 130+ known contract labels (DEX, NFT, lending, bridges, staking,
  exchanges, governance, restaking, yield)
- Transfer history with native ETH + ERC-20 unified timeline
- Wallet clustering with shared DeFi protocol detection
- Token holder profiling with exchange/whale/dust classification
- ACP trust scoring with 14 factors (9 positive, 5 negative)
- Revoke engine with 18 known exploited contracts (critical priority)
- Portfolio snapshots with Redis persistence (90-day TTL)
- Gas spend analysis endpoint

QUICK START
-----------
1. Install .NET 10 SDK and Node.js 20+
2. Get an Alchemy API key and Etherscan API key (free tiers work)
3. Configure keys in profiler-api/ProfilerApi/appsettings.Development.json
4. Run: cd profiler-api/ProfilerApi && dotnet run
5. Test: curl http://localhost:5000/health

SECURITY
--------
- Never commit API keys. Use appsettings.Development.json (gitignored).
- OFAC sanctions screening on all standard+ profiles.
- Rate limiting via configurable API key authentication.

DOCUMENTATION
-------------
  docs/design.md                   Architecture and design decisions
  docs/technical-specifications.md Full API spec, data models, service details
  docs/user-guide.md               Usage guide with examples
  DEPLOY.md                        AWS deployment instructions

REPOSITORY STRUCTURE
--------------------
  profiler-api/           C# backend (ProfilerApi project)
  virtuals-protocol-acp/  ACP seller runtime + 16 offerings
  docs/                   Documentation
  docker-compose.yml      Container orchestration
  AWS/                    Deployment keys and scripts
