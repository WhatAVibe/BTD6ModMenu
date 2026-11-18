using System;
using System.Collections.Generic;
using System.Linq;
using BTD_Mod_Helper.Api.Helpers;
using BTD_Mod_Helper.Extensions;
using Il2CppAssets.Scripts.Models.Map;
using Il2CppAssets.Scripts.Models.Towers.Projectiles;
using Il2CppAssets.Scripts.Models.Towers.Projectiles.Behaviors;
using Il2CppAssets.Scripts.Models.Towers.Weapons;
using Il2CppAssets.Scripts.Models.Towers.Behaviors.Attack;
using Il2CppAssets.Scripts.Simulation.Towers.Weapons;
using Il2CppAssets.Scripts.Simulation.Towers.Behaviors.Attack;
using Il2CppAssets.Scripts.Simulation.Towers.Behaviors;
using Il2CppAssets.Scripts.Models.Towers;
using Il2CppAssets.Scripts.Simulation;
using Il2CppAssets.Scripts.Simulation.Input;
using Il2CppAssets.Scripts.Simulation.Towers;
using Il2CppAssets.Scripts.Unity;
using Il2CppAssets.Scripts.Unity.Bridge;
using Il2CppAssets.Scripts.Unity.UI_New.InGame;
using Il2CppAssets.Scripts.Unity.UI_New.InGame.RightMenu;
using Il2CppAssets.Scripts.Unity.UI_New.InGame.StoreMenu;
using Il2CppAssets.Scripts.Utils;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace BTD6ModMenu
{
    /// <summary>
    /// In-match sandbox state and actions. Everything is opt-in and only runs while a match is active.
    /// </summary>
    public static class Sandbox
    {
        public static bool InfiniteCash;
        public static bool InfiniteLives;
        public static bool NoCooldowns;
        public static bool FreezeTowers;
        public static bool PlaceAnywhere;
        public static bool IgnoreTowerLimits;
        public static bool SandboxRules;
        public static bool UnlimitedT5 = true;

        public static float GameSpeed = 1f;
        public static float DamageMultiplier = 1f;
        private static readonly List<(DamageModel model, float damage, float maxDamage)> GlobalDamageOriginals = new();
        private static float _lastDamageApply = -999f;

        private static bool _placeAnywhereApplied;
        private static bool _freePurchasesApplied;
        private static float _lastLimitApply = -999f;
        private static float _lastErrorLog = -999f;
        private static float _appliedSpeed = 1f;

        private static readonly Dictionary<TowerModel, bool> OriginalIgnoreBlockers = new();
        private static readonly Dictionary<TowerModel, Il2CppStructArray<AreaType>> OriginalAreaTypes = new();
        private static readonly Dictionary<Tower, TowerModel> TowerOriginals = new();
        private static readonly Dictionary<Tower, TowerBuffState> TowerBuffs = new();
        private static float _lastBuffRefresh = -999f;

        public static InGame CurrentInGame => InGame.instance;

        public static bool IsInMatch
        {
            get
            {
                try
                {
                    var inGame = InGame.instance;
                    return inGame != null && inGame.bridge != null && inGame.GetSimulation() != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void Tick()
        {
            try
            {
                if (!IsInMatch)
                {
                    if (Math.Abs(Time.timeScale - 1f) > 0.01f) Time.timeScale = 1f;
                    _appliedSpeed = 1f;
                    return;
                }

                var inGame = InGame.instance;
                var sim = inGame.GetSimulation();

                if (InfiniteCash)
                {
                    inGame.SetCash(999_999_999);
                }

                if (InfiniteLives)
                {
                    sim.bloonLivesLostOverride = 0;
                    if (sim.health != null) sim.health.Value = 999_999;
                    if (sim.maxHealth != null) sim.maxHealth.Value = 999_999;
                }

                sim.instantCooldowns = NoCooldowns;
                sim.pauseWeapons = FreezeTowers;

                if (PlaceAnywhere)
                {
                    if (!_placeAnywhereApplied) ApplyPlaceAnywhere(true);
                }
                else if (_placeAnywhereApplied)
                {
                    ApplyPlaceAnywhere(false);
                }

                if (IgnoreTowerLimits && Time.realtimeSinceStartup - _lastLimitApply > 1f)
                {
                    _lastLimitApply = Time.realtimeSinceStartup;
                    ApplyTowerLimits(inGame);
                }

                if (SandboxRules) sim.SetSandbox(true);

                if (Time.realtimeSinceStartup - _lastBuffRefresh > 1f)
                {
                    _lastBuffRefresh = Time.realtimeSinceStartup;
                    ReapplyBuffsAfterUpgrades();
                }

                ApplyGameSpeed();
            }
            catch (Exception e)
            {
                if (Time.realtimeSinceStartup - _lastErrorLog > 5f)
                {
                    _lastErrorLog = Time.realtimeSinceStartup;
                    MelonLogger.Warning("Sandbox tick: " + e.Message);
                }
            }
        }

        public static void SetGameSpeed(float speed)
        {
            GameSpeed = Math.Max(1f, Math.Min(100f, speed));
            ApplyGameSpeed();
        }

        private static void ApplyGameSpeed()
        {
            var desired = Math.Max(1f, Math.Min(100f, GameSpeed));
            if (Math.Abs(_appliedSpeed - desired) < 0.001f) return;
            _appliedSpeed = desired;

            try
            {
                TimeHelper.OverrideFastForwardTimeScale = desired;

                if (IsInMatch)
                {
                    TimeManager.FastForwardActive = desired > 1.001f;
                }

                MelonLogger.Msg($"Game speed set to {desired:0.##}x.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Game speed failed: " + e.Message);
            }
        }
        // ------------------------------------------------------------------
        //  Instant actions
        // ------------------------------------------------------------------

        public static void ApplyPlaceAnywhere(bool enable)
        {
            try
            {
                if (enable)
                {
                    if (_placeAnywhereApplied) return;
                    _placeAnywhereApplied = true;

                    var allAreas = new AreaType[]
                    {
                        AreaType.track, AreaType.water, AreaType.land, AreaType.unplaceable,
                        AreaType.ice, AreaType.removable, AreaType.waterMermonkey, AreaType.shallowWater
                    };

                    var towers = Game.instance.model.towers;
                    for (int i = 0; i < towers.Length; i++)
                    {
                        var model = towers[i];
                        if (model == null || OriginalIgnoreBlockers.ContainsKey(model)) continue;
                        OriginalIgnoreBlockers[model] = model.ignoreBlockers;
                        OriginalAreaTypes[model] = model.areaTypes;
                        model.ignoreBlockers = true;
                        model.areaTypes = allAreas;
                    }

                    MelonLogger.Msg($"Place Anywhere: patched {OriginalIgnoreBlockers.Count} tower models.");
                }
                else
                {
                    if (!_placeAnywhereApplied) return;
                    _placeAnywhereApplied = false;

                    foreach (var pair in OriginalIgnoreBlockers)
                    {
                        if (pair.Key != null) pair.Key.ignoreBlockers = pair.Value;
                    }
                    foreach (var pair in OriginalAreaTypes)
                    {
                        if (pair.Key != null) pair.Key.areaTypes = pair.Value;
                    }

                    OriginalIgnoreBlockers.Clear();
                    OriginalAreaTypes.Clear();
                    MelonLogger.Msg("Place Anywhere: restored tower models.");
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Place Anywhere failed: " + e.Message);
            }
        }

        public static void ApplyTowerLimits(InGame inGame)
        {
            try
            {
                var ti = inGame.GetTowerInventory();
                if (ti == null) return;

                ti.totalTowerMax = 9999;

                if (ti.towerMaxes != null)
                {
                    var keys = new List<string>();
                    foreach (var pair in ti.towerMaxes) keys.Add(pair.Key);
                    foreach (var key in keys) ti.towerMaxes[key] = 9999;
                }

                // 5th-tier capacity is added by the Unlimited5thTiersPatch, not by
                // removing tier restrictions (which locked towers out entirely).
            }
            catch (Exception e)
            {
                if (Time.realtimeSinceStartup - _lastErrorLog > 10f)
                {
                    _lastErrorLog = Time.realtimeSinceStartup;
                    MelonLogger.Warning("Ignore Tower Limits failed: " + e.Message);
                }
            }
        }

        public static void MakeEverythingFree()
        {
            try
            {
                if (!IsInMatch) return;
                if (_freePurchasesApplied) return;
                _freePurchasesApplied = true;

                var inGame = InGame.instance;
                var sim = inGame.GetSimulation();
                var ti = inGame.GetTowerInventory();
                var towerIds = BTD6ModMenuMain.GetBaseTowerIds().ToArray();

                if (ti != null)
                {
                    foreach (var id in towerIds)
                    {
                        try { ti.AddFreeTowers(id, 9999, "", 0, sim); } catch { }
                    }

                    try
                    {
                        ti.AddTowerDiscount("BTD6ModMenuFreeTowers", towerIds, 999999, 0f, 0f);
                    }
                    catch (Exception e)
                    {
                        MelonLogger.Warning("Tower discount failed: " + e.Message);
                    }
                }

                try
                {
                    var pi = sim.GetPowerInventory(0);
                    var powerIds = BTD6ModMenuMain.GetPowerIds().ToArray();
                    foreach (var id in powerIds)
                    {
                        try { pi.AddFreePowers(id, 9999); } catch { }
                    }
                    pi.AddPowerDiscount("BTD6ModMenuFreePowers", powerIds, 999999, 0f, 0f);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("Power discount failed: " + e.Message);
                }

                MelonLogger.Msg("Free purchases applied to this match.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Make Everything Free failed: " + e.Message);
            }
        }

        public static void ClearBloons()
        {
            if (!IsInMatch) return;
            try
            {
                InGame.instance.GetSimulation().DestroyAllBloons();
                MelonLogger.Msg("Cleared all bloons.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Clear bloons failed: " + e.Message);
            }
        }

        public static void WinGame()
        {
            if (!IsInMatch) return;
            try
            {
                InGame.instance.GetSimulation().SetVictory();
                MelonLogger.Msg("SetVictory called.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Win game failed: " + e.Message);
            }
        }

        public static void LoseGame()
        {
            if (!IsInMatch) return;
            try
            {
                InGame.instance.GetSimulation().SetDefeat();
                MelonLogger.Msg("SetDefeat called.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Lose game failed: " + e.Message);
            }
        }

        public static void SetRound(int round)
        {
            if (!IsInMatch) return;
            try
            {
                if (round < 1) round = 1;
                InGame.instance.SetRound(round);
                MelonLogger.Msg($"Set round to {round}.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Set round failed: " + e.Message);
            }
        }

        public static int GetRound()
        {
            try
            {
                return IsInMatch ? InGame.instance.GetSimulation().GetCurrentRound() : 0;
            }
            catch
            {
                return 0;
            }
        }

        public static void SpawnBloon(string bloonName, int count = 1)
        {
            if (!IsInMatch) return;
            try
            {
                InGame.instance.SpawnBloons(bloonName, count, 1f);
                MelonLogger.Msg($"Spawned {count}x {bloonName}.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"Spawn '{bloonName}' failed: {e.Message}");
            }
        }

        public static void SellAllTowers()
        {
            if (!IsInMatch) return;
            try
            {
                var towers = InGame.instance.GetTowers();
                InGame.instance.SellTowers(towers);
                MelonLogger.Msg($"Sold {towers?.Count ?? 0} towers.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Sell all failed: " + e.Message);
            }
        }

        public static List<TowerInfo> GetTowers()
        {
            var result = new List<TowerInfo>();
            if (!IsInMatch) return result;

            try
            {
                var towers = InGame.instance.GetTowers();
                for (int i = 0; i < towers.Count; i++)
                {
                    var tower = towers[i];
                    if (tower == null) continue;

                    var model = tower.towerModel;
                    var tiers = model?.tiers;
                    string tierText = tiers != null && tiers.Length >= 3
                        ? $"{tiers[0]}-{tiers[1]}-{tiers[2]}"
                        : "";

                    result.Add(new TowerInfo
                    {
                        Index = i,
                        Tower = tower,
                        Name = model?.baseId ?? model?.name ?? "?",
                        Tiers = tierText,
                        Damage = tower.damageDealt
                    });
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("GetTowers failed: " + e.Message);
            }

            return result;
        }

        public static string GetBuffSummary(Tower tower)
        {
            if (tower == null || !TowerBuffs.TryGetValue(tower, out var state)) return "";

            var parts = new List<string>();
            if (state.Range > 1.001f) parts.Add($"R{state.Range:0.#}");
            if (state.Damage > 1.001f) parts.Add($"D{state.Damage:0.#}");
            if (state.Speed > 1.001f) parts.Add($"S{state.Speed:0.#}");
            if (state.Pierce > 1.001f) parts.Add($"P{state.Pierce:0.#}");
            return parts.Count == 0 ? "" : "[" + string.Join(" ", parts) + "]";
        }

        public static void SellTower(Tower tower)
        {
            if (!IsInMatch || tower == null) return;
            try
            {
                InGame.instance.SellTower(tower);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Sell tower failed: " + e.Message);
            }
        }

        /// <summary>Multiplies one stat on a single placed tower. Multipliers stack from the current model.</summary>
        public static void BuffTower(Tower tower, string stat, float multiplier)
        {
            if (!IsInMatch || tower == null) return;

            try
            {
                var current = tower.towerModel;
                if (current == null) return;

                if (!TowerOriginals.ContainsKey(tower))
                {
                    TowerOriginals[tower] = current.Clone().Cast<TowerModel>();
                }

                var model = current.Clone().Cast<TowerModel>();
                int changed = 0;

                switch (stat)
                {
                    case "range":
                        model.range = Math.Max(0.1f, Math.Min(30f, model.range * multiplier));
                        var attacks = model.GetAttackModels();
                        if (attacks != null)
                        {
                            for (int a = 0; a < attacks.Count; a++)
                            {
                                var attack = attacks[a];
                                if (attack != null) attack.range = Math.Max(0.1f, Math.Min(30f, attack.range * multiplier));
                            }
                        }
                        changed = 1;
                        break;

                    case "damage":
                        changed = MultiplyDamage(model, multiplier);
                        break;

                    case "speed":
                        changed = MultiplyAttackSpeed(model, multiplier);
                        break;

                    case "pierce":
                        changed = MultiplyPierce(model, multiplier);
                        break;
                }

                if (changed == 0)
                {
                    MelonLogger.Warning($"Buff {stat}: nothing changed on {model.baseId}.");
                    return;
                }

                tower.SetTowerModel(model);
                try { tower.UpdatedModel(model); } catch { }

                if (stat == "range" || stat == "pierce" || stat == "damage")
                {
                    ApplyLiveStats(tower, stat, multiplier);
                }

                MelonLogger.Msg($"Buffed {model.baseId} {stat} x{multiplier}.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"Buff {stat} failed: {e.Message}");
            }
        }

        private static void ApplyLiveStats(Tower tower, string stat, float multiplier)
        {
            try
            {
                if (stat == "range")
                {
                    var attacks = tower.attackBehaviorsInDependants;
                    if (attacks == null) return;

                    var snapshot = new List<AttackBehavior>();
                    var enumerator = new Il2CppEnumerator<AttackBehavior>(attacks.GetEnumerator());
                    while (enumerator.MoveNext()) snapshot.Add(enumerator.Current);

                    int rangeCount = 0;
                    foreach (var behavior in snapshot)
                    {
                        var attack = behavior?.attack;
                        if (attack == null) continue;

                        var attackModel = attack.attackModel;
                        if (attackModel != null)
                        {
                            attackModel.range = Math.Max(0.1f, Math.Min(30f, attackModel.range * multiplier));
                            attack.range = attackModel.range;
                        }
                        else
                        {
                            attack.range = Math.Max(0.1f, Math.Min(30f, attack.range * multiplier));
                        }

                        attack.UpdateRange();
                        rangeCount++;
                    }
                    MelonLogger.Msg($"Live range updated on {rangeCount} attacks.");
                    return;
                }

                var weapons = tower.weaponBehaviorsInDependants;
                if (weapons == null) return;

                var weaponSnapshot = new List<WeaponBehavior>();
                var weaponEnumerator = new Il2CppEnumerator<WeaponBehavior>(weapons.GetEnumerator());
                while (weaponEnumerator.MoveNext()) weaponSnapshot.Add(weaponEnumerator.Current);

                int liveCount = 0;
                foreach (var behavior in weaponSnapshot)
                {
                    var weapon = behavior?.weapon;
                    var weaponModel = weapon?.weaponModel;
                    var projectile = weaponModel?.projectile;
                    if (projectile == null) continue;

                    if (stat == "pierce")
                    {
                        projectile.pierce = Math.Max(0.1f, projectile.pierce * multiplier);
                        if (projectile.maxPierce > 0f) projectile.maxPierce *= multiplier;
                    }
                    else if (stat == "damage")
                    {
                        var damages = projectile.GetDescendants<DamageModel>();
                        if (damages == null) continue;

                        var damageEnumerator = new Il2CppEnumerator<DamageModel>(damages.GetEnumerator());
                        while (damageEnumerator.MoveNext())
                        {
                            var damage = damageEnumerator.Current;
                            if (damage == null) continue;
                            damage.damage = Math.Max(0f, damage.damage * multiplier);
                            if (damage.maxDamage > 0f) damage.maxDamage *= multiplier;
                        }
                    }
                    liveCount++;
                }
                MelonLogger.Msg($"Live {stat} updated on {liveCount} weapons.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Live stat update failed: " + e.Message);
            }
        }

        private static int MultiplyDamage(TowerModel model, float multiplier)
        {
            var damages = model.GetDescendants<DamageModel>();
            if (damages == null) return 0;

            int changed = 0;
            var damageEnumerator = new Il2CppEnumerator<DamageModel>(damages.GetEnumerator());
            while (damageEnumerator.MoveNext())
            {
                var damage = damageEnumerator.Current;
                if (damage == null) continue;
                damage.damage = Math.Max(0f, damage.damage * multiplier);
                if (damage.maxDamage > 0f) damage.maxDamage *= multiplier;
                changed++;
            }
            return changed;
        }
        private static int MultiplyAttackSpeed(TowerModel model, float multiplier)
        {
            int changed = 0;
            var weapons = model.GetWeapons();
            if (weapons == null) return 0;

            for (int w = 0; w < weapons.Count; w++)
            {
                var weapon = weapons[w];
                if (weapon == null) continue;

                weapon.rate = Math.Max(0.001f, weapon.rate / multiplier);
                if (weapon.rateFrames > 0)
                {
                    weapon.rateFrames = Math.Max(1, (int)(weapon.rateFrames / multiplier));
                }
                changed++;
            }

            return changed;
        }

        private static int MultiplyPierce(TowerModel model, float multiplier)
        {
            int changed = 0;
            var weapons = model.GetWeapons();
            if (weapons == null) return 0;

            for (int w = 0; w < weapons.Count; w++)
            {
                var projectile = weapons[w]?.projectile;
                if (projectile == null) continue;

                projectile.pierce = Math.Max(0.1f, projectile.pierce * multiplier);
                if (projectile.maxPierce > 0f) projectile.maxPierce *= multiplier;
                changed++;
            }

            return changed;
        }


        public static void ResetTowerBuffs(Tower tower)
        {
            if (!IsInMatch || tower == null) return;

            try
            {
                if (TowerOriginals.TryGetValue(tower, out var original))
                {
                    tower.SetTowerModel(original.Clone().Cast<TowerModel>());
                    TowerOriginals.Remove(tower);
                    TowerBuffs.Remove(tower);
                    MelonLogger.Msg($"Reset buffs on {tower.towerModel?.baseId}.");
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Reset tower buffs failed: " + e.Message);
            }
        }

        public static void SetDamageMultiplier(float multiplier)
        {
            DamageMultiplier = Math.Max(0.1f, Math.Min(9999f, multiplier));
            ApplyGlobalDamageMultiplier();
        }

        private static void ApplyGlobalDamageMultiplier()
        {
            try
            {
                foreach (var entry in GlobalDamageOriginals)
                {
                    if (entry.model == null) continue;
                    entry.model.damage = entry.damage;
                    entry.model.maxDamage = entry.maxDamage;
                }
                GlobalDamageOriginals.Clear();

                if (DamageMultiplier <= 1.001f) return;

                var model = Game.instance?.model;
                if (model != null)
                {
                    var towers = model.towers;
                    for (int i = 0; i < towers.Length; i++)
                    {
                        CollectGlobalDamage(towers[i], DamageMultiplier);
                    }
                }

                if (IsInMatch)
                {
                    var liveTowers = InGame.instance.GetTowers();
                    for (int i = 0; i < liveTowers.Count; i++)
                    {
                        var tower = liveTowers[i];
                        if (tower == null) continue;
                        CollectGlobalDamage(tower.towerModel, DamageMultiplier);
                    }
                }

                _lastDamageApply = Time.realtimeSinceStartup;
                MelonLogger.Msg($"Global damage multiplier applied: x{DamageMultiplier:0.##}.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Global damage multiplier failed: " + e.Message);
            }
        }

        private static void CollectGlobalDamage(TowerModel model, float multiplier)
        {
            if (model == null) return;

            var damages = model.GetDescendants<DamageModel>();
            if (damages == null) return;

            var damageEnumerator = new Il2CppEnumerator<DamageModel>(damages.GetEnumerator());
            while (damageEnumerator.MoveNext())
            {
                var damage = damageEnumerator.Current;
                if (damage == null) continue;

                bool remembered = false;
                for (int j = 0; j < GlobalDamageOriginals.Count; j++)
                {
                    if (ReferenceEquals(GlobalDamageOriginals[j].model, damage))
                    {
                        remembered = true;
                        break;
                    }
                }

                if (!remembered)
                {
                    GlobalDamageOriginals.Add((damage, damage.damage, damage.maxDamage));
                }

                damage.damage = Math.Max(0f, damage.damage * multiplier);
                if (damage.maxDamage > 0f) damage.maxDamage *= multiplier;
            }
        }
        private static TowerBuffState GetOrCreateBuffState(Tower tower)
        {
            if (!TowerBuffs.TryGetValue(tower, out var state))
            {
                state = new TowerBuffState();
                TowerBuffs[tower] = state;
            }
            return state;
        }

        private static void ReapplyBuffsAfterUpgrades()
        {
            if (!IsInMatch) return;

            try
            {
                var towers = InGame.instance.GetTowers();
                for (int i = 0; i < towers.Count; i++)
                {
                    var tower = towers[i];
                    if (tower == null) continue;
                    if (!TowerBuffs.TryGetValue(tower, out var state)) continue;

                    var currentName = tower.towerModel?.name ?? "";
                    if (string.Equals(currentName, state.LastModel, StringComparison.Ordinal)) continue;

                    // The tower was upgraded: use the upgraded model as the new baseline and
                    // reapply the accumulated multipliers.
                    TowerOriginals[tower] = (TowerModel)tower.towerModel.Clone();
                    var model = (TowerModel)tower.towerModel.Clone();

                    model.range = Math.Max(0.1f, Math.Min(30f, model.range * state.Range));

                    if (state.Damage > 1f) MultiplyDamage(model, state.Damage);
                    if (state.Speed > 1f) MultiplyAttackSpeed(model, state.Speed);
                    if (state.Pierce > 1f) MultiplyPierce(model, state.Pierce);

                    tower.SetTowerModel(model);
                    try { tower.UpdatedModel(model); } catch { }

                    if (state.Range > 1f) ApplyLiveStats(tower, "range", state.Range);
                    if (state.Pierce > 1f) ApplyLiveStats(tower, "pierce", state.Pierce);

                    state.LastModel = tower.towerModel?.name ?? "";
                    MelonLogger.Msg($"Reapplied buffs after upgrade on {model.baseId}.");
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Reapply buffs failed: " + e.Message);
            }
        }

        public static void SetTowerPops(Tower tower, long pops)
        {
            if (!IsInMatch || tower == null) return;
            try
            {
                tower.damageDealt = pops;
                // TowerToSimulation.pops is read-only; damageDealt is the writable stat.
                MelonLogger.Msg($"Set pops to {pops:N0} on {tower.towerModel?.baseId}.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Set pops failed: " + e.Message);
            }
        }

        public static void SetParagonDegree(Tower tower, int degree)
        {
            if (!IsInMatch || tower == null) return;
            try
            {
                var paragon = tower.GetTowerBehavior<ParagonTower>();
                if (paragon == null)
                {
                    MelonLogger.Warning("Paragon degree: this tower is not a paragon.");
                    return;
                }

                var info = paragon.investmentInfo;
                var investment = 1_000_000f * Math.Max(1, degree);
                info.totalInvestment = investment;
                info.powerFromPops = investment;
                info.powerFromMoneySpent = investment;
                info.powerFromNonTier5Tiers = investment;
                info.powerFromTier5Count = investment;
                info.powerFromBonus = investment;
                paragon.investmentInfo = info;
                paragon.UpdateDegree();

                MelonLogger.Msg($"Set paragon degree {degree} on {tower.towerModel?.baseId} " +
                                $"(now {paragon.GetCurrentDegree()}).");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Set paragon degree failed: " + e.Message);
            }
        }

        public static string CurrentHero
        {
            get
            {
                try
                {
                    return InGame.Bridge.players[InGame.Bridge.GetInputId()].hero;
                }
                catch
                {
                    return "";
                }
            }
            set
            {
                InGame.Bridge.players[InGame.Bridge.GetInputId()].hero = value;
            }
        }

        public static void SetHero(string heroId)
        {
            if (!IsInMatch || string.IsNullOrEmpty(heroId)) return;

            try
            {
                var profile = Game.instance.GetPlayerProfile();
                profile.unlockedHeroes.Add(heroId);

                var towerInventory = InGame.instance.GetTowerInventory();
                if (towerInventory.towerMaxes != null)
                {
                    foreach (var unlockedHero in profile.unlockedHeroes)
                    {
                        towerInventory.towerMaxes[unlockedHero] = 0;
                    }
                    towerInventory.towerMaxes[heroId] = 1;
                }

                RefreshShop();
                CurrentHero = heroId;

                try { InGame.instance.InputManager.SetSelected(null); } catch { }

                MelonLogger.Msg($"Hero set to {heroId} (current={CurrentHero}).");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Set hero failed: " + e.Message);
            }
        }

        private static void RefreshShop()
        {
            try
            {
                if (ShopMenu.instance == null) return;

                var disallowSelectingDifferentTowers = ShopMenu.instance.disallowSelectingDifferentTowers;
                ShopMenu.instance.disallowSelectingDifferentTowers = true;
                ShopMenu.instance.RebuildTowerSet();
                ShopMenu.instance.disallowSelectingDifferentTowers = disallowSelectingDifferentTowers;

                foreach (var button in ShopMenu.instance.ActiveTowerButtons)
                {
                    button.Cast<TowerPurchaseButton>().Update();
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Refresh shop failed: " + e.Message);
            }
        }

    }

    public class TowerBuffState
    {
        public float Range = 1f;
        public float Damage = 1f;
        public float Speed = 1f;
        public float Pierce = 1f;
        public string LastModel = "";
    }

    public class TowerInfo
    {
        public int Index;
        public string Name;
        public string Tiers;
        public long Damage;
        public Tower Tower;
    }
}
