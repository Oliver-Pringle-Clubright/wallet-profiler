using System.Text.Json;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Generates on-chain reputation badge metadata for a wallet.
/// Produces ERC-721 compatible JSON metadata that could be minted as a soulbound NFT.
/// Badge reflects wallet's trust score, classification, and key on-chain metrics.
/// </summary>
public class ReputationBadgeService
{
    private readonly ILogger<ReputationBadgeService> _logger;

    public ReputationBadgeService(ILogger<ReputationBadgeService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates a reputation badge from a completed wallet profile.
    /// </summary>
    public ReputationBadge GenerateBadge(WalletProfile profile)
    {
        var trustScore = profile.AcpTrust?.Score ?? 0;
        var trustLevel = profile.AcpTrust?.Level ?? "untrusted";

        // Classify wallet type
        var classification = ClassifyWallet(profile);

        // Calculate wallet age
        var walletAgeDays = 0;
        if (profile.Activity?.FirstTransaction != null)
            walletAgeDays = (int)(DateTime.UtcNow - profile.Activity.FirstTransaction.Value).TotalDays;

        var badge = new ReputationBadge
        {
            Address = profile.Address,
            TrustScore = trustScore,
            TrustLevel = trustLevel,
            Classification = classification,
            WalletAgeDays = walletAgeDays,
            TransactionCount = profile.TransactionCount,
            EnsName = profile.EnsName,
            Tags = profile.Tags.Take(10).ToList(),
            IssuedAt = DateTime.UtcNow
        };

        // Generate ERC-721 compatible metadata JSON
        badge.BadgeUri = GenerateMetadataUri(badge, profile);

        return badge;
    }

    private static string ClassifyWallet(WalletProfile profile)
    {
        if (profile.TotalValueUsd > 1_000_000) return "whale";
        if (profile.DeFiPositions.Count > 0 && profile.TransactionCount > 100) return "defi_native";
        if (profile.SmartMoney?.Classification == "active_trader") return "trader";
        if (profile.Activity?.DaysActive > 365 && profile.TransactionCount < 50) return "hodler";
        if (profile.TransactionCount < 10) return "newcomer";
        return "active_user";
    }

    private static string GenerateMetadataUri(ReputationBadge badge, WalletProfile profile)
    {
        var metadata = new
        {
            name = $"Wallet Profiler Reputation Badge - {badge.TrustLevel}",
            description = $"On-chain reputation badge for {(badge.EnsName ?? badge.Address[..10])}. " +
                          $"Trust: {badge.TrustScore}/100 ({badge.TrustLevel}). " +
                          $"Classification: {badge.Classification}. " +
                          $"Wallet age: {badge.WalletAgeDays} days. " +
                          $"Transactions: {badge.TransactionCount}.",
            image = GetBadgeImageUrl(badge.TrustLevel),
            attributes = new object[]
            {
                new { trait_type = "Trust Score", value = badge.TrustScore, max_value = 100 },
                new { trait_type = "Trust Level", value = badge.TrustLevel },
                new { trait_type = "Classification", value = badge.Classification },
                new { trait_type = "Wallet Age (Days)", value = badge.WalletAgeDays },
                new { trait_type = "Transaction Count", value = badge.TransactionCount },
                new { trait_type = "Portfolio Value", value = profile.TotalValueUsd?.ToString("F2") ?? "N/A" },
                new { trait_type = "Tags", value = string.Join(", ", badge.Tags) }
            }
        };

        // Return as base64 data URI (on-chain metadata pattern)
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        return $"data:application/json;base64,{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))}";
    }

    private static string GetBadgeImageUrl(string trustLevel) => trustLevel switch
    {
        "high" => "https://wallet-profiler.agdp.io/badges/gold.svg",
        "moderate" => "https://wallet-profiler.agdp.io/badges/silver.svg",
        "low" => "https://wallet-profiler.agdp.io/badges/bronze.svg",
        _ => "https://wallet-profiler.agdp.io/badges/unverified.svg"
    };
}
