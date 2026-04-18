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
