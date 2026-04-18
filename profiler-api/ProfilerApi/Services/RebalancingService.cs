using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class RebalancingService
{
    private static readonly HashSet<string> BluechipSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        "WETH", "WBTC", "LINK", "UNI", "AAVE", "MKR", "SNX", "CRV", "LDO",
        "RPL", "ENS", "GRT", "MATIC", "ARB", "OP", "COMP", "SUSHI", "BAL",
        "YFI", "1INCH", "DYDX", "PENDLE", "ENA", "EIGEN"
    };

    private static readonly HashSet<string> StablecoinSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDC", "USDT", "DAI", "FRAX", "BUSD", "TUSD", "USDP", "GUSD",
        "LUSD", "crvUSD", "PYUSD", "GHO", "sUSD"
    };

    private static readonly HashSet<string> DefiSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        "AAVE", "COMP", "MKR", "CRV", "SNX", "UNI", "SUSHI", "BAL",
        "YFI", "1INCH", "DYDX", "PENDLE", "LDO", "RPL", "ENA", "EIGEN"
    };

    private static readonly Dictionary<string, ModelPortfolio> Portfolios = new()
    {
        ["conservative"] = new("Conservative", "Capital preservation focus — heavy stablecoins, blue-chip ETH/BTC, minimal altcoin exposure",
            BluechipTarget: 40, StablecoinTarget: 45, AltcoinTarget: 10, DefiTarget: 5),
        ["balanced"] = new("Balanced", "Moderate risk/reward — diversified across blue-chips and stablecoins with some altcoin growth",
            BluechipTarget: 45, StablecoinTarget: 25, AltcoinTarget: 20, DefiTarget: 10),
        ["growth"] = new("Growth", "Capital appreciation focus — heavy blue-chip and altcoin allocation, light stablecoins",
            BluechipTarget: 40, StablecoinTarget: 10, AltcoinTarget: 35, DefiTarget: 15),
        ["yield-farmer"] = new("Yield Farmer", "Maximize DeFi yields — heavy DeFi governance tokens and LP positions, moderate stablecoins for farming",
            BluechipTarget: 20, StablecoinTarget: 25, AltcoinTarget: 20, DefiTarget: 35),
        ["degen"] = new("Degen", "Maximum risk tolerance — heavy altcoin exposure, minimal safety net, chasing alpha",
            BluechipTarget: 15, StablecoinTarget: 5, AltcoinTarget: 60, DefiTarget: 20)
    };

    public List<RebalanceSuggestion> Analyze(WalletProfile profile, string? portfolio = null)
    {
        var tokens = profile.TopTokens.Where(t => !t.IsSpam).ToList();
        var totalValue = profile.TotalValueUsd ?? 0;
        if (totalValue <= 0) totalValue = (profile.EthValueUsd ?? 0) + tokens.Where(t => t.ValueUsd.HasValue).Sum(t => t.ValueUsd!.Value);
        if (totalValue <= 0) return [];

        // Calculate current allocation
        var ethValue = profile.EthValueUsd ?? 0;
        var bluechipValue = ethValue + tokens.Where(t => BluechipSymbols.Contains(t.Symbol) && t.ValueUsd.HasValue).Sum(t => t.ValueUsd!.Value);
        var stablecoinValue = tokens.Where(t => StablecoinSymbols.Contains(t.Symbol) && t.ValueUsd.HasValue).Sum(t => t.ValueUsd!.Value);
        var defiValue = tokens.Where(t => DefiSymbols.Contains(t.Symbol) && !BluechipSymbols.Contains(t.Symbol) && t.ValueUsd.HasValue).Sum(t => t.ValueUsd!.Value);
        // DefiSymbols overlap with BluechipSymbols, so count DeFi-only tokens that aren't already in bluechip
        // Actually let's count DeFi governance tokens specially
        defiValue = tokens.Where(t => DefiSymbols.Contains(t.Symbol) && t.ValueUsd.HasValue).Sum(t => t.ValueUsd!.Value);
        var altcoinValue = totalValue - bluechipValue - stablecoinValue;
        if (altcoinValue < 0) altcoinValue = 0;

        var currentBluechip = Math.Round(bluechipValue / totalValue * 100, 1);
        var currentStable = Math.Round(stablecoinValue / totalValue * 100, 1);
        var currentAltcoin = Math.Round(altcoinValue / totalValue * 100, 1);

        var targetPortfolios = portfolio != null && Portfolios.ContainsKey(portfolio.ToLowerInvariant())
            ? new Dictionary<string, ModelPortfolio> { [portfolio.ToLowerInvariant()] = Portfolios[portfolio.ToLowerInvariant()] }
            : Portfolios;

        var suggestions = new List<RebalanceSuggestion>();
        foreach (var (key, model) in targetPortfolios)
        {
            var actions = new List<RebalanceAction>();

            var bluechipDelta = model.BluechipTarget - currentBluechip;
            var stableDelta = model.StablecoinTarget - currentStable;
            var altcoinDelta = model.AltcoinTarget - currentAltcoin;

            if (Math.Abs(bluechipDelta) > 5)
            {
                actions.Add(new RebalanceAction
                {
                    Action = bluechipDelta > 0 ? "increase" : "decrease",
                    Category = "bluechip",
                    Reason = bluechipDelta > 0
                        ? $"Blue-chip allocation ({currentBluechip:F0}%) is below target ({model.BluechipTarget}%)"
                        : $"Blue-chip allocation ({currentBluechip:F0}%) exceeds target ({model.BluechipTarget}%)",
                    CurrentPct = currentBluechip,
                    TargetPct = model.BluechipTarget,
                    DeltaPct = Math.Round(bluechipDelta, 1),
                    SuggestedTokens = bluechipDelta > 0 ? ["ETH", "WBTC", "LINK", "ARB"] : []
                });
            }

            if (Math.Abs(stableDelta) > 5)
            {
                actions.Add(new RebalanceAction
                {
                    Action = stableDelta > 0 ? "increase" : "decrease",
                    Category = "stablecoin",
                    Reason = stableDelta > 0
                        ? $"Stablecoin allocation ({currentStable:F0}%) is below target ({model.StablecoinTarget}%)"
                        : $"Stablecoin allocation ({currentStable:F0}%) exceeds target ({model.StablecoinTarget}%)",
                    CurrentPct = currentStable,
                    TargetPct = model.StablecoinTarget,
                    DeltaPct = Math.Round(stableDelta, 1),
                    SuggestedTokens = stableDelta > 0 ? ["USDC", "DAI", "USDT"] : []
                });
            }

            if (Math.Abs(altcoinDelta) > 5)
            {
                actions.Add(new RebalanceAction
                {
                    Action = altcoinDelta > 0 ? "increase" : "decrease",
                    Category = "altcoin",
                    Reason = altcoinDelta > 0
                        ? $"Altcoin allocation ({currentAltcoin:F0}%) is below target ({model.AltcoinTarget}%)"
                        : $"Altcoin allocation ({currentAltcoin:F0}%) exceeds target ({model.AltcoinTarget}%)",
                    CurrentPct = currentAltcoin,
                    TargetPct = model.AltcoinTarget,
                    DeltaPct = Math.Round(altcoinDelta, 1),
                    SuggestedTokens = altcoinDelta > 0 ? ["PENDLE", "ENA", "EIGEN", "GRT"] : []
                });
            }

            // Fit score: how close the current portfolio matches this model (100 = perfect match)
            var totalDeviation = Math.Abs(bluechipDelta) + Math.Abs(stableDelta) + Math.Abs(altcoinDelta);
            var fitScore = Math.Clamp(100 - (int)(totalDeviation * 0.5m), 0, 100);

            suggestions.Add(new RebalanceSuggestion
            {
                ModelPortfolio = key,
                Description = model.Description,
                FitScore = fitScore,
                BluechipPct = currentBluechip,
                StablecoinPct = currentStable,
                AltcoinPct = currentAltcoin,
                Actions = actions
            });
        }

        return suggestions.OrderByDescending(s => s.FitScore).ToList();
    }

    private record ModelPortfolio(string Name, string Description, decimal BluechipTarget, decimal StablecoinTarget, decimal AltcoinTarget, decimal DefiTarget);
}
