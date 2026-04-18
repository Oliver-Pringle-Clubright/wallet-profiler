import type { Offering } from "./types.js";
import { validateEvmAddress } from "../validators.js";

export const tokenscreen: Offering = {
  name: "tokenscreen",
  description:
    "Token holder screening — analyzes top holders of a given ERC-20 token contract, returning holder distribution, concentration risk, whale activity, and insider detection. Useful for due diligence before investing in a token.",
  requirementSchema: {
    type: "object",
    properties: {
      contract: { type: "string", description: "ERC-20 token contract address (0x...)" },
      limit: { type: "number", minimum: 1, maximum: 100, description: "Top holders (default 20)" },
    },
    required: ["contract"],
  },
  validate(req) {
    if (req.contract === undefined || req.contract === null || req.contract === "") {
      return { valid: false, reason: "contract address is required" };
    }
    const a = validateEvmAddress(req.contract);
    if (!a.valid) return a;
    if (req.limit !== undefined) {
      const n = Number(req.limit);
      if (!Number.isFinite(n) || n < 1 || n > 100) {
        return { valid: false, reason: "limit must be between 1 and 100" };
      }
    }
    return { valid: true };
  },
  async execute(req, { client }) {
    const contract = String(req.contract).trim();
    const limit = Number(req.limit ?? 20);
    return await client.get(`/token/${encodeURIComponent(contract)}/holders`, { limit });
  },
};
