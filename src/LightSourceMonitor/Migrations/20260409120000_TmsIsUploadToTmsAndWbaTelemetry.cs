using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightSourceMonitor.Migrations;

public partial class TmsIsUploadToTmsAndWbaTelemetry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "IsSyncedToTms",
            table: "MeasurementRecords",
            newName: "IsUploadToTms");

        migrationBuilder.RenameIndex(
            name: "IX_MeasurementRecords_IsSyncedToTms",
            table: "MeasurementRecords",
            newName: "IX_MeasurementRecords_IsUploadToTms");

        migrationBuilder.AddColumn<bool>(
            name: "IsUploadToTms",
            table: "WavelengthMeterSnapshots",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "IX_WavelengthMeterSnapshots_IsUploadToTms",
            table: "WavelengthMeterSnapshots",
            column: "IsUploadToTms");

        migrationBuilder.CreateTable(
            name: "WbaTelemetryRecords",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                DeviceSN = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                VoltagesJson = table.Column<string>(type: "TEXT", nullable: false),
                TemperaturesJson = table.Column<string>(type: "TEXT", nullable: false),
                AtmospherePressure = table.Column<double>(type: "REAL", nullable: false),
                IsUploadToTms = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => { table.PrimaryKey("PK_WbaTelemetryRecords", x => x.Id); });

        migrationBuilder.CreateIndex(
            name: "IX_WbaTelemetryRecords_Timestamp",
            table: "WbaTelemetryRecords",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_WbaTelemetryRecords_DeviceSN_Timestamp",
            table: "WbaTelemetryRecords",
            columns: new[] { "DeviceSN", "Timestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_WbaTelemetryRecords_IsUploadToTms",
            table: "WbaTelemetryRecords",
            column: "IsUploadToTms");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WbaTelemetryRecords");

        migrationBuilder.DropIndex(
            name: "IX_WavelengthMeterSnapshots_IsUploadToTms",
            table: "WavelengthMeterSnapshots");

        migrationBuilder.DropColumn(
            name: "IsUploadToTms",
            table: "WavelengthMeterSnapshots");

        migrationBuilder.RenameIndex(
            name: "IX_MeasurementRecords_IsUploadToTms",
            table: "MeasurementRecords",
            newName: "IX_MeasurementRecords_IsSyncedToTms");

        migrationBuilder.RenameColumn(
            name: "IsUploadToTms",
            table: "MeasurementRecords",
            newName: "IsSyncedToTms");
    }
}
