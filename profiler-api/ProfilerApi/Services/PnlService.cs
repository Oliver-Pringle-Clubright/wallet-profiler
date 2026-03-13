using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Calculates realized and unrealized P&L for a wallet using FIFO cost basis
/// from transfer history and current token holdings with live prices.
/// </summary>
public class PnlService
{
    private readonly ILogger<PnlService> _logger;

    public PnlService(ILogger<PnlService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Calculates P&L from transfer history (buys/sells) and current holdings.
    /// </summary>
    public PnlSummary Calculate(
        TransferHistory? transfers,
        List<TokenBalance> currentHoldings,
        Dictionary<string, decimal>? tokenPrices)
    {
        var summary = new PnlSummary();

        if (transfers == null || transfers.TotalTransfers == 0)
            return summary;

        // Group transfers by token
        var tokenTransfers = transfers.RecentTransfers
            .Concat(GetTimelineImpliedTransfers(transfers))
            .Where(t => t.Amount > 0 && t.TokenAddress != "native")
            .GroupBy(t => t.TokenAddress.ToLowerInvariant())
            .ToList();

        // Also include native token
        var nativeTransfers = transfers.RecentTransfers
            .Where(t => t.TokenAddress == "native" && t.Amount > 0)
            .ToList();

        if (nativeTransfers.Count > 0)
        {
            tokenTransfers.Add(nativeTransfers
                .GroupBy(t => "native")
                .First());
        }

        var allTokenPnl = new List<TokenPnl>();
        decimal totalRealized = 0;
        decimal totalUnrealized = 0;
        decimal totalCostBasis = 0;

        foreach (var group in tokenTransfers)
        {
            var tokenAddr = group.Key;
            var symbol = group.First().TokenSymbol;

            // Sort by time ascending for FIFO
            var sorted = group.OrderBy(t => t.Timestamp).ToList();

            // FIFO cost basis tracking
            var buyLots = new Queue<(decimal amount, decimal priceUsd)>();
            decimal realizedPnl = 0;
            decimal totalBought = 0;
            decimal totalSold = 0;
            decimal costBasis = 0;

            foreach (var tx in sorted)
            {
                if (tx.Direction == "in") // Buy
                {
                    totalBought += tx.Amount;
                    if (tx.ValueUsd.HasValue && tx.Amount > 0)
                    {
                        var price = tx.ValueUsd.Value / tx.Amount;
                        buyLots.Enqueue((tx.Amount, price));
                        costBasis += tx.ValueUsd.Value;
                    }
                    else
                    {
                        // No price data — estimate as zero cost (airdrop/gift)
                        buyLots.Enqueue((tx.Amount, 0));
                    }
                }
                else // Sell
                {
                    totalSold += tx.Amount;
                    var remaining = tx.Amount;
                    var salePrice = (tx.ValueUsd.HasValue && tx.Amount > 0)
                        ? tx.ValueUsd.Value / tx.Amount
                        : 0m;

                    // FIFO: consume oldest buy lots first
                    while (remaining > 0 && buyLots.Count > 0)
                    {
                        var lot = buyLots.Dequeue();
                        var consumed = Math.Min(remaining, lot.amount);
                        realizedPnl += consumed * (salePrice - lot.priceUsd);
                        costBasis -= consumed * lot.priceUsd;
                        remaining -= consumed;

                        if (lot.amount > consumed)
                        {
                            // Partial lot consumed — put remainder back
                            buyLots.Enqueue((lot.amount - consumed, lot.priceUsd));
                            break; // Queue was modified, stop iteration
                        }
                    }
                }
            }

            // Calculate unrealized P&L from remaining lots
            decimal? unrealizedPnl = null;
            decimal? currentHolding = null;
            decimal? currentValue = null;

            // Find current holding
            var holding = tokenAddr == "native"
                ? null // Native balance handled separately
                : currentHoldings.FirstOrDefault(h =>
                    h.ContractAddress.Equals(tokenAddr, StringComparison.OrdinalIgnoreCase));

            if (holding != null && holding.PriceUsd.HasValue)
            {
                currentHolding = holding.Balance;
                currentValue = holding.Balance * holding.PriceUsd.Value;

                // Unrealized = current value of remaining lots - their cost basis
                var remainingCost = 0m;
                foreach (var lot in buyLots)
                    remainingCost += lot.amount * lot.priceUsd;

                unrealizedPnl = (currentValue.Value) - remainingCost;
            }

            var pnlPct = costBasis > 0
                ? Math.Round(((realizedPnl + (unrealizedPnl ?? 0)) / costBasis) * 100, 2)
                : (decimal?)null;

            allTokenPnl.Add(new TokenPnl
            {
                TokenSymbol = symbol,
                TokenAddress = tokenAddr,
                TotalBought = totalBought,
                TotalSold = totalSold,
                CostBasisUsd = costBasis > 0 ? Math.Round(costBasis, 2) : null,
                RealizedPnlUsd = realizedPnl != 0 ? Math.Round(realizedPnl, 2) : null,
                UnrealizedPnlUsd = unrealizedPnl.HasValue ? Math.Round(unrealizedPnl.Value, 2) : null,
                CurrentHolding = currentHolding,
                CurrentValueUsd = currentValue.HasValue ? Math.Round(currentValue.Value, 2) : null,
                PnlPct = pnlPct
            });

            totalRealized += realizedPnl;
            if (unrealizedPnl.HasValue)
                totalUnrealized += unrealizedPnl.Value;
            totalCostBasis += costBasis;
        }

        // Sort and build summary
        var withPnl = allTokenPnl
            .Where(t => t.RealizedPnlUsd.HasValue || t.UnrealizedPnlUsd.HasValue)
            .ToList();

        summary.TokensAnalyzed = allTokenPnl.Count;
        summary.ProfitableTokens = withPnl.Count(t => (t.RealizedPnlUsd ?? 0) + (t.UnrealizedPnlUsd ?? 0) > 0);
        summary.LosingTokens = withPnl.Count(t => (t.RealizedPnlUsd ?? 0) + (t.UnrealizedPnlUsd ?? 0) < 0);
        summary.TotalRealizedPnlUsd = totalRealized != 0 ? Math.Round(totalRealized, 2) : null;
        summary.TotalUnrealizedPnlUsd = totalUnrealized != 0 ? Math.Round(totalUnrealized, 2) : null;
        summary.TotalPnlUsd = (totalRealized + totalUnrealized) != 0
            ? Math.Round(totalRealized + totalUnrealized, 2) : null;
        summary.TotalPnlPct = totalCostBasis > 0
            ? Math.Round(((totalRealized + totalUnrealized) / totalCostBasis) * 100, 2)
            : null;

        summary.TopGainers = withPnl
            .OrderByDescending(t => (t.RealizedPnlUsd ?? 0) + (t.UnrealizedPnlUsd ?? 0))
            .Take(5)
            .Where(t => (t.RealizedPnlUsd ?? 0) + (t.UnrealizedPnlUsd ?? 0) > 0)
            .ToList();

        summary.TopLosers = withPnl
            .OrderBy(t => (t.RealizedPnlUsd ?? 0) + (t.UnrealizedPnlUsd ?? 0))
            .Take(5)
            .Where(t => (t.RealizedPnlUsd ?? 0) + (t.UnrealizedPnlUsd ?? 0) < 0)
            .ToList();

        summary.AllTokenPnl = allTokenPnl
            .OrderByDescending(t => Math.Abs((t.RealizedPnlUsd ?? 0) + (t.UnrealizedPnlUsd ?? 0)))
            .Take(20)
            .ToList();

        return summary;
    }

    /// <summary>
    /// Placeholder — in a full implementation this would fetch extended transfer history
    /// beyond the recent transfers list. For now, we work with available data.
    /// </summary>
    private static IEnumerable<TokenTransfer> GetTimelineImpliedTransfers(TransferHistory history)
    {
        return Enumerable.Empty<TokenTransfer>();
    }
}
