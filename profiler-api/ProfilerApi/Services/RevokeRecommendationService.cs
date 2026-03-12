using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Analyzes token approvals and generates prioritized recommendations
/// for which approvals to revoke, based on risk level, spender reputation,
/// approval type, and known exploit history.
/// </summary>
public class RevokeRecommendationService
{
    // Contracts known to be exploited, deprecated, or abandoned
    private static readonly HashSet<string> HighRiskSpenders = new(StringComparer.OrdinalIgnoreCase)
    {
        // Multichain (Anyswap) — exploited July 2023, $130M stolen
        "0x6b7a87899490EcE95443e979cA9485CBE7E71522",
        "0xBa8Da9dcF11B50B03fd5284f164Ef5cdEF910705",
        // Euler Finance — exploited March 2023, $197M
        "0x27182842E098f60e3D576794A5bFFb0777E025d3",
        // Ronin Bridge — exploited March 2022, $625M
        "0x1A2a1c938CE3eC39b6D47113c7955bAa9DD454F2",
        // Wormhole — exploited Feb 2022, $326M
        "0x3ee18B2214AFF97000D974cf647E7C347E8fa585",
        // Nomad Bridge — exploited Aug 2022, $190M
        "0x88A69B4E698A4B090DF6CF5Bd7B2D47325Ad30A3",
        // BadgerDAO — exploited Dec 2021, $120M
        "0x1FcDb04d0C5364FBd92C73cA8AF9BAA72c269107",
        // Cream Finance — exploited multiple times
        "0xd06527D5e56A3495252A528C4987003b712860eE",
        "0x44fbebd2F576670a6C33f6Fc0B00aA8c5753b322",
        // Pickle Finance — exploited Nov 2020
        "0xbD17B1ce622d73bD438b9E658acA5996dc394b0d",
        // Harvest Finance — exploited Oct 2020
        "0xa0246c9032bC3A600820415aE600c6388619A14D",
        // BZx — exploited multiple times
        "0xD8Ee69652E4e4838f2531732a46d1f7F584F0b7f",
        // Deprecated Uniswap V1 Router
        "0xc0a47dFe034B400B47bDaD5FecDa2621de6c4d95",
        // Deprecated SushiSwap MasterChef V1
        "0xc2EdaD668740f1aA35E4D8f227fB8E17dcA888Cd",
        // Deprecated OpenSea Wyvern
        "0x7Be8076f4EA4A4AD08075C2508e481d6C946D12b",
        "0x7f268357A8c2552623316e2562D90e642bB538E5",
        // PolyNetwork — exploited Aug 2021
        "0x250e76987d838a75310c34bf422ea9f1AC4Cc906",
        // Wintermute (exploited Sep 2022)
        "0x0000000fEd53e89e173f694fc54e511b2399B55B",
    };

    // DEX routers that are generally safe but unlimited approvals are wasteful
    private static readonly HashSet<string> TrustedDexRouters = new(StringComparer.OrdinalIgnoreCase)
    {
        "0x7a250d5630B4cF539739dF2C5dAcb4c659F2488D", // Uniswap V2
        "0xE592427A0AEce92De3Edee1F18E0157C05861564", // Uniswap V3
        "0x3fC91A3afd70395Cd496C647d5a6CC9D4B2b7FAD", // Uniswap Universal
        "0x68b3465833fb72A70ecDF485E0e4C7bD8665Fc45", // Uniswap V3 Router 2
        "0xd9e1cE17f2641f24aE83637ab66a2cca9C378B9F", // SushiSwap
        "0x1111111254EEB25477B68fb85Ed929f73A960582", // 1inch V5
        "0x6131B5fae19EA4f9D964eAc0408E4408b66337b5", // 1inch V6
        "0xDef1C0ded9bec7F1a1670819833240f027b25EfF", // 0x Exchange
        "0x000000000022D473030F116dDEE9F6B43aC78BA3", // Uniswap Permit2
        "0x9008D19f58AAbD9eD0D60971565AA8510560ab41", // CoW Protocol
        "0xBA12222222228d8Ba445958a75a0704d566BF2C8", // Balancer V2
    };

    // Trusted lending/staking protocols — lower risk for approvals
    private static readonly HashSet<string> TrustedProtocols = new(StringComparer.OrdinalIgnoreCase)
    {
        "0x87870Bca3F3fD6335C3F4ce8392D69350B4fA4E2", // Aave V3
        "0x7d2768dE32b0b80b7a3454c06BdAc94A69DDc7A9", // Aave V2
        "0xc3d688B66703497DAA19211EEdff47f25384cdc3", // Compound V3 cUSDC
        "0xae7ab96520DE3A18E5e111B5EaAb095312D7fE84", // Lido stETH
        "0x7f39C581F595B53c5cb19bD0b3f8dA6c935E2Ca0", // Lido wstETH
        "0x858646372CC42E1A627fcE94aa7A7033e7CF075A", // EigenLayer
        "0x83F20F44975D03b1b09e64809B757c47f942BEeA", // MakerDAO sDAI
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
                // Critical: known exploit target or deprecated
                priority = "critical";
                reason = $"URGENT: {approval.SpenderLabel} is a known exploited or deprecated contract. Revoke immediately to protect funds.";
            }
            else if (approval.SpenderCategory == "nft" && approval.IsUnlimited)
            {
                // NFT marketplace unlimited approval — high risk for phishing
                priority = "high";
                reason = $"Unlimited approval to NFT marketplace {approval.SpenderLabel}. NFT approvals are commonly exploited in phishing attacks.";
            }
            else if (approval.IsUnlimited && !TrustedDexRouters.Contains(approval.SpenderAddress) && !TrustedProtocols.Contains(approval.SpenderAddress))
            {
                // Unlimited approval to unknown spender
                priority = "high";
                reason = $"Unlimited approval to {approval.SpenderLabel}. Unknown or unverified spender with unlimited access to your {approval.TokenSymbol}.";
            }
            else if (approval.IsUnlimited && TrustedDexRouters.Contains(approval.SpenderAddress))
            {
                // Unlimited approval to trusted DEX
                priority = "medium";
                reason = $"Unlimited approval to {approval.SpenderLabel}. While trusted, unlimited approvals are unnecessary for most users. Consider revoking and re-approving with specific amounts.";
            }
            else if (approval.IsUnlimited && TrustedProtocols.Contains(approval.SpenderAddress))
            {
                // Unlimited approval to trusted lending protocol — lowest risk
                priority = "low";
                reason = $"Unlimited approval to trusted protocol {approval.SpenderLabel}. Generally safe but can be reduced to a specific amount.";
            }
            else if (approval.SpenderCategory == "mixer")
            {
                // Mixer approval — always flag
                priority = "high";
                reason = $"Approval to mixer {approval.SpenderLabel}. Mixer approvals are suspicious and should be revoked.";
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
            .OrderByDescending(r => r.Priority switch { "critical" => 4, "high" => 3, "medium" => 2, _ => 1 })
            .ThenBy(r => r.TokenSymbol)
            .ToList();

        var criticalCount = recommendations.Count(r => r.Priority == "critical");
        var highCount = recommendations.Count(r => r.Priority is "critical" or "high");
        var overallUrgency = criticalCount switch
        {
            > 0 => "critical",
            _ => highCount switch
            {
                > 2 => "high",
                > 0 => "medium",
                _ => recommendations.Any(r => r.Priority == "medium") ? "low" : "none"
            }
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
