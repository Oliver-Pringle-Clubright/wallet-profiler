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
