using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Correlates on-chain wallet data with social identity signals.
/// Uses ENS text records, governance participation, and DAO membership
/// to build a social identity profile.
/// </summary>
public class SocialIdentityService
{
    private readonly EthereumService _ethService;
    private readonly ILogger<SocialIdentityService> _logger;

    // Common ENS text record keys for social identity
    private static readonly string[] TextRecordKeys =
    [
        "com.twitter", "com.github", "com.discord", "url",
        "email", "avatar", "description", "notice"
    ];

    public SocialIdentityService(
        EthereumService ethService,
        ILogger<SocialIdentityService> logger)
    {
        _ethService = ethService;
        _logger = logger;
    }

    public async Task<SocialIdentity> AnalyzeAsync(string address, WalletProfile profile)
    {
        var identity = new SocialIdentity
        {
            Address = address,
            EnsName = profile.EnsName
        };

        var score = 0;
        var signals = new List<string>();

        // ENS signals
        if (profile.EnsName != null)
        {
            score += 20;
            signals.Add($"ENS name: {profile.EnsName}");

            // ENS text records would require raw contract calls to the resolver
            // For now, ENS name ownership itself is a strong identity signal
            identity.EnsTextRecords.Add($"name: {profile.EnsName}");
        }

        // Governance signals from interactions
        if (profile.TopInteractions.Count > 0)
        {
            var govInteractions = profile.TopInteractions
                .Where(i => i.Category is "governance")
                .ToList();

            if (govInteractions.Count > 0)
            {
                identity.GovernanceVotes = govInteractions.Sum(i => i.TransactionCount);
                identity.DaoMemberships = govInteractions.Count;
                score += Math.Min(govInteractions.Count * 10, 20);
                signals.Add($"Active in {govInteractions.Count} governance protocol(s)");
            }
        }

        // Wallet maturity signals
        if (profile.Activity?.FirstTransaction != null)
        {
            var age = DateTime.UtcNow - profile.Activity.FirstTransaction.Value;
            if (age.TotalDays > 1825)
            {
                score += 15;
                signals.Add("5+ year wallet history");
            }
            else if (age.TotalDays > 365)
            {
                score += 10;
                signals.Add("1+ year wallet history");
            }
        }

        // Interaction diversity
        if (profile.Activity?.UniqueInteractions > 50)
        {
            score += 10;
            signals.Add($"Highly connected ({profile.Activity.UniqueInteractions} unique interactions)");
        }

        // NFT/identity signals
        if (profile.Nfts?.TotalCount > 0)
        {
            score += 5;
            signals.Add($"NFT collector ({profile.Nfts.TotalCount} NFTs)");
        }

        // Tag-based signals
        if (profile.Tags.Contains("power-user")) { score += 5; signals.Add("Power user"); }
        if (profile.Tags.Contains("defi-user")) { score += 5; signals.Add("DeFi participant"); }

        identity.IdentityScore = Math.Clamp(score, 0, 100);
        identity.SocialSignals = signals;
        identity.IdentityLevel = identity.IdentityScore switch
        {
            >= 50 => "identified",
            >= 20 => "pseudonymous",
            _ => "anonymous"
        };

        return identity;
    }
}
