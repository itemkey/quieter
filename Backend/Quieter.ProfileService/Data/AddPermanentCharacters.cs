using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202609070002_AddPermanentCharacters")]
public sealed class AddPermanentCharacters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<float>("PositionX", "characters", type: "real", nullable: false, defaultValue: 0f);
        migrationBuilder.AddColumn<float>("PositionY", "characters", type: "real", nullable: false, defaultValue: 0f);
        migrationBuilder.AddColumn<float>("PositionZ", "characters", type: "real", nullable: false, defaultValue: 0f);
        migrationBuilder.AddColumn<byte>("SelectedHotbarIndex", "characters", type: "smallint", nullable: false, defaultValue: (byte)0);
        migrationBuilder.AddColumn<DateTime>("DiedAtUtc", "characters", type: "timestamp with time zone", nullable: true);
        migrationBuilder.Sql("""
            UPDATE characters c SET "PositionX" = p."PositionX", "PositionY" = p."PositionY",
                "PositionZ" = p."PositionZ", "SelectedHotbarIndex" = p."SelectedHotbarIndex"
            FROM players p WHERE p."CurrentCharacterId" = c."CharacterId";
            UPDATE characters SET "DiedAtUtc" = "UpdatedAtUtc" WHERE "LifeState" = 4;
            """);
        migrationBuilder.CreateTable("character_replacements", table => new
        {
            OperationId = table.Column<Guid>(type: "uuid", nullable: false),
            SteamId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
            PreviousCharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            NewCharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_character_replacements", entry => entry.OperationId);
            table.ForeignKey("FK_character_replacements_players_SteamId", entry => entry.SteamId,
                "players", "SteamId", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_character_replacements_PreviousCharacterId",
            "character_replacements", "PreviousCharacterId", unique: true);
        migrationBuilder.CreateIndex("IX_character_replacements_SteamId", "character_replacements", "SteamId");
        migrationBuilder.CreateTable("character_transfers", table => new
        {
            OperationId = table.Column<Guid>(type: "uuid", nullable: false),
            SourceCharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            DestinationCharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
        }, constraints: table => table.PrimaryKey("PK_character_transfers", entry => entry.OperationId));
        migrationBuilder.CreateIndex("IX_character_transfers_CreatedAtUtc", "character_transfers", "CreatedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException(
        "Permanent bodies and life transitions cannot be downgraded safely. Restore a verified backup.");
}
