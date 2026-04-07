using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightSourceMonitor.Migrations;

public partial class AddWmMeasurementRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WmMeasurementRecords",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                QueryDeviceId = table.Column<string>(type: "TEXT", nullable: false),
                ChannelIndex = table.Column<int>(type: "INTEGER", nullable: false),
                WavelengthNm = table.Column<double>(type: "REAL", nullable: false),
                WmPowerDbm = table.Column<double>(type: "REAL", nullable: true),
                IsValid = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => { table.PrimaryKey("PK_WmMeasurementRecords", x => x.Id); });

        migrationBuilder.CreateIndex(
            name: "IX_WmMeasurementRecords_QueryDeviceId_ChannelIndex_Timestamp",
            table: "WmMeasurementRecords",
            columns: new[] { "QueryDeviceId", "ChannelIndex", "Timestamp" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WmMeasurementRecords");
    }
}
