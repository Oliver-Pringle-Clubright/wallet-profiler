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
