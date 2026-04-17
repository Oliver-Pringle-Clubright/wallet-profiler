using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProfilerApi.Data;
using ProfilerApi.Hubs;
using ProfilerApi.Models;
using ProfilerApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Request size limit (1MB max)
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);

builder.Services.AddMemoryCache();

// PostgreSQL (optional — falls back to in-memory if not configured)
var pgConnection = builder.Configuration.GetConnectionString("ProfilerDb");
if (!string.IsNullOrEmpty(pgConnection))
{
    builder.Services.AddDbContext<ProfilerDbContext>(options =>
        options.UseNpgsql(pgConnection));
}

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
builder.Services.AddSingleton<PnlService>();
builder.Services.AddSingleton<LpPositionService>();
builder.Services.AddSingleton<LiquidationRiskService>();
builder.Services.AddSingleton<RebalancingService>();
builder.Services.AddSingleton<AirdropEligibilityService>();
builder.Services.AddScoped<ProfileOrchestrator>();
builder.Services.AddSingleton<MonitorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MonitorService>());
builder.Services.AddSingleton<WalletStreamingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WalletStreamingService>());
builder.Services.AddHttpClient<TokenService>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<ActivityService>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<PriceService>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<NftService>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<TransferHistoryService>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<TokenHolderService>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<MevDetectionService>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<WhaleAlertService>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<SolanaService>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<VirtualsIntelService>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<AiAnalystService>(c => c.Timeout = TimeSpan.FromSeconds(60));

// SignalR for real-time WebSocket streaming
builder.Services.AddSignalR();

// Blazor Server for dashboard UI
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// CORS — restrict to known origins (configurable via AllowedOrigins in appsettings)
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000", "http://localhost:5000"];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .WithMethods("GET", "POST", "DELETE")
              .AllowCredentials();
    });
});

var app = builder.Build();

// Auto-migrate PostgreSQL if configured
if (!string.IsNullOrEmpty(pgConnection))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProfilerDbContext>();
        db.Database.EnsureCreated();
        app.Logger.LogInformation("PostgreSQL database initialized");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "PostgreSQL not available — falling back to in-memory storage");
    }
}

app.UseCors();
app.UseStaticFiles();
app.UseAntiforgery();

// --- Validation helpers ---
static bool IsValidEvmAddress(string addr) =>
    addr.Length == 42 && addr.StartsWith("0x") && Regex.IsMatch(addr, @"^0x[a-fA-F0-9]{40}$");

var validChains = new HashSet<string>(ChainConfig.AllChains, StringComparer.OrdinalIgnoreCase);

// --- Security headers middleware ---
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    await next();
});

// --- Warn if auth is disabled ---
{
    var authCheck = app.Services.GetRequiredService<ApiKeyAuthService>();
    if (!authCheck.IsEnabled)
    {
        var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
        startupLogger.LogWarning("⚠ API key authentication is DISABLED — no keys configured. All endpoints are open.");
    }
}

