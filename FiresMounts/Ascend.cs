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
    /// FiresMounts â€” alternate modes of transport for Valheim, starting with a proper rideable horse and later
    /// non-combat pets. Thin shell (mirrors FiresMausoleum/FiresWayshrines): loads the one 'firesmounts' bundle,
    /// registers its prefabs into ZNetScene the Jotunn-free way, and (task: runtime composer) assembles the
    /// vanilla creature stack + our custom mount controller onto the baked horse visual.
    ///
    /// The mount is 100% data-driven â€” vanilla has no hardcoded Asksvin/Lox class, so a horse is just a prefab
    /// with Character/MonsterAI/Tameable/Sadle + a gait animator. See reference_vanilla_mount_build.
    /// </summary>
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.Fire.FiresUnifiedCore", BepInDependency.DependencyFlags.HardDependency)]
    public class FiresMountsPlugin : FiresMod<FiresMountsPlugin>
    {
        public const string PluginGUID = "com.Fire.FiresMounts";
        public const string PluginName = "FiresMounts";
        public const string PluginVersion = "0.1.84";

        // FiresMounts' BepInEx log source. The banner headers route through this so
        // they print colored by source name without a raw white stdout duplicate.
        internal static BepInEx.Logging.ManualLogSource Log;

        internal ConfigSync configSync;

        private static readonly List<GameObject> _prefabs = new List<GameObject>();
        private static AssetBundle _bundle;
        internal static AssetBundle LoadedBundle => _bundle;

        // Swappable coat materials (FiresHorseCoat_<name>) loaded on demand for FiresMountCoat's per-horse random
        // coat. Cached (LoadAsset is cheap but this is called per spawn). Returns null on an older bundle without
        // the coat set â€” the caller then keeps the horse's baked default coat.
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
            MountNames.RefabHorsePrefab,
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
            LoadBundleAndPrefabs();
            MountCommands.Register();

            Logger.LogInfo($"{PluginName} v{PluginVersion} setup complete.");
        }

        protected override void WorldStart() { FiresMountsBanner.Print(); }
        protected override void TitleScene(bool isFirstBoot) { }
        protected override void Shutdown() { }

        // â”€â”€ bundle load â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void LoadBundleAndPrefabs()
        {
            try
            {
                _bundle = GetAssetBundleFromResources(MountNames.BundleResource);
                if (_bundle == null)
                {
                    FiresLogger.LogWarning("bundle not found â€” horse assets not yet baked; mounts will not spawn until the bundle ships.");
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
                return AssetBundle.LoadFromMemory(ms.ToArray());   // full copy â€” disposed-stream lazy re-read crashes the dedi
            }
        }

        // â”€â”€ ZNetScene registration (fake-null scrub + relogin re-acquire, mirrors the Fires family) â”€â”€

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        internal static class ZNetScenePatches
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                try { RegisterPrefabs(); }
                catch (Exception ex) { Debug.LogError($"[FiresMounts] ZNetScene register failed: {ex}"); }
            }
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        internal static class ObjectDBPatches
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                try { FixShaders(); }
                catch (Exception ex) { Debug.LogWarning($"[FiresMounts] shader fix skipped: {ex.Message}"); }
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
                // if composition fails (e.g. Lox not yet available) â€” EnsurePrefabsLoaded retries next pass.
                if (!MountFactory.Compose(prefab)) continue;

                RegisterByName(znet, named, prefab);
            }

            RegisterReferenceHorses(znet, named);
        }

        // The PROVEN working horse, verbatim: the yoinked MagicalMounts bundle (pure vanilla components â€” no
        // MagicalMounts code needed) registered UNMODIFIED as the reference/daily-driver mounts while FiresHorse
        // is refined to match. Loaded from FAP's external-assets dir; reused if FAP already loaded it.
        private static AssetBundle _mmBundle;
        private static void RegisterReferenceHorses(ZNetScene znet, Dictionary<int, GameObject> named)
        {
            try
            {
                if (_mmBundle == null)
                    foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
                        if (b != null && b.name == "mm_bundle") { _mmBundle = b; break; }
                if (_mmBundle == null)
                {
                    string path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "FiresAdminPrefabs", "Assets", "mm_bundle");
                    if (!System.IO.File.Exists(path))
                    { FiresLogger.LogWarning($"[FiresMounts] mm_bundle NOT FOUND at {path} â€” reference horses + FiresHorseMk2 will NOT register; only the scratch FiresHorse can spawn."); return; }
                    _mmBundle = AssetBundle.LoadFromFile(path);   // big bundle: file-backed load, never LoadFromMemory
                }
                if (_mmBundle == null) return;

                int n = 0;
                foreach (var go in _mmBundle.LoadAllAssets<GameObject>())
                {
                    if (go == null || go.GetComponent<ZNetView>() == null) continue;
                    RegisterByName(znet, named, go);
                    // their saddle node has NO collider (their plugin mounts via its own UI) â€” the vanilla
                    // E-to-ride path needs one to hover/interact, like the Lox's Sadel sphere. The saddle sits
                    // under a scale-100 bone chain, so the radius must counter lossyScale.
                    var sadle = go.GetComponentInChildren<Sadle>(true);
                    if (sadle != null && sadle.GetComponent<Collider>() == null)
                    {
                        var sc = sadle.gameObject.AddComponent<SphereCollider>();
                        sc.radius = 0.5f / Mathf.Max(sadle.transform.lossyScale.x, 0.0001f);
                    }
                    n++;
                }
                FiresLogger.LogInfo($"[FiresMounts] registered {n} reference mount prefabs from mm_bundle.");
                RegisterCookieClone(znet, named);
            }
            catch (Exception ex) { Debug.LogError($"[FiresMounts] mm_bundle register failed: {ex}"); }
        }

        // OUR mount on the PROVEN body: a clone of the working horse under our own prefab name, with our handling
        // attached â€” E-to-ride saddle collider, reins steering / stop-on-release / jump-hop / E-dismount /
        // no-dismount-on-combat (all gate on FiresMountController), obstacle roller, food taming, our configs.
        // Their physics/animator/skeleton stay VERBATIM â€” that's the part that works.
        private static void RegisterCookieClone(ZNetScene znet, Dictionary<int, GameObject> named)
        {
            const string CloneName = "FiresHorseMk2";
            try
            {
                if (znet.GetPrefab(CloneName) != null) return;
                GameObject src = _mmBundle != null ? _mmBundle.LoadAsset<GameObject>("mm_horse_black_mount") : null;
                if (src == null) { FiresLogger.LogWarning("[FiresMounts] mm_horse_black_mount not found â€” no Mk2 clone."); return; }

                GameObject clone = FiresCore.Net.NetworkObjectHelper.InstantiateInactiveClone(src, CloneName);
                if (clone == null) return;

                var nv = clone.GetComponent<ZNetView>();
                if (nv != null) nv.m_persistent = true;                       // theirs is summon-style; ours is a kept animal

                var rb = clone.GetComponent<Rigidbody>();
                if (rb != null) rb.interpolation = RigidbodyInterpolation.Interpolate;   // smooth body between physics ticks

                var hum = clone.GetComponent<Humanoid>();
                if (hum != null)
                {
                    hum.m_name = "Horse";
                    hum.m_walkSpeed = MountConfig.WalkSpeed.Value * 0.5f;
                    hum.m_speed = MountConfig.WalkSpeed.Value;
                    hum.m_runSpeed = MountConfig.RunSpeed.Value;
                    hum.m_acceleration = MountConfig.Acceleration.Value;
                }

                var tame = clone.GetComponent<Tameable>();
                if (tame != null) tame.m_tamingTime = Mathf.Max(1f, MountConfig.TameTimeMinutes.Value * 60f);
                MountFactory.ConfigureFood(clone);

                var sadle = clone.GetComponentInChildren<Sadle>(true);
                if (sadle != null && sadle.GetComponent<Collider>() == null)
                {
                    var sc = sadle.gameObject.AddComponent<SphereCollider>();
                    sc.radius = 0.5f / Mathf.Max(sadle.transform.lossyScale.x, 0.0001f);
                }

                if (clone.GetComponent<FiresMountController>() == null) clone.AddComponent<FiresMountController>();
                if (clone.GetComponent<FiresMountObstacleRoller>() == null) clone.AddComponent<FiresMountObstacleRoller>();

                ColliderBakeReader.Apply(clone, CloneName);   // in-world "Save as default" overrides for Mk2

                RegisterByName(znet, named, clone);
                FiresLogger.LogInfo($"[FiresMounts] registered {CloneName} (working-body clone + Fires handling).");
            }
            catch (Exception ex) { Debug.LogError($"[FiresMounts] {CloneName} clone failed: {ex}"); }
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
        // NATIVE materials policy: the bundle's Standard-shader materials render as-authored â€” no shader warm,
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

