#if MONO
using ScheduleOne.Map;
using ScheduleOne.DevUtilities;
using ScheduleOne.NPCs;
using ScheduleOne.PlayerScripts;
using FishNet.Connection;
using ScheduleOne.Doors;
#else
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppFishNet.Connection;
using Il2CppScheduleOne.Doors;
#endif
using HarmonyLib;
using BetterSewerKeys.Utils;

namespace BetterSewerKeys.Integrations
{
    /// <summary>
    /// Harmony patches for SewerManager to enable per-entrance unlock tracking
    /// </summary>
    [HarmonyPatch]
    public static class SewerManagerPatches
    {
        /// <summary>
        /// Patch IsSewerUnlocked property to check if ALL entrances are unlocked (for backward compatibility)
        /// </summary>
        [HarmonyPatch(typeof(SewerManager), "get_IsSewerUnlocked")]
        [HarmonyPrefix]
        public static bool SewerManager_IsSewerUnlocked_Getter_Prefix(ref bool __result)
        {
            try
            {
                // Check if all entrances are unlocked
                if (BetterSewerKeysManager.Instance != null)
                {
                    __result = BetterSewerKeysManager.Instance.AreAllEntrancesUnlocked();
                    return false; // Skip original getter
                }
                
                // Fallback to original if manager not initialized
                return true;
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in SewerManager.IsSewerUnlocked getter prefix", ex);
                return true; // Let original method run on error
            }
        }