// --- API Key Authentication Middleware (v1.5) ---
app.Use(async (context, next) =>
{
    var path = (context.Request.Path.Value ?? "").TrimEnd('/').ToLowerInvariant();

    // Skip auth for health, tiers, SLA, dashboard, and SignalR endpoints
    if (path is "/health" or "/tiers" or "/sla" ||
        path.StartsWith("/dashboard") || path.StartsWith("/css") || path.StartsWith("/_framework") ||
        path.StartsWith("/_blazor") || path.StartsWith("/ws/"))
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

    // Case-insensitive header lookup
    var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault()
              ?? context.Request.Headers["x-api-key"].FirstOrDefault();
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
    SlaTrackingService sla,
    ILogger<Program> logger) =>
{
    var address = request.Address.Trim();
    var chain = request.Chain.ToLowerInvariant();
    var tier = request.Tier.ToLowerInvariant();

    if (string.IsNullOrEmpty(address))
        return Results.BadRequest(new { error = "Address is required" });

    if (address.Length > 255 || (!IsValidEvmAddress(address) && !address.EndsWith(".eth")))
        return Results.BadRequest(new { error = "Invalid address format" });

    if (!validChains.Contains(chain))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

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
        logger.LogError(ex, "Profile failed for {Address} on {Chain}", address, chain);
        tracker.MarkFailed();
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
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

    if (!validChains.Contains(chain))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

    if (tier is not ("free" or "basic" or "standard" or "premium"))
        return Results.BadRequest(new { error = "Tier must be one of: free, basic, standard, premium" });

    // Validate individual addresses
    var invalidAddresses = request.Addresses
        .Where(a => { var t = a.Trim(); return t.Length > 255 || (!IsValidEvmAddress(t) && !t.EndsWith(".eth")); })
        .ToList();
    if (invalidAddresses.Count > 0)
        return Results.BadRequest(new { error = $"Invalid address format: {invalidAddresses.First().Trim()}" });

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
            return new BatchProfileResult { Address = rawAddress, Error = "Profile failed for this address" };
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

    var invalidChains = request.Chains.Where(c => !validChains.Contains(c.ToLowerInvariant())).ToList();
    if (invalidChains.Count > 0)
        return Results.BadRequest(new { error = $"Unsupported chain: {invalidChains.First()}. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

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
                return (Chain: c, Profile: (WalletProfile?)null, Error: "Profile failed");
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
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
    }
});

// --- GET /trust/{address} (Solana v2.6) ---
app.MapGet("/trust/{address}", async (
    string address,
    EthereumService ethService,
    TokenService tokenService,
    SolanaService solanaService,
    SlaTrackingService sla,
    HttpContext httpContext,
    ILogger<Program> logger) =>
{
    using var tracker = sla.Track("trust");
    address = address.Trim();
    var chain = (httpContext.Request.Query["chain"].FirstOrDefault() ?? "ethereum").ToLowerInvariant();

    if (!validChains.Contains(chain))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

    if (address.Length > 255 || (!IsValidEvmAddress(address) && !address.EndsWith(".eth") && !SolanaService.IsValidSolanaAddress(address)))
        return Results.BadRequest(new { error = "Invalid address format" });

    // Solana trust check
    if (ChainConfig.IsSolana(chain))
    {
        if (!SolanaService.IsValidSolanaAddress(address))
            return Results.BadRequest(new { error = "Invalid Solana address format" });

        var solBalance = await solanaService.GetSolBalanceAsync(address);
        var solTxCount = await solanaService.GetTransactionCountAsync(address);
        var solTokenCount = await solanaService.GetTokenAccountCountAsync(address);

        var solScore = 0;
        var solFactors = new List<string>();

        if (solTxCount > 1000) { solScore += 35; solFactors.Add($"Deep history {solTxCount} txs (+35)"); }
        else if (solTxCount > 500) { solScore += 30; solFactors.Add($"Strong history {solTxCount} txs (+30)"); }
        else if (solTxCount > 100) { solScore += 25; solFactors.Add($"Good history {solTxCount} txs (+25)"); }
        else if (solTxCount > 50) { solScore += 20; solFactors.Add($"Moderate history {solTxCount} txs (+20)"); }
        else if (solTxCount > 20) { solScore += 15; solFactors.Add($"Some history {solTxCount} txs (+15)"); }
        else if (solTxCount > 5) { solScore += 10; solFactors.Add($"Light history {solTxCount} txs (+10)"); }
        else if (solTxCount > 0) { solScore += 5; solFactors.Add($"Minimal history {solTxCount} txs (+5)"); }

        if (solBalance > 100) { solScore += 25; solFactors.Add($"Large SOL balance {solBalance:F2} (+25)"); }
        else if (solBalance > 10) { solScore += 20; solFactors.Add($"Good SOL balance {solBalance:F2} (+20)"); }
        else if (solBalance > 1) { solScore += 15; solFactors.Add($"Some SOL balance {solBalance:F2} (+15)"); }
        else if (solBalance > 0.1m) { solScore += 10; solFactors.Add($"Small SOL balance (+10)"); }
        else if (solBalance > 0) { solScore += 5; solFactors.Add("Minimal SOL balance (+5)"); }

        if (solTokenCount > 20) { solScore += 20; solFactors.Add($"Diverse portfolio {solTokenCount} tokens (+20)"); }
        else if (solTokenCount > 5) { solScore += 10; solFactors.Add($"Some tokens {solTokenCount} (+10)"); }
        else if (solTokenCount > 0) { solScore += 5; solFactors.Add($"Few tokens {solTokenCount} (+5)"); }

        solScore = Math.Clamp(solScore, 0, 100);

        return Results.Ok(new TrustCheckResponse
        {
            Address = address,
            EthBalance = solBalance,
            TransactionCount = solTxCount,
            TokenCount = solTokenCount,
            TrustScore = solScore,
            TrustLevel = solScore >= 80 ? "high" : solScore >= 60 ? "moderate" : solScore >= 30 ? "low" : "untrusted",
            Factors = solFactors
        });
    }

    // EVM trust check
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
            return Results.BadRequest(new { error = "Could not resolve ENS name" });
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

    try
    {
        var sub = monitorService.Subscribe(request);
        return Results.Ok(sub);
    }
    catch (ArgumentException)
    {
        return Results.BadRequest(new { error = "Webhook URL must be a valid public HTTP/HTTPS URL" });
    }
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
    var chain = (httpContext.Request.Query["chain"].FirstOrDefault() ?? "ethereum").ToLowerInvariant();
    var tier = (httpContext.Request.Query["tier"].FirstOrDefault() ?? "standard").ToLowerInvariant();
    var limitStr = httpContext.Request.Query["limit"].FirstOrDefault();
    var limit = int.TryParse(limitStr, out var l) ? Math.Clamp(l, 1, 100) : 20;

    if (!validChains.Contains(chain))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

    try
    {
        var analysis = await holderService.AnalyzeHoldersAsync(contract, chain.ToLowerInvariant(), tier.ToLowerInvariant(), limit);
        return Results.Ok(analysis);
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
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
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
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

    var chain = request.Chain.ToLowerInvariant();
    var tier = request.Tier.ToLowerInvariant();

    if (!validChains.Contains(chain))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

    using var tracker = sla.Track("compare");
    try
    {

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
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
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
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
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

// --- GET /risk/{address} (v2.7) ---
app.MapGet("/risk/{address}", async (
    string address,
    ProfileOrchestrator orchestrator,
    SlaTrackingService sla,
    HttpContext httpContext) =>
{
    using var tracker = sla.Track("risk_score");
    var chain = (httpContext.Request.Query["chain"].FirstOrDefault() ?? "ethereum").ToLowerInvariant();
    address = address.Trim();

    if (!validChains.Contains(chain))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

    if (address.Length > 255 || (!IsValidEvmAddress(address) && !address.EndsWith(".eth")))
        return Results.BadRequest(new { error = "Invalid address format" });

    try
    {
        if (address.EndsWith(".eth"))
            address = await orchestrator.ResolveAddressAsync(address, chain);

        var profile = await orchestrator.BuildProfileAsync(address, chain, "basic");

        var verdict = profile.Risk.Score switch
        {
            >= 70 => "DANGER",
            >= 50 => "WARNING",
            >= 30 => "CAUTION",
            _ => "SAFE"
        };

        return Results.Ok(new RiskScoreResponse
        {
            Address = address,
            Chain = chain,
            RiskScore = profile.Risk.Score,
            RiskLevel = profile.Risk.Level,
            Verdict = verdict,
            RiskFlags = profile.Risk.Flags,
            Tags = profile.Tags,
            IsSanctioned = profile.Sanctions?.IsSanctioned ?? false,
            SanctionsRisk = profile.Sanctions?.RiskLevel ?? "clear",
            ApprovalRiskCount = profile.ApprovalRisk?.TotalApprovals ?? 0,
            UnlimitedApprovals = profile.ApprovalRisk?.UnlimitedApprovals ?? 0
        });
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
    }
});

// --- GET /virtuals/ecosystem (v2.8) ---
app.MapGet("/virtuals/ecosystem", async (
    VirtualsIntelService virtualsService,
    SlaTrackingService sla,
    HttpContext httpContext) =>
{
    using var tracker = sla.Track("virtuals_intel");
    var query = httpContext.Request.Query["query"].FirstOrDefault();

    if (query != null && query.Length > 500)
        return Results.BadRequest(new { error = "Query too long. Maximum 500 characters." });

    try
    {
        var report = await virtualsService.GetEcosystemReportAsync(query);
        if (report.Error != null)
            return Results.BadRequest(new { error = report.Error });
        return Results.Ok(report);
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
    }
});

// --- GET /status/{address} (v2.2, Solana v2.6) ---
app.MapGet("/status/{address}", async (
    string address,
    EthereumService ethService,
    SolanaService solanaService,
    SlaTrackingService sla,
    HttpContext httpContext) =>
{
    using var tracker = sla.Track("status");
    var chain = (httpContext.Request.Query["chain"].FirstOrDefault() ?? "ethereum").ToLowerInvariant();
    address = address.Trim();

    if (!validChains.Contains(chain))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

    if (address.Length > 255 || (!IsValidEvmAddress(address) && !address.EndsWith(".eth") && !SolanaService.IsValidSolanaAddress(address)))
        return Results.BadRequest(new { error = "Invalid address format" });

    try
    {
        // Solana: separate RPC path
        if (ChainConfig.IsSolana(chain))
        {
            if (!SolanaService.IsValidSolanaAddress(address))
                return Results.BadRequest(new { error = "Invalid Solana address format" });

            var balanceTask = solanaService.GetSolBalanceAsync(address);
            var txCountTask = solanaService.GetTransactionCountAsync(address);
            var isContractTask = solanaService.IsContractAsync(address);

            await Task.WhenAll(balanceTask, txCountTask, isContractTask);

            return Results.Ok(new WalletStatusResponse
            {
                Address = address,
                Chain = "solana",
                EthBalance = balanceTask.Result,
                TransactionCount = txCountTask.Result,
                IsContract = isContractTask.Result
            });
        }

        // EVM chains
        var web3 = ethService.GetWeb3(chain);

        if (address.EndsWith(".eth"))
        {
            try { address = await ethService.ResolveEnsToAddressAsync(web3, address); }
            catch { return Results.BadRequest(new { error = "Could not resolve ENS name" }); }
        }

        var evmBalanceTask = ethService.GetEthBalanceAsync(web3, address);
        var evmTxCountTask = ethService.GetTransactionCountAsync(web3, address);
        var codeTask = web3.Eth.GetCode.SendRequestAsync(address);

        await Task.WhenAll(evmBalanceTask, evmTxCountTask, codeTask);

        var code = codeTask.Result;
        var isEvmContract = !string.IsNullOrEmpty(code) && code != "0x" && code != "0x0";

        return Results.Ok(new WalletStatusResponse
        {
            Address = address,
            Chain = chain,
            EthBalance = evmBalanceTask.Result,
            TransactionCount = evmTxCountTask.Result,
            IsContract = isEvmContract
        });
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
    }
});

// --- GET /whales/{chain}/recent (v2.0) ---
app.MapGet("/whales/{chain}/recent", async (
    string chain,
    WhaleAlertService whaleService,
    SlaTrackingService sla,
    int? hours,
    decimal? minValue) =>
{
    var chainLower = chain.ToLowerInvariant();
    if (!validChains.Contains(chainLower))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

    var clampedHours = Math.Clamp(hours ?? 24, 1, 168);
    var clampedMinValue = Math.Max(minValue ?? 100_000m, 0m);

    using var tracker = sla.Track("whale_alerts");
    try
    {
        var result = await whaleService.GetRecentWhaleTransfersAsync(
            chainLower,
            clampedHours,
            clampedMinValue);

        if (result.Error != null)
            return Results.BadRequest(new { error = result.Error });

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
    }
});

// --- GET /gas/{address} (v3.0) ---
app.MapGet("/gas/{address}", async (
    string address,
    ActivityService activityService,
    EthereumService ethService,
    PriceService priceService,
    SlaTrackingService sla,
    HttpContext httpContext) =>
{
    using var tracker = sla.Track("gas_analysis");
    var chain = (httpContext.Request.Query["chain"].FirstOrDefault() ?? "ethereum").ToLowerInvariant();
    address = address.Trim();

    if (!validChains.Contains(chain))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

    if (address.Length > 255 || (!IsValidEvmAddress(address) && !address.EndsWith(".eth")))
        return Results.BadRequest(new { error = "Invalid address format" });

    try
    {
        if (address.EndsWith(".eth"))
        {
            var web3 = ethService.GetWeb3(chain);
            address = await ethService.ResolveEnsToAddressAsync(web3, address);
        }

        // Fetch tx list via Etherscan V2
        if (!ChainConfig.Chains.TryGetValue(chain, out var chainConfig))
            return Results.BadRequest(new { error = "Unsupported chain" });

        var apiKey = ethService.GetEtherscanApiKey();
        if (string.IsNullOrEmpty(apiKey))
            return Results.BadRequest(new { error = "Etherscan API key not configured" });

        var url = $"{ChainConfig.EtherscanV2BaseUrl}?chainid={chainConfig.ChainId}&module=account&action=txlist&address={address}&startblock=0&endblock=99999999&page=1&offset=1000&sort=desc&apikey={apiKey}";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var response = await http.GetFromJsonAsync<EtherscanResponse<GasTxDto>>(url);

        if (response?.Result is null || response.Result.Count == 0)
            return Results.Ok(new { address, chain, totalTransactions = 0, totalGasSpentEth = 0m, message = "No transactions found" });

        var txs = response.Result;
        var outboundTxs = txs.Where(t => t.From?.Equals(address, StringComparison.OrdinalIgnoreCase) == true).ToList();

        var totalGasWei = outboundTxs.Sum(t =>
        {
            if (long.TryParse(t.GasUsed, out var gasUsed) && long.TryParse(t.GasPrice, out var gasPrice))
                return (decimal)gasUsed * gasPrice;
            return 0m;
        });
        var totalGasEth = totalGasWei / 1_000_000_000_000_000_000m;

        var avgGasPriceGwei = outboundTxs.Count > 0
            ? outboundTxs.Average(t => long.TryParse(t.GasPrice, out var gp) ? gp / 1_000_000_000.0 : 0)
            : 0;

        // Monthly breakdown
        var monthlyGas = outboundTxs
            .Where(t => long.TryParse(t.TimeStamp, out _))
            .GroupBy(t => DateTimeOffset.FromUnixTimeSeconds(long.Parse(t.TimeStamp!)).ToString("yyyy-MM"))
            .OrderByDescending(g => g.Key)
            .Take(6)
            .Select(g => new
            {
                period = g.Key,
                txCount = g.Count(),
                gasSpentEth = g.Sum(t =>
                {
                    if (long.TryParse(t.GasUsed, out var gu) && long.TryParse(t.GasPrice, out var gp))
                        return (decimal)gu * gp / 1_000_000_000_000_000_000m;
                    return 0m;
                })
            })
            .ToList();

        // Top 5 most expensive transactions
        var topGasTxs = outboundTxs
            .Select(t =>
            {
                long.TryParse(t.GasUsed, out var gu);
                long.TryParse(t.GasPrice, out var gp);
                return new { t.Hash, GasEth = (decimal)gu * gp / 1_000_000_000_000_000_000m, t.To, t.TimeStamp };
            })
            .OrderByDescending(t => t.GasEth)
            .Take(5)
            .Select(t => new
            {
                txHash = t.Hash,
                gasSpentEth = t.GasEth,
                to = t.To,
                timestamp = long.TryParse(t.TimeStamp, out var ts)
                    ? DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime
                    : (DateTime?)null
            })
            .ToList();

        // Get ETH price for USD conversion
        var (ethPrice, _) = await priceService.GetAllPricesAsync([], chain);

        return Results.Ok(new
        {
            address,
            chain,
            totalTransactions = txs.Count,
            outboundTransactions = outboundTxs.Count,
            totalGasSpentEth = Math.Round(totalGasEth, 8),
            totalGasSpentUsd = ethPrice.HasValue ? Math.Round(totalGasEth * ethPrice.Value, 2) : (decimal?)null,
            avgGasPriceGwei = Math.Round(avgGasPriceGwei, 2),
            ethPriceUsd = ethPrice,
            monthlyBreakdown = monthlyGas,
            topGasTransactions = topGasTxs,
            analyzedAt = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
    }
});

// --- GET /pnl/{address} (v3.1) ---
app.MapGet("/pnl/{address}", async (
    string address,
    ProfileOrchestrator orchestrator,
    SlaTrackingService sla,
    HttpContext httpContext) =>
{
    using var tracker = sla.Track("pnl");
    var chain = (httpContext.Request.Query["chain"].FirstOrDefault() ?? "ethereum").ToLowerInvariant();
    address = address.Trim();

    if (!validChains.Contains(chain))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

    if (address.Length > 255 || (!IsValidEvmAddress(address) && !address.EndsWith(".eth")))
        return Results.BadRequest(new { error = "Invalid address format" });

    try
    {
        if (address.EndsWith(".eth"))
            address = await orchestrator.ResolveAddressAsync(address, chain);

        // Build standard profile (includes P&L calculation)
        var profile = await orchestrator.BuildProfileAsync(address, chain, "standard");
        return Results.Ok(new
        {
            address,
            chain,
            pnl = profile.Pnl,
            totalValueUsd = profile.TotalValueUsd,
            profiledAt = profile.ProfiledAt
        });
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
    }
});

// --- GET /lp-positions/{address} (v3.1) ---
app.MapGet("/lp-positions/{address}", async (
    string address,
    ProfileOrchestrator orchestrator,
    SlaTrackingService sla,
    HttpContext httpContext) =>
{
    using var tracker = sla.Track("lp_positions");
    var chain = (httpContext.Request.Query["chain"].FirstOrDefault() ?? "ethereum").ToLowerInvariant();
    address = address.Trim();

    if (!validChains.Contains(chain))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

    if (address.Length > 255 || (!IsValidEvmAddress(address) && !address.EndsWith(".eth")))
        return Results.BadRequest(new { error = "Invalid address format" });

    try
    {
        if (address.EndsWith(".eth"))
            address = await orchestrator.ResolveAddressAsync(address, chain);

        var profile = await orchestrator.BuildProfileAsync(address, chain, "standard");
        return Results.Ok(new
        {
            address,
            chain,
            lpPositions = profile.LpPositions,
            totalPositions = profile.LpPositions.Count,
            activePositions = profile.LpPositions.Count(p => p.Status == "active"),
            profiledAt = profile.ProfiledAt
        });
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
    }
});

// --- GET /liquidation-risk/{address} (v3.1) ---
app.MapGet("/liquidation-risk/{address}", async (
    string address,
    ProfileOrchestrator orchestrator,
    SlaTrackingService sla,
    HttpContext httpContext) =>
{
    using var tracker = sla.Track("liquidation_risk");
    var chain = (httpContext.Request.Query["chain"].FirstOrDefault() ?? "ethereum").ToLowerInvariant();
    address = address.Trim();

    if (!validChains.Contains(chain))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

    if (address.Length > 255 || (!IsValidEvmAddress(address) && !address.EndsWith(".eth")))
        return Results.BadRequest(new { error = "Invalid address format" });

    try
    {
        if (address.EndsWith(".eth"))
            address = await orchestrator.ResolveAddressAsync(address, chain);

        var profile = await orchestrator.BuildProfileAsync(address, chain, "standard");
        return Results.Ok(new
        {
            address,
            chain,
            liquidationRisk = profile.LiquidationRisk,
            defiPositions = profile.DeFiPositions,
            profiledAt = profile.ProfiledAt
        });
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
    }
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
        includes = new[] { "Everything in Basic", "ERC-20 tokens (up to 30)", "USD prices for all tokens", "Total portfolio value", "DeFi positions (12 protocols)", "Transaction activity history", "Portfolio quality score", "ACP trust score", "Token approval risk scan", "Contract interaction labels", "NFT holdings & floor prices", "Cross-chain support", "Token transfer history timeline", "Similar wallet clustering", "Revoke recommendation engine", "OFAC sanctions screening", "Smart money analysis", "Portfolio snapshots & history", "MEV exposure detection", "P&L tracking (FIFO)", "Uniswap V3 LP positions", "Liquidation risk monitoring", "Portfolio rebalancing suggestions", "Airdrop eligibility checker", "AI wallet analyst", "Real-time WebSocket streaming", "Live Blazor dashboard" }
    },
    premium = new
    {
        fee = "0.003 ETH",
        includes = new[] { "Everything in Standard", "ERC-20 tokens (up to 50)", "Natural language summary", "Priority caching" }
    }
}));

// --- GET /rebalance/{address} (v4.0) ---
app.MapGet("/rebalance/{address}", async (
    string address,
    ProfileOrchestrator orchestrator,
    RebalancingService rebalancing,
    SlaTrackingService sla,
    HttpContext httpContext) =>
{
    using var tracker = sla.Track("rebalance");
    var chain = (httpContext.Request.Query["chain"].FirstOrDefault() ?? "ethereum").ToLowerInvariant();
    var portfolio = httpContext.Request.Query["portfolio"].FirstOrDefault();
    address = address.Trim();

    if (!validChains.Contains(chain))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

    if (address.Length > 255 || (!IsValidEvmAddress(address) && !address.EndsWith(".eth")))
        return Results.BadRequest(new { error = "Invalid address format" });

    try
    {
        if (address.EndsWith(".eth"))
            address = await orchestrator.ResolveAddressAsync(address, chain);

        var profile = await orchestrator.BuildProfileAsync(address, chain, "standard");
        var suggestions = rebalancing.Analyze(profile, portfolio);

        return Results.Ok(new
        {
            address,
            chain,
            suggestions,
            totalValueUsd = profile.TotalValueUsd,
            profiledAt = profile.ProfiledAt
        });
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
    }
});

// --- GET /airdrops/{address} (v4.0) ---
app.MapGet("/airdrops/{address}", async (
    string address,
    ProfileOrchestrator orchestrator,
    AirdropEligibilityService airdropService,
    SlaTrackingService sla,
    HttpContext httpContext) =>
{
    using var tracker = sla.Track("airdrops");
    var chain = (httpContext.Request.Query["chain"].FirstOrDefault() ?? "ethereum").ToLowerInvariant();
    address = address.Trim();

    if (!validChains.Contains(chain))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

    if (address.Length > 255 || (!IsValidEvmAddress(address) && !address.EndsWith(".eth")))
        return Results.BadRequest(new { error = "Invalid address format" });

    try
    {
        if (address.EndsWith(".eth"))
            address = await orchestrator.ResolveAddressAsync(address, chain);

        var profile = await orchestrator.BuildProfileAsync(address, chain, "standard");
        var eligibility = airdropService.Check(profile);

        return Results.Ok(eligibility);
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = "Request failed. Check address and parameters." });
    }
});

// --- GET /ai-analyze/{address} (v4.0) ---
app.MapGet("/ai-analyze/{address}", async (
    string address,
    ProfileOrchestrator orchestrator,
    AiAnalystService aiService,
    SlaTrackingService sla,
    HttpContext httpContext) =>
{
    using var tracker = sla.Track("ai_analyze");
    var chain = (httpContext.Request.Query["chain"].FirstOrDefault() ?? "ethereum").ToLowerInvariant();
    var question = httpContext.Request.Query["question"].FirstOrDefault() ?? "Give me a comprehensive analysis of this wallet";
    address = address.Trim();

    if (!aiService.IsConfigured)
        return Results.BadRequest(new { error = "AI analysis is not configured. Set Anthropic:ApiKey in appsettings." });

    if (!validChains.Contains(chain))
        return Results.BadRequest(new { error = $"Unsupported chain. Valid: {string.Join(", ", ChainConfig.AllChains)}" });

    if (address.Length > 255 || (!IsValidEvmAddress(address) && !address.EndsWith(".eth")))
        return Results.BadRequest(new { error = "Invalid address format" });

    try
    {
        if (address.EndsWith(".eth"))
            address = await orchestrator.ResolveAddressAsync(address, chain);

        var profile = await orchestrator.BuildProfileAsync(address, chain, "standard");
        var analysis = await aiService.AnalyzeAsync(profile, question);

        return Results.Ok(analysis);
    }
    catch (Exception ex)
    {
        tracker.MarkFailed();
        return Results.BadRequest(new { error = "AI analysis failed. " + ex.Message });
    }
});

// --- GET /ws/status (v4.0: WebSocket streaming status) ---
app.MapGet("/ws/status", (WalletStreamingService streamingService) =>
{
    return Results.Ok(new
    {
        activeSubscriptions = streamingService.ActiveSubscriptions,
        trackedAddresses = streamingService.TrackedAddresses
    });
});

// --- SignalR hub for real-time wallet streaming ---
app.MapHub<WalletHub>("/ws/wallet");

// --- Blazor Server dashboard ---
app.MapRazorComponents<ProfilerApi.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();

// --- DTOs for gas analysis ---
internal class GasTxDto
{
    [JsonPropertyName("hash")] public string? Hash { get; set; }
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("gasUsed")] public string? GasUsed { get; set; }
    [JsonPropertyName("gasPrice")] public string? GasPrice { get; set; }
    [JsonPropertyName("timeStamp")] public string? TimeStamp { get; set; }
}
