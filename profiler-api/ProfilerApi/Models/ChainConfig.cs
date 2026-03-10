namespace ProfilerApi.Models;

public class ChainConfig
{
    public string Name { get; set; } = string.Empty;
    public string RpcUrl { get; set; } = string.Empty;
    public int ChainId { get; set; }

    /// <summary>
    /// Etherscan V2 unified endpoint — uses chainid parameter for all chains.
    /// </summary>
    public const string EtherscanV2BaseUrl = "https://api.etherscan.io/v2/api";

    public static readonly Dictionary<string, ChainConfig> Chains = new()
    {
        ["ethereum"] = new ChainConfig
        {
            Name = "ethereum",
            RpcUrl = "https://eth-mainnet.g.alchemy.com/v2/{API_KEY}",
            ChainId = 1
        },
        ["base"] = new ChainConfig
        {
            Name = "base",
            RpcUrl = "https://base-mainnet.g.alchemy.com/v2/{API_KEY}",
            ChainId = 8453
        },
        ["arbitrum"] = new ChainConfig
        {
            Name = "arbitrum",
            RpcUrl = "https://arb-mainnet.g.alchemy.com/v2/{API_KEY}",
            ChainId = 42161
        },
        // v1.7: Multi-chain expansion
        ["polygon"] = new ChainConfig
        {
            Name = "polygon",
            RpcUrl = "https://polygon-mainnet.g.alchemy.com/v2/{API_KEY}",
            ChainId = 137
        },
        ["optimism"] = new ChainConfig
        {
            Name = "optimism",
            RpcUrl = "https://opt-mainnet.g.alchemy.com/v2/{API_KEY}",
            ChainId = 10
        },
        ["avalanche"] = new ChainConfig
        {
            Name = "avalanche",
            RpcUrl = "https://avax-mainnet.g.alchemy.com/v2/{API_KEY}",
            ChainId = 43114
        },
        ["bnb"] = new ChainConfig
        {
            Name = "bnb",
            RpcUrl = "https://bnb-mainnet.g.alchemy.com/v2/{API_KEY}",
            ChainId = 56
        }
    };

    /// <summary>
    /// Native token symbol per chain (v1.7).
    /// </summary>
    /// <summary>
    /// Whether a chain is Solana (non-EVM) — requires separate RPC handling.
    /// </summary>
    public static bool IsSolana(string chain) => chain == "solana";

    /// <summary>
    /// All supported chain names including Solana.
    /// </summary>
    public static readonly string[] AllChains =
        ["ethereum", "base", "arbitrum", "polygon", "optimism", "avalanche", "bnb", "solana"];

    public static string GetNativeSymbol(string chain) => chain switch
    {
        "polygon" => "MATIC",
        "avalanche" => "AVAX",
        "bnb" => "BNB",
        "solana" => "SOL",
        _ => "ETH"
    };

    /// <summary>
    /// DeFi Llama price key for native token (v1.7).
    /// </summary>
    public static string GetNativePriceKey(string chain) => chain switch
    {
        "polygon" => "coingecko:matic-network",
        "avalanche" => "coingecko:avalanche-2",
        "bnb" => "coingecko:binancecoin",
        "solana" => "coingecko:solana",
        _ => "coingecko:ethereum"
    };
}
