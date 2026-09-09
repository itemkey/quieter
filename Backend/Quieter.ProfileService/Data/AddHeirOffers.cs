using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202609090001_AddHeirOffers")]
public sealed class AddHeirOffers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("heir_offers", table => new
        {
            OfferId = table.Column<Guid>(type: "uuid", nullable: false),
            OperationId = table.Column<Guid>(type: "uuid", nullable: false),
            DonorSteamId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
            RecipientSteamId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
            DeceasedCharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            HeirCharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            OfferedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            AcceptanceStartedAtUtc = table.Column<DateTime>(
                type: "timestamp with time zone", nullable: true),
            HardExpiresAtUtc = table.Column<DateTime>(
                type: "timestamp with time zone", nullable: false),
            AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            Status = table.Column<byte>(type: "smallint", nullable: false),
        }, constraints: table => table.PrimaryKey("PK_heir_offers", entry => entry.OfferId));
        migrationBuilder.CreateIndex("IX_heir_offers_OperationId", "heir_offers", "OperationId",
            unique: true);
        migrationBuilder.CreateIndex("IX_heir_offers_RecipientSteamId_Status", "heir_offers",
            new[] { "RecipientSteamId", "Status" });
        migrationBuilder.CreateIndex("IX_heir_offers_HeirCharacterId", "heir_offers",
            "HeirCharacterId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException(
        "Heir offers cannot be downgraded safely. Restore a verified backup.");
}
