using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202609020002_MakeMapNotesPhysical")]
public sealed class MakeMapNotesPhysical : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_player_map_notes_players_SteamId",
            table: "player_map_notes");
        migrationBuilder.DropPrimaryKey(
            name: "PK_player_map_notes",
            table: "player_map_notes");
        migrationBuilder.AddColumn<decimal>(
            name: "MapItemInstanceId",
            table: "player_map_notes",
            type: "numeric(20,0)",
            nullable: false,
            defaultValue: 0m);
        migrationBuilder.AddPrimaryKey(
            name: "PK_player_map_notes",
            table: "player_map_notes",
            columns: new[] { "WorldId", "MapItemInstanceId", "NoteId" });
        migrationBuilder.CreateIndex(
            name: "IX_player_map_notes_SteamId",
            table: "player_map_notes",
            column: "SteamId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_player_map_notes_SteamId",
            table: "player_map_notes");
        migrationBuilder.DropPrimaryKey(
            name: "PK_player_map_notes",
            table: "player_map_notes");
        migrationBuilder.DropColumn(
            name: "MapItemInstanceId",
            table: "player_map_notes");
        migrationBuilder.AddPrimaryKey(
            name: "PK_player_map_notes",
            table: "player_map_notes",
            columns: new[] { "SteamId", "WorldId", "NoteId" });
        migrationBuilder.AddForeignKey(
            name: "FK_player_map_notes_players_SteamId",
            table: "player_map_notes",
            column: "SteamId",
            principalTable: "players",
            principalColumn: "SteamId",
            onDelete: ReferentialAction.Cascade);
    }
}
