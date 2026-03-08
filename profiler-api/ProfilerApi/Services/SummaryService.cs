using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class SummaryService
{
    public string Generate(WalletProfile profile)
    {
        var parts = new List<string>();

        // Wallet identity
        var identity = profile.EnsName != null
            ? $"{profile.EnsName} ({Shorten(profile.Address)})"
            : Shorten(profile.Address);

        // Wallet age
        var agePart = "";
        if (profile.Activity?.FirstTransaction != null)
        {
            var age = DateTime.UtcNow - profile.Activity.FirstTransaction.Value;
            agePart = age.TotalDays switch
            {
                > 1825 => $"active since {profile.Activity.FirstTransaction.Value.Year} ({age.TotalDays / 365:F0}+ years)",
                > 365 => $"active since {profile.Activity.FirstTransaction.Value:MMM yyyy}",
                > 30 => $"{age.TotalDays / 30:F0} months old",
                _ => $"{age.TotalDays:F0} days old"
            };
        }
        else
        {
            agePart = "no transaction history available";
        }

        // Portfolio value
        var valuePart = profile.TotalValueUsd.HasValue
            ? $"holding {FormatUsd(profile.TotalValueUsd.Value)} total"
            : $"holding {profile.EthBalance:F4} ETH";

        // Classify wallet size
        var sizePart = profile.TotalValueUsd switch
        {
            > 1_000_000 => "whale wallet",
            > 100_000 => "high-value wallet",
            > 10_000 => "mid-range wallet",
            > 1_000 => "small wallet",
            > 0 => "micro wallet",
            _ => "wallet"
        };

        parts.Add($"This is a {sizePart} ({agePart}), {valuePart}.");

        // Asset breakdown
        var nonSpamTokens = profile.TopTokens.Where(t => !t.IsSpam).ToList();
        var pricedTokens = nonSpamTokens.Where(t => t.ValueUsd > 0).ToList();

        if (profile.EthValueUsd > 0 || pricedTokens.Count > 0)
        {
            var breakdown = new List<string>();
            if (profile.EthValueUsd > 0)
                breakdown.Add($"{FormatUsd(profile.EthValueUsd.Value)} in ETH");

            foreach (var token in pricedTokens.Take(3))
                breakdown.Add($"{FormatUsd(token.ValueUsd!.Value)} in {token.Symbol}");

            if (pricedTokens.Count > 3)
                breakdown.Add($"{pricedTokens.Count - 3} other priced tokens");

            parts.Add($"Portfolio breakdown: {string.Join(", ", breakdown)}.");
        }

        // Spam tokens
        var spamCount = profile.TopTokens.Count(t => t.IsSpam);
        if (spamCount > 0)
            parts.Add($"{spamCount} spam/phishing token(s) detected and flagged.");

        // DeFi
        if (profile.DeFiPositions.Count > 0)
        {
            var protocols = profile.DeFiPositions.Select(p => p.Protocol).Distinct().ToList();
            parts.Add($"Active DeFi positions on {string.Join(", ", protocols)}.");
        }

        // Activity
        if (profile.Activity != null && profile.Activity.UniqueInteractions > 0)
        {
            parts.Add($"{profile.TransactionCount} transactions across {profile.Activity.UniqueInteractions} unique addresses, active on {profile.Activity.DaysActive} distinct days.");
        }

        // Tags
        if (profile.Tags.Count > 0)
            parts.Add($"Tags: {string.Join(", ", profile.Tags)}.");

        // Portfolio quality
        if (profile.PortfolioQuality != null && profile.PortfolioQuality.Grade != "N/A")
            parts.Add($"Portfolio quality: grade {profile.PortfolioQuality.Grade} ({profile.PortfolioQuality.BluechipPct:F0}% blue-chip, {profile.PortfolioQuality.StablecoinPct:F0}% stablecoins).");

        // ACP trust
        if (profile.AcpTrust != null)
            parts.Add($"ACP trust: {profile.AcpTrust.Level} ({profile.AcpTrust.Score}/100).");

        // Approval risk
        if (profile.ApprovalRisk != null && profile.ApprovalRisk.TotalApprovals > 0)
        {
            var approvalPart = $"{profile.ApprovalRisk.TotalApprovals} active token approval(s) detected";
            if (profile.ApprovalRisk.UnlimitedApprovals > 0)
                approvalPart += $" ({profile.ApprovalRisk.UnlimitedApprovals} unlimited)";
            approvalPart += $" — approval risk: {profile.ApprovalRisk.RiskLevel}.";
            parts.Add(approvalPart);
        }

        // NFT holdings
        if (profile.Nfts != null && profile.Nfts.TotalCount > 0)
        {
            var nftPart = $"{profile.Nfts.TotalCount} NFT(s) across {profile.Nfts.CollectionCount} collection(s)";
            if (profile.Nfts.EstimatedValueUsd.HasValue)
                nftPart += $", estimated floor value {FormatUsd(profile.Nfts.EstimatedValueUsd.Value)}";
            var topNft = profile.Nfts.TopCollections.FirstOrDefault();
            if (topNft != null && topNft.FloorPriceEth.HasValue)
                nftPart += $". Top collection: {topNft.Name} ({topNft.OwnedCount} owned, floor {topNft.FloorPriceEth:F3} ETH)";
            parts.Add(nftPart + ".");
        }

        // Top interactions
        if (profile.TopInteractions.Count > 0)
        {
            var labeled = profile.TopInteractions.Where(i => i.Label != null).Take(3).ToList();
            if (labeled.Count > 0)
                parts.Add($"Top interactions: {string.Join(", ", labeled.Select(i => $"{i.Label} ({i.TransactionCount} txs)"))}.");
        }

        // Transfer history
        if (profile.TransferHistory != null && profile.TransferHistory.TotalTransfers > 0)
        {
            var th = profile.TransferHistory;
            var flowPart = $"{th.TotalTransfers} token transfers ({th.InboundCount} in, {th.OutboundCount} out)";
            if (th.NetFlowUsd.HasValue)
                flowPart += th.NetFlowUsd > 0 ? $", net inflow {FormatUsd(th.NetFlowUsd.Value)}" : $", net outflow {FormatUsd(Math.Abs(th.NetFlowUsd.Value))}";
            parts.Add(flowPart + ".");
        }

        // Revoke advice
        if (profile.RevokeAdvice != null && profile.RevokeAdvice.TotalRecommendations > 0)
        {
            var ra = profile.RevokeAdvice;
            var revokePart = $"{ra.TotalRecommendations} approval revocation(s) recommended";
            if (ra.HighPriority > 0)
                revokePart += $" ({ra.HighPriority} high priority)";
            revokePart += $" — urgency: {ra.OverallUrgency}.";
            parts.Add(revokePart);
        }

        // Similar wallets
        if (profile.SimilarWallets != null && profile.SimilarWallets.Matches.Count > 0)
        {
            var topMatch = profile.SimilarWallets.Matches.First();
            parts.Add($"{profile.SimilarWallets.Matches.Count} similar wallet(s) found (top match: {Shorten(topMatch.Address)}, {topMatch.SimilarityScore}% similarity).");
        }

        // Risk
        var riskPart = profile.Risk.Level switch
        {
            "low" => "Low risk — well-established wallet with no significant concerns.",
            "medium" => $"Medium risk — {string.Join("; ", profile.Risk.Flags.Take(2))}.",
            "high" => $"High risk — {string.Join("; ", profile.Risk.Flags.Take(3))}.",
            "critical" => $"Critical risk — multiple red flags: {string.Join("; ", profile.Risk.Flags)}.",
            _ => ""
        };
        if (!string.IsNullOrEmpty(riskPart))
            parts.Add(riskPart);

        return string.Join(" ", parts);
    }

    private static string Shorten(string address)
        => address.Length > 10 ? $"{address[..6]}...{address[^4..]}" : address;

    private static string FormatUsd(decimal value) => value switch
    {
        >= 1_000_000 => $"${value / 1_000_000:F2}M",
        >= 1_000 => $"${value / 1_000:F1}K",
        >= 1 => $"${value:F2}",
        _ => $"${value:F4}"
    };
}
