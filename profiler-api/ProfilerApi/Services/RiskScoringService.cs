using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class RiskScoringService
{
    public RiskAssessment Assess(
        decimal ethBalance,
        int txCount,
        List<TokenBalance> tokens,
        List<DeFiPosition> defiPositions,
        WalletActivity activity)
    {
        var flags = new List<string>();
        var score = 0;

        // New wallet (< 30 days old)
        if (activity.FirstTransaction.HasValue)
        {
            var walletAge = DateTime.UtcNow - activity.FirstTransaction.Value;
            if (walletAge.TotalDays < 30)
            {
                score += 20;
                flags.Add("Wallet is less than 30 days old");
            }
            else if (walletAge.TotalDays < 90)
            {
                score += 10;
                flags.Add("Wallet is less than 90 days old");
            }
        }
        else
        {
            score += 25;
            flags.Add("No transaction history found");
        }

        // Low transaction count
        if (txCount < 5)
        {
            score += 15;
            flags.Add("Very few transactions (< 5)");
        }
        else if (txCount < 20)
        {
            score += 5;
            flags.Add("Low transaction count (< 20)");
        }

        // Empty wallet
        if (ethBalance == 0 && tokens.Count == 0)
        {
            score += 20;
            flags.Add("Wallet holds no ETH or tokens");
        }

        // Low interaction diversity
        if (activity.UniqueInteractions < 3)
        {
            score += 10;
            flags.Add("Interacts with very few addresses");
        }

        // Heavy borrowing relative to collateral
        var totalCollateral = defiPositions
            .Where(p => p.Type == "lending")
            .Sum(p => p.Amount);
        var totalDebt = defiPositions
            .Where(p => p.Type == "borrowing")
            .Sum(p => p.Amount);

        if (totalCollateral > 0 && totalDebt > 0)
        {
            var ratio = totalDebt / totalCollateral;
            if (ratio > 0.8m)
            {
                score += 15;
                flags.Add($"High debt-to-collateral ratio ({ratio:P0})");
            }
        }

        // Inactive wallet
        if (activity.LastTransaction.HasValue)
        {
            var daysSinceActive = (DateTime.UtcNow - activity.LastTransaction.Value).TotalDays;
            if (daysSinceActive > 180)
            {
                score += 10;
                flags.Add("Inactive for 6+ months");
            }
        }

        score = Math.Clamp(score, 0, 100);

        var level = score switch
        {
            < 20 => "low",
            < 50 => "medium",
            < 75 => "high",
            _ => "critical"
        };

        return new RiskAssessment
        {
            Score = score,
            Level = level,
            Flags = flags
        };
    }
}
