using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OuraDashboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OuraId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Steps = table.Column<int>(type: "integer", nullable: true),
                    ActiveCalories = table.Column<int>(type: "integer", nullable: true),
                    TotalCalories = table.Column<int>(type: "integer", nullable: true),
                    EquivalentWalkingDistance = table.Column<int>(type: "integer", nullable: true),
                    InactiveTime = table.Column<int>(type: "integer", nullable: true),
                    RestTime = table.Column<int>(type: "integer", nullable: true),
                    LowActivityTime = table.Column<int>(type: "integer", nullable: true),
                    MediumActivityTime = table.Column<int>(type: "integer", nullable: true),
                    HighActivityTime = table.Column<int>(type: "integer", nullable: true),
                    RawJson = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyActivities_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyHrvs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OuraId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    AvgHrv5Min = table.Column<double>(type: "double precision", nullable: true),
                    RawJson = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyHrvs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyHrvs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyReadinesses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OuraId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    TemperatureDeviation = table.Column<double>(type: "double precision", nullable: true),
                    TemperatureTrendDeviation = table.Column<double>(type: "double precision", nullable: true),
                    ActivityBalanceContributor = table.Column<int>(type: "integer", nullable: true),
                    BodyTemperatureContributor = table.Column<int>(type: "integer", nullable: true),
                    HrvBalanceContributor = table.Column<int>(type: "integer", nullable: true),
                    PreviousDayActivityContributor = table.Column<int>(type: "integer", nullable: true),
                    PreviousNightContributor = table.Column<int>(type: "integer", nullable: true),
                    RecoveryIndexContributor = table.Column<int>(type: "integer", nullable: true),
                    RestingHeartRateContributor = table.Column<int>(type: "integer", nullable: true),
                    SleepBalanceContributor = table.Column<int>(type: "integer", nullable: true),
                    RawJson = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyReadinesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyReadinesses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailySleeps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OuraId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    DeepSleepContributor = table.Column<int>(type: "integer", nullable: true),
                    EfficiencyContributor = table.Column<int>(type: "integer", nullable: true),
                    LatencyContributor = table.Column<int>(type: "integer", nullable: true),
                    RemSleepContributor = table.Column<int>(type: "integer", nullable: true),
                    RestfulnessContributor = table.Column<int>(type: "integer", nullable: true),
                    TimingContributor = table.Column<int>(type: "integer", nullable: true),
                    TotalSleepContributor = table.Column<int>(type: "integer", nullable: true),
                    RawJson = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailySleeps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailySleeps_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyStresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OuraId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    StressHigh = table.Column<int>(type: "integer", nullable: true),
                    RecoveryHigh = table.Column<int>(type: "integer", nullable: true),
                    DaytimeStress = table.Column<int>(type: "integer", nullable: true),
                    RawJson = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyStresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyStresses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HeartRateSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Bpm = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeartRateSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HeartRateSamples_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SleepSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OuraId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    BedtimeStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BedtimeEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AverageBreath = table.Column<double>(type: "double precision", nullable: true),
                    AverageHeartRate = table.Column<double>(type: "double precision", nullable: true),
                    AverageHrv = table.Column<int>(type: "integer", nullable: true),
                    AwakeTime = table.Column<int>(type: "integer", nullable: true),
                    DeepSleepDuration = table.Column<int>(type: "integer", nullable: true),
                    LightSleepDuration = table.Column<int>(type: "integer", nullable: true),
                    RemSleepDuration = table.Column<int>(type: "integer", nullable: true),
                    TotalSleepDuration = table.Column<int>(type: "integer", nullable: true),
                    TimeInBed = table.Column<int>(type: "integer", nullable: true),
                    Efficiency = table.Column<int>(type: "integer", nullable: true),
                    Latency = table.Column<int>(type: "integer", nullable: true),
                    LowestHeartRate = table.Column<int>(type: "integer", nullable: true),
                    RestlessPeriods = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: true),
                    HeartRateSeries = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    HrvSeries = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    SleepPhase30Sec = table.Column<string>(type: "text", nullable: true),
                    SleepPhase5Min = table.Column<string>(type: "text", nullable: true),
                    RawJson = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SleepSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SleepSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Vo2Maxes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OuraId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Vo2MaxValue = table.Column<double>(type: "double precision", nullable: true),
                    RawJson = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vo2Maxes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vo2Maxes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyActivities_UserId_Day",
                table: "DailyActivities",
                columns: new[] { "UserId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyHrvs_UserId_Day",
                table: "DailyHrvs",
                columns: new[] { "UserId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyReadinesses_UserId_Day",
                table: "DailyReadinesses",
                columns: new[] { "UserId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailySleeps_UserId_Day",
                table: "DailySleeps",
                columns: new[] { "UserId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyStresses_UserId_Day",
                table: "DailyStresses",
                columns: new[] { "UserId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HeartRateSamples_UserId_Timestamp",
                table: "HeartRateSamples",
                columns: new[] { "UserId", "Timestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SleepSessions_UserId_Day",
                table: "SleepSessions",
                columns: new[] { "UserId", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_SleepSessions_UserId_OuraId",
                table: "SleepSessions",
                columns: new[] { "UserId", "OuraId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Name",
                table: "Users",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vo2Maxes_UserId_Day",
                table: "Vo2Maxes",
                columns: new[] { "UserId", "Day" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyActivities");

            migrationBuilder.DropTable(
                name: "DailyHrvs");

            migrationBuilder.DropTable(
                name: "DailyReadinesses");

            migrationBuilder.DropTable(
                name: "DailySleeps");

            migrationBuilder.DropTable(
                name: "DailyStresses");

            migrationBuilder.DropTable(
                name: "HeartRateSamples");

            migrationBuilder.DropTable(
                name: "SleepSessions");

            migrationBuilder.DropTable(
                name: "Vo2Maxes");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
