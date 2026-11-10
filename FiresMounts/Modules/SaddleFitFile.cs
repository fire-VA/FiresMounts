using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// The saddle fit read at runtime from saddle_fit.json next to FiresMounts.dll (the plugins folder, so a public
    /// install never reads outside the game folder). The file is saddle_fit.json as the fitting pipeline
    /// writes it: a JSON array with one entry per mount ("Moder", "Drake", "Fader", "Horse").
    /// <list type="bullet">
    /// <item>The pose: "bone" (path under the mount root), "seat_local_position" / "saddle_local_forward" / "saddle_local_up" in that
    /// bone's local space, "bone_lossy" (the bone's lossy scale at the fit; the saddle mesh takes 1 / bone_lossy so it keeps its
    /// fitted size relative to the body at any ModerScale / FaderScale).</item>
    /// <item>The look (optional): "look": { "albedo", "saddle_tint", "strap_tint", "straps" }.</item>
    /// <item>The rider (optional): "rider_raise" / "rider_forward", the measured defaults for [Mounts.Flyers] / [Mounts.Fader]
    /// RiderRaise / RiderForward. Informational: the config keys stay the live control.</item>
    /// </list>
    /// Read once, on the first <see cref="TryGet"/>; <see cref="Reload"/> re-reads it (firesmounts_saddlefit_reload). A missing file, a
    /// missing mount or a bad entry leaves that mount on the caller's built-in values; one Debug line per mount names the source.
    /// </summary>
    internal static class SaddleFitFile
    {
        internal const string FileName = "saddle_fit.json";

        /// <summary>One mount's fit. HasPose is false for an entry that only carries a look (the horse keeps its own saddle).</summary>
        internal sealed class Fit
        {
            public string Mount;
            public bool HasPose;
            public string BonePath;
            public Vector3 Seat, Forward, Up;
            public float FitLossy;
            public float RiderRaise = float.NaN, RiderForward = float.NaN;
            public Look Look;
        }

        internal sealed class Look
        {
            public string Albedo;          // a Texture2D in firesmountsgear ("" = the prefab's own)
            public Color? SaddleTint;      // _Color on the 'saddle' slot
            public Color? StrapTint;       // _Color on the 'leather' slot
            public string Straps = "both"; // both | girth | breast | none: child renderers named *girth* / *breast*
        }

        // JsonUtility shapes: it reads neither a top-level array nor optional fields, so the text is wrapped in {"items": ...} and
        // a field absent from the file reads as null / empty / 0. JsonUtility fills the fields, so CS0649 is off for them.
#pragma warning disable CS0649
        [Serializable] private class Wrapper { public Entry[] items; }
        [Serializable] private class Entry
        {
            public string mount;
            public string bone;
            public float bone_lossy;
            public float[] seat_local_position, saddle_local_forward, saddle_local_up;
            public float[] rider;   // [raise, forward]; absent = the config defaults
            public LookEntry look;
        }
        [Serializable] private class LookEntry { public string albedo, saddle_tint, strap_tint, straps; }
#pragma warning restore CS0649

        private static Dictionary<string, Fit> _fits;
        private static string _source = "";

        internal static string FilePath => System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", FileName);

        /// <summary>The file's fit for <paramref name="mount"/> ("Moder", "Drake", "Fader", "Horse"), or false (use the built-in values).</summary>
        internal static bool TryGet(string mount, out Fit fit)
        {
            if (_fits == null) Load();
            fit = null;
            return mount != null && _fits.TryGetValue(mount, out fit) && fit != null;
        }

        /// <summary>Re-reads the file (the reload command). The caller re-poses live mounts; returns how many entries were read.</summary>
        internal static int Reload()
        {
            _fits = null;
            Load();
            return _fits.Count;
        }

        /// <summary>The Debug line for one mount: which values it got.</summary>
        internal static string Describe(string mount) =>
            TryGet(mount, out var f) && f.HasPose
                ? $"saddle fit for {mount}: file {_source} (bone {f.BonePath}, seat {f.Seat.ToString("F6")}, fit lossy {f.FitLossy:0.###})"
                : $"saddle fit for {mount}: built-in ({(_source == "" ? "no " + FileName : FileName + " has no pose for it")})";

        private static void Load()
        {
            _fits = new Dictionary<string, Fit>(StringComparer.OrdinalIgnoreCase);
            _source = "";
            string path = FilePath;
            try
            {
                if (!File.Exists(path)) { FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [SaddleFit] no {path}: built-in fits"); return; }
                var w = JsonUtility.FromJson<Wrapper>("{\"items\":" + File.ReadAllText(path) + "}");
                if (w?.items == null) { FiresLogger.LogWarning($"[SaddleFit] {path} is not a JSON array of mounts: built-in fits"); return; }
                _source = path;
                foreach (var e in w.items)
                {
                    if (e == null || string.IsNullOrEmpty(e.mount)) continue;
                    var fit = new Fit { Mount = e.mount, Look = ReadLook(e.look, e.mount) };
                    string why = ReadPose(e, fit);
                    if (why != null && !string.IsNullOrEmpty(e.bone)) FiresLogger.LogWarning($"[SaddleFit] {e.mount}: {why}: built-in pose");
                    if (e.rider != null && e.rider.Length == 2) { fit.RiderRaise = e.rider[0]; fit.RiderForward = e.rider[1]; }
                    _fits[e.mount] = fit;
                }
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [SaddleFit] read {_fits.Count} mount(s) from {path}: {string.Join(", ", _fits.Keys)}");
            }
            catch (Exception ex)
            {
                _fits.Clear();
                _source = "";
                FiresLogger.LogWarning($"[SaddleFit] {path} unreadable ({ex.Message}): built-in fits");
            }
        }

        // null = the pose is usable; otherwise why not (the mount keeps its built-in pose).
        private static string ReadPose(Entry e, Fit fit)
        {
            if (string.IsNullOrEmpty(e.bone)) return "no bone";
            if (!(e.bone_lossy > 0f)) return "bone_lossy must be > 0";
            if (!Vec(e.seat_local_position, out var seat)) return "seat_local_position is not [x, y, z]";
            if (!Vec(e.saddle_local_forward, out var fwd) || fwd.sqrMagnitude < 1e-8f) return "saddle_local_forward is not a direction";
            if (!Vec(e.saddle_local_up, out var up) || up.sqrMagnitude < 1e-8f) return "saddle_local_up is not a direction";
            if (Vector3.Cross(fwd.normalized, up.normalized).sqrMagnitude < 1e-4f) return "forward and up are parallel";
            fit.HasPose = true;
            fit.BonePath = e.bone;
            fit.Seat = seat;
            fit.Forward = fwd.normalized;
            fit.Up = up.normalized;
            fit.FitLossy = e.bone_lossy;
            return null;
        }

        private static Look ReadLook(LookEntry l, string mount)
        {
            if (l == null) return null;
            var look = new Look { Albedo = l.albedo ?? "", SaddleTint = Hex(l.saddle_tint, mount, "saddle_tint"), StrapTint = Hex(l.strap_tint, mount, "strap_tint") };
            string s = (l.straps ?? "").Trim().ToLowerInvariant();
            look.Straps = s == "girth" || s == "breast" || s == "none" ? s : "both";
            return look;
        }

        private static bool Vec(float[] a, out Vector3 v)
        {
            v = default;
            if (a == null || a.Length != 3 || float.IsNaN(a[0] + a[1] + a[2]) || float.IsInfinity(a[0] + a[1] + a[2])) return false;
            v = new Vector3(a[0], a[1], a[2]);
            return true;
        }

        private static Color? Hex(string s, string mount, string key)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (ColorUtility.TryParseHtmlString(s.StartsWith("#") ? s : "#" + s, out var c)) return c;
            FiresLogger.LogWarning($"[SaddleFit] {mount}: {key} '{s}' is not a #RRGGBB colour: left as the material's");
            return null;
        }

        /// <summary>
        /// Proves a fit value reached the code. Call after the saddle (or Sadel) is posed: one Debug line per spawn
        /// with the source (file path + row / built-in), the bone actually found, and the final local + world transform applied.
        /// </summary>
        internal static void LogApplied(string mount, string what, Transform t)
        {
            if (t == null) { FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [SaddleFit] {mount} {what}: NOT applied (no transform)"); return; }
            string src = TryGet(mount, out var f) && f.HasPose ? $"file {_source} row '{f.Mount}'" : "built-in";
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [SaddleFit] {mount} {what} applied from {src}: parent '{(t.parent ? t.parent.name : "-")}', " +
                $"local pos {t.localPosition.ToString("F5")} rot {t.localEulerAngles.ToString("F1")} scale {t.localScale.ToString("F5")}; " +
                $"world pos {t.position.ToString("F3")} lossy {t.lossyScale.x:0.###}");
        }

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        /// <summary>
        /// The look on a spawned saddle (FiresSaddleVisual.Make, every peer): per renderer and material slot a MaterialPropertyBlock
        /// (no material copies) with the 'saddle' slot's albedo + tint and the 'leather' slot's tint; the girth / breast child
        /// renderers off per "straps". <paramref name="texture"/> resolves an albedo name (MountGear's bundle); null = keep the prefab's.
        /// </summary>
        internal static void ApplyLook(GameObject saddle, Look look, Func<string, Texture> texture)
        {
            if (saddle == null || look == null) return;
            Texture albedo = !string.IsNullOrEmpty(look.Albedo) && texture != null ? texture(look.Albedo) : null;
            if (!string.IsNullOrEmpty(look.Albedo) && albedo == null)
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [SaddleFit] {saddle.name}: albedo '{look.Albedo}' is not in the gear bundle: the prefab's own");
            var block = new MaterialPropertyBlock();
            foreach (var r in saddle.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name.ToLowerInvariant();
                bool girth = n.Contains("girth"), breast = n.Contains("breast");
                if (girth || breast)
                    r.enabled = look.Straps == "both" || look.Straps == (girth ? "girth" : "breast");
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    bool strap = mats[i].name.IndexOf("leather", StringComparison.OrdinalIgnoreCase) >= 0;
                    Color? tint = strap ? look.StrapTint : look.SaddleTint;
                    Texture tex = strap ? null : albedo;
                    if (tint == null && tex == null) continue;
                    r.GetPropertyBlock(block, i);
                    if (tint != null) block.SetColor(ColorId, tint.Value);
                    if (tex != null) block.SetTexture(MainTexId, tex);
                    r.SetPropertyBlock(block, i);
                    block.Clear();
                }
            }
        }
    }
}
