using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Web3;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class DeFiService
{
    private readonly ILogger<DeFiService> _logger;

    // Aave V3 Pool (same address on Ethereum, Base, Arbitrum)
    private const string AaveV3Pool = "0x87870Bca3F3fD6335C3F4ce8392D69350B4fA4E2";

    // Compound V3 cUSDC (Ethereum)
    private const string CompoundV3USDC = "0xc3d688B66703497DAA19211EEdff47f25384cdc3";

    public DeFiService(ILogger<DeFiService> logger)
    {
        _logger = logger;
    }

    public async Task<List<DeFiPosition>> GetPositionsAsync(Web3 web3, string address, string chain)
    {
        var positions = new List<DeFiPosition>();

        var aaveTask = GetAavePositionsAsync(web3, address, chain);
        var compoundTask = GetCompoundPositionAsync(web3, address, chain);

        await Task.WhenAll(aaveTask, compoundTask);

        positions.AddRange(aaveTask.Result);
        positions.AddRange(compoundTask.Result);

        return positions;
    }

    private async Task<List<DeFiPosition>> GetAavePositionsAsync(Web3 web3, string address, string chain)
    {
        try
        {
            // Aave V3 getUserAccountData returns aggregate position info
            var getUserAccountDataHandler = web3.Eth.GetContractQueryHandler<GetUserAccountDataFunction>();
            var result = await getUserAccountDataHandler.QueryDeserializingToObjectAsync<GetUserAccountDataOutput>(
                new GetUserAccountDataFunction { User = address },
                AaveV3Pool
            );

            var positions = new List<DeFiPosition>();
            var collateral = Web3.Convert.FromWei(result.TotalCollateralBase, 8); // USD with 8 decimals
            var debt = Web3.Convert.FromWei(result.TotalDebtBase, 8);

            if (collateral > 0)
            {
                positions.Add(new DeFiPosition
                {
                    Protocol = "Aave V3",
                    Type = "lending",
                    Asset = "USD (aggregate)",
                    Amount = collateral
                });
            }

            if (debt > 0)
            {
                positions.Add(new DeFiPosition
                {
                    Protocol = "Aave V3",
                    Type = "borrowing",
                    Asset = "USD (aggregate)",
                    Amount = debt
                });
            }

            return positions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Aave positions for {Address}", address);
            return [];
        }
    }

    private async Task<List<DeFiPosition>> GetCompoundPositionAsync(Web3 web3, string address, string chain)
    {
        if (chain != "ethereum") return [];

        try
        {
            var balanceOfHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();
            var balance = await balanceOfHandler.QueryAsync<BigInteger>(
                CompoundV3USDC,
                new BalanceOfFunction { Account = address }
            );

            if (balance > 0)
            {
                return
                [
                    new DeFiPosition
                    {
                        Protocol = "Compound V3",
                        Type = "lending",
                        Asset = "USDC",
                        Amount = Web3.Convert.FromWei(balance, 6)
                    }
                ];
            }

            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Compound position for {Address}", address);
            return [];
        }
    }

    // --- Aave V3 ABI bindings ---

    [Function("getUserAccountData", typeof(GetUserAccountDataOutput))]
    private class GetUserAccountDataFunction : FunctionMessage
    {
        [Parameter("address", "user", 1)]
        public string User { get; set; } = string.Empty;
    }

    [FunctionOutput]
    private class GetUserAccountDataOutput : IFunctionOutputDTO
    {
        [Parameter("uint256", "totalCollateralBase", 1)]
        public BigInteger TotalCollateralBase { get; set; }

        [Parameter("uint256", "totalDebtBase", 2)]
        public BigInteger TotalDebtBase { get; set; }

        [Parameter("uint256", "availableBorrowsBase", 3)]
        public BigInteger AvailableBorrowsBase { get; set; }

        [Parameter("uint256", "currentLiquidationThreshold", 4)]
        public BigInteger CurrentLiquidationThreshold { get; set; }

        [Parameter("uint256", "ltv", 5)]
        public BigInteger Ltv { get; set; }

        [Parameter("uint256", "healthFactor", 6)]
        public BigInteger HealthFactor { get; set; }
    }

    // --- ERC-20 balanceOf ---

    [Function("balanceOf", "uint256")]
    private class BalanceOfFunction : FunctionMessage
    {
        [Parameter("address", "account", 1)]
        public string Account { get; set; } = string.Empty;
    }
}
