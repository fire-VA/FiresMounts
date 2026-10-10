using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// Reads a collider-bake file authored by FiresDebugginTools' in-world collider editor ("Save as default")
    /// and applies it to a prefab at compose time, so hand-tuned collider geometry ships as the spawn default for
    /// EVERY instance. This is the READ half of a FILE CONTRACT — no assembly reference to the debug tools; we
    /// just parse the same compact format they write (BepInEx/config/FiresColliderBakes/&lt;prefab&gt;.fcb).
    ///
    /// Layering: hardcoded MountFactory geometry -> THIS bake (per-prefab default) -> per-creature ZDO edits the
    /// debug tools re-apply on Character.Awake (an individually "Save to this one"'d animal wins over the default).
    /// Missing file = untouched, so shipping without a bake just uses the code defaults.
    /// </summary>
    internal static class ColliderBakeReader
    {
        private static string Dir => Path.Combine(BepInEx.Paths.ConfigPath, "FiresColliderBakes");

        public static int Apply(GameObject root, string prefabName)
        {
            if (root == null || string.IsNullOrEmpty(prefabName)) return 0;
            try
            {
                string path = Path.Combine(Dir, Sanitize(prefabName) + ".fcb");
                if (!File.Exists(path)) return 0;
                var edits = Parse(File.ReadAllText(path));
                if (edits.Count == 0) return 0;

                var byKey = Locate(root);
                int applied = 0;
                foreach (var e in edits)
                    if (byKey.TryGetValue(e.path + "#" + e.index, out var c) && c != null) { Write(c, e); applied++; }
                return applied;
            }
            catch { return 0; }
        }

        // ---- collider indexing (must mirror FiresColliderEdit.Locate exactly: skip CharacterController/MeshCollider,
        //      key = hierarchy path under root + ordinal among colliders sharing a transform) --------------------
        private static Dictionary<string, Collider> Locate(GameObject root)
        {
            var map = new Dictionary<string, Collider>();
            var perTransform = new Dictionary<Transform, int>();
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c is CharacterController || c is MeshCollider) continue;
                int idx = perTransform.TryGetValue(c.transform, out var n) ? n : 0;
                perTransform[c.transform] = idx + 1;
                map[PathOf(c.transform, root.transform) + "#" + idx] = c;
            }
            return map;
        }

        private static string PathOf(Transform t, Transform root)
        {
            if (t == root || t == null) return "";
            var stack = new List<string>();
            for (Transform cur = t; cur != null && cur != root; cur = cur.parent) stack.Insert(0, cur.name);
            return string.Join("/", stack);
        }

        private static void Write(Collider c, Edit e)
        {
            switch (c)
            {
                case BoxCollider b: b.center = e.center; b.size = e.size; break;
                case SphereCollider s: s.center = e.center; s.radius = e.radius; break;
                case CapsuleCollider p: p.center = e.center; p.radius = e.radius; p.height = e.height; p.direction = e.direction; break;
            }
        }

        // ---- parse (mirror of FiresColliderEdit.Serialize) ----------------------------------------------------
        private struct Edit
        {
            public string path; public int index; public Vector3 center;
            public float radius; public float height; public Vector3 size; public int direction;
        }

        private static List<Edit> Parse(string blob)
        {
            var list = new List<Edit>();
            if (string.IsNullOrEmpty(blob)) return list;
            foreach (var rec in blob.Split(';'))
            {
                var f = rec.Split('|');
                if (f.Length < 8) continue;
                list.Add(new Edit
                {
                    path = f[0],
                    index = PInt(f[1]),
                    center = V3(f[3]),
                    radius = PF(f[4]),
                    height = PF(f[5]),
                    size = V3(f[6]),
                    direction = PInt(f[7]),
                });
            }
            return list;
        }

        private static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private static float PF(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        private static int PInt(string s) => int.TryParse(s, out var v) ? v : 0;
        private static Vector3 V3(string s)
        {
            var p = s.Split(',');
            return p.Length == 3 ? new Vector3(PF(p[0]), PF(p[1]), PF(p[2])) : Vector3.zero;
        }
    }
}
