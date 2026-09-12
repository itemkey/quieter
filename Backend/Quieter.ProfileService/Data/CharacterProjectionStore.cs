using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Quieter.ProfileService.Data;

public sealed partial class ProfileStore
{
    // SurvivalJson remains the forward-compatible aggregate transported by the
    // Unity server. These relational projections are replaced in the same EF
    // transaction so operational queries never need to deserialize every body.
    private async Task ReplaceCharacterProjectionsAsync(
        Guid characterId,
        string survivalJson,
        CancellationToken cancellationToken)
    {
        var oldPhysiology = await database.CharacterPhysiology
            .SingleOrDefaultAsync(entry => entry.CharacterId == characterId, cancellationToken);
        var oldTraits = (await database.CharacterTraits
            .Where(entry => entry.CharacterId == characterId).ToListAsync(cancellationToken))
            .ToDictionary(entry => entry.TraitId);
        var oldAttributes = (await database.CharacterAttributes
            .Where(entry => entry.CharacterId == characterId).ToListAsync(cancellationToken))
            .ToDictionary(entry => entry.AttributeId);
        var oldSkills = (await database.CharacterSkills
            .Where(entry => entry.CharacterId == characterId).ToListAsync(cancellationToken))
            .ToDictionary(entry => entry.SkillId);
        var oldWounds = (await database.CharacterWounds
            .Where(entry => entry.CharacterId == characterId).ToListAsync(cancellationToken))
            .ToDictionary(entry => entry.WoundId);
        var oldRelationships = (await database.CharacterRelationships
            .Where(entry => entry.CharacterId == characterId).ToListAsync(cancellationToken))
            .ToDictionary(entry => entry.TargetCharacterId);
        var oldContract = await database.CharacterWorkerContracts
            .SingleOrDefaultAsync(entry => entry.CharacterId == characterId, cancellationToken);
        var oldNpc = await database.CharacterNpcRuntime
            .SingleOrDefaultAsync(entry => entry.CharacterId == characterId, cancellationToken);
        var oldWorkerJobs = (await database.CharacterWorkerJobs
            .Where(entry => entry.CharacterId == characterId).ToListAsync(cancellationToken))
            .ToDictionary(entry => entry.JobId);
        var oldNpcLessons = (await database.CharacterNpcLessons
            .Where(entry => entry.CharacterId == characterId).ToListAsync(cancellationToken))
            .ToDictionary(entry => entry.SkillId);

        using var document = JsonDocument.Parse(survivalJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return;

        if (TryObject(root, "Physiology", out var physiology))
        {
            TryObject(root, "Anatomy", out var anatomy);
            TryObject(root, "Conditions", out var conditions);
            var nextPhysiology = new CharacterPhysiologyEntity
            {
                CharacterId = characterId,
                AcuteStamina = Float(physiology, "AcuteStamina"),
                Oxygenation = Float(physiology, "Oxygenation"),
                BloodVolume = Float(physiology, "BloodVolume"),
                Hydration = Float(physiology, "Hydration"),
                ElectrolyteBalance = Float(physiology, "ElectrolyteBalance"),
                EnergyReserve = Float(physiology, "EnergyReserve"),
                ProteinReserve = Float(physiology, "ProteinReserve"),
                FatReserve = Float(physiology, "FatReserve"),
                MicronutrientReserve = Float(physiology, "MicronutrientReserve"),
                MineralReserve = Float(physiology, "MineralReserve"),
                SleepDebt = Float(physiology, "SleepDebt"),
                CircadianFatigue = Float(physiology, "CircadianFatigue"),
                SleepNoiseBurden = Float(physiology, "SleepNoiseBurden"),
                CoreTemperatureC = Float(physiology, "CoreTemperatureC", 37f),
                Pain = Float(physiology, "Pain"),
                Stress = Float(physiology, "Stress"),
                Consciousness = Float(physiology, "Consciousness"),
                SystemicInfection = Float(physiology, "SystemicInfection"),
                ToxinLoad = Float(physiology, "ToxinLoad"),
                FoodborneInfection = Float(conditions, "FoodborneInfection"),
                WaterborneInfection = Float(conditions, "WaterborneInfection"),
                ParasiteLoad = Float(conditions, "ParasiteLoad"),
                RespiratoryInfection = Float(conditions, "RespiratoryInfection"),
                BrainFunction = Float(anatomy, "BrainFunction", 1f),
                HeartFunction = Float(anatomy, "HeartFunction", 1f),
                LeftLungFunction = Float(anatomy, "LeftLungFunction", 1f),
                RightLungFunction = Float(anatomy, "RightLungFunction", 1f),
                LiverFunction = Float(anatomy, "LiverFunction", 1f),
                KidneyFunction = Float(anatomy, "KidneyFunction", 1f),
                GutFunction = Float(anatomy, "GutFunction", 1f),
                UpdatedAtUtc = DateTime.UtcNow,
            };
            if (oldPhysiology == null) database.CharacterPhysiology.Add(nextPhysiology);
            else database.Entry(oldPhysiology).CurrentValues.SetValues(nextPhysiology);
            oldPhysiology = null;
        }

        if (root.TryGetProperty("Traits", out var traits)
            && traits.ValueKind == JsonValueKind.Array)
        {
            var seen = new HashSet<byte>();
            foreach (var trait in traits.EnumerateArray())
            {
                if (!trait.TryGetByte(out var traitId) || !seen.Add(traitId)) continue;
                if (oldTraits.Remove(traitId)) continue;
                database.CharacterTraits.Add(new CharacterTraitEntity
                    { CharacterId = characterId, TraitId = traitId });
            }
        }

        if (TryObject(root, "Progression", out var progression))
        {
            progression.TryGetProperty("Attributes", out var attributes);
            progression.TryGetProperty("AttributeTrainingLoad", out var training);
            for (byte index = 0; index < 12; index++)
            {
                var next = new CharacterAttributeEntity
                {
                    CharacterId = characterId,
                    AttributeId = index,
                    Value = ArrayFloat(attributes, index, 50f),
                    TrainingLoad = ArrayFloat(training, index),
                };
                if (oldAttributes.Remove(index, out var old))
                    database.Entry(old).CurrentValues.SetValues(next);
                else database.CharacterAttributes.Add(next);
            }
            progression.TryGetProperty("SkillPracticeHours", out var practice);
            progression.TryGetProperty("RelevantPracticeHours", out var relevant);
            progression.TryGetProperty("PendingConsolidationHours", out var pending);
            var skillCount = Math.Min(64, Math.Max(
                ArrayLength(practice), Math.Max(ArrayLength(relevant), ArrayLength(pending))));
            for (byte index = 0; index < skillCount; index++)
            {
                var next = new CharacterSkillEntity
                {
                    CharacterId = characterId,
                    SkillId = index,
                    PracticeHours = ArrayFloat(practice, index),
                    RelevantPracticeHours = ArrayFloat(relevant, index),
                    PendingConsolidationHours = ArrayFloat(pending, index),
                };
                if (oldSkills.Remove(index, out var old))
                    database.Entry(old).CurrentValues.SetValues(next);
                else database.CharacterSkills.Add(next);
            }
        }

        if (TryObject(root, "Anatomy", out var anatomyWithWounds)
            && anatomyWithWounds.TryGetProperty("Wounds", out var wounds)
            && wounds.ValueKind == JsonValueKind.Array)
        {
            var seen = new HashSet<long>();
            foreach (var wound in wounds.EnumerateArray())
            {
                var woundId = Long(wound, "WoundId");
                if (wound.ValueKind != JsonValueKind.Object || woundId <= 0
                    || !seen.Add(woundId)) continue;
                var next = new CharacterWoundEntity
                {
                    CharacterId = characterId,
                    WoundId = woundId,
                    BodyRegion = Byte(wound, "Region"),
                    InjuryType = Byte(wound, "Type"),
                    Severity = Float(wound, "Severity"),
                    TissueDamage = Float(wound, "TissueDamage"),
                    Contamination = Float(wound, "Contamination"),
                    Infection = Float(wound, "Infection"),
                    Bleeding = Float(wound, "Bleeding"),
                    InternalBleedingSeverity = Float(wound, "InternalBleedingSeverity"),
                    Pain = Float(wound, "Pain"),
                    PermanentImpairment = Float(wound, "PermanentImpairment"),
                    PressureApplied = Bool(wound, "PressureApplied"),
                    Washed = Bool(wound, "Washed"),
                    Disinfected = Bool(wound, "Disinfected"),
                    Sutured = Bool(wound, "Sutured"),
                    Bandaged = Bool(wound, "Bandaged"),
                    Splinted = Bool(wound, "Splinted"),
                    Healed = Bool(wound, "Healed"),
                };
                if (oldWounds.Remove(woundId, out var old))
                    database.Entry(old).CurrentValues.SetValues(next);
                else database.CharacterWounds.Add(next);
            }
        }

        if (root.TryGetProperty("Relationships", out var relationships)
            && relationships.ValueKind == JsonValueKind.Array)
        {
            var seen = new HashSet<Guid>();
            foreach (var relationship in relationships.EnumerateArray())
            {
                if (!Guid.TryParse(String(relationship, "TargetCharacterId"), out var targetId)
                    || !seen.Add(targetId)) continue;
                var next = new CharacterRelationshipEntity
                {
                    CharacterId = characterId,
                    TargetCharacterId = targetId,
                    Trust = Float(relationship, "Trust"),
                    Fear = Float(relationship, "Fear"),
                    Resentment = Float(relationship, "Resentment"),
                    Loyalty = Float(relationship, "Loyalty"),
                    VoluntaryLoyalty = Bool(relationship, "VoluntaryLoyalty"),
                    PersonalRequestsCompleted = Int(relationship, "PersonalRequestsCompleted"),
                    PersuasionAttempts = Int(relationship, "PersuasionAttempts"),
                    IntimidationAttempts = Int(relationship, "IntimidationAttempts"),
                    NextSocialAttemptUtcTicks = Long(relationship, "NextSocialAttemptUtcTicks"),
                };
                if (oldRelationships.Remove(targetId, out var old))
                    database.Entry(old).CurrentValues.SetValues(next);
                else database.CharacterRelationships.Add(next);
            }
        }

        if (TryObject(root, "WorkerContract", out var contract))
        {
            TryObject(contract, "WorkZoneCenter", out var workZone);
            TryObject(contract, "StoragePosition", out var storage);
            _ = Guid.TryParse(String(contract, "ContractId"), out var contractId);
            _ = decimal.TryParse(String(contract, "EmployerAccountId"), out var employerId);
            decimal? bedId = decimal.TryParse(
                String(contract, "AssignedBedObjectId"), out var parsedBedId)
                ? parsedBedId : null;
            var nextContract = new CharacterWorkerContractEntity
            {
                CharacterId = characterId,
                ContractId = contractId,
                EmployerAccountId = employerId,
                AssignedBedObjectId = bedId,
                Active = Bool(contract, "Active"),
                Voluntary = Bool(contract, "Voluntary"),
                DailyRationCalories = Float(contract, "DailyRationCalories"),
                PromisedSafety = Float(contract, "PromisedSafety"),
                WorkdayStartHour = Float(contract, "WorkdayStartHour"),
                WorkdayEndHour = Float(contract, "WorkdayEndHour"),
                PaymentItemId = UShort(contract, "PaymentItemId"),
                PaymentQuantity = UShort(contract, "PaymentQuantity"),
                FulfilledContractGameSeconds = Double(contract, "FulfilledContractGameSeconds"),
                ConsecutiveBreaches = Int(contract, "ConsecutiveBreaches"),
                WorkZoneX = Float(workZone, "x"),
                WorkZoneY = Float(workZone, "y"),
                WorkZoneZ = Float(workZone, "z"),
                WorkZoneRadius = Float(contract, "WorkZoneRadius"),
                StorageX = Float(storage, "x"),
                StorageY = Float(storage, "y"),
                StorageZ = Float(storage, "z"),
            };
            if (oldContract == null) database.CharacterWorkerContracts.Add(nextContract);
            else database.Entry(oldContract).CurrentValues.SetValues(nextContract);
            oldContract = null;
        }

        var hasNpc = TryObject(root, "Npc", out var npc);
        if (hasNpc)
        {
            TryObject(npc, "HomePosition", out var home);
            TryObject(npc, "Destination", out var destination);
            var employerAccountId = DecimalString(npc, "EmployerAccountId");
            var employerCharacterId = GuidString(npc, "EmployerCharacterId");
            var nextNpc = new CharacterNpcRuntimeEntity
            {
                CharacterId = characterId,
                Disposition = Byte(npc, "Disposition"),
                Activity = Byte(npc, "Activity"),
                ActiveJob = Byte(npc, "ActiveJob"),
                WorkbookSelectedJob = Byte(npc, "WorkbookSelectedJob"),
                HomeX = Float(home, "x"),
                HomeY = Float(home, "y"),
                HomeZ = Float(home, "z"),
                DestinationX = Float(destination, "x"),
                DestinationY = Float(destination, "y"),
                DestinationZ = Float(destination, "z"),
                EmployerAccountId = employerAccountId,
                EmployerCharacterId = employerCharacterId,
                Motivation = Float(npc, "Motivation", 0.65f),
                WorkProgressSeconds = Float(npc, "WorkProgressSeconds"),
                NextDecisionUtcTicks = Long(npc, "NextDecisionUtcTicks"),
                LastNeedsActionUtcTicks = Long(npc, "LastNeedsActionUtcTicks"),
                LastWorkCompletedUtcTicks = Long(npc, "LastWorkCompletedUtcTicks"),
                ContractEvaluationGameDay = Long(npc, "ContractEvaluationGameDay"),
                RationCaloriesCurrentDay = Float(npc, "RationCaloriesCurrentDay"),
                PersonalRequest = Byte(npc, "PersonalRequest"),
                PersonalRequestPending = Bool(npc, "PersonalRequestPending"),
                PersonalRequestGameDay = Long(npc, "PersonalRequestGameDay"),
                LastPersonalRequestCompletedGameDay = Long(
                    npc, "LastPersonalRequestCompletedGameDay"),
                PendingSabotageActions = Int(npc, "PendingSabotageActions"),
                FleeUntilUtcTicks = Long(npc, "FleeUntilUtcTicks"),
                LastLeadershipAction = Byte(npc, "LastLeadershipAction"),
                LastLeadershipPracticeUtcTicks = Long(npc, "LastLeadershipPracticeUtcTicks"),
                LeadershipInstructions = Int(npc, "LeadershipInstructions"),
            };
            if (oldNpc == null) database.CharacterNpcRuntime.Add(nextNpc);
            else database.Entry(oldNpc).CurrentValues.SetValues(nextNpc);
            oldNpc = null;

            npc.TryGetProperty("LessonsReceived", out var lessons);
            var lessonCount = Math.Min(64, ArrayLength(lessons));
            for (byte index = 0; index < lessonCount; index++)
            {
                var received = ArrayInt(lessons, index);
                if (received <= 0) continue;
                var nextLesson = new CharacterNpcLessonEntity
                {
                    CharacterId = characterId,
                    SkillId = index,
                    LessonsReceived = received,
                };
                if (oldNpcLessons.Remove(index, out var old))
                    database.Entry(old).CurrentValues.SetValues(nextLesson);
                else database.CharacterNpcLessons.Add(nextLesson);
            }
        }

        if (hasNpc || contract.ValueKind == JsonValueKind.Object)
        {
            var allowedJobs = default(JsonElement);
            var jobPriorities = default(JsonElement);
            var completedTasks = default(JsonElement);
            if (contract.ValueKind == JsonValueKind.Object)
            {
                contract.TryGetProperty("AllowedJobs", out allowedJobs);
                contract.TryGetProperty("JobPriorities", out jobPriorities);
            }
            if (hasNpc) npc.TryGetProperty("CompletedTasks", out completedTasks);
            var allowed = ByteSet(allowedJobs, 8);
            for (byte index = 0; index < 8; index++)
            {
                var nextJob = new CharacterWorkerJobEntity
                {
                    CharacterId = characterId,
                    JobId = index,
                    Allowed = allowed.Contains(index),
                    Priority = ArrayByte(jobPriorities, index),
                    CompletedTasks = ArrayInt(completedTasks, index),
                };
                if (oldWorkerJobs.Remove(index, out var old))
                    database.Entry(old).CurrentValues.SetValues(nextJob);
                else database.CharacterWorkerJobs.Add(nextJob);
            }
        }

        if (oldPhysiology != null) database.CharacterPhysiology.Remove(oldPhysiology);
        database.CharacterTraits.RemoveRange(oldTraits.Values);
        database.CharacterAttributes.RemoveRange(oldAttributes.Values);
        database.CharacterSkills.RemoveRange(oldSkills.Values);
        database.CharacterWounds.RemoveRange(oldWounds.Values);
        database.CharacterRelationships.RemoveRange(oldRelationships.Values);
        if (oldContract != null) database.CharacterWorkerContracts.Remove(oldContract);
        if (oldNpc != null) database.CharacterNpcRuntime.Remove(oldNpc);
        database.CharacterWorkerJobs.RemoveRange(oldWorkerJobs.Values);
        database.CharacterNpcLessons.RemoveRange(oldNpcLessons.Values);
    }

    private static bool TryObject(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Object;
    }

    private static float Float(JsonElement parent, string name, float fallback = 0f)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.TryGetSingle(out var parsed) && float.IsFinite(parsed) ? parsed : fallback;

    private static double Double(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.TryGetDouble(out var parsed) && double.IsFinite(parsed) ? parsed : 0d;

    private static int Int(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
                ? parsed : 0;

    private static long Long(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value) && value.TryGetInt64(out var parsed)
                ? parsed : 0;

    private static byte Byte(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value) && value.TryGetByte(out var parsed)
                ? parsed : (byte)0;

    private static ushort UShort(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value) && value.TryGetUInt16(out var parsed)
                ? parsed : (ushort)0;

    private static bool Bool(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private static string String(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static decimal? DecimalString(JsonElement parent, string name)
        => decimal.TryParse(String(parent, name), out var parsed) ? parsed : null;

    private static Guid? GuidString(JsonElement parent, string name)
        => Guid.TryParse(String(parent, name), out var parsed) ? parsed : null;

    private static int ArrayLength(JsonElement value)
        => value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : 0;

    private static float ArrayFloat(JsonElement value, int index, float fallback = 0f)
    {
        if (value.ValueKind != JsonValueKind.Array || index < 0
            || index >= value.GetArrayLength()) return fallback;
        var item = value[index];
        return item.TryGetSingle(out var parsed) && float.IsFinite(parsed) ? parsed : fallback;
    }

    private static int ArrayInt(JsonElement value, int index)
    {
        if (value.ValueKind != JsonValueKind.Array || index < 0
            || index >= value.GetArrayLength()) return 0;
        return value[index].TryGetInt32(out var parsed) ? parsed : 0;
    }

    private static byte ArrayByte(JsonElement value, int index)
    {
        if (value.ValueKind != JsonValueKind.Array || index < 0
            || index >= value.GetArrayLength()) return 0;
        return value[index].TryGetByte(out var parsed) ? parsed : (byte)0;
    }

    private static HashSet<byte> ByteSet(JsonElement value, byte exclusiveMaximum)
    {
        var result = new HashSet<byte>();
        if (value.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in value.EnumerateArray())
            if (item.TryGetByte(out var parsed) && parsed < exclusiveMaximum) result.Add(parsed);
        return result;
    }
}
