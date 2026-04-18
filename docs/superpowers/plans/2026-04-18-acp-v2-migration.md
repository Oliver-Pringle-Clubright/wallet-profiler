# ACP v1 → v2 Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the custom ACP v1 TypeScript runtime (sibling `virtuals-protocol-acp/`) and the stub `wallet-profiler/acp-service/` with a new Node.js V2 seller sidecar at `profiler-api/acp-v2/`, using `@virtuals-protocol/acp-node-v2`, while adding a Redis-backed deliverable store to the C# API for oversized payloads.

**Architecture:** Two containers — the existing C# `profiler-api` (new: `POST/GET /deliverables` endpoints) and a new Node sidecar `acp-v2` that speaks ACP V2 via `PrivyAlchemyEvmProviderAdapter`, dispatches 22 offerings, and proxies job requirements over HTTP to `http://profiler-api:5000`. Pricing is USDC via `AssetToken.usdc()`. Deliverables are inline under 50 KB, URL-backed above.

**Tech Stack:** TypeScript 5.x · Node 22 · `@virtuals-protocol/acp-node-v2` · `viem` · `@account-kit/infra` · vitest · C# .NET 10 · StackExchange.Redis (already wired via `IDistributedCache`) · Docker Compose.

**Spec reference:** `docs/superpowers/specs/2026-04-18-acp-v2-migration-design.md`

---

## File Structure

### New files (all paths from repo root `wallet-profiler/`)

```
profiler-api/acp-v2/
  package.json
  tsconfig.json
  vitest.config.ts
  Dockerfile
  .env.example
  README.md
  src/
    env.ts                    # loadEnv(): typed, fail-fast env var reader
    chain.ts                  # chainFromEnv(), chain objects from viem
    provider.ts               # createProvider(): PrivyAlchemyEvmProviderAdapter
    profilerClient.ts         # typed fetch wrapper over the C# API
    deliverable.ts            # toDeliverable(): hybrid inline-vs-URL
    pricing.ts                # priceFor(): USDC AssetToken per offering
    validators.ts             # shared validators (address, tier, chain)
    offerings/
      types.ts                # Offering interface
      walletprofiler.ts
      quickcheck.ts
      walletstatus.ts
      riskscore.ts
      multichain.ts
      walletcompare.ts
      deepanalysis.ts
      aianalyze.ts
      airdrops.ts
      approvalaudit.ts
      gasspend.ts
      identity.ts
      liquidationrisk.ts
      lppositions.ts
      pnl.ts
      portfoliohistory.ts
      rebalance.ts
      reputation.ts
      tokenholders.ts
      tokenscreen.ts
      virtualsintel.ts
      whalealerts.ts
      registry.ts             # { [name]: Offering } map of all 22
    router.ts                 # dispatch(entry, session) delegator
    seller.ts                 # entry: AcpAgent.create + agent.on("entry", ...)
  scripts/
    print-offerings-for-registration.ts   # emits JSON blobs for UI copy-paste
  tests/
    env.test.ts
    chain.test.ts
    validators.test.ts
    profilerClient.test.ts
    deliverable.test.ts
    pricing.test.ts
    router.test.ts
    offerings/
      walletprofiler.test.ts
      quickcheck.test.ts
      walletstatus.test.ts
      multichain.test.ts
      walletcompare.test.ts
      deepanalysis.test.ts
      tokenholders.test.ts
      tokenscreen.test.ts
      portfoliohistory.test.ts
      whalealerts.test.ts

profiler-api/ProfilerApi/Services/DeliverableStore.cs   # NEW
```

### Modified files

```
profiler-api/ProfilerApi/Program.cs     # register DeliverableStore; add 2 endpoints
profiler-api/ProfilerApi/appsettings.json  # add AppSettings:PublicBaseUrl
deploy/docker-compose.yml               # replace acp-runtime with acp-v2; add redis
```

### Deleted files / directories

```
wallet-profiler/acp-service/            # superseded stub (entire folder)
deploy/Dockerfile.acp-runtime           # superseded
```

(Sibling `C:/code_crypto/virtuals-protocol-acp/` is left untouched — outside repo.)

---

## Task 1: Scaffold the acp-v2 project

**Files:**
- Create: `profiler-api/acp-v2/package.json`
- Create: `profiler-api/acp-v2/tsconfig.json`
- Create: `profiler-api/acp-v2/vitest.config.ts`
- Create: `profiler-api/acp-v2/.gitignore`

- [ ] **Step 1: Write `package.json`**

```json
{
  "name": "wallet-profiler-acp-v2",
  "version": "1.0.0",
  "private": true,
  "type": "module",
  "scripts": {
    "build": "tsc --noEmit",
    "start": "tsx src/seller.ts",
    "dev": "tsx watch src/seller.ts",
    "test": "vitest run",
    "test:watch": "vitest",
    "print-offerings": "tsx scripts/print-offerings-for-registration.ts"
  },
  "dependencies": {
    "@account-kit/infra": "^4.0.0",
    "@virtuals-protocol/acp-node-v2": "^0.1.0",
    "viem": "^2.21.0"
  },
  "devDependencies": {
    "@types/node": "^22.10.0",
    "tsx": "^4.19.2",
    "typescript": "^5.7.2",
    "vitest": "^2.1.8"
  }
}
```

- [ ] **Step 2: Write `tsconfig.json`**

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "NodeNext",
    "moduleResolution": "NodeNext",
    "lib": ["ES2022"],
    "types": ["node"],
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "esModuleInterop": true,
    "resolveJsonModule": true,
    "skipLibCheck": true,
    "outDir": "dist",
    "rootDir": ".",
    "forceConsistentCasingInFileNames": true
  },
  "include": ["src/**/*", "scripts/**/*", "tests/**/*"]
}
```

- [ ] **Step 3: Write `vitest.config.ts`**

```typescript
import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    environment: "node",
    include: ["tests/**/*.test.ts"],
  },
});
```

- [ ] **Step 4: Write `.gitignore`**

```
node_modules/
dist/
.env
*.log
```

- [ ] **Step 5: Install dependencies**

Run from `profiler-api/acp-v2/`:
```
npm install
```
Expected: lockfile created, `node_modules/` populated. `npm ls @virtuals-protocol/acp-node-v2` resolves.

> **If the published version number differs:** run `npm view @virtuals-protocol/acp-node-v2 version` and update `package.json`. Same check for `viem` and `@account-kit/infra`.

- [ ] **Step 6: Verify TypeScript builds an empty project**

Run:
```
npx tsc --noEmit
```
Expected: exits 0 (no files to check, no errors).

- [ ] **Step 7: Commit**

```
git add profiler-api/acp-v2/package.json profiler-api/acp-v2/package-lock.json profiler-api/acp-v2/tsconfig.json profiler-api/acp-v2/vitest.config.ts profiler-api/acp-v2/.gitignore
git commit -m "acp-v2: scaffold empty Node package"
```

---

## Task 2: Environment loader (`env.ts`)

**Files:**
- Test: `profiler-api/acp-v2/tests/env.test.ts`
- Create: `profiler-api/acp-v2/src/env.ts`

- [ ] **Step 1: Write failing test**

```typescript
// tests/env.test.ts
import { describe, it, expect } from "vitest";
import { loadEnv } from "../src/env.js";

