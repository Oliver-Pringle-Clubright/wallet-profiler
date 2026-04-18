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
