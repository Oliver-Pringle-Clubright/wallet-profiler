using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class AcpTrustService
{
    public AcpTrustScore Evaluate(WalletProfile profile)
    {
        var score = 0;
        var factors = new List<string>();

        // ==================== POSITIVE SIGNALS ====================

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
            if (protocols >= 3)
            {
                score += 10;
                factors.Add($"Active across {protocols} DeFi protocols (+10)");
            }
            else if (protocols >= 2)
            {
                score += 8;
                factors.Add("Multi-protocol DeFi user (+8)");
            }
            else
            {
                score += 5;
                factors.Add("DeFi participant (+5)");
            }
        }

        // 6. Portfolio quality (max 10 points)
        if (profile.PortfolioQuality != null)
        {
            if (profile.PortfolioQuality.QualityScore >= 70)
            {
                score += 10;
                factors.Add("High portfolio quality (+10)");
            }
            else if (profile.PortfolioQuality.QualityScore >= 50)
            {
                score += 7;
                factors.Add("Good portfolio quality (+7)");
            }
            else if (profile.PortfolioQuality.QualityScore >= 30)
            {
                score += 3;
                factors.Add("Basic portfolio quality (+3)");
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
            score += 7;
            factors.Add("Connected wallet 20+ interactions (+7)");
        }
        else if (profile.Activity?.UniqueInteractions > 5)
        {
            score += 3;
            factors.Add("Some interactions (+3)");
        }

        // 8. NFT holdings (max 5 points)
        if (profile.Nfts != null && profile.Nfts.TotalCount > 0)
        {
            if (profile.Nfts.EstimatedValueUsd > 1000)
            {
                score += 5;
                factors.Add("Valuable NFT collection (+5)");
            }
            else if (profile.Nfts.TotalCount > 5)
            {
                score += 3;
                factors.Add("NFT holder (+3)");
            }
        }

        // 9. Transfer history depth (max 5 points)
        if (profile.TransferHistory != null && profile.TransferHistory.TotalTransfers > 0)
        {
            if (profile.TransferHistory.TotalTransfers > 100)
            {
                score += 5;
                factors.Add("Deep transfer history (+5)");
            }
            else if (profile.TransferHistory.TotalTransfers > 20)
            {
                score += 3;
                factors.Add("Active transfer history (+3)");
            }
        }

        // ==================== NEGATIVE SIGNALS ====================

        // 10. Sanctions exposure (up to -30)
        if (profile.Sanctions != null)
        {
            if (profile.Sanctions.IsSanctioned)
            {
                score -= 30;
                factors.Add("SANCTIONED ADDRESS (-30)");
            }
            else if (profile.Sanctions.HasSanctionedInteractions)
            {
                score -= 15;
                factors.Add("Has interactions with sanctioned entities (-15)");
            }
        }

        // 11. Risky approvals (up to -15)
        if (profile.ApprovalRisk != null)
        {
            if (profile.ApprovalRisk.HighRiskApprovals > 3)
            {
                score -= 15;
                factors.Add($"{profile.ApprovalRisk.HighRiskApprovals} high-risk approvals (-15)");
            }
            else if (profile.ApprovalRisk.HighRiskApprovals > 0)
            {
                score -= 7;
                factors.Add($"{profile.ApprovalRisk.HighRiskApprovals} high-risk approval(s) (-7)");
            }
            else if (profile.ApprovalRisk.UnlimitedApprovals > 5)
            {
                score -= 5;
                factors.Add("Many unlimited approvals outstanding (-5)");
            }
        }

        // 12. MEV exposure (up to -10)
        if (profile.MevExposure != null)
        {
            if (profile.MevExposure.SandwichAttacks > 5)
            {
                score -= 10;
                factors.Add($"Frequent MEV victim — {profile.MevExposure.SandwichAttacks} sandwich attacks (-10)");
            }
            else if (profile.MevExposure.SandwichAttacks > 0)
            {
                score -= 3;
                factors.Add("Some MEV exposure (-3)");
            }
        }

        // 13. High risk score penalty (up to -10)
        if (profile.Risk.Level == "critical")
        {
            score -= 10;
            factors.Add("Critical risk level (-10)");
        }
        else if (profile.Risk.Level == "high")
        {
            score -= 5;
            factors.Add("High risk level (-5)");
        }

        // 14. Empty/dormant wallet penalty
        if (profile.TransactionCount == 0)
        {
            score -= 10;
            factors.Add("No transaction history (-10)");
        }
        else if (profile.Activity?.LastTransaction.HasValue == true)
        {
            var daysSinceLast = (DateTime.UtcNow - profile.Activity.LastTransaction.Value).TotalDays;
            if (daysSinceLast > 365)
            {
                score -= 5;
                factors.Add("Dormant wallet — inactive for 1+ year (-5)");
            }
        }

        // Clamp score
        score = Math.Clamp(score, 0, 100);

        var level = score switch
        {
            >= 80 => "high",
            >= 60 => "good",
            >= 40 => "moderate",
            >= 20 => "low",
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
