import type { Offering } from "./types.js";

export const virtualsintel: Offering = {
  name: "virtualsintel",
  description:
    "Virtuals Protocol ecosystem intelligence (~2s response, cached). Returns $VIRTUAL token price, market cap, 24h volume, price changes. Tracks top AI agent tokens: $AIXBT, $GAME, $LUNA, $VADER, $SEKOIA, $AIMONICA. Includes ecosystem total market cap, 24h volume, health sentiment, and natural language summary.",
  requirementSchema: {
    type: "object",
    properties: {
      query: { type: "string", description: "Optional query about the Virtuals ecosystem." },
    },
    required: [],
  },
  validate() {
    return { valid: true };
  },
  async execute(req, { client }) {
    const query = req.query as string | undefined;
    return await client.get(`/virtuals/ecosystem`, query ? { query } : undefined);
  },
};
