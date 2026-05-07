using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OuraDashboard.Data.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(OuraDbContext))]
    [Migration("20260506180500_AddWeatherData")]
    public partial class AddWeatherData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeatherLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    ElevationMeters = table.Column<double>(type: "double precision", nullable: true),
                    Timezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RawJson = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherLocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeatherStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WeatherLocationId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    StationCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    ElevationMeters = table.Column<double>(type: "double precision", nullable: true),
                    DistanceKm = table.Column<double>(type: "double precision", nullable: true),
                    ElementCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ElementName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ObservationPeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ObservationPeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RawJson = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherStations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeatherStations_WeatherLocations_WeatherLocationId",
                        column: x => x.WeatherLocationId,
                        principalTable: "WeatherLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WeatherHourlySamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WeatherLocationId = table.Column<int>(type: "integer", nullable: false),
                    WeatherStationId = table.Column<int>(type: "integer", nullable: true),
                    Source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimestampLocal = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TemperatureC = table.Column<double>(type: "double precision", nullable: true),
                    RelativeHumidityPct = table.Column<double>(type: "double precision", nullable: true),
                    DewPointC = table.Column<double>(type: "double precision", nullable: true),
                    ApparentTemperatureC = table.Column<double>(type: "double precision", nullable: true),
                    PrecipitationMm = table.Column<double>(type: "double precision", nullable: true),
                    RainMm = table.Column<double>(type: "double precision", nullable: true),
                    SnowfallCm = table.Column<double>(type: "double precision", nullable: true),
                    SnowDepthM = table.Column<double>(type: "double precision", nullable: true),
                    PressureMslHpa = table.Column<double>(type: "double precision", nullable: true),
                    SurfacePressureHpa = table.Column<double>(type: "double precision", nullable: true),
                    CloudCoverPct = table.Column<double>(type: "double precision", nullable: true),
                    WindSpeedMs = table.Column<double>(type: "double precision", nullable: true),
                    WindDirectionDeg = table.Column<double>(type: "double precision", nullable: true),
                    WindGustMs = table.Column<double>(type: "double precision", nullable: true),
                    ShortwaveRadiationWm2 = table.Column<double>(type: "double precision", nullable: true),
                    SunshineDurationSec = table.Column<double>(type: "double precision", nullable: true),
                    SoilTemperature0To7CmC = table.Column<double>(type: "double precision", nullable: true),
                    SoilMoisture0To7Cm = table.Column<double>(type: "double precision", nullable: true),
                    RawJson = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherHourlySamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeatherHourlySamples_WeatherLocations_WeatherLocationId",
                        column: x => x.WeatherLocationId,
                        principalTable: "WeatherLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WeatherHourlySamples_WeatherStations_WeatherStationId",
                        column: x => x.WeatherStationId,
                        principalTable: "WeatherStations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeatherHourlySamples_WeatherLocationId_Source_Model_WeatherStationId_TimestampUtc",
                table: "WeatherHourlySamples",
                columns: new[] { "WeatherLocationId", "Source", "Model", "WeatherStationId", "TimestampUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeatherHourlySamples_WeatherLocationId_TimestampUtc",
                table: "WeatherHourlySamples",
                columns: new[] { "WeatherLocationId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WeatherHourlySamples_WeatherStationId",
                table: "WeatherHourlySamples",
                column: "WeatherStationId");

            migrationBuilder.CreateIndex(
                name: "IX_WeatherLocations_Name",
                table: "WeatherLocations",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeatherStations_WeatherLocationId_Source_StationCode_ElementCode",
                table: "WeatherStations",
                columns: new[] { "WeatherLocationId", "Source", "StationCode", "ElementCode" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WeatherHourlySamples");
            migrationBuilder.DropTable(name: "WeatherStations");
            migrationBuilder.DropTable(name: "WeatherLocations");
        }
    }
}
