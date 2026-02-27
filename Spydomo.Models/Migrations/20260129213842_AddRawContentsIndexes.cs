using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spydomo.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddRawContentsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE INDEX IX_RawContents_Company_PostedDate
ON dbo.RawContents (CompanyId, PostedDate)
INCLUDE (DataSourceTypeId, CreatedAt, EngagementScore);
");

            migrationBuilder.Sql(@"
CREATE INDEX IX_RawContents_Company_CreatedAt
ON dbo.RawContents (CompanyId, CreatedAt)
INCLUDE (DataSourceTypeId, PostedDate, EngagementScore);
");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
