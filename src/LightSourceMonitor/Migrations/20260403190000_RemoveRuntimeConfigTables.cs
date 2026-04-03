using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightSourceMonitor.Migrations;

/// <summary>通用配置改为 config/*.json，SQLite 仅保留趋势与告警表。</summary>
public partial class RemoveRuntimeConfigTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AcquisitionConfigs");
        migrationBuilder.DropTable(name: "EmailConfigs");
        migrationBuilder.DropTable(name: "TmsConfigs");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EmailConfigs",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                SmtpServer = table.Column<string>(type: "TEXT", nullable: false),
                SmtpPort = table.Column<int>(type: "INTEGER", nullable: false),
                Username = table.Column<string>(type: "TEXT", nullable: false),
                EncryptedPassword = table.Column<string>(type: "TEXT", nullable: false),
                FromAddress = table.Column<string>(type: "TEXT", nullable: false),
                Recipients = table.Column<string>(type: "TEXT", nullable: false),
                UseSsl = table.Column<bool>(type: "INTEGER", nullable: false),
                MinAlarmLevel = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table => { table.PrimaryKey("PK_EmailConfigs", x => x.Id); });

        migrationBuilder.CreateTable(
            name: "TmsConfigs",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                BaseUrl = table.Column<string>(type: "TEXT", nullable: false),
                ApiKey = table.Column<string>(type: "TEXT", nullable: false),
                UploadIntervalSec = table.Column<int>(type: "INTEGER", nullable: false),
                IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => { table.PrimaryKey("PK_TmsConfigs", x => x.Id); });

        migrationBuilder.CreateTable(
            name: "AcquisitionConfigs",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                SamplingIntervalMs = table.Column<int>(type: "INTEGER", nullable: false),
                WmSweepEveryN = table.Column<int>(type: "INTEGER", nullable: false),
                DbWriteEveryN = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table => { table.PrimaryKey("PK_AcquisitionConfigs", x => x.Id); });
    }
}
