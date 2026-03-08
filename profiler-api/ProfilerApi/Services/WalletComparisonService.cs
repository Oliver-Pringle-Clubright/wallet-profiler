using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Compares multiple wallets side-by-side, identifying the leader,
/// common tokens, and unique insights about each wallet's relative
/// position and strategy.
/// </summary>
public class WalletComparisonService
{
    private readonly ILogger<WalletComparisonService> _logger;

    public WalletComparisonService(ILogger<WalletComparisonService> logger)
    {
        _logger = logger;
    }

    public WalletComparison Compare(List<WalletProfile> profiles)
    {
        var comparison = new WalletComparison();

        if (profiles.Count == 0)
            return comparison;

        // Build entries
        foreach (var p in profiles)
        {
            comparison.Wallets.Add(new WalletComparisonEntry
            {
                Address = p.Address,
                EnsName = p.EnsName,
                EthBalance = p.EthBalance,
                TotalValueUsd = p.TotalValueUsd,
                TransactionCount = p.TransactionCount,
                TokenCount = p.TopTokens.Count(t => !t.IsSpam),
                RiskLevel = p.Risk.Level,
                TrustScore = p.AcpTrust?.Score ?? 0,
                SmartMoneyClass = p.SmartMoney?.Classification,
                Tags = p.Tags.Take(5).ToList()
            });
        }

        // Find the leader (highest total value, or highest trust if values unavailable)
        var leader = profiles
            .OrderByDescending(p => p.TotalValueUsd ?? 0)
            .ThenByDescending(p => p.AcpTrust?.Score ?? 0)
            .First();
        comparison.LeaderAddress = leader.Address;

        // Find common tokens
        if (profiles.Count > 1)
        {
            var tokenSets = profiles.Select(p =>
                new HashSet<string>(p.TopTokens.Where(t => !t.IsSpam).Select(t => t.Symbol),
                    StringComparer.OrdinalIgnoreCase)).ToList();

            var commonTokens = tokenSets.First().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var set in tokenSets.Skip(1))
                commonTokens.IntersectWith(set);

            comparison.CommonTokens = commonTokens.Take(20).ToList();
        }

        // Generate insights
        var insights = new List<string>();

        // Value spread
        var values = profiles.Where(p => p.TotalValueUsd.HasValue).Select(p => p.TotalValueUsd!.Value).ToList();
        if (values.Count > 1)
        {
            var maxVal = values.Max();
            var minVal = values.Min();
            if (minVal > 0)
            {
                var spread = maxVal / minVal;
                if (spread > 100)
                    insights.Add($"Extreme value gap: largest wallet is {spread:F0}x the smallest");
                else if (spread > 10)
                    insights.Add($"Significant value gap: {spread:F1}x between largest and smallest");
            }
        }

        // Risk comparison
        var riskLevels = profiles.Select(p => p.Risk.Level).Distinct().ToList();
        if (riskLevels.Count == 1)
            insights.Add($"All wallets share the same risk level: {riskLevels[0]}");
        else if (riskLevels.Contains("high") || riskLevels.Contains("critical"))
            insights.Add("Caution: some wallets have elevated risk levels");

        // DeFi participation
        var defiCount = profiles.Count(p => p.DeFiPositions.Count > 0);
        if (defiCount > 0 && defiCount < profiles.Count)
            insights.Add($"{defiCount} of {profiles.Count} wallets are active in DeFi");
        else if (defiCount == profiles.Count)
            insights.Add("All wallets are active DeFi participants");

        // Smart money
        var smartCount = profiles.Count(p => p.SmartMoney?.Classification is "smart_money" or "active_trader");
        if (smartCount > 0)
            insights.Add($"{smartCount} of {profiles.Count} wallets classified as smart money or active traders");

        // Trust score spread
        var trustScores = profiles.Where(p => p.AcpTrust != null).Select(p => p.AcpTrust!.Score).ToList();
        if (trustScores.Count > 1)
        {
            var avgTrust = trustScores.Average();
            insights.Add($"Average trust score: {avgTrust:F0}/100");
        }

        // Common tokens insight
        if (comparison.CommonTokens.Count > 0)
            insights.Add($"{comparison.CommonTokens.Count} token(s) held by all wallets: {string.Join(", ", comparison.CommonTokens.Take(5))}");

        comparison.UniqueInsights = insights;
        return comparison;
    }
}
