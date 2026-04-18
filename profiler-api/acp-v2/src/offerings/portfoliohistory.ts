import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const portfoliohistory: Offering = {
  name: "portfoliohistory",
  description:
    "Historical portfolio snapshots — returns time-series data showing how a wallet's holdings, total value, and token allocations have changed over a specified period. Useful for tracking performance, identifying accumulation/distribution patterns, and portfolio analytics.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
      hours: { type: "number", minimum: 1, maximum: 168, description: "Lookback hours (default 24)" },
    },
    required: ["address"],
  },
  validate(req) {
    const a = validateAddress(req.address);
    if (!a.valid) return a;
    if (req.hours !== undefined) {
      const n = Number(req.hours);
      if (!Number.isFinite(n) || n < 1 || n > 168) {
        return { valid: false, reason: "hours must be between 1 and 168" };
      }
    }
    return { valid: true };
  },
  async execute(req, { client }) {
    const address = encodeURIComponent(String(req.address));
    const hours = Number(req.hours ?? 24);
    return await client.get(`/history/${address}`, { hours });
  },
};
