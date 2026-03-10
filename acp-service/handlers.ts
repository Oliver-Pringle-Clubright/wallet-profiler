// ACP Service Handler — thin proxy to the C# Wallet Profiler API
// The C# API runs on localhost:5000 and does all the heavy lifting.

const PROFILER_API_URL = process.env.PROFILER_API_URL || "http://localhost:5000";

const VALID_CHAINS = ["ethereum", "base", "arbitrum", "polygon", "optimism", "avalanche", "bnb"];
const VALID_TIERS = ["free", "basic", "standard", "premium"];

interface Job {
  id: string;
  requirements: {
    address: string;
    chain?: string;
    tier?: string;
  };
}

export async function validateRequirements(requirements: Record<string, unknown>): Promise<{ valid: boolean; error?: string }> {
  const address = requirements.address as string;

  if (!address) {
    return { valid: false, error: "address is required" };
  }

  const addresses = address.split(",").map((a: string) => a.trim()).filter(Boolean);
  for (const addr of addresses) {
    const isHexAddress = /^0x[a-fA-F0-9]{40}$/.test(addr);
    const isEns = addr.endsWith(".eth");
    if (!isHexAddress && !isEns) {
      return { valid: false, error: `invalid address: ${addr} — must be a valid 0x address or .eth ENS name` };
    }
  }

  const chain = requirements.chain as string | undefined;
  if (chain && !VALID_CHAINS.includes(chain)) {
    return { valid: false, error: `chain must be one of: ${VALID_CHAINS.join(", ")}` };
  }

  const tier = requirements.tier as string | undefined;
  if (tier && !VALID_TIERS.includes(tier)) {
    return { valid: false, error: `tier must be one of: ${VALID_TIERS.join(", ")}` };
  }

  return { valid: true };
}

export async function executeJob(job: Job): Promise<unknown> {
  const { address, chain = "ethereum", tier = "standard" } = job.requirements;

  // Support batch: if address contains commas, use batch endpoint
  const addresses = address.split(",").map((a: string) => a.trim()).filter(Boolean);

  if (addresses.length > 1) {
    const response = await fetch(`${PROFILER_API_URL}/profile/batch`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ addresses, chain, tier }),
    });

    if (!response.ok) {
      const error = await response.text();
      throw new Error(`Profiler API error (${response.status}): ${error}`);
    }

    return response.json();
  }

  const response = await fetch(`${PROFILER_API_URL}/profile`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ address, chain, tier }),
  });

  if (!response.ok) {
    const error = await response.text();
    throw new Error(`Profiler API error (${response.status}): ${error}`);
  }

  return response.json();
}
