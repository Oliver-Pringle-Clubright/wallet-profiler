using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProfilerApi.Services;

/// <summary>
/// Basic Solana JSON-RPC client for wallet status and trust checks.
/// Uses Alchemy Solana RPC if configured, falls back to public RPC.
/// </summary>
public class SolanaService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<SolanaService> _logger;

    public SolanaService(HttpClient httpClient, IConfiguration config, ILogger<SolanaService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public string GetRpcUrl()
    {
        var apiKey = _config["Alchemy:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey) && apiKey != "YOUR_ALCHEMY_API_KEY")
            return $"https://solana-mainnet.g.alchemy.com/v2/{apiKey}";
        return "https://api.mainnet-beta.solana.com";
    }

    public async Task<decimal> GetSolBalanceAsync(string address)
    {
        var result = await PostRpcAsync("getBalance", new object[] { address });
        var lamports = result?["result"]?["value"]?.GetValue<long>() ?? 0;
        return lamports / 1_000_000_000m;
    }

    public async Task<int> GetTransactionCountAsync(string address)
    {
        var result = await PostRpcAsync("getSignaturesForAddress",
            new object[] { address, new { limit = 1000 } });
        return result?["result"]?.AsArray()?.Count ?? 0;
    }

    public async Task<int> GetTokenAccountCountAsync(string address)
    {
        var result = await PostRpcAsync("getTokenAccountsByOwner",
            new object[]
            {
                address,
                new { programId = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA" },
                new { encoding = "jsonParsed" }
            });
        return result?["result"]?["value"]?.AsArray()?.Count ?? 0;
    }

    public async Task<bool> IsContractAsync(string address)
    {
        var result = await PostRpcAsync("getAccountInfo",
            new object[] { address, new { encoding = "jsonParsed" } });
        var data = result?["result"]?["value"]?["data"];
        if (data == null) return false;
        // Programs have executable = true
        var executable = result?["result"]?["value"]?["executable"]?.GetValue<bool>() ?? false;
        return executable;
    }

    /// <summary>
    /// Validates a Solana address format (base58, 32-44 chars).
    /// </summary>
    public static bool IsValidSolanaAddress(string address)
    {
        if (string.IsNullOrEmpty(address) || address.Length < 32 || address.Length > 44)
            return false;
        // Base58 alphabet: 123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz
        return address.All(c => "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz".Contains(c));
    }

    private async Task<JsonNode?> PostRpcAsync(string method, object[] parameters)
    {
        try
        {
            var rpcUrl = GetRpcUrl();
            var body = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method,
                @params = parameters
            });

            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(rpcUrl, content);
            var text = await response.Content.ReadAsStringAsync();
            return JsonNode.Parse(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Solana RPC call {Method} failed", method);
            return null;
        }
    }
}
