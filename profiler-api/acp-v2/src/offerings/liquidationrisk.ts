import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const liquidationrisk: Offering = {
  name: "liquidationrisk",
  description:
    "Liquidation risk monitoring — checks Aave V3 health factor and Compound V3 borrow balance. Returns health factor, risk level (safe/watch/warning/danger), collateral and debt values, and alerts. Critical for lending protocol risk management and DeFi safety monitoring.",
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
    return await client.get(`/liquidation-risk/${address}`, { chain });
  },
};
