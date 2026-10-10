using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using FiresCore.Lifecycle;
using FiresCore.Sync;
using HarmonyLib;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// FiresMounts - alternate modes of transport for Valheim, starting with a proper rideable horse and later
    /// non-combat pets. Thin shell (mirrors FiresMausoleum/FiresWayshrines): loads the one 'firesmounts' bundle,
    /// registers its prefabs into ZNetScene the Jotunn-free way, and (task: runtime composer) assembles the
    /// vanilla creature stack + our custom mount controller onto the baked horse visual.
    ///
    /// The mount is 100% data-driven - vanilla has no hardcoded Asksvin/Lox class, so a horse is just a prefab
    /// with Character/MonsterAI/Tameable/Sadle + a gait animator. See reference_vanilla_mount_build.
    /// </summary>
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.Fire.FiresUnifiedCore", BepInDependency.DependencyFlags.HardDependency)]
    public class FiresMountsPlugin : FiresMod<FiresMountsPlugin>
    {
        public const string PluginGUID = "com.Fire.FiresMounts";
        public const string PluginName = "FiresMounts";
        public const string PluginVersion = "0.1.131";

        // FiresMounts' BepInEx log source. The banner headers route through this so
        // they print colored by source name without a raw white stdout duplicate.
        internal static BepInEx.Logging.ManualLogSource Log;

        internal ConfigSync configSync;

        private static readonly List<GameObject> _prefabs = new List<GameObject>();
        private static AssetBundle _bundle;
        internal static AssetBundle LoadedBundle => _bundle;

        // Swappable coat materials (FiresHorseCoat_<name>) loaded on demand for FiresMountCoat's per-horse random
        // coat. Cached (LoadAsset is cheap but this is called per spawn). Returns null on an older bundle without
        // the coat set - the caller then keeps the horse's baked default coat.
        private static readonly Dictionary<string, Material> _coatCache = new Dictionary<string, Material>();
        internal static Material LoadCoat(string coatName)
        {
            if (string.IsNullOrEmpty(coatName) || _bundle == null) return null;
            if (_coatCache.TryGetValue(coatName, out var cached)) return cached;
            Material m = null;
            try { m = _bundle.LoadAsset<Material>("FiresHorseCoat_" + coatName); } catch { }
            _coatCache[coatName] = m;
            return m;
        }

        // Prefabs the bundle owns (baked by FiresMountBuilder.cs in the Unity project).
        private static readonly HashSet<string> _ownedPrefabNames = new HashSet<string>
        {
            MountNames.HorsePrefab,
        };

        protected override void Setup()
        {
            Log = Logger;
            FiresMountsBanner.PrintBig();

            configSync = new ConfigSync(PluginGUID)
            {
                DisplayName = PluginName,
                CurrentVersion = PluginVersion,
                MinimumRequiredVersion = PluginVersion,
            };

            MountConfig.Bind(Config);

            // Fire 10-07: gameplay values are SERVER-authoritative, so every peer spawns, tames and rides the same in
            // MP (vanilla spawns run on whichever peer owns the zone). Looks, audio and boot-time switches stay local.
            configSync.IsLocked = true;
            foreach (var e in new[] { MountSpawns.PlainsChance, MountSpawns.PlainsInterval, MountSpawns.MeadowsChance,
                                      MountSpawns.MeadowsInterval, MountConfig.TameTimeMinutes, MountConfig.MaxStamina,
                                      MountConfig.WalkSpeed, MountConfig.RunSpeed, MountConfig.TurnSpeed, MountConfig.Acceleration })
                configSync.AddConfigEntry(e);
            foreach (var e in new[] { MountSpawns.PlainsMax, MountSpawns.PlainsGroupMin, MountSpawns.PlainsGroupMax, MountSpawns.MeadowsMax })
                configSync.AddConfigEntry(e);
            configSync.AddConfigEntry(MountSpawns.SpawnEnabled);
            configSync.AddConfigEntry(MountConfig.EnableRiderJump);
            configSync.AddConfigEntry(MountConfig.AutoHitch);
            configSync.AddConfigEntry(MountConfig.TamingFood);
            // The server's food list arrives after ObjectDB.Awake wired the local one: re-wire prefab + live horses.
            MountConfig.TamingFood.SettingChanged += (s, a) =>
            {
                try
                {
                    foreach (var p in _prefabs) if (p != null) MountFactory.ConfigureFood(p);
                    foreach (var c in Character.GetAllCharacters())
                        if (c != null && c.GetComponent<FiresMountController>() != null) MountFactory.ConfigureFood(c.gameObject);
                }
                catch (Exception ex) { Debug.LogWarning($"[FiresMounts] taming food resync: {ex.Message}"); }
            };
            // Flyers: hatch time, drake food and taming time are gameplay (server); scales and seats stay local.
            configSync.AddConfigEntry(MountConfig.HatchSeconds);
            configSync.AddConfigEntry(MountConfig.DrakeFood);
            configSync.AddConfigEntry(MountConfig.DrakeEggChance);
            configSync.AddConfigEntry(MountConfig.FightWhenFollowing);
            configSync.AddConfigEntry(MountConfig.FaderClimbDrain);
            configSync.AddConfigEntry(MountConfig.GroundMountsEnabled);
            configSync.AddConfigEntry(MountConfig.DrakeTameMinutes);
            MountConfig.DrakeFood.SettingChanged += (s, a) => FlyerFactory.RefreshDrakeFood();
            MountConfig.DrakeTameMinutes.SettingChanged += (s, a) => FlyerFactory.RefreshTameTime();
            // Mini Fader leap / glide and the whistle's cooldown + distance are gameplay (server); its scale, seat and the key stay local.
            foreach (var e in new[] { MountConfig.FaderLeapHeight, MountConfig.FaderLeapForward, MountConfig.FaderLeapChargeSeconds,
                                      MountConfig.FaderGlideSink, MountConfig.FaderGlideSpeed, MountConfig.FaderGlideTurn,
                                      MountConfig.WhistleCooldown, MountConfig.WhistleDistance })
                configSync.AddConfigEntry(e);
            configSync.AddConfigEntry(MountConfig.WhistleEnabled);
            configSync.AddConfigEntry(MountConfig.LevelsEnabled);
            configSync.AddConfigEntry(MountConfig.FaderClimb);
            // 0.1.111 ([release]): the creature mounts' attacks and the RMB shield are gameplay too.
            configSync.AddConfigEntry(MountConfig.FlyerMountAttacks);
            foreach (var e in new[] { MountConfig.FlyerAttackDamage, MountConfig.FlyerShieldSeconds, MountConfig.FlyerShieldCooldown })
                configSync.AddConfigEntry(e);
            configSync.AddConfigEntry(MountConfig.FaderClimbSpeed);
            foreach (var e in new[] { MountConfig.LevelsXpRate, MountConfig.PoolModer, MountConfig.PoolDrake, MountConfig.PoolFader })
                configSync.AddConfigEntry(e);
            LoadBundleAndPrefabs();
            MountCommands.Register();
            MountWhistle.Install(gameObject);
            MountPillion.Install();   // 0.1.130: the pillion seat (Core's PillionApi provider)   // the double tap + its routed RPCs (the server answers the calls)

            try { MountHelpContent.Register(); }
            catch (System.Exception ex) { Logger.LogWarning($"Help registration failed: {ex.Message}"); }

            Logger.LogInfo($"{PluginName} v{PluginVersion} setup complete.");
        }

        protected override void WorldStart() { FiresMountsBanner.Print(); }
        protected override void TitleScene(bool isFirstBoot) { }
        protected override void Shutdown() { }

        // -- bundle load ----------------------------------------------------------

        private void LoadBundleAndPrefabs()
        {
            try
            {
                _bundle = GetAssetBundleFromResources(MountNames.BundleResource);
                if (_bundle == null)
                {
                    FiresLogger.LogWarning("bundle not found - horse assets not yet baked; mounts will not spawn until the bundle ships.");
                    return;
                }
                CollectPrefabs(_bundle.LoadAllAssets<GameObject>());
                FiresLogger.LogInfo($"bundle loaded; {_prefabs.Count} prefab(s).");
            }
            catch (Exception ex) { Debug.LogError($"[FiresMounts] bundle/prefab load failed: {ex}"); }
        }

        private static void CollectPrefabs(GameObject[] assets)
        {
            foreach (GameObject p in assets)
            {
                if (p == null) continue;
                if (!_ownedPrefabNames.Contains(p.name)) continue;
                UnityEngine.Object.DontDestroyOnLoad(p);
                if (!_prefabs.Contains(p)) _prefabs.Add(p);
            }
        }

        private AssetBundle GetAssetBundleFromResources(string name)
        {
            foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
                if (loaded != null && string.Equals(loaded.name, name, StringComparison.OrdinalIgnoreCase))
                    return loaded;

            Assembly asm = Assembly.GetExecutingAssembly();
            string resourceName = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null) return null;

            using (System.IO.Stream stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                using var ms = new System.IO.MemoryStream();
                stream.CopyTo(ms);
                return AssetBundle.LoadFromMemory(ms.ToArray());   // full copy - disposed-stream lazy re-read crashes the dedi
            }
        }

        // -- ZNetScene registration (fake-null scrub + relogin re-acquire, mirrors the Fires family) --

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        internal static class ZNetScenePatches
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                try { RegisterPrefabs(); }
                catch (Exception ex) { Debug.LogError($"[FiresMounts] ZNetScene register failed: {ex}"); }
                FlyerFactory.EnsureRegistered();   // flyer clones need the vanilla Dragon/Hatchling prefabs (own try/catch)
            }
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        internal static class ObjectDBPatches
        {
            [HarmonyPostfix]
            private static void Postfix(ObjectDB __instance)
            {
                try { FixShaders(); }
                catch (Exception ex) { Debug.LogWarning($"[FiresMounts] shader fix skipped: {ex.Message}"); }
                FlyerFactory.EnsureRegistered(__instance);   // Sky saddle + Moder egg items, saddle recipe (own try/catch)
            }
        }

        private static void RegisterPrefabs()
        {
            ZNetScene znet = ZNetScene.instance;
            if (znet == null || !MountConfig.Enabled.Value) return;

            EnsurePrefabsLoaded();

            FieldInfo namedField = typeof(ZNetScene).GetField("m_namedPrefabs", BindingFlags.NonPublic | BindingFlags.Instance);
            var named = namedField?.GetValue(znet) as Dictionary<int, GameObject>;

            znet.m_prefabs.RemoveAll(p => p == null);
            if (named != null)
            {
                var dead = new List<int>();
                foreach (var kvp in named) if (kvp.Value == null) dead.Add(kvp.Key);
                foreach (var k in dead) named.Remove(k);
            }

            foreach (GameObject prefab in _prefabs)
            {
                if (prefab == null) continue;

                // Compose the vanilla creature stack (Humanoid/MonsterAI/Tameable/Sadle/ZSync*) onto the baked
                // visual, seeded from the live Lox values. Adds a persistent ZNetView itself. Skip registration
                // if composition fails (e.g. Lox not yet available) - EnsurePrefabsLoaded retries next pass.
                if (!MountFactory.Compose(prefab)) continue;

                RegisterByName(znet, named, prefab);
            }
        }

        private static void RegisterByName(ZNetScene znet, Dictionary<int, GameObject> named, GameObject go)
        {
            if (go == null) return;
            if (znet.GetPrefab(go.name) != null) return;
            znet.m_prefabs.Add(go);
            int hash = go.name.GetStableHashCode();
            if (named != null && !named.ContainsKey(hash)) named.Add(hash, go);
        }

        // Swap bundle-orphaned Custom/* shaders + _Copy materials for the live vanilla ones (magenta guard).
        // NATIVE materials policy: the bundle's Standard-shader materials render as-authored - no shader warm,
        // no VanillaAssetResolver pass, no texture rebinding. ObjectDB.Awake only wires the taming food.
        private static void FixShaders()
        {
            EnsurePrefabsLoaded();
            foreach (GameObject prefab in _prefabs)
            {
                if (prefab == null) continue;
                try { MountFactory.ConfigureFood(prefab); }
                catch (Exception ex) { Debug.LogWarning($"[FiresMounts] taming food {prefab.name}: {ex.Message}"); }
            }
        }

        // Re-acquire bundle prefabs if a relogin's UnloadUnusedAssets collected them (DontDestroyOnLoad is a no-op
        // on bundle assets), else spawns stop until a full restart. No-op when healthy.
        private static void EnsurePrefabsLoaded()
        {
            if (_bundle == null) return;
            if (_prefabs.Count > 0 && !_prefabs.Exists(p => p == null)) return;
            _prefabs.RemoveAll(p => p == null);
            CollectPrefabs(_bundle.LoadAllAssets<GameObject>());
            FiresLogger.LogInfo($"re-acquired prefabs after relogin ({_prefabs.Count}).");
        }
    }
}

