using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BTD_Mod_Helper;
using BTD_Mod_Helper.Api.ModOptions;
using BTD_Mod_Helper.Extensions;
using Il2CppAssets.Scripts.Data;
using Il2CppAssets.Scripts.Data.TrophyStore;
using Il2CppAssets.Scripts.Models.Knowledge;
using Il2CppAssets.Scripts.Models.Powers;
using Il2CppAssets.Scripts.Models.TowerSets;
using Il2CppAssets.Scripts.Unity;
using Il2CppAssets.Scripts.Unity.Player;
using Il2CppAssets.Scripts.Utils;
using MelonLoader;
using UnityEngine;
using ProfileInstaTowerModel = Il2CppAssets.Scripts.Models.Profile.InstaTowerModel;
using ProfileModel = Il2CppAssets.Scripts.Models.Profile.ProfileModel;
using ProfilePowerData = Il2CppAssets.Scripts.Models.Profile.PowerData;
using PowersProPowerSaveData = Il2CppAssets.Scripts.Models.Profile.PowersProPowerSaveData;
using PowersProSaveData = Il2CppAssets.Scripts.Models.Profile.PowersProSaveData;

[assembly: MelonInfo(typeof(BTD6ModMenu.BTD6ModMenuMain), "BTD6 Mod Menu", "1.0.0", "WhatAVibe")]
[assembly: MelonGame("Ninja Kiwi", "BloonsTD6")]

namespace BTD6ModMenu
{
    /// <summary>
    /// Gives insta monkeys (including newly added towers), monkey knowledge, monkey money,
    /// tower XP, trophies, trophy items, powers and Powers Pro unlocks.
    ///
    /// F1  give all insta monkeys            F5  unlock all trophy items
    /// F2  unlock all monkey knowledge       F6  give all powers
    /// F3  monkey money + tower XP           F7  unlock all Powers Pro tiers
    /// F4  trophies                          Insert  open/close this window
    /// </summary>
    public class BTD6ModMenuMain : BloonsTD6Mod
    {
        // ------------------------------------------------------------------
        //  Settings (these also show up in Mod Helper's normal settings menu)
        // ------------------------------------------------------------------

        public static ModSettingInt InstaQuantity = new(50)
        {
            displayName = "Insta Monkey Quantity",
            description = "How many of EACH insta-monkey variant to add. There are 64 variants per tower " +
                          "(0-0-0 plus every 5-x-y / x-5-y / x-y-5 crosspath), so the in-game group total is " +
                          "about 64x this number.",
            min = 0,
            max = 10000000
        };

        public static ModSettingInt PowerQuantity = new(50)
        {
            displayName = "Power Quantity",
            description = "How many of each power to add.",
            min = 0,
            max = 2000000000
        };

        public static ModSettingInt MoneyAmount = new(1000000)
        {
            displayName = "Monkey Money Amount",
            description = "Amount used by the 'add monkey money' action / F3.",
            min = 0,
            max = 2000000000
        };

        public static ModSettingInt XpAmount = new(1000000)
        {
            displayName = "Tower XP Amount",
            description = "XP added to every base tower by the money + XP action / F3.",
            min = 0,
            max = 2000000000
        };

        public static ModSettingInt TrophyAmount = new(10000)
        {
            displayName = "Trophy Amount",
            description = "Trophies added by the trophy action / F4.",
            min = 0,
            max = 2000000000
        };

        public static ModSettingBool SaveAfterGiving = new(true)
        {
            displayName = "Save after giving",
            description = "Ask the game to save the profile after a bulk action. Recommended."
        };

        public static ModSettingBool LogDiagnostics = new(false)
        {
            displayName = "Log tower/power ids",
            description = "Write every base tower id and power id MelonLoader's console. Useful after a game update."
        };

        public static ModSettingHotkey GiveInstaHotkey = new(KeyCode.F1);
        public static ModSettingHotkey KnowledgeHotkey = new(KeyCode.F2);
        public static ModSettingHotkey MoneyXpHotkey = new(KeyCode.F3);
        public static ModSettingHotkey TrophyHotkey = new(KeyCode.F4);
        public static ModSettingHotkey TrophyItemsHotkey = new(KeyCode.F5);
        public static ModSettingHotkey PowersHotkey = new(KeyCode.F6);
        public static ModSettingHotkey PowersProHotkey = new(KeyCode.F7);
        public static ModSettingHotkey GuiHotkey = new(KeyCode.Insert);

        // ------------------------------------------------------------------
        //  State
        // ------------------------------------------------------------------

        private static MelonPreferences_Category _prefs;
        private static MelonPreferences_Entry<string> _towerOverridesPref;

        /// <summary>Per-tower quantity overrides, persisted as "id=qty;id=qty".</summary>
        private static readonly Dictionary<string, int> TowerQtyOverrides = new(StringComparer.OrdinalIgnoreCase);

        private static readonly List<int[]> TierCombos = BuildTierCombos();

        private static bool _loggedIds;

        // GUI state
        private bool _showGui;
        private Rect _windowRect = new(80, 80, 840, 700);
        private int _tab;
        private string _search = "";
        private Vector2 _scroll;


        private readonly Dictionary<string, string> _towerQtyText = new(StringComparer.OrdinalIgnoreCase);

        private List<string> _towerIds = new();
        private List<string> _powerIds = new();
        private List<TowerInfo> _activeTowers = new();
        private TowerInfo _editingTower;
        private float _lastTowerRefresh = -999f;
        private List<string> _heroIds = new();
        private readonly Dictionary<string, int> _towerCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _towerVariants = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _powerCounts = new(StringComparer.OrdinalIgnoreCase);

        private int _totalInsta;
        private double _monkeyMoney;
        private int _trophyBalance;
        private bool _powersProEnabled;
        private float _lastRefresh = -999f;

        // ------------------------------------------------------------------
        //  Lifecycle
        // ------------------------------------------------------------------

        public override void OnApplicationStart()
        {
            _prefs = MelonPreferences.CreateCategory("BTD6ModMenu", "BTD6 Mod Menu");
            _towerOverridesPref = _prefs.CreateEntry("TowerQuantities", "",
                "Per-tower insta quantities", "Internal: per-tower quantity overrides set from the in-game window.");
            LoadTowerOverrides();

            MelonLogger.Msg("BTD6 Mod Menu 1.0.0 loaded. Insert opens the window.");
            MelonLogger.Msg($"Insta variants per tower: {TierCombos.Count} (including 0-0-0)");
        }

        public override void OnUpdate()
        {
            try
            {
                // One-time id dump once the game model is available.
                if (!_loggedIds && Game.instance != null && Game.instance.model != null)
                {
                    _loggedIds = true;
                    var ids = GetBaseTowerIds();
                    MelonLogger.Msg($"Base towers found: {ids.Count} | " +
                                    $"Skywarden={(ids.Contains("Skywarden") ? "yes" : "no")} " +
                                    $"Mermonkey={(ids.Contains("Mermonkey") ? "yes" : "no")} " +
                                    $"Desperado={(ids.Contains("Desperado") ? "yes" : "no")}");
                    if (LogDiagnostics) LogGameIds();
                }

                Sandbox.Tick();

                if (GiveInstaHotkey.JustPressed()) GiveAllInsta();
                if (KnowledgeHotkey.JustPressed()) UnlockAllKnowledge();
                if (MoneyXpHotkey.JustPressed()) AddMoneyAndXp();
                if (TrophyHotkey.JustPressed()) AddTrophies();
                if (TrophyItemsHotkey.JustPressed()) UnlockTrophyItems();
                if (PowersHotkey.JustPressed()) GiveAllPowers();
                if (PowersProHotkey.JustPressed()) UnlockAllPowersPro();
                if (GuiHotkey.JustPressed()) SetGuiVisible(!_showGui);
            }
            catch (Exception e)
            {
                MelonLogger.Error("BTD6ModMenu OnUpdate: " + e);
            }
        }

