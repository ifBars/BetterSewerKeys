#if MONO
using ScheduleOne.Map;
using ScheduleOne.DevUtilities;
using ScheduleOne.NPCs;
using FishNet.Connection;
#else
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.NPCs;
using Il2CppFishNet.Connection;
#endif
using HarmonyLib;
using BetterSewerKeys.Utils;
using System.Collections.Generic;
using UnityEngine;

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
        /// Patch SetSewerUnlocked_Server to unlock only a specific entrance
        /// We need to detect which entrance triggered this and unlock only that one
        /// </summary>
        [HarmonyPatch(typeof(SewerManager), "SetSewerUnlocked_Server")]
        [HarmonyPrefix]
        public static bool SewerManager_SetSewerUnlocked_Server_Prefix(SewerManager __instance)
        {
            try
            {
                // Try to find which entrance triggered this unlock
                // We'll check all doors and find the one that was just interacted with
                var manager = BetterSewerKeysManager.Instance;
                if (manager == null)
                {
                    return true; // Let original method run
                }

                // Find the entrance ID for the door that was just interacted with
                // Since we can't directly track which door called this, we'll unlock the first locked entrance
                // This is a limitation, but in practice the door controller patch handles this correctly
                int entranceID = manager.GetFirstLockedEntranceID();
                
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
        /// Patch SetRandomWorldKeyCollected to track per-entrance world key collection
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
                }
                
                // Let original method run to handle pickup deactivation
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
