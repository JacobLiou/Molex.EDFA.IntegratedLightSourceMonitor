using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightSourceMonitor.Migrations;

public partial class AddWavelengthMeterSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WavelengthMeterSnapshots",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                QueryDeviceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                OneTimeValues = table.Column<string>(type: "TEXT", nullable: false),
                PowerValues = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => { table.PrimaryKey("PK_WavelengthMeterSnapshots", x => x.Id); });

        migrationBuilder.CreateIndex(
            name: "IX_WavelengthMeterSnapshots_Timestamp",
            table: "WavelengthMeterSnapshots",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_WavelengthMeterSnapshots_QueryDeviceId_Timestamp",
            table: "WavelengthMeterSnapshots",
            columns: new[] { "QueryDeviceId", "Timestamp" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WavelengthMeterSnapshots");
    }
}
