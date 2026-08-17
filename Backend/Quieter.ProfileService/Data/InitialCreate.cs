using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202608160001_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "players",
            columns: table => new
            {
                SteamId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                DisplayName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                PositionX = table.Column<float>(type: "real", nullable: false),
                PositionY = table.Column<float>(type: "real", nullable: false),
                PositionZ = table.Column<float>(type: "real", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_players", row => row.SteamId));

        migrationBuilder.CreateTable(
            name: "worlds",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false),
                Seed = table.Column<long>(type: "bigint", nullable: false),
                GeneratorVersion = table.Column<int>(type: "integer", nullable: false),
                ChunkCountX = table.Column<int>(type: "integer", nullable: false),
                ChunkCountZ = table.Column<int>(type: "integer", nullable: false),
                ChunkSize = table.Column<int>(type: "integer", nullable: false),
                SamplesPerSide = table.Column<int>(type: "integer", nullable: false),
                HeightStep = table.Column<float>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_worlds", row => row.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "players");
        migrationBuilder.DropTable(name: "worlds");
    }
}
