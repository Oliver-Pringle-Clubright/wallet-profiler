using ProfilerApi.Models;
using ProfilerApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();

// Redis distributed cache (optional — falls back to memory if not configured)
var redisConnection = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrEmpty(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = "WalletProfiler:";
    });
}

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
builder.Services.AddSingleton<RevokeRecommendationService>();
builder.Services.AddSingleton<WalletClusteringService>();
builder.Services.AddSingleton<ApiKeyAuthService>();
builder.Services.AddSingleton<SlaTrackingService>();
builder.Services.AddSingleton<SanctionsService>();
builder.Services.AddSingleton<SmartMoneyService>();
builder.Services.AddSingleton<SnapshotService>();
builder.Services.AddSingleton<ReputationBadgeService>();
builder.Services.AddSingleton<SocialIdentityService>();
builder.Services.AddSingleton<ReferralService>();
builder.Services.AddSingleton<WalletComparisonService>();
builder.Services.AddScoped<ProfileOrchestrator>();
builder.Services.AddSingleton<MonitorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MonitorService>());
builder.Services.AddHttpClient<TokenService>();
builder.Services.AddHttpClient<ActivityService>();
builder.Services.AddHttpClient<PriceService>();
builder.Services.AddHttpClient<NftService>();
builder.Services.AddHttpClient<TransferHistoryService>();
builder.Services.AddHttpClient<TokenHolderService>();
builder.Services.AddHttpClient<MevDetectionService>();

var app = builder.Build();

// --- API Key Authentication Middleware (v1.5) ---
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";

    // Skip auth for health, tiers, and SLA endpoints
    if (path is "/health" or "/tiers" or "/sla")
    {
        await next();
        return;
    }

    var authService = context.RequestServices.GetRequiredService<ApiKeyAuthService>();
    if (!authService.IsEnabled)
    {
        await next();
        return;
    }

    var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
    var (isValid, error, _) = authService.ValidateAndCheckRate(apiKey);

    if (!isValid)
    {
        context.Response.StatusCode = error?.Contains("Rate limit") == true ? 429 : 401;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error });
        return;
    }

    await next();
});

// --- POST /profile ---
app.MapPost("/profile", async (
    ProfileRequest request,
    ProfileOrchestrator orchestrator,
    SlaTrackingService sla) =>
{
    var address = request.Address.Trim();
    var chain = request.Chain.ToLowerInvariant();
    var tier = request.Tier.ToLowerInvariant();

    if (string.IsNullOrEmpty(address))
        return Results.BadRequest(new { error = "Address is required" });

    if (tier is not ("free" or "basic" or "standard" or "premium"))
        return Results.BadRequest(new { error = "Tier must be one of: free, basic, standard, premium" });

    using var tracker = sla.Track($"profile_{tier}");
    try
    {
        if (address.EndsWith(".eth"))
            address = await orchestrator.ResolveAddressAsync(address, chain);

        // Freemium tier returns a lightweight profile (v1.7)
        if (tier == "free")
        {
            var freemium = await orchestrator.BuildFreemiumAsync(address, chain);
            return Results.Ok(freemium);
        }

        var profile = await orchestrator.BuildProfileAsync(address, chain, tier);
        return Results.Ok(profile);
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = ex.Message });
    }
});

