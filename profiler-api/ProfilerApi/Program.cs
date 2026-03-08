using ProfilerApi.Models;
using ProfilerApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ProfileCacheService>();
builder.Services.AddSingleton<EthereumService>();
builder.Services.AddSingleton<RiskScoringService>();
builder.Services.AddSingleton<SummaryService>();
builder.Services.AddSingleton<DeFiService>();
builder.Services.AddSingleton<WalletTaggingService>();
builder.Services.AddSingleton<PortfolioQualityService>();
builder.Services.AddSingleton<AcpTrustService>();
builder.Services.AddSingleton<ContractLabelService>();
builder.Services.AddSingleton<ApprovalScannerService>();
builder.Services.AddHttpClient<TokenService>();
builder.Services.AddHttpClient<ActivityService>();
builder.Services.AddHttpClient<PriceService>();

var app = builder.Build();

app.MapPost("/profile", async (
    ProfileRequest request,
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
    ProfileCacheService cacheService,
    ILogger<Program> logger) =>
{
    var address = request.Address.Trim();
    var chain = request.Chain.ToLowerInvariant();
    var tier = request.Tier.ToLowerInvariant();

    if (string.IsNullOrEmpty(address))
        return Results.BadRequest(new { error = "Address is required" });

    if (tier is not ("basic" or "standard" or "premium"))
        return Results.BadRequest(new { error = "Tier must be one of: basic, standard, premium" });

    var web3 = ethService.GetWeb3(chain);
    var rpcUrl = ethService.GetRpcUrl(chain);

    // If ENS name provided, resolve to address
    if (address.EndsWith(".eth"))
    {
        try
        {
            var resolved = await ethService.ResolveEnsToAddressAsync(web3, address);
            logger.LogInformation("Resolved ENS {Name} to {Address}", address, resolved);
            address = resolved;
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Could not resolve ENS name: {ex.Message}" });
        }
    }

    // Check cache
    var cached = cacheService.GetProfile(address, chain, tier);
    if (cached != null)
        return Results.Ok(cached);

    // --- BASIC tier: balance + tokens + risk ---
    var balanceTask = ethService.GetEthBalanceAsync(web3, address);
    var txCountTask = ethService.GetTransactionCountAsync(web3, address);
    var ensTask = ethService.ResolveEnsAsync(web3, address);
    var tokensTask = tokenService.GetERC20BalancesAsync(rpcUrl, address, chain, tier);

    var basicTasks = new List<Task> { balanceTask, txCountTask, ensTask, tokensTask };

    // --- STANDARD tier adds: DeFi + activity + USD prices ---
    Task<List<DeFiPosition>>? defiTask = null;
    Task<(WalletActivity Activity, List<(string Address, int Count)> TopCounterparties)>? activityTask = null;

    if (tier is "standard" or "premium")
    {
        defiTask = defiService.GetPositionsAsync(web3, address, chain);
        activityTask = activityService.GetActivityAsync(address, chain);
        basicTasks.Add(defiTask);
        basicTasks.Add(activityTask);
    }

    await Task.WhenAll(basicTasks);

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
        ethPrice = await priceService.EnrichWithPricesAsync(tokens, chain);

        // Sort tokens by USD value (priced first), then by balance
        tokens = tokens
            .OrderBy(t => t.IsSpam)
            .ThenByDescending(t => t.ValueUsd ?? 0)
            .ThenByDescending(t => t.Balance)
            .ToList();

        ethValueUsd = ethPrice.HasValue ? ethBalance * ethPrice.Value : null;
        var tokenValueUsd = tokens.Where(t => t.ValueUsd.HasValue && !t.IsSpam).Sum(t => t.ValueUsd!.Value);
        totalValueUsd = ethValueUsd.HasValue ? ethValueUsd.Value + tokenValueUsd : null;
    }

    var risk = riskService.Assess(ethBalance, txCount, tokens, defiPositions, activity ?? new WalletActivity());

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
    profile.Tags = taggingService.GenerateTags(profile);

    // --- Standard+ features ---
    if (tier is "standard" or "premium")
    {
        profile.PortfolioQuality = qualityService.Evaluate(profile);

        // Contract interaction labels
        if (topCounterparties != null && topCounterparties.Count > 0)
            profile.TopInteractions = labelService.LabelInteractions(topCounterparties);

        // Approval risk scan
        profile.ApprovalRisk = await approvalService.ScanAsync(web3, address, tokens);

        profile.AcpTrust = trustService.Evaluate(profile);
    }

    // --- PREMIUM tier adds: natural language summary ---
    if (tier == "premium")
    {
        profile.Summary = summaryService.Generate(profile);
    }

    // Cache the result
    cacheService.SetProfile(address, chain, tier, profile);

    return Results.Ok(profile);
});

