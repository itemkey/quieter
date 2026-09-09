using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202609080003_AddInheritance")]
public sealed class AddInheritance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            "RegisteredHeirCharacterId", "players", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            "HeirRegisteredAtUtc", "players", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<long>(
            "EstateRevision", "players", type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.CreateIndex(
            "IX_players_RegisteredHeirCharacterId", "players", "RegisteredHeirCharacterId");
        migrationBuilder.CreateTable("inheritance_transitions", table => new
        {
            OperationId = table.Column<Guid>(type: "uuid", nullable: false),
            SteamId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
            DeceasedCharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            HeirCharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(
                type: "timestamp with time zone", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_inheritance_transitions", entry => entry.OperationId);
            table.ForeignKey("FK_inheritance_transitions_players_SteamId", entry => entry.SteamId,
                "players", "SteamId", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_inheritance_transitions_DeceasedCharacterId",
            "inheritance_transitions", "DeceasedCharacterId", unique: true);
        migrationBuilder.CreateIndex("IX_inheritance_transitions_SteamId",
            "inheritance_transitions", "SteamId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException(
        "Inheritance transitions cannot be downgraded safely. Restore a verified backup.");
}
