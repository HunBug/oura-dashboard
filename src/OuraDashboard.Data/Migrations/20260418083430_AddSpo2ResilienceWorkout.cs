using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OuraDashboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpo2ResilienceWorkout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyResilienceRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OuraId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SleepRecovery = table.Column<double>(type: "double precision", nullable: true),
                    DaytimeRecovery = table.Column<double>(type: "double precision", nullable: true),
                    Stress = table.Column<double>(type: "double precision", nullable: true),
                    RawJson = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyResilienceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyResilienceRecords_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailySpo2s",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OuraId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    BreathingDisturbanceIndex = table.Column<int>(type: "integer", nullable: true),
                    Spo2Average = table.Column<double>(type: "double precision", nullable: true),
                    RawJson = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailySpo2s", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailySpo2s_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Workouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OuraId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Activity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Calories = table.Column<double>(type: "double precision", nullable: true),
                    Distance = table.Column<double>(type: "double precision", nullable: true),
                    Intensity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    StartDatetime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndDatetime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RawJson = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Workouts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyResilienceRecords_UserId_Day",
                table: "DailyResilienceRecords",
                columns: new[] { "UserId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailySpo2s_UserId_Day",
                table: "DailySpo2s",
                columns: new[] { "UserId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_UserId_Day",
                table: "Workouts",
                columns: new[] { "UserId", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_UserId_OuraId",
                table: "Workouts",
                columns: new[] { "UserId", "OuraId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyResilienceRecords");

            migrationBuilder.DropTable(
                name: "DailySpo2s");

            migrationBuilder.DropTable(
                name: "Workouts");
        }
    }
}
