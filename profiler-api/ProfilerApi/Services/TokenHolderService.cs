using System.Text.Json;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Analyzes token holders by aggregating recent transfer events from Etherscan V2.
/// Profiles top holders with trust scores, tags, and classification.
/// </summary>
public class TokenHolderService
{
    private readonly HttpClient _httpClient;
    private readonly EthereumService _ethService;
    private readonly TokenService _tokenService;
    private readonly ContractLabelService _labelService;
    private readonly IConfiguration _config;
    private readonly ILogger<TokenHolderService> _logger;

    public TokenHolderService(
        HttpClient httpClient,
        EthereumService ethService,
        TokenService tokenService,
        ContractLabelService labelService,
        IConfiguration config,
        ILogger<TokenHolderService> logger)
    {
        _httpClient = httpClient;
        _ethService = ethService;
        _tokenService = tokenService;
        _labelService = labelService;
        _config = config;
        _logger = logger;
    }

    public async Task<TokenHolderAnalysis> AnalyzeHoldersAsync(string contractAddress, string chain, string tier, int limit = 20)
    {
        var rpcUrl = _ethService.GetRpcUrl(chain);
        limit = Math.Clamp(limit, 1, 50);

        try
        {
            // Get token metadata first
            var metadataRequest = new
            {
                jsonrpc = "2.0",
                method = "alchemy_getTokenMetadata",
                @params = new[] { contractAddress },
                id = 1
            };
            var metaResponse = await _httpClient.PostAsJsonAsync(rpcUrl, metadataRequest);
            var metaJson = await metaResponse.Content.ReadAsStringAsync();
            var metaDoc = JsonDocument.Parse(metaJson);
            string? tokenSymbol = null;
            int tokenDecimals = 18;
            if (metaDoc.RootElement.TryGetProperty("result", out var metaResult))
            {
                tokenSymbol = metaResult.TryGetProperty("symbol", out var s) ? s.GetString() : null;
                tokenDecimals = metaResult.TryGetProperty("decimals", out var d) ? d.GetInt32() : 18;
            }

            // Get top holders from recent transfer events
            var owners = await GetTopHoldersFromTransfersAsync(rpcUrl, contractAddress, chain, limit);

            if (owners.Count == 0)
                return new TokenHolderAnalysis
                {
                    TokenAddress = contractAddress,
                    TokenSymbol = tokenSymbol
                };

            // Profile each holder with quick trust check
            var web3 = _ethService.GetWeb3(chain);
            var semaphore = new SemaphoreSlim(5);
            var holderTasks = owners.Take(limit).Select(async owner =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var balanceTask = _ethService.GetEthBalanceAsync(web3, owner.Address);
                    var txCountTask = _ethService.GetTransactionCountAsync(web3, owner.Address);
                    var ensTask = _ethService.ResolveEnsAsync(web3, owner.Address);
                    await Task.WhenAll(balanceTask, txCountTask, ensTask);

                    var score = 0;
                    var tags = new List<string>();
                    var txCount = txCountTask.Result;
                    var ethBal = balanceTask.Result;

                    // Score components
                    if (txCount > 500) { score += 30; tags.Add("power-user"); }
                    else if (txCount > 100) { score += 20; tags.Add("active-trader"); }
                    else if (txCount > 20) score += 10;

                    if (ethBal > 100) { score += 25; tags.Add("whale"); }
                    else if (ethBal > 10) { score += 20; tags.Add("well-funded"); }
                    else if (ethBal > 1) score += 15;
                    else if (ethBal > 0.1m) score += 5;

                    if (ensTask.Result != null) { score += 15; tags.Add("ens-holder"); }

                    // Classify holder based on balance percentage
                    if (owner.BalancePct.HasValue)
                    {
                        if (owner.BalancePct > 10)
                            tags.Add("major-holder");
                        else if (owner.BalancePct > 5)
                            tags.Add("significant-holder");
                    }

                    // Check if holder is a known contract (exchange, protocol, etc.)
                    var (label, category) = _labelService.GetLabel(owner.Address);
                    if (label != null)
                    {
                        tags.Add($"known:{category}");
                        score += 10; // Known entities get a trust bonus
                    }

                    // Classify holder type
                    if (category == "exchange") tags.Add("exchange");
                    else if (txCount < 5 && ethBal < 0.01m) tags.Add("dust-holder");

                    score = Math.Clamp(score, 0, 100);

                    return new HolderProfile
                    {
                        Address = owner.Address,
                        EnsName = ensTask.Result ?? label,
                        Balance = owner.Balance,
                        BalancePct = owner.BalancePct,
                        TrustScore = score,
                        TrustLevel = score >= 60 ? "high" : score >= 30 ? "moderate" : "low",
                        Tags = tags
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to profile holder {Address}", owner.Address);
                    return new HolderProfile
                    {
                        Address = owner.Address,
                        Balance = owner.Balance,
                        BalancePct = owner.BalancePct
                    };
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var holders = (await Task.WhenAll(holderTasks)).ToList();

            // Calculate concentration
            var topHolderConcentration = holders.Where(h => h.BalancePct.HasValue)
                .Take(10)
                .Sum(h => h.BalancePct!.Value);

            // Count whales and exchanges
            var whaleCount = holders.Count(h => h.Tags.Contains("whale") || h.Tags.Contains("major-holder"));
            var exchangeCount = holders.Count(h => h.Tags.Contains("exchange") || h.Tags.Contains("known:exchange"));

            return new TokenHolderAnalysis
            {
                TokenAddress = contractAddress,
                TokenSymbol = tokenSymbol,
                HoldersAnalyzed = holders.Count,
                TopHolderConcentration = topHolderConcentration > 0 ? topHolderConcentration : null,
                Holders = holders
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze holders for {Contract}", contractAddress);
            return new TokenHolderAnalysis { TokenAddress = contractAddress };
        }
    }

    private async Task<List<(string Address, decimal Balance, decimal? BalancePct)>> GetTopHoldersFromTransfersAsync(
        string rpcUrl, string contractAddress, string chain, int limit)
    {
        if (!ChainConfig.Chains.TryGetValue(chain, out var chainConfig))
            return [];

        var apiKey = _config["Etherscan:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return [];

        try
        {
            // Get recent token transfers to identify active holders
            var url = $"{ChainConfig.EtherscanV2BaseUrl}?chainid={chainConfig.ChainId}&module=account&action=tokentx&contractaddress={contractAddress}&page=1&offset=500&sort=desc&apikey={apiKey}";
            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
                return [];

            // Aggregate balances from transfers (approximation)
            var balances = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var tx in result.EnumerateArray())
            {
                var to = tx.TryGetProperty("to", out var t) ? t.GetString() : null;
                var from = tx.TryGetProperty("from", out var f) ? f.GetString() : null;
                var valueStr = tx.TryGetProperty("value", out var v) ? v.GetString() ?? "0" : "0";
                var decimalsStr = tx.TryGetProperty("tokenDecimal", out var d) ? d.GetString() ?? "18" : "18";

                if (!int.TryParse(decimalsStr, out var decimals)) decimals = 18;
                if (!decimal.TryParse(valueStr, out var rawValue) || rawValue == 0) continue;

                var amount = rawValue / (decimal)Math.Pow(10, Math.Min(decimals, 18));

                if (!string.IsNullOrEmpty(to))
                    balances[to] = balances.GetValueOrDefault(to) + amount;
                if (!string.IsNullOrEmpty(from))
                    balances[from] = balances.GetValueOrDefault(from) - amount;
            }

            var totalPositive = balances.Values.Where(v => v > 0).Sum();

            return balances
                .Where(kv => kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .Take(limit)
                .Select(kv => (
                    Address: kv.Key,
                    Balance: kv.Value,
                    BalancePct: totalPositive > 0 ? (decimal?)(kv.Value / totalPositive * 100) : null
                ))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get holders from transfers for {Contract}", contractAddress);
            return [];
        }
    }
}
