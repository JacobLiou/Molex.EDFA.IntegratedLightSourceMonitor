using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace LightSourceMonitor.Data;

/// <summary>
/// Aligns on-disk SQLite schema with the current model when EF history and reality diverge
/// (e.g. backup restore, or a partially applied migration). Fixes missing columns/tables such as
/// IsUploadToTms and WbaTelemetryRecords.
/// </summary>
public static class TmsUploadColumnSchemaFix
{
    public static async Task EnsureAsync(MonitorDbContext db, CancellationToken ct = default)
    {
        await using var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await EnsureMeasurementRecordsAsync(db, connection, ct);
        await EnsureWavelengthMeterSnapshotsAsync(db, connection, ct);
        await EnsureWbaTelemetryRecordsTableAsync(db, connection, ct);
    }

    private static async Task EnsureMeasurementRecordsAsync(MonitorDbContext db, DbConnection connection,
        CancellationToken ct)
    {
        if (!await TableExistsAsync(connection, "MeasurementRecords", ct))
            return;

        var cols = await GetColumnNamesAsync(connection, "MeasurementRecords", ct);
        if (cols.Contains("IsUploadToTms"))
            return;

        if (!cols.Contains("IsSyncedToTms"))
        {
            Log.Warning("MeasurementRecords has neither IsUploadToTms nor IsSyncedToTms; skipping TMS column repair.");
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            @"ALTER TABLE ""MeasurementRecords"" RENAME COLUMN ""IsSyncedToTms"" TO ""IsUploadToTms"";", ct);
        Log.Information("Repaired schema: MeasurementRecords.IsSyncedToTms renamed to IsUploadToTms.");

        await db.Database.ExecuteSqlRawAsync(@"DROP INDEX IF EXISTS ""IX_MeasurementRecords_IsSyncedToTms"";", ct);
        await db.Database.ExecuteSqlRawAsync(
            @"CREATE INDEX IF NOT EXISTS ""IX_MeasurementRecords_IsUploadToTms"" ON ""MeasurementRecords"" (""IsUploadToTms"");",
            ct);
    }

    private static async Task EnsureWavelengthMeterSnapshotsAsync(MonitorDbContext db, DbConnection connection,
        CancellationToken ct)
    {
        if (!await TableExistsAsync(connection, "WavelengthMeterSnapshots", ct))
            return;

        var cols = await GetColumnNamesAsync(connection, "WavelengthMeterSnapshots", ct);
        if (cols.Contains("IsUploadToTms"))
            return;

        await db.Database.ExecuteSqlRawAsync(
            @"ALTER TABLE ""WavelengthMeterSnapshots"" ADD COLUMN ""IsUploadToTms"" INTEGER NOT NULL DEFAULT 0;", ct);
        await db.Database.ExecuteSqlRawAsync(
            @"CREATE INDEX IF NOT EXISTS ""IX_WavelengthMeterSnapshots_IsUploadToTms"" ON ""WavelengthMeterSnapshots"" (""IsUploadToTms"");",
            ct);
        Log.Information("Repaired schema: WavelengthMeterSnapshots.IsUploadToTms added.");
    }

    /// <summary>Creates WbaTelemetryRecords when migration history says it exists but the table file was never upgraded.</summary>
    private static async Task EnsureWbaTelemetryRecordsTableAsync(MonitorDbContext db, DbConnection connection,
        CancellationToken ct)
    {
        if (await TableExistsAsync(connection, "WbaTelemetryRecords", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            @"
CREATE TABLE ""WbaTelemetryRecords"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_WbaTelemetryRecords"" PRIMARY KEY AUTOINCREMENT,
    ""DeviceSN"" TEXT NOT NULL,
    ""Timestamp"" TEXT NOT NULL,
    ""VoltagesJson"" TEXT NOT NULL,
    ""TemperaturesJson"" TEXT NOT NULL,
    ""AtmospherePressure"" REAL NOT NULL,
    ""IsUploadToTms"" INTEGER NOT NULL
);",
            ct);
        await db.Database.ExecuteSqlRawAsync(
            @"CREATE INDEX ""IX_WbaTelemetryRecords_Timestamp"" ON ""WbaTelemetryRecords"" (""Timestamp"");", ct);
        await db.Database.ExecuteSqlRawAsync(
            @"CREATE INDEX ""IX_WbaTelemetryRecords_DeviceSN_Timestamp"" ON ""WbaTelemetryRecords"" (""DeviceSN"", ""Timestamp"");",
            ct);
        await db.Database.ExecuteSqlRawAsync(
            @"CREATE INDEX ""IX_WbaTelemetryRecords_IsUploadToTms"" ON ""WbaTelemetryRecords"" (""IsUploadToTms"");", ct);
        Log.Information("Repaired schema: created missing table WbaTelemetryRecords.");
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string tableName, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$n";
        p.Value = tableName;
        cmd.Parameters.Add(p);
        var o = await cmd.ExecuteScalarAsync(ct);
        return o is not null;
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(DbConnection connection, string tableName,
        CancellationToken ct)
    {
        var pragma = tableName switch
        {
            "MeasurementRecords" => "PRAGMA table_info(MeasurementRecords);",
            "WavelengthMeterSnapshots" => "PRAGMA table_info(WavelengthMeterSnapshots);",
            _ => throw new ArgumentOutOfRangeException(nameof(tableName))
        };

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = pragma;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(1))
                set.Add(reader.GetString(1));
        }

        return set;
    }
}
