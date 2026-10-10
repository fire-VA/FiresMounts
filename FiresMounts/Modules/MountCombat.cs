using FiresPossession;
using HarmonyLib;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// The rider's mouse buttons on a creature mount (the flyers, 0.1.104; the Mini Fader, 0.1.105). Fire 10-07: "when riding the
    /// Mini Moder we do Mini Moder attacks". Attacks go through Core's CreatureAttacks, the possession engine's own path ([ghost]'s
    /// drop 5): the creature's weapon equipped, its own StartAttack. LMB = Main, MMB = Special; Alt + the mouse wheel picks the
    /// special. Every attack goes where the crosshair points: the camera is free, so the crosshair is the aim (Fire 10-08: "we don't
    /// need hold RMB to aim"). RMB is the bubble shield (Fire 10-08: "rmb could actually be some sort of block ... a bubble shield").
    /// One per ridden mount component; <c>tag</c> names it in the Debug log ("Flight", "Fader").
    /// </summary>
    internal sealed class MountCombat
    {
        internal enum Slot { Main, Special }

        private const float AimHoldSeconds = 1.5f;
        private const float AimRange = 150f;

        // Vanilla's Staff of Protection shield (SE_Shield "Staff_shield": its bubble, hit and break effects; it absorbs ~200 damage, then
        // breaks), on the mount and on the rider, for [Mounts.Flyers] ShieldSeconds, once per ShieldCooldown.
        private static readonly int s_shieldEffect = "Staff_shield".GetStableHashCode();
        private static readonly Host s_host = new Host();

        private readonly Character _mount;
        private readonly string _tag;
        private float _shieldReadyAt;
        private Vector3 _aimPoint;
        private float _aimUntil = float.NegativeInfinity;

        internal MountCombat(Character mount, string tag)
        {
            _mount = mount;
            _tag = tag;
        }

        private string Name => _mount != null ? _mount.name : "?";

        /// <summary>Fires the mount's Main / Special attack at the crosshair. <paramref name="heading"/> (the mount's travel yaw, NaN if
        /// none) only feeds the log line.</summary>
        internal void Attack(Slot slot, float heading)
        {
            var creature = _mount as Humanoid;
            if (creature == null) return;
            _aimPoint = AimPoint();
            _aimUntil = Time.time + AimHoldSeconds;
            Vector3 dir = _aimPoint - _mount.GetCenterPoint();
            if (dir.sqrMagnitude > 0.01f) _mount.SetLookDir(dir.normalized);
            Vector3 flat = dir; flat.y = 0f;
            float off = !float.IsNaN(heading) && flat.sqrMagnitude > 0.01f
                ? Mathf.Abs(Mathf.DeltaAngle(heading, Quaternion.LookRotation(flat).eulerAngles.y)) : 0f;
            bool fired = CreatureAttacks.Fire(creature, slot == Slot.Main ? PossessAttackKind.Main : PossessAttackKind.Special, s_host);
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [{_tag}] {Name}: mount attack {slot} {(fired ? "fired" : "refused")} at the crosshair ({off:0}° off the heading, {dir.magnitude:0} m)");
        }

        internal void Shield()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            if (Time.time < _shieldReadyAt)
            {
                player.Message(MessageHud.MessageType.TopLeft, $"Shield ready in {Mathf.CeilToInt(_shieldReadyAt - Time.time)} s");
                return;
            }
            if (ObjectDB.instance == null || ObjectDB.instance.GetStatusEffect(s_shieldEffect) == null)
            {
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [{_tag}] {Name}: no vanilla Staff_shield effect in ObjectDB, no shield");
                return;
            }
            float seconds = MountConfig.FlyerShieldSeconds.Value;
            foreach (Character c in new Character[] { _mount, player })
            {
                if (c == null || c.GetSEMan() == null) continue;
                StatusEffect se = c.GetSEMan().AddStatusEffect(s_shieldEffect, resetTime: true, itemLevel: 1, skillLevel: 0f);
                if (se != null) se.m_ttl = seconds;
            }
            _shieldReadyAt = Time.time + MountConfig.FlyerShieldCooldown.Value;
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [{_tag}] {Name}: mount shield up on the mount and the rider ({seconds:0} s, ready again in {MountConfig.FlyerShieldCooldown.Value:0} s)");
        }

        /// <summary>While an aimed attack runs, the mount keeps looking at the aim point (its own steering sets the look every tick).</summary>
        internal void HoldAim()
        {
            if (Time.time > _aimUntil || _mount == null || !_mount.InAttack()) return;
            Vector3 dir = _aimPoint - _mount.GetCenterPoint();
            if (dir.sqrMagnitude > 0.01f) _mount.SetLookDir(dir.normalized);
        }

        /// <summary>Alt + mouse wheel picks the special (call from Update while the local player rides).</summary>
        internal void CycleSpecialOnAltWheel()
        {
            if (!MountConfig.FlyerMountAttacks.Value || !FiresCore.Flight.FlightKeys.InputOpen || TypingGate.Typing()) return;
            if (!ZInput.GetKey(KeyCode.LeftAlt) && !ZInput.GetKey(KeyCode.RightAlt)) return;
            float wheel = ZInput.GetMouseScrollWheel();
            if (Mathf.Abs(wheel) < 0.01f || !(_mount is Humanoid creature)) return;
            CreatureAttacks.CycleSpecial(creature, wheel > 0f ? 1 : -1, s_host);
        }

        // The point under the crosshair: the camera ray's first hit past the mount and its rider, else AimRange out.
        private Vector3 AimPoint()
        {
            Transform cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            if (cam == null) return _mount.transform.position + _mount.transform.forward * AimRange;
            int mask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "character", "character_net", "character_noenv", "Default_small");
            RaycastHit[] hits = Physics.RaycastAll(cam.position, cam.forward, AimRange, mask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                var c = hit.collider != null ? hit.collider.GetComponentInParent<Character>() : null;
                if (c == _mount || c == Player.m_localPlayer) continue;
                return hit.point;
            }
            return cam.position + cam.forward * AimRange;
        }

        /// <summary>A creature mount the rider fights from (a flyer or the Mini Fader).</summary>
        internal static bool IsCombatMount(Character c) =>
            c != null && (c.GetComponent<FiresFlyerFlight>() != null || c.GetComponent<FiresFaderRide>() != null);

        // The engine's host for the mount's attacks: its picks to the rider's screen, its refusal reasons to the Debug log.
        private sealed class Host : IPossessionHost
        {
            public void CameraOn(Character creature) { }
            public void CameraOff() { }
            public float Config(string key, float def) => def;
            public bool Config(string key, bool def) => def;
            public void Say(string message)
            {
                if (Player.m_localPlayer != null && !string.IsNullOrEmpty(message)) Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, message);
            }
            public void Log(string line) => FiresMountsPlugin.Log.LogDebug("[FiresMounts] mount attack: " + line);
        }

        // A creature mount's attacks deal [Mounts.Flyers] AttackDamage of the vanilla creature's damage (the mini Moder carries the boss's
        // breath and claws, the Mini Fader the Fader's). Scaled where the hit is made, on the attacker's side, once.
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        internal static class MountAttackDamage
        {
            [HarmonyPrefix]
            private static void Prefix(HitData hit)
            {
                if (hit == null || hit.m_attacker.IsNone()) return;
                if (!IsCombatMount(hit.GetAttacker())) return;
                hit.m_damage.Modify(Mathf.Max(0f, MountConfig.FlyerAttackDamage.Value));
            }
        }
    }
}
