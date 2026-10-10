using System;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// Which egg hatches what (Tools\FIRESMOUNTS_LEVELS_AND_CLIMBER.md 3, Fire 10-08: "the same campfire nest"). The nest reads this
    /// table and nothing egg-specific, so Fire's planned Incubator piece only needs the FiresNestIncubator component.
    /// </summary>
    internal static class NestHatchTable
    {
        internal sealed class Entry
        {
            public string Egg, Creature, EggName, CreatureName;
        }

        internal static readonly Entry[] Entries =
        {
            new Entry { Egg = MountNames.ModerEggItem, Creature = MountNames.MiniModerPrefab, EggName = "Moder egg", CreatureName = "mini Moder" },
            new Entry { Egg = MountNames.FaderEggItem, Creature = MountNames.MiniFaderPrefab, EggName = "Fader egg", CreatureName = "Mini Fader" },
            new Entry { Egg = MountNames.DrakeEggItem, Creature = MountNames.DrakePrefab, EggName = "Drake egg", CreatureName = "drake" },
        };

        internal static Entry ForEgg(string eggPrefab)
        {
            foreach (var e in Entries) if (e.Egg == eggPrefab) return e;
            return null;
        }
    }

    /// <summary>
    /// The mount nest (FIRESMOUNTS_FLYING_PLAN 2): use an egg from NestHatchTable on it (like an ItemStand) and after HatchSeconds
    /// of world time (0 = one in-game day) it hatches that TAME, saddle-less creature beside it. State is two ZDO values: the ZNet
    /// time ticks the egg went in (0 = empty) and which egg (empty = a Moder egg, as nests before 0.1.105 hold). The placing client
    /// removes the egg from its own inventory and asks the ZDO owner by RPC; only the owner writes the ZDO, advances the timer and
    /// spawns, so MP never double-hatches.
    /// </summary>
    public class FiresNestIncubator : MonoBehaviour, Hoverable, Interactable
    {
        private const string HoverName = "Mount nest";
        // 0.1.109 variants (Tools\FIRES_VARIANTS_PLAN.md 5, frozen keys): an egg's "fires_variant" (its item m_customData, or an essence
        // used on the nest) rides in the nest's ZDO and is written into the hatchling's ZDO "fires_variant".
        internal const string VariantKey = "fires_variant";
        private const string ZdoNestVariant = "firesmounts_nest_variant";
        private ZNetView _nview;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            if (_nview == null || !_nview.IsValid()) return;   // ghost placement preview
            _nview.Register<string, string>("FiresNest_PlaceEgg3", RPC_PlaceEgg);
            _nview.Register<string>("FiresNest_SetVariant", RPC_SetVariant);
            _nview.Register("FiresNest_Hatch", RPC_HatchNow);
            InvokeRepeating(nameof(Tick), 2f, 2f);
            InvokeRepeating(nameof(UpdateEggVisual), 0.5f, 0.5f);
        }

        // 0.1.115 ([lead] 10-08: the placed egg was invisible): every peer shows the egg the nest's ZDO holds, the egg item's own
        // meshes copied (no ZNetView, no collider, no physics) and scaled to sit in the ring, with a soft warm light. The ZDO is
        // already synced, so nothing new goes over the network; the hatch clears the ZDO and the next check removes the visual.
        private const float EggVisualSize = 0.45f, EggVisualHeight = 0.2f;
        private GameObject _eggVisual;
        private string _eggVisualItem = "";

        private void UpdateEggVisual()
        {
            string item = "";
            if (_nview != null && _nview.IsValid() && EggTicks() != 0L)
            {
                item = _nview.GetZDO().GetString(MountNames.ZdoNestEggItem, "");
                if (string.IsNullOrEmpty(item)) item = MountNames.ModerEggItem;
            }
            if (item == _eggVisualItem && (_eggVisual != null || item.Length == 0)) return;
            if (_eggVisual != null) Destroy(_eggVisual);
            _eggVisual = null;
            _eggVisualItem = item;
            if (item.Length == 0) return;
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item) : null;
            if (prefab == null) return;
            _eggVisual = BuildEggVisual(prefab);
            if (_eggVisual != null)
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] nest egg visual shown at {transform.position}: {item}");
        }

        private GameObject BuildEggVisual(GameObject prefab)
        {
            var root = new GameObject("FiresNestEggVisual");
            root.transform.SetParent(transform, false);
            var holder = new GameObject("model");
            holder.transform.SetParent(root.transform, false);
            Bounds bounds = default;
            bool any = false;
            Matrix4x4 toPrefab = prefab.transform.worldToLocalMatrix;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null || !mr.enabled) continue;
                var part = new GameObject(mf.name);
                part.transform.SetParent(holder.transform, false);
                Matrix4x4 m = toPrefab * mf.transform.localToWorldMatrix;
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = part.AddComponent<MeshRenderer>();
                r.sharedMaterials = mr.sharedMaterials;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Bounds b = mf.sharedMesh.bounds;
                Vector3 c = m.MultiplyPoint3x4(b.center);
                Vector3 e = Vector3.Scale(b.extents, new Vector3(Mathf.Abs(m.lossyScale.x), Mathf.Abs(m.lossyScale.y), Mathf.Abs(m.lossyScale.z)));
                var pb = new Bounds(c, e * 2f);
                if (any) bounds.Encapsulate(pb); else { bounds = pb; any = true; }
            }
            if (!any) { Destroy(root); return null; }
            float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = size > 0.001f ? EggVisualSize / size : 1f;
            holder.transform.localScale = Vector3.one * scale;
            // Bottom of the egg on the ring's floor, centred.
            holder.transform.localPosition = new Vector3(-bounds.center.x * scale, EggVisualHeight - bounds.min.y * scale, -bounds.center.z * scale);
            var glow = new GameObject("glow");
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0f, EggVisualHeight + EggVisualSize * 0.5f, 0f);
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.62f, 0.3f);
            light.range = 1.6f;
            light.intensity = 0.7f;
            light.shadows = LightShadows.None;
            return root;
        }

        private void OnDestroy()
        {
            if (_eggVisual != null) Destroy(_eggVisual);
        }

        internal static float HatchDuration()
        {
            float s = MountConfig.HatchSeconds != null ? MountConfig.HatchSeconds.Value : 0f;
            if (s > 0f) return s;
            return EnvMan.instance != null ? EnvMan.instance.m_dayLengthSec : 1200f;
        }

        private long EggTicks() => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetLong(MountNames.ZdoNestEgg, 0L) : 0L;

        private NestHatchTable.Entry EggEntry()
        {
            string egg = _nview != null && _nview.IsValid() ? _nview.GetZDO().GetString(MountNames.ZdoNestEggItem, "") : "";
            return NestHatchTable.ForEgg(string.IsNullOrEmpty(egg) ? MountNames.ModerEggItem : egg);
        }

        private float SecondsLeft()
        {
            long placed = EggTicks();
            if (placed == 0L || ZNet.instance == null) return -1f;
            double elapsed = (ZNet.instance.GetTime().Ticks - placed) / (double)TimeSpan.TicksPerSecond;
            return Mathf.Max(0f, HatchDuration() - (float)elapsed);
        }

        public string GetHoverText()
        {
            if (_nview == null || !_nview.IsValid()) return HoverName;
            float left = SecondsLeft();
            if (left < 0f)
                return HoverName + ": empty\n[<color=yellow><b>1-8</b></color>] Place a Moder, Fader or drake egg";
            var entry = EggEntry();
            string variant = _nview.GetZDO().GetString(ZdoNestVariant, "");
            return $"{HoverName}\nIncubating a {(entry != null ? entry.EggName : "egg")}{(variant.Length > 0 ? " (" + variant + ")" : "")}: {Mathf.CeilToInt(left / 60f)} min left";
        }

        public string GetHoverName() => HoverName;

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt) => false;

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            var entry = item != null && item.m_dropPrefab != null ? NestHatchTable.ForEgg(item.m_dropPrefab.name) : null;
            if (entry == null) return false;
            if (_nview == null || !_nview.IsValid()) return false;
            if (EggTicks() != 0L) { user.Message(MessageHud.MessageType.Center, "The nest already holds an egg"); return true; }
            string variant = item.m_customData != null && item.m_customData.TryGetValue(VariantKey, out string v) ? v : "";
            if (!user.GetInventory().RemoveItem(item, 1)) return true;
            _nview.InvokeRPC("FiresNest_PlaceEgg3", entry.Egg, variant);
            user.Message(MessageHud.MessageType.Center, $"The {entry.EggName} settles into the warm nest");
            return true;
        }

        // Console hook (firesmounts_hatch): finish incubation now, on the owner.
        internal void RequestHatchNow()
        {
            if (_nview != null && _nview.IsValid()) _nview.InvokeRPC("FiresNest_Hatch");
        }

        /// <summary>An egg sits in the nest (an essence used on the nest colours it).</summary>
        internal bool HoldsEgg => EggTicks() != 0L;

        /// <summary>Sets the incubating egg's variant on the nest's owner ("" = the default look).</summary>
        internal void SetEggVariant(string id)
        {
            if (_nview != null && _nview.IsValid()) _nview.InvokeRPC("FiresNest_SetVariant", id ?? "");
        }

        private void RPC_SetVariant(long sender, string id)
        {
            if (!_nview.IsOwner() || EggTicks() == 0L) return;
            _nview.GetZDO().Set(ZdoNestVariant, id ?? "");
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] nest egg variant set to '{id}' at {transform.position}");
        }

        private void RPC_PlaceEgg(long sender, string egg, string variant)
        {
            if (!_nview.IsOwner() || EggTicks() != 0L || ZNet.instance == null || NestHatchTable.ForEgg(egg) == null) return;
            _nview.GetZDO().Set(ZdoNestVariant, variant ?? "");
            _nview.GetZDO().Set(MountNames.ZdoNestEggItem, egg);
            _nview.GetZDO().Set(MountNames.ZdoNestEgg, ZNet.instance.GetTime().Ticks);
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] nest egg placed at {transform.position}: {egg} (hatch in {HatchDuration():0}s)");
        }

        private void RPC_HatchNow(long sender)
        {
            if (!_nview.IsOwner() || EggTicks() == 0L) return;
            Hatch();
        }

        private void Tick()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            if (EggTicks() == 0L) return;
            if (SecondsLeft() <= 0f) Hatch();
        }

        private void Hatch()
        {
            var entry = EggEntry();
            string variant = _nview.GetZDO().GetString(ZdoNestVariant, "");
            // Clear first: a failed spawn never loops.
            _nview.GetZDO().Set(MountNames.ZdoNestEgg, 0L);
            _nview.GetZDO().Set(MountNames.ZdoNestEggItem, "");
            _nview.GetZDO().Set(ZdoNestVariant, "");
            string creature = entry != null ? entry.Creature : MountNames.MiniModerPrefab;
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(creature) : null;
            if (prefab == null) { FiresLogger.LogWarning($"hatch: {creature} not registered."); return; }
            Vector3 pos = transform.position + transform.right * 2.5f + Vector3.up * 0.5f;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(pos, out float gh)) pos.y = Mathf.Max(pos.y, gh + 0.5f);
            var go = Instantiate(prefab, pos, transform.rotation);
            var ch = go.GetComponent<Character>();
            if (ch != null) ch.SetTamed(true);
            var hatchView = go.GetComponent<ZNetView>();
            if (variant.Length > 0 && hatchView != null && hatchView.IsValid()) hatchView.GetZDO().Set(VariantKey, variant);
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] nest hatched {creature} at {pos}{(variant.Length > 0 ? ", variant " + variant : "")}");
        }
    }
}
