using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class ContractLabelService
{
    private static readonly Dictionary<string, (string Label, string Category)> KnownContracts = new(StringComparer.OrdinalIgnoreCase)
    {
        // DEXes
        ["0x7a250d5630B4cF539739dF2C5dAcb4c659F2488D"] = ("Uniswap V2 Router", "dex"),
        ["0xE592427A0AEce92De3Edee1F18E0157C05861564"] = ("Uniswap V3 Router", "dex"),
        ["0x3fC91A3afd70395Cd496C647d5a6CC9D4B2b7FAD"] = ("Uniswap Universal Router", "dex"),
        ["0x68b3465833fb72A70ecDF485E0e4C7bD8665Fc45"] = ("Uniswap V3 Router 2", "dex"),
        ["0xd9e1cE17f2641f24aE83637ab66a2cca9C378B9F"] = ("SushiSwap Router", "dex"),
        ["0x1111111254EEB25477B68fb85Ed929f73A960582"] = ("1inch V5 Router", "dex"),
        ["0x1111111254fb6c44bAC0beD2854e76F90643097d"] = ("1inch V4 Router", "dex"),
        ["0xDef1C0ded9bec7F1a1670819833240f027b25EfF"] = ("0x Exchange Proxy", "dex"),
        ["0xbEbc44782C7dB0a1A60Cb6fe97d0b483032FF1C7"] = ("Curve 3pool", "dex"),
        ["0xD51a44d3FaE010294C616388b506AcdA1bfAAE46"] = ("Curve Tricrypto2", "dex"),

        // NFT Marketplaces
        ["0x00000000000000ADc04C56Bf30aC9d3c0aAF14dC"] = ("OpenSea Seaport 1.5", "nft"),
        ["0x00000000006c3852cbEf3e08E8dF289169EdE581"] = ("OpenSea Seaport 1.1", "nft"),
        ["0x29469395eAf6f95920E59F858042f0e28D98a20B"] = ("Blur Marketplace", "nft"),
        ["0x0000000000E655fAe4d56241588680F86E3b2377"] = ("LooksRare Exchange", "nft"),

        // Lending
        ["0x87870Bca3F3fD6335C3F4ce8392D69350B4fA4E2"] = ("Aave V3 Pool", "lending"),
        ["0x7d2768dE32b0b80b7a3454c06BdAc94A69DDc7A9"] = ("Aave V2 Pool", "lending"),
        ["0xc3d688B66703497DAA19211EEdff47f25384cdc3"] = ("Compound V3 cUSDC", "lending"),
        ["0x3d9819210A31b4961b30EF54bE2aeD79B9c9Cd3B"] = ("Compound Comptroller", "lending"),

        // Bridges
        ["0x3ee18B2214AFF97000D974cf647E7C347E8fa585"] = ("Wormhole Token Bridge", "bridge"),
        ["0x8731d54E9D02c286767d56ac03e8037C07e01e98"] = ("Stargate Router", "bridge"),
        ["0x4Dbd4fc535Ac27206064B68FfCf827b0A60BAB3f"] = ("Arbitrum Inbox", "bridge"),
        ["0x99C9fc46f92E8a1c0deC1b1747d010903E884bE1"] = ("Optimism Bridge", "bridge"),
        ["0x3154Cf16ccdb4C6d922629664174b904d80F2C35"] = ("Base Bridge", "bridge"),

        // Staking
        ["0xae7ab96520DE3A18E5e111B5EaAb095312D7fE84"] = ("Lido stETH", "staking"),
        ["0x7f39C581F595B53c5cb19bD0b3f8dA6c935E2Ca0"] = ("Lido wstETH", "staking"),
        ["0xBe9895146f7AF43049ca1c1AE358B0541Ea49704"] = ("Coinbase cbETH", "staking"),
        ["0xf951E335afb289353dc249e82926178EaC7DEd78"] = ("Rocket Pool rETH Swap", "staking"),

        // Tokens
        ["0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2"] = ("WETH", "token"),
        ["0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48"] = ("USDC", "token"),
        ["0xdAC17F958D2ee523a2206206994597C13D831ec7"] = ("USDT", "token"),
        ["0x6B175474E89094C44Da98b954EedeAC495271d0F"] = ("DAI", "token"),
        ["0x2260FAC5E5542a773Aa44fBCfeDf7C193bc2C599"] = ("WBTC", "token"),

        // Mixers
        ["0xd90e2f925DA726b50C4Ed8D0Fb90Ad053324F31b"] = ("Tornado Cash Router", "mixer"),
        ["0x12D66f87A04A9E220743712cE6d9bB1B5616B8Fc"] = ("Tornado Cash 0.1 ETH", "mixer"),
        ["0x47CE0C6eD5B0Ce3d3A51fdb1C52DC66a7c3c2936"] = ("Tornado Cash 1 ETH", "mixer"),
        ["0x910Cbd523D972eb0a6f4cAe4618aD62622b39DbF"] = ("Tornado Cash 10 ETH", "mixer"),
        ["0xA160cdAB225685dA1d56aa342Ad8841c3b53f291"] = ("Tornado Cash 100 ETH", "mixer"),

        // Identity / Governance
        ["0x57f1887a8BF19b14fC0dF6Fd9B2acc9Af147eA85"] = ("ENS Registrar", "identity"),
        ["0xC18360217D8F7Ab5e7c516566761Ea12Ce7F9D72"] = ("ENS Token", "governance"),
        ["0x1f9840a85d5aF5bf1D1762F925BDADdC4201F984"] = ("Uniswap (UNI)", "governance"),
        ["0x7Fc66500c84A76Ad7e9c93437bFc5Ac33E2DDaE9"] = ("Aave (AAVE)", "governance"),
        ["0x9f8F72aA9304c8B593d555F12eF6589cC3A579A2"] = ("Maker (MKR)", "governance"),

        // System
        ["0x0000000000000000000000000000000000000000"] = ("Null Address (Contract Creation)", "system"),
        ["0x000000000000000000000000000000000000dEaD"] = ("Burn Address", "system"),
    };

    public (string? Label, string? Category) GetLabel(string address)
    {
        if (KnownContracts.TryGetValue(address, out var info))
            return (info.Label, info.Category);
        return (null, null);
    }

    public List<ContractInteraction> LabelInteractions(List<(string Address, int TxCount)> rawInteractions)
    {
        return rawInteractions.Select(i =>
        {
            var (label, category) = GetLabel(i.Address);
            return new ContractInteraction
            {
                Address = i.Address,
                Label = label,
                Category = category,
                TransactionCount = i.TxCount
            };
        }).ToList();
    }
}
