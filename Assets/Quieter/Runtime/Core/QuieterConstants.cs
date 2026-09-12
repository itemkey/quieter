using System;

namespace Quieter.Core
{
    [Flags]
    public enum QuieterFeatureStages : ulong
    {
        None = 0,
        CharacterIdentityAndPersistence = 1UL << 0,
        PhysicalItemsAndCartography = 1UL << 1,
        PhysiologyProgressionAndMedicine = 1UL << 2,
        WeatherConstructionAndSanitation = 1UL << 3,
        MeleeRestraintAndCapture = 1UL << 4,
        LivingNpcsContractsAndInheritance = 1UL << 5,
        ReleaseUxAccessibilityAndBalance = 1UL << 6,
        CompleteSurvivalUpdate = CharacterIdentityAndPersistence
            | PhysicalItemsAndCartography
            | PhysiologyProgressionAndMedicine
            | WeatherConstructionAndSanitation
            | MeleeRestraintAndCapture
            | LivingNpcsContractsAndInheritance
            | ReleaseUxAccessibilityAndBalance,
    }

    public static class QuieterConstants
    {
        public const ushort ProtocolVersion = 15;
        public const ushort GeneratorVersion = 7;
        public const QuieterFeatureStages EnabledFeatureStages =
            QuieterFeatureStages.CompleteSurvivalUpdate;
        public const ushort DefaultGamePort = 7777;
        public const int DefaultMaxPlayers = 16;
        public const int ServerTickRate = 30;
        public const int MovementSimulationRate = 60;
        public const int AuthenticationTimeoutSeconds = 15;
        public const int PositionSaveIntervalSeconds = 5;
        public const uint DevelopmentSteamAppId = 480;
    }
}
