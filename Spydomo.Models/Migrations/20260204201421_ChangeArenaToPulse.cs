using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spydomo.Models.Migrations
{
    /// <inheritdoc />
    public partial class ChangeArenaToPulse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupArenaSnapshots");

            migrationBuilder.AlterColumn<string>(
                name: "OriginType",
                table: "SummarizedInfos",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "OriginType",
                table: "RawContents",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateTable(
                name: "GroupSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    GroupSlug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TimeWindowDays = table.Column<int>(type: "int", nullable: false, defaultValue: 30),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "Pulse"),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupSnapshots_CompanyGroups",
                        column: x => x.GroupId,
                        principalTable: "CompanyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupSnapshots_GroupId_Kind_Window_Generated",
                table: "GroupSnapshots",
                columns: new[] { "GroupId", "Kind", "TimeWindowDays", "GeneratedAtUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupSnapshots_GroupSlug",
                table: "GroupSnapshots",
                column: "GroupSlug");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupSnapshots");

            migrationBuilder.AlterColumn<int>(
                name: "OriginType",
                table: "SummarizedInfos",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "OriginType",
                table: "RawContents",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.CreateTable(
                name: "GroupArenaSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GroupSlug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    TimeWindowDays = table.Column<int>(type: "int", nullable: false, defaultValue: 30)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupArenaSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupArenaSnapshots_CompanyGroups",
                        column: x => x.GroupId,
                        principalTable: "CompanyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupArenaSnapshots_GroupId",
                table: "GroupArenaSnapshots",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupArenaSnapshots_GroupSlug_TimeWindowDays",
                table: "GroupArenaSnapshots",
                columns: new[] { "GroupSlug", "TimeWindowDays" });
        }
    }
}
