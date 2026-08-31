using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202608300001_AddResourceExtraction")]
public sealed class AddResourceExtraction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "UPDATE worlds SET \"GeneratorVersion\" = 3 WHERE \"GeneratorVersion\" < 3;");

        migrationBuilder.AddColumn<ushort>(
            name: "Condition",
            table: "player_inventory_slots",
            type: "integer",
            nullable: false,
            defaultValue: (ushort)0);
        migrationBuilder.AddColumn<byte>(
            name: "Quality",
            table: "player_inventory_slots",
            type: "smallint",
            nullable: false,
            defaultValue: (byte)0);
        migrationBuilder.AddColumn<ushort>(
            name: "HiddenItemId",
            table: "player_inventory_slots",
            type: "integer",
            nullable: false,
            defaultValue: (ushort)0);
        migrationBuilder.AddColumn<decimal>(
            name: "SourceNodeId",
            table: "player_inventory_slots",
            type: "numeric(20,0)",
            nullable: true);
        migrationBuilder.AddColumn<byte>(
            name: "RevealAtPercent",
            table: "player_inventory_slots",
            type: "smallint",
            nullable: false,
            defaultValue: (byte)0);

        migrationBuilder.CreateTable(
            name: "world_resource_nodes",
            columns: table => new
            {
                WorldId = table.Column<int>(type: "integer", nullable: false),
                InstanceId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                RemainingReserves = table.Column<ushort>(type: "integer", nullable: false),
                AvailableAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_world_resource_nodes", row => new { row.WorldId, row.InstanceId });
                table.ForeignKey(
                    name: "FK_world_resource_nodes_worlds_WorldId",
                    column: row => row.WorldId,
                    principalTable: "worlds",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "player_deposit_knowledge",
            columns: table => new
            {
                SteamId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                WorldId = table.Column<int>(type: "integer", nullable: false),
                InstanceId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                StudyBasisPoints = table.Column<ushort>(type: "integer", nullable: false),
                DiscoveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_player_deposit_knowledge",
                    row => new { row.SteamId, row.WorldId, row.InstanceId });
                table.ForeignKey(
                    name: "FK_player_deposit_knowledge_players_SteamId",
                    column: row => row.SteamId,
                    principalTable: "players",
                    principalColumn: "SteamId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_player_deposit_knowledge_worlds_WorldId",
                    column: row => row.WorldId,
                    principalTable: "worlds",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_player_deposit_knowledge_WorldId",
            table: "player_deposit_knowledge",
            column: "WorldId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "player_deposit_knowledge");
        migrationBuilder.DropTable(name: "world_resource_nodes");
        migrationBuilder.DropColumn(name: "Condition", table: "player_inventory_slots");
        migrationBuilder.DropColumn(name: "Quality", table: "player_inventory_slots");
        migrationBuilder.DropColumn(name: "HiddenItemId", table: "player_inventory_slots");
        migrationBuilder.DropColumn(name: "SourceNodeId", table: "player_inventory_slots");
        migrationBuilder.DropColumn(name: "RevealAtPercent", table: "player_inventory_slots");
        migrationBuilder.Sql(
            "UPDATE worlds SET \"GeneratorVersion\" = 2 WHERE \"GeneratorVersion\" = 3;");
    }
}
