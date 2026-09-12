using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202609100001_MoveDepositKnowledgeToCharacters")]
public sealed class MoveDepositKnowledgeToCharacters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_deposit_knowledge",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                WorldId = table.Column<int>(type: "integer", nullable: false),
                InstanceId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                StudyBasisPoints = table.Column<ushort>(type: "integer", nullable: false),
                DiscoveredAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_deposit_knowledge",
                    entry => new { entry.CharacterId, entry.WorldId, entry.InstanceId });
                table.ForeignKey(
                    name: "FK_character_deposit_knowledge_characters_CharacterId",
                    column: entry => entry.CharacterId,
                    principalTable: "characters",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_character_deposit_knowledge_worlds_WorldId",
                    column: entry => entry.WorldId,
                    principalTable: "worlds",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(
            name: "IX_character_deposit_knowledge_WorldId",
            table: "character_deposit_knowledge",
            column: "WorldId");
        migrationBuilder.Sql("""
            INSERT INTO character_deposit_knowledge
                ("CharacterId", "WorldId", "InstanceId", "StudyBasisPoints",
                 "DiscoveredAtUtc", "UpdatedAtUtc")
            SELECT p."CurrentCharacterId", k."WorldId", k."InstanceId",
                k."StudyBasisPoints", k."DiscoveredAtUtc", k."UpdatedAtUtc"
            FROM player_deposit_knowledge k
            JOIN players p ON p."SteamId" = k."SteamId"
            WHERE p."CurrentCharacterId" IS NOT NULL
            ON CONFLICT ("CharacterId", "WorldId", "InstanceId") DO UPDATE SET
                "StudyBasisPoints" = EXCLUDED."StudyBasisPoints",
                "DiscoveredAtUtc" = EXCLUDED."DiscoveredAtUtc",
                "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc";
            """);
        migrationBuilder.DropTable(name: "player_deposit_knowledge");
        migrationBuilder.RenameColumn(
            name: "SteamId",
            table: "player_map_notes",
            newName: "LastEditorSteamId");
        migrationBuilder.RenameIndex(
            name: "IX_player_map_notes_SteamId",
            table: "player_map_notes",
            newName: "IX_player_map_notes_LastEditorSteamId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException(
        "Character-bound memory cannot be downgraded safely. Restore a verified backup.");
}
