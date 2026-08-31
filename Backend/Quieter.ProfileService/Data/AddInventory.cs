using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202608290001_AddInventory")]
public sealed class AddInventory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte>(
            name: "SelectedHotbarIndex",
            table: "players",
            type: "smallint",
            nullable: false,
            defaultValue: (byte)0);

        migrationBuilder.CreateTable(
            name: "player_inventory_slots",
            columns: table => new
            {
                SteamId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                SlotIndex = table.Column<byte>(type: "smallint", nullable: false),
                ItemId = table.Column<ushort>(type: "integer", nullable: false),
                Quantity = table.Column<ushort>(type: "integer", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_player_inventory_slots", row => new { row.SteamId, row.SlotIndex });
                table.ForeignKey(
                    name: "FK_player_inventory_slots_players_SteamId",
                    column: row => row.SteamId,
                    principalTable: "players",
                    principalColumn: "SteamId",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "player_inventory_slots");
        migrationBuilder.DropColumn(name: "SelectedHotbarIndex", table: "players");
    }
}
