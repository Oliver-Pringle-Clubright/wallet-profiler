using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Web3;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Detects Uniswap V3 LP positions by reading the NonfungiblePositionManager contract.
/// Returns position details: token pair, fee tier, liquidity, uncollected fees.
/// </summary>
public class LpPositionService
{
    private readonly ILogger<LpPositionService> _logger;
    private readonly ContractLabelService _labelService;

    // Uniswap V3 NonfungiblePositionManager (Ethereum mainnet)
    private const string UniV3PositionManager = "0xC36442b4a4522E871399CD717aBDD847Ab11FE88";
    private const int MaxPositionsToCheck = 10;

    // Known token addresses -> symbols for common pairs
    private static readonly Dictionary<string, string> KnownTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2"] = "WETH",
        ["0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48"] = "USDC",
        ["0xdAC17F958D2ee523a2206206994597C13D831ec7"] = "USDT",
        ["0x6B175474E89094C44Da98b954EedeAC495271d0F"] = "DAI",
        ["0x2260FAC5E5542a773Aa44fBCfeDf7C193bc2C599"] = "WBTC",
        ["0x514910771AF9Ca656af840dff83E8264EcF986CA"] = "LINK",
        ["0x1f9840a85d5aF5bf1D1762F925BDADdC4201F984"] = "UNI",
        ["0x7Fc66500c84A76Ad7e9c93437bFc5Ac33E2DDaE9"] = "AAVE",
        ["0xae7ab96520DE3A18E5e111B5EaAb095312D7fE84"] = "stETH",
        ["0x7f39C581F595B53c5cb19bD0b3f8dA6c935E2Ca0"] = "wstETH",
        ["0xae78736Cd615f374D3085123A210448E74Fc6393"] = "rETH",
        ["0xBe9895146f7AF43049ca1c1AE358B0541Ea49704"] = "cbETH",
        ["0xD533a949740bb3306d119CC777fa900bA034cd52"] = "CRV",
        ["0x9f8F72aA9304c8B593d555F12eF6589cC3A579A2"] = "MKR",
        ["0xC011a73ee8576Fb46F5E1c5751cA3B9Fe0af2a6F"] = "SNX",
        ["0x853d955aCEf822Db058eb8505911ED77F175b99e"] = "FRAX",
        ["0x5A98FcBEA516Cf06857215779Fd812CA3beF1B32"] = "LDO",
        ["0xBBBBBbbBBb9cC5e90e3b3Af64bdAF62C37EEFFCb"] = "MORPHO",
        ["0x6982508145454Ce325dDbE47a25d4ec3d2311933"] = "PEPE",
        ["0x95aD61b0a150d79219dCF64E1E6Cc01f0B64C4cE"] = "SHIB",
    };

    public LpPositionService(ILogger<LpPositionService> logger, ContractLabelService labelService)
    {
        _logger = logger;
        _labelService = labelService;
    }

    public async Task<List<LpPosition>> GetPositionsAsync(Web3 web3, string address, string chain)
    {
        if (chain != "ethereum") return [];

        try
        {
            // Step 1: Get number of LP positions
            var balanceHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();
            var balance = await balanceHandler.QueryAsync<BigInteger>(
                UniV3PositionManager,
                new BalanceOfFunction { Owner = address });

            var count = (int)Math.Min((long)balance, MaxPositionsToCheck);
            if (count == 0) return [];

            // Step 2: Get token IDs for each position
            var tokenIdHandler = web3.Eth.GetContractQueryHandler<TokenOfOwnerByIndexFunction>();
            var tokenIdTasks = Enumerable.Range(0, count).Select(async i =>
            {
                try
                {
                    return await tokenIdHandler.QueryAsync<BigInteger>(
                        UniV3PositionManager,
                        new TokenOfOwnerByIndexFunction { Owner = address, Index = new BigInteger(i) });
                }
                catch
                {
                    return BigInteger.Zero;
                }
            });

            var tokenIds = await Task.WhenAll(tokenIdTasks);
            var validIds = tokenIds.Where(id => id > 0).ToList();
            if (validIds.Count == 0) return [];

            // Step 3: Get position details for each token ID
            var positionHandler = web3.Eth.GetContractQueryHandler<PositionsFunction>();
            var positionTasks = validIds.Select(async tokenId =>
            {
                try
                {
                    var result = await positionHandler.QueryDeserializingToObjectAsync<PositionsOutput>(
                        new PositionsFunction { TokenId = tokenId },
                        UniV3PositionManager);

                    var token0 = result.Token0 ?? "";
                    var token1 = result.Token1 ?? "";
                    var liquidity = result.Liquidity;
                    var tokensOwed0 = Web3.Convert.FromWei(result.TokensOwed0);
                    var tokensOwed1 = Web3.Convert.FromWei(result.TokensOwed1);

                    var status = liquidity == 0 ? "closed" : "active";

                    return new LpPosition
                    {
                        Protocol = "Uniswap V3",
                        TokenId = (int)(long)tokenId,
                        Token0Symbol = LookupSymbol(token0),
                        Token1Symbol = LookupSymbol(token1),
                        Token0Address = token0,
                        Token1Address = token1,
                        FeeTier = (int)result.Fee,
                        Liquidity = (decimal)liquidity,
                        TokensOwed0 = tokensOwed0 > 0 ? tokensOwed0 : null,
                        TokensOwed1 = tokensOwed1 > 0 ? tokensOwed1 : null,
                        InRange = liquidity > 0, // Simplified — full range check needs sqrtPriceX96
                        Status = status
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to read position {TokenId}", tokenId);
                    return null;
                }
            });

            var positions = await Task.WhenAll(positionTasks);
            return positions.Where(p => p != null).Cast<LpPosition>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Uniswap V3 LP positions for {Address}", address);
            return [];
        }
    }

    private string LookupSymbol(string contractAddress)
    {
        if (KnownTokens.TryGetValue(contractAddress, out var symbol))
            return symbol;

        // Fall back to ContractLabelService
        var (label, _) = _labelService.GetLabel(contractAddress);
        return label ?? (contractAddress.Length >= 10 ? contractAddress[..10] + "..." : contractAddress);
    }

    // --- ABI Bindings ---

    [Function("balanceOf", "uint256")]
    private class BalanceOfFunction : FunctionMessage
    {
        [Parameter("address", "owner", 1)]
        public string Owner { get; set; } = string.Empty;
    }

    [Function("tokenOfOwnerByIndex", "uint256")]
    private class TokenOfOwnerByIndexFunction : FunctionMessage
    {
        [Parameter("address", "owner", 1)]
        public string Owner { get; set; } = string.Empty;

        [Parameter("uint256", "index", 2)]
        public BigInteger Index { get; set; }
    }

    [Function("positions", typeof(PositionsOutput))]
    private class PositionsFunction : FunctionMessage
    {
        [Parameter("uint256", "tokenId", 1)]
        public BigInteger TokenId { get; set; }
    }

    [FunctionOutput]
    private class PositionsOutput : IFunctionOutputDTO
    {
        [Parameter("uint96", "nonce", 1)]
        public BigInteger Nonce { get; set; }

        [Parameter("address", "operator", 2)]
        public string? Operator { get; set; }

        [Parameter("address", "token0", 3)]
        public string? Token0 { get; set; }

        [Parameter("address", "token1", 4)]
        public string? Token1 { get; set; }

        [Parameter("uint24", "fee", 5)]
        public BigInteger Fee { get; set; }

        [Parameter("int24", "tickLower", 6)]
        public BigInteger TickLower { get; set; }

        [Parameter("int24", "tickUpper", 7)]
        public BigInteger TickUpper { get; set; }

        [Parameter("uint128", "liquidity", 8)]
        public BigInteger Liquidity { get; set; }

        [Parameter("uint256", "feeGrowthInside0LastX128", 9)]
        public BigInteger FeeGrowthInside0LastX128 { get; set; }

        [Parameter("uint256", "feeGrowthInside1LastX128", 10)]
        public BigInteger FeeGrowthInside1LastX128 { get; set; }

        [Parameter("uint128", "tokensOwed0", 11)]
        public BigInteger TokensOwed0 { get; set; }

        [Parameter("uint128", "tokensOwed1", 12)]
        public BigInteger TokensOwed1 { get; set; }
    }
}
