import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const lppositions: Offering = {
  name: "lppositions",
  description:
    "Uniswap V3 LP position detection — reads NonfungiblePositionManager to discover liquidity positions. Returns token pairs, fee tiers, liquidity amounts, uncollected fees, in-range status, and position status. Essential for DeFi portfolio tracking and yield monitoring.",
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
    return await client.get(`/lp-positions/${address}`, { chain });
  },
};
