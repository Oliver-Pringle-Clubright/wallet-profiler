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

    // Lido
    private const string LidoStETH = "0xae7ab96520DE3A18E5e111B5EaAb095312D7fE84";
    private const string LidoWstETH = "0x7f39C581F595B53c5cb19bD0b3f8dA6c935E2Ca0";

    // Rocket Pool
    private const string RocketPoolRETH = "0xae78736Cd615f374D3085123A210448E74Fc6393";

    // Coinbase
    private const string CoinbaseCbETH = "0xBe9895146f7AF43049ca1c1AE358B0541Ea49704";

    // EtherFi
    private const string EtherFiWeETH = "0xCd5fE23C85820F7B72D0926FC9b05b43E359b7ee";

    // MakerDAO
    private const string MakerSDAI = "0x83F20F44975D03b1b09e64809B757c47f942BEeA";

    // EigenLayer
    private const string EigenLayerStrategyManager = "0x858646372CC42E1A627fcE94aa7A7033e7CF075A";
    private const string EigenLayerStETHStrategy = "0x93c4b944D05dfe6df7645A86cd2206016c51564D";
    private const string EigenLayerCbETHStrategy = "0x54945180dB7943c0ed0FEE7EdaB2Bd24620256Bc";
    private const string EigenLayerRETHStrategy = "0x1BeE69b7dFFfA4E2d53C2a2Df135C388AD25dCD2";

    // Ethena
    private const string EthenaSUSDe = "0x9D39A5DE30e57443BfF2A8307A4256c8797A3497";

    // Frax
    private const string FraxSfrxETH = "0xac3E018457B222d93114458476f3E3416Abbe38F";

    // Morpho Blue — MetaMorpho vaults (ERC-4626, standard balanceOf)
    private const string MorphoGauntletWETH = "0x38989BBA00BDF8181F4082995b3DEAe96163aC5D";
    private const string MorphoGauntletUSDC = "0x8eB67A509616cd6A7c1B3c8C21D48FF57df3d458";
    private const string MorphoSteakhouseUSDC = "0xBEEF01735c132Ada46AA9aA4c54623cAA92A64CB";
    private const string MorphoRe7WETH = "0x78Fc2c2eD71dAb0491d268b4e7EFBFe8Abb768C8";

    // Aura Finance
    private const string AuraBAL = "0x616e8BfA43F920657B3497DBf40D6b1A02D4608d";

    // Pendle — we detect PT/YT tokens from existing token balances (symbol-based)
    // No additional contract calls needed — handled via token list analysis

    public DeFiService(ILogger<DeFiService> logger)
    {
        _logger = logger;
    }

    public async Task<List<DeFiPosition>> GetPositionsAsync(Web3 web3, string address, string chain)
    {
        var positions = new List<DeFiPosition>();

        // Run all protocol checks in parallel
        var tasks = new List<Task<List<DeFiPosition>>>
        {
            GetAavePositionsAsync(web3, address, chain),
            GetCompoundPositionAsync(web3, address, chain),
            GetLidoPositionsAsync(web3, address, chain),
            GetLiquidStakingPositionsAsync(web3, address, chain),
            GetMakerPositionsAsync(web3, address, chain),
            GetEigenLayerPositionsAsync(web3, address, chain),
            GetYieldPositionsAsync(web3, address, chain),
            GetMorphoPositionsAsync(web3, address, chain),
            GetAuraPositionsAsync(web3, address, chain),
        };

        var results = await Task.WhenAll(tasks);
        foreach (var result in results)
            positions.AddRange(result);

        return positions;
    }

    private async Task<List<DeFiPosition>> GetAavePositionsAsync(Web3 web3, string address, string chain)
    {
        try
        {
            var getUserAccountDataHandler = web3.Eth.GetContractQueryHandler<GetUserAccountDataFunction>();
            var result = await getUserAccountDataHandler.QueryDeserializingToObjectAsync<GetUserAccountDataOutput>(
                new GetUserAccountDataFunction { User = address },
                AaveV3Pool
            );

            var positions = new List<DeFiPosition>();
            var collateral = Web3.Convert.FromWei(result.TotalCollateralBase, 8);
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

    private async Task<List<DeFiPosition>> GetLidoPositionsAsync(Web3 web3, string address, string chain)
    {
        if (chain != "ethereum") return [];
        var positions = new List<DeFiPosition>();

        try
        {
            var balanceOfHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();

            // Check stETH balance
            var stEthTask = balanceOfHandler.QueryAsync<BigInteger>(
                LidoStETH, new BalanceOfFunction { Account = address });
            // Check wstETH balance
            var wstEthTask = balanceOfHandler.QueryAsync<BigInteger>(
                LidoWstETH, new BalanceOfFunction { Account = address });

            await Task.WhenAll(stEthTask, wstEthTask);

            var stEthBalance = Web3.Convert.FromWei(stEthTask.Result);
            var wstEthBalance = Web3.Convert.FromWei(wstEthTask.Result);

            if (stEthBalance > 0.001m)
            {
                positions.Add(new DeFiPosition
                {
                    Protocol = "Lido",
                    Type = "staking",
                    Asset = "stETH",
                    Amount = stEthBalance
                });
            }

            if (wstEthBalance > 0.001m)
            {
                positions.Add(new DeFiPosition
                {
                    Protocol = "Lido",
                    Type = "staking",
                    Asset = "wstETH",
                    Amount = wstEthBalance
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Lido positions for {Address}", address);
        }

        return positions;
    }

    private async Task<List<DeFiPosition>> GetLiquidStakingPositionsAsync(Web3 web3, string address, string chain)
    {
        if (chain != "ethereum") return [];
        var positions = new List<DeFiPosition>();

        var checks = new (string Contract, string Protocol, string Asset)[]
        {
            (RocketPoolRETH, "Rocket Pool", "rETH"),
            (CoinbaseCbETH, "Coinbase", "cbETH"),
            (EtherFiWeETH, "EtherFi", "weETH"),
            (FraxSfrxETH, "Frax", "sfrxETH"),
        };

        var balanceOfHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();
        var tasks = checks.Select(async c =>
        {
            try
            {
                var balance = await balanceOfHandler.QueryAsync<BigInteger>(
                    c.Contract, new BalanceOfFunction { Account = address });
                var amount = Web3.Convert.FromWei(balance);
                if (amount > 0.001m)
                {
                    return new DeFiPosition
                    {
                        Protocol = c.Protocol,
                        Type = "staking",
                        Asset = c.Asset,
                        Amount = amount
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check {Asset} balance for {Address}", c.Asset, address);
            }
            return null;
        });

        var results = await Task.WhenAll(tasks);
        positions.AddRange(results.Where(r => r != null)!);
        return positions;
    }

    private async Task<List<DeFiPosition>> GetMakerPositionsAsync(Web3 web3, string address, string chain)
    {
        if (chain != "ethereum") return [];

        try
        {
            var balanceOfHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();

            // Check sDAI balance (MakerDAO Savings DAI)
            var balance = await balanceOfHandler.QueryAsync<BigInteger>(
                MakerSDAI, new BalanceOfFunction { Account = address });
            var amount = Web3.Convert.FromWei(balance);

            if (amount > 0.01m)
            {
                return
                [
                    new DeFiPosition
                    {
                        Protocol = "MakerDAO",
                        Type = "savings",
                        Asset = "sDAI",
                        Amount = amount
                    }
                ];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch MakerDAO position for {Address}", address);
        }

        return [];
    }

    private async Task<List<DeFiPosition>> GetEigenLayerPositionsAsync(Web3 web3, string address, string chain)
    {
        if (chain != "ethereum") return [];
        var positions = new List<DeFiPosition>();

        var strategies = new (string Contract, string Asset)[]
        {
            (EigenLayerStETHStrategy, "stETH (restaked)"),
            (EigenLayerCbETHStrategy, "cbETH (restaked)"),
            (EigenLayerRETHStrategy, "rETH (restaked)"),
        };

        var sharesHandler = web3.Eth.GetContractQueryHandler<SharesFunction>();
        var tasks = strategies.Select(async s =>
        {
            try
            {
                var shares = await sharesHandler.QueryAsync<BigInteger>(
                    s.Contract, new SharesFunction { Account = address });
                var amount = Web3.Convert.FromWei(shares);
                if (amount > 0.001m)
                {
                    return new DeFiPosition
                    {
                        Protocol = "EigenLayer",
                        Type = "restaking",
                        Asset = s.Asset,
                        Amount = amount
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check EigenLayer {Asset} for {Address}", s.Asset, address);
            }
            return null;
        });

        var results = await Task.WhenAll(tasks);
        positions.AddRange(results.Where(r => r != null)!);
        return positions;
    }

    private async Task<List<DeFiPosition>> GetYieldPositionsAsync(Web3 web3, string address, string chain)
    {
        if (chain != "ethereum") return [];
        var positions = new List<DeFiPosition>();

        var checks = new (string Contract, string Protocol, string Asset, int Decimals)[]
        {
            (EthenaSUSDe, "Ethena", "sUSDe", 18),
        };

        var balanceOfHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();
        var tasks = checks.Select(async c =>
        {
            try
            {
                var balance = await balanceOfHandler.QueryAsync<BigInteger>(
                    c.Contract, new BalanceOfFunction { Account = address });
                var amount = Web3.Convert.FromWei(balance, c.Decimals);
                if (amount > 0.01m)
                {
                    return new DeFiPosition
                    {
                        Protocol = c.Protocol,
                        Type = "yield",
                        Asset = c.Asset,
                        Amount = amount
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check {Asset} for {Address}", c.Asset, address);
            }
            return null;
        });

        var results = await Task.WhenAll(tasks);
        positions.AddRange(results.Where(r => r != null)!);
        return positions;
    }

    private async Task<List<DeFiPosition>> GetMorphoPositionsAsync(Web3 web3, string address, string chain)
    {
        if (chain != "ethereum") return [];
        var positions = new List<DeFiPosition>();

        var vaults = new (string Contract, string Asset, int Decimals)[]
        {
            (MorphoGauntletWETH, "WETH (Gauntlet)", 18),
            (MorphoGauntletUSDC, "USDC (Gauntlet)", 6),
            (MorphoSteakhouseUSDC, "USDC (Steakhouse)", 6),
            (MorphoRe7WETH, "WETH (Re7)", 18),
        };

        var balanceOfHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();
        var tasks = vaults.Select(async v =>
        {
            try
            {
                var balance = await balanceOfHandler.QueryAsync<BigInteger>(
                    v.Contract, new BalanceOfFunction { Account = address });
                var amount = Web3.Convert.FromWei(balance, v.Decimals);
                if (amount > (v.Decimals == 6 ? 0.01m : 0.001m))
                {
                    return new DeFiPosition
                    {
                        Protocol = "Morpho",
                        Type = "lending",
                        Asset = v.Asset,
                        Amount = amount
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check Morpho {Asset} for {Address}", v.Asset, address);
            }
            return null;
        });

        var results = await Task.WhenAll(tasks);
        positions.AddRange(results.Where(r => r != null)!);
        return positions;
    }

    private async Task<List<DeFiPosition>> GetAuraPositionsAsync(Web3 web3, string address, string chain)
    {
        if (chain != "ethereum") return [];

        try
        {
            var balanceOfHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();
            var balance = await balanceOfHandler.QueryAsync<BigInteger>(
                AuraBAL, new BalanceOfFunction { Account = address });
            var amount = Web3.Convert.FromWei(balance);

            if (amount > 0.01m)
            {
                return
                [
                    new DeFiPosition
                    {
                        Protocol = "Aura Finance",
                        Type = "staking",
                        Asset = "auraBAL",
                        Amount = amount
                    }
                ];
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to check Aura position for {Address}", address);
        }

        return [];
    }

    /// <summary>
    /// Detects Pendle PT/YT positions from existing token balances.
    /// Call this after token discovery — no additional RPC calls needed.
    /// </summary>
    public List<DeFiPosition> DetectPendlePositions(List<TokenBalance> tokens)
    {
        var positions = new List<DeFiPosition>();

        foreach (var token in tokens)
        {
            if (token.IsSpam || token.Balance <= 0) continue;

            var sym = token.Symbol.ToUpperInvariant();
            if (sym.StartsWith("PT-"))
            {
                positions.Add(new DeFiPosition
                {
                    Protocol = "Pendle",
                    Type = "yield",
                    Asset = token.Symbol,
                    Amount = token.Balance
                });
            }
            else if (sym.StartsWith("YT-"))
            {
                positions.Add(new DeFiPosition
                {
                    Protocol = "Pendle",
                    Type = "yield",
                    Asset = token.Symbol,
                    Amount = token.Balance
                });
            }
            else if (sym == "PENDLE" && token.Balance > 0.01m)
            {
                // Pendle governance token — not a DeFi position per se, but indicates participation
                // Skip — this is a token holding, not a DeFi position
            }
        }

        return positions;
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

    // --- EigenLayer shares ---

    [Function("shares", "uint256")]
    private class SharesFunction : FunctionMessage
    {
        [Parameter("address", "user", 1)]
        public string Account { get; set; } = string.Empty;
    }
}
