using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// Saddle looks (Fire 10-08: "saddle colors and appearances is something that the player should be able to change"; he picked dye
    /// kits). [saddles]' looks are recolours of the saddle albedo on the same UVs (the horse's saddle too), baked into firesmountsgear as
    /// "FiresSaddle_Albedo_&lt;Look&gt;"; Classic is the saddle's own texture. Each mount takes three looks. A "Saddle dye: &lt;Look&gt;" (workbench:
    /// leather scraps + a themed material) used on a saddled tamed mount (E with it in hand, like an essence) sets the mount's ZDO
    /// "fires_saddle_look"; every peer swaps the saddle materials' albedo within half a second. Unset = Fire's default for that mount.
    /// </summary>
    internal static class SaddleLooks
    {
        internal const string ZdoLook = "fires_saddle_look";
        internal const string Classic = "Classic";
        internal const string DyePrefix = "FiresSaddleDye_";

        internal sealed class Dye
        {
            public string Look, Theme;
            public int ThemeAmount;
            public string Description;
        }

        internal static readonly Dye[] Dyes =
        {
            new Dye { Look = Classic, Description = "The saddle's own tan leather back again." },
            new Dye { Look = "Frost", Theme = "FreezeGland", ThemeAmount = 1, Description = "Cold blue and white, for a mini Moder's saddle." },
            new Dye { Look = "Royal", Theme = "Coins", ThemeAmount = 10, Description = "Deep red and gold, for a mini Moder's saddle." },
            new Dye { Look = "Glacier", Theme = "Crystal", ThemeAmount = 1, Description = "Pale ice tones, for a drake's saddle." },
            new Dye { Look = "WolfHide", Theme = "WolfPelt", ThemeAmount = 1, Description = "Grey wolf hide, for a drake's saddle." },
            new Dye { Look = "Ashlands", Theme = "CharredBone", ThemeAmount = 1, Description = "Scorched reds and soot, for a Mini Fader's saddle." },
            new Dye { Look = "BlackHide", Theme = "Coal", ThemeAmount = 5, Description = "Black hide, for a Mini Fader's saddle." },
            new Dye { Look = "Meadow", Theme = "Dandelion", ThemeAmount = 3, Description = "Green meadow tones, for a horse's saddle." },
            new Dye { Look = "Palomino", Theme = "Honey", ThemeAmount = 2, Description = "Golden palomino tones, for a horse's saddle." },
        };

        // Which looks each mount takes ([saddles]' sets) and Fire's default (10-08: Moder C Royal, Drake A, Fader B, Horse A).
        private static readonly Dictionary<string, string[]> LooksByMount = new Dictionary<string, string[]>
        {
            { MountNames.MiniModerPrefab, new[] { Classic, "Frost", "Royal" } },
            { MountNames.DrakePrefab, new[] { Classic, "Glacier", "WolfHide" } },
            { MountNames.MiniFaderPrefab, new[] { Classic, "Ashlands", "BlackHide" } },
            { MountNames.HorsePrefab, new[] { Classic, "Meadow", "Palomino" } },
        };
        private static readonly Dictionary<string, string> DefaultByMount = new Dictionary<string, string>
        {
            { MountNames.MiniModerPrefab, "Royal" },
            { MountNames.DrakePrefab, Classic },
            { MountNames.MiniFaderPrefab, "Ashlands" },
            { MountNames.HorsePrefab, Classic },
        };

        private static string MountKey(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            foreach (string key in LooksByMount.Keys) if (prefabName.StartsWith(key)) return key;
            return null;
        }

        internal static string Default(string prefabName)
        {
            string key = MountKey(prefabName);
            return key != null && DefaultByMount.TryGetValue(key, out string d) ? d : Classic;
        }

        internal static bool Fits(string prefabName, string look)
        {
            string key = MountKey(prefabName);
            return key != null && Array.IndexOf(LooksByMount[key], look) >= 0;
        }

        internal static string LooksFor(string prefabName)
        {
            string key = MountKey(prefabName);
            return key != null ? string.Join(", ", LooksByMount[key]) : Classic;
        }

        internal static bool IsDye(ItemDrop.ItemData item, out string look)
        {
            look = null;
            string prefab = item?.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            if (prefab == null || !prefab.StartsWith(DyePrefix)) return false;
            look = prefab.Substring(DyePrefix.Length);
            return true;
        }

        internal static string Shown(string look) => look == "WolfHide" ? "Wolf hide" : look == "BlackHide" ? "Black hide" : look;
    }

    /// <summary>Every peer: the saddle materials of this mount wear its ZDO look (or the mount's default).</summary>
    public class MountSaddleLook : MonoBehaviour
    {
        private const string RpcSet = "FiresMounts_SetSaddleLook";
        private ZNetView _nview;
        private string _applied;
        private int _appliedRenderers = -1;
        private readonly Dictionary<Material, Texture> _classic = new Dictionary<Material, Texture>();

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            if (_nview == null || !_nview.IsValid()) return;
            _nview.Register<string>(RpcSet, RPC_Set);
            InvokeRepeating(nameof(Tick), 0.5f, 0.5f);
        }

        private string Prefab => Utils.GetPrefabName(gameObject);

        internal string Current()
        {
            string look = _nview != null && _nview.IsValid() ? _nview.GetZDO().GetString(SaddleLooks.ZdoLook, "") : "";
            return string.IsNullOrEmpty(look) ? SaddleLooks.Default(Prefab) : look;
        }

        private void Tick()
        {
            if (_nview == null || !_nview.IsValid()) return;
            string look = Current();
            List<Renderer> saddle = SaddleRenderers();
            if (look == _applied && saddle.Count == _appliedRenderers) return;
            Apply(look, saddle);
        }

        // The saddle's renderers: the horse's FiresHorseSaddle mesh, or FiresSaddleVisual's spawned saddle (and its girth / breast
        // straps): any renderer with "saddle" in its own or a parent's name below the mount.
        private List<Renderer> SaddleRenderers()
        {
            var list = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                for (Transform t = r.transform; t != null && t != transform; t = t.parent)
                    if (t.name.IndexOf("saddle", StringComparison.OrdinalIgnoreCase) >= 0) { list.Add(r); break; }
            }
            return list;
        }

        private void Apply(string look, List<Renderer> saddle)
        {
            Texture tex = look == SaddleLooks.Classic ? null : MountGear.Texture("FiresSaddle_Albedo_" + look);
            int swapped = 0;
            foreach (var r in saddle)
                foreach (var m in r.materials)   // per-renderer instances: never the shared bundle material
                {
                    if (m == null || !m.HasProperty("_MainTex")) continue;
                    if (!_classic.TryGetValue(m, out Texture original))
                    {
                        original = m.mainTexture;
                        if (original == null || original.name.IndexOf("saddle", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        _classic[m] = original;
                    }
                    m.mainTexture = tex != null ? tex : original;
                    swapped++;
                }
            _applied = look;
            _appliedRenderers = saddle.Count;
            if (saddle.Count > 0)
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [SaddleLook] {name}: {look}{(tex == null && look != SaddleLooks.Classic ? " (texture missing: classic)" : "")}, {swapped} material(s) on {saddle.Count} renderer(s)");
        }

        internal void Set(string look)
        {
            if (_nview == null || !_nview.IsValid()) return;
            if (_nview.IsOwner()) RPC_Set(0L, look);
            else _nview.InvokeRPC(RpcSet, look ?? "");
        }

        private void RPC_Set(long sender, string look)
        {
            if (!_nview.IsOwner()) return;
            _nview.GetZDO().Set(SaddleLooks.ZdoLook, look ?? "");
        }

        // E with a saddle dye in hand on a saddled tamed mount of ours (vanilla Tameable.UseItem is the saddle's own path).
        [HarmonyPatch(typeof(Tameable), nameof(Tameable.UseItem))]
        internal static class DyeOnMount
        {
            [HarmonyPrefix]
            private static bool Prefix(Tameable __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
            {
                if (__instance == null || user == null || !SaddleLooks.IsDye(item, out string look)) return true;
                var mount = __instance.GetComponent<MountSaddleLook>();
                var character = __instance.GetComponent<Character>();
                if (mount == null || character == null || !character.IsTamed()) return true;
                __result = true;
                string prefab = Utils.GetPrefabName(__instance.gameObject);
                if (!__instance.HaveSaddle()) { user.Message(MessageHud.MessageType.Center, $"{character.GetHoverName()} wears no saddle to dye"); return false; }
                if (!SaddleLooks.Fits(prefab, look))
                {
                    user.Message(MessageHud.MessageType.Center, $"That dye doesn't suit this saddle ({SaddleLooks.LooksFor(prefab)})");
                    return false;
                }
                if (mount.Current() == look) { user.Message(MessageHud.MessageType.Center, $"The saddle is already {SaddleLooks.Shown(look)}"); return false; }
                if (!user.GetInventory().RemoveItem(item, 1)) return false;
                mount.Set(look);
                user.Message(MessageHud.MessageType.Center, $"{character.GetHoverName()}'s saddle: {SaddleLooks.Shown(look)}");
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [SaddleLook] {mount.name}: {item.m_dropPrefab?.name} used, look {look}");
                return false;
            }
        }
    }

    internal static partial class FlyerFactory
    {
        private static readonly List<GameObject> _dyes = new List<GameObject>();
        private static readonly List<Recipe> _dyeRecipes = new List<Recipe>();

        // The dye items: stand-in clones of vanilla's mead base bottle, [worldgen]'s icons from the gear bundle.
        private static void BuildDyes(ObjectDB db)
        {
            if (_dyes.Count > 0) return;
            var src = FindVanilla("MeadBaseTasty", db) ?? FindVanilla("MeadBaseMinorHealth", db);
            if (src == null) return;
            foreach (var dye in SaddleLooks.Dyes)
            {
                string id = SaddleLooks.DyePrefix + dye.Look;
                var go = CloneUnderHolder(src, id);
                var shared = go.GetComponent<ItemDrop>().m_itemData.m_shared;
                shared.m_name = "Saddle dye: " + SaddleLooks.Shown(dye.Look);
                shared.m_description = dye.Description + " Use it on a saddled mount (E with the dye in hand).";
                shared.m_itemType = ItemDrop.ItemData.ItemType.Material;
                shared.m_maxStackSize = 10;
                shared.m_value = 0;
                shared.m_teleportable = true;
                shared.m_consumeStatusEffect = null;
                UseIcon(shared, id);
                _dyes.Add(go);
            }
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] saddle dyes registered: {_dyes.Count}");
        }

        private static void RegisterDyes(ObjectDB db)
        {
            bool changed = false;
            foreach (var go in _dyes) if (go != null && !db.m_items.Contains(go)) { db.m_items.Add(go); changed = true; }
            if (changed) s_updateRegisters?.Invoke(db, null);
            if (db.m_recipes == null || _dyes.Count == 0) return;
            if (_dyeRecipes.Count == 0)
            {
                var station = FindVanilla("piece_workbench", db);
                if (station == null) return;   // retried on the next ObjectDB pass
                for (int i = 0; i < SaddleLooks.Dyes.Length && i < _dyes.Count; i++)
                {
                    var dye = SaddleLooks.Dyes[i];
                    var reqs = new List<Piece.Requirement>();
                    AddReq(reqs, db, "LeatherScraps", dye.Theme == null ? 3 : 2);
                    if (dye.Theme != null) AddReq(reqs, db, dye.Theme, dye.ThemeAmount);
                    if (reqs.Count < (dye.Theme == null ? 1 : 2)) continue;
                    var recipe = ScriptableObject.CreateInstance<Recipe>();
                    recipe.name = "Recipe_" + _dyes[i].name;
                    recipe.m_item = _dyes[i].GetComponent<ItemDrop>();
                    recipe.m_amount = 1;
                    recipe.m_craftingStation = station.GetComponent<CraftingStation>();
                    recipe.m_minStationLevel = 1;
                    recipe.m_resources = reqs.ToArray();
                    UnityEngine.Object.DontDestroyOnLoad(recipe);
                    _dyeRecipes.Add(recipe);
                }
            }
            foreach (var r in _dyeRecipes) if (!db.m_recipes.Contains(r)) db.m_recipes.Add(r);
        }

        private static IEnumerable<GameObject> DyePrefabs() => _dyes;
    }
}
