using System;
using System.Collections;
using FiresCore.Logging;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// Per-instance appearance LOADOUT — coat + mane + tail. Each spawned horse rolls its loadout the first time it
    /// exists (rolled by the ZDO owner) and writes each choice to the horse's ZDO, so the look is permanent across
    /// relogs, identical for every client, and survives ownership handoff. Coat = material swap (FiresHorseCoat_*
    /// baked into the bundle); mane/tail = enable ONE style's renderer(s), disable the rest (there is no LODGroup on
    /// the baked prefab, so we keep only LOD0 to avoid overlapping manes). Config Coat/Mane/Tail pin a specific one
    /// (or leave Random). Added by MountFactory.Compose so every spawn carries it. No-ops gracefully if the bundle
    /// predates the coat materials (keeps the baked default) or a style's meshes aren't present.
    /// </summary>
    internal sealed class FiresMountCoat : MonoBehaviour
    {
        // ZDO keys for the rolled loadout (stable string hashes; Valheim hashes them internally).
        private const string CoatKey = "fires_coat";
        private const string ManeKey = "fires_mane";
        private const string TailKey = "fires_tail";

        // Coat keys — MUST match the FiresHorseCoat_<name> materials baked by FiresMountBuilder.HapCoats.
        internal static readonly string[] Coats = { "Brown", "Gray", "Gray2", "Palomino", "White" };
        // Mane/tail styles: (config name, renderer-name substring). The substrings are disjoint and match the HAP
        // mesh SMRs (cookie recipe): "Horse Mane V/Long/01_Short/_Old ...", "Horse Tail_New/_Old ...". Bones (no
        // renderer) are skipped automatically. We enable the chosen style's LOD0 and disable every other mane/tail.
        private static readonly (string name, string sub)[] Manes =
            { ("V", "mane v "), ("Long", "mane long"), ("Short", "mane01"), ("Old", "mane_old") };
        private static readonly (string name, string sub)[] Tails =
            { ("New", "tail_new"), ("Old", "tail_old") };

        private ZNetView _nview;
        private int _coatApplied = -1;
        private bool _loadoutApplied;
        private int _toggleLive;   // activeInHierarchy count of the last ToggleStyle's chosen renderers (fix confirmation)

        private void Start()
        {
            _nview = GetComponent<ZNetView>();
            // No early-out on an invalid nview: that silently killed the WHOLE loadout (no mane, no tail, saddle mesh
            // stuck at its baked default) on any instance whose ZNetView wasn't valid yet at Start. The coroutines
            // self-heal now — they wait for validity instead of never running.
            StartCoroutine(Resolve());
            StartCoroutine(SaddleVisibilityLoop());   // spawn bare-backed; saddle mesh appears only once applied
        }

        // Owner rolls + writes each loadout piece; non-owners wait for it to replicate. NEVER gives up (the old 6s
        // window silently expired on slow owner rolls/late nviews = bald+tailless horses): retries every 0.5s until
        // the loadout is applied AND the coat resolves. Logs the outcome once per horse so a broken link is visible
        // in the log instead of a mystery; a chosen style that matches 0 renderers falls back to one that matches
        // (a horse can never lose its tail to a bad style table).
        private IEnumerator Resolve()
        {
            var wait = new WaitForSeconds(0.5f);
            float waited = 0f; bool waitLogged = false;
            while (true)
            {
                if (_nview == null || !_nview.IsValid())
                {
                    _nview = GetComponent<ZNetView>();
                    yield return wait; continue;
                }
                var zdo = _nview.GetZDO();
                int coat = RollOrRead(zdo, CoatKey, Coats.Length, PickCoat);
                int mane = RollOrRead(zdo, ManeKey, Manes.Length + 1, PickMane);   // +1: index==Manes.Length is the rare bald roll
                int tail = RollOrRead(zdo, TailKey, Tails.Length, PickTail);        // tails NEVER roll none — every horse has a tail

                if (coat >= 0 && mane >= 0 && tail >= 0)
                {
                    if (!_loadoutApplied)
                    {
                        int maneOn = mane < Manes.Length ? ToggleStyle("mane", Manes[mane].sub) : ToggleStyle("mane", null);
                        int maneLive = _toggleLive;
                        if (maneOn == 0 && mane < Manes.Length && FallbackStyle("mane", Manes, out int mFb) > 0 && _nview.IsOwner())
                            zdo.Set(ManeKey, mFb);
                        int tailOn = ToggleStyle("tail", Tails[tail].sub);
                        int tailLive = _toggleLive;
                        if (tailOn == 0 && FallbackStyle("tail", Tails, out int tFb) > 0)
                        { tailOn = 1; if (_nview.IsOwner()) zdo.Set(TailKey, tFb); }
                        _loadoutApplied = tailOn > 0;   // don't latch while the tail is missing — keep retrying
                        FiresMountsPlugin.Log.LogInfo($"[FiresMounts] loadout '{name}' owner={_nview.IsOwner()} coat={coat} mane={mane}{(mane == Manes.Length ? "(bald)" : "")} tail={tail} maneOn={maneOn}(live {maneLive}) tailOn={tailOn}(live {tailLive})");
                    }
                    if (_loadoutApplied && ApplyCoat(coat)) yield break;
                }
                else if (!waitLogged && waited >= 8f)
                {
                    waitLogged = true;
                    FiresMountsPlugin.Log.LogWarning($"[FiresMounts] loadout '{name}' STILL WAITING after {waited:0}s: owner={_nview.IsOwner()} coat={coat} mane={mane} tail={tail} (owner hasn't rolled/replicated)");
                }
                waited += 0.5f;
                yield return wait;
            }
        }

        // Try every style in the table until one actually matches a renderer; logs the candidates if nothing does.
        private int FallbackStyle(string group, (string name, string sub)[] table, out int idx)
        {
            for (int i = 0; i < table.Length; i++)
            {
                int on = ToggleStyle(group, table[i].sub);
                if (on > 0) { idx = i; FiresMountsPlugin.Log.LogWarning($"[FiresMounts] {group} style fell back to '{table[i].name}' on '{name}'."); return on; }
            }
            idx = -1;
            var sb = new System.Text.StringBuilder();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.name.ToLowerInvariant().Contains(group)) sb.Append(r.gameObject.name).Append("; ");
            FiresMountsPlugin.Log.LogWarning($"[FiresMounts] {group}: NO style matched any renderer on '{name}'. candidates: {sb}");
            return 0;
        }

        // Read the ZDO value; if unset and we own it, roll + persist. Returns -1 if unset and we're not the owner
        // (so the caller waits for the owner's roll to replicate).
        private int RollOrRead(ZDO zdo, string key, int count, Func<int> roll)
        {
            int idx = zdo.GetInt(key, -1);
            if (idx < 0 && _nview.IsOwner()) { idx = Mathf.Clamp(roll(), 0, count - 1); zdo.Set(key, idx); }
            return idx;
        }

        // Config pins ("Random"/empty => random). Names are matched case-insensitively against the style tables.
        private int PickCoat() => PickPinned(MountConfig.Coat, Coats, i => Coats[i]);
        private int PickTail() => PickPinned(MountConfig.Tail, Tails, i => Tails[i].name);

        // Manes: pinnable to a style or "None"; Random rolls a style ~96% of the time and bald only very rarely.
        private const float BaldManeChance = 0.04f;
        private int PickMane()
        {
            string v = MountConfig.Mane != null ? MountConfig.Mane.Value : null;
            if (!string.IsNullOrEmpty(v))
            {
                string t = v.Trim();
                if (t.Equals("None", StringComparison.OrdinalIgnoreCase)) return Manes.Length;
                if (!t.Equals("Random", StringComparison.OrdinalIgnoreCase))
                    for (int i = 0; i < Manes.Length; i++)
                        if (Manes[i].name.Equals(t, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return UnityEngine.Random.value < BaldManeChance ? Manes.Length : UnityEngine.Random.Range(0, Manes.Length);
        }

        private static int PickPinned<T>(BepInEx.Configuration.ConfigEntry<string> cfg, T[] table, Func<int, string> nameOf)
        {
            string v = cfg != null ? cfg.Value : null;
            if (!string.IsNullOrEmpty(v) && !v.Trim().Equals("Random", StringComparison.OrdinalIgnoreCase))
                for (int i = 0; i < table.Length; i++)
                    if (nameOf(i).Equals(v.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
            return UnityEngine.Random.Range(0, table.Length);
        }

        // Swap the chosen coat material onto the body mesh slot(s). Shared bundle asset (same coat = same asset), so
        // nothing per-instance leaks. Returns true once at least one slot is swapped (or the material is missing, so
        // the retry loop can give up gracefully on an older bundle).
        private bool ApplyCoat(int idx)
        {
            idx = Mathf.Clamp(idx, 0, Coats.Length - 1);
            if (_coatApplied == idx) return true;
            Material coat = FiresMountsPlugin.LoadCoat(Coats[idx]);
            if (coat == null) return _loadoutApplied;   // bundle predates coats — don't spin forever, keep baked default

            int slots = 0;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                bool bodyNode = r.gameObject.name.ToLowerInvariant().Contains("body");
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string mn = mats[i].name.ToLowerInvariant();
                    if (!(bodyNode || mn.Contains("bodycoat") || mn.Contains("fireshorsecoat"))) continue;
                    if (mats[i] == coat) { changed = true; continue; }
                    mats[i] = coat; changed = true; slots++;
                }
                if (changed) r.sharedMaterials = mats;
            }
            if (slots == 0 && _coatApplied < 0) return false;   // body slot not matched yet — let the retry loop try again
            _coatApplied = idx;
            FiresLogger.LogVerbose($"coat '{Coats[idx]}' -> {slots} slot(s) on '{name}'.");
            return true;
        }

        // Enable the chosen style's LOD0 renderer(s) among all mane/tail renderers; disable everything else in that
        // group. No LODGroup on the baked prefab → keeping only LOD0 avoids overlapping LODs AND cuts the overdraw of
        // every mane variant rendering at once.
        // Returns how many renderers were enabled for the chosen style (0 = the style matched nothing — caller falls back).
        private int ToggleStyle(string group, string chosenSub)
        {
            int on = 0, off = 0, live = 0;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                string n = r.gameObject.name.ToLowerInvariant();
                if (!n.Contains(group)) continue;
                // null chosenSub = the rare bald-mane roll: every renderer in the group goes off
                bool chosen = chosenSub != null && n.Contains(chosenSub) && (n.Contains("lod0") || !n.Contains("lod"));
                r.enabled = chosen;
                if (chosen)
                {
                    // The HAP model ships EVERY mane/tail variant except its defaults (Mane V / Tail New) under an
                    // INACTIVE parent node (probe: activeInHier=False on Old/Short/Long/Tail_Old). Enabling the leaf
                    // renderer alone leaves activeInHierarchy=false, so it never draws — that was the "no mane/tail"
                    // bug. Walk the whole chain from the renderer up to the horse root active so the chosen style is
                    // genuinely in the hierarchy. Non-chosen renderers stay enabled=false, so a shared parent going
                    // active can't reveal a wrong variant.
                    for (Transform t = r.transform; t != null; t = t.parent)
                    {
                        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                        if (t == transform) break;
                    }
                    if (r.gameObject.activeInHierarchy) live++;
                    on++;
                }
                else off++;
            }
            _toggleLive = live;
            FiresLogger.LogVerbose($"{group} '{chosenSub ?? "NONE"}' -> {on} on / {off} off / {live} live on '{name}'.");
            return on;
        }

        // Bare-backed until saddled: the saddle mesh lives INSIDE the horse (mirrors MM's .../saddle/Saddle01_LOD0),
        // shown only when the haveSaddle flag is set — i.e. after the player puts the SaddleLox on. Polls a couple
        // times a second so it appears right after you equip it (and hides if removed). Cheap: one ZDO bool + a few
        // renderer toggles, and only when the state actually flips.
        private IEnumerator SaddleVisibilityLoop()
        {
            var wait = new WaitForSeconds(0.5f);
            bool? last = null;
            while (true)
            {
                if (_nview == null || !_nview.IsValid()) { _nview = GetComponent<ZNetView>(); yield return wait; continue; }
                bool saddled = _nview.GetZDO().GetBool(ZDOVars.s_haveSaddleHash, false);
                if (last != saddled)
                {
                    foreach (var r in GetComponentsInChildren<Renderer>(true))
                    {
                        if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                        if (r.gameObject.name.ToLowerInvariant().Contains("saddle")) r.enabled = saddled;
                    }
                    last = saddled;
                }
                yield return wait;
            }
        }

        // Live re-apply when a config pins a specific coat/mane/tail (Random leaves each horse's own rolled loadout
        // alone). Called from MountFactory.ApplyLiveTuning on a config change.
        public void ReapplyFromConfig()
        {
            if (_nview == null || !_nview.IsValid()) return;
            var zdo = _nview.GetZDO();
            bool owner = _nview.IsOwner();

            if (IsPinned(MountConfig.Coat)) { int i = PickCoat(); if (owner) zdo.Set(CoatKey, i); _coatApplied = -1; ApplyCoat(i); }
            if (IsPinned(MountConfig.Mane)) { int i = PickMane(); if (owner) zdo.Set(ManeKey, i); ToggleStyle("mane", i < Manes.Length ? Manes[i].sub : null); }
            if (IsPinned(MountConfig.Tail)) { int i = PickTail(); if (owner) zdo.Set(TailKey, i); ToggleStyle("tail", Tails[i].sub); }
        }

        /// <summary>
        /// 0.1.109 variants (Tools\FIRES_VARIANTS_PLAN.md): the coat a variant names (VariantDef.HorseCoat), on every peer; the owner
        /// keeps it in the ZDO so a reload rolls nothing new. False for an unknown coat name.
        /// </summary>
        internal bool UseCoat(string coatName)
        {
            int i = Array.FindIndex(Coats, c => string.Equals(c, coatName, StringComparison.OrdinalIgnoreCase));
            if (i < 0 || _nview == null || !_nview.IsValid()) return false;
            if (_nview.IsOwner()) _nview.GetZDO().Set(CoatKey, i);
            _coatApplied = -1;
            return ApplyCoat(i);
        }

        private static bool IsPinned(BepInEx.Configuration.ConfigEntry<string> cfg)
            => cfg != null && !string.IsNullOrEmpty(cfg.Value) && !cfg.Value.Trim().Equals("Random", StringComparison.OrdinalIgnoreCase);
    }
}
