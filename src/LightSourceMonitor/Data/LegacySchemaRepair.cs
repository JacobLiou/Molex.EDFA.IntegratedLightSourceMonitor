using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace LightSourceMonitor.Data;

public static class LegacySchemaRepair
{
    public static async Task<bool> EnsureNoLegacyLaserChannelForeignKeysAsync(MonitorDbContext db, CancellationToken ct = default)
    {
        await using var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        var hasLegacyAlarmFk = await TableSqlContainsAsync(connection, "AlarmEvents", "REFERENCES \"LaserChannels\"", ct);
        var hasLegacyMeasurementFk = await TableSqlContainsAsync(connection, "MeasurementRecords", "REFERENCES \"LaserChannels\"", ct);

        if (!hasLegacyAlarmFk && !hasLegacyMeasurementFk)
            return false;

        var tmsFlagCol = await ResolveMeasurementRecordsTmsFlagColumnAsync(connection, ct);
        if (tmsFlagCol is null)
            throw new InvalidOperationException(
                "MeasurementRecords is missing IsSyncedToTms / IsUploadToTms; cannot repair legacy LaserChannels FK.");

        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;", ct);

        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ""AlarmEvents_new"" (
    ""Id""            INTEGER NOT NULL CONSTRAINT ""PK_AlarmEvents"" PRIMARY KEY AUTOINCREMENT,
    ""ChannelId""     INTEGER NOT NULL,
    ""OccurredAt""    TEXT    NOT NULL,
    ""ResolvedAt""    TEXT    NULL,
    ""AlarmType""     INTEGER NOT NULL,
    ""Level""         INTEGER NOT NULL,
    ""MeasuredValue"" REAL    NOT NULL,
    ""SpecValue""     REAL    NOT NULL,
    ""Delta""         REAL    NOT NULL,
    ""EmailSent""     INTEGER NOT NULL
);
INSERT INTO ""AlarmEvents_new""
    SELECT ""Id"",""ChannelId"",""OccurredAt"",""ResolvedAt"",""AlarmType"",
           ""Level"",""MeasuredValue"",""SpecValue"",""Delta"",""EmailSent""
    FROM ""AlarmEvents"";
DROP TABLE ""AlarmEvents"";
ALTER TABLE ""AlarmEvents_new"" RENAME TO ""AlarmEvents"";
CREATE INDEX IF NOT EXISTS ""IX_AlarmEvents_ChannelId_OccurredAt""
    ON ""AlarmEvents"" (""ChannelId"", ""OccurredAt"");
", ct);

        // Two static SQL paths (no interpolation) — tmsFlagCol is only IsUploadToTms or IsSyncedToTms from resolver.
        if (tmsFlagCol == "IsUploadToTms")
        {
            await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ""MeasurementRecords_new"" (
    ""Id""            INTEGER NOT NULL CONSTRAINT ""PK_MeasurementRecords"" PRIMARY KEY AUTOINCREMENT,
    ""ChannelId""     INTEGER NOT NULL,
    ""Timestamp""     TEXT    NOT NULL,
    ""Power""         REAL    NOT NULL,
    ""Wavelength""    REAL    NOT NULL,
    ""IsUploadToTms"" INTEGER NOT NULL
);
INSERT INTO ""MeasurementRecords_new""
    SELECT ""Id"",""ChannelId"",""Timestamp"",""Power"",""Wavelength"",""IsUploadToTms""
    FROM ""MeasurementRecords"";
DROP TABLE ""MeasurementRecords"";
ALTER TABLE ""MeasurementRecords_new"" RENAME TO ""MeasurementRecords"";
CREATE INDEX IF NOT EXISTS ""IX_MeasurementRecords_ChannelId_Timestamp""
    ON ""MeasurementRecords"" (""ChannelId"", ""Timestamp"");
CREATE INDEX IF NOT EXISTS ""IX_MeasurementRecords_IsUploadToTms""
    ON ""MeasurementRecords"" (""IsUploadToTms"");
", ct);
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ""MeasurementRecords_new"" (
    ""Id""            INTEGER NOT NULL CONSTRAINT ""PK_MeasurementRecords"" PRIMARY KEY AUTOINCREMENT,
    ""ChannelId""     INTEGER NOT NULL,
    ""Timestamp""     TEXT    NOT NULL,
    ""Power""         REAL    NOT NULL,
    ""Wavelength""    REAL    NOT NULL,
    ""IsUploadToTms"" INTEGER NOT NULL
);
INSERT INTO ""MeasurementRecords_new""
    SELECT ""Id"",""ChannelId"",""Timestamp"",""Power"",""Wavelength"",""IsSyncedToTms""
    FROM ""MeasurementRecords"";
DROP TABLE ""MeasurementRecords"";
ALTER TABLE ""MeasurementRecords_new"" RENAME TO ""MeasurementRecords"";
CREATE INDEX IF NOT EXISTS ""IX_MeasurementRecords_ChannelId_Timestamp""
    ON ""MeasurementRecords"" (""ChannelId"", ""Timestamp"");
CREATE INDEX IF NOT EXISTS ""IX_MeasurementRecords_IsUploadToTms""
    ON ""MeasurementRecords"" (""IsUploadToTms"");
", ct);
        }

        await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"LaserChannels\";", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;", ct);

        return true;
    }

    private static async Task<bool> TableSqlContainsAsync(DbConnection connection, string tableName, string keyword, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $tableName";

        var param = cmd.CreateParameter();
        param.ParameterName = "$tableName";
        param.Value = tableName;
        cmd.Parameters.Add(param);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is not string sql || string.IsNullOrWhiteSpace(sql))
            return false;

        return sql.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>After EF rename migration, the column may be IsUploadToTms or still IsSyncedToTms on old DBs.</summary>
    private static async Task<string?> ResolveMeasurementRecordsTmsFlagColumnAsync(DbConnection connection,
        CancellationToken ct)
    {
        var cols = await GetColumnNamesAsync(connection, "MeasurementRecords", ct);
        if (cols.Contains("IsUploadToTms", StringComparer.OrdinalIgnoreCase))
            return "IsUploadToTms";
        if (cols.Contains("IsSyncedToTms", StringComparer.OrdinalIgnoreCase))
            return "IsSyncedToTms";
        return null;
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(DbConnection connection, string tableName,
        CancellationToken ct)
    {
        var pragma = tableName switch
        {
            "MeasurementRecords" => "PRAGMA table_info(MeasurementRecords);",
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
