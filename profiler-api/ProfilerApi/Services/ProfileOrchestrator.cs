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
    private readonly SanctionsService _sanctionsService;
    private readonly SmartMoneyService _smartMoneyService;
    private readonly MevDetectionService _mevService;
    private readonly SnapshotService _snapshotService;
    private readonly PnlService _pnlService;
    private readonly LpPositionService _lpPositionService;
    private readonly LiquidationRiskService _liquidationService;
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
        SanctionsService sanctionsService,
        SmartMoneyService smartMoneyService,
        MevDetectionService mevService,
        SnapshotService snapshotService,
        PnlService pnlService,
        LpPositionService lpPositionService,
        LiquidationRiskService liquidationService,
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
        _sanctionsService = sanctionsService;
        _smartMoneyService = smartMoneyService;
        _mevService = mevService;
        _snapshotService = snapshotService;
        _pnlService = pnlService;
        _lpPositionService = lpPositionService;
        _liquidationService = liquidationService;
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
    /// Builds a freemium (free tier) lightweight profile. No token details, no USD prices.
    /// </summary>
    public async Task<FreemiumProfile> BuildFreemiumAsync(string address, string chain)
    {
        var web3 = _ethService.GetWeb3(chain);
        var rpcUrl = _ethService.GetRpcUrl(chain);

        var balanceTask = _ethService.GetEthBalanceAsync(web3, address);
        var txCountTask = _ethService.GetTransactionCountAsync(web3, address);
        var ensTask = _ethService.ResolveEnsAsync(web3, address);
        var tokenCountTask = _tokenService.GetTokenCountAsync(rpcUrl, address);

        await Task.WhenAll(balanceTask, txCountTask, ensTask, tokenCountTask);

        var ethBalance = balanceTask.Result;
        var txCount = txCountTask.Result;

        // Simple risk level
        var riskLevel = (txCount, ethBalance) switch
        {
            (0, _) => "high",
            ( < 5, < 0.01m) => "high",
            ( < 20, _) => "medium",
            _ => "low"
        };

        // Basic tags
        var tags = new List<string>();
        if (ensTask.Result != null) tags.Add("ens-holder");
        if (txCount > 100) tags.Add("active-trader");
        if (ethBalance > 10) tags.Add("well-funded");
        if (txCount == 0) tags.Add("fresh-wallet");

        return new FreemiumProfile
        {
            Address = address,
            EnsName = ensTask.Result,
            EthBalance = ethBalance,
            TransactionCount = txCount,
            TokenCount = tokenCountTask.Result,
            RiskLevel = riskLevel,
            Tags = tags
        };
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

            // Build token prices dictionary (needed by transfer history)
            var tokenPrices = tokens
                .Where(t => t.PriceUsd.HasValue && !t.IsSpam)
                .ToDictionary(t => t.ContractAddress.ToLowerInvariant(), t => t.PriceUsd!.Value);

            // --- Run all async premium features in PARALLEL ---
            var approvalTask = _approvalService.ScanAsync(web3, address, tokens);
            var nftTask = _nftService.GetNftSummaryAsync(rpcUrl, address, ethPrice);
            var transferTask = _transferService.GetTransferHistoryAsync(address, chain, tokenPrices);
            var clusteringTask = _clusteringService.FindSimilarAsync(
                address, chain, tier, tokens, profile.TopInteractions, topCounterparties);
            var mevTask = _mevService.AnalyzeAsync(address, chain);
            var lpTask = _lpPositionService.GetPositionsAsync(web3, address, chain);
            var liquidationTask = _liquidationService.AnalyzeAsync(web3, address, chain);

            await Task.WhenAll(approvalTask, nftTask, transferTask, clusteringTask, mevTask, lpTask, liquidationTask);

            // --- Assign parallel results ---
            profile.ApprovalRisk = approvalTask.Result;
            profile.Nfts = nftTask.Result;
            profile.TransferHistory = transferTask.Result;
            profile.SimilarWallets = clusteringTask.Result;
            profile.MevExposure = mevTask.Result;
            profile.LpPositions = lpTask.Result;
            profile.LiquidationRisk = liquidationTask.Result;

            // --- Detect Pendle positions from token list (no RPC calls) ---
            var pendlePositions = _defiService.DetectPendlePositions(tokens);
            if (pendlePositions.Count > 0)
                profile.DeFiPositions.AddRange(pendlePositions);

            // --- P&L calculation (depends on transfer history) ---
            profile.Pnl = _pnlService.Calculate(profile.TransferHistory, tokens, tokenPrices);

            // --- Sync features that depend on async results ---
            profile.AcpTrust = _trustService.Evaluate(profile);
            profile.RevokeAdvice = _revokeService.Analyze(profile.ApprovalRisk);
            profile.Sanctions = _sanctionsService.Screen(address, profile.TopInteractions);
            profile.SmartMoney = _smartMoneyService.Analyze(profile, profile.TransferHistory);

            // Add NFT value to total if available
            if (profile.Nfts?.EstimatedValueUsd > 0 && totalValueUsd.HasValue)
                profile.TotalValueUsd = totalValueUsd.Value + profile.Nfts.EstimatedValueUsd.Value;
        }

        // --- PREMIUM: natural language summary ---
        if (tier == "premium")
        {
            profile.Summary = _summaryService.Generate(profile);
        }

        _cacheService.SetProfile(address, chain, tier, profile);

        // Record snapshot for historical tracking (v1.6)
        if (tier is "standard" or "premium")
            _snapshotService.RecordSnapshot(profile);

        return profile;
    }
}
