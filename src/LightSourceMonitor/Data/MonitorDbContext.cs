using Microsoft.EntityFrameworkCore;
using LightSourceMonitor.Models;

namespace LightSourceMonitor.Data;

public class MonitorDbContext : DbContext
{
    public DbSet<MeasurementRecord> MeasurementRecords => Set<MeasurementRecord>();
    public DbSet<AlarmEvent> AlarmEvents => Set<AlarmEvent>();

    public MonitorDbContext(DbContextOptions<MonitorDbContext> options) : base(options)
    {
        Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MeasurementRecord>(entity =>
        {
            entity.HasIndex(e => new { e.ChannelId, e.Timestamp });
            entity.HasIndex(e => e.IsSyncedToTms);
        });

        modelBuilder.Entity<AlarmEvent>(entity =>
        {
            entity.HasIndex(e => new { e.ChannelId, e.OccurredAt });
        });
    }
}
