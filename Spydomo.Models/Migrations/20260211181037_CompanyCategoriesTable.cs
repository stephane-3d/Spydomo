using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spydomo.Models.Migrations
{
    /// <inheritdoc />
    public partial class CompanyCategoriesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrimaryCategory",
                table: "Companies");

            
            migrationBuilder.CreateIndex(
                name: "IX_CompanyCategories_Slug",
                table: "CompanyCategories",
                column: "Slug",
                unique: true);

            
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_CompanyCategories_PrimaryCategoryId",
                table: "Companies");

            migrationBuilder.DropTable(
                name: "CompanyCategories");

            migrationBuilder.DropIndex(
                name: "IX_Companies_PrimaryCategoryId",
                table: "Companies");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryCategory",
                table: "Companies",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            
        }
    }
}
