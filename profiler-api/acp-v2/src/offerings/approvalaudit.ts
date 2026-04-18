import type { Offering } from "./types.js";
import { validateAddress } from "../validators.js";

export const approvalaudit: Offering = {
  name: "approvalaudit",
  description:
    "Token approval security audit — scans a wallet's outstanding ERC-20 and ERC-721 approvals, flags risky unlimited approvals, identifies suspicious spender contracts, and provides revocation recommendations. Essential for wallet security hygiene.",
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
    const address = String(req.address);
    const profile = await client.profile({ address, tier: "standard" }) as {
      approvalRisk?: { totalApprovals?: number; riskyApprovals?: number } | null;
      revokeAdvice?: unknown;
    };
    return {
      address,
      approvalRisk: profile.approvalRisk ?? null,
      revokeAdvice: profile.revokeAdvice ?? null,
      summary: profile.approvalRisk
        ? `Found ${profile.approvalRisk.totalApprovals ?? 0} approvals, ${profile.approvalRisk.riskyApprovals ?? 0} flagged as risky`
        : "No approval data available",
    };
  },
};
