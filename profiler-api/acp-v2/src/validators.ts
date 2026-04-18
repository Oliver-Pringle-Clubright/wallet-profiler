export interface ValidationResult {
  valid: boolean;
  reason?: string;
}

export const EVM_CHAINS = [
  "ethereum",
  "base",
  "arbitrum",
  "polygon",
  "optimism",
  "avalanche",
  "bnb",
] as const;

export const EVM_CHAINS_PLUS_SOLANA = [...EVM_CHAINS, "solana"] as const;

export const TIERS = ["free", "basic", "standard", "premium"] as const;
export type Tier = (typeof TIERS)[number];

const HEX_ADDRESS = /^0x[a-fA-F0-9]{40}$/;
const SOLANA_ADDRESS = /^[1-9A-HJ-NP-Za-km-z]{32,44}$/;

export function validateAddress(raw: unknown): ValidationResult {
  if (typeof raw !== "string" || raw.length === 0) {
    return { valid: false, reason: "address is required" };
  }
  const addr = raw.trim();
  if (HEX_ADDRESS.test(addr)) return { valid: true };
  if (addr.endsWith(".eth")) return { valid: true };
  if (SOLANA_ADDRESS.test(addr)) return { valid: true };
  return { valid: false, reason: `invalid address format: ${addr}` };
}

export function validateEvmAddress(raw: unknown): ValidationResult {
  if (typeof raw !== "string" || raw.length === 0) {
    return { valid: false, reason: "address is required" };
  }
  const addr = raw.trim();
  if (HEX_ADDRESS.test(addr)) return { valid: true };
  return { valid: false, reason: `invalid EVM address: ${addr}` };
}

export function validateChain(
  raw: unknown,
  allowed: readonly string[] = EVM_CHAINS
): ValidationResult {
  if (raw === undefined || raw === null) return { valid: true };
  if (typeof raw !== "string" || !allowed.includes(raw)) {
    return { valid: false, reason: `chain must be one of: ${allowed.join(", ")}` };
  }
  return { valid: true };
}

export function validateTier(raw: unknown): ValidationResult {
  if (raw === undefined || raw === null) return { valid: true };
  if (typeof raw !== "string" || !TIERS.includes(raw as Tier)) {
    return { valid: false, reason: `tier must be one of: ${TIERS.join(", ")}` };
  }
  return { valid: true };
}
