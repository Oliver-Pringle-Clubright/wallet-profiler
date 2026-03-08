using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class PortfolioQualityService
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

    public PortfolioQuality Evaluate(WalletProfile profile)
    {
        var tokens = profile.TopTokens;
        var nonSpam = tokens.Where(t => !t.IsSpam).ToList();
        var totalTokenValue = nonSpam.Where(t => t.ValueUsd.HasValue).Sum(t => t.ValueUsd!.Value);
        var totalValue = profile.TotalValueUsd ?? totalTokenValue;

        if (totalValue <= 0)
        {
            return new PortfolioQuality
            {
                QualityScore = 0,
                Grade = "N/A"
            };
        }

        // Blue-chip percentage (ETH counts as blue-chip)
        var bluechipValue = (profile.EthValueUsd ?? 0);
        bluechipValue += nonSpam
            .Where(t => BluechipSymbols.Contains(t.Symbol) && t.ValueUsd.HasValue)
            .Sum(t => t.ValueUsd!.Value);
        var bluechipPct = Math.Round(bluechipValue / totalValue * 100, 1);

        // Stablecoin percentage
        var stablecoinValue = nonSpam
            .Where(t => StablecoinSymbols.Contains(t.Symbol) && t.ValueUsd.HasValue)
            .Sum(t => t.ValueUsd!.Value);
        var stablecoinPct = Math.Round(stablecoinValue / totalValue * 100, 1);

        // Spam percentage
        var spamCount = tokens.Count(t => t.IsSpam);
        var spamPct = tokens.Count > 0 ? Math.Round((decimal)spamCount / tokens.Count * 100, 1) : 0;

        // Diversity score (0-100): based on number of priced non-spam tokens
        var pricedTokenCount = nonSpam.Count(t => t.ValueUsd > 0);
        var diversityScore = pricedTokenCount switch
        {
            >= 20 => 100,
            >= 15 => 85,
            >= 10 => 70,
            >= 5 => 50,
            >= 3 => 30,
            >= 1 => 15,
            _ => 0
        };

        // Quality score: weighted composite
        // 40% blue-chip allocation, 20% diversity, 20% low spam, 20% stablecoin balance
        var bluechipScore = Math.Min((int)bluechipPct, 100) * 0.4;
        var diversityComponent = diversityScore * 0.2;
        var spamScore = Math.Max(0, 100 - (int)spamPct * 3) * 0.2;
        // Stablecoins: best around 10-30%, penalize extremes
        var stablecoinScore = stablecoinPct switch
        {
            >= 10 and <= 30 => 100,
            >= 5 and < 10 => 70,
            > 30 and <= 50 => 70,
            > 50 and <= 80 => 40,
            > 80 => 20,
            _ => 50 // 0-5% is fine, just means all-in on crypto
        } * 0.2;

        var qualityScore = Math.Clamp((int)(bluechipScore + diversityComponent + spamScore + stablecoinScore), 0, 100);

        var grade = qualityScore switch
        {
            >= 80 => "A",
            >= 60 => "B",
            >= 40 => "C",
            >= 20 => "D",
            _ => "F"
        };

        return new PortfolioQuality
        {
            BluechipPct = bluechipPct,
            StablecoinPct = stablecoinPct,
            SpamPct = spamPct,
            DiversityScore = diversityScore,
            QualityScore = qualityScore,
            Grade = grade
        };
    }
}
