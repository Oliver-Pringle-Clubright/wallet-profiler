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
