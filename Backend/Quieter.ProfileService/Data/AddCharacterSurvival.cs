using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202609020001_AddCharacterSurvival")]
public sealed class AddCharacterSurvival : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        AddItemStateColumns(migrationBuilder, "player_inventory_slots", string.Empty);
        AddItemStateColumns(migrationBuilder, "player_pending_items", string.Empty);
        AddItemStateColumns(migrationBuilder, "world_placed_objects", "Input");
        migrationBuilder.CreateTable(
            name: "characters",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                SurvivalJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                LifeState = table.Column<byte>(type: "smallint", nullable: false),
                DeathCause = table.Column<byte>(type: "smallint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_characters", value => value.CharacterId));

        migrationBuilder.AddColumn<Guid>(
            name: "CurrentCharacterId",
            table: "players",
            type: "uuid",
            nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_players_CurrentCharacterId",
            table: "players",
            column: "CurrentCharacterId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_characters_UpdatedAtUtc",
            table: "characters",
            column: "UpdatedAtUtc");
        migrationBuilder.AddForeignKey(
            name: "FK_players_characters_CurrentCharacterId",
            table: "players",
            column: "CurrentCharacterId",
            principalTable: "characters",
            principalColumn: "CharacterId",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_players_characters_CurrentCharacterId",
            table: "players");
        migrationBuilder.DropIndex(
            name: "IX_players_CurrentCharacterId",
            table: "players");
        migrationBuilder.DropColumn(name: "CurrentCharacterId", table: "players");
        migrationBuilder.DropTable(name: "characters");
        DropItemStateColumns(migrationBuilder, "player_inventory_slots", string.Empty);
        DropItemStateColumns(migrationBuilder, "player_pending_items", string.Empty);
        DropItemStateColumns(migrationBuilder, "world_placed_objects", "Input");
    }

    private static void AddItemStateColumns(
        MigrationBuilder migrationBuilder,
        string table,
        string prefix)
    {
        migrationBuilder.AddColumn<decimal>(
            name: prefix + "ItemInstanceId",
            table: table,
            type: "numeric(20,0)",
            nullable: true);
        migrationBuilder.AddColumn<ushort>(
            name: prefix + "Freshness",
            table: table,
            type: "integer",
            nullable: false,
            defaultValue: (ushort)10000);
        migrationBuilder.AddColumn<ushort>(
            name: prefix + "BiologicalContamination",
            table: table,
            type: "integer",
            nullable: false,
            defaultValue: (ushort)0);
        migrationBuilder.AddColumn<ushort>(
            name: prefix + "ToxinContamination",
            table: table,
            type: "integer",
            nullable: false,
            defaultValue: (ushort)0);
        migrationBuilder.AddColumn<ushort>(
            name: prefix + "Wetness",
            table: table,
            type: "integer",
            nullable: false,
            defaultValue: (ushort)0);
        migrationBuilder.AddColumn<ushort>(
            name: prefix + "Cleanliness",
            table: table,
            type: "integer",
            nullable: false,
            defaultValue: (ushort)10000);
        migrationBuilder.AddColumn<ushort>(
            name: prefix + "LiquidMilliliters",
            table: table,
            type: "integer",
            nullable: false,
            defaultValue: (ushort)0);
        migrationBuilder.AddColumn<byte>(
            name: prefix + "LiquidKind",
            table: table,
            type: "smallint",
            nullable: false,
            defaultValue: (byte)0);
    }

    private static void DropItemStateColumns(
        MigrationBuilder migrationBuilder,
        string table,
        string prefix)
    {
        migrationBuilder.DropColumn(name: prefix + "ItemInstanceId", table: table);
        migrationBuilder.DropColumn(name: prefix + "Freshness", table: table);
        migrationBuilder.DropColumn(name: prefix + "BiologicalContamination", table: table);
        migrationBuilder.DropColumn(name: prefix + "ToxinContamination", table: table);
        migrationBuilder.DropColumn(name: prefix + "Wetness", table: table);
        migrationBuilder.DropColumn(name: prefix + "Cleanliness", table: table);
        migrationBuilder.DropColumn(name: prefix + "LiquidMilliliters", table: table);
        migrationBuilder.DropColumn(name: prefix + "LiquidKind", table: table);
    }
}
