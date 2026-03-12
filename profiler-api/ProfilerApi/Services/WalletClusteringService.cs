using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Finds wallets with similar on-chain behavior by analyzing shared tokens,
/// shared contract interactions, and similar DeFi activity patterns.
/// Uses the wallet's top counterparties as candidates, scores by Jaccard
/// token similarity plus interaction frequency and protocol overlap.
/// </summary>
public class WalletClusteringService
{
    private readonly EthereumService _ethService;
    private readonly TokenService _tokenService;
    private readonly ContractLabelService _labelService;
    private readonly ILogger<WalletClusteringService> _logger;

    public WalletClusteringService(
        EthereumService ethService,
        TokenService tokenService,
        ContractLabelService labelService,
        ILogger<WalletClusteringService> logger)
    {
        _ethService = ethService;
        _tokenService = tokenService;
        _labelService = labelService;
        _logger = logger;
    }

    /// <summary>
    /// Analyzes top counterparties to find wallets with similar token holdings
    /// and protocol usage patterns.
    /// </summary>
    public async Task<SimilarWallets> FindSimilarAsync(
        string address,
        string chain,
        string tier,
        List<TokenBalance> walletTokens,
        List<ContractInteraction> walletInteractions,
        List<(string Address, int Count)>? topCounterparties)
    {
        if (topCounterparties == null || topCounterparties.Count == 0)
            return new SimilarWallets();

        var addressLower = address.ToLowerInvariant();

        // Filter candidates: skip known contracts, take top 15 personal wallets
        var candidates = topCounterparties
            .Where(cp => !_labelService.IsKnownContract(cp.Address))
            .Where(cp => cp.Address.ToLowerInvariant() != addressLower)
            .Take(15)
            .ToList();

        if (candidates.Count == 0)
            return new SimilarWallets();

        var rpcUrl = _ethService.GetRpcUrl(chain);
        var walletTokenSet = walletTokens
            .Where(t => !t.IsSpam && t.Symbol != "UNKNOWN")
            .Select(t => t.ContractAddress.ToLowerInvariant())
            .ToHashSet();

        var walletTokenSymbols = walletTokens
            .Where(t => !t.IsSpam && t.Symbol != "UNKNOWN")
            .ToDictionary(t => t.ContractAddress.ToLowerInvariant(), t => t.Symbol);

        // Build protocol set from wallet interactions
        var walletProtocols = walletInteractions
            .Where(i => i.Category != null && i.Category != "token" && i.Category != "system")
            .Select(i => i.Address.ToLowerInvariant())
            .ToHashSet();

        var walletProtocolLabels = walletInteractions
            .Where(i => i.Label != null)
            .ToDictionary(i => i.Address.ToLowerInvariant(), i => i.Label!);

        // Fetch token lists for candidates in parallel (max 5 concurrent)
        var semaphore = new SemaphoreSlim(5);
        var tasks = candidates.Select(async candidate =>
        {
            await semaphore.WaitAsync();
            try
            {
                var candidateTokens = await _tokenService.GetERC20BalancesAsync(rpcUrl, candidate.Address, chain, "basic");
                var candidateTokenSet = candidateTokens
                    .Where(t => !t.IsSpam && t.Symbol != "UNKNOWN")
                    .Select(t => t.ContractAddress.ToLowerInvariant())
                    .ToHashSet();

                // Token similarity (Jaccard)
                var commonTokens = walletTokenSet.Intersect(candidateTokenSet).ToList();
                var tokenUnion = walletTokenSet.Union(candidateTokenSet).Count();
                var jaccardSimilarity = tokenUnion > 0 ? (double)commonTokens.Count / tokenUnion : 0;

                // Interaction frequency bonus (how often they transact with our wallet)
                var interactionBonus = candidate.Count switch
                {
                    > 100 => 25,
                    > 50 => 20,
                    > 20 => 15,
                    > 10 => 10,
                    > 5 => 5,
                    _ => 0
                };

                // Shared protocol bonus: check if candidate holds tokens associated with same protocols
                var sharedProtocolNames = new List<string>();
                foreach (var proto in walletProtocols)
                {
                    // Check if candidate also holds the protocol's token or interacted with it
                    if (candidateTokenSet.Contains(proto))
                    {
                        if (walletProtocolLabels.TryGetValue(proto, out var label))
                            sharedProtocolNames.Add(label);
                    }
                }

                // Also detect shared behavior via token overlap with known DeFi tokens
                var sharedDefiTokens = commonTokens
                    .Where(ct => walletTokenSymbols.TryGetValue(ct, out var sym) &&
                        IsDefiToken(sym))
                    .Select(ct => walletTokenSymbols[ct])
                    .ToList();

                var protocolBonus = (sharedProtocolNames.Count + sharedDefiTokens.Count) * 5;

                var similarityScore = (int)Math.Min(100,
                    jaccardSimilarity * 60 + interactionBonus + Math.Min(protocolBonus, 15));

                // Build common interaction labels
                var interactionLabels = sharedProtocolNames
                    .Concat(sharedDefiTokens)
                    .Distinct()
                    .Take(5)
                    .ToList();

                return new SimilarWallet
                {
                    Address = candidate.Address,
                    SimilarityScore = similarityScore,
                    CommonTokens = commonTokens
                        .Where(ct => walletTokenSymbols.ContainsKey(ct))
                        .Select(ct => walletTokenSymbols[ct])
                        .Take(8)
                        .ToList(),
                    CommonInteractions = interactionLabels,
                    SharedProtocols = sharedProtocolNames.Count + sharedDefiTokens.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to analyze candidate {Address}", candidate.Address);
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = (await Task.WhenAll(tasks))
            .Where(r => r != null && r.SimilarityScore > 5)
            .OrderByDescending(r => r!.SimilarityScore)
            .Take(5)
            .ToList();

        return new SimilarWallets
        {
            CandidatesAnalyzed = candidates.Count,
            Matches = results!
        };
    }

    private static bool IsDefiToken(string symbol)
    {
        return symbol.ToUpperInvariant() switch
        {
            "AAVE" or "COMP" or "CRV" or "CVX" or "LDO" or "RPL" or "MKR"
            or "UNI" or "SUSHI" or "BAL" or "SNX" or "YFI" or "PENDLE"
            or "ENA" or "EIGEN" or "STETH" or "WSTETH" or "RETH" or "CBETH"
            or "SDAI" or "SUSDE" or "FRXETH" or "SFRXETH" or "WEETH" => true,
            _ => false
        };
    }
}
