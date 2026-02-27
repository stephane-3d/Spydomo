using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spydomo.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategicSummaryStateAndSourceKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StrategicSummaries_CompanyGroupId",
                table: "StrategicSummaries");

            migrationBuilder.AlterColumn<string>(
                name: "PeriodType",
                table: "StrategicSummaries",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "StrategicSummaries",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompanyGroupStrategicSummaryStates",
                columns: table => new
                {
                    CompanyGroupId = table.Column<int>(type: "int", nullable: false),
                    LastProcessedSummarizedInfoId = table.Column<int>(type: "int", nullable: false),
                    LastRunUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyGroupStrategicSummaryStates", x => x.CompanyGroupId);
                    table.ForeignKey(
                        name: "FK_CompanyGroupStrategicSummaryStates_CompanyGroups_CompanyGroupId",
                        column: x => x.CompanyGroupId,
                        principalTable: "CompanyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StrategicSummaries_CompanyGroupId_PeriodType_SourceKey",
                table: "StrategicSummaries",
                columns: new[] { "CompanyGroupId", "PeriodType", "SourceKey" },
                unique: true,
                filter: "[SourceKey] IS NOT NULL AND [SourceKey] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyGroupStrategicSummaryStates");

            migrationBuilder.DropIndex(
                name: "IX_StrategicSummaries_CompanyGroupId_PeriodType_SourceKey",
                table: "StrategicSummaries");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "StrategicSummaries");

            migrationBuilder.AlterColumn<string>(
                name: "PeriodType",
                table: "StrategicSummaries",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_StrategicSummaries_CompanyGroupId",
                table: "StrategicSummaries",
                column: "CompanyGroupId");
        }
    }
}
