using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202608280001_UpdateWorldGeneratorVersion")]
public sealed class UpdateWorldGeneratorVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.UpdateData(
            table: "worlds",
            keyColumn: "Id",
            keyValue: 1,
            column: "GeneratorVersion",
            value: (int)ProfileStore.CurrentGeneratorVersion);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.UpdateData(
            table: "worlds",
            keyColumn: "Id",
            keyValue: 1,
            column: "GeneratorVersion",
            value: 1);
    }
}
