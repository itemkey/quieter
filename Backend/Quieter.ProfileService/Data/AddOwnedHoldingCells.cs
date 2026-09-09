using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202609080001_AddOwnedHoldingCells")]
public sealed class AddOwnedHoldingCells : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            "OwnerAccountId", "world_placed_objects", type: "numeric(20,0)", nullable: true);
        migrationBuilder.AddColumn<bool>(
            "Locked", "world_placed_objects", type: "boolean", nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("OwnerAccountId", "world_placed_objects");
        migrationBuilder.DropColumn("Locked", "world_placed_objects");
    }
}
