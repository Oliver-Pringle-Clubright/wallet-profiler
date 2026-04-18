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
