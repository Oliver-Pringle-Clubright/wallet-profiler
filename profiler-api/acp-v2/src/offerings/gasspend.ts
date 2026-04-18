import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const gasspend: Offering = {
  name: "gasspend",
  description:
    "Gas spending analysis — calculates total gas spent, average gas price, monthly breakdown, and top 5 most expensive transactions for a wallet. Useful for cost optimization, tax reporting, and understanding on-chain activity patterns.",
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
    return await client.get(`/gas/${address}`);
  },
};
