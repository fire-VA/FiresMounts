using FiresCore.Variants;
using HarmonyLib;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// The mount side of the shared variant system (Tools\FIRES_VARIANTS_PLAN.md, Fire approved 10-08: generic essences, the Cauldron, Clarity
    /// resets, all seven at once). Core (FiresCore.Variants) holds the table, VariantApply and the attack-FX recolour; FiresMounts keeps:
    /// - the mount's ZDO "fires_variant" (only the owner writes; others ask it by RPC), applied on every peer within a second of a change;
    /// - the body filter: every skinned body renderer, never the saddle (FiresSaddleVisual's mesh, the horse's FiresHorseSaddle) or particles;
    /// - the horse: a variant naming a coat swaps it first (FiresMountCoat.UseCoat), then the tint goes on top; Clear comes before both;
    /// - an essence used on a tamed mount (E with it in hand, like the saddle) or on an egg in a mount nest; it is consumed, Clarity resets.
    /// </summary>
    public class MountVariant : MonoBehaviour
    {
        private const string RpcSet = "FiresMounts_SetVariant";
        private ZNetView _nview;
        private string _applied;
        private float _next;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            if (_nview != null && _nview.IsValid()) _nview.Register<string>(RpcSet, RPC_Set);
        }

        private void Update()
        {
            if (_nview == null || !_nview.IsValid() || Time.time < _next) return;
            _next = Time.time + 1f;
            string id = _nview.GetZDO().GetString(VariantTable.KeyVariantHash, "");
            if (id != _applied) ApplyNow(id);
        }

        private void ApplyNow(string id)
        {
            VariantApply.Clear(gameObject);
            var coat = GetComponent<FiresMountCoat>();
            if (coat != null && VariantTable.TryGet(id, out VariantDef def) && !string.IsNullOrEmpty(def.HorseCoat)) coat.UseCoat(def.HorseCoat);
            bool drawn = string.IsNullOrEmpty(id) || id == VariantTable.Clear || VariantApply.Apply(gameObject, id, IsBody);
            _applied = id;
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Variant] {name}: {(string.IsNullOrEmpty(id) ? "default look" : "variant " + id)}{(drawn ? "" : " (unknown id, or nothing to draw)")}");
        }

        private static bool IsBody(Renderer r) =>
            r is SkinnedMeshRenderer && r.gameObject.name.IndexOf("saddle", System.StringComparison.OrdinalIgnoreCase) < 0;

        /// <summary>Sets this mount's variant from any peer ("" or Clarity = the default look).</summary>
        internal void Set(string id)
        {
            if (_nview == null || !_nview.IsValid()) return;
            if (_nview.IsOwner()) RPC_Set(0L, id);
            else _nview.InvokeRPC(RpcSet, id ?? "");
        }

        private void RPC_Set(long sender, string id)
        {
            if (!_nview.IsOwner()) return;
            _nview.GetZDO().Set(VariantTable.KeyVariantHash, id == VariantTable.Clear ? "" : id ?? "");
        }

        private static string Shown(VariantDef def) => def.IsClear ? "its own colours again" : (def.DisplayName ?? def.Id);

        // E with an essence in hand on a tamed mount of ours: it takes the variant (vanilla Tameable.UseItem is the saddle's path too; only
        // essences stop here, a saddle goes on to vanilla).
        [HarmonyPatch(typeof(Tameable), nameof(Tameable.UseItem))]
        internal static class EssenceOnMount
        {
            [HarmonyPrefix]
            private static bool Prefix(Tameable __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
            {
                var mount = __instance != null ? __instance.GetComponent<MountVariant>() : null;
                if (mount == null || user == null || !VariantEssences.IsEssence(item, out VariantDef def)) return true;
                var character = __instance.GetComponent<Character>();
                if (character == null || !character.IsTamed()) return true;
                if (!user.GetInventory().RemoveItem(item, 1)) { __result = true; return false; }
                mount.Set(def.IsClear ? "" : def.Id);
                user.Message(MessageHud.MessageType.Center, $"{character.GetHoverName()}: {Shown(def)}");
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Variant] {mount.name}: {item.m_dropPrefab?.name} used, variant {(def.IsClear ? "cleared" : def.Id)}");
                __result = true;
                return false;
            }
        }

        // An essence used on a mount nest holding an egg: the hatchling comes out in that variant.
        [HarmonyPatch(typeof(FiresNestIncubator), nameof(FiresNestIncubator.UseItem))]
        internal static class EssenceOnEgg
        {
            [HarmonyPrefix]
            private static bool Prefix(FiresNestIncubator __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
            {
                if (__instance == null || user == null || !VariantEssences.IsEssence(item, out VariantDef def)) return true;
                if (!__instance.HoldsEgg) { user.Message(MessageHud.MessageType.Center, "The nest holds no egg"); __result = true; return false; }
                if (!user.GetInventory().RemoveItem(item, 1)) { __result = true; return false; }
                __instance.SetEggVariant(def.IsClear ? "" : def.Id);
                user.Message(MessageHud.MessageType.Center, $"The egg: {Shown(def)}");
                __result = true;
                return false;
            }
        }
    }
}
