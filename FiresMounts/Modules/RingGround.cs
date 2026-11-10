using UnityEngine;

namespace FiresMounts
{
    // A13 ([fgn] files for [worldgen], halo ring 10-24): the ONE place FiresMounts asks about the ring. Every caller keeps its old
    // world-Y code in the else branch, so off the ring nothing changes. Core 0.2.816+: GravityUp (the ring's up, toward the axis)
    // and Surface.Point (the floor along -up).
    internal static class RingGround
    {
        private static float s_nextLog;

        /// <summary>True only inside a gravity field (the halo ring); up = the local up there.</summary>
        internal static bool InRing(Vector3 at, out Vector3 up) => FiresCore.World.GravityUp.InField(at, out up);

        /// <summary>In the ring's field: the floor under <paramref name="at"/> along -up. False off the ring or when nothing is under it.</summary>
        internal static bool Floor(Vector3 at, out Vector3 floor, out Vector3 up)
        {
            floor = at; up = Vector3.up;
            if (!InRing(at, out _)) return false;
            return FiresCore.World.Surface.Point(at, out floor, out up);
        }

        /// <summary>dir flattened onto the ring's floor plane (dir.y = 0 on the flat world).</summary>
        internal static Vector3 Flat(Vector3 dir, Vector3 up, Vector3 fallback)
        {
            Vector3 f = Vector3.ProjectOnPlane(dir, up);
            return f.sqrMagnitude > 1e-4f ? f.normalized : fallback;
        }

        internal static void Note(string what)
        {
            if (Time.unscaledTime < s_nextLog) return;
            s_nextLog = Time.unscaledTime + 10f;
            FiresMountsPlugin.Log.LogDebug("[FiresMounts] [Ring] " + what);
        }
    }
}