app.MapPost("/profile/batch", async (
    BatchProfileRequest request,
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
    ProfileCacheService cacheService,
    ILogger<Program> logger) =>
{
    if (request.Addresses.Count == 0)
        return Results.BadRequest(new { error = "At least one address is required" });

    if (request.Addresses.Count > 50)
        return Results.BadRequest(new { error = "Maximum 50 addresses per batch" });

    var chain = request.Chain.ToLowerInvariant();
    var tier = request.Tier.ToLowerInvariant();

    if (tier is not ("basic" or "standard" or "premium"))
        return Results.BadRequest(new { error = "Tier must be one of: basic, standard, premium" });

    var sw = System.Diagnostics.Stopwatch.StartNew();

    // Process up to 5 addresses in parallel to avoid overwhelming APIs
    var semaphore = new SemaphoreSlim(5);
    var tasks = request.Addresses.Select(async rawAddress =>
    {
        await semaphore.WaitAsync();
        try
        {
            var address = rawAddress.Trim();
            var web3 = ethService.GetWeb3(chain);
            var rpcUrl = ethService.GetRpcUrl(chain);

            // Resolve ENS if needed
            if (address.EndsWith(".eth"))
            {
                address = await ethService.ResolveEnsToAddressAsync(web3, address);
            }

            // Check cache
            var cached = cacheService.GetProfile(address, chain, tier);
            if (cached != null)
                return new BatchProfileResult { Address = rawAddress, Profile = cached };

            // Fetch data
            var balanceTask = ethService.GetEthBalanceAsync(web3, address);
            var txCountTask = ethService.GetTransactionCountAsync(web3, address);
            var ensTask = ethService.ResolveEnsAsync(web3, address);
            var tokensTask = tokenService.GetERC20BalancesAsync(rpcUrl, address, chain, tier);
            var parallel = new List<Task> { balanceTask, txCountTask, ensTask, tokensTask };

            Task<List<DeFiPosition>>? defiTask = null;
            Task<(WalletActivity Activity, List<(string Address, int Count)> TopCounterparties)>? activityTask = null;
            if (tier is "standard" or "premium")
            {
                defiTask = defiService.GetPositionsAsync(web3, address, chain);
                activityTask = activityService.GetActivityAsync(address, chain);
                parallel.Add(defiTask);
                parallel.Add(activityTask);
            }

            await Task.WhenAll(parallel);

            var tokens = tokensTask.Result;
            WalletActivity? activity = null;
            List<(string Address, int Count)>? topCounterparties = null;
            if (activityTask != null)
            {
                activity = activityTask.Result.Activity;
                topCounterparties = activityTask.Result.TopCounterparties;
            }

            decimal? ethPrice = null, ethValueUsd = null, totalValueUsd = null;
            if (tier is "standard" or "premium")
            {
                ethPrice = await priceService.EnrichWithPricesAsync(tokens, chain);
                tokens = tokens.OrderBy(t => t.IsSpam).ThenByDescending(t => t.ValueUsd ?? 0).ThenByDescending(t => t.Balance).ToList();
                ethValueUsd = ethPrice.HasValue ? balanceTask.Result * ethPrice.Value : null;
                totalValueUsd = ethValueUsd.HasValue ? ethValueUsd.Value + tokens.Where(t => t.ValueUsd.HasValue && !t.IsSpam).Sum(t => t.ValueUsd!.Value) : null;
            }

            var risk = riskService.Assess(balanceTask.Result, txCountTask.Result, tokens, defiTask?.Result ?? [], activity ?? new WalletActivity());

            var profile = new WalletProfile
            {
                Tier = tier,
                Address = address,
                EnsName = ensTask.Result,
                EthBalance = balanceTask.Result,
                EthPriceUsd = ethPrice,
                EthValueUsd = ethValueUsd,
                TotalValueUsd = totalValueUsd,
                TransactionCount = txCountTask.Result,
                TopTokens = tokens,
                DeFiPositions = defiTask?.Result ?? [],
                Risk = risk,
                Activity = activity,
                ProfiledAt = DateTime.UtcNow
            };

            profile.Tags = taggingService.GenerateTags(profile);
            if (tier is "standard" or "premium")
            {
                profile.PortfolioQuality = qualityService.Evaluate(profile);
                if (topCounterparties != null && topCounterparties.Count > 0)
                    profile.TopInteractions = labelService.LabelInteractions(topCounterparties);
                profile.ApprovalRisk = await approvalService.ScanAsync(web3, address, tokens);
                profile.AcpTrust = trustService.Evaluate(profile);
            }
            if (tier == "premium")
                profile.Summary = summaryService.Generate(profile);

            cacheService.SetProfile(address, chain, tier, profile);
            return new BatchProfileResult { Address = rawAddress, Profile = profile };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Batch profile failed for {Address}", rawAddress);
            return new BatchProfileResult { Address = rawAddress, Error = ex.Message };
        }
        finally
        {
            semaphore.Release();
        }
    });

    var results = await Task.WhenAll(tasks);
    sw.Stop();

    return Results.Ok(new BatchProfileResponse
    {
        Total = results.Length,
        Succeeded = results.Count(r => r.Profile != null),
        Failed = results.Count(r => r.Error != null),
        ElapsedMs = sw.ElapsedMilliseconds,
        Results = results.ToList()
    });
});

