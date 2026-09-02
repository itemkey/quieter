using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202609010001_AddResearchTables")]
public sealed class AddResearchTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "UPDATE worlds SET \"GeneratorVersion\" = 5 WHERE \"GeneratorVersion\" < 5;");
        migrationBuilder.AddColumn<decimal>(
            name: "SampleId",
            table: "player_inventory_slots",
            type: "numeric(20,0)",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "player_pending_items",
            columns: table => new
            {
                SteamId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                ItemIndex = table.Column<ushort>(type: "integer", nullable: false),
                ItemId = table.Column<ushort>(type: "integer", nullable: false),
                Quantity = table.Column<ushort>(type: "integer", nullable: false),
                Condition = table.Column<ushort>(type: "integer", nullable: false),
                Quality = table.Column<byte>(type: "smallint", nullable: false),
                HiddenItemId = table.Column<ushort>(type: "integer", nullable: false),
                SourceNodeId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                RevealAtPercent = table.Column<byte>(type: "smallint", nullable: false),
                SampleId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_player_pending_items", value => new
                {
                    value.SteamId,
                    value.ItemIndex,
                });
                table.ForeignKey(
                    name: "FK_player_pending_items_players_SteamId",
                    column: value => value.SteamId,
                    principalTable: "players",
                    principalColumn: "SteamId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "world_placed_objects",
            columns: table => new
            {
                WorldId = table.Column<int>(type: "integer", nullable: false),
                ObjectId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                ItemId = table.Column<ushort>(type: "integer", nullable: false),
                X = table.Column<float>(type: "real", nullable: false),
                Y = table.Column<float>(type: "real", nullable: false),
                Z = table.Column<float>(type: "real", nullable: false),
                Yaw = table.Column<float>(type: "real", nullable: false),
                InputItemId = table.Column<ushort>(type: "integer", nullable: false),
                InputQuantity = table.Column<ushort>(type: "integer", nullable: false),
                InputCondition = table.Column<ushort>(type: "integer", nullable: false),
                InputQuality = table.Column<byte>(type: "smallint", nullable: false),
                InputHiddenItemId = table.Column<ushort>(type: "integer", nullable: false),
                InputSourceNodeId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                InputRevealAtPercent = table.Column<byte>(type: "smallint", nullable: false),
                InputSampleId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_world_placed_objects", value => new
                {
                    value.WorldId,
                    value.ObjectId,
                });
                table.ForeignKey(
                    name: "FK_world_placed_objects_worlds_WorldId",
                    column: value => value.WorldId,
                    principalTable: "worlds",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "player_pending_items");
        migrationBuilder.DropTable(name: "world_placed_objects");
        migrationBuilder.DropColumn(name: "SampleId", table: "player_inventory_slots");
        migrationBuilder.Sql(
            "UPDATE worlds SET \"GeneratorVersion\" = 4 WHERE \"GeneratorVersion\" = 5;");
    }
}
