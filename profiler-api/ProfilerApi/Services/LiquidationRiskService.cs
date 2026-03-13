using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Web3;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Monitors DeFi liquidation risk by checking Aave V3 health factor
/// and Compound V3 borrow balances. Returns risk levels and alerts.
/// </summary>
public class LiquidationRiskService
{
    private readonly ILogger<LiquidationRiskService> _logger;

    // Aave V3 Pool
    private const string AaveV3Pool = "0x87870Bca3F3fD6335C3F4ce8392D69350B4fA4E2";

    // Compound V3 cUSDC (Ethereum)
    private const string CompoundV3USDC = "0xc3d688B66703497DAA19211EEdff47f25384cdc3";

    public LiquidationRiskService(ILogger<LiquidationRiskService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyzes liquidation risk from Aave V3 and Compound V3 positions.
    /// </summary>
    public async Task<LiquidationRisk> AnalyzeAsync(Web3 web3, string address, string chain)
    {
        var risk = new LiquidationRisk();

        var aaveTask = GetAaveRiskAsync(web3, address, risk);
        var compoundTask = GetCompoundRiskAsync(web3, address, chain, risk);

        await Task.WhenAll(aaveTask, compoundTask);

        // Determine overall risk
        risk.OverallRisk = DetermineOverallRisk(risk);

        return risk;
    }

    private async Task GetAaveRiskAsync(Web3 web3, string address, LiquidationRisk risk)
    {
        try
        {
            var handler = web3.Eth.GetContractQueryHandler<GetUserAccountDataFunction>();
            var result = await handler.QueryDeserializingToObjectAsync<GetUserAccountDataOutput>(
                new GetUserAccountDataFunction { User = address },
                AaveV3Pool);

            var collateral = Web3.Convert.FromWei(result.TotalCollateralBase, 8);
            var debt = Web3.Convert.FromWei(result.TotalDebtBase, 8);

            risk.AaveCollateralUsd = collateral > 0 ? collateral : null;
            risk.AaveDebtUsd = debt > 0 ? debt : null;

            if (debt <= 0)
            {
                risk.AaveRiskLevel = "none";
                return;
            }

            // Health factor is returned as 18-decimal value, 1e18 = health factor of 1.0
            var healthFactor = Web3.Convert.FromWei(result.HealthFactor);
            risk.AaveHealthFactor = Math.Round(healthFactor, 4);

            // Risk levels based on health factor
            if (healthFactor < 1.0m)
            {
                risk.AaveRiskLevel = "danger";
                risk.Alerts.Add($"CRITICAL: Aave health factor {healthFactor:F2} — liquidation imminent!");
            }
            else if (healthFactor < 1.1m)
            {
                risk.AaveRiskLevel = "danger";
                risk.Alerts.Add($"DANGER: Aave health factor {healthFactor:F2} — extremely close to liquidation");
            }
            else if (healthFactor < 1.3m)
            {
                risk.AaveRiskLevel = "warning";
                risk.Alerts.Add($"WARNING: Aave health factor {healthFactor:F2} — liquidation risk is elevated");
            }
            else if (healthFactor < 1.5m)
            {
                risk.AaveRiskLevel = "watch";
                risk.Alerts.Add($"WATCH: Aave health factor {healthFactor:F2} — monitor closely during volatility");
            }
            else
            {
                risk.AaveRiskLevel = "safe";
            }

            // Additional context
            if (collateral > 0 && debt > 0)
            {
                var ltv = (debt / collateral) * 100;
                risk.Alerts.Add($"Aave LTV: {ltv:F1}% (collateral ${collateral:F0}, debt ${debt:F0})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to check Aave liquidation risk for {Address}", address);
            risk.AaveRiskLevel = "none";
        }
    }

    private async Task GetCompoundRiskAsync(Web3 web3, string address, string chain, LiquidationRisk risk)
    {
        if (chain != "ethereum") return;

        try
        {
            // Check borrow balance on Compound V3
            var handler = web3.Eth.GetContractQueryHandler<BorrowBalanceOfFunction>();
            var balance = await handler.QueryAsync<BigInteger>(
                CompoundV3USDC,
                new BorrowBalanceOfFunction { Account = address });

            var borrowBalance = Web3.Convert.FromWei(balance, 6);
            if (borrowBalance > 0)
            {
                risk.CompoundBorrowBalance = borrowBalance;
                risk.Alerts.Add($"Compound V3 borrow: ${borrowBalance:F2} USDC");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to check Compound borrow for {Address}", address);
        }
    }

    private static string DetermineOverallRisk(LiquidationRisk risk)
    {
        if (risk.AaveRiskLevel == "danger") return "danger";
        if (risk.AaveRiskLevel == "warning") return "warning";
        if (risk.AaveRiskLevel == "watch") return "watch";
        if (risk.AaveDebtUsd > 0 || risk.CompoundBorrowBalance > 0) return "safe";
        return "none";
    }

    // --- Aave V3 ABI ---

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

    // --- Compound V3 ABI ---

    [Function("borrowBalanceOf", "uint256")]
    private class BorrowBalanceOfFunction : FunctionMessage
    {
        [Parameter("address", "account", 1)]
        public string Account { get; set; } = string.Empty;
    }
}
