using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202609020003_AddEquippedItemState")]
public sealed class AddEquippedItemState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "Equipped",
            table: "player_inventory_slots",
            type: "boolean",
            nullable: false,
            defaultValue: false);
        migrationBuilder.AddColumn<bool>(
            name: "Equipped",
            table: "player_pending_items",
            type: "boolean",
            nullable: false,
            defaultValue: false);
        migrationBuilder.AddColumn<bool>(
            name: "InputEquipped",
            table: "world_placed_objects",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Equipped", table: "player_inventory_slots");
        migrationBuilder.DropColumn(name: "Equipped", table: "player_pending_items");
        migrationBuilder.DropColumn(name: "InputEquipped", table: "world_placed_objects");
    }
}
