using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// FiresMounts' gear bundle "firesmountsgear" (0.1.108, built in the 1.0 rip by FiresMountsGearBundle): [worldgen]'s saddles for the
    /// mini Moder, the drake and the Mini Fader (Fire approved 10-08; the drake and the Fader at 75% width) and the decimated mount whistle.
    /// Embedded in the DLL next to "firesmounts" (its own bundle name, so no CAB clash). The bundle's materials use Unity's Standard; each
    /// shader is re-resolved by name against the game's own at load. A missing bundle (an older build) only leaves the stand-ins.
    /// </summary>
    internal static class MountGear
    {
        internal const string BundleResource = "firesmountsgear";
        internal const string WhistleModel = "FiresMountWhistle_model";
        private static AssetBundle _bundle;
        private static bool _tried;
        private static readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();

        /// <summary>A texture from the bundle (0.1.117: the saddle look albedos "FiresSaddle_Albedo_&lt;Look&gt;"), or null.</summary>
        internal static Texture2D Texture(string name)
        {
            Load();
            return name != null && _textures.TryGetValue(name, out var t) && t != null ? t : null;
        }

        /// <summary>An item icon from the bundle ([worldgen]'s, Fire approved 10-08), or null.</summary>
        internal static Sprite Icon(string name)
        {
            Load();
            return name != null && _sprites.TryGetValue(name, out var s) && s != null ? s : null;
        }

        internal static GameObject Get(string name)
        {
            Load();
            return name != null && _prefabs.TryGetValue(name, out var p) && p != null ? p : null;
        }

        private static void Load()
        {
            if (_tried) { if (_bundle != null && !PrefabsAlive()) Collect(); return; }
            _tried = true;
            try
            {
                foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
                    if (loaded != null && string.Equals(loaded.name, BundleResource, StringComparison.OrdinalIgnoreCase)) _bundle = loaded;
                if (_bundle == null)
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    string res = Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith("." + BundleResource, StringComparison.OrdinalIgnoreCase));
                    if (res == null) { FiresMountsPlugin.Log.LogDebug("[FiresMounts] [Gear] no firesmountsgear bundle in this build: stand-in saddles and whistle"); return; }
                    using (var s = asm.GetManifestResourceStream(res))
                    using (var ms = new System.IO.MemoryStream())
                    {
                        s.CopyTo(ms);
                        _bundle = AssetBundle.LoadFromMemory(ms.ToArray());   // a full copy, as for "firesmounts" (a lazy stream re-read crashes the dedi)
                    }
                }
                Collect();
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresMounts] [Gear] bundle load failed: {ex.Message}"); }
        }

        private static bool PrefabsAlive()
        {
            foreach (var p in _prefabs.Values) if (p == null) return false;
            foreach (var s in _sprites.Values) if (s == null) return false;
            foreach (var t in _textures.Values) if (t == null) return false;
            return true;
        }

        // Re-acquired after a relogin's UnloadUnusedAssets, like the horse prefabs.
        private static void Collect()
        {
            if (_bundle == null) return;
            _prefabs.Clear();
            foreach (var go in _bundle.LoadAllAssets<GameObject>())
            {
                if (go == null) continue;
                FixShaders(go);
                _prefabs[go.name] = go;
            }
            _sprites.Clear();
            foreach (var s in _bundle.LoadAllAssets<Sprite>()) if (s != null) _sprites[s.name] = s;
            _textures.Clear();
            foreach (var t in _bundle.LoadAllAssets<Texture2D>())
                if (t != null && t.name.StartsWith("FiresSaddle_Albedo_", StringComparison.Ordinal)) _textures[t.name] = t;
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Gear] firesmountsgear loaded: {string.Join(", ", _prefabs.Keys)}; icons {string.Join(", ", _sprites.Keys)}");
        }

        private static void FixShaders(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    var game = Shader.Find(m.shader.name);
                    if (game != null) m.shader = game;
                }
        }

        /// <summary>A gear prefab's copy under <paramref name="parent"/> (looks only: any collider it carries is dropped), or null.</summary>
        internal static GameObject Spawn(string name, Transform parent)
        {
            var prefab = Get(name);
            if (prefab == null || parent == null) return null;
            var go = UnityEngine.Object.Instantiate(prefab, parent, false);
            go.name = name;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);
            return go;
        }
    }

    /// <summary>
    /// The saddle on a creature mount's back (0.1.108): [worldgen]'s mesh on the fitted back bone at the saddle_fit.json pose (the seat
    /// point, the saddle's forward / up, 1 / lossy scale so the mesh stays metres), shown while the mount wears a saddle (vanilla
    /// Tameable's "HaveSaddle" ZDO flag), on every peer. Set on the prefab by FlyerFactory; nothing is synced of its own.
    /// </summary>
    public class FiresSaddleVisual : MonoBehaviour
    {
        public string GearName = "";
        public string BonePath = "";
        public Vector3 Seat, Forward = Vector3.forward, Up = Vector3.up;
        public float FitLossy;   // the bone's lossy scale at the fit: 1 / FitLossy keeps the saddle the fitted size relative to the mount

        private ZNetView _nview;
        private GameObject _saddle;
        private bool _tried;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            InvokeRepeating(nameof(Tick), 0.5f, 0.5f);
        }

        private void Tick()
        {
            if (_nview == null || !_nview.IsValid()) return;
            bool want = _nview.GetZDO().GetBool(ZDOVars.s_haveSaddleHash, false);
            if (want && _saddle == null && !_tried) Make();
            if (_saddle != null && _saddle.activeSelf != want) _saddle.SetActive(want);
        }

        private void Make()
        {
            _tried = true;
            var bone = string.IsNullOrEmpty(BonePath) ? null : transform.Find(BonePath);
            if (bone == null) return;
            _saddle = MountGear.Spawn(GearName, bone);
            if (_saddle == null) return;
            float lossy = Mathf.Max(bone.lossyScale.x, 0.0001f);
            _saddle.transform.localPosition = Seat;
            _saddle.transform.localRotation = Quaternion.LookRotation(Forward, Up);
            _saddle.transform.localScale = Vector3.one / (FitLossy > 0f ? FitLossy : lossy);
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Gear] {name}: saddle {GearName} on {bone.name} (lossy {lossy:0.#})");
        }
    }
}
