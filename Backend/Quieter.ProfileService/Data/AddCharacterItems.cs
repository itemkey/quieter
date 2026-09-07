using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202609070001_AddCharacterItems")]
public sealed class AddCharacterItems : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_items",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                StorageArea = table.Column<byte>(type: "smallint", nullable: false),
                SlotIndex = table.Column<ushort>(type: "integer", nullable: false),
                ItemId = table.Column<ushort>(type: "integer", nullable: false),
                Quantity = table.Column<ushort>(type: "integer", nullable: false),
                Condition = table.Column<ushort>(type: "integer", nullable: false),
                Quality = table.Column<byte>(type: "smallint", nullable: false),
                HiddenItemId = table.Column<ushort>(type: "integer", nullable: false),
                SourceNodeId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                RevealAtPercent = table.Column<byte>(type: "smallint", nullable: false),
                SampleId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                ItemInstanceId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                Freshness = table.Column<ushort>(type: "integer", nullable: false),
                BiologicalContamination = table.Column<ushort>(type: "integer", nullable: false),
                ToxinContamination = table.Column<ushort>(type: "integer", nullable: false),
                Wetness = table.Column<ushort>(type: "integer", nullable: false),
                Cleanliness = table.Column<ushort>(type: "integer", nullable: false),
                LiquidMilliliters = table.Column<ushort>(type: "integer", nullable: false),
                LiquidKind = table.Column<byte>(type: "smallint", nullable: false),
                Equipped = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_items", item => new { item.CharacterId, item.StorageArea, item.SlotIndex });
                table.ForeignKey("FK_character_items_characters_CharacterId", item => item.CharacterId,
                    "characters", "CharacterId", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_character_items_ItemInstanceId", "character_items", "ItemInstanceId");
        // Legacy account rows are moved, with their original item identities, in
        // the login transaction. This also handles profiles without a character yet.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "Character-owned items cannot safely be downgraded to account inventory. Restore a verified backup.");
    }
}
