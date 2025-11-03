#if MONO
using ScheduleOne.Doors;
using ScheduleOne.Map;
using ScheduleOne.DevUtilities;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Interaction;
#else
using Il2CppScheduleOne.Doors;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Interaction;
#endif
using HarmonyLib;
using UnityEngine;
using BetterSewerKeys.Utils;

namespace BetterSewerKeys.Integrations
{
    /// <summary>
    /// Harmony patches for SewerDoorController to enable per-entrance key checking
    /// </summary>
    [HarmonyPatch]
    public static class SewerDoorControllerPatches
    {
        private static readonly System.Reflection.FieldInfo? EntranceIDField = typeof(SewerDoorController)
            .GetField("_entranceID", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) 
            ?? typeof(SewerDoorController).GetField("EntranceID", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        /// <summary>
        /// Store entrance ID on door controller using reflection
        /// </summary>
        private static void SetEntranceID(SewerDoorController door, int entranceID)
        {
            if (EntranceIDField != null)
            {
                EntranceIDField.SetValue(door, entranceID);
            }
            else
            {
                // Fallback: store in a static dictionary
                _entranceIDMap[door] = entranceID;
            }
        }

        /// <summary>
        /// Get entrance ID from door controller
        /// </summary>
        private static int GetEntranceID(SewerDoorController door)
        {
            if (EntranceIDField != null)
            {
                var value = EntranceIDField.GetValue(door);
                return value is int id ? id : -1;
            }
            else
            {
                return _entranceIDMap.TryGetValue(door, out int id) ? id : -1;
            }
        }

        private static readonly System.Collections.Generic.Dictionary<SewerDoorController, int> _entranceIDMap = new();

        /// <summary>
        /// Patch Awake to register door with manager and get entrance ID
        /// </summary>
        [HarmonyPatch(typeof(SewerDoorController), "Awake")]
        [HarmonyPostfix]
        public static void SewerDoorController_Awake_Postfix(SewerDoorController __instance)
        {
            try
            {
                // Register door and get entrance ID
                int entranceID = BetterSewerKeysManager.Instance.RegisterDoor(__instance);
                SetEntranceID(__instance, entranceID);
                
                ModLogger.Debug($"SewerDoorController.Awake: Registered door {__instance.gameObject.name} with entrance ID {entranceID}");
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in SewerDoorController.Awake postfix", ex);
            }
        }

        /// <summary>
        /// Patch CanPlayerAccess to check per-entrance unlock state instead of global
        /// </summary>
        [HarmonyPatch(typeof(SewerDoorController), "CanPlayerAccess")]
        [HarmonyPrefix]
        public static bool SewerDoorController_CanPlayerAccess_Prefix(SewerDoorController __instance, EDoorSide side, ref bool __result, ref string reason)
        {
            try
            {
                reason = string.Empty;
                
                // Only check for exterior side when door is locked
                if (side == EDoorSide.Exterior)
                {
                    int entranceID = GetEntranceID(__instance);
                    
                    if (entranceID == -1)
                    {
                        // Door not registered yet, let original method run
                        return true;
                    }

                    // Check if this specific entrance is unlocked
                    bool isUnlocked = BetterSewerKeysManager.Instance.IsEntranceUnlocked(entranceID);
                    
                    if (!isUnlocked && !__instance.IsOpen)
                    {
                        // Check if player has the key item
                        var sewerManager = NetworkSingleton<SewerManager>.Instance;
                        if (sewerManager != null)
                        {
                            var playerInventory = PlayerSingleton<PlayerInventory>.Instance;
                            if (playerInventory != null && playerInventory.GetAmountOfItem(sewerManager.SewerKeyItem.ID) != 0)
                            {
                                __result = true;
                                return false; // Skip original method
                            }
                        }
                        
                        reason = sewerManager?.SewerKeyItem.Name + " required";
                        __result = false;
                        return false; // Skip original method
                    }
                }
                
                // Let original method handle other cases
                return true;
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in SewerDoorController.CanPlayerAccess prefix", ex);
                return true; // Let original method run on error
            }
        }

        private static readonly System.Reflection.FieldInfo? ExteriorIntObjsField = typeof(DoorController)
            .GetField("ExteriorIntObjs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        /// <summary>
        /// Get ExteriorIntObjs array via reflection
        /// </summary>
        private static InteractableObject[]? GetExteriorIntObjs(DoorController door)
        {
            if (ExteriorIntObjsField != null)
            {
                return ExteriorIntObjsField.GetValue(door) as InteractableObject[];
            }
            return null;
        }

        /// <summary>
        /// Patch ExteriorHandleInteracted to unlock only this specific entrance
        /// </summary>
        [HarmonyPatch(typeof(SewerDoorController), "ExteriorHandleInteracted")]
        [HarmonyPrefix]
        public static bool SewerDoorController_ExteriorHandleInteracted_Prefix(SewerDoorController __instance)
        {
            try
            {
                int entranceID = GetEntranceID(__instance);
                
                if (entranceID == -1)
                {
                    // Door not registered yet, let original method run
                    return true;
                }

                // Check if entrance is already unlocked
                if (BetterSewerKeysManager.Instance.IsEntranceUnlocked(entranceID))
                {
                    // Already unlocked, let original method handle opening
                    return true;
                }

                // Check if player can access (has key)
                if (__instance.CanPlayerAccess(EDoorSide.Exterior))
                {
                    var sewerManager = NetworkSingleton<SewerManager>.Instance;
                    if (sewerManager != null)
                    {
                        // Get ExteriorIntObjs via reflection
                        var exteriorIntObjs = GetExteriorIntObjs(__instance);
                        if (exteriorIntObjs != null && exteriorIntObjs.Length > 0)
                        {
                            // Play unlock sound
                            sewerManager.SewerUnlockSound.transform.position = exteriorIntObjs[0].transform.position;
                            sewerManager.SewerUnlockSound.Play();
                        }
                        
                        // Unlock only this specific entrance via our custom method
                        BetterSewerKeysManager.Instance.UnlockEntrance(entranceID);
                        
                        // Remove key from player inventory
                        var playerInventory = PlayerSingleton<PlayerInventory>.Instance;
                        if (playerInventory != null)
                        {
                            playerInventory.RemoveAmountOfItem(sewerManager.SewerKeyItem.ID);
                        }
                        
                        ModLogger.Info($"Unlocked entrance {entranceID} via door interaction");
                        
                        // Don't call original method - we've handled it
                        return false;
                    }
                }
                
                // Let original method handle if we can't process
                return true;
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in SewerDoorController.ExteriorHandleInteracted prefix", ex);
                return true; // Let original method run on error
            }
        }
    }
}
