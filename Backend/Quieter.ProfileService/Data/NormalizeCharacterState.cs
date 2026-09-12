using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quieter.ProfileService.Data;

[DbContext(typeof(ProfileDbContext))]
[Migration("202609100002_NormalizeCharacterState")]
public sealed class NormalizeCharacterState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("character_physiology", table => new
        {
            CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            AcuteStamina = table.Column<float>(type: "real", nullable: false),
            Oxygenation = table.Column<float>(type: "real", nullable: false),
            BloodVolume = table.Column<float>(type: "real", nullable: false),
            Hydration = table.Column<float>(type: "real", nullable: false),
            ElectrolyteBalance = table.Column<float>(type: "real", nullable: false),
            EnergyReserve = table.Column<float>(type: "real", nullable: false),
            ProteinReserve = table.Column<float>(type: "real", nullable: false),
            FatReserve = table.Column<float>(type: "real", nullable: false),
            MicronutrientReserve = table.Column<float>(type: "real", nullable: false),
            MineralReserve = table.Column<float>(type: "real", nullable: false),
            SleepDebt = table.Column<float>(type: "real", nullable: false),
            CircadianFatigue = table.Column<float>(type: "real", nullable: false),
            CoreTemperatureC = table.Column<float>(type: "real", nullable: false),
            Pain = table.Column<float>(type: "real", nullable: false),
            Stress = table.Column<float>(type: "real", nullable: false),
            Consciousness = table.Column<float>(type: "real", nullable: false),
            SystemicInfection = table.Column<float>(type: "real", nullable: false),
            ToxinLoad = table.Column<float>(type: "real", nullable: false),
            FoodborneInfection = table.Column<float>(type: "real", nullable: false),
            WaterborneInfection = table.Column<float>(type: "real", nullable: false),
            ParasiteLoad = table.Column<float>(type: "real", nullable: false),
            RespiratoryInfection = table.Column<float>(type: "real", nullable: false),
            BrainFunction = table.Column<float>(type: "real", nullable: false),
            HeartFunction = table.Column<float>(type: "real", nullable: false),
            LeftLungFunction = table.Column<float>(type: "real", nullable: false),
            RightLungFunction = table.Column<float>(type: "real", nullable: false),
            LiverFunction = table.Column<float>(type: "real", nullable: false),
            KidneyFunction = table.Column<float>(type: "real", nullable: false),
            GutFunction = table.Column<float>(type: "real", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_character_physiology", entry => entry.CharacterId);
            table.ForeignKey("FK_character_physiology_characters_CharacterId",
                entry => entry.CharacterId, "characters", "CharacterId",
                onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("character_traits", table => new
        {
            CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            TraitId = table.Column<byte>(type: "smallint", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_character_traits", entry => new { entry.CharacterId, entry.TraitId });
            table.ForeignKey("FK_character_traits_characters_CharacterId",
                entry => entry.CharacterId, "characters", "CharacterId",
                onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("character_attributes", table => new
        {
            CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            AttributeId = table.Column<byte>(type: "smallint", nullable: false),
            Value = table.Column<float>(type: "real", nullable: false),
            TrainingLoad = table.Column<float>(type: "real", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_character_attributes",
                entry => new { entry.CharacterId, entry.AttributeId });
            table.ForeignKey("FK_character_attributes_characters_CharacterId",
                entry => entry.CharacterId, "characters", "CharacterId",
                onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("character_skills", table => new
        {
            CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            SkillId = table.Column<byte>(type: "smallint", nullable: false),
            PracticeHours = table.Column<float>(type: "real", nullable: false),
            RelevantPracticeHours = table.Column<float>(type: "real", nullable: false),
            PendingConsolidationHours = table.Column<float>(type: "real", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_character_skills", entry => new { entry.CharacterId, entry.SkillId });
            table.ForeignKey("FK_character_skills_characters_CharacterId",
                entry => entry.CharacterId, "characters", "CharacterId",
                onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("character_wounds", table => new
        {
            CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            WoundId = table.Column<long>(type: "bigint", nullable: false),
            BodyRegion = table.Column<byte>(type: "smallint", nullable: false),
            InjuryType = table.Column<byte>(type: "smallint", nullable: false),
            Severity = table.Column<float>(type: "real", nullable: false),
            TissueDamage = table.Column<float>(type: "real", nullable: false),
            Contamination = table.Column<float>(type: "real", nullable: false),
            Infection = table.Column<float>(type: "real", nullable: false),
            Bleeding = table.Column<float>(type: "real", nullable: false),
            InternalBleedingSeverity = table.Column<float>(type: "real", nullable: false),
            Pain = table.Column<float>(type: "real", nullable: false),
            PermanentImpairment = table.Column<float>(type: "real", nullable: false),
            PressureApplied = table.Column<bool>(type: "boolean", nullable: false),
            Washed = table.Column<bool>(type: "boolean", nullable: false),
            Disinfected = table.Column<bool>(type: "boolean", nullable: false),
            Sutured = table.Column<bool>(type: "boolean", nullable: false),
            Bandaged = table.Column<bool>(type: "boolean", nullable: false),
            Splinted = table.Column<bool>(type: "boolean", nullable: false),
            Healed = table.Column<bool>(type: "boolean", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_character_wounds", entry => new { entry.CharacterId, entry.WoundId });
            table.ForeignKey("FK_character_wounds_characters_CharacterId",
                entry => entry.CharacterId, "characters", "CharacterId",
                onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("character_relationships", table => new
        {
            CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            TargetCharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            Trust = table.Column<float>(type: "real", nullable: false),
            Fear = table.Column<float>(type: "real", nullable: false),
            Resentment = table.Column<float>(type: "real", nullable: false),
            Loyalty = table.Column<float>(type: "real", nullable: false),
            VoluntaryLoyalty = table.Column<bool>(type: "boolean", nullable: false),
            PersonalRequestsCompleted = table.Column<int>(type: "integer", nullable: false),
            PersuasionAttempts = table.Column<int>(type: "integer", nullable: false),
            IntimidationAttempts = table.Column<int>(type: "integer", nullable: false),
            NextSocialAttemptUtcTicks = table.Column<long>(type: "bigint", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_character_relationships",
                entry => new { entry.CharacterId, entry.TargetCharacterId });
            table.ForeignKey("FK_character_relationships_characters_CharacterId",
                entry => entry.CharacterId, "characters", "CharacterId",
                onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_character_relationships_TargetCharacterId",
            "character_relationships", "TargetCharacterId");

        migrationBuilder.CreateTable("character_worker_contracts", table => new
        {
            CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            ContractId = table.Column<Guid>(type: "uuid", nullable: false),
            EmployerAccountId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
            AssignedBedObjectId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
            Active = table.Column<bool>(type: "boolean", nullable: false),
            Voluntary = table.Column<bool>(type: "boolean", nullable: false),
            DailyRationCalories = table.Column<float>(type: "real", nullable: false),
            PromisedSafety = table.Column<float>(type: "real", nullable: false),
            WorkdayStartHour = table.Column<float>(type: "real", nullable: false),
            WorkdayEndHour = table.Column<float>(type: "real", nullable: false),
            PaymentItemId = table.Column<ushort>(type: "integer", nullable: false),
            PaymentQuantity = table.Column<ushort>(type: "integer", nullable: false),
            FulfilledContractGameSeconds = table.Column<double>(type: "double precision", nullable: false),
            ConsecutiveBreaches = table.Column<int>(type: "integer", nullable: false),
            WorkZoneX = table.Column<float>(type: "real", nullable: false),
            WorkZoneY = table.Column<float>(type: "real", nullable: false),
            WorkZoneZ = table.Column<float>(type: "real", nullable: false),
            WorkZoneRadius = table.Column<float>(type: "real", nullable: false),
            StorageX = table.Column<float>(type: "real", nullable: false),
            StorageY = table.Column<float>(type: "real", nullable: false),
            StorageZ = table.Column<float>(type: "real", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_character_worker_contracts", entry => entry.CharacterId);
            table.ForeignKey("FK_character_worker_contracts_characters_CharacterId",
                entry => entry.CharacterId, "characters", "CharacterId",
                onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("character_npc_runtime", table => new
        {
            CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            Disposition = table.Column<byte>(type: "smallint", nullable: false),
            Activity = table.Column<byte>(type: "smallint", nullable: false),
            ActiveJob = table.Column<byte>(type: "smallint", nullable: false),
            WorkbookSelectedJob = table.Column<byte>(type: "smallint", nullable: false),
            HomeX = table.Column<float>(type: "real", nullable: false),
            HomeY = table.Column<float>(type: "real", nullable: false),
            HomeZ = table.Column<float>(type: "real", nullable: false),
            DestinationX = table.Column<float>(type: "real", nullable: false),
            DestinationY = table.Column<float>(type: "real", nullable: false),
            DestinationZ = table.Column<float>(type: "real", nullable: false),
            EmployerAccountId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
            EmployerCharacterId = table.Column<Guid>(type: "uuid", nullable: true),
            Motivation = table.Column<float>(type: "real", nullable: false),
            WorkProgressSeconds = table.Column<float>(type: "real", nullable: false),
            NextDecisionUtcTicks = table.Column<long>(type: "bigint", nullable: false),
            LastNeedsActionUtcTicks = table.Column<long>(type: "bigint", nullable: false),
            LastWorkCompletedUtcTicks = table.Column<long>(type: "bigint", nullable: false),
            ContractEvaluationGameDay = table.Column<long>(type: "bigint", nullable: false),
            RationCaloriesCurrentDay = table.Column<float>(type: "real", nullable: false),
            PersonalRequest = table.Column<byte>(type: "smallint", nullable: false),
            PersonalRequestPending = table.Column<bool>(type: "boolean", nullable: false),
            PersonalRequestGameDay = table.Column<long>(type: "bigint", nullable: false),
            LastPersonalRequestCompletedGameDay = table.Column<long>(type: "bigint", nullable: false),
            PendingSabotageActions = table.Column<int>(type: "integer", nullable: false),
            FleeUntilUtcTicks = table.Column<long>(type: "bigint", nullable: false),
            LastLeadershipAction = table.Column<byte>(type: "smallint", nullable: false),
            LastLeadershipPracticeUtcTicks = table.Column<long>(type: "bigint", nullable: false),
            LeadershipInstructions = table.Column<int>(type: "integer", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_character_npc_runtime", entry => entry.CharacterId);
            table.ForeignKey("FK_character_npc_runtime_characters_CharacterId",
                entry => entry.CharacterId, "characters", "CharacterId",
                onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_character_npc_runtime_EmployerAccountId",
            "character_npc_runtime", "EmployerAccountId");
        migrationBuilder.CreateIndex("IX_character_npc_runtime_EmployerCharacterId",
            "character_npc_runtime", "EmployerCharacterId");

        migrationBuilder.CreateTable("character_worker_jobs", table => new
        {
            CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            JobId = table.Column<byte>(type: "smallint", nullable: false),
            Allowed = table.Column<bool>(type: "boolean", nullable: false),
            Priority = table.Column<byte>(type: "smallint", nullable: false),
            CompletedTasks = table.Column<int>(type: "integer", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_character_worker_jobs", entry => new { entry.CharacterId, entry.JobId });
            table.ForeignKey("FK_character_worker_jobs_characters_CharacterId",
                entry => entry.CharacterId, "characters", "CharacterId",
                onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("character_npc_lessons", table => new
        {
            CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
            SkillId = table.Column<byte>(type: "smallint", nullable: false),
            LessonsReceived = table.Column<int>(type: "integer", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_character_npc_lessons",
                entry => new { entry.CharacterId, entry.SkillId });
            table.ForeignKey("FK_character_npc_lessons_characters_CharacterId",
                entry => entry.CharacterId, "characters", "CharacterId",
                onDelete: ReferentialAction.Cascade);
        });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException(
        "Normalized survival projections cannot be downgraded safely. Restore a verified backup.");
}
