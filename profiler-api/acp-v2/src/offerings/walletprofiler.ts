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
