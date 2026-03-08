using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Web3;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class ApprovalScannerService
{
    private readonly ILogger<ApprovalScannerService> _logger;

    // Known spender contracts to check for active approvals
    private static readonly Dictionary<string, (string Label, string Category)> KnownSpenders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0x7a250d5630B4cF539739dF2C5dAcb4c659F2488D"] = ("Uniswap V2 Router", "dex"),
        ["0xE592427A0AEce92De3Edee1F18E0157C05861564"] = ("Uniswap V3 Router", "dex"),
        ["0x3fC91A3afd70395Cd496C647d5a6CC9D4B2b7FAD"] = ("Uniswap Universal Router", "dex"),
        ["0xd9e1cE17f2641f24aE83637ab66a2cca9C378B9F"] = ("SushiSwap Router", "dex"),
        ["0x1111111254EEB25477B68fb85Ed929f73A960582"] = ("1inch V5 Router", "dex"),
        ["0xDef1C0ded9bec7F1a1670819833240f027b25EfF"] = ("0x Exchange Proxy", "dex"),
        ["0x00000000000000ADc04C56Bf30aC9d3c0aAF14dC"] = ("OpenSea Seaport", "nft"),
    };

    // Anything above 10^30 is effectively unlimited (max uint256 = ~1.15 × 10^77)
    private static readonly BigInteger UnlimitedThreshold = BigInteger.Pow(10, 30);

    public ApprovalScannerService(ILogger<ApprovalScannerService> logger)
    {
        _logger = logger;
    }

    public async Task<ApprovalRisk> ScanAsync(Web3 web3, string ownerAddress, List<TokenBalance> tokens)
    {
        var approvals = new List<TokenApproval>();

        // Only check top 10 non-spam tokens with known symbols
        var tokensToCheck = tokens
            .Where(t => !t.IsSpam && t.Symbol != "UNKNOWN")
            .Take(10)
            .ToList();

        if (tokensToCheck.Count == 0)
            return new ApprovalRisk();

        var semaphore = new SemaphoreSlim(10);
        var tasks = new List<Task>();

        foreach (var token in tokensToCheck)
        {
            foreach (var (spenderAddress, (label, category)) in KnownSpenders)
            {
                tasks.Add(CheckAllowanceAsync(web3, token, ownerAddress, spenderAddress, label, category, approvals, semaphore));
            }
        }

        await Task.WhenAll(tasks);

        var unlimitedCount = approvals.Count(a => a.IsUnlimited);
        var highRiskCount = approvals.Count(a => a.RiskLevel == "danger");

        var overallRisk = (highRiskCount, unlimitedCount) switch
        {
            ( > 0, _) => "danger",
            (_, > 3) => "caution",
            (_, > 0) => "low",
            _ => "safe"
        };

        return new ApprovalRisk
        {
            TotalApprovals = approvals.Count,
            UnlimitedApprovals = unlimitedCount,
            HighRiskApprovals = highRiskCount,
            RiskLevel = overallRisk,
            Approvals = approvals
                .OrderByDescending(a => a.IsUnlimited)
                .ThenBy(a => a.TokenSymbol)
                .ToList()
        };
    }

    private async Task CheckAllowanceAsync(
        Web3 web3,
        TokenBalance token,
        string owner,
        string spender,
        string spenderLabel,
        string spenderCategory,
        List<TokenApproval> results,
        SemaphoreSlim semaphore)
    {
        await semaphore.WaitAsync();
        try
        {
            var handler = web3.Eth.GetContractQueryHandler<AllowanceFunction>();
            var allowance = await handler.QueryAsync<BigInteger>(
                token.ContractAddress,
                new AllowanceFunction { Owner = owner, Spender = spender }
            );

            if (allowance > 0)
            {
                var isUnlimited = allowance >= UnlimitedThreshold;
                lock (results)
                {
                    results.Add(new TokenApproval
                    {
                        TokenSymbol = token.Symbol,
                        TokenAddress = token.ContractAddress,
                        SpenderAddress = spender,
                        SpenderLabel = spenderLabel,
                        SpenderCategory = spenderCategory,
                        IsUnlimited = isUnlimited,
                        RiskLevel = isUnlimited ? "caution" : "safe"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Allowance check failed for {Token} -> {Spender}", token.Symbol, spenderLabel);
        }
        finally
        {
            semaphore.Release();
        }
    }

    [Function("allowance", "uint256")]
    private class AllowanceFunction : FunctionMessage
    {
        [Parameter("address", "owner", 1)]
        public string Owner { get; set; } = string.Empty;

        [Parameter("address", "spender", 2)]
        public string Spender { get; set; } = string.Empty;
    }
}
