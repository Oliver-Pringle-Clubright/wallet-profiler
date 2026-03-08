// ACP Service Handler — thin proxy to the C# Wallet Profiler API
// The C# API runs on localhost:5000 and does all the heavy lifting.

const PROFILER_API_URL = process.env.PROFILER_API_URL || "http://localhost:5000";

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

  const isHexAddress = /^0x[a-fA-F0-9]{40}$/.test(address);
  const isEns = address.endsWith(".eth");

  if (!isHexAddress && !isEns) {
    return { valid: false, error: "address must be a valid 0x address or .eth ENS name" };
  }

  const chain = requirements.chain as string | undefined;
  if (chain && !["ethereum", "base", "arbitrum"].includes(chain)) {
    return { valid: false, error: "chain must be one of: ethereum, base, arbitrum" };
  }

  const tier = requirements.tier as string | undefined;
  if (tier && !["basic", "standard", "premium"].includes(tier)) {
    return { valid: false, error: "tier must be one of: basic, standard, premium" };
  }

  return { valid: true };
}

export async function executeJob(job: Job): Promise<unknown> {
  const { address, chain = "ethereum", tier = "standard" } = job.requirements;

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
