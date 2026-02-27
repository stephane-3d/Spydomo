using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spydomo.Models.Migrations
{
    /// <inheritdoc />
    public partial class removeThemeStatsCGid2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ThemeStats_CompanyGroups_CompanyGroupId",
                table: "ThemeStats");

            migrationBuilder.DropIndex(
                name: "IX_ThemeStats_ClientId",
                table: "ThemeStats");

            migrationBuilder.DropIndex(
                name: "IX_ThemeStats_CompanyGroupId",
                table: "ThemeStats");

            migrationBuilder.DropColumn(
                name: "CompanyGroupId",
                table: "ThemeStats");

            migrationBuilder.CreateIndex(
                name: "IX_ThemeStats_ClientId_CompanyId_ThemeId_PeriodType_StartDate_EndDate",
                table: "ThemeStats",
                columns: new[] { "ClientId", "CompanyId", "ThemeId", "PeriodType", "StartDate", "EndDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ThemeStats_ClientId_CompanyId_ThemeId_PeriodType_StartDate_EndDate",
                table: "ThemeStats");

            migrationBuilder.AddColumn<int>(
                name: "CompanyGroupId",
                table: "ThemeStats",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThemeStats_ClientId",
                table: "ThemeStats",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ThemeStats_CompanyGroupId",
                table: "ThemeStats",
                column: "CompanyGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_ThemeStats_CompanyGroups_CompanyGroupId",
                table: "ThemeStats",
                column: "CompanyGroupId",
                principalTable: "CompanyGroups",
                principalColumn: "Id");
        }
    }
}
