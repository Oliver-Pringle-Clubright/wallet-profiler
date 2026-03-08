using System.Collections.Concurrent;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Manages an agent referral program where agents earn commission
/// for referring other agents to use the Wallet Profiler service.
/// Generates unique referral codes and tracks earnings.
/// </summary>
public class ReferralService
{
    private readonly ConcurrentDictionary<string, ReferralAgent> _agents = new();
    private readonly ConcurrentDictionary<string, string> _codeToAgent = new();
    private readonly ILogger<ReferralService> _logger;

    private const decimal CommissionRate = 0.10m; // 10% commission
    private const decimal BaseProfileFee = 0.001m; // Standard tier fee

    public ReferralService(ILogger<ReferralService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers an agent in the referral program and returns their referral code.
    /// </summary>
    public string Register(string agentAddress)
    {
        var address = agentAddress.ToLowerInvariant();
        var agent = _agents.GetOrAdd(address, _ =>
        {
            var code = GenerateCode(address);
            _codeToAgent[code] = address;
            return new ReferralAgent
            {
                Address = address,
                ReferralCode = code,
                RegisteredAt = DateTime.UtcNow
            };
        });

        _logger.LogInformation("Agent {Address} registered with referral code {Code}",
            address, agent.ReferralCode);

        return agent.ReferralCode;
    }

    /// <summary>
    /// Records a referral when a referred agent uses the service.
    /// </summary>
    public void RecordReferral(string referralCode, string referredAgent, decimal feePaid)
    {
        if (!_codeToAgent.TryGetValue(referralCode.ToUpperInvariant(), out var referrerAddress))
            return;

        if (!_agents.TryGetValue(referrerAddress, out var agent))
            return;

        var commission = feePaid * CommissionRate;

        lock (agent)
        {
            agent.TotalReferrals++;
            agent.TotalEarningsEth += commission;
            agent.Referrals.Add(new ReferralRecord
            {
                ReferralCode = referralCode,
                ReferredAgent = referredAgent.ToLowerInvariant(),
                EarningsEth = commission,
                ReferredAt = DateTime.UtcNow
            });

            // Cap stored records
            while (agent.Referrals.Count > 100)
                agent.Referrals.RemoveAt(0);
        }

        _logger.LogInformation("Referral recorded: {Referrer} earned {Commission} ETH from {Referred}",
            referrerAddress, commission, referredAgent);
    }

    /// <summary>
    /// Gets referral statistics for an agent.
    /// </summary>
    public ReferralStats GetStats(string agentAddress)
    {
        var address = agentAddress.ToLowerInvariant();
        if (!_agents.TryGetValue(address, out var agent))
            return new ReferralStats();

        lock (agent)
        {
            return new ReferralStats
            {
                TotalReferrals = agent.TotalReferrals,
                TotalEarningsEth = agent.TotalEarningsEth,
                CommissionRate = CommissionRate,
                RecentReferrals = agent.Referrals.TakeLast(10).ToList()
            };
        }
    }

    /// <summary>
    /// Validates a referral code exists.
    /// </summary>
    public bool IsValidCode(string code) =>
        _codeToAgent.ContainsKey(code.ToUpperInvariant());

    private static string GenerateCode(string address)
    {
        // Generate a short, memorable code from the address
        var hash = address.GetHashCode();
        return $"WP{Math.Abs(hash) % 100000:D5}".ToUpperInvariant();
    }

    private class ReferralAgent
    {
        public string Address { get; set; } = string.Empty;
        public string ReferralCode { get; set; } = string.Empty;
        public int TotalReferrals { get; set; }
        public decimal TotalEarningsEth { get; set; }
        public DateTime RegisteredAt { get; set; }
        public List<ReferralRecord> Referrals { get; set; } = [];
    }
}
