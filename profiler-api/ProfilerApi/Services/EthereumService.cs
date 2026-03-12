using Nethereum.Web3;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class EthereumService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EthereumService> _logger;
    private readonly ProfileCacheService _cache;

    public EthereumService(IConfiguration config, ILogger<EthereumService> logger, ProfileCacheService cache)
    {
        _config = config;
        _logger = logger;
        _cache = cache;
    }

    public Web3 GetWeb3(string chain = "ethereum")
    {
        var rpcUrl = GetRpcUrl(chain);
        return new Web3(rpcUrl);
    }

    public string GetRpcUrl(string chain = "ethereum")
    {
        var apiKey = _config["Alchemy:ApiKey"] ?? throw new InvalidOperationException("Alchemy:ApiKey not configured");
        if (!ChainConfig.Chains.TryGetValue(chain, out var chainConfig))
            throw new ArgumentException($"Unsupported chain: {chain}");

        return chainConfig.RpcUrl.Replace("{API_KEY}", apiKey);
    }

    public string? GetEtherscanApiKey() => _config["Etherscan:ApiKey"];

    public async Task<decimal> GetEthBalanceAsync(Web3 web3, string address)
    {
        var balance = await web3.Eth.GetBalance.SendRequestAsync(address);
        return Web3.Convert.FromWei(balance);
    }

    public async Task<int> GetTransactionCountAsync(Web3 web3, string address)
    {
        var count = await web3.Eth.Transactions.GetTransactionCount.SendRequestAsync(address);
        return (int)count.Value;
    }

    public async Task<string?> ResolveEnsAsync(Web3 web3, string address)
    {
        // Check cache first
        if (_cache.TryGetEns(address, out var cached))
            return cached;

        try
        {
            var ensService = web3.Eth.GetEnsService();
            var name = await ensService.ReverseResolveAsync(address);
            var result = string.IsNullOrEmpty(name) ? null : name;
            _cache.SetEns(address, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ENS reverse resolution failed for {Address}", address);
            _cache.SetEns(address, null);
            return null;
        }
    }

    public async Task<string> ResolveEnsToAddressAsync(Web3 web3, string ensName)
    {
        // Check cache first
        if (_cache.TryGetEnsReverse(ensName, out var cached) && cached != null)
            return cached;

        var ensService = web3.Eth.GetEnsService();
        var address = await ensService.ResolveAddressAsync(ensName);
        if (address == null)
            throw new ArgumentException($"Could not resolve ENS name: {ensName}");

        _cache.SetEnsReverse(ensName, address);
        return address;
    }
}
