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
  walletprofiler: 0.5,
  multichain: 0.5,
  walletcompare: 0.5,
  deepanalysis: 0.5,
  riskscore: 0.5,
  approvalaudit: 0.5,
  gasspend: 0.5,
  tokenscreen: 0.5,
  identity: 0.5,
  reputation: 0.5,
  aianalyze: 0.5,
  airdrops: 0.5,
  liquidationrisk: 0.5,
  lppositions: 0.5,
  pnl: 0.5,
  portfoliohistory: 0.5,
  rebalance: 0.5,
  tokenholders: 0.5,
  virtualsintel: 1,
  whalealerts: 1,
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
