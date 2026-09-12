using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202609120001_AddSleepNoiseBurden")]
public sealed class AddSleepNoiseBurden : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<float>(
            name: "SleepNoiseBurden",
            table: "character_physiology",
            type: "real",
            nullable: false,
            defaultValue: 0f);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SleepNoiseBurden",
            table: "character_physiology");
    }
}
