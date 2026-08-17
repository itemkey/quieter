using Quieter.Core;
using UnityEngine;

namespace Quieter.Networking
{
    [CreateAssetMenu(menuName = "Quieter/Steam Settings", fileName = "SteamSettings")]
    public sealed class SteamSettings : ScriptableObject
    {
        [SerializeField] private uint appId = QuieterConstants.DevelopmentSteamAppId;
        [SerializeField] private bool productionBuild;

        public uint AppId => appId;
        public bool ProductionBuild => productionBuild;

#if UNITY_EDITOR
        public void Configure(uint newAppId, bool isProduction)
        {
            appId = newAppId;
            productionBuild = isProduction;
        }
#endif
    }
}
