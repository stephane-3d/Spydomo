using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spydomo.Models.Migrations
{
    /// <inheritdoc />
    public partial class CompanyRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyRelationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<int>(type: "int", nullable: false),
                    Query = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PromptVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RawResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ParsedCandidatesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyRelationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyRelationRuns_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanyRelations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    RelatedCompanyId = table.Column<int>(type: "int", nullable: true),
                    RelatedCompanyNameRaw = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RelatedCompanyUrlRaw = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RelatedDomain = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RelationType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(4,3)", nullable: false),
                    EvidenceCount = table.Column<int>(type: "int", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RunId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyRelations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyRelations_Companies_RelatedCompanyId",
                        column: x => x.RelatedCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyRelations_CompanyRelationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "CompanyRelationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRelationRuns_CompanyId_CreatedAt",
                table: "CompanyRelationRuns",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRelations_CompanyId_RelatedCompanyId_RelationType",
                table: "CompanyRelations",
                columns: new[] { "CompanyId", "RelatedCompanyId", "RelationType" },
                unique: true,
                filter: "[RelatedCompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRelations_CompanyId_RelatedDomain_RelationType",
                table: "CompanyRelations",
                columns: new[] { "CompanyId", "RelatedDomain", "RelationType" },
                filter: "[RelatedCompanyId] IS NULL AND [RelatedDomain] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRelations_CompanyId_Status_RelationType_LastSeenAt",
                table: "CompanyRelations",
                columns: new[] { "CompanyId", "Status", "RelationType", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRelations_RelatedCompanyId",
                table: "CompanyRelations",
                column: "RelatedCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRelations_RunId",
                table: "CompanyRelations",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyRelations");

            migrationBuilder.DropTable(
                name: "CompanyRelationRuns");
        }
    }
}
