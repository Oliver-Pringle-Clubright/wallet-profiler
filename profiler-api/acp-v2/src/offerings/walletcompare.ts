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
