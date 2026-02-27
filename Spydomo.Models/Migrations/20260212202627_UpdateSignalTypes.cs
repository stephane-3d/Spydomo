using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spydomo.Models.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSignalTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Label",
                table: "SummarizedInfoSignalTypes");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastCompanyDataUpdate",
                table: "Companies",
                type: "datetime2",
                nullable: true);

            
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SummarizedInfoSignalTypes_SignalTypes_SignalTypeId",
                table: "SummarizedInfoSignalTypes");

            migrationBuilder.DropTable(
                name: "SignalTypes");

            migrationBuilder.DropIndex(
                name: "IX_SummarizedInfoSignalTypes_SignalTypeId",
                table: "SummarizedInfoSignalTypes");

            migrationBuilder.DropColumn(
                name: "SignalTypeId",
                table: "SummarizedInfoSignalTypes");

            migrationBuilder.DropColumn(
                name: "LastCompanyDataUpdate",
                table: "Companies");

            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "SummarizedInfoSignalTypes",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }
    }
}
