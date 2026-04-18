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
