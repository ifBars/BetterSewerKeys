#if MONO
using ScheduleOne;
#else
#endif
using HarmonyLib;

namespace BetterSewerKeys.Integrations
{
    [HarmonyPatch]
    public static class HarmonyPatches
    {
        private static Core? _modInstance;

        /// <summary>
        /// Set the mod instance for patch callbacks
        /// </summary>
        public static void SetModInstance(Core modInstance)
        {
            _modInstance = modInstance;
        }
    }
}
