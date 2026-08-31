using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202608310001_AddNonOreAndMapNotes")]
public sealed class AddNonOreAndMapNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "UPDATE worlds SET \"GeneratorVersion\" = 4 WHERE \"GeneratorVersion\" < 4;");
        migrationBuilder.CreateTable(
            name: "player_map_notes",
            columns: table => new
            {
                SteamId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                WorldId = table.Column<int>(type: "integer", nullable: false),
                NoteId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                X = table.Column<float>(type: "real", nullable: false),
                Z = table.Column<float>(type: "real", nullable: false),
                Text = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_player_map_notes", value => new
                {
                    value.SteamId,
                    value.WorldId,
                    value.NoteId,
                });
                table.ForeignKey(
                    name: "FK_player_map_notes_players_SteamId",
                    column: value => value.SteamId,
                    principalTable: "players",
                    principalColumn: "SteamId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_player_map_notes_worlds_WorldId",
                    column: value => value.WorldId,
                    principalTable: "worlds",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(
            name: "IX_player_map_notes_WorldId",
            table: "player_map_notes",
            column: "WorldId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "player_map_notes");
        migrationBuilder.Sql(
            "UPDATE worlds SET \"GeneratorVersion\" = 3 WHERE \"GeneratorVersion\" = 4;");
    }
}
