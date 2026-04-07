using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightSourceMonitor.Migrations;

public partial class AddWbaMeasurementRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WbaMeasurementRecords",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                DeviceSN = table.Column<string>(type: "TEXT", nullable: false),
                Temperature0 = table.Column<double>(type: "REAL", nullable: false),
                Temperature1 = table.Column<double>(type: "REAL", nullable: false),
                Temperature2 = table.Column<double>(type: "REAL", nullable: false),
                Temperature3 = table.Column<double>(type: "REAL", nullable: false),
                Voltage0 = table.Column<double>(type: "REAL", nullable: false),
                Voltage1 = table.Column<double>(type: "REAL", nullable: false),
                Voltage2 = table.Column<double>(type: "REAL", nullable: false),
                Voltage3 = table.Column<double>(type: "REAL", nullable: false),
                AtmospherePressure = table.Column<double>(type: "REAL", nullable: false)
            },
            constraints: table => { table.PrimaryKey("PK_WbaMeasurementRecords", x => x.Id); });

        migrationBuilder.CreateIndex(
            name: "IX_WbaMeasurementRecords_DeviceSN_Timestamp",
            table: "WbaMeasurementRecords",
            columns: new[] { "DeviceSN", "Timestamp" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WbaMeasurementRecords");
    }
}
