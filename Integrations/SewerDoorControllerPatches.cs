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
        /// Track the last door that was interacted with, so we know which entrance to unlock
        /// </summary>
        public static SewerDoorController? _lastInteractedDoor = null;

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
                    
                    // If unlocked, let original method handle (it will return true)
                    if (isUnlocked)
                    {
                        return true; // Let original method run
                    }
                    
                    // Entrance is locked - check if player has the key item
                    if (!__instance.IsOpen)
                    {
                        var sewerManager = NetworkSingleton<SewerManager>.Instance;
                        if (sewerManager != null)
                        {
                            var playerInventory = PlayerSingleton<PlayerInventory>.Instance;
                            if (playerInventory != null && playerInventory.GetAmountOfItem(sewerManager.SewerKeyItem.ID) != 0)
                            {
                                __result = true;
                                return false; // Skip original method - player has key
                            }
                        }
                        
                        reason = sewerManager?.SewerKeyItem.Name + " required";
                        __result = false;
                        return false; // Skip original method - player doesn't have key
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
        /// Patch ExteriorHandleInteracted to prevent duplicate unlock attempts
        /// We need to prevent the unlock logic from running if this entrance is already unlocked
        /// </summary>
        [HarmonyPatch(typeof(SewerDoorController), "ExteriorHandleInteracted")]
        [HarmonyPrefix]
        public static void SewerDoorController_ExteriorHandleInteracted_Prefix(SewerDoorController __instance, out bool __state)
        {
            // Track this door as the last interacted one
            _lastInteractedDoor = __instance;
            
            // Track if this entrance was already unlocked before the original method ran
            int entranceID = GetEntranceID(__instance);
            __state = entranceID != -1 && BetterSewerKeysManager.Instance.IsEntranceUnlocked(entranceID);
        }

        /// <summary>
        /// Patch ExteriorHandleInteracted postfix to prevent duplicate unlock attempts
        /// </summary>
        [HarmonyPatch(typeof(SewerDoorController), "ExteriorHandleInteracted")]
        [HarmonyPostfix]
        public static void SewerDoorController_ExteriorHandleInteracted_Postfix(SewerDoorController __instance, bool __state)
        {
            try
            {
                // If entrance was already unlocked before original method ran, prevent duplicate unlock
                if (__state)
                {
                    int entranceID = GetEntranceID(__instance);
                    
                    // Check if the original method tried to unlock (consumed a key)
                    var sewerManager = NetworkSingleton<SewerManager>.Instance;
                    if (sewerManager != null)
                    {
                        var playerInventory = PlayerSingleton<PlayerInventory>.Instance;
                        if (playerInventory != null)
                        {
                            // Check if a key was consumed but shouldn't have been
                            // The original method checks !IsSewerUnlocked, which we patch to return false until all unlocked
                            // So it would have tried to unlock. But since this entrance is already unlocked,
                            // SetSewerUnlocked_Server would have unlocked a different entrance (first locked one)
                            // So we need to check if SetSewerUnlocked_Server was called and undo it if needed
                            
                            // Actually, the issue is that SetSewerUnlocked_Server will unlock the first locked entrance
                            // But if the player used a key on an already-unlocked entrance, we shouldn't unlock anything
                            // We need to prevent SetSewerUnlocked_Server from running in this case
                            
                            // The best way is to clear _lastInteractedDoor before SetSewerUnlocked_Server runs
                            // But that's timing-dependent. Instead, we'll check in SetSewerUnlocked_Server if the entrance
                            // was already unlocked before the interaction
                        }
                    }
                }
                
                // Clear tracked door after a short delay to ensure SetSewerUnlocked_Server has processed it
                MelonLoader.MelonCoroutines.Start(ClearTrackedDoor(__instance));
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in SewerDoorController.ExteriorHandleInteracted postfix", ex);
            }
        }

        private static System.Collections.IEnumerator ClearTrackedDoor(SewerDoorController door)
        {
            yield return new WaitForSeconds(0.1f);
            if (_lastInteractedDoor == door)
            {
                _lastInteractedDoor = null;
            }
        }
    }
}
