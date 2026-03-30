using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightSourceMonitor.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLaserChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite does not support DROP CONSTRAINT; rebuilding tables is required.
            // Disable FK enforcement for the duration of the rebuild.
            migrationBuilder.Sql("PRAGMA foreign_keys = OFF;", suppressTransaction: true);

            // ── Rebuild AlarmEvents without FK to LaserChannels ─────────────────
            migrationBuilder.Sql(@"
CREATE TABLE ""AlarmEvents_new"" (
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
CREATE INDEX ""IX_AlarmEvents_ChannelId_OccurredAt""
    ON ""AlarmEvents"" (""ChannelId"", ""OccurredAt"");
");

            // ── Rebuild MeasurementRecords without FK to LaserChannels ───────────
            migrationBuilder.Sql(@"
CREATE TABLE ""MeasurementRecords_new"" (
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
CREATE INDEX ""IX_MeasurementRecords_ChannelId_Timestamp""
    ON ""MeasurementRecords"" (""ChannelId"", ""Timestamp"");
CREATE INDEX ""IX_MeasurementRecords_IsSyncedToTms""
    ON ""MeasurementRecords"" (""IsSyncedToTms"");
");

            // ── Drop LaserChannels (IF EXISTS handles the case it was already gone) ──
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""LaserChannels"";");

            migrationBuilder.Sql("PRAGMA foreign_keys = ON;", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreate LaserChannels and re-add FK constraints (reverse of Up)
            migrationBuilder.Sql("PRAGMA foreign_keys = OFF;", suppressTransaction: true);

            migrationBuilder.CreateTable(
                name: "LaserChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceSN = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelName = table.Column<string>(type: "TEXT", nullable: false),
                    SpecWavelength = table.Column<double>(type: "REAL", nullable: false),
                    SpecPowerMin = table.Column<double>(type: "REAL", nullable: false),
                    SpecPowerMax = table.Column<double>(type: "REAL", nullable: false),
                    AlarmDelta = table.Column<double>(type: "REAL", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LaserChannels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LaserChannels_DeviceSN_ChannelIndex",
                table: "LaserChannels",
                columns: new[] { "DeviceSN", "ChannelIndex" },
                unique: true);

            // Rebuild AlarmEvents with FK restored
            migrationBuilder.Sql(@"
CREATE TABLE ""AlarmEvents_new"" (
    ""Id""            INTEGER NOT NULL CONSTRAINT ""PK_AlarmEvents"" PRIMARY KEY AUTOINCREMENT,
    ""ChannelId""     INTEGER NOT NULL,
    ""OccurredAt""    TEXT    NOT NULL,
    ""ResolvedAt""    TEXT    NULL,
    ""AlarmType""     INTEGER NOT NULL,
    ""Level""         INTEGER NOT NULL,
    ""MeasuredValue"" REAL    NOT NULL,
    ""SpecValue""     REAL    NOT NULL,
    ""Delta""         REAL    NOT NULL,
    ""EmailSent""     INTEGER NOT NULL,
    CONSTRAINT ""FK_AlarmEvents_LaserChannels_ChannelId"" FOREIGN KEY (""ChannelId"") REFERENCES ""LaserChannels"" (""Id"") ON DELETE CASCADE
);
INSERT INTO ""AlarmEvents_new""
    SELECT ""Id"",""ChannelId"",""OccurredAt"",""ResolvedAt"",""AlarmType"",
           ""Level"",""MeasuredValue"",""SpecValue"",""Delta"",""EmailSent""
    FROM ""AlarmEvents"";
DROP TABLE ""AlarmEvents"";
ALTER TABLE ""AlarmEvents_new"" RENAME TO ""AlarmEvents"";
CREATE INDEX ""IX_AlarmEvents_ChannelId_OccurredAt""
    ON ""AlarmEvents"" (""ChannelId"", ""OccurredAt"");
");

            // Rebuild MeasurementRecords with FK restored
            migrationBuilder.Sql(@"
CREATE TABLE ""MeasurementRecords_new"" (
    ""Id""            INTEGER NOT NULL CONSTRAINT ""PK_MeasurementRecords"" PRIMARY KEY AUTOINCREMENT,
    ""ChannelId""     INTEGER NOT NULL,
    ""Timestamp""     TEXT    NOT NULL,
    ""Power""         REAL    NOT NULL,
    ""Wavelength""    REAL    NOT NULL,
    ""IsSyncedToTms"" INTEGER NOT NULL,
    CONSTRAINT ""FK_MeasurementRecords_LaserChannels_ChannelId"" FOREIGN KEY (""ChannelId"") REFERENCES ""LaserChannels"" (""Id"") ON DELETE CASCADE
);
INSERT INTO ""MeasurementRecords_new""
    SELECT ""Id"",""ChannelId"",""Timestamp"",""Power"",""Wavelength"",""IsSyncedToTms""
    FROM ""MeasurementRecords"";
DROP TABLE ""MeasurementRecords"";
ALTER TABLE ""MeasurementRecords_new"" RENAME TO ""MeasurementRecords"";
CREATE INDEX ""IX_MeasurementRecords_ChannelId_Timestamp""
    ON ""MeasurementRecords"" (""ChannelId"", ""Timestamp"");
CREATE INDEX ""IX_MeasurementRecords_IsSyncedToTms""
    ON ""MeasurementRecords"" (""IsSyncedToTms"");
");

            migrationBuilder.Sql("PRAGMA foreign_keys = ON;", suppressTransaction: true);
        }
    }
}
