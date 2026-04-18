import type { Offering } from "./types.js";
import { validateEvmAddress, validateChain, EVM_CHAINS } from "../validators.js";

export const tokenholders: Offering = {
  name: "tokenholders",
  description:
    "Token holder concentration analysis for any ERC-20 token (~8s response). Returns top holders with trust scores, holder concentration percentage, wallet age, ENS names, and activity tags. Detect whale concentration, rug pull risk, smart money accumulation, and insider trading patterns. Essential for token due diligence, investment risk assessment, and fraud detection. Supports 7 EVM chains.",
  requirementSchema: {
    type: "object",
    properties: {
      contract: { type: "string", description: "ERC-20 token contract address (0x...)" },
      chain: { type: "string", enum: EVM_CHAINS as unknown as string[] },
    },
    required: ["contract"],
  },
  validate(req) {
    if (req.contract === undefined || req.contract === null || req.contract === "") {
      return { valid: false, reason: "contract address is required" };
    }
    const a = validateEvmAddress(req.contract);
    if (!a.valid) return a;
    return validateChain(req.chain);
  },
  async execute(req, { client }) {
    const contract = String(req.contract).trim();
    const chain = (req.chain as string | undefined) ?? "ethereum";
    return await client.get(`/token/${encodeURIComponent(contract)}/holders`, { chain });
  },
};
