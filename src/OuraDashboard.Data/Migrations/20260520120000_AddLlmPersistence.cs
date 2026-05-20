using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OuraDashboard.Data.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(OuraDbContext))]
    [Migration("20260520120000_AddLlmPersistence")]
    public partial class AddLlmPersistence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LlmInteractions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    UserNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Day = table.Column<DateOnly>(type: "date", nullable: true),
                    StartDay = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDay = table.Column<DateOnly>(type: "date", nullable: true),
                    PromptKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PromptVersion = table.Column<int>(type: "integer", nullable: false),
                    Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ParametersJson = table.Column<string>(type: "jsonb", nullable: false),
                    InputHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InputJson = table.Column<string>(type: "jsonb", nullable: false),
                    MessagesJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResponseText = table.Column<string>(type: "text", nullable: true),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: true),
                    RawRequestJson = table.Column<string>(type: "jsonb", nullable: true),
                    RawResponseJson = table.Column<string>(type: "jsonb", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: true),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: true),
                    LatencyMs = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmInteractions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LlmInteractions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LlmPrompts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmPrompts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LlmPrompts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(name: "IX_LlmInteractions_CreatedAtUtc", table: "LlmInteractions", column: "CreatedAtUtc");
            migrationBuilder.CreateIndex(name: "IX_LlmInteractions_InputHash_Status", table: "LlmInteractions", columns: new[] { "InputHash", "Status" });
            migrationBuilder.CreateIndex(name: "IX_LlmInteractions_Scope_UserId_Day_CreatedAtUtc", table: "LlmInteractions", columns: new[] { "Scope", "UserId", "Day", "CreatedAtUtc" });
            migrationBuilder.CreateIndex(name: "IX_LlmInteractions_Scope_UserId_StartDay_EndDay_CreatedAtUtc", table: "LlmInteractions", columns: new[] { "Scope", "UserId", "StartDay", "EndDay", "CreatedAtUtc" });
            migrationBuilder.CreateIndex(name: "IX_LlmInteractions_UserId", table: "LlmInteractions", column: "UserId");
            migrationBuilder.CreateIndex(name: "IX_LlmPrompts_Key_Scope_UserId_IsActive", table: "LlmPrompts", columns: new[] { "Key", "Scope", "UserId", "IsActive" });
            migrationBuilder.CreateIndex(name: "IX_LlmPrompts_Key_Scope_UserId_Version", table: "LlmPrompts", columns: new[] { "Key", "Scope", "UserId", "Version" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_LlmPrompts_UserId", table: "LlmPrompts", column: "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LlmInteractions");
            migrationBuilder.DropTable(name: "LlmPrompts");
        }
    }
}
