namespace ProfilerApi.Models;

public class WalletProfile
{
    public string Tier { get; set; } = "standard";
    public string Address { get; set; } = string.Empty;
    public string? EnsName { get; set; }
    public decimal EthBalance { get; set; }
    public decimal? EthPriceUsd { get; set; }
    public decimal? EthValueUsd { get; set; }
    public decimal? TotalValueUsd { get; set; }
    public int TransactionCount { get; set; }
    public List<TokenBalance> TopTokens { get; set; } = [];
    public List<DeFiPosition> DeFiPositions { get; set; } = [];
    public RiskAssessment Risk { get; set; } = new();
    public WalletActivity? Activity { get; set; }
    public string? Summary { get; set; }
    public List<string> Tags { get; set; } = [];
    public PortfolioQuality? PortfolioQuality { get; set; }
    public AcpTrustScore? AcpTrust { get; set; }
    public ApprovalRisk? ApprovalRisk { get; set; }
    public List<ContractInteraction> TopInteractions { get; set; } = [];
    public NftSummary? Nfts { get; set; }
    public DateTime ProfiledAt { get; set; } = DateTime.UtcNow;
}

public class TokenBalance
{
    public string Symbol { get; set; } = string.Empty;
    public string ContractAddress { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public int Decimals { get; set; }
    public decimal? PriceUsd { get; set; }
    public decimal? ValueUsd { get; set; }
    public bool IsSpam { get; set; }
}

public class DeFiPosition
{
    public string Protocol { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Asset { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class RiskAssessment
{
    public int Score { get; set; }
    public string Level { get; set; } = "unknown";
    public List<string> Flags { get; set; } = [];
}

public class WalletActivity
{
    public DateTime? FirstTransaction { get; set; }
    public DateTime? LastTransaction { get; set; }
    public int DaysActive { get; set; }
    public int UniqueInteractions { get; set; }
}

public class PortfolioQuality
{
    public decimal BluechipPct { get; set; }
    public decimal StablecoinPct { get; set; }
    public decimal SpamPct { get; set; }
    public int DiversityScore { get; set; }
    public int QualityScore { get; set; }
    public string Grade { get; set; } = "N/A";
}

public class AcpTrustScore
{
    public int Score { get; set; }
    public string Level { get; set; } = "untrusted";
    public List<string> Factors { get; set; } = [];
}

public class ApprovalRisk
{
    public int TotalApprovals { get; set; }
    public int UnlimitedApprovals { get; set; }
    public int HighRiskApprovals { get; set; }
    public string RiskLevel { get; set; } = "safe";
    public List<TokenApproval> Approvals { get; set; } = [];
}

public class TokenApproval
{
    public string TokenSymbol { get; set; } = string.Empty;
    public string TokenAddress { get; set; } = string.Empty;
    public string SpenderAddress { get; set; } = string.Empty;
    public string SpenderLabel { get; set; } = string.Empty;
    public string SpenderCategory { get; set; } = string.Empty;
    public bool IsUnlimited { get; set; }
    public string RiskLevel { get; set; } = "safe";
}

public class ContractInteraction
{
    public string Address { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? Category { get; set; }
    public int TransactionCount { get; set; }
}

public class TrustCheckResponse
{
    public string Address { get; set; } = string.Empty;
    public string? EnsName { get; set; }
    public decimal EthBalance { get; set; }
    public int TransactionCount { get; set; }
    public int TokenCount { get; set; }
    public int TrustScore { get; set; }
    public string TrustLevel { get; set; } = "untrusted";
    public List<string> Factors { get; set; } = [];
}

public class BatchProfileRequest
{
    public List<string> Addresses { get; set; } = [];
    public string Chain { get; set; } = "ethereum";
    public string Tier { get; set; } = "standard";
}

public class BatchProfileResponse
{
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public long ElapsedMs { get; set; }
    public List<BatchProfileResult> Results { get; set; } = [];
}

public class BatchProfileResult
{
    public string Address { get; set; } = string.Empty;
    public WalletProfile? Profile { get; set; }
    public string? Error { get; set; }
}

// --- v1.3: NFT Holdings ---

public class NftSummary
{
    public int TotalCount { get; set; }
    public int CollectionCount { get; set; }
    public decimal? EstimatedValueEth { get; set; }
    public decimal? EstimatedValueUsd { get; set; }
    public List<NftCollection> TopCollections { get; set; } = [];
}

public class NftCollection
{
    public string Name { get; set; } = string.Empty;
    public string ContractAddress { get; set; } = string.Empty;
    public int OwnedCount { get; set; }
    public decimal? FloorPriceEth { get; set; }
    public decimal? FloorPriceUsd { get; set; }
}

// --- v1.3: Whale Movement Monitor ---

public class MonitorSubscription
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Address { get; set; } = string.Empty;
    public string Chain { get; set; } = "ethereum";
    public string WebhookUrl { get; set; } = string.Empty;
    public decimal ThresholdEth { get; set; } = 10m;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Active { get; set; } = true;
}

public class MonitorRequest
{
    public string Address { get; set; } = string.Empty;
    public string Chain { get; set; } = "ethereum";
    public string WebhookUrl { get; set; } = string.Empty;
    public decimal ThresholdEth { get; set; } = 10m;
}

public class WalletAlert
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? AmountEth { get; set; }
    public string? TxHash { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

public class MonitorStatusResponse
{
    public int ActiveSubscriptions { get; set; }
    public List<MonitorSubscription> Subscriptions { get; set; } = [];
}

// --- v1.3: Cross-Chain Aggregated Profile ---

public class MultiChainRequest
{
    public string Address { get; set; } = string.Empty;
    public List<string> Chains { get; set; } = ["ethereum", "base", "arbitrum"];
    public string Tier { get; set; } = "standard";
}

public class MultiChainProfile
{
    public string Address { get; set; } = string.Empty;
    public string? EnsName { get; set; }
    public decimal? TotalValueUsd { get; set; }
    public Dictionary<string, WalletProfile> ChainProfiles { get; set; } = new();
    public List<string> ActiveChains { get; set; } = [];
    public DateTime ProfiledAt { get; set; } = DateTime.UtcNow;
}
