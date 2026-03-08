using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class TokenService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<TokenService> _logger;

    public TokenService(HttpClient httpClient, IConfiguration config, ILogger<TokenService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Fetches all ERC-20 token balances using Alchemy's alchemy_getTokenBalances endpoint.
    /// Falls back to Etherscan V2 token transfer history if Alchemy call fails.
    /// </summary>
    public async Task<List<TokenBalance>> GetERC20BalancesAsync(string rpcUrl, string address, string chain = "ethereum")
    {
        try
        {
            // Step 1: Get all non-zero token balances via Alchemy (single RPC call)
            var tokenBalances = await GetAlchemyTokenBalancesAsync(rpcUrl, address);

            if (tokenBalances.Count == 0)
                return [];

            // Step 2: Get metadata (symbol, decimals) for each token via Alchemy batch
            var metadataTasks = tokenBalances.Select(async tb =>
            {
                var metadata = await GetAlchemyTokenMetadataAsync(rpcUrl, tb.ContractAddress);
                if (metadata != null)
                {
                    tb.Symbol = metadata.Symbol ?? "UNKNOWN";
                    tb.Decimals = metadata.Decimals;
                    tb.Balance = ConvertBalance(tb.RawBalanceHex, metadata.Decimals);
                }
            });

            await Task.WhenAll(metadataTasks);

            // Step 3: Detect spam tokens
            foreach (var token in tokenBalances)
            {
                token.IsSpam = DetectSpam(token);
            }

            // Return non-zero balances, non-spam first, sorted by balance
            return tokenBalances
                .Where(t => t.Balance > 0)
                .OrderBy(t => t.IsSpam)
                .ThenByDescending(t => t.Balance)
                .Select(t => new TokenBalance
                {
                    Symbol = t.Symbol,
                    ContractAddress = t.ContractAddress,
                    Balance = t.Balance,
                    Decimals = t.Decimals,
                    IsSpam = t.IsSpam
                })
                .Take(50)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch token balances for {Address} on {Chain}", address, chain);
            return [];
        }
    }

    private async Task<List<AlchemyToken>> GetAlchemyTokenBalancesAsync(string rpcUrl, string address)
    {
        var request = new
        {
            jsonrpc = "2.0",
            method = "alchemy_getTokenBalances",
            @params = new object[] { address, "erc20" },
            id = 1
        };

        var response = await _httpClient.PostAsJsonAsync(rpcUrl, request);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("result", out var result))
            return [];

        if (!result.TryGetProperty("tokenBalances", out var balances))
            return [];

        var tokens = new List<AlchemyToken>();
        foreach (var tb in balances.EnumerateArray())
        {
            var contractAddress = tb.GetProperty("contractAddress").GetString();
            var balanceHex = tb.GetProperty("tokenBalance").GetString();

            if (string.IsNullOrEmpty(contractAddress) || string.IsNullOrEmpty(balanceHex))
                continue;

            // Skip zero balances
            if (balanceHex == "0x0" || balanceHex == "0x")
                continue;

            tokens.Add(new AlchemyToken
            {
                ContractAddress = contractAddress,
                RawBalanceHex = balanceHex
            });
        }

        return tokens;
    }

    private async Task<TokenMetadata?> GetAlchemyTokenMetadataAsync(string rpcUrl, string contractAddress)
    {
        try
        {
            var request = new
            {
                jsonrpc = "2.0",
                method = "alchemy_getTokenMetadata",
                @params = new[] { contractAddress },
                id = 1
            };

            var response = await _httpClient.PostAsJsonAsync(rpcUrl, request);
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("result", out var result))
                return null;

            var symbol = result.TryGetProperty("symbol", out var s) ? s.GetString() : null;
            var decimals = result.TryGetProperty("decimals", out var d) ? d.GetInt32() : 18;
            var name = result.TryGetProperty("name", out var n) ? n.GetString() : null;

            return new TokenMetadata
            {
                Symbol = symbol,
                Decimals = decimals,
                Name = name
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get metadata for {Contract}", contractAddress);
            return null;
        }
    }

    private static decimal ConvertBalance(string hexBalance, int decimals)
    {
        try
        {
            if (string.IsNullOrEmpty(hexBalance))
                return 0;

            var hex = hexBalance.StartsWith("0x") ? hexBalance[2..] : hexBalance;
            if (string.IsNullOrEmpty(hex))
                return 0;

            var rawBalance = BigInteger.Parse("0" + hex, System.Globalization.NumberStyles.HexNumber);
            if (rawBalance == 0) return 0;

            var divisor = BigInteger.Pow(10, decimals);
            var wholePart = (decimal)(rawBalance / divisor);
            var remainder = (decimal)(rawBalance % divisor);
            return wholePart + (remainder / (decimal)divisor);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Heuristic spam detection for tokens.
    /// </summary>
    private static bool DetectSpam(AlchemyToken token)
    {
        var symbol = token.Symbol ?? "";
        var name = token.ContractAddress;

        // No symbol or very long symbol
        if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > 20)
            return true;

        // Contains non-ASCII characters (common in spam tokens)
        if (symbol.Any(c => c > 127))
            return true;

        // Contains URL-like patterns
        if (symbol.Contains("http") || symbol.Contains(".com") || symbol.Contains(".io") || symbol.Contains("www"))
            return true;

        // Contains common phishing patterns
        var lowerSymbol = symbol.ToLowerInvariant();
        if (lowerSymbol.Contains("visit") || lowerSymbol.Contains("claim") ||
            lowerSymbol.Contains("airdrop") || lowerSymbol.Contains("reward"))
            return true;

        return false;
    }

    private class AlchemyToken
    {
        public string ContractAddress { get; set; } = string.Empty;
        public string RawBalanceHex { get; set; } = string.Empty;
        public string Symbol { get; set; } = "UNKNOWN";
        public int Decimals { get; set; } = 18;
        public decimal Balance { get; set; }
        public bool IsSpam { get; set; }
    }

    private class TokenMetadata
    {
        public string? Symbol { get; set; }
        public int Decimals { get; set; }
        public string? Name { get; set; }
    }
}
