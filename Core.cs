using BetterSewerKeys.Integrations;
using BetterSewerKeys.Utils;
using MelonLoader;
using UnityEngine;
#if MONO
using ScheduleOne.Map;
using ScheduleOne.DevUtilities;
#else
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.DevUtilities;
#endif

[assembly: MelonInfo(typeof(BetterSewerKeys.Core), Constants.MOD_NAME, Constants.MOD_VERSION, Constants.MOD_AUTHOR)]
[assembly: MelonGame(Constants.Game.GAME_STUDIO, Constants.Game.GAME_NAME)]

namespace BetterSewerKeys
{
    public class Core : MelonMod
    {
        public static Core? Instance { get; private set; }

        private BetterSewerKeysSave? _saveData;

        public override void OnInitializeMelon()
        {
            Instance = this;
            ModLogger.LogInitialization();

            try
            {
                // Initialize Harmony patches
                HarmonyPatches.SetModInstance(this);

                // Create save data instance (S1API will auto-discover it)
                _saveData = new BetterSewerKeysSave();

                // Initialize manager with save data
                BetterSewerKeysManager.Instance.Initialize(_saveData);

                ModLogger.Info("BetterSewerKeys mod initialized successfully");
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Failed to initialize BetterSewerKeys mod", ex);
            }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            try
            {
                // Discover entrances when main game scene loads
                if (sceneName.Contains("Main") || sceneName.Contains("Game"))
                {
                    ModLogger.Info($"Scene initialized: {sceneName} - Discovering sewer entrances...");
                    
                    // Delay discovery slightly to ensure all objects are initialized
                    MelonCoroutines.Start(DelayedDiscovery());
                }
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error during scene initialization", ex);
            }
        }

        private System.Collections.IEnumerator DelayedDiscovery()
        {
            yield return new WaitForSeconds(1f);
            
            try
            {
                BetterSewerKeysManager.Instance.DiscoverEntrances();
                
                // Assign key distribution after discovery
                var sewerManager = NetworkSingleton<SewerManager>.Instance;
                if (sewerManager != null)
                {
                    BetterSewerKeysManager.Instance.AssignKeyDistribution(sewerManager);
                }
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error during entrance discovery", ex);
            }
        }

        public override void OnApplicationQuit()
        {
            Instance = null;
        }

        public BetterSewerKeysSave? GetSaveData()
        {
            return _saveData;
        }
    }
}