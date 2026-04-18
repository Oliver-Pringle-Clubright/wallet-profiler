import type { AssetToken } from "@virtuals-protocol/acp-node-v2";

export type Tier = "free" | "basic" | "standard" | "premium";

export const TIER_USDC: Record<Tier, number> = {
  free: 0,
  basic: 1,
  standard: 2,
  premium: 5,
};

export const OFFERING_OVERRIDES: Record<string, number> = {
  quickcheck: 0.5,
  walletstatus: 0.5,
  riskscore: 2,
  approvalaudit: 2,
  gasspend: 2,
  tokenscreen: 2,
  identity: 2,
  reputation: 2,
  aianalyze: 2,
  airdrops: 2,
  liquidationrisk: 2,
  lppositions: 2,
  pnl: 2,
  portfoliohistory: 2,
  rebalance: 2,
  tokenholders: 2,
  virtualsintel: 1,
  whalealerts: 10,
};

export interface Price {
  amount: number;
  token: "USDC";
}

export function tierUsdc(tier: string | undefined): number {
  const t = (tier as Tier | undefined) ?? "standard";
  return TIER_USDC[t] ?? TIER_USDC.standard;
}

export function priceFor(offeringName: string, requirement: Record<string, unknown>): Price {
  if (offeringName in OFFERING_OVERRIDES) {
    return { amount: OFFERING_OVERRIDES[offeringName]!, token: "USDC" };
  }
  return { amount: tierUsdc(requirement.tier as string | undefined), token: "USDC" };
}

export async function priceForAssetToken(
  offeringName: string,
  requirement: Record<string, unknown>,
  chainId: number
): Promise<AssetToken> {
  const price = priceFor(offeringName, requirement);
  const { AssetToken } = await import("@virtuals-protocol/acp-node-v2");
  return AssetToken.usdc(price.amount, chainId);
}