app.MapGet("/trust/{address}", async (
    string address,
    EthereumService ethService,
    TokenService tokenService,
    ProfileCacheService cacheService,
    ILogger<Program> logger) =>
{
    address = address.Trim();
    var chain = "ethereum";
    var web3 = ethService.GetWeb3(chain);
    var rpcUrl = ethService.GetRpcUrl(chain);

    if (address.EndsWith(".eth"))
    {
        try
        {
            address = await ethService.ResolveEnsToAddressAsync(web3, address);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Could not resolve ENS name: {ex.Message}" });
        }
    }

    // Fast parallel fetch — no metadata, no DeFi, no prices
    var balanceTask = ethService.GetEthBalanceAsync(web3, address);
    var txCountTask = ethService.GetTransactionCountAsync(web3, address);
    var ensTask = ethService.ResolveEnsAsync(web3, address);
    var tokenCountTask = tokenService.GetTokenCountAsync(rpcUrl, address);

    await Task.WhenAll(balanceTask, txCountTask, ensTask, tokenCountTask);

    var ethBalance = balanceTask.Result;
    var txCount = txCountTask.Result;
    var ensName = ensTask.Result;
    var tokenCount = tokenCountTask.Result;

    // Quick trust scoring
    var score = 0;
    var factors = new List<string>();

    // Transaction depth (max 35)
    if (txCount > 1000) { score += 35; factors.Add($"Deep history {txCount} txs (+35)"); }
    else if (txCount > 500) { score += 30; factors.Add($"Strong history {txCount} txs (+30)"); }
    else if (txCount > 100) { score += 25; factors.Add($"Good history {txCount} txs (+25)"); }
    else if (txCount > 50) { score += 20; factors.Add($"Moderate history {txCount} txs (+20)"); }
    else if (txCount > 20) { score += 15; factors.Add($"Some history {txCount} txs (+15)"); }
    else if (txCount > 5) { score += 10; factors.Add($"Light history {txCount} txs (+10)"); }
    else if (txCount > 0) { score += 5; factors.Add($"Minimal history {txCount} txs (+5)"); }

    // ETH balance (max 25)
    if (ethBalance > 10) { score += 25; factors.Add($"Large ETH balance {ethBalance:F2} (+25)"); }
    else if (ethBalance > 1) { score += 20; factors.Add($"Good ETH balance {ethBalance:F2} (+20)"); }
    else if (ethBalance > 0.1m) { score += 15; factors.Add($"Some ETH balance {ethBalance:F4} (+15)"); }
    else if (ethBalance > 0.01m) { score += 10; factors.Add($"Small ETH balance (+10)"); }
    else if (ethBalance > 0) { score += 5; factors.Add("Minimal ETH balance (+5)"); }

    // ENS (max 20)
    if (ensName != null) { score += 20; factors.Add($"ENS: {ensName} (+20)"); }

    // Token holdings (max 20)
    if (tokenCount > 20) { score += 20; factors.Add($"Diverse portfolio {tokenCount} tokens (+20)"); }
    else if (tokenCount > 5) { score += 10; factors.Add($"Some tokens {tokenCount} (+10)"); }
    else if (tokenCount > 0) { score += 5; factors.Add($"Few tokens {tokenCount} (+5)"); }

    score = Math.Clamp(score, 0, 100);

    var level = score switch
    {
        >= 80 => "high",
        >= 60 => "moderate",
        >= 30 => "low",
        _ => "untrusted"
    };

    return Results.Ok(new TrustCheckResponse
    {
        Address = address,
        EnsName = ensName,
        EthBalance = ethBalance,
        TransactionCount = txCount,
        TokenCount = tokenCount,
        TrustScore = score,
        TrustLevel = level,
        Factors = factors
    });
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/tiers", () => Results.Ok(new
{
    basic = new
    {
        fee = "0.0005 ETH",
        includes = new[] { "ETH balance", "ERC-20 tokens (up to 15)", "ENS resolution", "Risk score", "Spam detection", "Wallet tags" }
    },
    standard = new
    {
        fee = "0.001 ETH",
        includes = new[] { "Everything in Basic", "ERC-20 tokens (up to 30)", "USD prices for all tokens", "Total portfolio value", "DeFi positions (Aave, Compound)", "Transaction activity history", "Portfolio quality score", "ACP trust score", "Token approval risk scan", "Contract interaction labels" }
    },
    premium = new
    {
        fee = "0.003 ETH",
        includes = new[] { "Everything in Standard", "ERC-20 tokens (up to 50)", "Natural language summary", "Priority caching" }
    }
}));

app.Run();