        // ------------------------------------------------------------------
        //  Actions
        // ------------------------------------------------------------------

        public static void GiveAllInsta()
        {
            var player = GetPlayer();
            if (player == null)
            {
                MelonLogger.Warning("Player is not ready yet - enter the main menu first.");
                return;
            }

            var ids = GetBaseTowerIds();
            if (ids.Count == 0)
            {
                MelonLogger.Warning("No base towers found - is the game still loading?");
                return;
            }

            MelonLogger.Msg($"Giving insta monkeys for {ids.Count} towers...");
            int ok = 0, failed = 0;

            foreach (var id in ids)
            {
                try
                {
                    int qty = GetQuantityFor(id);
                    if (qty <= 0) continue;
                    GiveInstaForTower(player, id, qty, add: true);
                    ok++;
                }
                catch (Exception e)
                {
                    failed++;
                    MelonLogger.Warning($"Insta failed for '{id}': {e.Message}");
                }
            }

            player.Data.knowledgeDisabled = false;
            SaveProfile(player);
            MelonLogger.Msg($"Insta monkeys done: {ok} towers gave, {failed} failed. " +
                            $"Total insta count is now {SafeInt(() => player.GetNumberOfInstaTowers()):N0}.");
        }

        private static void GiveInstaForTower(Btd6Player player, string id, int perVariant, bool add)
        {
            if (perVariant < 0) perVariant = 0;
            if (perVariant > 10000000) perVariant = 10000000;

            foreach (var tiers in TierCombos)
            {
                if (add)
                {
                    if (perVariant > 0) player.AddInstaTower(id, tiers, perVariant);
                }
                else
                {
                    var model = player.GetInstaTower(id, tiers);
                    if (model != null) model.Quantity = perVariant;
                    else if (perVariant > 0) player.AddInstaTower(id, tiers, perVariant);
                }
            }
        }

        /// <summary>Replaces all insta-monkey variants of a tower with a single exact total.</summary>
        private static void SetExactInstaTotal(Btd6Player player, string id, int total)
        {
            if (total < 0) total = 0;

            try
            {
                var dict = player.Data.instaTowers;
                if (dict != null && dict.ContainsKey(id))
                {
                    dict[id].Clear();
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"Could not clear insta list for '{id}': {e.Message}");
            }

            if (total > 0) player.AddInstaTower(id, new int[3], total);
        }

        public static void UnlockAllKnowledge()
        {
            var player = GetPlayer();
            if (player == null) return;

            var model = Game.instance.model;
            var knowledge = model?.allKnowledge;
            if (knowledge == null) return;

            player.Data.knowledgeDisabled = false;

            int unlocked = 0;
            for (int i = 0; i < knowledge.Length; i++)
            {
                var k = knowledge[i];
                if (k == null) continue;

                var id = ResolveKnowledgeId(player, k.name);
                try
                {
                    if (player.HasKnowledge(id)) continue;
                    player.AcquireKnowledge(id);
                    unlocked++;
                }
                catch (Exception e)
                {
                    MelonLogger.Warning($"Knowledge '{id}' failed: {e.Message}");
                }
            }

            SaveProfile(player);
            MelonLogger.Msg($"Unlocked {unlocked} monkey knowledge entries (already had the rest).");
        }

        private static string ResolveKnowledgeId(Btd6Player player, string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            // Old mods stripped 15 chars. Current data may already be just the id.
            var candidates = new List<string> { name };
            if (name.Length > 15) candidates.Add(name.Substring(15));
            var kIdx = name.IndexOf("Knowledge", StringComparison.OrdinalIgnoreCase);
            if (kIdx >= 0) candidates.Add(name.Substring(kIdx + "Knowledge".Length).TrimStart('_', '.', '-'));

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                try
                {
                    if (player.HasKnowledge(candidate)) return candidate;
                }
                catch
                {
                    // ignored
                }
            }

            return candidates.Count > 1 ? candidates[1] : name;
        }

        public static void AddMoneyAndXp()
        {
            var player = GetPlayer();
            if (player == null) return;

            int money = MoneyAmount;
            int xp = XpAmount;

            try
            {
                if (money > 0) Game.instance.AddMonkeyMoney(money);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Monkey money failed: " + e.Message);
            }

            int ok = 0;
            foreach (var id in GetBaseTowerIds())
            {
                try
                {
                    if (xp > 0) player.AddTowerXP(id, xp);
                    ok++;
                }
                catch (Exception e)
                {
                    MelonLogger.Warning($"XP failed for '{id}': {e.Message}");
                }
            }

            SaveProfile(player);
            MelonLogger.Msg($"Added {money:N0} monkey money and {xp:N0} XP to {ok} towers.");
        }

        public static void AddTrophies()
        {
            var player = GetPlayer();
            if (player == null) return;

            int amount = TrophyAmount;
            if (amount <= 0) return;

            try
            {
                player.GainTrophies(amount, "event", null);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("GainTrophies failed, writing balance directly: " + e.Message);
                try
                {
                    if (player.Data.trophies != null) player.Data.trophies.Value += amount;
                }
                catch (Exception e2)
                {
                    MelonLogger.Error("Direct trophy write failed: " + e2.Message);
                }
            }

            SaveProfile(player);
            MelonLogger.Msg($"Added {amount:N0} trophies.");
        }

        public static void UnlockTrophyItems()
        {
            var player = GetPlayer();
            if (player == null) return;

            int count = 0;
            try
            {
                var items = GameData.Instance?.trophyStoreItems?.storeItems;
                if (items == null) return;

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item == null || string.IsNullOrEmpty(item.id)) continue;
                    try
                    {
                        player.AddTrophyStoreItem(item.id);
                        count++;
                    }
                    catch
                    {
                        // ignore individual bad item
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error("Trophy item unlock failed: " + e);
            }

            SaveProfile(player);
            MelonLogger.Msg($"Unlocked {count} trophy store items.");
        }

        public static void GiveAllPowers()
        {
            var player = GetPlayer();
            if (player == null) return;

            int qty = PowerQuantity;
            if (qty <= 0) return;

            int ok = 0, failed = 0;
            foreach (var id in GetPowerIds())
            {
                try
                {
                    player.AddPower(id, qty);
                    ok++;
                }
                catch (Exception e)
                {
                    failed++;
                    MelonLogger.Warning($"Power '{id}' failed: {e.Message}");
                }
            }

            SaveProfile(player);
            MelonLogger.Msg($"Powers: added {qty:N0} to {ok} powers ({failed} failed).");
        }

        public static void UnlockAllPowersPro()
        {
            var player = GetPlayer();
            if (player == null) return;

            try
            {
                // Debug flag used by the game's profile handling.
                player.debugUnlockAllPowersPro = true;

                var save = player.Data.powersProSaveData;
                if (save == null)
                {
                    save = new PowersProSaveData();
                    player.Data.powersProSaveData = save;
                }

                save.isEnabled = true;

                if (save.dataByPower == null)
                {
                    save.dataByPower =
                        new Il2CppSystem.Collections.Generic.Dictionary<string, PowersProPowerSaveData>();
                }

                if (save.seenInfoPopup == null)
                {
                    save.seenInfoPopup = new KonFuze_NoShuffle(0);
                }
                save.seenInfoPopup.Value = 1;

                var ids = GetPowerProIds();
                foreach (var id in ids)
                {
                    if (string.IsNullOrEmpty(id)) continue;

                    PowersProPowerSaveData data;
                    if (!save.dataByPower.ContainsKey(id))
                    {
                        data = new PowersProPowerSaveData();
                        save.dataByPower[id] = data;
                    }
                    else
                    {
                        data = save.dataByPower[id];
                    }

                    if (data.xp == null) data.xp = new KonFuze_NoShuffle(0);
                    if (data.unlockedTier == null) data.unlockedTier = new KonFuze_NoShuffle(0);
                    if (data.isSeen == null) data.isSeen = new KonFuze_NoShuffle(0);
                    if (data.seenUnlockPro == null) data.seenUnlockPro = new KonFuze_NoShuffle(0);
                    if (data.seenUnlockTier == null) data.seenUnlockTier = new KonFuze_NoShuffle(0);

                    // 1e9 XP is far beyond the last tier; 999 marks every tier as unlocked.
                    data.xp.Value = 1_000_000_000;
                    data.unlockedTier.Value = 999;
                    data.isSeen.Value = 1;
                    data.seenUnlockPro.Value = 1;
                    data.seenUnlockTier.Value = 1;
                }

                SaveProfile(player);
                MelonLogger.Msg($"Powers Pro: enabled and unlocked {ids.Count} power entries.");
            }
            catch (Exception e)
            {
                MelonLogger.Error("Powers Pro unlock failed: " + e);
            }
        }

