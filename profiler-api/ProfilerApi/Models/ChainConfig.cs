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
        }
    };
}
