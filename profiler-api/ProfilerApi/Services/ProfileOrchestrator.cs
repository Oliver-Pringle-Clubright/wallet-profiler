using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Extracts profile-building logic from Program.cs endpoints for reuse
/// by /profile, /profile/batch, and /profile/multi-chain.
/// </summary>
public class ProfileOrchestrator
{
    private readonly EthereumService _ethService;
    private readonly TokenService _tokenService;
    private readonly DeFiService _defiService;
    private readonly ActivityService _activityService;
    private readonly RiskScoringService _riskService;
    private readonly PriceService _priceService;
    private readonly SummaryService _summaryService;
    private readonly WalletTaggingService _taggingService;
    private readonly PortfolioQualityService _qualityService;
    private readonly AcpTrustService _trustService;
    private readonly ApprovalScannerService _approvalService;
    private readonly ContractLabelService _labelService;
    private readonly NftService _nftService;
    private readonly TransferHistoryService _transferService;
    private readonly WalletClusteringService _clusteringService;
    private readonly RevokeRecommendationService _revokeService;
    private readonly ProfileCacheService _cacheService;
    private readonly ILogger<ProfileOrchestrator> _logger;

    public ProfileOrchestrator(
        EthereumService ethService,
        TokenService tokenService,
        DeFiService defiService,
        ActivityService activityService,
        RiskScoringService riskService,
        PriceService priceService,
        SummaryService summaryService,
        WalletTaggingService taggingService,
        PortfolioQualityService qualityService,
        AcpTrustService trustService,
        ApprovalScannerService approvalService,
        ContractLabelService labelService,
        NftService nftService,
        TransferHistoryService transferService,
        WalletClusteringService clusteringService,
        RevokeRecommendationService revokeService,
        ProfileCacheService cacheService,
        ILogger<ProfileOrchestrator> logger)
    {
        _ethService = ethService;
        _tokenService = tokenService;
        _defiService = defiService;
        _activityService = activityService;
        _riskService = riskService;
        _priceService = priceService;
        _summaryService = summaryService;
        _taggingService = taggingService;
        _qualityService = qualityService;
        _trustService = trustService;
        _approvalService = approvalService;
        _labelService = labelService;
        _nftService = nftService;
        _transferService = transferService;
        _clusteringService = clusteringService;
        _revokeService = revokeService;
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <summary>
    /// Resolves ENS name to address if needed. Returns the resolved address.
    /// </summary>
    public async Task<string> ResolveAddressAsync(string address, string chain)
    {
        if (!address.EndsWith(".eth"))
            return address;

        var web3 = _ethService.GetWeb3(chain);
        var resolved = await _ethService.ResolveEnsToAddressAsync(web3, address);
        _logger.LogInformation("Resolved ENS {Name} to {Address}", address, resolved);
        return resolved;
    }

    /// <summary>
    /// Builds a full wallet profile for a single address/chain/tier.
    /// </summary>
    public async Task<WalletProfile> BuildProfileAsync(string address, string chain, string tier)
    {
        // Check cache
        var cached = _cacheService.GetProfile(address, chain, tier);
        if (cached != null)
            return cached;

        var web3 = _ethService.GetWeb3(chain);
        var rpcUrl = _ethService.GetRpcUrl(chain);

        // --- BASIC tier: balance + tokens + risk ---
        var balanceTask = _ethService.GetEthBalanceAsync(web3, address);
        var txCountTask = _ethService.GetTransactionCountAsync(web3, address);
        var ensTask = _ethService.ResolveEnsAsync(web3, address);
        var tokensTask = _tokenService.GetERC20BalancesAsync(rpcUrl, address, chain, tier);

        var parallelTasks = new List<Task> { balanceTask, txCountTask, ensTask, tokensTask };

        // --- STANDARD+ adds: DeFi + activity ---
        Task<List<DeFiPosition>>? defiTask = null;
        Task<(WalletActivity Activity, List<(string Address, int Count)> TopCounterparties)>? activityTask = null;

        if (tier is "standard" or "premium")
        {
            defiTask = _defiService.GetPositionsAsync(web3, address, chain);
            activityTask = _activityService.GetActivityAsync(address, chain);
            parallelTasks.Add(defiTask);
            parallelTasks.Add(activityTask);
        }

        await Task.WhenAll(parallelTasks);

        var ethBalance = balanceTask.Result;
        var txCount = txCountTask.Result;
        var tokens = tokensTask.Result;
        var defiPositions = defiTask?.Result ?? [];
        WalletActivity? activity = null;
        List<(string Address, int Count)>? topCounterparties = null;

        if (activityTask != null)
        {
            var activityResult = activityTask.Result;
            activity = activityResult.Activity;
            topCounterparties = activityResult.TopCounterparties;
        }

        // USD prices for standard and premium tiers
        decimal? ethPrice = null;
        decimal? ethValueUsd = null;
        decimal? totalValueUsd = null;

        if (tier is "standard" or "premium")
        {
            ethPrice = await _priceService.EnrichWithPricesAsync(tokens, chain);

            tokens = tokens
                .OrderBy(t => t.IsSpam)
                .ThenByDescending(t => t.ValueUsd ?? 0)
                .ThenByDescending(t => t.Balance)
                .ToList();

            ethValueUsd = ethPrice.HasValue ? ethBalance * ethPrice.Value : null;
            var tokenValueUsd = tokens.Where(t => t.ValueUsd.HasValue && !t.IsSpam).Sum(t => t.ValueUsd!.Value);
            totalValueUsd = ethValueUsd.HasValue ? ethValueUsd.Value + tokenValueUsd : null;
        }

        var risk = _riskService.Assess(ethBalance, txCount, tokens, defiPositions, activity ?? new WalletActivity());

        var profile = new WalletProfile
        {
            Tier = tier,
            Address = address,
            EnsName = ensTask.Result,
            EthBalance = ethBalance,
            EthPriceUsd = ethPrice,
            EthValueUsd = ethValueUsd,
            TotalValueUsd = totalValueUsd,
            TransactionCount = txCount,
            TopTokens = tokens,
            DeFiPositions = defiPositions,
            Risk = risk,
            Activity = activity,
            ProfiledAt = DateTime.UtcNow
        };

        // --- Wallet tags (all tiers) ---
        profile.Tags = _taggingService.GenerateTags(profile);

        // --- Standard+ features ---
        if (tier is "standard" or "premium")
        {
            profile.PortfolioQuality = _qualityService.Evaluate(profile);

            if (topCounterparties != null && topCounterparties.Count > 0)
                profile.TopInteractions = _labelService.LabelInteractions(topCounterparties);

            profile.ApprovalRisk = await _approvalService.ScanAsync(web3, address, tokens);
            profile.AcpTrust = _trustService.Evaluate(profile);

            // NFT holdings (v1.3)
            profile.Nfts = await _nftService.GetNftSummaryAsync(rpcUrl, address, ethPrice);

            // Add NFT value to total if available
            if (profile.Nfts?.EstimatedValueUsd > 0 && totalValueUsd.HasValue)
                profile.TotalValueUsd = totalValueUsd.Value + profile.Nfts.EstimatedValueUsd.Value;

            // Token transfer history (v1.4)
            var tokenPrices = tokens
                .Where(t => t.PriceUsd.HasValue && !t.IsSpam)
                .ToDictionary(t => t.ContractAddress.ToLowerInvariant(), t => t.PriceUsd!.Value);
            profile.TransferHistory = await _transferService.GetTransferHistoryAsync(address, chain, tokenPrices);

            // Similar wallet clustering (v1.4)
            profile.SimilarWallets = await _clusteringService.FindSimilarAsync(
                address, chain, tier, tokens, profile.TopInteractions, topCounterparties);

            // Revoke recommendations (v1.4)
            profile.RevokeAdvice = _revokeService.Analyze(profile.ApprovalRisk);
        }

        // --- PREMIUM: natural language summary ---
        if (tier == "premium")
        {
            profile.Summary = _summaryService.Generate(profile);
        }

        _cacheService.SetProfile(address, chain, tier, profile);
        return profile;
    }
}
