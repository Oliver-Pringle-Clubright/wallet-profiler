import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const aianalyze: Offering = {
  name: "aianalyze",
  description:
    "AI-powered wallet analysis using Claude — feeds complete wallet profile into Claude for natural language Q&A. Supports custom questions about risk, strategy, portfolio optimization, and DeFi activity. Returns analysis text, key insights, and recommendations.",
  requirementSchema: {
    type: "object",
    properties: {
      address: { type: "string", description: "Wallet address (0x...) or ENS name" },
      question: { type: "string", description: "Natural language question (default: comprehensive analysis)" },
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
    const question = req.question as string | undefined;
    return await client.get(`/ai-analyze/${address}`, { chain, question });
  },
};
