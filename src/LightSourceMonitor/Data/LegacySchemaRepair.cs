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

        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ""MeasurementRecords_new"" (
    ""Id""            INTEGER NOT NULL CONSTRAINT ""PK_MeasurementRecords"" PRIMARY KEY AUTOINCREMENT,
    ""ChannelId""     INTEGER NOT NULL,
    ""Timestamp""     TEXT    NOT NULL,
    ""Power""         REAL    NOT NULL,
    ""Wavelength""    REAL    NOT NULL,
    ""IsSyncedToTms"" INTEGER NOT NULL
);
INSERT INTO ""MeasurementRecords_new""
    SELECT ""Id"",""ChannelId"",""Timestamp"",""Power"",""Wavelength"",""IsSyncedToTms""
    FROM ""MeasurementRecords"";
DROP TABLE ""MeasurementRecords"";
ALTER TABLE ""MeasurementRecords_new"" RENAME TO ""MeasurementRecords"";
CREATE INDEX IF NOT EXISTS ""IX_MeasurementRecords_ChannelId_Timestamp""
    ON ""MeasurementRecords"" (""ChannelId"", ""Timestamp"");
CREATE INDEX IF NOT EXISTS ""IX_MeasurementRecords_IsSyncedToTms""
    ON ""MeasurementRecords"" (""IsSyncedToTms"");
", ct);

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
}
