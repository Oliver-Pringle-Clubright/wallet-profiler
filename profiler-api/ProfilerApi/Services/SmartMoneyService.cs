using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Analyzes a wallet's token trading patterns to classify it as smart money,
/// active trader, whale, or retail. Uses transfer history and current holdings
/// to estimate profit/loss signals.
/// </summary>
public class SmartMoneyService
{
    private readonly ILogger<SmartMoneyService> _logger;

    public SmartMoneyService(ILogger<SmartMoneyService> logger)
    {
        _logger = logger;
    }

    public SmartMoneySignal Analyze(
        WalletProfile profile,
        TransferHistory? transfers)
    {
        var signal = new SmartMoneySignal
        {
            Address = profile.Address,
            EnsName = profile.EnsName
        };

        if (transfers == null || transfers.TotalTransfers == 0)
        {
            signal.Classification = "unknown";
            signal.ProfitScore = 0;
            return signal;
        }

        var profitScore = 0;
        var factors = 0;

        // Factor 1: Portfolio value relative to activity (efficiency)
        if (profile.TotalValueUsd.HasValue && profile.TransactionCount > 0)
        {
            var efficiency = profile.TotalValueUsd.Value / Math.Max(profile.TransactionCount, 1);
            if (efficiency > 1000) profitScore += 25; // High value per tx
            else if (efficiency > 100) profitScore += 15;
            else if (efficiency > 10) profitScore += 5;
            factors++;
        }

        // Factor 2: Token diversity with value (holding quality)
        var pricedNonSpam = profile.TopTokens.Where(t => !t.IsSpam && t.ValueUsd > 0).ToList();
        if (pricedNonSpam.Count > 10) profitScore += 20;
        else if (pricedNonSpam.Count > 5) profitScore += 10;
        factors++;

        // Factor 3: Net flow direction (accumulating vs distributing)
        if (transfers.NetFlowUsd.HasValue)
        {
            if (transfers.NetFlowUsd > 0) profitScore += 15; // Net accumulator
            else profitScore += 5; // Net distributor (could be taking profits)
            factors++;
        }

        // Factor 4: Trading frequency (active vs passive)
        var tradingIntensity = (double)transfers.TotalTransfers / Math.Max(1, profile.Activity?.DaysActive ?? 1);
        if (tradingIntensity > 5) profitScore += 10;
        else if (tradingIntensity > 1) profitScore += 5;
        factors++;

        // Factor 5: Blue-chip allocation (quality signal)
        if (profile.PortfolioQuality != null)
        {
            if (profile.PortfolioQuality.BluechipPct > 60) profitScore += 15;
            else if (profile.PortfolioQuality.BluechipPct > 30) profitScore += 10;
            factors++;
        }

        // Factor 6: DeFi participation (sophistication signal)
        if (profile.DeFiPositions.Count > 0)
        {
            profitScore += 15;
            factors++;
        }

        // Normalize to 0-100
        profitScore = Math.Clamp(profitScore, 0, 100);

        // Classify
        var classification = (profitScore, profile.TotalValueUsd) switch
        {
            ( > 70, > 100_000) => "smart_money",
            ( > 70, _) => "active_trader",
            (_, > 1_000_000) => "whale",
            ( > 40, _) => "active_trader",
            _ => "retail"
        };

        // Extract recent trades from transfers
        var recentTrades = transfers.RecentTransfers
            .Where(t => t.Amount > 0)
            .Take(10)
            .Select(t => new SmartMoneyTrade
            {
                TokenSymbol = t.TokenSymbol,
                TokenAddress = t.TokenAddress,
                Action = t.Direction == "in" ? "buy" : "sell",
                Amount = t.Amount,
                ValueUsd = t.ValueUsd,
                Timestamp = t.Timestamp
            })
            .ToList();

        signal.ProfitScore = profitScore;
        signal.Classification = classification;
        signal.RecentTrades = recentTrades;

        // Estimate simple PnL from current holdings vs recent outflows
        if (profile.TotalValueUsd > 0 && transfers.OutboundCount > 0)
        {
            var outflowUsd = transfers.RecentTransfers
                .Where(t => t.Direction == "out" && t.ValueUsd.HasValue)
                .Sum(t => t.ValueUsd!.Value);
            if (outflowUsd > 0)
            {
                var currentValue = profile.TotalValueUsd.Value;
                signal.EstimatedPnlPct = Math.Round(((currentValue + outflowUsd) / outflowUsd - 1) * 100, 1);
            }
        }

        return signal;
    }
}
