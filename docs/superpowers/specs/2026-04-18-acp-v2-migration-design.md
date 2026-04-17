# ACP v1 → v2 Migration — Design

**Date:** 2026-04-18
**Scope:** Wallet Profiler ACP seller agent
**Target directory:** `C:\code_crypto\wallet-profiler\profiler-api\`

## Background

Wallet Profiler is an Agent Commerce Protocol (ACP) seller agent on Virtual-Protocol with 22 offerings that proxy to a C# .NET 10 analysis API.

The current (v1) ACP integration is a **hand-rolled TypeScript CLI** at `C:/code_crypto/virtuals-protocol-acp/` (a sibling repo, not part of this project). It connects to `acpx.virtuals.io` over socket.io, uses local private-key signing, branches on job phases (`REQUEST` → `NEGOTIATION` → `TRANSACTION` → `EVALUATION`), and registers offerings via a custom `acp sell create` command.

V2 changes are breaking across authentication, event model, pricing, transport, and offering registration. A secondary `wallet-profiler/acp-service/` folder is a stub wrapper and is also superseded.

## Goals

- Replace v1 ACP integration with `@virtuals-protocol/acp-node-v2`.
- Preserve all 22 current offerings.
- Support both **Base Sepolia** (testnet) and **Base mainnet** via one env var.
- Keep the C# profiler API the source of truth; ACP sidecar is a thin dispatcher.
- Retire the custom v1 CLI in favour of the official SDK + Virtuals UI for offering registration.

## Non-goals

- Migrating offerings to a new agent wallet (reuse existing `0xf19526…440a`).
- Automating offering registration (V2 registry is UI-only).
- Porting Solana or non-EVM chains.
- Subscription-tier offerings (not in V2 SDK yet).
- Any refactor to the C# analysis logic beyond the two new deliverable endpoints.

## Chosen approach

**Option C1 (greenfield, Node sidecar nested in `profiler-api/`).**

A new TypeScript sidecar under `profiler-api/acp-v2/` uses `@virtuals-protocol/acp-node-v2` with `PrivyAlchemyEvmProviderAdapter` to speak V2. It proxies job requirements to the existing C# API at `http://profiler-api:5000` over HTTP. The C# project gains a small deliverable store for large payloads. The old `acp-service/` folder is deleted; the sibling `virtuals-protocol-acp/` repo is left untouched but no longer built by deploy.

## Architecture

```
┌─────────────────────────────┐       ┌──────────────────────────────┐
│  acp-v2-seller  (Node.js)   │       │  profiler-api  (C# .NET 10)  │
│  — @virtuals-protocol/      │ HTTP  │  — existing wallet analysis  │
│    acp-node-v2              │──────▶│  — NEW POST /deliverables    │
│  — PrivyAlchemyEvm adapter  │       │  — NEW GET  /deliverables/{id}│
│  — dispatches 22 offerings  │       │  — Redis store (1h TTL)      │
└─────────────────────────────┘       └──────────────────────────────┘
         │
         ▼
   acpx.virtuals.io  (SSE default)
```

