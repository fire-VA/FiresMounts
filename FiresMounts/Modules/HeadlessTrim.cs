using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FiresMounts
{
    /// <summary>
    /// Saves memory on a headless dedicated server (~51 MB of embedded bundles otherwise). The server needs our prefabs,
    /// their colliders and meshes (ZNetScene, ObjectDB, physics) but never draws: on a headless server only, once a bundle's prefabs
    /// are collected, the texture slots of their materials are cleared, unused assets are unloaded once, and the bundle itself is
    /// unloaded with Unload(false) (the loaded prefabs stay). Every client keeps everything. Memory is logged before / after.
    /// Also <see cref="ReadAll"/>: one exact-size copy of an embedded resource instead of MemoryStream + ToArray (3 copies at peak).
    /// </summary>
    internal static class HeadlessTrim
    {
        internal static bool Headless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        internal static byte[] ReadAll(System.IO.Stream s)
        {
            var data = new byte[s.Length];
            int at = 0, n;
            while (at < data.Length && (n = s.Read(data, at, data.Length - at)) > 0) at += n;
            return data;
        }

        private static readonly List<Material> Mats = new List<Material>();

        internal static void Trim(IEnumerable<GameObject> prefabs, ref AssetBundle bundle, string tag)
        {
            if (!Headless || bundle == null) return;
            long before = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(), managedBefore = System.GC.GetTotalMemory(false);
            int cleared = 0;
            var seen = new HashSet<Material>();
            foreach (GameObject p in prefabs)
            {
                if (p == null) continue;
                foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true))
                {
                    r.GetSharedMaterials(Mats);
                    foreach (Material m in Mats)
                    {
                        if (m == null || !seen.Add(m)) continue;
                        foreach (int id in m.GetTexturePropertyNameIDs())
                            if (m.GetTexture(id) != null) { m.SetTexture(id, null); cleared++; }
                    }
                }
            }
            bundle.Unload(false);
            bundle = null;
            Resources.UnloadUnusedAssets();
            System.GC.Collect();
            long after = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(), managedAfter = System.GC.GetTotalMemory(true);
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Mem] headless server: '{tag}' bundle unloaded, {cleared} texture slot(s) cleared on {seen.Count} material(s); " +
                $"Unity allocated {before / 1048576f:0.0} -> {after / 1048576f:0.0} MB, managed {managedBefore / 1048576f:0.0} -> {managedAfter / 1048576f:0.0} MB");
        }
    }
}