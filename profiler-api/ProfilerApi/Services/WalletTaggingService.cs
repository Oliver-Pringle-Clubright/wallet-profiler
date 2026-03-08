using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class WalletTaggingService
{
    public List<string> GenerateTags(WalletProfile profile)
    {
        var tags = new List<string>();

        // Size-based tags
        if (profile.TotalValueUsd > 1_000_000)
            tags.Add("whale");
        else if (profile.TotalValueUsd > 100_000)
            tags.Add("high-roller");
        else if (profile.TotalValueUsd > 10_000)
            tags.Add("mid-tier");

        // ENS identity
        if (!string.IsNullOrEmpty(profile.EnsName))
            tags.Add("ens-holder");

        // DeFi participation
        if (profile.DeFiPositions.Count > 0)
        {
            tags.Add("defi-user");
            var protocols = profile.DeFiPositions.Select(p => p.Protocol).Distinct().Count();
            if (protocols >= 2)
                tags.Add("multi-protocol");
        }

        // Activity-based tags
        if (profile.Activity != null)
        {
            if (profile.Activity.FirstTransaction.HasValue)
            {
                var walletAge = DateTime.UtcNow - profile.Activity.FirstTransaction.Value;
                if (walletAge.TotalDays < 30)
                    tags.Add("fresh-wallet");
                else if (walletAge.TotalDays > 365)
                    tags.Add("veteran");
                if (walletAge.TotalDays > 1825)
                    tags.Add("og");
            }

            if (profile.Activity.LastTransaction.HasValue)
            {
                var daysSinceActive = (DateTime.UtcNow - profile.Activity.LastTransaction.Value).TotalDays;
                if (daysSinceActive > 180)
                    tags.Add("dormant");
            }

            if (profile.TransactionCount > 1000 && profile.Activity.DaysActive > 100)
                tags.Add("power-user");
            else if (profile.TransactionCount > 100)
                tags.Add("active-trader");

            if (profile.Activity.UniqueInteractions > 50)
                tags.Add("well-connected");
        }

        // Portfolio composition tags
        var nonSpamTokens = profile.TopTokens.Where(t => !t.IsSpam).ToList();
        if (nonSpamTokens.Count > 20)
            tags.Add("diversified");
        else if (nonSpamTokens.Count <= 3 && profile.EthBalance > 0)
            tags.Add("concentrated");

        // Stablecoin heavy
        var stablecoinSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "USDC", "USDT", "DAI", "FRAX", "BUSD", "TUSD", "USDP", "GUSD", "LUSD", "crvUSD" };

        var stablecoinValue = nonSpamTokens
            .Where(t => stablecoinSymbols.Contains(t.Symbol) && t.ValueUsd.HasValue)
            .Sum(t => t.ValueUsd!.Value);

        if (profile.TotalValueUsd > 0 && stablecoinValue / profile.TotalValueUsd > 0.5m)
            tags.Add("stablecoin-heavy");

        // Spam magnet
        var spamCount = profile.TopTokens.Count(t => t.IsSpam);
        if (spamCount > 10)
            tags.Add("spam-magnet");

        // Hodler pattern: old wallet, low tx count relative to age
        if (profile.Activity?.FirstTransaction != null && profile.TransactionCount > 0)
        {
            var age = (DateTime.UtcNow - profile.Activity.FirstTransaction.Value).TotalDays;
            if (age > 365 && profile.TransactionCount / (age / 30.0) < 2)
                tags.Add("hodler");
        }

        return tags;
    }
}
