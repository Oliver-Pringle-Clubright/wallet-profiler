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
