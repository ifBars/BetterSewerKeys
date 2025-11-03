using S1API.Internal.Abstraction;
using S1API.Saveables;
using UnityEngine;
using MelonLoader;
using S1API.GameTime;
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
    /// Serializable data class for BetterSewerKeys save data
    /// </summary>
    public class BetterSewerKeysData
    {
        public Dictionary<int, bool> UnlockedEntrances = new Dictionary<int, bool>();
        public Dictionary<int, int> KeyLocationIndices = new Dictionary<int, int>();
        public Dictionary<int, int> KeyPossessorIndices = new Dictionary<int, int>();
        public Dictionary<int, bool> IsRandomWorldKeyCollected = new Dictionary<int, bool>();
        public int LastDayKeyWasCollected = -1;

        public BetterSewerKeysData()
        {
        }
    }

    /// <summary>
    /// S1API Saveable for persisting per-entrance unlock states and key distribution data
    /// </summary>
    public class BetterSewerKeysSave : Saveable
    {
        [SaveableField("better_sewer_keys_data")]
        private BetterSewerKeysData _data = new BetterSewerKeysData();

        public Dictionary<int, bool> UnlockedEntrances => _data.UnlockedEntrances;
        public Dictionary<int, int> KeyLocationIndices => _data.KeyLocationIndices;
        public Dictionary<int, int> KeyPossessorIndices => _data.KeyPossessorIndices;
        public Dictionary<int, bool> IsRandomWorldKeyCollected => _data.IsRandomWorldKeyCollected;
        public int LastDayKeyWasCollected => _data.LastDayKeyWasCollected;

        public BetterSewerKeysSave()
        {
        }

        protected override void OnLoaded()
        {
            Utils.ModLogger.Info($"BetterSewerKeys: Loaded save data - {_data.UnlockedEntrances.Count} entrances tracked");
            
            // Check for migration from old save format
            CheckAndMigrateOldSave();
        }

        /// <summary>
        /// Apply save data to manager after doors are discovered
        /// Called from Core.DelayedDiscovery()
        /// </summary>
        public void ApplySaveDataAfterDiscovery()
        {
            // Make sure we're subscribed to day pass events
            TimeManager.OnDayPass -= OnDayPass;
            TimeManager.OnDayPass += OnDayPass;
            
            // Apply loaded data to manager
            if (BetterSewerKeysManager.Instance != null)
            {
                BetterSewerKeysManager.Instance.ApplySaveData(this);
            }

            // Trigger new key pickup if needed
            CheckAndSpawnNewKeyPickup();
        }

        /// <summary>
        /// Called when a new day passes - move random key to new location and enable it
        /// </summary>
        private void OnDayPass()
        {
            Utils.ModLogger.Info("BetterSewerKeys: OnDayPass callback fired");
            CheckAndSpawnNewKeyPickup();
        }

        /// <summary>
        /// Check if we need to spawn a new key pickup and do so if all entrances aren't unlocked
        /// Moves the key to a random new location for a locked entrance
        /// </summary>
        private void CheckAndSpawnNewKeyPickup()
        {
            try
            {
                var manager = BetterSewerKeysManager.Instance;
                if (manager == null || manager.AreAllEntrancesUnlocked())
                {
                    return; // All unlocked, no need for new keys
                }

                var sewerManager = NetworkSingleton<SewerManager>.Instance;
                if (sewerManager == null || sewerManager.RandomWorldSewerKeyPickup == null || 
                    sewerManager.RandomSewerKeyLocations == null || sewerManager.RandomSewerKeyLocations.Length == 0)
                {
                    return;
                }

                // Find all locked entrances (regardless of whether they've collected their world key before)
                var lockedEntrances = new List<int>();
                foreach (var entranceID in manager.GetAllEntranceIDs())
                {
                    if (!manager.IsEntranceUnlocked(entranceID))
                    {
                        lockedEntrances.Add(entranceID);
                    }
                }

                if (lockedEntrances.Count == 0)
                {
                    return; // All entrances unlocked
                }

                // Pick a random locked entrance
                int randomIndex = UnityEngine.Random.Range(0, lockedEntrances.Count);
                int selectedEntranceID = lockedEntrances[randomIndex];

                // Get available locations (all locations)
                var availableLocations = new List<int>();
                for (int i = 0; i < sewerManager.RandomSewerKeyLocations.Length; i++)
                {
                    availableLocations.Add(i);
                }

                // Pick a random location
                int randomLocationIndex = availableLocations[UnityEngine.Random.Range(0, availableLocations.Count)];

                // Update the location index for this entrance (or assign if not set)
                SetKeyLocationIndex(selectedEntranceID, randomLocationIndex);

                // Move the pickup to the new location and enable it
                sewerManager.SetSewerKeyLocation(null, randomLocationIndex);
                sewerManager.RandomWorldSewerKeyPickup.gameObject.SetActive(true);

                Utils.ModLogger.Info($"BetterSewerKeys: Moved random key pickup to new location {randomLocationIndex} for entrance {selectedEntranceID} on day pass");
            }
            catch (System.Exception ex)
            {
                Utils.ModLogger.Error("Error checking for new key pickup", ex);
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
                if (_data.UnlockedEntrances.Count == 0)
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
            Utils.ModLogger.Debug($"BetterSewerKeys: Saved data - {_data.UnlockedEntrances.Count} entrances tracked");
        }

        /// <summary>
        /// Check if an entrance is unlocked
        /// </summary>
        public bool IsEntranceUnlocked(int entranceID)
        {
            return _data.UnlockedEntrances.TryGetValue(entranceID, out bool unlocked) && unlocked;
        }

        /// <summary>
        /// Set an entrance's unlock state
        /// </summary>
        public void SetEntranceUnlocked(int entranceID, bool unlocked)
        {
            _data.UnlockedEntrances[entranceID] = unlocked;
            RequestGameSave();
        }

        /// <summary>
        /// Get the key location index for an entrance
        /// </summary>
        public int GetKeyLocationIndex(int entranceID)
        {
            return _data.KeyLocationIndices.TryGetValue(entranceID, out int index) ? index : -1;
        }

        /// <summary>
        /// Set the key location index for an entrance
        /// </summary>
        public void SetKeyLocationIndex(int entranceID, int locationIndex)
        {
            _data.KeyLocationIndices[entranceID] = locationIndex;
        }

        /// <summary>
        /// Get the key possessor index for an entrance
        /// </summary>
        public int GetKeyPossessorIndex(int entranceID)
        {
            return _data.KeyPossessorIndices.TryGetValue(entranceID, out int index) ? index : -1;
        }

        /// <summary>
        /// Set the key possessor index for an entrance
        /// </summary>
        public void SetKeyPossessorIndex(int entranceID, int possessorIndex)
        {
            _data.KeyPossessorIndices[entranceID] = possessorIndex;
        }

        /// <summary>
        /// Check if random world key is collected for an entrance
        /// </summary>
        public bool IsRandomWorldKeyCollectedForEntrance(int entranceID)
        {
            return _data.IsRandomWorldKeyCollected.TryGetValue(entranceID, out bool collected) && collected;
        }

        /// <summary>
        /// Set random world key collected state for an entrance
        /// </summary>
        public void SetRandomWorldKeyCollected(int entranceID, bool collected)
        {
            _data.IsRandomWorldKeyCollected[entranceID] = collected;
            if (collected)
            {
                _data.LastDayKeyWasCollected = TimeManager.ElapsedDays;
            }
        }
    }
}