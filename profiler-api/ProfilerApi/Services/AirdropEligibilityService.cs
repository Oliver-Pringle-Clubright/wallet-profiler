using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class AirdropEligibilityService
{
    public AirdropEligibility Check(WalletProfile profile)
    {
        var checks = new List<AirdropCheck>
        {
            CheckLayerZero(profile),
            CheckZkSync(profile),
            CheckStarknet(profile),
            CheckScroll(profile),
            CheckLinea(profile),
            CheckEigenLayer(profile),
            CheckPendle(profile)
        };

        return new AirdropEligibility
        {
            Address = profile.Address,
            TotalChecked = checks.Count,
            EligibleCount = checks.Count(c => c.Status is "eligible" or "likely"),
            Checks = checks
        };
    }

    private static AirdropCheck CheckLayerZero(WalletProfile profile)
    {
        var criteria = new List<AirdropCriterion>();

        // Cross-chain activity (inferred from multi-chain interactions)
        var hasBridgeInteraction = profile.TopInteractions.Any(i =>
            i.Label != null && (i.Label.Contains("Bridge", StringComparison.OrdinalIgnoreCase) ||
                               i.Label.Contains("LayerZero", StringComparison.OrdinalIgnoreCase) ||
                               i.Label.Contains("Stargate", StringComparison.OrdinalIgnoreCase)));
        criteria.Add(new AirdropCriterion
        {
            Name = "Bridge Usage",
            Description = "Used a cross-chain bridge (LayerZero/Stargate)",
            Met = hasBridgeInteraction,
            Evidence = hasBridgeInteraction ? "Bridge interaction found in top contracts" : null
        });

        // Transaction volume
        var highVolume = profile.TransactionCount > 100;
        criteria.Add(new AirdropCriterion
        {
            Name = "Transaction Volume",
            Description = "At least 100 transactions",
            Met = highVolume,
            Evidence = $"{profile.TransactionCount} transactions"
        });

        // Active over time
        var longActive = profile.Activity?.DaysActive > 180;
        criteria.Add(new AirdropCriterion
        {
            Name = "Sustained Activity",
            Description = "Active for at least 180 days",
            Met = longActive == true,
            Evidence = longActive == true ? $"{profile.Activity!.DaysActive} days active" : null
        });

        // Diverse interactions
        var diverseInteractions = profile.Activity?.UniqueInteractions > 10;
        criteria.Add(new AirdropCriterion
        {
            Name = "Contract Diversity",
            Description = "Interacted with 10+ unique contracts",
            Met = diverseInteractions == true,
            Evidence = diverseInteractions == true ? $"{profile.Activity!.UniqueInteractions} unique interactions" : null
        });

        return BuildCheck("LayerZero", criteria);
    }

    private static AirdropCheck CheckZkSync(WalletProfile profile)
    {
        var criteria = new List<AirdropCriterion>();

        var highTxCount = profile.TransactionCount > 50;
        criteria.Add(new AirdropCriterion
        {
            Name = "Transaction Count",
            Description = "At least 50 transactions on mainnet",
            Met = highTxCount,
            Evidence = $"{profile.TransactionCount} transactions"
        });

        var hasDeFi = profile.DeFiPositions.Count > 0;
        criteria.Add(new AirdropCriterion
        {
            Name = "DeFi Usage",
            Description = "Has active DeFi positions",
            Met = hasDeFi,
            Evidence = hasDeFi ? $"{profile.DeFiPositions.Count} DeFi positions" : null
        });

        var hasValue = (profile.TotalValueUsd ?? 0) > 100;
        criteria.Add(new AirdropCriterion
        {
            Name = "Portfolio Value",
            Description = "Total portfolio value > $100",
            Met = hasValue,
            Evidence = hasValue ? $"${profile.TotalValueUsd:F0}" : null
        });

        var oldWallet = profile.Activity?.DaysActive > 90;
        criteria.Add(new AirdropCriterion
        {
            Name = "Wallet Age",
            Description = "Wallet active for 90+ days",
            Met = oldWallet == true,
            Evidence = oldWallet == true ? $"{profile.Activity!.DaysActive} days" : null
        });

        return BuildCheck("zkSync", criteria);
    }

    private static AirdropCheck CheckStarknet(WalletProfile profile)
    {
        var criteria = new List<AirdropCriterion>();

        var hasStarknetInteraction = profile.TopInteractions.Any(i =>
            i.Label != null && i.Label.Contains("Starknet", StringComparison.OrdinalIgnoreCase));
        criteria.Add(new AirdropCriterion
        {
            Name = "Starknet Bridge",
            Description = "Used Starknet bridge or dApp",
            Met = hasStarknetInteraction,
            Evidence = hasStarknetInteraction ? "Starknet interaction found" : null
        });

        var highActivity = profile.TransactionCount > 200;
        criteria.Add(new AirdropCriterion
        {
            Name = "High Activity",
            Description = "At least 200 transactions (power user)",
            Met = highActivity,
            Evidence = $"{profile.TransactionCount} transactions"
        });

        var hasEns = profile.EnsName != null;
        criteria.Add(new AirdropCriterion
        {
            Name = "ENS Name",
            Description = "Has an ENS name registered",
            Met = hasEns,
            Evidence = hasEns ? profile.EnsName : null
        });

        return BuildCheck("Starknet", criteria);
    }

    private static AirdropCheck CheckScroll(WalletProfile profile)
    {
        var criteria = new List<AirdropCriterion>();

        var hasBridge = profile.TopInteractions.Any(i =>
            i.Label != null && (i.Label.Contains("Scroll", StringComparison.OrdinalIgnoreCase) ||
                               i.Label.Contains("Bridge", StringComparison.OrdinalIgnoreCase)));
        criteria.Add(new AirdropCriterion
        {
            Name = "Scroll Bridge Usage",
            Description = "Bridged to Scroll or used Scroll dApps",
            Met = hasBridge,
            Evidence = hasBridge ? "Scroll/Bridge interaction found" : null
        });

        var hasDeFi = profile.DeFiPositions.Count >= 2;
        criteria.Add(new AirdropCriterion
        {
            Name = "Multi-Protocol DeFi",
            Description = "Active in 2+ DeFi protocols",
            Met = hasDeFi,
            Evidence = hasDeFi ? $"{profile.DeFiPositions.Select(d => d.Protocol).Distinct().Count()} protocols" : null
        });

        var goodTrust = (profile.AcpTrust?.Score ?? 0) >= 50;
        criteria.Add(new AirdropCriterion
        {
            Name = "Trust Score",
            Description = "ACP trust score >= 50",
            Met = goodTrust,
            Evidence = goodTrust ? $"Trust score: {profile.AcpTrust!.Score}" : null
        });

        return BuildCheck("Scroll", criteria);
    }

    private static AirdropCheck CheckLinea(WalletProfile profile)
    {
        var criteria = new List<AirdropCriterion>();

        var hasLineaInteraction = profile.TopInteractions.Any(i =>
            i.Label != null && i.Label.Contains("Linea", StringComparison.OrdinalIgnoreCase));
        criteria.Add(new AirdropCriterion
        {
            Name = "Linea Activity",
            Description = "Used Linea bridge or dApps",
            Met = hasLineaInteraction,
            Evidence = hasLineaInteraction ? "Linea interaction found" : null
        });

        var hasNonSpamTokens = profile.TopTokens.Count(t => !t.IsSpam && t.ValueUsd > 0) >= 5;
        criteria.Add(new AirdropCriterion
        {
            Name = "Token Diversity",
            Description = "Holds 5+ non-spam tokens with value",
            Met = hasNonSpamTokens,
            Evidence = hasNonSpamTokens ? $"{profile.TopTokens.Count(t => !t.IsSpam && t.ValueUsd > 0)} tokens" : null
        });

        var isOldWallet = profile.Activity?.DaysActive > 365;
        criteria.Add(new AirdropCriterion
        {
            Name = "Veteran Wallet",
            Description = "Wallet active for 1+ year",
            Met = isOldWallet == true,
            Evidence = isOldWallet == true ? $"{profile.Activity!.DaysActive} days" : null
        });

        return BuildCheck("Linea", criteria);
    }

    private static AirdropCheck CheckEigenLayer(WalletProfile profile)
    {
        var criteria = new List<AirdropCriterion>();

        var hasEigen = profile.TopTokens.Any(t => t.Symbol.Equals("EIGEN", StringComparison.OrdinalIgnoreCase));
        criteria.Add(new AirdropCriterion
        {
            Name = "EIGEN Holder",
            Description = "Holds EIGEN token",
            Met = hasEigen,
            Evidence = hasEigen ? "EIGEN token found in portfolio" : null
        });

        var hasLsdInteraction = profile.DeFiPositions.Any(d =>
            d.Protocol.Contains("Lido", StringComparison.OrdinalIgnoreCase) ||
            d.Protocol.Contains("Rocket", StringComparison.OrdinalIgnoreCase) ||
            d.Protocol.Contains("EigenLayer", StringComparison.OrdinalIgnoreCase));
        criteria.Add(new AirdropCriterion
        {
            Name = "Staking/Restaking",
            Description = "Active in liquid staking or restaking protocols",
            Met = hasLsdInteraction,
            Evidence = hasLsdInteraction ? "Staking protocol interaction found" : null
        });

        var hasStEth = profile.TopTokens.Any(t =>
            t.Symbol.Equals("stETH", StringComparison.OrdinalIgnoreCase) ||
            t.Symbol.Equals("wstETH", StringComparison.OrdinalIgnoreCase) ||
            t.Symbol.Equals("rETH", StringComparison.OrdinalIgnoreCase));
        criteria.Add(new AirdropCriterion
        {
            Name = "LST Holder",
            Description = "Holds liquid staking tokens (stETH, wstETH, rETH)",
            Met = hasStEth,
            Evidence = hasStEth ? "LST found in portfolio" : null
        });

        var highValue = (profile.TotalValueUsd ?? 0) > 1000;
        criteria.Add(new AirdropCriterion
        {
            Name = "Significant Value",
            Description = "Portfolio value > $1,000",
            Met = highValue,
            Evidence = highValue ? $"${profile.TotalValueUsd:F0}" : null
        });

        return BuildCheck("EigenLayer", criteria);
    }

    private static AirdropCheck CheckPendle(WalletProfile profile)
    {
        var criteria = new List<AirdropCriterion>();

        var hasPendle = profile.TopTokens.Any(t => t.Symbol.Equals("PENDLE", StringComparison.OrdinalIgnoreCase));
        criteria.Add(new AirdropCriterion
        {
            Name = "PENDLE Holder",
            Description = "Holds PENDLE token",
            Met = hasPendle,
            Evidence = hasPendle ? "PENDLE token found in portfolio" : null
        });

        var hasPendleInteraction = profile.DeFiPositions.Any(d =>
            d.Protocol.Contains("Pendle", StringComparison.OrdinalIgnoreCase));
        criteria.Add(new AirdropCriterion
        {
            Name = "Pendle Usage",
            Description = "Has Pendle DeFi positions (PT/YT/LP)",
            Met = hasPendleInteraction,
            Evidence = hasPendleInteraction ? "Pendle position found" : null
        });

        var hasYieldTokens = profile.TopTokens.Any(t =>
            t.Symbol.StartsWith("PT-", StringComparison.OrdinalIgnoreCase) ||
            t.Symbol.StartsWith("YT-", StringComparison.OrdinalIgnoreCase));
        criteria.Add(new AirdropCriterion
        {
            Name = "Yield Token Holder",
            Description = "Holds Pendle PT or YT tokens",
            Met = hasYieldTokens,
            Evidence = hasYieldTokens ? "PT/YT token found" : null
        });

        return BuildCheck("Pendle", criteria);
    }

    private static AirdropCheck BuildCheck(string protocol, List<AirdropCriterion> criteria)
    {
        var met = criteria.Count(c => c.Met);
        var total = criteria.Count;
        var pct = total > 0 ? Math.Round((decimal)met / total * 100, 0) : 0;

        var status = pct switch
        {
            >= 75 => "eligible",
            >= 50 => "likely",
            >= 25 => "possible",
            _ => "ineligible"
        };

        return new AirdropCheck
        {
            Protocol = protocol,
            Status = status,
            CriteriaMet = met,
            TotalCriteria = total,
            EligibilityPct = pct,
            Criteria = criteria
        };
    }
}