        // ------------------------------------------------------------------
        //  Data helpers
        // ------------------------------------------------------------------

        private static Btd6Player GetPlayer()
        {
            try
            {
                return Game.instance?.GetBtd6Player();
            }
            catch
            {
                return null;
            }
        }

        private static void SaveProfile(Btd6Player player)
        {
            if (!SaveAfterGiving || player == null) return;
            try
            {
                player.Save();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Profile save failed: " + e.Message);
            }
        }

        public static List<string> GetBaseTowerIds()
        {
            var ids = new List<string>();

            try
            {
                var details = Game.instance?.GetTowerDetailModels();
                if (details != null)
                {
                    for (int i = 0; i < details.Length; i++)
                    {
                        var d = details[i];
                        if (d == null || string.IsNullOrEmpty(d.towerId)) continue;
                        if (!ids.Contains(d.towerId)) ids.Add(d.towerId);
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("GetTowerDetailModels failed: " + e.Message);
            }

            if (ids.Count == 0)
            {
                // Fallback: every base tower model that is not a hero/sub tower.
                try
                {
                    var towers = Game.instance?.model?.towers;
                    if (towers != null)
                    {
                        for (int i = 0; i < towers.Length; i++)
                        {
                            var t = towers[i];
                            if (t == null || t.isSubTower) continue;
                            if (t.name != t.BaseId) continue;
                            if (t.IsHero()) continue;
                            if (!ids.Contains(t.BaseId)) ids.Add(t.BaseId);
                        }
                    }
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("Fallback tower scan failed: " + e.Message);
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        public static List<string> GetPowerIds()
        {
            var ids = new List<string>();

            try
            {
                var details = Game.instance?.GetPowerDetailModels();
                if (details != null)
                {
                    for (int i = 0; i < details.Length; i++)
                    {
                        var d = details[i];
                        if (d == null || string.IsNullOrEmpty(d.powerId)) continue;
                        if (!ids.Contains(d.powerId)) ids.Add(d.powerId);
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("GetPowerDetailModels failed: " + e.Message);
            }

            try
            {
                var powers = Game.instance?.model?.powers;
                if (powers != null)
                {
                    for (int i = 0; i < powers.Length; i++)
                    {
                        var p = powers[i];
                        if (p == null || p.isHidden || string.IsNullOrEmpty(p.name)) continue;
                        if (!ids.Contains(p.name)) ids.Add(p.name);
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Power model scan failed: " + e.Message);
            }

            // Drop duplicate "<name> Power" entries when the real power already exists.
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                var id = ids[i];
                if (!id.EndsWith(" Power", StringComparison.OrdinalIgnoreCase)) continue;
                var baseId = id.Substring(0, id.Length - " Power".Length);
                if (ids.Any(x => string.Equals(x, baseId, StringComparison.OrdinalIgnoreCase)))
                {
                    ids.RemoveAt(i);
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        public static List<string> GetPowerProIds()
        {
            var ids = new List<string>();

            try
            {
                var pro = Game.instance?.model?.powersPro;
                if (pro != null)
                {
                    for (int i = 0; i < pro.Length; i++)
                    {
                        var p = pro[i];
                        if (p == null || string.IsNullOrEmpty(p.powerId)) continue;
                        if (!ids.Contains(p.powerId)) ids.Add(p.powerId);
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Powers Pro model scan failed: " + e.Message);
            }

            // Also cover the base ids, because some save files key Powers Pro data by base id.
            foreach (var id in GetPowerIds())
            {
                if (!ids.Contains(id)) ids.Add(id);
            }

            return ids;
        }

        private static int GetQuantityFor(string towerId)
        {
            if (TowerQtyOverrides.TryGetValue(towerId, out var q)) return Clamp(q, 0, 10000000);
            return Clamp(InstaQuantity, 0, 10000000);
        }

        private static int SafeInt(Func<int> func)
        {
            try
            {
                return func();
            }
            catch
            {
                return 0;
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static List<int[]> BuildTierCombos()
        {
            var list = new List<int[]>();
            var seen = new HashSet<string>();

            void Add(int a, int b, int c)
            {
                var key = a + "-" + b + "-" + c;
                if (!seen.Add(key)) return;
                list.Add(new[] { a, b, c });
            }

            Add(0, 0, 0);

            // The game only supports 64 insta variants per tower: 0-0-0 plus every
            // combination where one path is 1-5 and at most one other path is 1-2
            // (there is no 5-2-2 insta monkey).
            for (int top = 1; top <= 5; top++)
            {
                for (int second = 0; second <= 2; second++)
                {
                    Add(top, second, 0);
                    Add(top, 0, second);
                    Add(second, top, 0);
                    Add(0, top, second);
                    Add(second, 0, top);
                    Add(0, second, top);
                }
            }

            return list;
        }

        private static void LogGameIds()
        {
            var towers = GetBaseTowerIds();
            var powers = GetPowerIds();
            var pro = GetPowerProIds();

            MelonLogger.Msg($"BASE TOWERS ({towers.Count}): {string.Join(", ", towers)}");
            MelonLogger.Msg($"POWERS ({powers.Count}): {string.Join(", ", powers)}");
            MelonLogger.Msg($"POWERS PRO IDS ({pro.Count}): {string.Join(", ", pro)}");

            foreach (var name in new[] { "Skywarden", "Mermonkey", "Desperado" })
            {
                MelonLogger.Msg($"New tower check: {name} = {(towers.Contains(name) ? "FOUND" : "MISSING")}");
            }

            try
            {
                var knowledge = Game.instance?.model?.allKnowledge;
                if (knowledge != null && knowledge.Length > 0)
                {
                    var sample = new List<string>();
                    for (int i = 0; i < Math.Min(3, knowledge.Length); i++)
                    {
                        sample.Add(knowledge[i]?.name ?? "<null>");
                    }
                    MelonLogger.Msg("Knowledge name samples: " + string.Join(" | ", sample));
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Knowledge sample failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------------
        //  Overrides persistence
        // ------------------------------------------------------------------

        private static void LoadTowerOverrides()
        {
            TowerQtyOverrides.Clear();
            var raw = _towerOverridesPref?.Value;
            if (string.IsNullOrWhiteSpace(raw)) return;

            foreach (var part in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = part.Split('=');
                if (pieces.Length != 2) continue;
                if (int.TryParse(pieces[1], out var qty)) TowerQtyOverrides[pieces[0]] = qty;
            }
        }

        private static void SaveTowerOverrides()
        {
            var sb = new StringBuilder();
            foreach (var pair in TowerQtyOverrides)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(pair.Key).Append('=').Append(pair.Value);
            }

            if (_towerOverridesPref != null) _towerOverridesPref.Value = sb.ToString();
            try
            {
                MelonPreferences.Save();
            }
            catch
            {
                // ignored
            }
        }

        // ------------------------------------------------------------------
        //  GUI
        // ------------------------------------------------------------------

        private void SetGuiVisible(bool visible)
        {
            _showGui = visible;
        }

        public override void OnGUI()
        {
            if (!_showGui) return;

            try
            {
                var skin = GUI.skin;
                if (skin != null)
                {
                    skin.label.fontSize = 14;
                    skin.button.fontSize = 12;
                    skin.window.fontSize = 15;
                }

                Action<int> drawWindow = DrawWindow;
                _windowRect = GUI.Window(0x8F3A1, _windowRect, drawWindow, "BTD6 Mod Menu 1.0");
            }
            catch (Exception e)
            {
                MelonLogger.Error("GUI: " + e);
            }
        }

        private void DrawWindow(int id)
        {
            const float pad = 10f;
            var w = _windowRect.width;
            var y = 28f;

            if (GUI.Button(new Rect(pad, y, 88f, 26f), "Insta Monkeys")) _tab = 0;
            if (GUI.Button(new Rect(pad + 92f, y, 80f, 26f), "Powers")) _tab = 1;
            if (GUI.Button(new Rect(pad + 176f, y, 90f, 26f), "Sandbox")) _tab = 2;
            if (GUI.Button(new Rect(pad + 270f, y, 80f, 26f), "Towers")) _tab = 3;
            if (GUI.Button(new Rect(pad + 354f, y, 90f, 26f), "Unlocks")) _tab = 4;
            if (GUI.Button(new Rect(pad + 448f, y, 100f, 26f), "Resources")) _tab = 5;
            if (GUI.Button(new Rect(pad + 552f, y, 60f, 26f), "Help")) _tab = 6;
            if (GUI.Button(new Rect(w - 44f, y, 30f, 26f), "X")) SetGuiVisible(false);

            y += 32f;
            RefreshDataIfNeeded();

            try
            {
                switch (_tab)
                {
                    case 0:
                        DrawInstaTab(pad, y, w);
                        break;
                    case 1:
                        DrawPowersTab(pad, y, w);
                        break;
                    case 2:
                        DrawSandboxTab(pad, y, w);
                        break;
                    case 3:
                        DrawTowersTab(pad, y, w);
                        break;
                    case 4:
                        DrawUnlocksTab(pad, y, w);
                        break;
                    case 5:
                        DrawResourcesTab(pad, y, w);
                        break;
                    default:
                        DrawHelpTab(pad, y, w);
                        break;
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error("GUI tab: " + e);
            }

            GUI.Label(new Rect(pad, _windowRect.height - 25f, w - pad * 2f, 20f),
                "F1 instas | F2 knowledge | F3 money+XP | F4 trophies | F5 trophy items | F6 powers | F7 Powers Pro | Insert window");

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void DrawInstaTab(float x, float y, float w)
        {
            GUI.Label(new Rect(x, y, 250f, 22f), "Insta quantity per variant (default):");
            GUI.Label(new Rect(x + 255f, y, 120f, 22f), ((int)InstaQuantity).ToString("N0"));
            y += 26f;

            DrawStepper(x, y, (int)InstaQuantity, v => InstaQuantity.SetValue((long)Clamp(v, 0, 10000000)));
            y += 28f;

            DrawPresets(x, y, new[] { 0, 50, 100, 1000, 10000, 100000, 1000000, 10000000 },
                v => InstaQuantity.SetValue((long)v));
            y += 32f;

            if (GUI.Button(new Rect(x, y, 200f, 28f), "Give All Insta Monkeys"))
            {
                GiveAllInsta();
                RefreshDataIfNeeded(true);
            }

            if (GUI.Button(new Rect(x + 208f, y, 210f, 28f), "Set Every Variant To Default"))
            {
                SetAllVariantsToDefault();
            }

            if (GUI.Button(new Rect(x + 426f, y, 190f, 28f), "Reset Overrides"))
            {
                TowerQtyOverrides.Clear();
                _towerQtyText.Clear();
                SaveTowerOverrides();
            }

            y += 34f;

            GUI.Label(new Rect(x, y, w - 20f, 22f),
                $"Towers: {_towerIds.Count}    Variants/tower: {TierCombos.Count}    " +
                $"Total insta monkeys in profile: {_totalInsta:N0}");

            y += 24f;


            var filtered = FilterTowers().ToList();
            var listHeight = Math.Max(120f, _windowRect.height - y - 40f);
            var viewport = new Rect(x, y, w - x - 14f, listHeight);
            var content = new Rect(0f, 0f, Math.Max(1f, viewport.width - 20f), Math.Max(1f, filtered.Count * 30f));

            _scroll = GUI.BeginScrollView(viewport, _scroll, content);
            var rowY = 0f;
            foreach (var towerId in filtered)
            {
                _towerCounts.TryGetValue(towerId, out var have);
                _towerVariants.TryGetValue(towerId, out var variants);

                bool hasOverride = TowerQtyOverrides.TryGetValue(towerId, out var overrideQty);
                int qty = hasOverride ? overrideQty : (int)InstaQuantity;

                GUI.Label(new Rect(0f, rowY, 240f, 24f), Pretty(towerId));
                GUI.Label(new Rect(245f, rowY, 115f, 24f), $"{have:N0} ({variants} var)");
                GUI.Label(new Rect(365f, rowY, 70f, 24f), qty.ToString("N0") + (hasOverride ? "" : " def"));

                var player = GetPlayer();

                if (GUI.Button(new Rect(438f, rowY, 24f, 24f), "-")) SetTowerOverride(towerId, qty - 1);
                if (GUI.Button(new Rect(464f, rowY, 24f, 24f), "+")) SetTowerOverride(towerId, qty + 1);
                if (GUI.Button(new Rect(490f, rowY, 38f, 24f), "x10")) SetTowerOverride(towerId, qty * 10);
                if (GUI.Button(new Rect(530f, rowY, 38f, 24f), "/10")) SetTowerOverride(towerId, qty / 10);

                if (GUI.Button(new Rect(572f, rowY, 50f, 24f), "Give") && player != null)
                {
                    GiveInstaForTower(player, towerId, qty, true);
                    SaveProfile(player);
                    RefreshDataIfNeeded(true);
                }

                if (GUI.Button(new Rect(624f, rowY, 52f, 24f), "Set") && player != null)
                {
                    GiveInstaForTower(player, towerId, qty, false);
                    SaveProfile(player);
                    RefreshDataIfNeeded(true);
                }

                if (GUI.Button(new Rect(678f, rowY, 50f, 24f), "Exact") && player != null)
                {
                    SetExactInstaTotal(player, towerId, qty);
                    SaveProfile(player);
                    RefreshDataIfNeeded(true);
                }

                if (GUI.Button(new Rect(730f, rowY, 42f, 24f), "Rst"))
                {
                    TowerQtyOverrides.Remove(towerId);
                    SaveTowerOverrides();
                }

                rowY += 30f;
            }
            GUI.EndScrollView();
        }

        private void DrawPowersTab(float x, float y, float w)
        {
            GUI.Label(new Rect(x, y, 200f, 22f), "Quantity per power:");
            GUI.Label(new Rect(x + 205f, y, 120f, 22f), ((int)PowerQuantity).ToString("N0"));
            y += 26f;

            DrawStepper(x, y, (int)PowerQuantity, v => PowerQuantity.SetValue((long)Clamp(v, 0, 2000000000)));
            y += 28f;

            DrawPresets(x, y, new[] { 0, 50, 100, 1000, 10000, 100000, 1000000 },
                v => PowerQuantity.SetValue((long)v));
            y += 32f;

            if (GUI.Button(new Rect(x, y, 170f, 28f), "Give All Powers"))
            {
                GiveAllPowers();
                RefreshDataIfNeeded(true);
            }

            if (GUI.Button(new Rect(x + 178f, y, 280f, 28f), "Unlock All Powers Pro (max tier)"))
            {
                UnlockAllPowersPro();
                RefreshDataIfNeeded(true);
            }

            y += 34f;

            GUI.Label(new Rect(x, y, w - 20f, 22f),
                $"Powers: {_powerIds.Count}    Powers Pro enabled: {(_powersProEnabled ? "yes" : "no")}");
            y += 24f;
            GUI.Label(new Rect(x, y, w - 20f, 22f),
                "Powers are consumables. Powers Pro sets max tier + XP for every power id.");
            y += 30f;

            var viewport = new Rect(x, y, w - x - 14f, Math.Max(120f, _windowRect.height - y - 40f));
            var content = new Rect(0f, 0f, Math.Max(1f, viewport.width - 20f), Math.Max(1f, _powerIds.Count * 30f));

            _scroll = GUI.BeginScrollView(viewport, _scroll, content);
            var rowY = 0f;
            foreach (var powerId in _powerIds)
            {
                _powerCounts.TryGetValue(powerId, out var have);
                GUI.Label(new Rect(0f, rowY, 360f, 24f), Pretty(powerId));
                GUI.Label(new Rect(365f, rowY, 140f, 24f), $"have {have:N0}");

                var player = GetPlayer();
                if (GUI.Button(new Rect(510f, rowY, 60f, 24f), "Give") && player != null)
                {
                    int qty = PowerQuantity;
                    if (qty > 0) player.AddPower(powerId, qty);
                    SaveProfile(player);
                    RefreshDataIfNeeded(true);
                }

                if (GUI.Button(new Rect(575f, rowY, 60f, 24f), "Set") && player != null)
                {
                    try
                    {
                        var data = player.GetPowerData(powerId);
                        if (data != null)
                        {
                            data.Quantity = PowerQuantity;
                            SaveProfile(player);
                            RefreshDataIfNeeded(true);
                        }
                    }
                    catch (Exception e)
                    {
                        MelonLogger.Warning($"Set power '{powerId}' failed: {e.Message}");
                    }
                }

                rowY += 30f;
            }
            GUI.EndScrollView();
        }

        private static bool ToggleButton(float x, float y, float width, string label, bool value)
        {
            return GUI.Button(new Rect(x, y, width, 26f), (value ? "[x] " : "[ ] ") + label) ? !value : value;
        }

        private void DrawSandboxTab(float x, float y, float w)
        {
            var viewport = new Rect(x, y, w - x - 14f, Math.Max(160f, _windowRect.height - y - 40f));
            var contentW = Math.Max(1f, viewport.width - 20f);
            var content = new Rect(0f, 0f, contentW, 1080f);

            _scroll = GUI.BeginScrollView(viewport, _scroll, content);
            var cy = 0f;

            GUI.Label(new Rect(0f, cy, contentW, 22f), "Match cheats - enter a match first (these do nothing in the menu)");
            cy += 26f;

            Sandbox.InfiniteCash = ToggleButton(0f, cy, 500f, "Infinite cash (refills every frame)", Sandbox.InfiniteCash);
            cy += 30f;
            Sandbox.InfiniteLives = ToggleButton(0f, cy, 500f, "Infinite lives / god mode", Sandbox.InfiniteLives);
            cy += 30f;
            Sandbox.NoCooldowns = ToggleButton(0f, cy, 500f, "No ability cooldowns", Sandbox.NoCooldowns);
            cy += 30f;
            Sandbox.FreezeTowers = ToggleButton(0f, cy, 500f, "Freeze towers", Sandbox.FreezeTowers);
            cy += 30f;
            Sandbox.PlaceAnywhere = ToggleButton(0f, cy, 500f, "Place anywhere (land / water / track)", Sandbox.PlaceAnywhere);
            cy += 30f;
            Sandbox.IgnoreTowerLimits = ToggleButton(0f, cy, 500f, "Ignore tower count limit", Sandbox.IgnoreTowerLimits);
            cy += 30f;
            Sandbox.UnlimitedT5 = ToggleButton(0f, cy, 500f, "Unlimited 5th tiers", Sandbox.UnlimitedT5);
            cy += 30f;
            Sandbox.SandboxRules = ToggleButton(0f, cy, 500f, "Sandbox rules (experimental)", Sandbox.SandboxRules);
            cy += 34f;

            if (GUI.Button(new Rect(0f, cy, 360f, 28f), "Make Everything Free This Match"))
            {
                Sandbox.MakeEverythingFree();
            }
            GUI.Label(new Rect(370f, cy + 4f, 420f, 22f),
                "Adds free tower/power charges and 0% discounts. Cash toggle covers upgrades.");
            cy += 38f;

            GUI.Label(new Rect(0f, cy, contentW, 22f), "Damage multiplier (one-shot bloons)");
            cy += 24f;
            DrawChoiceRow(cy, new[] { "1x", "2x", "5x", "10x", "100x", "9999x" }, new[] { 1f, 2f, 5f, 10f, 100f, 9999f },
                Sandbox.DamageMultiplier, v => Sandbox.SetDamageMultiplier(v));
            cy += 34f;

            GUI.Label(new Rect(0f, cy, contentW, 22f), "Game speed (requires in-game fast-forward ON)");
            cy += 24f;
            DrawChoiceRow(cy, new[] { "1x", "2x", "3x", "5x", "10x", "20x" }, new[] { 1f, 2f, 3f, 5f, 10f, 20f },
                Sandbox.GameSpeed, v => Sandbox.SetGameSpeed(v));
            cy += 40f;

            GUI.Label(new Rect(0f, cy, contentW, 22f), $"Round controls - current round: {Sandbox.GetRound()}");
            cy += 24f;
            if (GUI.Button(new Rect(0f, cy, 60f, 26f), "-10")) Sandbox.SetRound(Sandbox.GetRound() - 10);
            if (GUI.Button(new Rect(64f, cy, 60f, 26f), "-1")) Sandbox.SetRound(Sandbox.GetRound() - 1);
            if (GUI.Button(new Rect(128f, cy, 60f, 26f), "+1")) Sandbox.SetRound(Sandbox.GetRound() + 1);
            if (GUI.Button(new Rect(192f, cy, 60f, 26f), "+10")) Sandbox.SetRound(Sandbox.GetRound() + 10);
            if (GUI.Button(new Rect(264f, cy, 80f, 26f), "Set 50")) Sandbox.SetRound(50);
            if (GUI.Button(new Rect(348f, cy, 80f, 26f), "Set 100")) Sandbox.SetRound(100);
            if (GUI.Button(new Rect(432f, cy, 80f, 26f), "Set 200")) Sandbox.SetRound(200);
            cy += 34f;

            if (GUI.Button(new Rect(0f, cy, 150f, 28f), "Clear All Bloons")) Sandbox.ClearBloons();
            if (GUI.Button(new Rect(158f, cy, 120f, 28f), "Win Game")) Sandbox.WinGame();
            if (GUI.Button(new Rect(286f, cy, 130f, 28f), "Test Defeat")) Sandbox.LoseGame();
            if (GUI.Button(new Rect(424f, cy, 150f, 28f), "Sell All Towers")) Sandbox.SellAllTowers();
            cy += 38f;

            GUI.Label(new Rect(0f, cy, contentW, 22f), "Spawn bloons");
            cy += 24f;
            var bloons = new[] { "Red", "Blue", "Green", "Ceramic", "Moab", "Bfb", "Zomg", "Ddt", "Bad" };
            for (int i = 0; i < bloons.Length; i++)
            {
                var bx = (i % 5) * 100f;
                var by = cy + (i / 5) * 30f;
                if (GUI.Button(new Rect(bx, by, 92f, 26f), bloons[i])) Sandbox.SpawnBloon(bloons[i], 1);
            }
            cy += 66f;

            GUI.Label(new Rect(0f, cy, contentW, 22f), "Hero switch (current match)");
            cy += 24f;
            if (_heroIds.Count == 0) _heroIds = GetHeroIds();
            var currentHero = Sandbox.CurrentHero;
            for (int i = 0; i < _heroIds.Count; i++)
            {
                var hx = (i % 5) * 130f;
                var hy = cy + (i / 5) * 30f;
                var old = GUI.backgroundColor;
                if (string.Equals(_heroIds[i], currentHero, StringComparison.OrdinalIgnoreCase))
                {
                    GUI.backgroundColor = new Color(0.4f, 0.8f, 0.5f);
                }
                if (GUI.Button(new Rect(hx, hy, 122f, 26f), Pretty(_heroIds[i])))
                {
                    Sandbox.SetHero(_heroIds[i]);
                }
                GUI.backgroundColor = old;
            }
            cy += ((_heroIds.Count + 4) / 5) * 30f + 10f;

            GUI.Label(new Rect(0f, cy, contentW, 22f),
                "Tips: 'Place anywhere' modifies all tower models. 'Sandbox rules' calls Simulation.SetSandbox. " +
                "Some modes block changes until the next round.");
            cy += 24f;
            GUI.Label(new Rect(0f, cy, contentW, 22f),
                "Turn the in-game fast-forward button on for the selected speed to take effect.");

            GUI.EndScrollView();
        }

        private static void DrawChoiceRow(float y, string[] labels, float[] values, float current, Action<float> set)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                var old = GUI.backgroundColor;
                if (Math.Abs(current - values[i]) < 0.001f) GUI.backgroundColor = new Color(0.4f, 0.8f, 0.5f);
                if (GUI.Button(new Rect(i * 80f, y, 74f, 26f), labels[i])) set(values[i]);
                GUI.backgroundColor = old;
            }
        }

        private void DrawTowersTab(float x, float y, float w)
        {
            if (!Sandbox.IsInMatch)
            {
                GUI.Label(new Rect(x, y, w - 20f, 24f), "Enter a match to see active towers.");
                return;
            }

            if (Time.realtimeSinceStartup - _lastTowerRefresh > 1f)
            {
                _lastTowerRefresh = Time.realtimeSinceStartup;
                _activeTowers = Sandbox.GetTowers();
            }

            GUI.Label(new Rect(x, y, w - 20f, 22f),
                $"Active towers: {_activeTowers.Count}   Round: {Sandbox.GetRound()}");
            y += 26f;

            if (GUI.Button(new Rect(x, y, 130f, 26f), "Sell All Towers")) Sandbox.SellAllTowers();
            if (GUI.Button(new Rect(x + 138f, y, 100f, 26f), "Refresh"))
            {
                _activeTowers = Sandbox.GetTowers();
                _lastTowerRefresh = Time.realtimeSinceStartup;
            }
            y += 32f;

            if (_editingTower != null && _editingTower.Tower != null)
            {
                GUI.Label(new Rect(x, y, w - 20f, 22f),
                    $"Editing: {Pretty(_editingTower.Name)} {_editingTower.Tiers} " +
                    $"{Sandbox.GetBuffSummary(_editingTower.Tower)} (this tower only)");
                y += 24f;

                var stats = new[]
                {
                    ("Range x2", "range", 2f), ("Range x5", "range", 5f), ("Range x10", "range", 10f),
                    ("Dmg x2", "damage", 2f), ("Dmg x5", "damage", 5f), ("Dmg x10", "damage", 10f)
                };
                for (int i = 0; i < stats.Length; i++)
                {
                    var bx = x + (i % 3) * 110f;
                    var by = y + (i / 3) * 30f;
                    var (label, stat, mult) = stats[i];
                    if (GUI.Button(new Rect(bx, by, 100f, 26f), label))
                    {
                        Sandbox.BuffTower(_editingTower.Tower, stat, mult);
                        _activeTowers = Sandbox.GetTowers();
                    }
                }
                y += 66f;

                var stats2 = new[]
                {
                    ("Speed x2", "speed", 2f), ("Speed x5", "speed", 5f),
                    ("Pierce x2", "pierce", 2f), ("Pierce x5", "pierce", 5f)
                };
                for (int i = 0; i < stats2.Length; i++)
                {
                    var (label, stat, mult) = stats2[i];
                    if (GUI.Button(new Rect(x + i * 110f, y, 100f, 26f), label))
                    {
                        Sandbox.BuffTower(_editingTower.Tower, stat, mult);
                        _activeTowers = Sandbox.GetTowers();
                    }
                }
                if (GUI.Button(new Rect(x + 450f, y, 110f, 26f), "Reset Buffs"))
                {
                    Sandbox.ResetTowerBuffs(_editingTower.Tower);
                    _activeTowers = Sandbox.GetTowers();
                }
                y += 34f;

                if (GUI.Button(new Rect(x, y, 80f, 24f), "Pops 1K")) Sandbox.SetTowerPops(_editingTower.Tower, 1000);
                if (GUI.Button(new Rect(x + 84f, y, 80f, 24f), "Pops 1M")) Sandbox.SetTowerPops(_editingTower.Tower, 1000000);
                if (GUI.Button(new Rect(x + 168f, y, 80f, 24f), "Pops 10M")) Sandbox.SetTowerPops(_editingTower.Tower, 10000000);
                if (GUI.Button(new Rect(x + 252f, y, 90f, 24f), "Pops 100M")) Sandbox.SetTowerPops(_editingTower.Tower, 100000000);
                var isParagon = _editingTower.Tower.towerModel?.isParagon == true;
                if (isParagon)
                {
                    if (GUI.Button(new Rect(x + 350f, y, 120f, 24f), "Paragon Level 100")) Sandbox.SetParagonDegree(_editingTower.Tower, 100);
                    if (GUI.Button(new Rect(x + 476f, y, 90f, 24f), "Paragon 1")) Sandbox.SetParagonDegree(_editingTower.Tower, 1);
                }
                y += 32f;
            }

            var viewport = new Rect(x, y, w - x - 14f, Math.Max(120f, _windowRect.height - y - 40f));
            var content = new Rect(0f, 0f, Math.Max(1f, viewport.width - 20f), Math.Max(1f, _activeTowers.Count * 30f));

            _scroll = GUI.BeginScrollView(viewport, _scroll, content);
            var ry = 0f;
            foreach (var info in _activeTowers)
            {
                GUI.Label(new Rect(0f, ry, 200f, 24f), $"{Pretty(info.Name)} {info.Tiers}");
                GUI.Label(new Rect(205f, ry, 120f, 24f), $"dmg {info.Damage:N0}");

                if (GUI.Button(new Rect(330f, ry, 55f, 24f), "Edit"))
                {
                    _editingTower = info;
                }
                if (GUI.Button(new Rect(390f, ry, 70f, 24f), "Sell"))
                {
                    Sandbox.SellTower(info.Tower);
                    if (_editingTower == info) _editingTower = null;
                    _activeTowers = Sandbox.GetTowers();
                }

                ry += 30f;
            }
            GUI.EndScrollView();
        }
        private void DrawUnlocksTab(float x, float y, float w)
        {
            GUI.Label(new Rect(x, y, w - 20f, 22f), "Account unlocks (these write to your profile)");
            y += 28f;

            if (GUI.Button(new Rect(x, y, 220f, 30f), "Unlock All Towers")) UnlockAllTowers();
            if (GUI.Button(new Rect(x + 228f, y, 220f, 30f), "Unlock All Heroes")) UnlockAllHeroes();
            y += 38f;

            if (GUI.Button(new Rect(x, y, 220f, 30f), "Unlock All Upgrades")) UnlockAllUpgrades();
            if (GUI.Button(new Rect(x + 228f, y, 220f, 30f), "Unlock All Paragons")) UnlockAllParagons();
            y += 38f;

            if (GUI.Button(new Rect(x, y, 220f, 30f), "Unlock All Trophy Items")) UnlockTrophyItems();
            if (GUI.Button(new Rect(x + 228f, y, 220f, 30f), "Refund All Knowledge")) RefundAllKnowledge();
            y += 44f;

            GUI.Label(new Rect(x, y, w - 20f, 22f),
                "Knowledge refund clears acquired knowledge so the points can be spent again.");
            y += 24f;
            GUI.Label(new Rect(x, y, w - 20f, 22f),
                "Map/mode medal unlocks are not included yet; they live in a different profile section.");
        }

        public static List<string> GetHeroIds()
        {
            var ids = new List<string>();
            try
            {
                var details = Game.instance?.GetHeroDetailModels();
                if (details != null)
                {
                    for (int i = 0; i < details.Length; i++)
                    {
                        var d = details[i];
                        if (d == null || string.IsNullOrEmpty(d.towerId)) continue;
                        if (!ids.Contains(d.towerId)) ids.Add(d.towerId);
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("GetHeroIds failed: " + e.Message);
            }

            try
            {
                var towers = Game.instance?.model?.towers;
                if (towers != null)
                {
                    for (int i = 0; i < towers.Length; i++)
                    {
                        var tower = towers[i];
                        if (tower == null || !tower.IsHero() || string.IsNullOrEmpty(tower.BaseId)) continue;
                        if (!ids.Contains(tower.BaseId)) ids.Add(tower.BaseId);
                    }
                }

                var unlocked = Game.instance?.GetPlayerProfile()?.unlockedHeroes;
                if (unlocked != null)
                {
                    foreach (var hero in unlocked)
                    {
                        if (!string.IsNullOrEmpty(hero) && !ids.Contains(hero)) ids.Add(hero);
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("GetHeroIds fallback failed: " + e.Message);
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        public static void UnlockAllTowers()
        {
            var player = GetPlayer();
            if (player == null) return;
            try
            {
                var data = player.Data;
                foreach (var id in GetBaseTowerIds()) data.unlockedTowers.Add(id);
                SaveProfile(player);
                MelonLogger.Msg($"Unlocked {data.unlockedTowers.Count} towers.");
            }
            catch (Exception e)
            {
                MelonLogger.Error("Unlock towers failed: " + e);
            }
        }

        public static void UnlockAllHeroes()
        {
            var player = GetPlayer();
            if (player == null) return;
            try
            {
                var data = player.Data;
                foreach (var id in GetHeroIds()) data.unlockedHeroes.Add(id);
                SaveProfile(player);
                MelonLogger.Msg($"Unlocked {data.unlockedHeroes.Count} heroes.");
            }
            catch (Exception e)
            {
                MelonLogger.Error("Unlock heroes failed: " + e);
            }
        }

        public static void UnlockAllUpgrades()
        {
            var player = GetPlayer();
            if (player == null) return;
            try
            {
                var data = player.Data;
                var upgrades = Game.instance?.model?.upgrades;
                if (upgrades == null) return;

                for (int i = 0; i < upgrades.Length; i++)
                {
                    var upgrade = upgrades[i];
                    if (upgrade == null || string.IsNullOrEmpty(upgrade.name)) continue;
                    data.acquiredUpgrades.Add(upgrade.name);
                    data.viewedUpgrades.Add(upgrade.name);
                }

                SaveProfile(player);
                MelonLogger.Msg($"Unlocked {data.acquiredUpgrades.Count} upgrades.");
            }
            catch (Exception e)
            {
                MelonLogger.Error("Unlock upgrades failed: " + e);
            }
        }

        public static void UnlockAllParagons()
        {
            var player = GetPlayer();
            if (player == null) return;
            try
            {
                var data = player.Data;
                var upgrades = Game.instance?.model?.upgrades;
                if (upgrades == null) return;

                int count = 0;
                for (int i = 0; i < upgrades.Length; i++)
                {
                    var upgrade = upgrades[i];
                    if (upgrade == null || string.IsNullOrEmpty(upgrade.name)) continue;
                    if (!upgrade.name.Contains("Paragon")) continue;
                    data.paragonUpgradesPurchased.Add(upgrade.name);
                    data.acquiredUpgrades.Add(upgrade.name);
                    count++;
                }

                SaveProfile(player);
                MelonLogger.Msg($"Unlocked {count} paragon upgrades.");
            }
            catch (Exception e)
            {
                MelonLogger.Error("Unlock paragons failed: " + e);
            }
        }

        public static void RefundAllKnowledge()
        {
            var player = GetPlayer();
            if (player == null) return;
            try
            {
                var data = player.Data;
                data.acquiredKnowledge.Clear();
                data.paidForKnowledge.Clear();
                data.knowledgeDisabled = false;
                SaveProfile(player);
                MelonLogger.Msg("Knowledge refunded (acquired knowledge cleared).");
            }
            catch (Exception e)
            {
                MelonLogger.Error("Knowledge refund failed: " + e);
            }
        }
        private void DrawResourcesTab(float x, float y, float w)
        {
            GUI.Label(new Rect(x, y, w - 20f, 22f),
                $"Monkey money: {_monkeyMoney:N0}    Trophies: {_trophyBalance:N0}");
            y += 30f;

            GUI.Label(new Rect(x, y, 200f, 22f), "Monkey money amount:");
            GUI.Label(new Rect(x + 205f, y, 120f, 22f), ((int)MoneyAmount).ToString("N0"));
            y += 26f;
            DrawStepper(x, y, (int)MoneyAmount, v => MoneyAmount.SetValue((long)Clamp(v, 0, 2000000000)));
            y += 28f;
            DrawPresets(x, y, new[] { 0, 1000, 1000000, 10000000, 100000000, 1000000000 },
                v => MoneyAmount.SetValue((long)v));
            y += 30f;
            if (GUI.Button(new Rect(x, y, 70f, 24f), "Add")) AddMoneyAndXp();
            if (GUI.Button(new Rect(x + 78f, y, 100f, 24f), "Set Exact"))
            {
                try
                {
                    Game.instance.SetMonkeyMoney(MoneyAmount);
                    RefreshDataIfNeeded(true);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("Set money failed: " + e.Message);
                }
            }
            y += 36f;

            GUI.Label(new Rect(x, y, 200f, 22f), "Tower XP amount:");
            GUI.Label(new Rect(x + 205f, y, 120f, 22f), ((int)XpAmount).ToString("N0"));
            y += 26f;
            DrawStepper(x, y, (int)XpAmount, v => XpAmount.SetValue((long)Clamp(v, 0, 2000000000)));
            y += 28f;
            DrawPresets(x, y, new[] { 0, 1000, 1000000, 10000000, 100000000, 1000000000 },
                v => XpAmount.SetValue((long)v));
            y += 30f;
            if (GUI.Button(new Rect(x, y, 110f, 24f), "Add To All")) AddMoneyAndXp();
            y += 36f;

            GUI.Label(new Rect(x, y, 200f, 22f), "Trophy amount:");
            GUI.Label(new Rect(x + 205f, y, 120f, 22f), ((int)TrophyAmount).ToString("N0"));
            y += 26f;
            DrawStepper(x, y, (int)TrophyAmount, v => TrophyAmount.SetValue((long)Clamp(v, 0, 2000000000)));
            y += 28f;
            DrawPresets(x, y, new[] { 0, 100, 1000, 10000, 100000, 1000000 },
                v => TrophyAmount.SetValue((long)v));
            y += 30f;
            if (GUI.Button(new Rect(x, y, 120f, 24f), "Add Trophies")) AddTrophies();
            y += 40f;

            if (GUI.Button(new Rect(x, y, w - 40f, 30f), "Unlock ALL Monkey Knowledge")) UnlockAllKnowledge();
            y += 38f;
            if (GUI.Button(new Rect(x, y, w - 40f, 30f), "Unlock ALL Trophy Store Items")) UnlockTrophyItems();
            y += 38f;
            if (GUI.Button(new Rect(x, y, 180f, 26f), "Save Profile Now"))
            {
                var player = GetPlayer();
                if (player != null)
                {
                    try
                    {
                        player.Save();
                        MelonLogger.Msg("Profile save requested.");
                    }
                    catch (Exception e)
                    {
                        MelonLogger.Warning("Save failed: " + e.Message);
                    }
                }
            }
            y += 36f;

            if (GUI.Button(new Rect(x, y, 460f, 26f),
                    ((bool)LogDiagnostics ? "[x] " : "[ ] ") + "Log tower/power ids to the MelonLoader console"))
            {
                LogDiagnostics.SetValue(!(bool)LogDiagnostics);
                if (LogDiagnostics) LogGameIds();
            }
            y += 30f;
            if (GUI.Button(new Rect(x, y, 460f, 26f),
                    ((bool)SaveAfterGiving ? "[x] " : "[ ] ") + "Save the profile after bulk actions"))
            {
                SaveAfterGiving.SetValue(!(bool)SaveAfterGiving);
            }
        }

        private void DrawHelpTab(float x, float y, float w)
        {
            var lines = new[]
            {
                "What each action does",
                "",
                "Insta Monkeys tab",
                "  - The tower list comes from the game's tower set, so new towers (Skywarden, Mermonkey,",
                "    Desperado, and anything added later) are included automatically.",
                "  - 64 insta variants exist per tower: 0-0-0 and every crosspath with one path at 1-5",
                "    and at most one other path at 1-2 (the game has no 5-2-2 insta).",
                "  - The per-tower number is the quantity for every variant. The in-game group total is",
                "    roughly 64 x that number. Give adds, Set sets every variant, Exact puts the whole",
                "    number into a single 0-0-0 entry so the total matches exactly.",
                "  - Use x10 / /10 / + / - to reach large per-tower quantities, or set the default and use",
                "    Give All.",
                "",
                "Powers tab",
                "  - Give All Powers uses Btd6Player.AddPower, so every power in the game's power set gets",
                "    the quantity you choose (including event/DLC powers that exist in the set).",
                "  - Unlock All Powers Pro sets debugUnlockAllPowersPro, enables Powers Pro and maxes XP +",
                "    unlocked tier for every power id.",
                "",
                "Resources tab",
                "  - Monkey money, trophies, XP for every base tower, all monkey knowledge and every trophy",
                "    store item.",
                "",
                "Notes",
                "  - Changes are made in memory and saved to your profile. Give the game a moment after a",
                "    large action.",
                "  - If something fails, the MelonLoader console prints the id and the exact error. Enable",
                "    'Log tower/power ids' to dump the current lists after a game update.",
                "  - Modding a profile can break cloud sync. Back up your profile if the account matters."
            };

            var viewport = new Rect(x, y, w - x - 14f, Math.Max(120f, _windowRect.height - y - 40f));
            var content = new Rect(0f, 0f, Math.Max(1f, viewport.width - 20f), Math.Max(1f, lines.Length * 22f));

            _scroll = GUI.BeginScrollView(viewport, _scroll, content);
            for (int i = 0; i < lines.Length; i++)
            {
                GUI.Label(new Rect(0f, i * 22f, content.width, 20f), lines[i]);
            }
            GUI.EndScrollView();
        }

        private static void DrawStepper(float x, float y, int value, Action<int> set)
        {
            long[] steps = { 1, 10, 100, 1000, 10000, 100000, 1000000 };
            var bx = x;

            for (int i = steps.Length - 1; i >= 0; i--)
            {
                var step = steps[i];
                if (GUI.Button(new Rect(bx, y, 38f, 24f), "-" + Short(step)))
                {
                    set((int)Math.Max(0, Math.Min(int.MaxValue, (long)value - step)));
                }
                bx += 40f;
            }

            bx += 8f;
            for (int i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                if (GUI.Button(new Rect(bx, y, 38f, 24f), "+" + Short(step)))
                {
                    set((int)Math.Min(int.MaxValue, (long)value + step));
                }
                bx += 40f;
            }
        }

        private static void DrawPresets(float x, float y, int[] values, Action<int> set)
        {
            var bx = x;
            foreach (var value in values)
            {
                if (GUI.Button(new Rect(bx, y, 70f, 26f), value == 0 ? "0" : value.ToString("N0")))
                {
                    set(value);
                }
                bx += 74f;
            }
        }

        private static string Short(long value)
        {
            if (value >= 1000000) return (value / 1000000) + "M";
            if (value >= 1000) return (value / 1000) + "K";
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static void SetTowerOverride(string towerId, int value)
        {
            var clamped = Clamp(value, 0, 10000000);
            TowerQtyOverrides[towerId] = clamped;
            SaveTowerOverrides();
        }
        private IEnumerable<string> FilterTowers()
        {
            if (string.IsNullOrWhiteSpace(_search)) return _towerIds;

            var s = _search.Trim();
            return _towerIds.Where(id =>
                id.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 ||
                Pretty(id).IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private string GetTowerQtyText(string towerId)
        {
            if (_towerQtyText.TryGetValue(towerId, out var text)) return text;

            if (TowerQtyOverrides.TryGetValue(towerId, out var overridden))
            {
                text = overridden.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                text = ((int)InstaQuantity).ToString(CultureInfo.InvariantCulture);
            }

            _towerQtyText[towerId] = text;
            return text;
        }

        private void SetTowerQtyText(string towerId, string text)
        {
            _towerQtyText[towerId] = text ?? "";

            if (string.IsNullOrWhiteSpace(text))
            {
                TowerQtyOverrides.Remove(towerId);
            }
            else if (int.TryParse(text, out var qty))
            {
                TowerQtyOverrides[towerId] = Clamp(qty, 0, 10000000);
            }

            SaveTowerOverrides();
        }

        private static int ParseQty(string text)
        {
            if (int.TryParse(text, out var qty)) return Clamp(qty, 0, 10000000);
            return 0;
        }

        private void SetAllVariantsToDefault()
        {
            var player = GetPlayer();
            if (player == null) return;

            int qty = InstaQuantity;
            foreach (var towerId in _towerIds)
            {
                try
                {
                    GiveInstaForTower(player, towerId, qty, add: false);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning($"Set variants failed for '{towerId}': {e.Message}");
                }
            }

            SaveProfile(player);
            RefreshDataIfNeeded(true);
            MelonLogger.Msg($"Set every insta variant of every tower to {qty:N0}.");
        }

        private void RefreshDataIfNeeded(bool force = false)
        {
            if (!force && Time.realtimeSinceStartup - _lastRefresh < 1f) return;
            _lastRefresh = Time.realtimeSinceStartup;

            if (_towerIds.Count == 0) _towerIds = GetBaseTowerIds();
            if (_powerIds.Count == 0) _powerIds = GetPowerIds();

            var player = GetPlayer();
            if (player == null)
            {
                _totalInsta = 0;
                _monkeyMoney = 0;
                _trophyBalance = 0;
                _powersProEnabled = false;
                return;
            }

            _towerCounts.Clear();
            _towerVariants.Clear();
            foreach (var id in _towerIds)
            {
                _towerCounts[id] = SafeInt(() => player.GetInstaTowerGroupQuanity(id));
                _towerVariants[id] = SafeInt(() => player.GetInstaTowers(id)?.Count ?? 0);
            }

            _powerCounts.Clear();
            foreach (var id in _powerIds)
            {
                _powerCounts[id] = SafeInt(() =>
                {
                    var data = player.GetPowerData(id);
                    return data?.Quantity ?? 0;
                });
            }

            _totalInsta = SafeInt(() => player.GetNumberOfInstaTowers());

            try
            {
                _monkeyMoney = Game.instance.GetMonkeyMoney();
            }
            catch
            {
                _monkeyMoney = 0;
            }

            try
            {
                _trophyBalance = player.Data.trophies != null ? (int)player.Data.trophies.Value : 0;
            }
            catch
            {
                _trophyBalance = 0;
            }

            try
            {
                _powersProEnabled = player.Data.powersProSaveData != null &&
                                    player.Data.powersProSaveData.isEnabled;
            }
            catch
            {
                _powersProEnabled = false;
            }
        }

        private static string Pretty(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;

            var sb = new StringBuilder(id.Length + 8);
            for (int i = 0; i < id.Length; i++)
            {
                var c = id[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(id[i - 1]) && id[i - 1] != ' ')
                {
                    sb.Append(' ');
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
