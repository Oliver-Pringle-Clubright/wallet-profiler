using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class AcpTrustService
{
    public AcpTrustScore Evaluate(WalletProfile profile)
    {
        var score = 0;
        var factors = new List<string>();

        // 1. Wallet age (max 25 points)
        if (profile.Activity?.FirstTransaction.HasValue == true)
        {
            var ageDays = (DateTime.UtcNow - profile.Activity.FirstTransaction.Value).TotalDays;
            if (ageDays > 1095) // 3+ years
            {
                score += 25;
                factors.Add("Wallet age 3+ years (+25)");
            }
            else if (ageDays > 365) // 1+ year
            {
                score += 20;
                factors.Add("Wallet age 1+ year (+20)");
            }
            else if (ageDays > 90)
            {
                score += 10;
                factors.Add("Wallet age 3+ months (+10)");
            }
            else if (ageDays > 30)
            {
                score += 5;
                factors.Add("Wallet age 1+ month (+5)");
            }
        }

        // 2. ETH balance as collateral/bond (max 20 points)
        if (profile.EthValueUsd > 10_000)
        {
            score += 20;
            factors.Add("Significant ETH balance >$10K (+20)");
        }
        else if (profile.EthValueUsd > 1_000)
        {
            score += 15;
            factors.Add("Moderate ETH balance >$1K (+15)");
        }
        else if (profile.EthValueUsd > 100)
        {
            score += 10;
            factors.Add("Some ETH balance >$100 (+10)");
        }
        else if (profile.EthBalance > 0)
        {
            score += 5;
            factors.Add("Has ETH balance (+5)");
        }

        // 3. Transaction depth (max 15 points)
        if (profile.TransactionCount > 500)
        {
            score += 15;
            factors.Add("Deep transaction history 500+ txs (+15)");
        }
        else if (profile.TransactionCount > 100)
        {
            score += 10;
            factors.Add("Good transaction history 100+ txs (+10)");
        }
        else if (profile.TransactionCount > 20)
        {
            score += 5;
            factors.Add("Some transaction history 20+ txs (+5)");
        }

        // 4. ENS identity (10 points)
        if (!string.IsNullOrEmpty(profile.EnsName))
        {
            score += 10;
            factors.Add("ENS name registered (+10)");
        }

        // 5. DeFi participation (max 10 points)
        if (profile.DeFiPositions.Count > 0)
        {
            var protocols = profile.DeFiPositions.Select(p => p.Protocol).Distinct().Count();
            if (protocols >= 2)
            {
                score += 10;
                factors.Add("Multi-protocol DeFi user (+10)");
            }
            else
            {
                score += 7;
                factors.Add("DeFi participant (+7)");
            }
        }

        // 6. Portfolio quality (max 10 points)
        if (profile.PortfolioQuality != null)
        {
            if (profile.PortfolioQuality.QualityScore >= 60)
            {
                score += 10;
                factors.Add("High portfolio quality (+10)");
            }
            else if (profile.PortfolioQuality.QualityScore >= 40)
            {
                score += 5;
                factors.Add("Moderate portfolio quality (+5)");
            }
        }

        // 7. Interaction diversity (max 10 points)
        if (profile.Activity?.UniqueInteractions > 50)
        {
            score += 10;
            factors.Add("Highly connected wallet 50+ interactions (+10)");
        }
        else if (profile.Activity?.UniqueInteractions > 20)
        {
            score += 5;
            factors.Add("Connected wallet 20+ interactions (+5)");
        }

        // Clamp score
        score = Math.Clamp(score, 0, 100);

        var level = score switch
        {
            >= 80 => "high",
            >= 60 => "moderate",
            >= 30 => "low",
            _ => "untrusted"
        };

        return new AcpTrustScore
        {
            Score = score,
            Level = level,
            Factors = factors
        };
    }
}
