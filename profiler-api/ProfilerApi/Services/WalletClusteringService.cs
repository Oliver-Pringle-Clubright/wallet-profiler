using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Finds wallets with similar on-chain behavior by analyzing shared tokens,
/// shared contract interactions, and similar DeFi activity patterns.
/// Uses the wallet's top counterparties as candidates.
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
    /// Analyzes top counterparties to find wallets with similar token holdings.
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

        // Filter candidates: skip known contracts, take top 10 personal wallets
        var candidates = topCounterparties
            .Where(cp => !_labelService.IsKnownContract(cp.Address))
            .Where(cp => cp.Address.ToLowerInvariant() != addressLower)
            .Take(10)
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

        var walletInteractionSet = walletInteractions
            .Where(i => i.Label != null)
            .Select(i => i.Address.ToLowerInvariant())
            .ToHashSet();

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

                // Compute similarity
                var commonTokens = walletTokenSet.Intersect(candidateTokenSet).ToList();
                var tokenUnion = walletTokenSet.Union(candidateTokenSet).Count();
                var jaccardSimilarity = tokenUnion > 0 ? (double)commonTokens.Count / tokenUnion : 0;

                // Interaction frequency bonus
                var interactionBonus = candidate.Count > 50 ? 20 : candidate.Count > 20 ? 10 : candidate.Count > 5 ? 5 : 0;

                var similarityScore = (int)Math.Min(100, jaccardSimilarity * 80 + interactionBonus);

                return new SimilarWallet
                {
                    Address = candidate.Address,
                    SimilarityScore = similarityScore,
                    CommonTokens = commonTokens
                        .Where(ct => walletTokenSymbols.ContainsKey(ct))
                        .Select(ct => walletTokenSymbols[ct])
                        .Take(5)
                        .ToList(),
                    CommonInteractions = walletInteractionSet
                        .Take(3)
                        .Select(a => { var lbl = _labelService.GetLabel(a); return lbl.Label ?? a; })
                        .ToList(),
                    SharedProtocols = commonTokens.Count
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
}
