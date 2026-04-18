import type { Offering } from "./types.js";
import { validateChain, EVM_CHAINS } from "../validators.js";

export const whalealerts: Offering = {
  name: "whalealerts",
  description:
    "Real-time whale movement tracker (~3s response). Monitors exchange hot wallets (Binance, Coinbase, Kraken, Bitfinex, Crypto.com) and known whale addresses for large token transfers. Returns labeled movements with USD values and direction (deposit/withdrawal). Market intelligence, smart money tracking, and exchange flow analysis. Supports 7 EVM chains.",
  requirementSchema: {
    type: "object",
    properties: {
      chain: { type: "string", enum: EVM_CHAINS as unknown as string[] },
      hours: { type: "number", description: "Lookback window in hours (default 24, max 72)" },
      minValue: { type: "number", description: "Minimum USD value (default 100000)" },
    },
    required: [],
  },
  validate(req) {
    const c = validateChain(req.chain);
    if (!c.valid) return c;
    if (req.hours !== undefined) {
      const n = Number(req.hours);
      if (!Number.isFinite(n) || n < 1 || n > 72) {
        return { valid: false, reason: "hours must be between 1 and 72" };
      }
    }
    return { valid: true };
  },
  async execute(req, { client }) {
    const chain = (req.chain as string | undefined) ?? "ethereum";
    const hours = Number(req.hours ?? 24);
    const minValue = Number(req.minValue ?? 100000);
    return await client.get(`/whales/${chain}/recent`, { hours, minValue });
  },
};
