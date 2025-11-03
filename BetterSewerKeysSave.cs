using System.Collections.Generic;
using S1API.Internal.Abstraction;
using S1API.Saveables;
using UnityEngine;
using MelonLoader;
#if MONO
using ScheduleOne.Map;
using ScheduleOne.DevUtilities;
#else
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.DevUtilities;
#endif

namespace BetterSewerKeys
{
    /// <summary>
    /// S1API Saveable for persisting per-entrance unlock states and key distribution data
    /// </summary>
    public class BetterSewerKeysSave : Saveable
    {
        [SaveableField("unlockedEntrances")]
        private Dictionary<int, bool> _unlockedEntrances = new Dictionary<int, bool>();

        [SaveableField("keyLocationIndices")]
        private Dictionary<int, int> _keyLocationIndices = new Dictionary<int, int>();

        [SaveableField("keyPossessorIndices")]
        private Dictionary<int, int> _keyPossessorIndices = new Dictionary<int, int>();

        [SaveableField("isRandomWorldKeyCollected")]
        private Dictionary<int, bool> _isRandomWorldKeyCollected = new Dictionary<int, bool>();

        public Dictionary<int, bool> UnlockedEntrances => _unlockedEntrances;
        public Dictionary<int, int> KeyLocationIndices => _keyLocationIndices;
        public Dictionary<int, int> KeyPossessorIndices => _keyPossessorIndices;
        public Dictionary<int, bool> IsRandomWorldKeyCollected => _isRandomWorldKeyCollected;

        protected override void OnLoaded()
        {
            Utils.ModLogger.Info($"BetterSewerKeys: Loaded save data - {_unlockedEntrances.Count} entrances tracked");
            
            // Check for migration from old save format
            CheckAndMigrateOldSave();
            
            // Apply loaded data to manager
            if (BetterSewerKeysManager.Instance != null)
            {
                BetterSewerKeysManager.Instance.ApplySaveData(this);
            }
        }

        /// <summary>
        /// Check if this is an old save and migrate if needed
        /// </summary>
        private void CheckAndMigrateOldSave()
        {
            try
            {
                // Check if this is first run (no save data exists)
                if (_unlockedEntrances.Count == 0)
                {
                    Utils.ModLogger.Debug("BetterSewerKeys: First run detected, checking for old save migration");
                    
                    // Check if SewerManager has old global unlock state
                    var sewerManager = NetworkSingleton<SewerManager>.Instance;
                    if (sewerManager != null)
                    {
                        // Use reflection to check the old IsSewerUnlocked property
                        var isSewerUnlockedProp = typeof(SewerManager).GetProperty("IsSewerUnlocked");
                        if (isSewerUnlockedProp != null)
                        {
                            var isUnlocked = (bool)isSewerUnlockedProp.GetValue(sewerManager);
                            
                            if (isUnlocked)
                            {
                                Utils.ModLogger.Info("BetterSewerKeys: Old save detected with global sewer unlock - migrating to per-entrance system");
                                
                                // Wait for doors to be discovered, then unlock all
                                // We'll do this in a delayed callback
                                MelonCoroutines.Start(DelayedMigration(sewerManager));
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Utils.ModLogger.Error("Error during save migration check", ex);
            }
        }

        /// <summary>
        /// Delayed migration to wait for doors to be discovered
        /// </summary>
        private System.Collections.IEnumerator DelayedMigration(SewerManager sewerManager)
        {
            // Wait for doors to be discovered
            yield return new WaitForSeconds(2f);
            
            try
            {
                var manager = BetterSewerKeysManager.Instance;
                if (manager == null)
                {
                    Utils.ModLogger.Warning("BetterSewerKeys: Manager not initialized during migration");
                    yield break;
                }

                // Discover entrances if not already done
                manager.DiscoverEntrances();
                
                // Unlock all discovered entrances
                foreach (var entranceID in manager.GetAllEntranceIDs())
                {
                    SetEntranceUnlocked(entranceID, true);
                    Utils.ModLogger.Info($"BetterSewerKeys: Migrated - unlocked entrance {entranceID}");
                }
                
                Utils.ModLogger.Info($"BetterSewerKeys: Migration complete - unlocked {manager.GetAllEntranceIDs().Count} entrances");
            }
            catch (System.Exception ex)
            {
                Utils.ModLogger.Error("Error during delayed migration", ex);
            }
        }

        protected override void OnSaved()
        {
            Utils.ModLogger.Debug($"BetterSewerKeys: Saved data - {_unlockedEntrances.Count} entrances tracked");
        }

        /// <summary>
        /// Check if an entrance is unlocked
        /// </summary>
        public bool IsEntranceUnlocked(int entranceID)
        {
            return _unlockedEntrances.TryGetValue(entranceID, out bool unlocked) && unlocked;
        }

        /// <summary>
        /// Set an entrance's unlock state
        /// </summary>
        public void SetEntranceUnlocked(int entranceID, bool unlocked)
        {
            _unlockedEntrances[entranceID] = unlocked;
            RequestGameSave();
        }

        /// <summary>
        /// Get the key location index for an entrance
        /// </summary>
        public int GetKeyLocationIndex(int entranceID)
        {
            return _keyLocationIndices.TryGetValue(entranceID, out int index) ? index : -1;
        }

        /// <summary>
        /// Set the key location index for an entrance
        /// </summary>
        public void SetKeyLocationIndex(int entranceID, int locationIndex)
        {
            _keyLocationIndices[entranceID] = locationIndex;
        }

        /// <summary>
        /// Get the key possessor index for an entrance
        /// </summary>
        public int GetKeyPossessorIndex(int entranceID)
        {
            return _keyPossessorIndices.TryGetValue(entranceID, out int index) ? index : -1;
        }

        /// <summary>
        /// Set the key possessor index for an entrance
        /// </summary>
        public void SetKeyPossessorIndex(int entranceID, int possessorIndex)
        {
            _keyPossessorIndices[entranceID] = possessorIndex;
        }

        /// <summary>
        /// Check if random world key is collected for an entrance
        /// </summary>
        public bool IsRandomWorldKeyCollectedForEntrance(int entranceID)
        {
            return _isRandomWorldKeyCollected.TryGetValue(entranceID, out bool collected) && collected;
        }

        /// <summary>
        /// Set random world key collected state for an entrance
        /// </summary>
        public void SetRandomWorldKeyCollected(int entranceID, bool collected)
        {
            _isRandomWorldKeyCollected[entranceID] = collected;
        }
    }
}
