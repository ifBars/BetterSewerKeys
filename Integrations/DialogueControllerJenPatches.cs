#if MONO
using ScheduleOne.Dialogue;
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.Map;
using ScheduleOne.NPCs.Relation;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Money;
#else
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs.Relation;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Money;
#endif
using HarmonyLib;
using BetterSewerKeys.Utils;

namespace BetterSewerKeys.Integrations
{
    /// <summary>
    /// Harmony patches for DialogueController_Jen to allow buying keys for locked entrances sequentially
    /// </summary>
    [HarmonyPatch]
    public static class DialogueControllerJenPatches
    {
        /// <summary>
        /// Patch CanBuyKey to check if there are any locked entrances
        /// </summary>
        [HarmonyPatch(typeof(DialogueController_Jen), "CanBuyKey")]
        [HarmonyPrefix]
        public static bool DialogueController_Jen_CanBuyKey_Prefix(DialogueController_Jen __instance, ref bool __result, ref string invalidReason)
        {
            try
            {
                var manager = BetterSewerKeysManager.Instance;
                if (manager == null)
                {
                    // Fallback to original check if manager not initialized
                    return true;
                }

                // Check if all entrances are unlocked
                if (manager.AreAllEntrancesUnlocked())
                {
                    invalidReason = "All sewer entrances are already unlocked";
                    __result = false;
                    return false; // Skip original method
                }

                // Check if there are any locked entrances
                int firstLockedEntranceID = manager.GetFirstLockedEntranceID();
                if (firstLockedEntranceID == -1)
                {
                    invalidReason = "All sewer entrances are already unlocked";
                    __result = false;
                    return false; // Skip original method
                }

                // Still check relationship requirement from original method
                if (__instance.npc.RelationData.RelationDelta < __instance.MinRelationToBuyKey)
                {
                    invalidReason = "'" + RelationshipCategory.GetCategory(__instance.MinRelationToBuyKey).ToString() + "' relationship required";
                    __result = false;
                    return false; // Skip original method
                }

                // Player can buy key
                __result = true;
                return false; // Skip original method
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in DialogueController_Jen.CanBuyKey prefix", ex);
                return true; // Let original method run on error
            }
        }

        /// <summary>
        /// Patch ChoiceCallback to give player a key for the first locked entrance
        /// </summary>
        [HarmonyPatch(typeof(DialogueController_Jen), "ChoiceCallback")]
        [HarmonyPrefix]
        public static bool DialogueController_Jen_ChoiceCallback_Prefix(DialogueController_Jen __instance, string choiceLabel)
        {
            try
            {
                if (choiceLabel != "CHOICE_CONFIRM")
                {
                    // Let original method handle non-confirm choices
                    return true;
                }

                var manager = BetterSewerKeysManager.Instance;
                if (manager == null)
                {
                    // Fallback to original behavior if manager not initialized
                    return true;
                }

                // Check if all entrances are unlocked
                if (manager.AreAllEntrancesUnlocked())
                {
                    ModLogger.Warning("DialogueController_Jen.ChoiceCallback: All entrances unlocked, cannot buy key");
                    return false; // Don't process purchase
                }

                // Find first locked entrance
                int firstLockedEntranceID = manager.GetFirstLockedEntranceID();
                if (firstLockedEntranceID == -1)
                {
                    ModLogger.Warning("DialogueController_Jen.ChoiceCallback: No locked entrances found");
                    return false; // Don't process purchase
                }

                // Check if player has enough cash
                if (NetworkSingleton<MoneyManager>.Instance.cashBalance < __instance.KeyItem.BasePurchasePrice)
                {
                    // Let original method handle insufficient cash error
                    return true;
                }

                // Deduct cash
                NetworkSingleton<MoneyManager>.Instance.ChangeCashBalance(0f - __instance.KeyItem.BasePurchasePrice, visualizeChange: true, playCashSound: true);
                
                // Give cash to NPC
                __instance.npc.Inventory.InsertItem(NetworkSingleton<MoneyManager>.Instance.GetCashInstance(__instance.KeyItem.BasePurchasePrice));
                
                // Give player the sewer key item (same item for all entrances, but tracks which entrance it unlocks via context)
                // The key will unlock the first locked entrance when used
                PlayerSingleton<PlayerInventory>.Instance.AddItemToInventory(__instance.KeyItem.GetDefaultInstance());
                
                ModLogger.Info($"DialogueController_Jen.ChoiceCallback: Player bought key for entrance {firstLockedEntranceID}");

                // Don't call original method - we've handled it
                return false;
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("Error in DialogueController_Jen.ChoiceCallback prefix", ex);
                return true; // Let original method run on error
            }
        }
    }
}
