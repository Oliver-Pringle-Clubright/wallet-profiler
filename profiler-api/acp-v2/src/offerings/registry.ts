import type { Offering } from "./types.js";
import { walletprofiler } from "./walletprofiler.js";
import { quickcheck } from "./quickcheck.js";
import { walletstatus } from "./walletstatus.js";
import { riskscore } from "./riskscore.js";
import { multichain } from "./multichain.js";
import { walletcompare } from "./walletcompare.js";
import { deepanalysis } from "./deepanalysis.js";
import { aianalyze } from "./aianalyze.js";
import { airdrops } from "./airdrops.js";
import { approvalaudit } from "./approvalaudit.js";
import { gasspend } from "./gasspend.js";
import { identity } from "./identity.js";
import { liquidationrisk } from "./liquidationrisk.js";
import { lppositions } from "./lppositions.js";
import { pnl } from "./pnl.js";
import { portfoliohistory } from "./portfoliohistory.js";
import { rebalance } from "./rebalance.js";
import { reputation } from "./reputation.js";
import { tokenholders } from "./tokenholders.js";
import { tokenscreen } from "./tokenscreen.js";
import { virtualsintel } from "./virtualsintel.js";
import { whalealerts } from "./whalealerts.js";

export const OFFERINGS: Record<string, Offering> = {
  walletprofiler, quickcheck, walletstatus, riskscore,
  multichain, walletcompare, deepanalysis,
  aianalyze, airdrops, approvalaudit, gasspend, identity,
  liquidationrisk, lppositions, pnl, portfoliohistory, rebalance, reputation,
  tokenholders, tokenscreen, virtualsintel, whalealerts,
};

export function getOffering(name: string): Offering | undefined {
  return OFFERINGS[name];
}

export function listOfferings(): string[] {
  return Object.keys(OFFERINGS);
}