        /// <summary>
        /// Patch SetSewerUnlocked_Server to unlock only the specific entrance that was interacted with
        /// </summary>
        [HarmonyPatch(typeof(SewerManager), "SetSewerUnlocked_Server")]
        [HarmonyPrefix]
        public static bool SewerManager_SetSewerUnlocked_Server_Prefix(SewerManager __instance)
        {
            try
            {
                var manager = BetterSewerKeysManager.Instance;
                if (manager == null)
                {
                    return true; // Let original method run
                }

                // Get the last interacted door from SewerDoorControllerPatches
                var lastInteractedDoorField = typeof(SewerDoorControllerPatches)
                    .GetField("_lastInteractedDoor", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                
                SewerDoorController? lastInteractedDoor = null;
                if (lastInteractedDoorField != null)
                {
                    lastInteractedDoor = lastInteractedDoorField.GetValue(null) as SewerDoorController;
                }

                int entranceID = -1;
                
                if (lastInteractedDoor != null)
                {
                    // Get entrance ID from the door
                    var getEntranceIDMethod = typeof(SewerDoorControllerPatches)
                        .GetMethod("GetEntranceID", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    
                    if (getEntranceIDMethod != null)
                    {
                        entranceID = (int)getEntranceIDMethod.Invoke(null, new object[] { lastInteractedDoor });
                        
                        // Check if this entrance is already unlocked - if so, don't unlock anything
                        if (entranceID != -1 && manager.IsEntranceUnlocked(entranceID))
                        {
                            ModLogger.Debug($"SetSewerUnlocked_Server: Entrance {entranceID} already unlocked, skipping unlock");
                            // Don't unlock anything, but also don't call original method
                            // We need to restore the key that was consumed
                            var playerInventory = PlayerSingleton<PlayerInventory>.Instance;
                            if (playerInventory != null)
                            {
                                playerInventory.AddItemToInventory(__instance.SewerKeyItem.GetDefaultInstance());
                                ModLogger.Debug($"SetSewerUnlocked_Server: Restored key to player inventory");
                            }
                            return false; // Skip original method
                        }
                    }
                }

                // If we couldn't get entrance ID from door, fall back to first locked entrance
                if (entranceID == -1)
                {
                    entranceID = manager.GetFirstLockedEntranceID();
                    if (entranceID == -1)
                    {
                        // All entrances unlocked, let original method handle
                        return true;
                    }
                    ModLogger.Debug($"SetSewerUnlocked_Server: Could not determine entrance ID from door, using first locked entrance: {entranceID}");
                }
                
                if (entranceID != -1)
                {
                    // Unlock this specific entrance
                    manager.UnlockEntrance(entranceID);
                    ModLogger.Info($"SewerManager.SetSewerUnlocked_Server: Unlocked entrance {entranceID}");
                    
                    // Don't call original method - we've handled it
                    return false;
                }
                
                // All entrances already unlocked, let original method handle
                return true;
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in SewerManager.SetSewerUnlocked_Server prefix", ex);
                return true; // Let original method run on error
            }
        }

        /// <summary>
        /// Patch SetSewerUnlocked_Client to handle per-entrance unlocks
        /// </summary>
        [HarmonyPatch(typeof(SewerManager), "SetSewerUnlocked_Client", new System.Type[] { typeof(NetworkConnection) })]
        [HarmonyPrefix]
        public static bool SewerManager_SetSewerUnlocked_Client_Prefix(SewerManager __instance, NetworkConnection conn)
        {
            try
            {
                // Note: The door controller patch handles unlocking directly, so this might not be called
                // But we patch it anyway for safety
                var manager = BetterSewerKeysManager.Instance;
                if (manager != null)
                {
                    int entranceID = manager.GetFirstLockedEntranceID();
                    if (entranceID != -1)
                    {
                        manager.UnlockEntrance(entranceID);
                        ModLogger.Info($"SewerManager.SetSewerUnlocked_Client: Unlocked entrance {entranceID}");
                    }
                }
                
                // Still let original method run to maintain compatibility
                return true;
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in SewerManager.SetSewerUnlocked_Client prefix", ex);
                return true; // Let original method run on error
            }
        }

        /// <summary>
        /// Patch OnSpawnServer to sync per-entrance unlock states to joining clients
        /// </summary>
        [HarmonyPatch(typeof(SewerManager), "OnSpawnServer")]
        [HarmonyPostfix]
        public static void SewerManager_OnSpawnServer_Postfix(SewerManager __instance, NetworkConnection connection)
        {
            try
            {
                if (connection.IsHost)
                    return; // Host already has all data

                var manager = BetterSewerKeysManager.Instance;
                if (manager == null)
                    return;

                var saveData = manager.GetSaveData();
                if (saveData == null)
                    return;

                // Sync each entrance's unlock state to the joining client
                foreach (var entranceID in manager.GetAllEntranceIDs())
                {
                    if (saveData.IsEntranceUnlocked(entranceID))
                    {
                        // Need to call the client RPC for this specific entrance
                        // Since we don't have per-entrance RPCs, we'll handle this differently
                        // The client will check the save data on load
                        ModLogger.Debug($"SewerManager.OnSpawnServer: Entrance {entranceID} is unlocked, should sync to client");
                    }
                }
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in SewerManager.OnSpawnServer postfix", ex);
            }
        }

        /// <summary>
        /// Patch SetSewerKeyLocation to handle per-entrance key locations
        /// </summary>
        [HarmonyPatch(typeof(SewerManager), "SetSewerKeyLocation")]
        [HarmonyPrefix]
        public static bool SewerManager_SetSewerKeyLocation_Prefix(SewerManager __instance, NetworkConnection conn, int locationIndex)
        {
            try
            {
                // This is called per-entrance, but we need to track which entrance this location belongs to
                // Since we can't modify the method signature, we'll track this via the manager
                var manager = BetterSewerKeysManager.Instance;
                var saveData = manager?.GetSaveData();
                
                if (manager != null && saveData != null)
                {
                    // Find which entrance this location should be assigned to
                    // We'll assign locations sequentially or randomly per entrance
                    int entranceID = manager.GetFirstLockedEntranceID();
                    if (entranceID != -1 && !saveData.KeyLocationIndices.ContainsKey(entranceID))
                    {
                        saveData.SetKeyLocationIndex(entranceID, locationIndex);
                        ModLogger.Debug($"Assigned key location {locationIndex} to entrance {entranceID}");
                    }
                }
                
                // Let original method run
                return true;
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in SewerManager.SetSewerKeyLocation prefix", ex);
                return true; // Let original method run on error
            }
        }

        /// <summary>
        /// Patch SetRandomKeyPossessor to assign different NPCs to different entrances
        /// </summary>
        [HarmonyPatch(typeof(SewerManager), "SetRandomKeyPossessor")]
        [HarmonyPrefix]
        public static bool SewerManager_SetRandomKeyPossessor_Prefix(SewerManager __instance, NetworkConnection conn, int possessorIndex)
        {
            try
            {
                var manager = BetterSewerKeysManager.Instance;
                var saveData = manager?.GetSaveData();
                
                if (manager != null && saveData != null)
                {
                    // Find which entrance this possessor should be assigned to
                    int entranceID = manager.GetFirstLockedEntranceID();
                    if (entranceID != -1 && !saveData.KeyPossessorIndices.ContainsKey(entranceID))
                    {
                        saveData.SetKeyPossessorIndex(entranceID, possessorIndex);
                        ModLogger.Debug($"Assigned key possessor {possessorIndex} to entrance {entranceID}");
                    }
                }
                
                // Let original method run
                return true;
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in SewerManager.SetRandomKeyPossessor prefix", ex);
                return true; // Let original method run on error
            }
        }

        /// <summary>
        /// Patch Load to prevent IsRandomWorldKeyCollected from being set to true and prevent calling SetRandomWorldKeyCollected
        /// </summary>
        [HarmonyPatch(typeof(SewerManager), "Load")]
        [HarmonyPrefix]
        public static void SewerManager_Load_Prefix(SewerManager __instance, ref bool __state)
        {
            try
            {
                var manager = BetterSewerKeysManager.Instance;
                // Store whether we should block the SetRandomWorldKeyCollected call
                __state = manager != null && !manager.AreAllEntrancesUnlocked();
            }
            catch
            {
                __state = false;
            }
        }

        /// <summary>
        /// Patch Load postfix to prevent IsRandomWorldKeyCollected from being set to true until all entrances are unlocked
        /// </summary>
        [HarmonyPatch(typeof(SewerManager), "Load")]
        [HarmonyPostfix]
        public static void SewerManager_Load_Postfix(SewerManager __instance, bool __state)
        {
            try
            {
                if (!__state)
                    return; // All entrances unlocked, let normal behavior happen

                var manager = BetterSewerKeysManager.Instance;
                if (manager == null)
                    return;

                // Force IsRandomWorldKeyCollected to false if not all entrances are unlocked
                // Use reflection to set the private property
                var property = typeof(SewerManager).GetProperty("IsRandomWorldKeyCollected", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                
                if (property != null && property.CanWrite)
                {
                    property.SetValue(__instance, false);
                    ModLogger.Debug("SewerManager.Load: Forced IsRandomWorldKeyCollected to false (not all entrances unlocked)");
                    
                    // Re-enable pickup if it was disabled
                    if (__instance.RandomWorldSewerKeyPickup != null && !__instance.RandomWorldSewerKeyPickup.gameObject.activeSelf)
                    {
                        var saveData = manager.GetSaveData();
                        if (saveData != null)
                        {
                            // Find an entrance that hasn't had its key collected yet
                            int entranceID = manager.GetFirstLockedEntranceID();
                            if (entranceID != -1)
                            {
                                int locationIndex = saveData.GetKeyLocationIndex(entranceID);
                                if (locationIndex >= 0 && locationIndex < __instance.RandomSewerKeyLocations.Length)
                                {
                                    __instance.SetSewerKeyLocation(null, locationIndex);
                                    __instance.RandomWorldSewerKeyPickup.gameObject.SetActive(true);
                                    ModLogger.Debug($"SewerManager.Load: Re-enabled key pickup for entrance {entranceID}");
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in SewerManager.Load postfix", ex);
            }
        }

        /// <summary>
        /// Patch SetRandomKeyCollected_Server to prevent RPC from being sent until all entrances are unlocked
        /// </summary>
        [HarmonyPatch(typeof(SewerManager), "SetRandomKeyCollected_Server")]
        [HarmonyPrefix]
        public static bool SewerManager_SetRandomKeyCollected_Server_Prefix(SewerManager __instance)
        {
            try
            {
                var manager = BetterSewerKeysManager.Instance;
                if (manager != null && !manager.AreAllEntrancesUnlocked())
                {
                    // Don't send RPC if not all entrances are unlocked
                    // This prevents the RPC chain from disabling the pickup
                    ModLogger.Debug("SewerManager.SetRandomKeyCollected_Server: Blocked RPC (not all entrances unlocked)");
                    return false; // Skip original method
                }
                
                return true; // Let original method run
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in SewerManager.SetRandomKeyCollected_Server prefix", ex);
                return true; // Let original method run on error
            }
        }

        /// <summary>
        /// Patch SetRandomWorldKeyCollected to track per-entrance world key collection
        /// and prevent base game from marking it as collected until all entrances are unlocked
        /// </summary>
        [HarmonyPatch(typeof(SewerManager), "SetRandomWorldKeyCollected")]
        [HarmonyPrefix]
        public static bool SewerManager_SetRandomWorldKeyCollected_Prefix(SewerManager __instance)
        {
            try
            {
                var manager = BetterSewerKeysManager.Instance;
                var saveData = manager?.GetSaveData();
                
                if (manager != null && saveData != null)
                {
                    // Find which entrance this world key belongs to based on current pickup location
                    int currentLocationIndex = __instance.RandomSewerKeyLocationIndex;
                    
                    // Find entrance ID that has this location assigned
                    foreach (var entranceID in manager.GetAllEntranceIDs())
                    {
                        if (saveData.GetKeyLocationIndex(entranceID) == currentLocationIndex)
                        {
                            saveData.SetRandomWorldKeyCollected(entranceID, true);
                            ModLogger.Debug($"Marked world key as collected for entrance {entranceID} (location {currentLocationIndex})");
                            break;
                        }
                    }

                    // Check if all entrances are unlocked - if not, prevent base game from marking as collected
                    if (!manager.AreAllEntrancesUnlocked())
                    {
                        // Don't let base game mark as collected - we'll handle pickup deactivation ourselves
                        // We'll re-enable it on day pass if needed
                        __instance.RandomWorldSewerKeyPickup.gameObject.SetActive(false);
                        
                        // Don't call original method - we've handled it
                        // This prevents the RPC chain from starting
                        return false;
                    }
                }
                
                // All entrances unlocked, let original method run to handle pickup deactivation
                return true;
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in SewerManager.SetRandomWorldKeyCollected prefix", ex);
                return true; // Let original method run on error
            }
        }

        /// <summary>
        /// Patch EnsureKeyPosessorHasKey to give keys to possessors per entrance
        /// </summary>
        [HarmonyPatch(typeof(SewerManager), "EnsureKeyPosessorHasKey")]
        [HarmonyPrefix]
        public static bool SewerManager_EnsureKeyPosessorHasKey_Prefix(SewerManager __instance)
        {
            try
            {
                var manager = BetterSewerKeysManager.Instance;
                var saveData = manager?.GetSaveData();
                
                if (manager == null || saveData == null || __instance.SewerKeyPossessors == null)
                {
                    return true; // Let original method run
                }

                // Give key to each possessor for their assigned entrance
                foreach (var entranceID in manager.GetAllEntranceIDs())
                {
                    int possessorIndex = saveData.GetKeyPossessorIndex(entranceID);
                    if (possessorIndex >= 0 && possessorIndex < __instance.SewerKeyPossessors.Length)
                    {
                        var possessor = __instance.SewerKeyPossessors[possessorIndex];
                        if (possessor?.NPC != null && possessor.NPC.Inventory != null)
                        {
                            if (possessor.NPC.Inventory._GetItemAmount(__instance.SewerKeyItem.ID) == 0)
                            {
                                possessor.NPC.Inventory.InsertItem(__instance.SewerKeyItem.GetDefaultInstance());
                                ModLogger.Debug($"Ensured possessor {possessorIndex} has key for entrance {entranceID}");
                            }
                        }
                    }
                }
                
                // Don't call original method - we've handled it
                return false;
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in SewerManager.EnsureKeyPosessorHasKey prefix", ex);
                return true; // Let original method run on error
            }
        }
    }
}
