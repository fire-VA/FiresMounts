using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// Natural horse spawns (Fire 10-07): Plains is where horses live (common herds), Meadows a rare lone horse.
    /// One SpawnSystemList of our own is appended to every SpawnSystem as it wakes; its SpawnData are rebuilt from
    /// the Mounts.Spawns config each time, so a config change applies to zones loaded after it.
    /// </summary>
    internal static class MountSpawns
    {
        const string SPAWNS = "Mounts.Spawns";

        public static ConfigEntry<bool> SpawnEnabled;
        public static ConfigEntry<float> PlainsChance, PlainsInterval, MeadowsChance, MeadowsInterval;
        public static ConfigEntry<int> PlainsMax, PlainsGroupMin, PlainsGroupMax, MeadowsMax;

        static SpawnSystemList _list;

        public static void Bind(ConfigFile cfg)
        {
            SpawnEnabled = cfg.Bind(SPAWNS, "Enabled", true,
                "Horses spawn naturally in the Plains (common herds) and, rarely, alone in the Meadows. OFF = only the firesmounts_spawn command makes them.");
            PlainsChance = cfg.Bind(SPAWNS, "PlainsChance", 25f, new ConfigDescription(
                "Chance (%) per spawn check that a Plains herd appears near a player.", new AcceptableValueRange<float>(0f, 100f)));
            PlainsInterval = cfg.Bind(SPAWNS, "PlainsInterval", 300f, new ConfigDescription(
                "Seconds between Plains spawn checks.", new AcceptableValueRange<float>(30f, 3600f)));
            PlainsMax = cfg.Bind(SPAWNS, "PlainsMax", 3, new ConfigDescription(
                "Most horses alive near a player before Plains spawning stops.", new AcceptableValueRange<int>(1, 10)));
            PlainsGroupMin = cfg.Bind(SPAWNS, "PlainsGroupMin", 2, new ConfigDescription(
                "Smallest Plains herd.", new AcceptableValueRange<int>(1, 10)));
            PlainsGroupMax = cfg.Bind(SPAWNS, "PlainsGroupMax", 4, new ConfigDescription(
                "Largest Plains herd.", new AcceptableValueRange<int>(1, 10)));
            MeadowsChance = cfg.Bind(SPAWNS, "MeadowsChance", 2f, new ConfigDescription(
                "Chance (%) per spawn check that a lone horse appears in the Meadows (clear skies, daytime).", new AcceptableValueRange<float>(0f, 100f)));
            MeadowsInterval = cfg.Bind(SPAWNS, "MeadowsInterval", 900f, new ConfigDescription(
                "Seconds between Meadows spawn checks.", new AcceptableValueRange<float>(30f, 3600f)));
            MeadowsMax = cfg.Bind(SPAWNS, "MeadowsMax", 1, new ConfigDescription(
                "Most horses alive near a player before Meadows spawning stops.", new AcceptableValueRange<int>(1, 10)));

            // Live: a changed value rebuilds the shared list in place, so already-awake SpawnSystems use it at once.
            foreach (ConfigEntryBase e in new ConfigEntryBase[] { SpawnEnabled, PlainsChance, PlainsInterval, PlainsMax, PlainsGroupMin,
                         PlainsGroupMax, MeadowsChance, MeadowsInterval, MeadowsMax })
                cfg.SettingChanged += (s, a) => { if (a.ChangedSetting == e) Refresh(); };
        }

        static void Refresh()
        {
            try
            {
                if (_list == null || ZNetScene.instance == null) return;
                var horse = ZNetScene.instance.GetPrefab(MountNames.HorsePrefab);
                if (horse != null) _list.m_spawners = Build(horse);
            }
            catch (Exception ex) { FiresLogger.LogWarning($"[FiresMounts] spawn list refresh skipped: {ex.Message}"); }
        }

        static List<SpawnSystem.SpawnData> Build(GameObject horse)
        {
            var plains = new SpawnSystem.SpawnData
            {
                m_name = "FiresHorse_Plains",
                m_enabled = SpawnEnabled.Value,
                m_prefab = horse,
                m_biome = Heightmap.Biome.Plains,
                m_spawnChance = PlainsChance.Value,
                m_spawnInterval = PlainsInterval.Value,
                m_maxSpawned = PlainsMax.Value,
                m_groupSizeMin = Math.Min(PlainsGroupMin.Value, PlainsGroupMax.Value),
                m_groupSizeMax = Math.Max(PlainsGroupMin.Value, PlainsGroupMax.Value),
                m_groupRadius = 6f,
                m_spawnAtDay = true,
                m_spawnAtNight = false,
                m_minAltitude = 5f,
                m_maxAltitude = 1000f,
                m_maxLevel = 3,
            };
            var meadows = new SpawnSystem.SpawnData
            {
                m_name = "FiresHorse_Meadows",
                m_enabled = SpawnEnabled.Value,
                m_prefab = horse,
                m_biome = Heightmap.Biome.Meadows,
                m_spawnChance = MeadowsChance.Value,
                m_spawnInterval = MeadowsInterval.Value,
                m_maxSpawned = MeadowsMax.Value,
                m_groupSizeMin = 1,
                m_groupSizeMax = 1,
                m_spawnAtDay = true,
                m_spawnAtNight = false,
                m_minAltitude = 5f,
                m_maxAltitude = 1000f,
                m_requiredEnvironments = new List<string> { "Clear" },
            };
            return new List<SpawnSystem.SpawnData> { plains, meadows };
        }

        [HarmonyPatch(typeof(SpawnSystem), "Awake")]
        internal static class SpawnSystemAwakePatch
        {
            [HarmonyPostfix]
            private static void Postfix(SpawnSystem __instance)
            {
                try
                {
                    if (!MountConfig.Enabled.Value || ZNetScene.instance == null) return;
                    var horse = ZNetScene.instance.GetPrefab(MountNames.HorsePrefab);
                    if (horse == null) return;
                    if (_list == null)
                    {
                        var go = new GameObject("FiresMounts_SpawnList");
                        UnityEngine.Object.DontDestroyOnLoad(go);
                        _list = go.AddComponent<SpawnSystemList>();
                    }
                    _list.m_spawners = Build(horse);
                    if (!__instance.m_spawnLists.Contains(_list))
                        __instance.m_spawnLists.Add(_list);
                }
                catch (Exception ex) { FiresLogger.LogWarning($"[FiresMounts] spawn list skipped: {ex.Message}"); }
            }
        }
    }
}
