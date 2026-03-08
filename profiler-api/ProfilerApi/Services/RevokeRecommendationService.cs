using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Analyzes token approvals and generates prioritized recommendations
/// for which approvals to revoke, based on risk level, spender reputation,
/// and approval type.
/// </summary>
public class RevokeRecommendationService
{
    // Contracts known to be inactive, deprecated, or exploited
    private static readonly HashSet<string> HighRiskSpenders = new(StringComparer.OrdinalIgnoreCase)
    {
        // Historical exploit targets / deprecated contracts
    };

    // DEX routers that are generally safe but unlimited approvals are wasteful
    private static readonly HashSet<string> TrustedDexRouters = new(StringComparer.OrdinalIgnoreCase)
    {
        "0x7a250d5630B4cF539739dF2C5dAcb4c659F2488D", // Uniswap V2
        "0xE592427A0AEce92De3Edee1F18E0157C05861564", // Uniswap V3
        "0x3fC91A3afd70395Cd496C647d5a6CC9D4B2b7FAD", // Uniswap Universal
        "0xd9e1cE17f2641f24aE83637ab66a2cca9C378B9F", // SushiSwap
        "0x1111111254EEB25477B68fb85Ed929f73A960582", // 1inch V5
        "0xDef1C0ded9bec7F1a1670819833240f027b25EfF", // 0x Exchange
    };

    public RevokeRecommendations Analyze(ApprovalRisk? approvalRisk)
    {
        if (approvalRisk == null || approvalRisk.TotalApprovals == 0)
            return new RevokeRecommendations { OverallUrgency = "none" };

        var recommendations = new List<RevokeRecommendation>();

        foreach (var approval in approvalRisk.Approvals)
        {
            var priority = "low";
            var reason = "";

            if (HighRiskSpenders.Contains(approval.SpenderAddress))
            {
                // High risk: known exploit target or deprecated
                priority = "high";
                reason = $"Spender {approval.SpenderLabel} is flagged as high-risk. Revoke immediately.";
            }
            else if (approval.SpenderCategory == "nft" && approval.IsUnlimited)
            {
                // NFT marketplace unlimited approval — moderate risk
                priority = "high";
                reason = $"Unlimited approval to NFT marketplace {approval.SpenderLabel}. NFT approvals are commonly exploited in phishing attacks.";
            }
            else if (approval.IsUnlimited && !TrustedDexRouters.Contains(approval.SpenderAddress))
            {
                // Unlimited approval to unknown spender
                priority = "high";
                reason = $"Unlimited approval to {approval.SpenderLabel}. Unknown or unverified spender with unlimited access.";
            }
            else if (approval.IsUnlimited && TrustedDexRouters.Contains(approval.SpenderAddress))
            {
                // Unlimited approval to trusted DEX — low to medium risk
                priority = "medium";
                reason = $"Unlimited approval to {approval.SpenderLabel}. While trusted, unlimited approvals are unnecessary for most users. Consider setting a specific amount.";
            }
            else
            {
                // Limited approval to known spender — generally safe
                priority = "low";
                reason = $"Limited approval to {approval.SpenderLabel}. Low risk but can be revoked if no longer using this protocol.";
            }

            recommendations.Add(new RevokeRecommendation
            {
                TokenSymbol = approval.TokenSymbol,
                TokenAddress = approval.TokenAddress,
                SpenderAddress = approval.SpenderAddress,
                SpenderLabel = approval.SpenderLabel,
                Priority = priority,
                Reason = reason,
                IsUnlimited = approval.IsUnlimited
            });
        }

        // Sort by priority
        recommendations = recommendations
            .OrderByDescending(r => r.Priority switch { "high" => 3, "medium" => 2, _ => 1 })
            .ThenBy(r => r.TokenSymbol)
            .ToList();

        var highCount = recommendations.Count(r => r.Priority == "high");
        var overallUrgency = highCount switch
        {
            > 2 => "high",
            > 0 => "medium",
            _ => recommendations.Any(r => r.Priority == "medium") ? "low" : "none"
        };

        return new RevokeRecommendations
        {
            TotalRecommendations = recommendations.Count,
            HighPriority = highCount,
            OverallUrgency = overallUrgency,
            Recommendations = recommendations
        };
    }
}
