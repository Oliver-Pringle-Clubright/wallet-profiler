import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const airdrops: Offering = {
  name: "airdrops",
  description:
    "Airdrop eligibility checker — evaluates wallet against criteria for LayerZero, zkSync, Starknet, Scroll, Linea, EigenLayer, and Pendle. Returns eligibility status (eligible/likely/possible/ineligible), criteria breakdown, and evidence for each protocol. Critical for DeFi airdrop farming strategy.",
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
    return await client.get(`/airdrops/${address}`, { chain });
  },
};
