using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spydomo.Models.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCompanyStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyStats");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyGroupId = table.Column<int>(type: "int", nullable: true),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    AvgEngagementScore = table.Column<double>(type: "float", nullable: false),
                    CompanyPostCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NegativeCount = table.Column<int>(type: "int", nullable: false),
                    NeutralCount = table.Column<int>(type: "int", nullable: false),
                    PeriodType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PositiveCount = table.Column<int>(type: "int", nullable: false),
                    PrevAvgEngagementScore = table.Column<double>(type: "float", nullable: false),
                    PrevCompanyPostCount = table.Column<int>(type: "int", nullable: false),
                    PrevNegativeCount = table.Column<int>(type: "int", nullable: false),
                    PrevPositiveCount = table.Column<int>(type: "int", nullable: false),
                    PrevUserMentionCount = table.Column<int>(type: "int", nullable: false),
                    SourceBreakdownJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TopTagsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TopThemesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserMentionCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyStats_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompanyStats_CompanyGroups_CompanyGroupId",
                        column: x => x.CompanyGroupId,
                        principalTable: "CompanyGroups",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyStats_CompanyGroupId",
                table: "CompanyStats",
                column: "CompanyGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyStats_CompanyId",
                table: "CompanyStats",
                column: "CompanyId");
        }
    }
}
