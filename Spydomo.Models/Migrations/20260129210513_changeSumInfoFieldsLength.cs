using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spydomo.Models.Migrations
{
    /// <inheritdoc />
    public partial class changeSumInfoFieldsLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "SummarizedInfoSignalTypes",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
    name: "Label",
    table: "SummarizedInfoSignalTypes",
    type: "nvarchar(64)",
    maxLength: 64,
    nullable: false,
    oldClrType: typeof(string),
    oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
    name: "OriginType",
    table: "SummarizedInfos",
    type: "nvarchar(32)",
    maxLength: 32,
    nullable: false,
    oldClrType: typeof(string),
    oldType: "nvarchar(max)");

            // Main window query index
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SummarizedInfos_CompanyStatusDate' AND object_id = OBJECT_ID('dbo.SummarizedInfos'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SummarizedInfos_CompanyStatusDate
    ON dbo.SummarizedInfos (CompanyId, ProcessingStatus, [Date])
    INCLUDE (Sentiment);
END
");

            // Signal types exists check
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SignalTypes_SummarizedInfoId_Label' AND object_id = OBJECT_ID('dbo.SummarizedInfoSignalTypes'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SignalTypes_SummarizedInfoId_Label
    ON dbo.SummarizedInfoSignalTypes (SummarizedInfoId, Label);
END
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SummarizedInfos_Complete_CompanyDate' AND object_id = OBJECT_ID('dbo.SummarizedInfos'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SummarizedInfos_Complete_CompanyDate
    ON dbo.SummarizedInfos (CompanyId, [Date])
    INCLUDE (Sentiment)
    WHERE ProcessingStatus = 7 AND [Date] IS NOT NULL;
END
");


        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "SummarizedInfoSignalTypes",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<string>(
                name: "Label",
                table: "SummarizedInfoSignalTypes",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "OriginType",
                table: "SummarizedInfos",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
