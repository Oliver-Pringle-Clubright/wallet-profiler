import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const identity: Offering = {
  name: "identity",
  description:
    "Social identity resolution — resolves a wallet's on-chain identity including ENS name, social profiles, on-chain reputation signals, and identity confidence score. Useful for KYC-lite checks and verifying wallet ownership.",
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
    return await client.get(`/identity/${address}`);
  },
};
