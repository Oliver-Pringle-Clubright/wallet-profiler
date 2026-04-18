import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const reputation: Offering = {
  name: "reputation",
  description:
    "Wallet reputation badge — generates a comprehensive reputation score and badge based on wallet history, holdings, trust score, and on-chain activity. Returns a badge tier (Bronze/Silver/Gold/Platinum/Diamond), reputation score, and achievement list.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
    },
    required: ["address"],
  },
  validate(req) {
    return validateAddress(req.address);
  },
  async execute(req, { client }) {
    const address = encodeURIComponent(String(req.address));
    return await client.get(`/reputation/${address}`);
  },
};