Two long-running containers (sidecar + C# API) plus Redis for the deliverable store. One SSE connection to Virtuals infrastructure; signing handled internally by Privy.

## Directory layout

```
profiler-api/
  ProfilerApi/                       # C# project
    Program.cs                       # + 2 new endpoints (deliverable store)
    Services/DeliverableStore.cs     # NEW — Redis wrapper for stored JSON
    appsettings.json                 # + Redis connection string
  acp-v2/                            # NEW — Node.js V2 seller sidecar
    package.json
    tsconfig.json
    Dockerfile
    .env.example
    scripts/
      print-offerings-for-registration.ts   # emits offering JSON for UI copy-paste
    src/
      seller.ts                      # entry: AcpAgent.create + agent.on("entry", ...)
      provider.ts                    # PrivyAlchemyEvmProviderAdapter factory
      router.ts                      # offering-name → handler dispatch
      pricing.ts                     # { offeringName → AssetToken.usdc(amount, chainId) }
      deliverable.ts                 # hybrid inline-vs-URL logic
      profilerClient.ts              # typed fetch wrapper around C# API
      offerings/
        registry.ts                  # { [name]: { validate, execute, requirementSchema, description } }
        walletprofiler.ts
        quickcheck.ts
        trustscore.ts
        batchprofiler.ts
        multichain.ts
        walletcompare.ts
        tokenholders.ts
        whalealerts.ts               # renamed from "whalemonitor" to match existing code
        reputation.ts
        identity.ts
        gasspend.ts
        approvalaudit.ts
        portfoliohistory.ts
        tokenscreen.ts
        riskscore.ts
        virtualsintel.ts
        pnl.ts
        lppositions.ts
        liquidationrisk.ts
        rebalance.ts
        airdrops.ts
        aianalyze.ts
        deepanalysis.ts
        walletstatus.ts
    tests/                           # vitest
      pricing.test.ts
      deliverable.test.ts
      router.test.ts
      offerings/                     # per-offering validate() tests
```

## Component boundaries

### `provider.ts`
One function: `createProvider()`. Reads `ACP_CHAIN`, maps to viem's `baseSepolia` or `base`, constructs `PrivyAlchemyEvmProviderAdapter.create({ walletAddress, walletId, signerPrivateKey, chains, builderCode? })`. No state. Fails fast on missing env vars.

### `seller.ts`
Entry point. Creates provider, creates `AcpAgent`, registers a single `agent.on("entry", handler)`, calls `agent.start()`, wires SIGINT/SIGTERM to `agent.stop()`.

### Event handler (inside `seller.ts`, delegates to `router.ts`)
- `entry.kind === "system"` + `entry.event.type === "job.created"` → log.
- `entry.kind === "message"` + `entry.contentType === "requirement"` → parse `{ name, requirement }`. Look up offering in registry. Call `validate(requirement)`:
  - invalid → `session.sendMessage(reason)`; do **not** setBudget (job times out via SLA).
  - valid → `session.setBudget(priceFor(name, requirement, session.chainId))`.
- `entry.event.type === "job.funded"` → call `execute(requirement)` → pipe through `deliverable.ts` → `session.submit(content)`.
- `entry.event.type === "job.completed" | "job.rejected"` → log.
- `entry.event.type === "job.timedout"` (or equivalent) → log and move on.

### `router.ts`
Pure dispatcher: `dispatch(offeringName, phase, requirement, session)`. Looks up registry. Single place for error wrapping so handler errors become logged seller messages rather than unhandled rejections.

### `pricing.ts`
Single exported function: `priceFor(offeringName, requirement, chainId): AssetToken`. Internal tables:

```ts
const TIER_USDC = { free: 0, basic: 1, standard: 2, premium: 5 };
const OFFERING_OVERRIDES: Record<string, number> = {
  quickcheck: 0.5,
  walletstatus: 0.5,
  trustscore: 2,
  riskscore: 2,
  approvalaudit: 2,
  gasspend: 2,
  tokenscreen: 2,
  identity: 2,
  reputation: 2,
  batchprofiler: 5,
  whalealerts: 10,
};
```

For offerings absent from `OFFERING_OVERRIDES` (walletprofiler, multichain, tokenholders, portfoliohistory, walletcompare, virtualsintel, pnl, lppositions, liquidationrisk, rebalance, airdrops, aianalyze, deepanalysis), price by `requirement.tier ?? "standard"` via `TIER_USDC`.

All prices in one file; trivial to change.

### `deliverable.ts`
```ts
export async function toDeliverable(jobId: string, payload: unknown): Promise<string> {
  const json = JSON.stringify(payload);
  if (json.length <= 50_000) return json;                  // inline
  const { url } = await profilerClient.storeDeliverable(jobId, payload);
  return url;                                              // URL fallback
}
```

Threshold (50 KB) as a constant, exported for tests.

### `profilerClient.ts`
Typed wrapper over `fetch` against `PROFILER_API_URL`. One method per C# endpoint the sidecar needs (profile, batch, multichain, quickcheck, etc.) plus `storeDeliverable(jobId, payload)`. Per-call timeout (30 s standard, 60 s for batch/deep).

### `offerings/*.ts`
One file per offering. Each exports `{ requirementSchema, description, validate, execute }`:

```ts
export const walletprofiler: Offering = {
  description: "...",                                      // copied from v1 offering.json
  requirementSchema: { type: "object", properties: {...}, required: ["address"] },
  validate(req) { /* same as v1 */ },
  async execute(req) {
    const { address, chain = "ethereum", tier = "standard" } = req;
    const addresses = address.split(",").map(s => s.trim()).filter(Boolean);
    return addresses.length > 1
      ? await profilerClient.profileBatch({ addresses, chain, tier })
      : await profilerClient.profile({ address, chain, tier });
  },
};
```

`offerings/registry.ts` re-exports all 22 keyed by offering name.

### C# additions (`ProfilerApi`)
- **`Services/DeliverableStore.cs`** — thin wrapper around the existing `StackExchange.Redis` connection. Keys: `deliverable:{guid}`. Value: JSON. TTL: 1 hour.
- **`POST /deliverables`** — body `{ jobId: string, payload: object }`. Generates GUID, stores payload, returns `{ id, url }` where `url = $"{PublicBaseUrl}/deliverables/{id}"`. `PublicBaseUrl` comes from config (`AppSettings:PublicBaseUrl`).
- **`GET /deliverables/{id}`** — looks up `deliverable:{id}` in Redis. Returns JSON with `Content-Type: application/json` or 404 if expired/missing.
- **No auth** on `GET /deliverables/{id}` — IDs are 128-bit GUIDs, short-lived (1 h), and only stored for successfully-delivered jobs. Accepted risk.

## Configuration

### `.env` (gitignored)

```
# Credentials — from https://app.virtuals.io/acp/agents/ → Signers tab
ACP_WALLET_ADDRESS=0xf19526F4A82f51da749c4776fA00bDA0076C440a
ACP_WALLET_ID=...
ACP_SIGNER_PRIVATE_KEY=0x...
ACP_BUILDER_CODE=bc-...            # optional, Settings tab

# Chain switch
ACP_CHAIN=baseSepolia              # or "base" for mainnet

# Inter-service
PROFILER_API_URL=http://profiler-api:5000

# C# side
ALCHEMY_API_KEY=...
ETHERSCAN_API_KEY=...
REDIS_CONNECTION=redis:6379
PUBLIC_BASE_URL=https://walletprofiler.example.com   # for deliverable URLs
```

### `.env.example`
Committed, identical shape, empty/placeholder values.

## Deployment

Replace current `deploy/docker-compose.yml`:

```yaml
services:
  profiler-api:
    build: { context: ../profiler-api, dockerfile: Dockerfile }
    ports: ["5000:5000"]
    env_file: .env
    environment:
      - Alchemy__ApiKey=${ALCHEMY_API_KEY}
      - Etherscan__ApiKey=${ETHERSCAN_API_KEY}
      - Redis__ConnectionString=${REDIS_CONNECTION}
      - AppSettings__PublicBaseUrl=${PUBLIC_BASE_URL}
    depends_on: { redis: { condition: service_started } }
    restart: unless-stopped

  acp-v2:
    build: { context: ../profiler-api/acp-v2, dockerfile: Dockerfile }
    depends_on:
      profiler-api: { condition: service_healthy }
    env_file: .env
    environment:
      - PROFILER_API_URL=http://profiler-api:5000
      - ACP_CHAIN=${ACP_CHAIN:-baseSepolia}
      - ACP_WALLET_ADDRESS=${ACP_WALLET_ADDRESS}
      - ACP_WALLET_ID=${ACP_WALLET_ID}
      - ACP_SIGNER_PRIVATE_KEY=${ACP_SIGNER_PRIVATE_KEY}
      - ACP_BUILDER_CODE=${ACP_BUILDER_CODE:-}
    restart: unless-stopped

  redis:
    image: redis:7-alpine
    restart: unless-stopped
```

Delete `deploy/Dockerfile.acp-runtime`. Rewrite `deploy/deploy.sh` — drop `virtuals-protocol-acp/` sync, add `profiler-api/acp-v2/` sync.

## Offering registration (one-time manual per environment)

V2 has no programmatic registration API. Procedure:

1. Open https://app.virtuals.io/acp/agents/ → WalletProfiler.
2. Upgrade to V2 (if not already done). Pull `walletId` + `signerPrivateKey` from **Signers** tab.
3. Run locally: `npx tsx scripts/print-offerings-for-registration.ts`. Output is one block per offering with name, description, requirement JSON schema, and USDC price.
4. For each of the 22 offerings: UI → Offerings → New offering → paste the block.
5. Repeat on Base mainnet when ready to flip `ACP_CHAIN=base`.

## Testing

**Unit (vitest, CI)**
- `pricing.ts` — every offering resolves to a price > $0 except `free` tier.
- `deliverable.ts` — under threshold returns inline JSON, over threshold calls profilerClient.storeDeliverable and returns URL.
- `router.ts` — unknown offering produces a logged rejection, not a throw.
- Per-offering `validate()` — matches v1 behaviour (address format, chain enum, tier enum).

**Integration (local)**
- `docker compose up` with `ACP_CHAIN=baseSepolia` and a `MOCK_PROFILER=1` flag in the sidecar that stubs `profilerClient` with canned JSON.
- Verify: SSE connects, log shows `listOfferings` count = 22, a scripted fake `job.created` → `job.funded` cycle produces `setBudget` then `submit` calls (captured via a dev-only `--dry-run` flag that logs instead of calling `session.*`).

**Live testnet (Base Sepolia)**
- Register 5 offerings (walletprofiler, quickcheck, batchprofiler, multichain, approvalaudit) on Sepolia.
- Using `TestBuyer` agent (`0x3Ad7936FCF587f29656F6a2D8942db547B4F91Da`), run end-to-end purchases:
  - walletprofiler standard tier → inline deliverable.
  - batchprofiler 50 wallets → URL deliverable (size > 50 KB).
  - multichain → long-running, must finish inside SLA.
  - quickcheck → fast path, $0.50 price correctly charged.
  - approvalaudit → standard $2 price.

## Rollout sequence

1. Branch; scaffold `profiler-api/acp-v2/`; add `DeliverableStore.cs` + endpoints.
2. Unit tests green.
3. Upgrade WalletProfiler agent in Virtuals UI; pull V2 credentials.
4. Register 22 offerings on Base Sepolia via copy-paste from script.
5. Deploy to a staging environment with `ACP_CHAIN=baseSepolia`.
6. Run live testnet plan.
7. Register 22 offerings on Base mainnet.
8. Flip production `ACP_CHAIN=base`, redeploy.
9. Delete `wallet-profiler/acp-service/`.
10. Retire old `acp-runtime` service (already removed from compose — just delete `deploy/Dockerfile.acp-runtime`).
11. Leave sibling `C:/code_crypto/virtuals-protocol-acp/` untouched.

## Error handling

- Missing env var at startup → fail fast with explicit message naming the var.
- `validate()` fails → `session.sendMessage(reason)`, no `setBudget`; job times out per ACP SLA.
- C# API unreachable during `execute()` → catch, `session.sendMessage("Service temporarily unavailable")`, do not `submit`; job times out. Log error with jobId for investigation.
- `storeDeliverable()` fails on oversized payload → fall back to inline submit (accepting ACP's size risk is better than no delivery).
- SSE disconnect → SDK's built-in reconnect; log reconnects.
- Uncaught exception in handler → caught at `router.ts` boundary; logged; does not kill the process.

## Open questions (none blocking)

- Exact USDC threshold on the "free" tier: whether to `setBudget(AssetToken.usdc(0, …))` or skip `setBudget` entirely — will confirm against V2 SDK behaviour during implementation. If zero-budget is rejected, drop the `free` tier at registration time.
- Whether `builderCode` is required for mainnet earning attribution; treat as optional in code.
