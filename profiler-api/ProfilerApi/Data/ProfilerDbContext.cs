using Microsoft.EntityFrameworkCore;

namespace ProfilerApi.Data;

public class ProfilerDbContext : DbContext
{
    public ProfilerDbContext(DbContextOptions<ProfilerDbContext> options) : base(options) { }

    public DbSet<SnapshotEntity> Snapshots => Set<SnapshotEntity>();
    public DbSet<ReferralEntity> Referrals => Set<ReferralEntity>();
    public DbSet<PnlRecordEntity> PnlRecords => Set<PnlRecordEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SnapshotEntity>(e =>
        {
            e.ToTable("portfolio_snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.Address, x.SnapshotAt });
            e.HasIndex(x => x.Address);
        });

        modelBuilder.Entity<ReferralEntity>(e =>
        {
            e.ToTable("referrals");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => x.AgentAddress);
            e.HasIndex(x => x.ReferralCode);
        });

        modelBuilder.Entity<PnlRecordEntity>(e =>
        {
            e.ToTable("pnl_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.Address, x.Chain });
        });
    }
}

public class SnapshotEntity
{
    public long Id { get; set; }
    public string Address { get; set; } = string.Empty;
    public decimal? TotalValueUsd { get; set; }
    public decimal EthBalance { get; set; }
    public int TokenCount { get; set; }
    public int TransactionCount { get; set; }
    public DateTime SnapshotAt { get; set; }
}

public class ReferralEntity
{
    public long Id { get; set; }
    public string ReferralCode { get; set; } = string.Empty;
    public string AgentAddress { get; set; } = string.Empty;
    public string ReferredAgent { get; set; } = string.Empty;
    public decimal EarningsEth { get; set; }
    public DateTime ReferredAt { get; set; }
}

public class PnlRecordEntity
{
    public long Id { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Chain { get; set; } = "ethereum";
    public decimal? TotalRealizedPnlUsd { get; set; }
    public decimal? TotalUnrealizedPnlUsd { get; set; }
    public int TokensAnalyzed { get; set; }
    public DateTime CalculatedAt { get; set; }
}
