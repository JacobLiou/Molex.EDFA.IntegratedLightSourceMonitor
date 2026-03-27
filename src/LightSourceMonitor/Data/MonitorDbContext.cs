using Microsoft.EntityFrameworkCore;
using LightSourceMonitor.Models;

namespace LightSourceMonitor.Data;

public class MonitorDbContext : DbContext
{
    public DbSet<LaserChannel> LaserChannels => Set<LaserChannel>();
    public DbSet<MeasurementRecord> MeasurementRecords => Set<MeasurementRecord>();
    public DbSet<AlarmEvent> AlarmEvents => Set<AlarmEvent>();
    public DbSet<EmailConfig> EmailConfigs => Set<EmailConfig>();
    public DbSet<TmsConfig> TmsConfigs => Set<TmsConfig>();

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
            entity.HasOne(e => e.Channel)
                  .WithMany(c => c.Measurements)
                  .HasForeignKey(e => e.ChannelId);
        });

        modelBuilder.Entity<AlarmEvent>(entity =>
        {
            entity.HasIndex(e => new { e.ChannelId, e.OccurredAt });
            entity.HasOne(e => e.Channel)
                  .WithMany(c => c.AlarmEvents)
                  .HasForeignKey(e => e.ChannelId);
        });

        modelBuilder.Entity<LaserChannel>(entity =>
        {
            entity.HasIndex(e => new { e.DeviceSN, e.ChannelIndex }).IsUnique();
        });
    }
}
