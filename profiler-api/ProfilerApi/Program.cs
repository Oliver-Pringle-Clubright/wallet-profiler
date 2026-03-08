using ProfilerApi.Models;
using ProfilerApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ProfileCacheService>();
builder.Services.AddSingleton<EthereumService>();
builder.Services.AddSingleton<RiskScoringService>();
builder.Services.AddSingleton<SummaryService>();
builder.Services.AddSingleton<DeFiService>();
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
    var tokensTask = tokenService.GetERC20BalancesAsync(rpcUrl, address, chain);

    var basicTasks = new List<Task> { balanceTask, txCountTask, ensTask, tokensTask };

    // --- STANDARD tier adds: DeFi + activity + USD prices ---
    Task<List<DeFiPosition>>? defiTask = null;
    Task<WalletActivity>? activityTask = null;

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
    var activity = activityTask?.Result;

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

    // --- PREMIUM tier adds: natural language summary ---
    if (tier == "premium")
    {
        profile.Summary = summaryService.Generate(profile);
    }

    // Cache the result
    cacheService.SetProfile(address, chain, tier, profile);

    return Results.Ok(profile);
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/tiers", () => Results.Ok(new
{
    basic = new
    {
        fee = "0.0005 ETH",
        includes = new[] { "ETH balance", "ERC-20 tokens with balances", "ENS resolution", "Risk score", "Spam detection" }
    },
    standard = new
    {
        fee = "0.001 ETH",
        includes = new[] { "Everything in Basic", "USD prices for all tokens", "Total portfolio value", "DeFi positions (Aave, Compound)", "Transaction activity history" }
    },
    premium = new
    {
        fee = "0.003 ETH",
        includes = new[] { "Everything in Standard", "Natural language summary", "Priority caching" }
    }
}));

app.Run();
