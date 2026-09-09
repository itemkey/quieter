using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202609080002_AddBedAssignments")]
public sealed class AddBedAssignments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            "AssignedCharacterId", "world_placed_objects", type: "uuid", nullable: true);
        migrationBuilder.CreateIndex(
            "IX_world_placed_objects_AssignedCharacterId",
            "world_placed_objects", "AssignedCharacterId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            "IX_world_placed_objects_AssignedCharacterId", "world_placed_objects");
        migrationBuilder.DropColumn("AssignedCharacterId", "world_placed_objects");
    }
}
