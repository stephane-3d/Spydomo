using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spydomo.Models.Migrations
{
    /// <inheritdoc />
    public partial class RemoveThemeStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ThemeStats");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ThemeStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    ThemeId = table.Column<int>(type: "int", nullable: false),
                    AverageSignalScore = table.Column<float>(type: "real", nullable: true),
                    AvgEngagementScore = table.Column<double>(type: "float", nullable: false),
                    CompetitorMentionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MentionsCount = table.Column<int>(type: "int", nullable: false),
                    PeriodType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PrevMentionsCount = table.Column<int>(type: "int", nullable: false),
                    SentimentNegativeCount = table.Column<int>(type: "int", nullable: false),
                    SentimentPositiveCount = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TopTagsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalEngagementScore = table.Column<int>(type: "int", nullable: false),
                    TotalSignalScore = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThemeStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThemeStats_CanonicalThemes_ThemeId",
                        column: x => x.ThemeId,
                        principalTable: "CanonicalThemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ThemeStats_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ThemeStats_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ThemeStats_ClientId_CompanyId_ThemeId_PeriodType_StartDate_EndDate",
                table: "ThemeStats",
                columns: new[] { "ClientId", "CompanyId", "ThemeId", "PeriodType", "StartDate", "EndDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThemeStats_CompanyId",
                table: "ThemeStats",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ThemeStats_ThemeId",
                table: "ThemeStats",
                column: "ThemeId");
        }
    }
}
