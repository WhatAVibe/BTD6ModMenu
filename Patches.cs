using System;
using BTD_Mod_Helper.Extensions;
using HarmonyLib;
using Il2CppAssets.Scripts.Models.TowerSets;
using Il2CppAssets.Scripts.Simulation;
using Il2CppAssets.Scripts.Simulation.Input;
using Il2CppSystem.Collections.Generic;

namespace BTD6ModMenu
{
    /// <summary>
    /// Unlimited 5th tiers, integrated from Unlimited5thTiers by doombubbles.
    /// This adds tier-5 capacity instead of removing restrictions (which locked towers out).
    /// </summary>
    [HarmonyPatch(typeof(TowerInventory), nameof(TowerInventory.SetTowerTierRestrictions))]
    internal static class Unlimited5thTiersPatch
    {
        [HarmonyPostfix]
        private static void Postfix(TowerInventory __instance, IEnumerable<TowerDetailsModel> towers)
        {
            if (!Sandbox.UnlimitedT5) return;

            towers.ForEach(towerDetails =>
            {
                if (towerDetails.Is<ShopTowerDetailsModel>())
                {
                    for (var path = 0; path < 3; path++)
                    {
                        __instance.AddTierRestriction(towerDetails.towerId, path, 5, 9999999);
                    }
                }
            });
        }
    }

    /// <summary>Blocks the defeat transition while infinite lives/god mode is enabled.</summary>
    [HarmonyPatch(typeof(Simulation), nameof(Simulation.SetDefeat))]
    internal static class InfiniteLivesPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !Sandbox.InfiniteLives;
        }
    }
}
