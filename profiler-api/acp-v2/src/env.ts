export type ChainName = "base" | "baseSepolia";

export interface AcpEnv {
  walletAddress: string;
  walletId: string;
  signerPrivateKey: string;
  chain: ChainName;
  profilerApiUrl: string;
  builderCode?: string;
}

const REQUIRED = [
  "ACP_WALLET_ADDRESS",
  "ACP_WALLET_ID",
  "ACP_SIGNER_PRIVATE_KEY",
  "ACP_CHAIN",
  "PROFILER_API_URL",
] as const;

export function loadEnv(source: NodeJS.ProcessEnv = process.env): AcpEnv {
  for (const name of REQUIRED) {
    if (!source[name] || source[name] === "") {
      throw new Error(`Missing required env var: ${name}`);
    }
  }

  const chain = source.ACP_CHAIN;
  if (chain !== "base" && chain !== "baseSepolia") {
    throw new Error(`ACP_CHAIN must be "base" or "baseSepolia", got "${chain}"`);
  }

  return {
    walletAddress: source.ACP_WALLET_ADDRESS!,
    walletId: source.ACP_WALLET_ID!,
    signerPrivateKey: source.ACP_SIGNER_PRIVATE_KEY!,
    chain,
    profilerApiUrl: source.PROFILER_API_URL!,
    builderCode: source.ACP_BUILDER_CODE || undefined,
  };
}
