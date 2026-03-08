using System.Text.Json;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Analyzes token holders using Alchemy's getOwnersForToken API.
/// Profiles top holders with trust scores and tags.
/// </summary>
public class TokenHolderService
{
    private readonly HttpClient _httpClient;
    private readonly EthereumService _ethService;
    private readonly TokenService _tokenService;
    private readonly IConfiguration _config;
    private readonly ILogger<TokenHolderService> _logger;

    public TokenHolderService(
        HttpClient httpClient,
        EthereumService ethService,
        TokenService tokenService,
        IConfiguration config,
        ILogger<TokenHolderService> logger)
    {
        _httpClient = httpClient;
        _ethService = ethService;
        _tokenService = tokenService;
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

            // Get top holders using Alchemy getOwnersForToken
            var holdersRequest = new
            {
                jsonrpc = "2.0",
                method = "alchemy_getTokenBalances",
                @params = new object[] { contractAddress, "erc20" },
                id = 2
            };

            // Actually use getOwnersForToken endpoint (Alchemy NFT-style API)
            var nftBaseUrl = rpcUrl.Replace("/v2/", "/nft/v3/");
            var ownersUrl = $"{nftBaseUrl}/getOwnersForContract?contractAddress={contractAddress}&withTokenBalances=true&limit={limit}";

            // Fallback: since getOwnersForContract is NFT-focused, use a simpler approach
            // Get the top holders by analyzing recent transfer events
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

                    if (txCount > 100) { score += 30; tags.Add("active-trader"); }
                    else if (txCount > 20) score += 15;
                    if (ethBal > 10) { score += 25; tags.Add("well-funded"); }
                    else if (ethBal > 1) score += 15;
                    if (ensTask.Result != null) { score += 20; tags.Add("ens-holder"); }

                    score = Math.Clamp(score, 0, 100);

                    return new HolderProfile
                    {
                        Address = owner.Address,
                        EnsName = ensTask.Result,
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
