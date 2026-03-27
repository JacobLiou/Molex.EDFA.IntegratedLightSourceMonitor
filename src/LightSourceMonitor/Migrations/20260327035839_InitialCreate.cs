using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightSourceMonitor.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailConfigs", x => x.Id);
                });

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
                constraints: table =>
                {
                    table.PrimaryKey("PK_TmsConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AlarmEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AlarmType = table.Column<int>(type: "INTEGER", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    MeasuredValue = table.Column<double>(type: "REAL", nullable: false),
                    SpecValue = table.Column<double>(type: "REAL", nullable: false),
                    Delta = table.Column<double>(type: "REAL", nullable: false),
                    EmailSent = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlarmEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlarmEvents_LaserChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "LaserChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Power = table.Column<double>(type: "REAL", nullable: false),
                    Wavelength = table.Column<double>(type: "REAL", nullable: false),
                    IsSyncedToTms = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeasurementRecords_LaserChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "LaserChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlarmEvents_ChannelId_OccurredAt",
                table: "AlarmEvents",
                columns: new[] { "ChannelId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LaserChannels_DeviceSN_ChannelIndex",
                table: "LaserChannels",
                columns: new[] { "DeviceSN", "ChannelIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementRecords_ChannelId_Timestamp",
                table: "MeasurementRecords",
                columns: new[] { "ChannelId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementRecords_IsSyncedToTms",
                table: "MeasurementRecords",
                column: "IsSyncedToTms");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlarmEvents");

            migrationBuilder.DropTable(
                name: "EmailConfigs");

            migrationBuilder.DropTable(
                name: "MeasurementRecords");

            migrationBuilder.DropTable(
                name: "TmsConfigs");

            migrationBuilder.DropTable(
                name: "LaserChannels");
        }
    }
}
