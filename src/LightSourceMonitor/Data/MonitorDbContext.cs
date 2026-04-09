using Microsoft.EntityFrameworkCore;
using LightSourceMonitor.Models;

namespace LightSourceMonitor.Data;

public class MonitorDbContext : DbContext
{
    public DbSet<MeasurementRecord> MeasurementRecords => Set<MeasurementRecord>();
    public DbSet<WavelengthMeterSnapshot> WavelengthMeterSnapshots => Set<WavelengthMeterSnapshot>();
    public DbSet<WbaTelemetryRecord> WbaTelemetryRecords => Set<WbaTelemetryRecord>();
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
            entity.HasIndex(e => e.IsUploadToTms);
        });

        modelBuilder.Entity<AlarmEvent>(entity =>
        {
            entity.HasIndex(e => new { e.ChannelId, e.OccurredAt });
        });

        modelBuilder.Entity<WavelengthMeterSnapshot>(entity =>
        {
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => new { e.QueryDeviceId, e.Timestamp });
            entity.HasIndex(e => e.IsUploadToTms);
        });

        modelBuilder.Entity<WbaTelemetryRecord>(entity =>
        {
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => new { e.DeviceSN, e.Timestamp });
            entity.HasIndex(e => e.IsUploadToTms);
        });
    }
}