// --- POST /profile/batch ---
app.MapPost("/profile/batch", async (
    BatchProfileRequest request,
    ProfileOrchestrator orchestrator,
    SlaTrackingService sla,
    ILogger<Program> logger) =>
{
    if (request.Addresses.Count == 0)
        return Results.BadRequest(new { error = "At least one address is required" });

    if (request.Addresses.Count > 50)
        return Results.BadRequest(new { error = "Maximum 50 addresses per batch" });

    var chain = request.Chain.ToLowerInvariant();
    var tier = request.Tier.ToLowerInvariant();

    if (tier is not ("free" or "basic" or "standard" or "premium"))
        return Results.BadRequest(new { error = "Tier must be one of: free, basic, standard, premium" });

    using var tracker = sla.Track("profile_batch");
    var sw = System.Diagnostics.Stopwatch.StartNew();

    var semaphore = new SemaphoreSlim(5);
    var tasks = request.Addresses.Select(async rawAddress =>
    {
        await semaphore.WaitAsync();
        try
        {
            var address = rawAddress.Trim();
            if (address.EndsWith(".eth"))
                address = await orchestrator.ResolveAddressAsync(address, chain);

            var profile = await orchestrator.BuildProfileAsync(address, chain, tier);
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

// --- POST /profile/multi-chain (v1.3) ---
app.MapPost("/profile/multi-chain", async (
    MultiChainRequest request,
    ProfileOrchestrator orchestrator,
    SlaTrackingService sla,
    ILogger<Program> logger) =>
{
    var address = request.Address.Trim();
    var tier = request.Tier.ToLowerInvariant();

    if (string.IsNullOrEmpty(address))
        return Results.BadRequest(new { error = "Address is required" });

    if (tier is not ("free" or "basic" or "standard" or "premium"))
        return Results.BadRequest(new { error = "Tier must be one of: free, basic, standard, premium" });

    if (request.Chains.Count == 0)
        return Results.BadRequest(new { error = "At least one chain is required" });

    if (request.Chains.Count > 5)
        return Results.BadRequest(new { error = "Maximum 5 chains per request" });

    using var tracker = sla.Track("profile_multi_chain");
    try
    {
        var resolvedAddress = address;
        if (address.EndsWith(".eth"))
            resolvedAddress = await orchestrator.ResolveAddressAsync(address, "ethereum");

        var chainTasks = request.Chains.Select(async chain =>
        {
            var c = chain.ToLowerInvariant();
            try
            {
                var profile = await orchestrator.BuildProfileAsync(resolvedAddress, c, tier);
                return (Chain: c, Profile: profile, Error: (string?)null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Multi-chain profile failed for {Chain}", c);
                return (Chain: c, Profile: (WalletProfile?)null, Error: ex.Message);
            }
        });

        var results = await Task.WhenAll(chainTasks);

        var chainProfiles = new Dictionary<string, WalletProfile>();
        var activeChains = new List<string>();
        decimal totalValueUsd = 0;
        string? ensName = null;

        foreach (var r in results)
        {
            if (r.Profile == null) continue;
            chainProfiles[r.Chain] = r.Profile;

            if (r.Profile.EthBalance > 0 || r.Profile.TopTokens.Count > 0)
                activeChains.Add(r.Chain);

            if (r.Profile.TotalValueUsd.HasValue)
                totalValueUsd += r.Profile.TotalValueUsd.Value;

            if (ensName == null && r.Profile.EnsName != null)
                ensName = r.Profile.EnsName;
        }

        return Results.Ok(new MultiChainProfile
        {
            Address = resolvedAddress,
            EnsName = ensName,
            TotalValueUsd = totalValueUsd > 0 ? totalValueUsd : null,
            ChainProfiles = chainProfiles,
            ActiveChains = activeChains,
            ProfiledAt = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = ex.Message });
    }
});

// --- GET /trust/{address} ---
app.MapGet("/trust/{address}", async (
    string address,
    EthereumService ethService,
    TokenService tokenService,
    SlaTrackingService sla,
    ILogger<Program> logger) =>
{
    using var tracker = sla.Track("trust");
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
            tracker.MarkFailed();
            return Results.BadRequest(new { error = $"Could not resolve ENS name: {ex.Message}" });
        }
    }

    var balanceTask = ethService.GetEthBalanceAsync(web3, address);
    var txCountTask = ethService.GetTransactionCountAsync(web3, address);
    var ensTask = ethService.ResolveEnsAsync(web3, address);
    var tokenCountTask = tokenService.GetTokenCountAsync(rpcUrl, address);

    await Task.WhenAll(balanceTask, txCountTask, ensTask, tokenCountTask);

    var ethBalance = balanceTask.Result;
    var txCount = txCountTask.Result;
    var ensName = ensTask.Result;
    var tokenCount = tokenCountTask.Result;

    var score = 0;
    var factors = new List<string>();

    if (txCount > 1000) { score += 35; factors.Add($"Deep history {txCount} txs (+35)"); }
    else if (txCount > 500) { score += 30; factors.Add($"Strong history {txCount} txs (+30)"); }
    else if (txCount > 100) { score += 25; factors.Add($"Good history {txCount} txs (+25)"); }
    else if (txCount > 50) { score += 20; factors.Add($"Moderate history {txCount} txs (+20)"); }
    else if (txCount > 20) { score += 15; factors.Add($"Some history {txCount} txs (+15)"); }
    else if (txCount > 5) { score += 10; factors.Add($"Light history {txCount} txs (+10)"); }
    else if (txCount > 0) { score += 5; factors.Add($"Minimal history {txCount} txs (+5)"); }

    if (ethBalance > 10) { score += 25; factors.Add($"Large ETH balance {ethBalance:F2} (+25)"); }
    else if (ethBalance > 1) { score += 20; factors.Add($"Good ETH balance {ethBalance:F2} (+20)"); }
    else if (ethBalance > 0.1m) { score += 15; factors.Add($"Some ETH balance {ethBalance:F4} (+15)"); }
    else if (ethBalance > 0.01m) { score += 10; factors.Add($"Small ETH balance (+10)"); }
    else if (ethBalance > 0) { score += 5; factors.Add("Minimal ETH balance (+5)"); }

    if (ensName != null) { score += 20; factors.Add($"ENS: {ensName} (+20)"); }

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

// --- POST /monitor (v1.3) ---
app.MapPost("/monitor", (
    MonitorRequest request,
    MonitorService monitorService,
    SlaTrackingService sla) =>
{
    using var tracker = sla.Track("monitor");
    if (string.IsNullOrEmpty(request.Address))
        return Results.BadRequest(new { error = "Address is required" });

    if (string.IsNullOrEmpty(request.WebhookUrl))
        return Results.BadRequest(new { error = "WebhookUrl is required" });

    var sub = monitorService.Subscribe(request);
    return Results.Ok(sub);
});

// --- GET /monitor (v1.3) ---
app.MapGet("/monitor", (MonitorService monitorService) =>
{
    return Results.Ok(monitorService.GetStatus());
});

// --- DELETE /monitor/{id} (v1.3) ---
app.MapDelete("/monitor/{id}", (string id, MonitorService monitorService) =>
{
    if (monitorService.Unsubscribe(id))
        return Results.Ok(new { message = $"Subscription {id} removed" });

    return Results.NotFound(new { error = $"Subscription {id} not found" });
});

// --- GET /token/{contract}/holders (v1.6) ---
app.MapGet("/token/{contract}/holders", async (
    string contract,
    TokenHolderService holderService,
    SlaTrackingService sla,
    HttpContext httpContext) =>
{
    using var tracker = sla.Track("token_holders");
    var chain = httpContext.Request.Query["chain"].FirstOrDefault() ?? "ethereum";
    var tier = httpContext.Request.Query["tier"].FirstOrDefault() ?? "standard";
    var limitStr = httpContext.Request.Query["limit"].FirstOrDefault();
    var limit = int.TryParse(limitStr, out var l) ? l : 20;

    try
    {
        var analysis = await holderService.AnalyzeHoldersAsync(contract, chain.ToLowerInvariant(), tier.ToLowerInvariant(), limit);
        return Results.Ok(analysis);
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = ex.Message });
    }
});

// --- GET /history/{address} (v1.6) ---
app.MapGet("/history/{address}", (
    string address,
    SnapshotService snapshotService,
    SlaTrackingService sla,
    HttpContext httpContext) =>
{
    using var tracker = sla.Track("history");
    var daysStr = httpContext.Request.Query["days"].FirstOrDefault();
    var days = int.TryParse(daysStr, out var d) ? Math.Clamp(d, 1, 365) : 30;

    return Results.Ok(snapshotService.GetHistory(address.Trim(), days));
});

// --- GET /monitor/plans (v1.6) ---
app.MapGet("/monitor/plans", () =>
{
    return Results.Ok(MonitorService.GetPlans());
});

// --- GET /reputation/{address} (v1.7) ---
app.MapGet("/reputation/{address}", async (
    string address,
    ProfileOrchestrator orchestrator,
    ReputationBadgeService badgeService,
    SlaTrackingService sla) =>
{
    using var tracker = sla.Track("reputation");
    try
    {
        var resolved = address.Trim();
        if (resolved.EndsWith(".eth"))
            resolved = await orchestrator.ResolveAddressAsync(resolved, "ethereum");

        var profile = await orchestrator.BuildProfileAsync(resolved, "ethereum", "standard");
        var badge = badgeService.GenerateBadge(profile);
        return Results.Ok(badge);
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = ex.Message });
    }
});

// --- GET /pricing/enterprise (v1.7) ---
app.MapGet("/pricing/enterprise", () =>
{
    var plans = new List<EnterprisePricingPlan>
    {
        new()
        {
            Plan = "starter",
            MonthlyFeeEth = 0.5m,
            IncludedProfiles = 1000,
            OverageFeeEth = 0.0008m,
            SupportLevel = "email",
            Features = ["All standard tier features", "API key with 100 req/min", "Email support"]
        },
        new()
        {
            Plan = "growth",
            MonthlyFeeEth = 2m,
            IncludedProfiles = 5000,
            OverageFeeEth = 0.0006m,
            SupportLevel = "priority",
            Features = ["All premium tier features", "API key with 500 req/min", "Priority support", "Custom webhooks", "Bulk batch (100 addresses)"]
        },
        new()
        {
            Plan = "enterprise",
            MonthlyFeeEth = 10m,
            IncludedProfiles = 50000,
            OverageFeeEth = 0.0003m,
            SupportLevel = "dedicated",
            Features = ["All premium tier features", "Unlimited rate", "Dedicated support", "Custom integrations", "SLA guarantee (99.9%)", "White-label option"]
        }
    };
    return Results.Ok(plans);
});

// --- GET /chains (v1.7) ---
app.MapGet("/chains", () =>
{
    var chains = ChainConfig.Chains.Select(c => new
    {
        name = c.Key,
        chainId = c.Value.ChainId,
        nativeToken = ChainConfig.GetNativeSymbol(c.Key)
    });
    return Results.Ok(chains);
});

// --- POST /compare (v1.8) ---
app.MapPost("/compare", async (
    WalletComparisonRequest request,
    ProfileOrchestrator orchestrator,
    WalletComparisonService comparisonService,
    SlaTrackingService sla,
    ILogger<Program> logger) =>
{
    if (request.Addresses.Count < 2)
        return Results.BadRequest(new { error = "At least 2 addresses required" });

    if (request.Addresses.Count > 10)
        return Results.BadRequest(new { error = "Maximum 10 addresses per comparison" });

    using var tracker = sla.Track("compare");
    try
    {
        var chain = request.Chain.ToLowerInvariant();
        var tier = request.Tier.ToLowerInvariant();

        var profiles = new List<WalletProfile>();
        var semaphore = new SemaphoreSlim(5);
        var tasks = request.Addresses.Select(async rawAddress =>
        {
            await semaphore.WaitAsync();
            try
            {
                var address = rawAddress.Trim();
                if (address.EndsWith(".eth"))
                    address = await orchestrator.ResolveAddressAsync(address, chain);
                return await orchestrator.BuildProfileAsync(address, chain, tier);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Comparison failed for {Address}", rawAddress);
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        profiles = results.Where(p => p != null).Cast<WalletProfile>().ToList();

        if (profiles.Count < 2)
            return Results.BadRequest(new { error = "Need at least 2 valid profiles to compare" });

        var comparison = comparisonService.Compare(profiles);
        return Results.Ok(comparison);
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = ex.Message });
    }
});

// --- GET /identity/{address} (v1.8) ---
app.MapGet("/identity/{address}", async (
    string address,
    ProfileOrchestrator orchestrator,
    SocialIdentityService identityService,
    SlaTrackingService sla) =>
{
    using var tracker = sla.Track("identity");
    try
    {
        var resolved = address.Trim();
        if (resolved.EndsWith(".eth"))
            resolved = await orchestrator.ResolveAddressAsync(resolved, "ethereum");

        var profile = await orchestrator.BuildProfileAsync(resolved, "ethereum", "standard");
        var identity = await identityService.AnalyzeAsync(resolved, profile);
        return Results.Ok(identity);
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = ex.Message });
    }
});

// --- POST /referral/register (v1.8) ---
app.MapPost("/referral/register", (
    ReferralRequest request,
    ReferralService referralService) =>
{
    if (string.IsNullOrEmpty(request.AgentAddress))
        return Results.BadRequest(new { error = "AgentAddress is required" });

    var code = referralService.Register(request.AgentAddress);
    return Results.Ok(new { referralCode = code, commissionRate = "10%" });
});

// --- GET /referral/{address} (v1.8) ---
app.MapGet("/referral/{address}", (
    string address,
    ReferralService referralService) =>
{
    var stats = referralService.GetStats(address);
    return Results.Ok(stats);
});

// --- GET /sla (v1.5) ---
app.MapGet("/sla", (SlaTrackingService sla, ProfileCacheService cache) =>
{
    var report = sla.GetReport();
    return Results.Ok(new
    {
        report.GeneratedAt,
        cacheBackend = cache.CacheBackend,
        report.Endpoints
    });
});

// --- Utility endpoints ---
app.MapGet("/health", (ProfileCacheService cache) => Results.Ok(new
{
    status = "healthy",
    cacheBackend = cache.CacheBackend
}));

app.MapGet("/tiers", () => Results.Ok(new
{
    free = new
    {
        fee = "0 ETH",
        includes = new[] { "ETH balance", "Transaction count", "Token count", "Risk level", "Basic tags", "Upgrade hints" }
    },
    basic = new
    {
        fee = "0.0005 ETH",
        includes = new[] { "ETH balance", "ERC-20 tokens (up to 15)", "ENS resolution", "Risk score", "Spam detection", "Wallet tags" }
    },
    standard = new
    {
        fee = "0.001 ETH",
        includes = new[] { "Everything in Basic", "ERC-20 tokens (up to 30)", "USD prices for all tokens", "Total portfolio value", "DeFi positions (Aave, Compound)", "Transaction activity history", "Portfolio quality score", "ACP trust score", "Token approval risk scan", "Contract interaction labels", "NFT holdings & floor prices", "Cross-chain support", "Token transfer history timeline", "Similar wallet clustering", "Revoke recommendation engine", "OFAC sanctions screening", "Smart money analysis", "Portfolio snapshots & history", "MEV exposure detection" }
    },
    premium = new
    {
        fee = "0.003 ETH",
        includes = new[] { "Everything in Standard", "ERC-20 tokens (up to 50)", "Natural language summary", "Priority caching" }
    }
}));

app.Run();
