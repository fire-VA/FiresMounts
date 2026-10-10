using System.Collections.Generic;
using FiresCore.Mounts;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// 0.1.130 (Fire 10-09: "up to ONE companion can ride on any mount directly behind the player"): FiresMounts' half of Core's
    /// PillionApi (contract frozen with [generator]). Core picks the companion (the owner's nearest in Follow mode while the owner
    /// rides) and does the body work in PillionApi.Seat / Unseat; this provider owns:
    /// - the "Pillion" node: beside the rider's seat node (Sadel), under the same parent (the seat bone on the creature mounts, so it
    ///   banks and tilts with the body), PillionBack metres behind it along the saddle's forward, made on the first board;
    /// - the mount's ZDO "firesmounts_pillion" (the companion's ZDOID), written by the mount's owner (the rider);
    /// - no collisions between the companion and the mount / rider while seated;
    /// - the set-down point on Leave: beside the mount on solid ground with a clear capsule, else where the rider stands.
    /// The rides call LeaveFor(mount) from their End (dismount for any reason, death, despawn, teleport) and OnDestroy.
    /// </summary>
    internal sealed class MountPillion : IPillionProvider
    {
        internal const string ZdoPillion = "firesmounts_pillion";
        private const string NodeName = "Pillion";
        private const float PillionBack = 0.45f, BoardRange = 12f, SideStep = 1.5f;
        private static MountPillion s_instance;

        // The companion's colliders and what it was told to ignore, for the restore.
        private static readonly Dictionary<Character, List<(Collider, Collider)>> s_ignored = new Dictionary<Character, List<(Collider, Collider)>>();

        internal static void Install()
        {
            if (s_instance != null) return;
            s_instance = new MountPillion();
            PillionApi.Register(s_instance);
            FiresMountsPlugin.Log.LogDebug("[FiresMounts] [Pillion] provider registered with Core's PillionApi");
        }

        public bool TryBoard(Character companion, Character mount, out string reason)
        {
            reason = null;
            if (companion == null || mount == null) { reason = "nothing to board"; return false; }
            if (!MountNames.IsMount(global::Utils.GetPrefabName(mount.gameObject))) { reason = "not one of our mounts"; return false; }
            var sadle = mount.GetComponentInChildren<Sadle>(true);
            if (sadle == null || !sadle.HaveValidUser()) { reason = "nobody rides it"; return false; }
            var nview = mount.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) { reason = "the mount is not the rider's here"; return false; }
            Character seated = Seated(mount);
            if (seated != null && seated != companion) { reason = $"{seated.m_name} already rides behind"; return false; }
            if (Vector3.Distance(companion.transform.position, mount.transform.position) > BoardRange) { reason = $"more than {BoardRange:0} m away"; return false; }

            Transform node = NodeOf(sadle);
            if (node == null) { reason = "no seat to sit behind"; return false; }
            nview.GetZDO().Set(ZdoPillion, companion.GetZDOID());
            IgnoreCollisions(companion, mount, Player.m_localPlayer);
            PillionApi.Seat(companion, node, "attach_lox");
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Pillion] {companion.m_name} boards {mount.name} at {node.parent.name}/{NodeName}");
            return true;
        }

        public void Leave(Character companion, string why)
        {
            if (companion == null) return;
            Character mount = MountOf(companion);
            Vector3 down = SetDownPoint(mount, companion);
            if (mount != null)
            {
                var nview = mount.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && nview.IsOwner()) nview.GetZDO().Set(ZdoPillion, ZDOID.None);
            }
            RestoreCollisions(companion);
            PillionApi.Unseat(companion, down);
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Pillion] {companion.m_name} leaves {(mount != null ? mount.name : "the mount")} ({why}) at {down}");
        }

        public Character Seated(Character mount)
        {
            var nview = mount != null ? mount.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid()) return null;
            ZDOID id = nview.GetZDO().GetZDOID(ZdoPillion);
            if (id.IsNone() || ZNetScene.instance == null) return null;
            GameObject go = ZNetScene.instance.FindInstance(id);
            return go != null ? go.GetComponent<Character>() : null;
        }

        /// <summary>The rides' End / OnDestroy: whoever sits behind on this mount gets off.</summary>
        internal static void LeaveFor(Character mount, string why)
        {
            if (s_instance == null || mount == null) return;
            Character c = s_instance.Seated(mount);
            if (c != null) s_instance.Leave(c, why);
        }

        // The node beside Sadel, under the same parent, PillionBack metres behind it (in that parent's scale), the saddle's rotation.
        private static Transform NodeOf(Sadle sadle)
        {
            Transform seat = sadle.transform, parent = seat.parent;
            if (parent == null) return null;
            Transform node = parent.Find(NodeName);
            if (node != null) return node;
            node = new GameObject(NodeName).transform;
            node.SetParent(parent, false);
            float lossy = Mathf.Max(0.0001f, parent.lossyScale.x);
            node.localPosition = seat.localPosition - seat.localRotation * Vector3.forward * (PillionBack / lossy);
            node.localRotation = seat.localRotation;
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Pillion] node on {sadle.GetCharacter()?.name} at {parent.name}, {PillionBack} m behind the seat");
            return node;
        }

        private static Character MountOf(Character companion)
        {
            foreach (Character c in Character.GetAllCharacters())
                if (c != null && MountNames.IsMount(global::Utils.GetPrefabName(c.gameObject)) && s_instance.Seated(c) == companion) return c;
            return null;
        }

        // Beside the mount (right, then left) on solid ground with room for the companion's capsule; else the rider's own spot.
        private static Vector3 SetDownPoint(Character mount, Character companion)
        {
            Player rider = Player.m_localPlayer;
            Vector3 fallback = rider != null ? rider.transform.position : companion.transform.position;
            if (mount == null || ZoneSystem.instance == null) return fallback;
            float radius = 0.5f, height = 1.8f;
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 p = mount.transform.position + mount.transform.right * (side * (SideStep + mount.GetRadius()));
                if (!ZoneSystem.instance.GetSolidHeight(p, out float ground)) continue;
                p.y = ground + 0.05f;
                bool blocked = Physics.CheckCapsule(p + Vector3.up * (radius + 0.05f), p + Vector3.up * (height - radius), radius,
                    LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle"), QueryTriggerInteraction.Ignore);
                if (!blocked) return p;
            }
            return fallback;
        }

        private static void IgnoreCollisions(Character companion, Character mount, Player rider)
        {
            var pairs = new List<(Collider, Collider)>();
            var mine = companion.GetComponentsInChildren<Collider>(true);
            var others = new List<Collider>(mount.GetComponentsInChildren<Collider>(true));
            if (rider != null) others.AddRange(rider.GetComponentsInChildren<Collider>(true));
            foreach (var a in mine)
                foreach (var b in others)
                    if (a != null && b != null && !Physics.GetIgnoreCollision(a, b)) { Physics.IgnoreCollision(a, b, true); pairs.Add((a, b)); }
            s_ignored[companion] = pairs;
        }

        private static void RestoreCollisions(Character companion)
        {
            if (!s_ignored.TryGetValue(companion, out var pairs)) return;
            foreach (var (a, b) in pairs) if (a != null && b != null) Physics.IgnoreCollision(a, b, false);
            s_ignored.Remove(companion);
        }
    }
}
