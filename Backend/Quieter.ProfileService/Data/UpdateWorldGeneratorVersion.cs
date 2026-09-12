using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202608280001_UpdateWorldGeneratorVersion")]
public sealed class UpdateWorldGeneratorVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Keep historical migrations deterministic. Later generator bumps have
        // their own migrations or an explicit, backed-up world reset.
        migrationBuilder.Sql(
            "UPDATE worlds SET \"GeneratorVersion\" = 2 WHERE \"Id\" = 1;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "UPDATE worlds SET \"GeneratorVersion\" = 1 WHERE \"Id\" = 1;");
    }
}