describe("loadEnv", () => {
  it("returns all required vars when present", () => {
    const env = loadEnv({
      ACP_WALLET_ADDRESS: "0xabc",
      ACP_WALLET_ID: "wid-1",
      ACP_SIGNER_PRIVATE_KEY: "0xkey",
      ACP_CHAIN: "baseSepolia",
      PROFILER_API_URL: "http://profiler-api:5000",
    });
    expect(env.walletAddress).toBe("0xabc");
    expect(env.walletId).toBe("wid-1");
    expect(env.signerPrivateKey).toBe("0xkey");
    expect(env.chain).toBe("baseSepolia");
    expect(env.profilerApiUrl).toBe("http://profiler-api:5000");
    expect(env.builderCode).toBeUndefined();
  });

  it("passes through optional builderCode", () => {
    const env = loadEnv({
      ACP_WALLET_ADDRESS: "0xabc",
      ACP_WALLET_ID: "wid-1",
      ACP_SIGNER_PRIVATE_KEY: "0xkey",
      ACP_CHAIN: "base",
      PROFILER_API_URL: "http://profiler-api:5000",
      ACP_BUILDER_CODE: "bc-123",
    });
    expect(env.builderCode).toBe("bc-123");
  });

  it("throws on missing required var with var name in message", () => {
    expect(() =>
      loadEnv({
        ACP_WALLET_ID: "wid-1",
        ACP_SIGNER_PRIVATE_KEY: "0xkey",
        ACP_CHAIN: "base",
        PROFILER_API_URL: "http://profiler-api:5000",
      })
    ).toThrow(/ACP_WALLET_ADDRESS/);
  });

  it("throws on invalid ACP_CHAIN", () => {
    expect(() =>
      loadEnv({
        ACP_WALLET_ADDRESS: "0xabc",
        ACP_WALLET_ID: "wid-1",
        ACP_SIGNER_PRIVATE_KEY: "0xkey",
        ACP_CHAIN: "ethereum",
        PROFILER_API_URL: "http://profiler-api:5000",
      })
    ).toThrow(/ACP_CHAIN/);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```
npm test -- env.test.ts
```
Expected: FAIL with "Cannot find module '../src/env.js'".

- [ ] **Step 3: Implement `src/env.ts`**

```typescript
export type ChainName = "base" | "baseSepolia";

export interface AcpEnv {
  walletAddress: string;
  walletId: string;
  signerPrivateKey: string;
  chain: ChainName;
  profilerApiUrl: string;
  builderCode?: string;
}

const REQUIRED = [
  "ACP_WALLET_ADDRESS",
  "ACP_WALLET_ID",
  "ACP_SIGNER_PRIVATE_KEY",
  "ACP_CHAIN",
  "PROFILER_API_URL",
] as const;

export function loadEnv(source: NodeJS.ProcessEnv = process.env): AcpEnv {
  for (const name of REQUIRED) {
    if (!source[name] || source[name] === "") {
      throw new Error(`Missing required env var: ${name}`);
    }
  }

  const chain = source.ACP_CHAIN;
  if (chain !== "base" && chain !== "baseSepolia") {
    throw new Error(`ACP_CHAIN must be "base" or "baseSepolia", got "${chain}"`);
  }

  return {
    walletAddress: source.ACP_WALLET_ADDRESS!,
    walletId: source.ACP_WALLET_ID!,
    signerPrivateKey: source.ACP_SIGNER_PRIVATE_KEY!,
    chain,
    profilerApiUrl: source.PROFILER_API_URL!,
    builderCode: source.ACP_BUILDER_CODE || undefined,
  };
}
```

- [ ] **Step 4: Run tests — all pass**

```
npm test -- env.test.ts
```
Expected: 4 passed.

- [ ] **Step 5: Commit**

```
git add profiler-api/acp-v2/src/env.ts profiler-api/acp-v2/tests/env.test.ts
git commit -m "acp-v2: env loader with fail-fast validation"
```

---

## Task 3: Chain mapping (`chain.ts`)

**Files:**
- Test: `profiler-api/acp-v2/tests/chain.test.ts`
- Create: `profiler-api/acp-v2/src/chain.ts`

- [ ] **Step 1: Write failing test**

```typescript
// tests/chain.test.ts
import { describe, it, expect } from "vitest";
import { getChain } from "../src/chain.js";
import { base, baseSepolia } from "viem/chains";

describe("getChain", () => {
  it("returns viem base for 'base'", () => {
    expect(getChain("base")).toBe(base);
  });
  it("returns viem baseSepolia for 'baseSepolia'", () => {
    expect(getChain("baseSepolia")).toBe(baseSepolia);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```
npm test -- chain.test.ts
```
Expected: FAIL.

- [ ] **Step 3: Implement `src/chain.ts`**

```typescript
import { base, baseSepolia, type Chain } from "viem/chains";
import type { ChainName } from "./env.js";

export function getChain(name: ChainName): Chain {
  if (name === "base") return base;
  return baseSepolia;
}
```

- [ ] **Step 4: Run tests — all pass**

```
npm test -- chain.test.ts
```
Expected: 2 passed.

- [ ] **Step 5: Commit**

```
git add profiler-api/acp-v2/src/chain.ts profiler-api/acp-v2/tests/chain.test.ts
git commit -m "acp-v2: chain name → viem Chain mapping"
```

---

## Task 4: Provider adapter (`provider.ts`)

**Files:**
- Create: `profiler-api/acp-v2/src/provider.ts`

> Note: this file is a thin wrapper around the SDK's `PrivyAlchemyEvmProviderAdapter.create(...)` — we do not unit-test it because mocking Privy+Alchemy adds no signal. It's exercised by the integration smoke test in Task 20.

- [ ] **Step 1: Implement `src/provider.ts`**

```typescript
import { PrivyAlchemyEvmProviderAdapter } from "@virtuals-protocol/acp-node-v2";
import { getChain } from "./chain.js";
import type { AcpEnv } from "./env.js";

export async function createProvider(env: AcpEnv) {
  return await PrivyAlchemyEvmProviderAdapter.create({
    walletAddress: env.walletAddress,
    walletId: env.walletId,
    signerPrivateKey: env.signerPrivateKey,
    chains: [getChain(env.chain)],
    ...(env.builderCode ? { builderCode: env.builderCode } : {}),
  });
}
```

> If `PrivyAlchemyEvmProviderAdapter` is not the export name in the installed SDK version, run `node -e "console.log(Object.keys(require('@virtuals-protocol/acp-node-v2')))"` and update the import accordingly. The migration doc uses this name.

- [ ] **Step 2: Build check**

```
cd profiler-api/acp-v2 && npm run build
```
Expected: exits 0.

- [ ] **Step 3: Commit**

```
git add profiler-api/acp-v2/src/provider.ts
git commit -m "acp-v2: provider adapter factory"
```

---

## Task 5: Shared validators (`validators.ts`)

**Files:**
- Test: `profiler-api/acp-v2/tests/validators.test.ts`
- Create: `profiler-api/acp-v2/src/validators.ts`

- [ ] **Step 1: Write failing test**

```typescript
// tests/validators.test.ts
import { describe, it, expect } from "vitest";
import {
  validateAddress,
  validateEvmAddress,
  validateChain,
  validateTier,
  EVM_CHAINS,
  TIERS,
} from "../src/validators.js";

describe("validateAddress", () => {
  it("accepts hex address", () => {
    expect(validateAddress("0x" + "a".repeat(40))).toEqual({ valid: true });
  });
  it("accepts ENS", () => {
    expect(validateAddress("vitalik.eth")).toEqual({ valid: true });
  });
  it("accepts solana address", () => {
    expect(validateAddress("9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM")).toEqual({ valid: true });
  });
  it("rejects empty", () => {
    expect(validateAddress("")).toEqual({ valid: false, reason: "address is required" });
  });
  it("rejects garbage", () => {
    const res = validateAddress("not-an-address");
    expect(res.valid).toBe(false);
  });
});

describe("validateEvmAddress", () => {
  it("accepts hex", () => {
    expect(validateEvmAddress("0x" + "a".repeat(40))).toEqual({ valid: true });
  });
  it("rejects ENS", () => {
    expect(validateEvmAddress("vitalik.eth").valid).toBe(false);
  });
  it("rejects wrong length", () => {
    expect(validateEvmAddress("0xabc").valid).toBe(false);
  });
});

describe("validateChain", () => {
  it("accepts undefined (default)", () => {
    expect(validateChain(undefined)).toEqual({ valid: true });
  });
  it("accepts known chain", () => {
    expect(validateChain("ethereum")).toEqual({ valid: true });
  });
  it("rejects unknown chain", () => {
    const res = validateChain("fakechain");
    expect(res.valid).toBe(false);
    expect(res.reason).toMatch(/chain must be one of/);
  });
});

describe("validateTier", () => {
  it("accepts basic/standard/premium/free + undefined", () => {
    expect(validateTier(undefined).valid).toBe(true);
    expect(validateTier("free").valid).toBe(true);
    expect(validateTier("basic").valid).toBe(true);
    expect(validateTier("standard").valid).toBe(true);
    expect(validateTier("premium").valid).toBe(true);
  });
  it("rejects unknown", () => {
    expect(validateTier("platinum").valid).toBe(false);
  });
});

describe("constants", () => {
  it("EVM_CHAINS has 7 chains", () => {
    expect(EVM_CHAINS).toHaveLength(7);
  });
  it("TIERS has 4 tiers", () => {
    expect(TIERS).toEqual(["free", "basic", "standard", "premium"]);
  });
});
```

- [ ] **Step 2: Run test — fails**

```
npm test -- validators.test.ts
```

- [ ] **Step 3: Implement `src/validators.ts`**

```typescript
export interface ValidationResult {
  valid: boolean;
  reason?: string;
}

export const EVM_CHAINS = [
  "ethereum",
  "base",
  "arbitrum",
  "polygon",
  "optimism",
  "avalanche",
  "bnb",
] as const;

export const EVM_CHAINS_PLUS_SOLANA = [...EVM_CHAINS, "solana"] as const;

export const TIERS = ["free", "basic", "standard", "premium"] as const;
export type Tier = (typeof TIERS)[number];

const HEX_ADDRESS = /^0x[a-fA-F0-9]{40}$/;
const SOLANA_ADDRESS = /^[1-9A-HJ-NP-Za-km-z]{32,44}$/;

export function validateAddress(raw: unknown): ValidationResult {
  if (typeof raw !== "string" || raw.length === 0) {
    return { valid: false, reason: "address is required" };
  }
  const addr = raw.trim();
  if (HEX_ADDRESS.test(addr)) return { valid: true };
  if (addr.endsWith(".eth")) return { valid: true };
  if (SOLANA_ADDRESS.test(addr)) return { valid: true };
  return { valid: false, reason: `invalid address format: ${addr}` };
}

export function validateEvmAddress(raw: unknown): ValidationResult {
  if (typeof raw !== "string" || raw.length === 0) {
    return { valid: false, reason: "address is required" };
  }
  const addr = raw.trim();
  if (HEX_ADDRESS.test(addr)) return { valid: true };
  return { valid: false, reason: `invalid EVM address: ${addr}` };
}

export function validateChain(
  raw: unknown,
  allowed: readonly string[] = EVM_CHAINS
): ValidationResult {
  if (raw === undefined || raw === null) return { valid: true };
  if (typeof raw !== "string" || !allowed.includes(raw)) {
    return { valid: false, reason: `chain must be one of: ${allowed.join(", ")}` };
  }
  return { valid: true };
}

export function validateTier(raw: unknown): ValidationResult {
  if (raw === undefined || raw === null) return { valid: true };
  if (typeof raw !== "string" || !TIERS.includes(raw as Tier)) {
    return { valid: false, reason: `tier must be one of: ${TIERS.join(", ")}` };
  }
  return { valid: true };
}
```

- [ ] **Step 4: Tests pass**

```
npm test -- validators.test.ts
```
Expected: all pass.

- [ ] **Step 5: Commit**

```
git add profiler-api/acp-v2/src/validators.ts profiler-api/acp-v2/tests/validators.test.ts
git commit -m "acp-v2: shared request validators"
```

---

## Task 6: ProfilerClient (`profilerClient.ts`)

**Files:**
- Test: `profiler-api/acp-v2/tests/profilerClient.test.ts`
- Create: `profiler-api/acp-v2/src/profilerClient.ts`

- [ ] **Step 1: Write failing test**

```typescript
// tests/profilerClient.test.ts
import { describe, it, expect, vi, beforeEach } from "vitest";
import { createProfilerClient } from "../src/profilerClient.js";

const BASE = "http://profiler-api:5000";

function mockFetch(response: { ok: boolean; status?: number; body: unknown }) {
  const fn = vi.fn(async () => ({
    ok: response.ok,
    status: response.status ?? (response.ok ? 200 : 500),
    text: async () => (typeof response.body === "string" ? response.body : JSON.stringify(response.body)),
    json: async () => response.body,
  }));
  globalThis.fetch = fn as unknown as typeof fetch;
  return fn;
}

describe("profilerClient", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it("profile() POSTs to /profile", async () => {
    const fetchFn = mockFetch({ ok: true, body: { score: 80 } });
    const client = createProfilerClient(BASE);
    const res = await client.profile({ address: "0xabc", chain: "ethereum", tier: "standard" });
    expect(res).toEqual({ score: 80 });
    expect(fetchFn).toHaveBeenCalledWith(
      `${BASE}/profile`,
      expect.objectContaining({
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ address: "0xabc", chain: "ethereum", tier: "standard" }),
      })
    );
  });

  it("profileBatch() POSTs to /profile/batch", async () => {
    const fetchFn = mockFetch({ ok: true, body: [{ a: 1 }, { a: 2 }] });
    const client = createProfilerClient(BASE);
    const res = await client.profileBatch({ addresses: ["0x1", "0x2"], chain: "base", tier: "standard" });
    expect(res).toEqual([{ a: 1 }, { a: 2 }]);
    const call = fetchFn.mock.calls[0];
    expect(call[0]).toBe(`${BASE}/profile/batch`);
  });

  it("get() adds query params when provided", async () => {
    const fetchFn = mockFetch({ ok: true, body: { ok: true } });
    const client = createProfilerClient(BASE);
    await client.get("/trust/0xabc", { chain: "ethereum" });
    expect(fetchFn.mock.calls[0][0]).toBe(`${BASE}/trust/0xabc?chain=ethereum`);
  });

  it("throws on non-2xx with status + body", async () => {
    mockFetch({ ok: false, status: 500, body: "server blew up" });
    const client = createProfilerClient(BASE);
    await expect(client.get("/gas/0xabc")).rejects.toThrow(/500.*server blew up/);
  });

  it("storeDeliverable() POSTs JSON and returns id + url", async () => {
    const fetchFn = mockFetch({ ok: true, body: { id: "uuid-1", url: "http://host/deliverables/uuid-1" } });
    const client = createProfilerClient(BASE);
    const res = await client.storeDeliverable("job-123", { big: "payload" });
    expect(res).toEqual({ id: "uuid-1", url: "http://host/deliverables/uuid-1" });
    expect(fetchFn.mock.calls[0][0]).toBe(`${BASE}/deliverables`);
  });
});
```

- [ ] **Step 2: Run test — fails**

- [ ] **Step 3: Implement `src/profilerClient.ts`**

```typescript
export interface ProfileRequest {
  address: string;
  chain?: string;
  tier?: string;
}

export interface BatchRequest {
  addresses: string[];
  chain?: string;
  tier?: string;
}

export interface MultiChainRequest {
  address: string;
  chains?: string[];
  tier?: string;
}

export interface CompareRequest {
  addresses: string[];
  chain?: string;
  tier?: string;
}

export interface StoredDeliverable {
  id: string;
  url: string;
}

export interface ProfilerClient {
  profile(req: ProfileRequest): Promise<unknown>;
  profileBatch(req: BatchRequest): Promise<unknown>;
  profileMultiChain(req: MultiChainRequest): Promise<unknown>;
  compare(req: CompareRequest): Promise<unknown>;
  get(path: string, query?: Record<string, string | number | undefined>): Promise<unknown>;
  storeDeliverable(jobId: string, payload: unknown): Promise<StoredDeliverable>;
}

export function createProfilerClient(baseUrl: string, timeoutMs = 60_000): ProfilerClient {
  async function post<T>(path: string, body: unknown): Promise<T> {
    const ctl = new AbortController();
    const timer = setTimeout(() => ctl.abort(), timeoutMs);
    try {
      const res = await fetch(`${baseUrl}${path}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
        signal: ctl.signal,
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(`profiler-api ${res.status}: ${text}`);
      }
      return (await res.json()) as T;
    } finally {
      clearTimeout(timer);
    }
  }

  async function get<T>(
    path: string,
    query?: Record<string, string | number | undefined>
  ): Promise<T> {
    const qs = query
      ? "?" +
        Object.entries(query)
          .filter(([, v]) => v !== undefined && v !== "")
          .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
          .join("&")
      : "";
    const ctl = new AbortController();
    const timer = setTimeout(() => ctl.abort(), timeoutMs);
    try {
      const res = await fetch(`${baseUrl}${path}${qs}`, { signal: ctl.signal });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(`profiler-api ${res.status}: ${text}`);
      }
      return (await res.json()) as T;
    } finally {
      clearTimeout(timer);
    }
  }

  return {
    profile: (req) => post("/profile", req),
    profileBatch: (req) => post("/profile/batch", req),
    profileMultiChain: (req) => post("/profile/multi-chain", req),
    compare: (req) => post("/compare", req),
    get: (path, query) => get(path, query),
    storeDeliverable: (jobId, payload) =>
      post<StoredDeliverable>("/deliverables", { jobId, payload }),
  };
}
```

- [ ] **Step 4: Tests pass**

- [ ] **Step 5: Commit**

```
git add profiler-api/acp-v2/src/profilerClient.ts profiler-api/acp-v2/tests/profilerClient.test.ts
git commit -m "acp-v2: typed HTTP client for C# profiler API"
```

---

## Task 7: Offering interface (`offerings/types.ts`)

**Files:**
- Create: `profiler-api/acp-v2/src/offerings/types.ts`

- [ ] **Step 1: Write file**

```typescript
import type { ValidationResult } from "../validators.js";
import type { ProfilerClient } from "../profilerClient.js";

export interface OfferingContext {
  client: ProfilerClient;
}

export interface Offering {
  name: string;
  description: string;
  requirementSchema: Record<string, unknown>;
  validate(req: Record<string, unknown>): ValidationResult;
  execute(req: Record<string, unknown>, ctx: OfferingContext): Promise<unknown>;
}
```

- [ ] **Step 2: Build check**

```
cd profiler-api/acp-v2 && npx tsc --noEmit
```
Expected: exits 0.

- [ ] **Step 3: Commit**

```
git add profiler-api/acp-v2/src/offerings/types.ts
git commit -m "acp-v2: Offering interface"
```

---

## Task 8: Offerings batch 1 — `walletprofiler`, `quickcheck`, `walletstatus`, `riskscore`

**Files:**
- Create: `profiler-api/acp-v2/src/offerings/walletprofiler.ts`
- Create: `profiler-api/acp-v2/src/offerings/quickcheck.ts`
- Create: `profiler-api/acp-v2/src/offerings/walletstatus.ts`
- Create: `profiler-api/acp-v2/src/offerings/riskscore.ts`
- Test: `profiler-api/acp-v2/tests/offerings/walletprofiler.test.ts`
- Test: `profiler-api/acp-v2/tests/offerings/quickcheck.test.ts`
- Test: `profiler-api/acp-v2/tests/offerings/walletstatus.test.ts`

- [ ] **Step 1: Write `walletprofiler` test**

```typescript
// tests/offerings/walletprofiler.test.ts
import { describe, it, expect, vi } from "vitest";
import { walletprofiler } from "../../src/offerings/walletprofiler.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

function mockClient(): { client: ProfilerClient; profile: any; profileBatch: any } {
  const profile = vi.fn(async () => ({ score: 80 }));
  const profileBatch = vi.fn(async () => [{ score: 80 }, { score: 70 }]);
  const client: ProfilerClient = {
    profile, profileBatch,
    profileMultiChain: vi.fn(), compare: vi.fn(), get: vi.fn(), storeDeliverable: vi.fn(),
  };
  return { client, profile, profileBatch };
}

describe("walletprofiler", () => {
  it("validates: rejects missing address", () => {
    expect(walletprofiler.validate({}).valid).toBe(false);
  });
  it("validates: rejects unknown chain", () => {
    expect(walletprofiler.validate({ address: "0x" + "a".repeat(40), chain: "fake" }).valid).toBe(false);
  });
  it("validates: rejects unknown tier", () => {
    expect(walletprofiler.validate({ address: "0x" + "a".repeat(40), tier: "platinum" }).valid).toBe(false);
  });
  it("validates: accepts comma-separated addresses", () => {
    const a = "0x" + "a".repeat(40);
    const b = "0x" + "b".repeat(40);
    expect(walletprofiler.validate({ address: `${a},${b}` }).valid).toBe(true);
  });

  it("executes single profile", async () => {
    const { client, profile } = mockClient();
    await walletprofiler.execute({ address: "0xabc", chain: "ethereum" }, { client });
    expect(profile).toHaveBeenCalledWith({ address: "0xabc", chain: "ethereum", tier: "standard" });
  });
  it("executes batch for comma-separated", async () => {
    const { client, profileBatch } = mockClient();
    await walletprofiler.execute({ address: "0xabc,0xdef" }, { client });
    expect(profileBatch).toHaveBeenCalledWith({
      addresses: ["0xabc", "0xdef"],
      chain: "ethereum",
      tier: "standard",
    });
  });
});
```

- [ ] **Step 2: Write `src/offerings/walletprofiler.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress, validateChain, validateTier, EVM_CHAINS } from "../validators.js";

function splitAddresses(raw: string): string[] {
  return raw.split(",").map((s) => s.trim()).filter(Boolean);
}

export const walletprofiler: Offering = {
  name: "walletprofiler",
  description:
    "Comprehensive on-chain wallet profiling (~5s response). Token holdings with USD valuations, NFT portfolio with floor prices, DeFi positions (Aave, Compound), risk scoring, OFAC sanctions screening, smart money classification, MEV exposure detection, approval risk scan, and portfolio quality grading. Wallet due diligence, counterparty risk, AML compliance, and fraud detection. Batch profiling up to 50 wallets. Supports 7 EVM chains.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name. Comma-separated for batch." },
      chain: { type: "string", enum: EVM_CHAINS as unknown as string[], description: "Target chain (defaults to ethereum)" },
      tier: { type: "string", enum: ["basic", "standard"], description: "basic or standard. Default: standard." },
    },
    required: ["address"],
  },
  validate(req) {
    const addrRaw = req.address;
    if (typeof addrRaw !== "string" || addrRaw.length === 0) {
      return { valid: false, reason: "address is required" };
    }
    for (const addr of splitAddresses(addrRaw)) {
      const r = validateAddress(addr);
      if (!r.valid) return r;
    }
    const c = validateChain(req.chain);
    if (!c.valid) return c;
    const t = validateTier(req.tier);
    if (!t.valid) return t;
    return { valid: true };
  },
  async execute(req, { client }) {
    const address = String(req.address);
    const chain = (req.chain as string | undefined) ?? "ethereum";
    const tier = (req.tier as string | undefined) ?? "standard";
    const addresses = splitAddresses(address);
    return addresses.length > 1
      ? await client.profileBatch({ addresses, chain, tier })
      : await client.profile({ address, chain, tier });
  },
};
```

- [ ] **Step 3: Run walletprofiler test — pass**

```
npm test -- walletprofiler.test.ts
```

- [ ] **Step 4: Write `quickcheck` test**

```typescript
// tests/offerings/quickcheck.test.ts
import { describe, it, expect, vi } from "vitest";
import { quickcheck } from "../../src/offerings/quickcheck.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

describe("quickcheck", () => {
  it("validates EVM + solana + ENS", () => {
    expect(quickcheck.validate({ address: "0x" + "a".repeat(40) }).valid).toBe(true);
    expect(quickcheck.validate({ address: "vitalik.eth" }).valid).toBe(true);
    expect(quickcheck.validate({ address: "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM" }).valid).toBe(true);
  });
  it("rejects missing address", () => {
    expect(quickcheck.validate({}).valid).toBe(false);
  });
  it("executes GET /trust/{address}", async () => {
    const get = vi.fn(async () => ({ score: 90 }));
    const client: ProfilerClient = {
      profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain: vi.fn(),
      compare: vi.fn(), get, storeDeliverable: vi.fn(),
    };
    await quickcheck.execute({ address: "0xabc" }, { client });
    expect(get).toHaveBeenCalledWith("/trust/0xabc", { chain: "ethereum" });
  });
});
```

- [ ] **Step 5: Write `src/offerings/quickcheck.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress, validateChain, EVM_CHAINS_PLUS_SOLANA } from "../validators.js";

export const quickcheck: Offering = {
  name: "quickcheck",
  description:
    "Instant wallet trust score and risk assessment (~500ms response). Returns trust score (0-100), trust level, risk flags, native balance, transaction count, ENS name, and token diversity. Fast counterparty due diligence for agent-to-agent commerce, AML pre-screening, and fraud detection. Supports 8 chains: Ethereum, Base, Arbitrum, Polygon, Optimism, Avalanche, BNB, and Solana.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
      chain: { type: "string", enum: EVM_CHAINS_PLUS_SOLANA as unknown as string[], description: "Target chain (defaults to ethereum)" },
    },
    required: ["address"],
  },
  validate(req) {
    const a = validateAddress(req.address);
    if (!a.valid) return a;
    return validateChain(req.chain, EVM_CHAINS_PLUS_SOLANA);
  },
  async execute(req, { client }) {
    const address = String(req.address).trim();
    const chain = (req.chain as string | undefined) ?? "ethereum";
    return await client.get(`/trust/${encodeURIComponent(address)}`, { chain });
  },
};
```

- [ ] **Step 6: Run quickcheck test — pass**

- [ ] **Step 7: Write `walletstatus` test**

```typescript
// tests/offerings/walletstatus.test.ts
import { describe, it, expect, vi } from "vitest";
import { walletstatus } from "../../src/offerings/walletstatus.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

describe("walletstatus", () => {
  it("rejects missing address", () => {
    expect(walletstatus.validate({}).valid).toBe(false);
  });
  it("accepts solana address", () => {
    expect(walletstatus.validate({ address: "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM" }).valid).toBe(true);
  });
  it("executes GET /status/{address}", async () => {
    const get = vi.fn(async () => ({ balance: "1.5" }));
    const client: ProfilerClient = {
      profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain: vi.fn(),
      compare: vi.fn(), get, storeDeliverable: vi.fn(),
    };
    await walletstatus.execute({ address: "0xabc", chain: "base" }, { client });
    expect(get).toHaveBeenCalledWith("/status/0xabc", { chain: "base" });
  });
});
```

- [ ] **Step 8: Write `src/offerings/walletstatus.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress, validateChain, EVM_CHAINS_PLUS_SOLANA } from "../validators.js";

export const walletstatus: Offering = {
  name: "walletstatus",
  description:
    "Ultra-fast wallet status check (~200ms response). Returns native balance, transaction count, and smart contract detection. Cheapest entry point for wallet due diligence and counterparty verification. Ideal for high-volume pre-filtering before full profiling. Supports 8 chains: Ethereum, Base, Arbitrum, Polygon, Optimism, Avalanche, BNB, and Solana.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
      chain: { type: "string", enum: EVM_CHAINS_PLUS_SOLANA as unknown as string[], description: "Target chain (defaults to ethereum)" },
    },
    required: ["address"],
  },
  validate(req) {
    const a = validateAddress(req.address);
    if (!a.valid) return a;
    return validateChain(req.chain, EVM_CHAINS_PLUS_SOLANA);
  },
  async execute(req, { client }) {
    const address = String(req.address).trim();
    const chain = (req.chain as string | undefined) ?? "ethereum";
    return await client.get(`/status/${encodeURIComponent(address)}`, { chain });
  },
};
```

- [ ] **Step 9: Write `src/offerings/riskscore.ts`** (no dedicated test — shape identical to quickcheck; covered by router test)

```typescript
import type { Offering } from "./types.js";
import { validateAddress, validateChain, EVM_CHAINS } from "../validators.js";

export const riskscore: Offering = {
  name: "riskscore",
  description:
    "Wallet risk score and safety assessment (~3s response). Returns risk score (0-100), risk level, verdict (SAFE/CAUTION/WARNING/DANGER), risk flags, OFAC sanctions screening, token approval risk count, and wallet classification tags. Essential for counterparty due diligence, AML compliance, fraud detection, scam checking, and pre-transaction risk assessment. Supports 7 EVM chains: Ethereum, Base, Arbitrum, Polygon, Optimism, Avalanche, BNB.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
      chain: { type: "string", enum: EVM_CHAINS as unknown as string[], description: "Target chain (defaults to ethereum)" },
    },
    required: ["address"],
  },
  validate(req) {
    const a = validateAddress(req.address);
    if (!a.valid) return a;
    return validateChain(req.chain);
  },
  async execute(req, { client }) {
    const address = String(req.address).trim();
    const chain = (req.chain as string | undefined) ?? "ethereum";
    return await client.get(`/risk/${encodeURIComponent(address)}`, { chain });
  },
};
```

- [ ] **Step 10: Run all offering tests — pass**

```
npm test -- offerings
```

- [ ] **Step 11: Commit**

```
git add profiler-api/acp-v2/src/offerings/walletprofiler.ts profiler-api/acp-v2/src/offerings/quickcheck.ts profiler-api/acp-v2/src/offerings/walletstatus.ts profiler-api/acp-v2/src/offerings/riskscore.ts profiler-api/acp-v2/tests/offerings/walletprofiler.test.ts profiler-api/acp-v2/tests/offerings/quickcheck.test.ts profiler-api/acp-v2/tests/offerings/walletstatus.test.ts
git commit -m "acp-v2: offerings batch 1 (walletprofiler, quickcheck, walletstatus, riskscore)"
```

---

## Task 9: Offerings batch 2 — `multichain`, `walletcompare`, `deepanalysis`

**Files:**
- Create: `profiler-api/acp-v2/src/offerings/multichain.ts`
- Create: `profiler-api/acp-v2/src/offerings/walletcompare.ts`
- Create: `profiler-api/acp-v2/src/offerings/deepanalysis.ts`
- Test: `profiler-api/acp-v2/tests/offerings/multichain.test.ts`
- Test: `profiler-api/acp-v2/tests/offerings/walletcompare.test.ts`
- Test: `profiler-api/acp-v2/tests/offerings/deepanalysis.test.ts`

- [ ] **Step 1: Write `multichain` test**

```typescript
// tests/offerings/multichain.test.ts
import { describe, it, expect, vi } from "vitest";
import { multichain } from "../../src/offerings/multichain.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

describe("multichain", () => {
  it("rejects missing address", () => {
    expect(multichain.validate({}).valid).toBe(false);
  });
  it("accepts default (no chains)", () => {
    expect(multichain.validate({ address: "0x" + "a".repeat(40) }).valid).toBe(true);
  });
  it("rejects invalid chain in chains array", () => {
    expect(multichain.validate({ address: "0x" + "a".repeat(40), chains: ["ethereum", "fake"] }).valid).toBe(false);
  });
  it("rejects more than 5 chains", () => {
    const six = ["ethereum", "base", "arbitrum", "polygon", "optimism", "avalanche"];
    expect(multichain.validate({ address: "0x" + "a".repeat(40), chains: six }).valid).toBe(false);
  });
  it("executes with default tier=standard and all chains", async () => {
    const profileMultiChain = vi.fn(async () => ({ chains: {} }));
    const client: ProfilerClient = {
      profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain,
      compare: vi.fn(), get: vi.fn(), storeDeliverable: vi.fn(),
    };
    await multichain.execute({ address: "0xabc" }, { client });
    const call = profileMultiChain.mock.calls[0][0];
    expect(call.address).toBe("0xabc");
    expect(call.tier).toBe("standard");
    expect(call.chains).toEqual(["ethereum", "base", "arbitrum", "polygon", "optimism", "avalanche", "bnb"]);
  });
});
```

- [ ] **Step 2: Write `src/offerings/multichain.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress, validateTier, EVM_CHAINS } from "../validators.js";

const ALL_CHAINS = [...EVM_CHAINS];

export const multichain: Offering = {
  name: "multichain",
  description:
    "Multi-chain wallet profile — profiles a wallet across up to 5 EVM chains in a single request. Returns per-chain balances, tokens, DeFi positions, risk scores, and aggregated total portfolio value. Ideal for cross-chain portfolio analysis.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
      chains: {
        type: "array",
        items: { type: "string", enum: [...EVM_CHAINS] as unknown as string[] },
        description: "Chains to profile (max 5). Defaults to all if omitted.",
      },
      tier: { type: "string", enum: ["basic", "standard", "premium"], description: "Profile tier. Default: standard" },
    },
    required: ["address"],
  },
  validate(req) {
    const a = validateAddress(req.address);
    if (!a.valid) return a;
    if (req.chains !== undefined) {
      if (!Array.isArray(req.chains)) return { valid: false, reason: "chains must be an array" };
      if (req.chains.length > 5) return { valid: false, reason: "chains must be 5 or fewer" };
      for (const c of req.chains) {
        if (typeof c !== "string" || !EVM_CHAINS.includes(c as (typeof EVM_CHAINS)[number])) {
          return { valid: false, reason: `chain must be one of: ${EVM_CHAINS.join(", ")}` };
        }
      }
    }
    return validateTier(req.tier);
  },
  async execute(req, { client }) {
    const address = String(req.address);
    const chains = (req.chains as string[] | undefined) ?? ALL_CHAINS;
    const tier = (req.tier as string | undefined) ?? "standard";
    return await client.profileMultiChain({ address, chains, tier });
  },
};
```

- [ ] **Step 3: Run multichain test — pass**

- [ ] **Step 4: Write `walletcompare` test**

```typescript
// tests/offerings/walletcompare.test.ts
import { describe, it, expect, vi } from "vitest";
import { walletcompare } from "../../src/offerings/walletcompare.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

describe("walletcompare", () => {
  it("rejects fewer than 2 addresses", () => {
    expect(walletcompare.validate({ addresses: ["0x" + "a".repeat(40)] }).valid).toBe(false);
  });
  it("rejects more than 10 addresses", () => {
    const addrs = Array.from({ length: 11 }, (_, i) => "0x" + String(i).padStart(40, "0"));
    expect(walletcompare.validate({ addresses: addrs }).valid).toBe(false);
  });
  it("rejects non-array", () => {
    expect(walletcompare.validate({ addresses: "0xabc,0xdef" }).valid).toBe(false);
  });
  it("executes compare()", async () => {
    const compare = vi.fn(async () => ({ result: true }));
    const client: ProfilerClient = {
      profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain: vi.fn(),
      compare, get: vi.fn(), storeDeliverable: vi.fn(),
    };
    const a = "0x" + "a".repeat(40);
    const b = "0x" + "b".repeat(40);
    await walletcompare.execute({ addresses: [a, b] }, { client });
    expect(compare).toHaveBeenCalledWith({ addresses: [a, b], chain: "ethereum", tier: "standard" });
  });
});
```

- [ ] **Step 5: Write `src/offerings/walletcompare.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress, validateChain, EVM_CHAINS } from "../validators.js";

export const walletcompare: Offering = {
  name: "walletcompare",
  description:
    "Compare 2-10 wallets side by side — returns comparative analysis including portfolio overlap, common tokens, risk comparison, balance rankings, and similarity scores. Great for fund analysis, DAO treasury comparison, or identifying related wallets.",
  requirementSchema: {
    type: "object",
    properties: {
      addresses: { type: "array", items: { type: "string" }, minItems: 2, maxItems: 10 },
      chain: { type: "string", enum: EVM_CHAINS as unknown as string[] },
      tier: { type: "string", enum: ["basic", "standard"], description: "Profile tier. Default: standard" },
    },
    required: ["addresses"],
  },
  validate(req) {
    const addrs = req.addresses;
    if (!Array.isArray(addrs) || addrs.length < 2) {
      return { valid: false, reason: "At least 2 addresses are required" };
    }
    if (addrs.length > 10) return { valid: false, reason: "Maximum 10 addresses per comparison" };
    for (const a of addrs) {
      const r = validateAddress(a);
      if (!r.valid) return r;
    }
    return validateChain(req.chain);
  },
  async execute(req, { client }) {
    const addresses = req.addresses as string[];
    const chain = (req.chain as string | undefined) ?? "ethereum";
    const tier = (req.tier as string | undefined) ?? "standard";
    return await client.compare({ addresses, chain, tier });
  },
};
```

- [ ] **Step 6: Write `deepanalysis` test**

```typescript
// tests/offerings/deepanalysis.test.ts
import { describe, it, expect, vi } from "vitest";
import { deepanalysis } from "../../src/offerings/deepanalysis.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

function client(): { c: ProfilerClient; batch: any; compare: any; multi: any } {
  const batch = vi.fn(async () => [{}, {}]);
  const compare = vi.fn(async () => ({ overlap: 0.5 }));
  const multi = vi.fn(async () => ({ chains: {} }));
  return {
    c: { profile: vi.fn(), profileBatch: batch, profileMultiChain: multi, compare, get: vi.fn(), storeDeliverable: vi.fn() },
    batch, compare, multi,
  };
}

describe("deepanalysis", () => {
  it("accepts 'all' as chain", () => {
    expect(deepanalysis.validate({ address: "0x" + "a".repeat(40), chain: "all" }).valid).toBe(true);
  });
  it("rejects invalid chain", () => {
    expect(deepanalysis.validate({ address: "0x" + "a".repeat(40), chain: "fake" }).valid).toBe(false);
  });
  it("single address + chain=all → multichain premium", async () => {
    const { c, multi } = client();
    await deepanalysis.execute({ address: "0xabc", chain: "all" }, { client: c });
    expect(multi).toHaveBeenCalledWith({ address: "0xabc", chains: expect.any(Array), tier: "premium" });
  });
  it("single address + specific chain → profile premium", async () => {
    const { c } = client();
    const spy = c.profile as any;
    spy.mockResolvedValueOnce({ score: 99 });
    await deepanalysis.execute({ address: "0xabc", chain: "ethereum" }, { client: c });
    expect(spy).toHaveBeenCalledWith({ address: "0xabc", chain: "ethereum", tier: "premium" });
  });
  it("multi address (2-10) → batch + compare", async () => {
    const { c, batch, compare } = client();
    const a = "0x" + "a".repeat(40);
    const b = "0x" + "b".repeat(40);
    await deepanalysis.execute({ address: `${a},${b}` }, { client: c });
    expect(batch).toHaveBeenCalled();
    expect(compare).toHaveBeenCalled();
  });
});
```

- [ ] **Step 7: Write `src/offerings/deepanalysis.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress, EVM_CHAINS } from "../validators.js";

const ALL_CHAINS = [...EVM_CHAINS];

function splitAddresses(raw: string): string[] {
  return raw.split(",").map((s) => s.trim()).filter(Boolean);
}

export const deepanalysis: Offering = {
  name: "deepanalysis",
  description:
    "Premium deep wallet analysis with cross-chain aggregation across all 7 EVM chains (~15s response). Full token holdings (50+), USD valuations, DeFi positions (Aave, Compound), NFT portfolio with floor prices, OFAC sanctions screening, smart money classification, MEV exposure, approval risk scan, social identity, reputation badge, and AI-generated summary. Multi-address batch includes automatic wallet comparison with common tokens, leader identification, and unique insights. Comprehensive wallet due diligence, AML compliance, counterparty risk assessment, and fraud detection. Set chain to 'all' or omit for cross-chain profiling.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address or ENS name. Comma-separated for batch." },
      chain: { type: "string", enum: [...EVM_CHAINS, "all"] as unknown as string[] },
    },
    required: ["address"],
  },
  validate(req) {
    const addrRaw = req.address;
    if (typeof addrRaw !== "string" || addrRaw.length === 0) {
      return { valid: false, reason: "address is required" };
    }
    for (const a of splitAddresses(addrRaw)) {
      const r = validateAddress(a);
      if (!r.valid) return r;
    }
    const chain = req.chain as string | undefined;
    if (chain !== undefined && chain !== "all" && !EVM_CHAINS.includes(chain as (typeof EVM_CHAINS)[number])) {
      return { valid: false, reason: `chain must be one of: ${EVM_CHAINS.join(", ")}, or "all"` };
    }
    return { valid: true };
  },
  async execute(req, { client }) {
    const addressRaw = String(req.address);
    const chain = (req.chain as string | undefined) ?? "all";
    const addresses = splitAddresses(addressRaw);

    if (addresses.length > 1) {
      const targetChain = chain === "all" ? "ethereum" : chain;
      const batch = await client.profileBatch({ addresses, chain: targetChain, tier: "premium" });
      let comparison: unknown = null;
      if (addresses.length >= 2 && addresses.length <= 10) {
        try {
          comparison = await client.compare({ addresses, chain: targetChain, tier: "premium" });
        } catch {
          /* comparison is optional */
        }
      }
      return { batch, comparison };
    }

    if (chain === "all") {
      return await client.profileMultiChain({ address: addressRaw, chains: ALL_CHAINS, tier: "premium" });
    }
    return await client.profile({ address: addressRaw, chain, tier: "premium" });
  },
};
```

- [ ] **Step 8: Run all offering tests — pass**

```
npm test -- offerings
```

- [ ] **Step 9: Commit**

```
git add profiler-api/acp-v2/src/offerings/multichain.ts profiler-api/acp-v2/src/offerings/walletcompare.ts profiler-api/acp-v2/src/offerings/deepanalysis.ts profiler-api/acp-v2/tests/offerings/multichain.test.ts profiler-api/acp-v2/tests/offerings/walletcompare.test.ts profiler-api/acp-v2/tests/offerings/deepanalysis.test.ts
git commit -m "acp-v2: offerings batch 2 (multichain, walletcompare, deepanalysis)"
```

---

## Task 10: Offerings batch 3 — simple GET proxies

These 11 offerings share the pattern: validate address (EVM only, via `validateEvmAddress` for contract cases, `validateAddress` otherwise), then `client.get(path, query?)`. No dedicated unit tests — the shape is identical and `validators.test.ts` already covers address validation; each offering is exercised end-to-end in the integration smoke test.

**Files:**
- Create: `profiler-api/acp-v2/src/offerings/aianalyze.ts`
- Create: `profiler-api/acp-v2/src/offerings/airdrops.ts`
- Create: `profiler-api/acp-v2/src/offerings/approvalaudit.ts`
- Create: `profiler-api/acp-v2/src/offerings/gasspend.ts`
- Create: `profiler-api/acp-v2/src/offerings/identity.ts`
- Create: `profiler-api/acp-v2/src/offerings/liquidationrisk.ts`
- Create: `profiler-api/acp-v2/src/offerings/lppositions.ts`
- Create: `profiler-api/acp-v2/src/offerings/pnl.ts`
- Create: `profiler-api/acp-v2/src/offerings/rebalance.ts`
- Create: `profiler-api/acp-v2/src/offerings/reputation.ts`
- Create: `profiler-api/acp-v2/src/offerings/virtualsintel.ts`

- [ ] **Step 1: Write `src/offerings/aianalyze.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const aianalyze: Offering = {
  name: "aianalyze",
  description:
    "AI-powered wallet analysis using Claude — feeds complete wallet profile into Claude for natural language Q&A. Supports custom questions about risk, strategy, portfolio optimization, and DeFi activity. Returns analysis text, key insights, and recommendations.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
      question: { type: "string", description: "Natural language question (default: comprehensive analysis)" },
      chain: { type: "string", description: "Chain to query (default: ethereum)" },
    },
    required: ["address"],
  },
  validate(req) {
    return validateAddress(req.address);
  },
  async execute(req, { client }) {
    const address = encodeURIComponent(String(req.address));
    const chain = (req.chain as string | undefined) ?? "ethereum";
    const question = req.question as string | undefined;
    return await client.get(`/ai-analyze/${address}`, { chain, question });
  },
};
```

- [ ] **Step 2: Write `src/offerings/airdrops.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const airdrops: Offering = {
  name: "airdrops",
  description:
    "Airdrop eligibility checker — evaluates wallet against criteria for LayerZero, zkSync, Starknet, Scroll, Linea, EigenLayer, and Pendle. Returns eligibility status (eligible/likely/possible/ineligible), criteria breakdown, and evidence for each protocol. Critical for DeFi airdrop farming strategy.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
      chain: { type: "string", description: "Chain to query (default: ethereum)" },
    },
    required: ["address"],
  },
  validate(req) {
    return validateAddress(req.address);
  },
  async execute(req, { client }) {
    const address = encodeURIComponent(String(req.address));
    const chain = (req.chain as string | undefined) ?? "ethereum";
    return await client.get(`/airdrops/${address}`, { chain });
  },
};
```

- [ ] **Step 3: Write `src/offerings/approvalaudit.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const approvalaudit: Offering = {
  name: "approvalaudit",
  description:
    "Token approval security audit — scans a wallet's outstanding ERC-20 and ERC-721 approvals, flags risky unlimited approvals, identifies suspicious spender contracts, and provides revocation recommendations. Essential for wallet security hygiene.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
    },
    required: ["address"],
  },
  validate(req) {
    return validateAddress(req.address);
  },
  async execute(req, { client }) {
    const address = String(req.address);
    const profile = await client.get(`/profile`, { address, tier: "standard" }) as {
      approvalRisk?: { totalApprovals?: number; riskyApprovals?: number } | null;
      revokeAdvice?: unknown;
    };
    return {
      address,
      approvalRisk: profile.approvalRisk ?? null,
      revokeAdvice: profile.revokeAdvice ?? null,
      summary: profile.approvalRisk
        ? `Found ${profile.approvalRisk.totalApprovals ?? 0} approvals, ${profile.approvalRisk.riskyApprovals ?? 0} flagged as risky`
        : "No approval data available",
    };
  },
};
```

> Note: v1 of this handler calls `/profile?address=...&tier=standard` via GET. Confirm `profile` supports GET with query args; if not, switch to `client.profile({ address, tier: "standard" })` (POST).

- [ ] **Step 4: Write `src/offerings/gasspend.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const gasspend: Offering = {
  name: "gasspend",
  description:
    "Gas spending analysis — calculates total gas spent, average gas price, monthly breakdown, and top 5 most expensive transactions for a wallet. Useful for cost optimization, tax reporting, and understanding on-chain activity patterns.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
    },
    required: ["address"],
  },
  validate(req) {
    return validateAddress(req.address);
  },
  async execute(req, { client }) {
    const address = encodeURIComponent(String(req.address));
    return await client.get(`/gas/${address}`);
  },
};
```

- [ ] **Step 5: Write `src/offerings/identity.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const identity: Offering = {
  name: "identity",
  description:
    "Social identity resolution — resolves a wallet's on-chain identity including ENS name, social profiles, on-chain reputation signals, and identity confidence score. Useful for KYC-lite checks and verifying wallet ownership.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
    },
    required: ["address"],
  },
  validate(req) {
    return validateAddress(req.address);
  },
  async execute(req, { client }) {
    const address = encodeURIComponent(String(req.address));
    return await client.get(`/identity/${address}`);
  },
};
```

- [ ] **Step 6: Write `src/offerings/liquidationrisk.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const liquidationrisk: Offering = {
  name: "liquidationrisk",
  description:
    "Liquidation risk monitoring — checks Aave V3 health factor and Compound V3 borrow balance. Returns health factor, risk level (safe/watch/warning/danger), collateral and debt values, and alerts. Critical for lending protocol risk management and DeFi safety monitoring.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
      chain: { type: "string", description: "Chain to query (default: ethereum)" },
    },
    required: ["address"],
  },
  validate(req) {
    return validateAddress(req.address);
  },
  async execute(req, { client }) {
    const address = encodeURIComponent(String(req.address));
    const chain = (req.chain as string | undefined) ?? "ethereum";
    return await client.get(`/liquidation-risk/${address}`, { chain });
  },
};
```

- [ ] **Step 7: Write `src/offerings/lppositions.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const lppositions: Offering = {
  name: "lppositions",
  description:
    "Uniswap V3 LP position detection — reads NonfungiblePositionManager to discover liquidity positions. Returns token pairs, fee tiers, liquidity amounts, uncollected fees, in-range status, and position status. Essential for DeFi portfolio tracking and yield monitoring.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
      chain: { type: "string", description: "Chain to query (default: ethereum)" },
    },
    required: ["address"],
  },
  validate(req) {
    return validateAddress(req.address);
  },
  async execute(req, { client }) {
    const address = encodeURIComponent(String(req.address));
    const chain = (req.chain as string | undefined) ?? "ethereum";
    return await client.get(`/lp-positions/${address}`, { chain });
  },
};
```

- [ ] **Step 8: Write `src/offerings/pnl.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const pnl: Offering = {
  name: "pnl",
  description:
    "P&L tracking with FIFO cost basis — calculates realized and unrealized profit/loss from transfer history. Returns per-token breakdown, top gainers/losers, cost basis, and P&L percentage. Essential for trading agents, portfolio tracking, and tax reporting.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
      chain: { type: "string", description: "Chain to query (default: ethereum)" },
    },
    required: ["address"],
  },
  validate(req) {
    return validateAddress(req.address);
  },
  async execute(req, { client }) {
    const address = encodeURIComponent(String(req.address));
    const chain = (req.chain as string | undefined) ?? "ethereum";
    return await client.get(`/pnl/${address}`, { chain });
  },
};
```

- [ ] **Step 9: Write `src/offerings/rebalance.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const rebalance: Offering = {
  name: "rebalance",
  description:
    "Portfolio rebalancing suggestions — scores wallet against 5 model portfolios (conservative, balanced, growth, yield-farmer, degen). Returns fit scores, allocation percentages, and specific rebalance actions with suggested tokens. Essential for portfolio optimization and risk management.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
      chain: { type: "string", description: "Chain to query (default: ethereum)" },
      portfolio: { type: "string", description: "Specific model portfolio to compare against. Omit for all." },
    },
    required: ["address"],
  },
  validate(req) {
    return validateAddress(req.address);
  },
  async execute(req, { client }) {
    const address = encodeURIComponent(String(req.address));
    const chain = (req.chain as string | undefined) ?? "ethereum";
    const portfolio = req.portfolio as string | undefined;
    return await client.get(`/rebalance/${address}`, { chain, portfolio });
  },
};
```

- [ ] **Step 10: Write `src/offerings/reputation.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const reputation: Offering = {
  name: "reputation",
  description:
    "Wallet reputation badge — generates a comprehensive reputation score and badge based on wallet history, holdings, trust score, and on-chain activity. Returns a badge tier (Bronze/Silver/Gold/Platinum/Diamond), reputation score, and achievement list.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
    },
    required: ["address"],
  },
  validate(req) {
    return validateAddress(req.address);
  },
  async execute(req, { client }) {
    const address = encodeURIComponent(String(req.address));
    return await client.get(`/reputation/${address}`);
  },
};
```

- [ ] **Step 11: Write `src/offerings/virtualsintel.ts`**

```typescript
import type { Offering } from "./types.js";

export const virtualsintel: Offering = {
  name: "virtualsintel",
  description:
    "Virtuals Protocol ecosystem intelligence (~2s response, cached). Returns $VIRTUAL token price, market cap, 24h volume, price changes. Tracks top AI agent tokens: $AIXBT, $GAME, $LUNA, $VADER, $SEKOIA, $AIMONICA. Includes ecosystem total market cap, 24h volume, health sentiment, and natural language summary.",
  requirementSchema: {
    type: "object",
    properties: {
      query: { type: "string", description: "Optional query about the Virtuals ecosystem." },
    },
    required: [],
  },
  validate() {
    return { valid: true };
  },
  async execute(req, { client }) {
    const query = req.query as string | undefined;
    return await client.get(`/virtuals/ecosystem`, query ? { query } : undefined);
  },
};
```

- [ ] **Step 12: Build check**

```
cd profiler-api/acp-v2 && npx tsc --noEmit
```
Expected: exits 0.

- [ ] **Step 13: Commit**

```
git add profiler-api/acp-v2/src/offerings/aianalyze.ts profiler-api/acp-v2/src/offerings/airdrops.ts profiler-api/acp-v2/src/offerings/approvalaudit.ts profiler-api/acp-v2/src/offerings/gasspend.ts profiler-api/acp-v2/src/offerings/identity.ts profiler-api/acp-v2/src/offerings/liquidationrisk.ts profiler-api/acp-v2/src/offerings/lppositions.ts profiler-api/acp-v2/src/offerings/pnl.ts profiler-api/acp-v2/src/offerings/rebalance.ts profiler-api/acp-v2/src/offerings/reputation.ts profiler-api/acp-v2/src/offerings/virtualsintel.ts
git commit -m "acp-v2: offerings batch 3 (11 simple GET proxies)"
```

---

## Task 11: Offerings batch 4 — token/whales with custom schemas

**Files:**
- Test: `profiler-api/acp-v2/tests/offerings/tokenholders.test.ts`
- Test: `profiler-api/acp-v2/tests/offerings/tokenscreen.test.ts`
- Test: `profiler-api/acp-v2/tests/offerings/portfoliohistory.test.ts`
- Test: `profiler-api/acp-v2/tests/offerings/whalealerts.test.ts`
- Create: `profiler-api/acp-v2/src/offerings/tokenholders.ts`
- Create: `profiler-api/acp-v2/src/offerings/tokenscreen.ts`
- Create: `profiler-api/acp-v2/src/offerings/portfoliohistory.ts`
- Create: `profiler-api/acp-v2/src/offerings/whalealerts.ts`

- [ ] **Step 1: Write `tokenholders` test**

```typescript
// tests/offerings/tokenholders.test.ts
import { describe, it, expect, vi } from "vitest";
import { tokenholders } from "../../src/offerings/tokenholders.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

describe("tokenholders", () => {
  it("requires contract", () => {
    expect(tokenholders.validate({}).valid).toBe(false);
  });
  it("rejects non-hex contract", () => {
    expect(tokenholders.validate({ contract: "not-hex" }).valid).toBe(false);
  });
  it("executes GET /token/{contract}/holders", async () => {
    const get = vi.fn(async () => ({ holders: [] }));
    const client: ProfilerClient = {
      profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain: vi.fn(),
      compare: vi.fn(), get, storeDeliverable: vi.fn(),
    };
    const contract = "0x" + "c".repeat(40);
    await tokenholders.execute({ contract, chain: "base" }, { client });
    expect(get).toHaveBeenCalledWith(`/token/${contract}/holders`, { chain: "base" });
  });
});
```

- [ ] **Step 2: Write `src/offerings/tokenholders.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateEvmAddress, validateChain, EVM_CHAINS } from "../validators.js";

export const tokenholders: Offering = {
  name: "tokenholders",
  description:
    "Token holder concentration analysis for any ERC-20 token (~8s response). Returns top holders with trust scores, holder concentration percentage, wallet age, ENS names, and activity tags. Detect whale concentration, rug pull risk, smart money accumulation, and insider trading patterns. Essential for token due diligence, investment risk assessment, and fraud detection. Supports 7 EVM chains.",
  requirementSchema: {
    type: "object",
    properties: {
      contract: { type: "string", description: "ERC-20 token contract address (0x...)" },
      chain: { type: "string", enum: EVM_CHAINS as unknown as string[] },
    },
    required: ["contract"],
  },
  validate(req) {
    if (req.contract === undefined || req.contract === null || req.contract === "") {
      return { valid: false, reason: "contract address is required" };
    }
    const a = validateEvmAddress(req.contract);
    if (!a.valid) return a;
    return validateChain(req.chain);
  },
  async execute(req, { client }) {
    const contract = String(req.contract).trim();
    const chain = (req.chain as string | undefined) ?? "ethereum";
    return await client.get(`/token/${encodeURIComponent(contract)}/holders`, { chain });
  },
};
```

- [ ] **Step 3: Write `tokenscreen` test**

```typescript
// tests/offerings/tokenscreen.test.ts
import { describe, it, expect } from "vitest";
import { tokenscreen } from "../../src/offerings/tokenscreen.js";

const CONTRACT = "0x" + "c".repeat(40);

describe("tokenscreen", () => {
  it("rejects limit below 1", () => {
    expect(tokenscreen.validate({ contract: CONTRACT, limit: 0 }).valid).toBe(false);
  });
  it("rejects limit above 100", () => {
    expect(tokenscreen.validate({ contract: CONTRACT, limit: 101 }).valid).toBe(false);
  });
  it("accepts limit 50", () => {
    expect(tokenscreen.validate({ contract: CONTRACT, limit: 50 }).valid).toBe(true);
  });
});
```

- [ ] **Step 4: Write `src/offerings/tokenscreen.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateEvmAddress } from "../validators.js";

export const tokenscreen: Offering = {
  name: "tokenscreen",
  description:
    "Token holder screening — analyzes top holders of a given ERC-20 token contract, returning holder distribution, concentration risk, whale activity, and insider detection. Useful for due diligence before investing in a token.",
  requirementSchema: {
    type: "object",
    properties: {
      contract: { type: "string", description: "ERC-20 token contract address (0x...)" },
      limit: { type: "number", minimum: 1, maximum: 100, description: "Top holders (default 20)" },
    },
    required: ["contract"],
  },
  validate(req) {
    if (req.contract === undefined || req.contract === null || req.contract === "") {
      return { valid: false, reason: "contract address is required" };
    }
    const a = validateEvmAddress(req.contract);
    if (!a.valid) return a;
    if (req.limit !== undefined) {
      const n = Number(req.limit);
      if (!Number.isFinite(n) || n < 1 || n > 100) {
        return { valid: false, reason: "limit must be between 1 and 100" };
      }
    }
    return { valid: true };
  },
  async execute(req, { client }) {
    const contract = String(req.contract).trim();
    const limit = Number(req.limit ?? 20);
    return await client.get(`/token/${encodeURIComponent(contract)}/holders`, { limit });
  },
};
```

- [ ] **Step 5: Write `portfoliohistory` test**

```typescript
// tests/offerings/portfoliohistory.test.ts
import { describe, it, expect } from "vitest";
import { portfoliohistory } from "../../src/offerings/portfoliohistory.js";

const A = "0x" + "a".repeat(40);

describe("portfoliohistory", () => {
  it("rejects hours < 1", () => {
    expect(portfoliohistory.validate({ address: A, hours: 0 }).valid).toBe(false);
  });
  it("rejects hours > 168", () => {
    expect(portfoliohistory.validate({ address: A, hours: 169 }).valid).toBe(false);
  });
  it("accepts hours 24", () => {
    expect(portfoliohistory.validate({ address: A, hours: 24 }).valid).toBe(true);
  });
});
```

- [ ] **Step 6: Write `src/offerings/portfoliohistory.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const portfoliohistory: Offering = {
  name: "portfoliohistory",
  description:
    "Historical portfolio snapshots — returns time-series data showing how a wallet's holdings, total value, and token allocations have changed over a specified period. Useful for tracking performance, identifying accumulation/distribution patterns, and portfolio analytics.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
      hours: { type: "number", minimum: 1, maximum: 168, description: "Lookback hours (default 24)" },
    },
    required: ["address"],
  },
  validate(req) {
    const a = validateAddress(req.address);
    if (!a.valid) return a;
    if (req.hours !== undefined) {
      const n = Number(req.hours);
      if (!Number.isFinite(n) || n < 1 || n > 168) {
        return { valid: false, reason: "hours must be between 1 and 168" };
      }
    }
    return { valid: true };
  },
  async execute(req, { client }) {
    const address = encodeURIComponent(String(req.address));
    const hours = Number(req.hours ?? 24);
    return await client.get(`/history/${address}`, { hours });
  },
};
```

- [ ] **Step 7: Write `whalealerts` test**

```typescript
// tests/offerings/whalealerts.test.ts
import { describe, it, expect, vi } from "vitest";
import { whalealerts } from "../../src/offerings/whalealerts.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

describe("whalealerts", () => {
  it("accepts no arguments", () => {
    expect(whalealerts.validate({}).valid).toBe(true);
  });
  it("rejects hours > 72", () => {
    expect(whalealerts.validate({ hours: 100 }).valid).toBe(false);
  });
  it("rejects unknown chain", () => {
    expect(whalealerts.validate({ chain: "solana" }).valid).toBe(false);
  });
  it("executes with defaults", async () => {
    const get = vi.fn(async () => ({ whales: [] }));
    const client: ProfilerClient = {
      profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain: vi.fn(),
      compare: vi.fn(), get, storeDeliverable: vi.fn(),
    };
    await whalealerts.execute({}, { client });
    expect(get).toHaveBeenCalledWith("/whales/ethereum/recent", { hours: 24, minValue: 100000 });
  });
});
```

- [ ] **Step 8: Write `src/offerings/whalealerts.ts`**

```typescript
import type { Offering } from "./types.js";
import { validateChain, EVM_CHAINS } from "../validators.js";

export const whalealerts: Offering = {
  name: "whalealerts",
  description:
    "Real-time whale movement tracker (~3s response). Monitors exchange hot wallets (Binance, Coinbase, Kraken, Bitfinex, Crypto.com) and known whale addresses for large token transfers. Returns labeled movements with USD values and direction (deposit/withdrawal). Market intelligence, smart money tracking, and exchange flow analysis. Supports 7 EVM chains.",
  requirementSchema: {
    type: "object",
    properties: {
      chain: { type: "string", enum: EVM_CHAINS as unknown as string[] },
      hours: { type: "number", description: "Lookback window in hours (default 24, max 72)" },
      minValue: { type: "number", description: "Minimum USD value (default 100000)" },
    },
    required: [],
  },
  validate(req) {
    const c = validateChain(req.chain);
    if (!c.valid) return c;
    if (req.hours !== undefined) {
      const n = Number(req.hours);
      if (!Number.isFinite(n) || n < 1 || n > 72) {
        return { valid: false, reason: "hours must be between 1 and 72" };
      }
    }
    return { valid: true };
  },
  async execute(req, { client }) {
    const chain = (req.chain as string | undefined) ?? "ethereum";
    const hours = Number(req.hours ?? 24);
    const minValue = Number(req.minValue ?? 100000);
    return await client.get(`/whales/${chain}/recent`, { hours, minValue });
  },
};
```

- [ ] **Step 9: Run all offering tests — pass**

```
npm test
```

- [ ] **Step 10: Commit**

```
git add profiler-api/acp-v2/src/offerings/tokenholders.ts profiler-api/acp-v2/src/offerings/tokenscreen.ts profiler-api/acp-v2/src/offerings/portfoliohistory.ts profiler-api/acp-v2/src/offerings/whalealerts.ts profiler-api/acp-v2/tests/offerings/tokenholders.test.ts profiler-api/acp-v2/tests/offerings/tokenscreen.test.ts profiler-api/acp-v2/tests/offerings/portfoliohistory.test.ts profiler-api/acp-v2/tests/offerings/whalealerts.test.ts
git commit -m "acp-v2: offerings batch 4 (tokenholders, tokenscreen, portfoliohistory, whalealerts)"
```

---

## Task 12: Registry (`offerings/registry.ts`)

**Files:**
- Create: `profiler-api/acp-v2/src/offerings/registry.ts`

- [ ] **Step 1: Write `src/offerings/registry.ts`**

```typescript
import type { Offering } from "./types.js";
import { walletprofiler } from "./walletprofiler.js";
import { quickcheck } from "./quickcheck.js";
import { walletstatus } from "./walletstatus.js";
import { riskscore } from "./riskscore.js";
import { multichain } from "./multichain.js";
import { walletcompare } from "./walletcompare.js";
import { deepanalysis } from "./deepanalysis.js";
import { aianalyze } from "./aianalyze.js";
import { airdrops } from "./airdrops.js";
import { approvalaudit } from "./approvalaudit.js";
import { gasspend } from "./gasspend.js";
import { identity } from "./identity.js";
import { liquidationrisk } from "./liquidationrisk.js";
import { lppositions } from "./lppositions.js";
import { pnl } from "./pnl.js";
import { portfoliohistory } from "./portfoliohistory.js";
import { rebalance } from "./rebalance.js";
import { reputation } from "./reputation.js";
import { tokenholders } from "./tokenholders.js";
import { tokenscreen } from "./tokenscreen.js";
import { virtualsintel } from "./virtualsintel.js";
import { whalealerts } from "./whalealerts.js";

export const OFFERINGS: Record<string, Offering> = {
  walletprofiler, quickcheck, walletstatus, riskscore,
  multichain, walletcompare, deepanalysis,
  aianalyze, airdrops, approvalaudit, gasspend, identity,
  liquidationrisk, lppositions, pnl, portfoliohistory, rebalance, reputation,
  tokenholders, tokenscreen, virtualsintel, whalealerts,
};

export function getOffering(name: string): Offering | undefined {
  return OFFERINGS[name];
}

export function listOfferings(): string[] {
  return Object.keys(OFFERINGS);
}
```

- [ ] **Step 2: Build check**

```
cd profiler-api/acp-v2 && npx tsc --noEmit
```
Expected: exits 0.

- [ ] **Step 3: Sanity assertion — count is 22**

Run:
```
node -e 'import("./src/offerings/registry.js").then(m => console.log(Object.keys(m.OFFERINGS).length))'
```
Expected: `22`.

(This requires esm resolution from src; if it fails due to .ts imports, add a quick vitest check instead: create `tests/registry.test.ts`)

```typescript
// tests/registry.test.ts
import { describe, it, expect } from "vitest";
import { OFFERINGS } from "../src/offerings/registry.js";

describe("registry", () => {
  it("exports exactly 22 offerings", () => {
    expect(Object.keys(OFFERINGS)).toHaveLength(22);
  });
  it("every offering has name matching its key", () => {
    for (const [key, value] of Object.entries(OFFERINGS)) {
      expect(value.name).toBe(key);
    }
  });
});
```

- [ ] **Step 4: Run test — pass**

```
npm test -- registry.test.ts
```
Expected: 2 passed.

- [ ] **Step 5: Commit**

```
git add profiler-api/acp-v2/src/offerings/registry.ts profiler-api/acp-v2/tests/registry.test.ts
git commit -m "acp-v2: offering registry with 22 offerings"
```

---

## Task 13: Pricing module (`pricing.ts`)

**Files:**
- Test: `profiler-api/acp-v2/tests/pricing.test.ts`
- Create: `profiler-api/acp-v2/src/pricing.ts`

- [ ] **Step 1: Write failing test**

```typescript
// tests/pricing.test.ts
import { describe, it, expect } from "vitest";
import { priceFor, tierUsdc, OFFERING_OVERRIDES, TIER_USDC } from "../src/pricing.js";

describe("pricing tables", () => {
  it("TIER_USDC has 4 entries", () => {
    expect(TIER_USDC.free).toBe(0);
    expect(TIER_USDC.basic).toBe(1);
    expect(TIER_USDC.standard).toBe(2);
    expect(TIER_USDC.premium).toBe(5);
  });
  it("whalealerts override is $10", () => {
    expect(OFFERING_OVERRIDES.whalealerts).toBe(10);
  });
  it("quickcheck override is $0.50", () => {
    expect(OFFERING_OVERRIDES.quickcheck).toBe(0.5);
  });
});

describe("tierUsdc", () => {
  it("defaults to standard when tier is undefined", () => {
    expect(tierUsdc(undefined)).toBe(2);
  });
  it("maps free=0, basic=1, standard=2, premium=5", () => {
    expect(tierUsdc("free")).toBe(0);
    expect(tierUsdc("basic")).toBe(1);
    expect(tierUsdc("standard")).toBe(2);
    expect(tierUsdc("premium")).toBe(5);
  });
});

describe("priceFor", () => {
  it("override offering ignores tier", () => {
    expect(priceFor("quickcheck", { tier: "premium" }).amount).toBe(0.5);
    expect(priceFor("whalealerts", {}).amount).toBe(10);
  });
  it("tier-driven offering uses tier when override missing", () => {
    expect(priceFor("walletprofiler", { tier: "basic" }).amount).toBe(1);
    expect(priceFor("walletprofiler", { tier: "premium" }).amount).toBe(5);
    expect(priceFor("walletprofiler", {}).amount).toBe(2);
  });
  it("unknown offering falls through to standard", () => {
    expect(priceFor("notARealOffering", {}).amount).toBe(2);
  });
  it("returns USDC symbol", () => {
    expect(priceFor("walletprofiler", {}).token).toBe("USDC");
  });
});
```

- [ ] **Step 2: Run test — fails**

- [ ] **Step 3: Write `src/pricing.ts`**

```typescript
import { AssetToken } from "@virtuals-protocol/acp-node-v2";

export type Tier = "free" | "basic" | "standard" | "premium";

export const TIER_USDC: Record<Tier, number> = {
  free: 0,
  basic: 1,
  standard: 2,
  premium: 5,
};

export const OFFERING_OVERRIDES: Record<string, number> = {
  quickcheck: 0.5,
  walletstatus: 0.5,
  riskscore: 2,
  approvalaudit: 2,
  gasspend: 2,
  tokenscreen: 2,
  identity: 2,
  reputation: 2,
  aianalyze: 2,
  airdrops: 2,
  liquidationrisk: 2,
  lppositions: 2,
  pnl: 2,
  portfoliohistory: 2,
  rebalance: 2,
  tokenholders: 2,
  virtualsintel: 1,
  whalealerts: 10,
};

export interface Price {
  amount: number;
  token: "USDC";
}

export function tierUsdc(tier: string | undefined): number {
  const t = (tier as Tier | undefined) ?? "standard";
  return TIER_USDC[t] ?? TIER_USDC.standard;
}

export function priceFor(offeringName: string, requirement: Record<string, unknown>): Price {
  if (offeringName in OFFERING_OVERRIDES) {
    return { amount: OFFERING_OVERRIDES[offeringName]!, token: "USDC" };
  }
  return { amount: tierUsdc(requirement.tier as string | undefined), token: "USDC" };
}

export function priceForAssetToken(
  offeringName: string,
  requirement: Record<string, unknown>,
  chainId: number
) {
  const price = priceFor(offeringName, requirement);
  return AssetToken.usdc(price.amount, chainId);
}
```

- [ ] **Step 4: Run test — pass**

```
npm test -- pricing.test.ts
```

- [ ] **Step 5: Commit**

```
git add profiler-api/acp-v2/src/pricing.ts profiler-api/acp-v2/tests/pricing.test.ts
git commit -m "acp-v2: USDC pricing with per-offering overrides"
```

---

## Task 14: Deliverable module (`deliverable.ts`)

**Files:**
- Test: `profiler-api/acp-v2/tests/deliverable.test.ts`
- Create: `profiler-api/acp-v2/src/deliverable.ts`

- [ ] **Step 1: Write failing test**

```typescript
// tests/deliverable.test.ts
import { describe, it, expect, vi } from "vitest";
import { toDeliverable, INLINE_SIZE_LIMIT_BYTES } from "../src/deliverable.js";
import type { ProfilerClient } from "../src/profilerClient.js";

function mockClient() {
  const storeDeliverable = vi.fn(async () => ({ id: "abc", url: "http://host/deliverables/abc" }));
  const client: ProfilerClient = {
    profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain: vi.fn(),
    compare: vi.fn(), get: vi.fn(), storeDeliverable,
  };
  return { client, storeDeliverable };
}

describe("toDeliverable", () => {
  it("returns inline JSON under the threshold", async () => {
    const { client, storeDeliverable } = mockClient();
    const payload = { score: 80, tags: ["a", "b"] };
    const out = await toDeliverable("job-1", payload, client);
    expect(out).toBe(JSON.stringify(payload));
    expect(storeDeliverable).not.toHaveBeenCalled();
  });

  it("returns URL when over the threshold", async () => {
    const { client, storeDeliverable } = mockClient();
    const big = { blob: "x".repeat(INLINE_SIZE_LIMIT_BYTES + 100) };
    const out = await toDeliverable("job-2", big, client);
    expect(out).toBe("http://host/deliverables/abc");
    expect(storeDeliverable).toHaveBeenCalledWith("job-2", big);
  });

  it("falls back to inline if storeDeliverable throws", async () => {
    const storeDeliverable = vi.fn(async () => { throw new Error("redis down"); });
    const client: ProfilerClient = {
      profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain: vi.fn(),
      compare: vi.fn(), get: vi.fn(), storeDeliverable,
    };
    const big = { blob: "x".repeat(INLINE_SIZE_LIMIT_BYTES + 100) };
    const out = await toDeliverable("job-3", big, client);
    expect(out).toBe(JSON.stringify(big));
  });
});
```

- [ ] **Step 2: Run test — fails**

- [ ] **Step 3: Write `src/deliverable.ts`**

```typescript
import type { ProfilerClient } from "./profilerClient.js";

export const INLINE_SIZE_LIMIT_BYTES = 50_000;

export async function toDeliverable(
  jobId: string,
  payload: unknown,
  client: ProfilerClient
): Promise<string> {
  const json = JSON.stringify(payload);
  if (json.length <= INLINE_SIZE_LIMIT_BYTES) return json;

  try {
    const { url } = await client.storeDeliverable(jobId, payload);
    return url;
  } catch (err) {
    console.warn(
      `[deliverable] storeDeliverable failed for job ${jobId}, falling back to inline submit: ${String(err)}`
    );
    return json;
  }
}
```

- [ ] **Step 4: Run test — pass**

- [ ] **Step 5: Commit**

```
git add profiler-api/acp-v2/src/deliverable.ts profiler-api/acp-v2/tests/deliverable.test.ts
git commit -m "acp-v2: hybrid inline/URL deliverable with graceful fallback"
```

---

## Task 15: Router (`router.ts`)

**Files:**
- Test: `profiler-api/acp-v2/tests/router.test.ts`
- Create: `profiler-api/acp-v2/src/router.ts`

- [ ] **Step 1: Write failing test**

```typescript
// tests/router.test.ts
import { describe, it, expect, vi } from "vitest";
import { route } from "../src/router.js";
import type { ProfilerClient } from "../src/profilerClient.js";

function ctx() {
  const client: ProfilerClient = {
    profile: vi.fn(async () => ({ score: 80 })),
    profileBatch: vi.fn(), profileMultiChain: vi.fn(),
    compare: vi.fn(), get: vi.fn(), storeDeliverable: vi.fn(),
  };
  return { client };
}

describe("route", () => {
  it("validates + executes known offering", async () => {
    const { client } = ctx();
    const res = await route("walletprofiler", { address: "0x" + "a".repeat(40) }, { client });
    expect(res.ok).toBe(true);
    expect(res.result).toEqual({ score: 80 });
  });

  it("returns validation failure for unknown offering", async () => {
    const { client } = ctx();
    const res = await route("not-a-real-offering", {}, { client });
    expect(res.ok).toBe(false);
    expect(res.reason).toMatch(/unknown offering/i);
  });

  it("returns validation failure for bad requirements", async () => {
    const { client } = ctx();
    const res = await route("walletprofiler", {}, { client });
    expect(res.ok).toBe(false);
    expect(res.reason).toMatch(/address/);
  });

  it("catches execute errors and returns ok=false", async () => {
    const client: ProfilerClient = {
      profile: vi.fn(async () => { throw new Error("upstream 500"); }),
      profileBatch: vi.fn(), profileMultiChain: vi.fn(),
      compare: vi.fn(), get: vi.fn(), storeDeliverable: vi.fn(),
    };
    const res = await route("walletprofiler", { address: "0x" + "a".repeat(40) }, { client });
    expect(res.ok).toBe(false);
    expect(res.reason).toMatch(/upstream 500/);
  });
});
```

- [ ] **Step 2: Run test — fails**

- [ ] **Step 3: Write `src/router.ts`**

```typescript
import { getOffering } from "./offerings/registry.js";
import type { OfferingContext } from "./offerings/types.js";

export type RouteResult =
  | { ok: true; result: unknown }
  | { ok: false; reason: string };

export async function route(
  offeringName: string,
  requirement: Record<string, unknown>,
  ctx: OfferingContext
): Promise<RouteResult> {
  const offering = getOffering(offeringName);
  if (!offering) {
    return { ok: false, reason: `unknown offering: ${offeringName}` };
  }
  const validation = offering.validate(requirement);
  if (!validation.valid) {
    return { ok: false, reason: validation.reason ?? "validation failed" };
  }
  try {
    const result = await offering.execute(requirement, ctx);
    return { ok: true, result };
  } catch (err) {
    return { ok: false, reason: err instanceof Error ? err.message : String(err) };
  }
}

export function priceRequirement(requirement: Record<string, unknown>): Record<string, unknown> {
  return requirement;
}
```

- [ ] **Step 4: Run test — pass**

- [ ] **Step 5: Commit**

```
git add profiler-api/acp-v2/src/router.ts profiler-api/acp-v2/tests/router.test.ts
git commit -m "acp-v2: router with validation + execution error wrapping"
```

---

## Task 16: Seller entry (`seller.ts`)

**Files:**
- Create: `profiler-api/acp-v2/src/seller.ts`

> This file integrates with the live V2 SDK and therefore has no unit test. It is exercised in the integration smoke test (Task 20).

- [ ] **Step 1: Write `src/seller.ts`**

```typescript
import { AcpAgent } from "@virtuals-protocol/acp-node-v2";
import { loadEnv } from "./env.js";
import { createProvider } from "./provider.js";
import { createProfilerClient } from "./profilerClient.js";
import { route } from "./router.js";
import { priceForAssetToken } from "./pricing.js";
import { toDeliverable } from "./deliverable.js";
import { listOfferings } from "./offerings/registry.js";

async function main() {
  const env = loadEnv();
  const client = createProfilerClient(env.profilerApiUrl);

  console.log(`[seller] chain=${env.chain} wallet=${env.walletAddress}`);
  console.log(`[seller] offerings registered (in code): ${listOfferings().length}`);

  const provider = await createProvider(env);
  const agent = await AcpAgent.create({ provider });

  agent.on("entry", async (session: any, entry: any) => {
    try {
      if (entry.kind === "system") {
        switch (entry.event?.type) {
          case "job.created":
            console.log(`[seller] job.created jobId=${session.jobId}`);
            return;
          case "job.funded":
            return await handleJobFunded(session);
          case "job.completed":
            console.log(`[seller] job.completed jobId=${session.jobId}`);
            return;
          case "job.rejected":
            console.log(`[seller] job.rejected jobId=${session.jobId}`);
            return;
          default:
            return;
        }
      }

      if (entry.kind === "message" && entry.contentType === "requirement") {
        await handleRequirement(session, entry);
        return;
      }
    } catch (err) {
      console.error(`[seller] handler error for job ${session.jobId}:`, err);
    }
  });

  async function handleRequirement(session: any, entry: any) {
    let parsed: { name?: string; requirement?: Record<string, unknown> };
    try {
      parsed = JSON.parse(entry.content);
    } catch {
      await session.sendMessage("invalid requirement payload");
      return;
    }
    const offeringName = parsed.name ?? "";
    const requirement = parsed.requirement ?? {};

    const { getOffering } = await import("./offerings/registry.js");
    const offering = getOffering(offeringName);
    if (!offering) {
      await session.sendMessage(`unknown offering: ${offeringName}`);
      return;
    }
    const v = offering.validate(requirement);
    if (!v.valid) {
      await session.sendMessage(v.reason ?? "validation failed");
      return;
    }

    const price = priceForAssetToken(offeringName, requirement, session.chainId);
    await session.setBudget(price);

    // Stash so handleJobFunded can retrieve it. The SDK passes session by reference
    // across events, so attach the offering name + requirement for later execution.
    (session as any)._walletProfiler = { offeringName, requirement };
  }

  async function handleJobFunded(session: any) {
    const stash = (session as any)._walletProfiler as
      | { offeringName: string; requirement: Record<string, unknown> }
      | undefined;
    if (!stash) {
      console.warn(`[seller] job.funded without stashed requirement, jobId=${session.jobId}`);
      return;
    }
    const outcome = await route(stash.offeringName, stash.requirement, { client });
    if (!outcome.ok) {
      await session.sendMessage(`execution failed: ${outcome.reason}`);
      return;
    }
    const payload = await toDeliverable(String(session.jobId), outcome.result, client);
    await session.submit(payload);
    console.log(`[seller] submitted jobId=${session.jobId} offering=${stash.offeringName}`);
  }

  await agent.start();

  const shutdown = async (signal: string) => {
    console.log(`[seller] ${signal} received, stopping agent`);
    try {
      await agent.stop();
    } finally {
      process.exit(0);
    }
  };
  process.on("SIGINT", () => void shutdown("SIGINT"));
  process.on("SIGTERM", () => void shutdown("SIGTERM"));

  console.log("[seller] running — waiting for jobs");
}

main().catch((err) => {
  console.error("[seller] fatal:", err);
  process.exit(1);
});
```

> **Caveat on `session.chainId`**: if the SDK surfaces the chain via a different field (`session.chain?.id`, `session.evmChainId`, etc.), swap it in. Same for `session.jobId` vs `session.id`. The migration doc uses `session.chainId` and `session.jobId`.

> **Caveat on attaching state to `session`**: if V2 sessions are frozen proxies, replace the `_walletProfiler` stash with a `Map<jobId, { offeringName, requirement }>` keyed by `session.jobId`, cleared on `job.completed` / `job.rejected` / `job.timedout`.

- [ ] **Step 2: Build check**

```
cd profiler-api/acp-v2 && npx tsc --noEmit
```
Expected: exits 0. (If `AcpAgent`, `session.chainId`, or event shape differ, adjust types and re-run.)

- [ ] **Step 3: Commit**

```
git add profiler-api/acp-v2/src/seller.ts
git commit -m "acp-v2: seller entry point with event-driven job handling"
```

---

## Task 17: Registration-print script

**Files:**
- Create: `profiler-api/acp-v2/scripts/print-offerings-for-registration.ts`

- [ ] **Step 1: Write script**

```typescript
import { OFFERINGS } from "../src/offerings/registry.js";
import { priceFor } from "../src/pricing.js";

function main() {
  for (const offering of Object.values(OFFERINGS)) {
    const basePrice = priceFor(offering.name, {});
    console.log("=".repeat(72));
    console.log(`Offering: ${offering.name}`);
    console.log(`Price (default / standard tier): ${basePrice.amount} USDC`);
    console.log("");
    console.log("Description:");
    console.log(offering.description);
    console.log("");
    console.log("Requirement schema:");
    console.log(JSON.stringify(offering.requirementSchema, null, 2));
    console.log("");
  }
}

main();
```

- [ ] **Step 2: Run once to verify output**

```
cd profiler-api/acp-v2 && npm run print-offerings | head -50
```
Expected: prints first offering block with name / price / description / schema.

- [ ] **Step 3: Commit**

```
git add profiler-api/acp-v2/scripts/print-offerings-for-registration.ts
git commit -m "acp-v2: script to emit offering registration data for Virtuals UI"
```

---

## Task 18: C# `DeliverableStore` service

**Files:**
- Create: `profiler-api/ProfilerApi/Services/DeliverableStore.cs`

- [ ] **Step 1: Write `DeliverableStore.cs`**

```csharp
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace ProfilerApi.Services;

public class DeliverableStore
{
    private readonly IDistributedCache _cache;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    public DeliverableStore(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<string> StoreAsync(object payload, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var json = JsonSerializer.Serialize(payload);
        await _cache.SetStringAsync(
            Key(id),
            json,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
            ct);
        return id;
    }

    public async Task<string?> FetchAsync(string id, CancellationToken ct = default)
    {
        if (!IsValidId(id)) return null;
        return await _cache.GetStringAsync(Key(id), ct);
    }

    private static string Key(string id) => $"deliverable:{id}";

    private static bool IsValidId(string id) =>
        !string.IsNullOrEmpty(id)
        && id.Length == 32
        && id.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
}
```

- [ ] **Step 2: Build the C# project**

```
cd profiler-api/ProfilerApi && dotnet build
```
Expected: exits 0.

- [ ] **Step 3: Commit**

```
git add profiler-api/ProfilerApi/Services/DeliverableStore.cs
git commit -m "profiler-api: add DeliverableStore backed by IDistributedCache"
```

---

## Task 19: Register `DeliverableStore` + add deliverable endpoints

**Files:**
- Modify: `profiler-api/ProfilerApi/Program.cs`
- Modify: `profiler-api/ProfilerApi/appsettings.json`

- [ ] **Step 1: Add `AppSettings:PublicBaseUrl` to appsettings.json**

Open `profiler-api/ProfilerApi/appsettings.json`. Add a new top-level key (order does not matter, but put it after `Redis`):

```json
  "Redis": {
    "ConnectionString": ""
  },
  "AppSettings": {
    "PublicBaseUrl": "http://localhost:5000"
  },
  "ApiKeys": []
```

- [ ] **Step 2: Register the service**

In `Program.cs`, find the block of `builder.Services.AddSingleton<...>` around line 36-61 and add:

```csharp
builder.Services.AddSingleton<DeliverableStore>();
```

(Place it among the other `AddSingleton` lines. It has no dependencies besides `IDistributedCache`, which is only bound when `Redis:ConnectionString` is configured — if not configured, the call will fail at request time. That is acceptable because deliverables over 50 KB only happen in production environments where Redis must be configured.)

- [ ] **Step 3: Add POST /deliverables endpoint**

In `Program.cs`, find a natural insertion point after other `app.MapPost(...)` endpoints. Add:

```csharp
app.MapPost("/deliverables", async (
    DeliverableStoreRequest body,
    DeliverableStore store,
    IConfiguration config) =>
{
    if (body is null || body.Payload is null)
        return Results.BadRequest(new { error = "payload is required" });

    var id = await store.StoreAsync(body.Payload);
    var baseUrl = config["AppSettings:PublicBaseUrl"] ?? "http://localhost:5000";
    var url = $"{baseUrl.TrimEnd('/')}/deliverables/{id}";
    return Results.Ok(new { id, url });
});
```

And add the request record near other DTOs (e.g. inside a "Models" area or at the bottom of Program.cs):

```csharp
public record DeliverableStoreRequest(string? JobId, object? Payload);
```

- [ ] **Step 4: Add GET /deliverables/{id} endpoint**

After the POST, add:

```csharp
app.MapGet("/deliverables/{id}", async (
    string id,
    DeliverableStore store,
    HttpContext http) =>
{
    var json = await store.FetchAsync(id);
    if (json is null) return Results.NotFound();
    http.Response.ContentType = "application/json";
    return Results.Content(json, "application/json");
});
```

- [ ] **Step 5: Add required using at the top**

In `Program.cs`, ensure `using ProfilerApi.Services;` is present (likely already is because other services from that namespace are used).

- [ ] **Step 6: Build**

```
cd profiler-api/ProfilerApi && dotnet build
```
Expected: exits 0.

- [ ] **Step 7: Sanity run**

```
cd profiler-api/ProfilerApi && dotnet run --no-build &
# wait a few seconds for startup
curl -s -X POST http://localhost:5000/deliverables \
  -H "Content-Type: application/json" \
  -d '{"jobId":"test-1","payload":{"hello":"world"}}'
```
Expected: JSON response with `id` (32-char hex) and `url` (`http://localhost:5000/deliverables/{id}`).

Then:
```
curl -s http://localhost:5000/deliverables/{id-from-above}
```
Expected: `{"hello":"world"}`.

Stop the server (`kill %1` or Ctrl-C).

> If Redis is not configured in `appsettings.Development.json`, the POST will 500 with "IDistributedCache not registered". Set `Redis:ConnectionString` to `localhost:6379` (plus `docker run -d --name redis-dev -p 6379:6379 redis:7-alpine`) to run the sanity check.

- [ ] **Step 8: Commit**

```
git add profiler-api/ProfilerApi/Program.cs profiler-api/ProfilerApi/appsettings.json
git commit -m "profiler-api: POST/GET /deliverables backed by Redis, 1h TTL"
```

---

## Task 20: Dockerfile + .env.example + README for acp-v2

**Files:**
- Create: `profiler-api/acp-v2/Dockerfile`
- Create: `profiler-api/acp-v2/.env.example`
- Create: `profiler-api/acp-v2/README.md`

- [ ] **Step 1: Write `Dockerfile`**

```dockerfile
FROM node:22-slim

WORKDIR /app

# Install deps first for better layer caching
COPY package.json package-lock.json* ./
RUN npm ci --omit=dev

# Copy source
COPY tsconfig.json ./
COPY src ./src
COPY scripts ./scripts

# Use tsx at runtime — no build step needed
RUN npm install tsx@^4.19.2

ENV NODE_ENV=production
ENV PROFILER_API_URL=http://profiler-api:5000

CMD ["npx", "tsx", "src/seller.ts"]
```

- [ ] **Step 2: Write `.env.example`**

```
# Credentials — from https://app.virtuals.io/acp/agents/ → Signers tab
ACP_WALLET_ADDRESS=0x0000000000000000000000000000000000000000
ACP_WALLET_ID=
ACP_SIGNER_PRIVATE_KEY=0x

# Optional — from Settings tab
ACP_BUILDER_CODE=

# "base" for mainnet, "baseSepolia" for testnet
ACP_CHAIN=baseSepolia

# Inter-service
PROFILER_API_URL=http://profiler-api:5000
```

- [ ] **Step 3: Write `README.md`**

```markdown
# Wallet Profiler ACP v2 Seller

Node.js sidecar that speaks ACP v2 via `@virtuals-protocol/acp-node-v2`, dispatches 22 offerings, and proxies to the C# profiler API.

## Setup

1. Upgrade the WalletProfiler agent in https://app.virtuals.io/acp/agents/ to V2.
2. From the Signers tab, copy `walletId` and `signerPrivateKey`.
3. Copy `.env.example` → `.env` and fill in credentials.
4. `npm install`
5. `npm test` — unit tests.
6. `npm start` — runs the seller against the chain in `ACP_CHAIN`.

## Register offerings

V2 has no programmatic registration. Run:

```
npm run print-offerings
```

Copy each block into app.virtuals.io → WalletProfiler agent → Offerings → New offering.

## Layout

- `src/seller.ts` — entry point
- `src/offerings/` — 22 offering handlers
- `src/pricing.ts` — USDC price table
- `src/deliverable.ts` — inline vs URL deliverables (50 KB threshold)
- `src/profilerClient.ts` — typed HTTP client for the C# API
```

- [ ] **Step 4: Commit**

```
git add profiler-api/acp-v2/Dockerfile profiler-api/acp-v2/.env.example profiler-api/acp-v2/README.md
git commit -m "acp-v2: Dockerfile, env example, README"
```

---

## Task 21: Update docker-compose

**Files:**
- Modify: `deploy/docker-compose.yml`
- Delete: `deploy/Dockerfile.acp-runtime`
- Modify: `deploy/deploy.sh`

- [ ] **Step 1: Rewrite `deploy/docker-compose.yml`**

Replace the entire file with:

```yaml
services:
  profiler-api:
    build:
      context: ../profiler-api
      dockerfile: Dockerfile
    ports:
      - "5000:5000"
    env_file: .env
    environment:
      - Alchemy__ApiKey=${ALCHEMY_API_KEY}
      - Etherscan__ApiKey=${ETHERSCAN_API_KEY}
      - Basescan__ApiKey=${BASESCAN_API_KEY:-}
      - Arbiscan__ApiKey=${ARBISCAN_API_KEY:-}
      - Anthropic__ApiKey=${ANTHROPIC_API_KEY:-}
      - Redis__ConnectionString=${REDIS_CONNECTION:-redis:6379}
      - AppSettings__PublicBaseUrl=${PUBLIC_BASE_URL:-http://localhost:5000}
    depends_on:
      redis:
        condition: service_started
    healthcheck:
      test: ["CMD-SHELL", "dotnet --info > /dev/null 2>&1 || exit 1"]
      interval: 5s
      timeout: 3s
      retries: 5
      start_period: 10s
    restart: unless-stopped

  acp-v2:
    build:
      context: ../profiler-api/acp-v2
      dockerfile: Dockerfile
    depends_on:
      profiler-api:
        condition: service_healthy
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
    volumes:
      - redis-data:/data

volumes:
  redis-data:
```

- [ ] **Step 2: Delete old runtime Dockerfile**

```
rm deploy/Dockerfile.acp-runtime
```

- [ ] **Step 3: Rewrite `deploy/deploy.sh`**

```bash
#!/bin/bash
# Deploy WalletProfiler (v2) to EC2
# Usage: bash deploy.sh <ec2-user@host> <key-file.pem>

set -e

EC2_HOST="$1"
KEY_FILE="$2"
REMOTE_DIR="/home/ubuntu/walletprofiler"

if [ -z "$EC2_HOST" ] || [ -z "$KEY_FILE" ]; then
  echo "Usage: bash deploy.sh <ec2-user@host> <key-file.pem>"
  exit 1
fi

SSH="ssh -i $KEY_FILE -o StrictHostKeyChecking=no"
SCP="scp -i $KEY_FILE -o StrictHostKeyChecking=no"

echo "=== Deploying WalletProfiler v2 to $EC2_HOST ==="

$SSH $EC2_HOST "mkdir -p $REMOTE_DIR/wallet-profiler/{profiler-api,deploy}"

echo "Uploading profiler-api (C# + acp-v2 sidecar)..."
$SCP -r ../profiler-api/ProfilerApi $EC2_HOST:$REMOTE_DIR/wallet-profiler/profiler-api/
$SCP ../profiler-api/Dockerfile $EC2_HOST:$REMOTE_DIR/wallet-profiler/profiler-api/
$SCP -r ../profiler-api/acp-v2 $EC2_HOST:$REMOTE_DIR/wallet-profiler/profiler-api/

echo "Uploading deploy config..."
$SCP docker-compose.yml $EC2_HOST:$REMOTE_DIR/wallet-profiler/deploy/

$SSH $EC2_HOST "test -f $REMOTE_DIR/wallet-profiler/deploy/.env" || {
  echo "WARNING: No .env on remote. Create $REMOTE_DIR/wallet-profiler/deploy/.env with:"
  echo "  ALCHEMY_API_KEY=..."
  echo "  ETHERSCAN_API_KEY=..."
  echo "  ACP_WALLET_ADDRESS=0x..."
  echo "  ACP_WALLET_ID=..."
  echo "  ACP_SIGNER_PRIVATE_KEY=0x..."
  echo "  ACP_CHAIN=baseSepolia|base"
  echo "  PUBLIC_BASE_URL=https://your.domain"
}

echo "Building and starting containers..."
$SSH $EC2_HOST "cd $REMOTE_DIR/wallet-profiler/deploy && docker compose up -d --build"

echo "=== Deployment complete ==="
echo "Check status: $SSH $EC2_HOST 'cd $REMOTE_DIR/wallet-profiler/deploy && docker compose ps'"
echo "View logs:    $SSH $EC2_HOST 'cd $REMOTE_DIR/wallet-profiler/deploy && docker compose logs -f'"
```

- [ ] **Step 4: Local compose-config validation**

```
cd deploy && docker compose --env-file /dev/null config
```
Expected: prints the resolved compose file without errors. Unset env vars will appear as empty strings — that's fine for validation.

- [ ] **Step 5: Commit**

```
git add deploy/docker-compose.yml deploy/deploy.sh
git rm deploy/Dockerfile.acp-runtime
git commit -m "deploy: replace v1 acp-runtime with acp-v2 + redis service"
```

---

## Task 22: Delete `wallet-profiler/acp-service/`

**Files:**
- Delete: `acp-service/` (entire directory)

- [ ] **Step 1: Remove directory**

```
git rm -r acp-service/
```

- [ ] **Step 2: Commit**

```
git commit -m "acp-v2: remove superseded wallet-profiler/acp-service stub"
```

---

## Task 23: Integration smoke test (local)

**Files:**
- Create: `profiler-api/acp-v2/scripts/smoke-test.sh`

> No unit test — this script is run by a human (or CI) to validate the built containers work end-to-end without touching real Virtuals infra.

- [ ] **Step 1: Write the script**

```bash
#!/bin/bash
# Integration smoke test — requires .env with at least ACP_CHAIN set and a running Redis.
# Starts only profiler-api + redis, then hits deliverable endpoints.

set -e
cd "$(dirname "$0")/../../.."    # cwd = wallet-profiler/

docker compose -f deploy/docker-compose.yml up -d profiler-api redis

# Wait for profiler-api
for i in $(seq 1 30); do
  if curl -sf http://localhost:5000/health > /dev/null 2>&1; then
    echo "profiler-api up"
    break
  fi
  sleep 1
done

# Store a deliverable
RESP=$(curl -s -X POST http://localhost:5000/deliverables \
  -H "Content-Type: application/json" \
  -d '{"jobId":"smoke-1","payload":{"hello":"world","n":42}}')
echo "POST /deliverables → $RESP"

ID=$(echo "$RESP" | python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])')

FETCH=$(curl -s http://localhost:5000/deliverables/$ID)
echo "GET  /deliverables/$ID → $FETCH"

[ "$FETCH" = '{"hello":"world","n":42}' ] && echo "SMOKE OK" || { echo "SMOKE FAIL"; exit 1; }

echo ""
echo "Done. Leave containers up with: docker compose -f deploy/docker-compose.yml logs -f"
```

- [ ] **Step 2: Make executable**

```
chmod +x profiler-api/acp-v2/scripts/smoke-test.sh
```

- [ ] **Step 3: Commit**

```
git add profiler-api/acp-v2/scripts/smoke-test.sh
git commit -m "acp-v2: local smoke test for deliverable round-trip"
```

---

## Task 24: Run the full test suite + final commit

- [ ] **Step 1: Run everything**

```
cd profiler-api/acp-v2 && npm test
```
Expected: all tests pass (env, chain, validators, profilerClient, deliverable, pricing, router, registry, offerings/*).

- [ ] **Step 2: Type-check everything**

```
cd profiler-api/acp-v2 && npx tsc --noEmit
```
Expected: exits 0.

- [ ] **Step 3: C# build**

```
cd profiler-api/ProfilerApi && dotnet build
```
Expected: exits 0.

- [ ] **Step 4: (Optional) Run smoke test**

Requires Docker; local Redis and profiler-api come up.
```
bash profiler-api/acp-v2/scripts/smoke-test.sh
```
Expected: `SMOKE OK`.

- [ ] **Step 5: Push branch**

```
git push -u origin <branch-name>
```

No new commit required if all the above passed without code changes.

---

## Manual rollout steps (post-implementation, not code)

These are the operational steps needed once the implementation is merged. Document them in the PR description, not the code:

1. Upgrade the WalletProfiler agent at https://app.virtuals.io/acp/agents/ to V2.
2. From the Signers tab, copy `walletId` and `signerPrivateKey`.
3. From the Settings tab, optionally copy `builderCode`.
4. Populate `deploy/.env` on target host with:
   - `ACP_WALLET_ADDRESS=0xf19526F4A82f51da749c4776fA00bDA0076C440a`
   - `ACP_WALLET_ID=<from UI>`
   - `ACP_SIGNER_PRIVATE_KEY=<from UI>`
   - `ACP_BUILDER_CODE=<from UI, optional>`
   - `ACP_CHAIN=baseSepolia`
   - `ALCHEMY_API_KEY=...`, `ETHERSCAN_API_KEY=...`
   - `REDIS_CONNECTION=redis:6379`
   - `PUBLIC_BASE_URL=https://<your-host>`
5. `npm run print-offerings` in `profiler-api/acp-v2` locally — copy each block into the UI.
6. Deploy to Sepolia: `bash deploy/deploy.sh user@host key.pem`.
7. Run live testnet purchases as listed in spec §Testing (walletprofiler, quickcheck, deepanalysis, multichain, approvalaudit).
8. Register the same offerings on Base mainnet.
9. Flip `ACP_CHAIN=base` and redeploy.
10. Monitor `docker compose logs -f acp-v2`.

---

## Self-Review

Ran through the plan against the spec:

- ✅ Scope: Option C1 (greenfield Node sidecar in `profiler-api/acp-v2/`) — Tasks 1, 20, 21, 22.
- ✅ All 22 offerings: Tasks 8, 9, 10, 11 (4+3+11+4 = 22), registry in Task 12.
- ✅ Testnet+mainnet switch via `ACP_CHAIN`: Tasks 2, 3, 21.
- ✅ Flat tiered USDC pricing: Task 13 (matches spec table; "free" tier kept — spec flagged handling may drop it at registration time).
- ✅ Hybrid deliverable 50 KB threshold: Task 14.
- ✅ C# deliverable store + 2 endpoints: Tasks 18, 19.
- ✅ Offering registration script: Task 17 + manual rollout section.
- ✅ Single SDK event handler with phase-to-event switch: Task 16.
- ✅ Delete `acp-service/`, retire `acp-runtime` from compose: Tasks 21, 22.
- ✅ `virtuals-protocol-acp/` sibling left untouched (never referenced in any task).
- ✅ Tests for every non-trivial piece (pricing, deliverable, router, validators, profilerClient, 10+ offering tests, registry count).
- ✅ No "TBD", "TODO", or "implement later" markers.
- ✅ Type consistency: `Offering`, `OfferingContext`, `ProfilerClient`, `RouteResult`, `Price`, `AcpEnv` — each defined once, used with matching names.
- ✅ Integration smoke test (Task 23) covers the C# deliverable path.
- ⚠️ Live-V2 integration behaviour (session.chainId, session.jobId, event shapes) verified only at runtime — plan flags this inline in Task 16 with fallbacks.
