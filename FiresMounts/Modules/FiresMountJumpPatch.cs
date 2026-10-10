using HarmonyLib;

namespace FiresMounts
{
    /// <summary>
    /// Vanilla Player.SetControls dismounts the rider whenever jump/attack/secondaryAttack/dodge is pressed while
    /// doodad-riding (Player.cs ~4688: `if (jump | attack | secondaryAttack | dodge) StopDoodadControl()`) —
    /// because vanilla mounts can't fight. Ours can: on OUR mounts, Jump makes the HORSE hop and combat inputs
    /// are consumed so they never eject the rider (horseback combat is a planned feature; dismount is the E key —
    /// see FiresMountController). Vanilla mounts and other mods' saddles are untouched.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class FiresMountJumpPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance, ref bool jump, ref bool attack, ref bool secondaryAttack, ref bool dodge)
        {
            if (!(jump | attack | secondaryAttack | dodge)) return;
            if (!(__instance.GetDoodadController() is Sadle sadle)) return;
            Character mount = sadle.GetCharacter();
            if (mount == null) return;
            var ctrl = mount.GetComponent<FiresMountController>();
            if (ctrl == null) return;   // not a Fires mount — vanilla dismount behaviour stays

            if (jump) { jump = false; ctrl.TryRiderJump(); }       // moving = hop; stationary = rear (never eject)
            if (attack) { attack = false; ctrl.TryRiderAttack(); } // rear-up strike, Asksvin-grade damage
            secondaryAttack = false;
            dodge = false;
        }
    }

    /// <summary>
    /// Reins steering (user-preferred): A/D turn the horse relative to its own heading, camera orbits free,
    /// releasing all input sends Stop. Replaces vanilla camera-steer on OUR mounts only.
    /// </summary>
    [HarmonyPatch(typeof(Sadle), nameof(Sadle.ApplyControlls))]
    internal static class FiresMountSteeringPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Sadle __instance, UnityEngine.Vector3 moveDir, bool run)
        {
            Character mount = __instance.GetCharacter();
            // The horse and (0.1.105) the Mini Fader, a ground mount steered the same way.
            if (mount == null || (mount.GetComponent<FiresMountController>() == null && !FiresFaderRide.IsFader(mount))) return true;
            if (MountConfig.UseVanillaSteering.Value) return true;   // A/B toggle: run vanilla Sadle.ApplyControlls (look-dir steering)
            if (Player.m_localPlayer == null) return false;

            float skill = Player.m_localPlayer.GetSkills().GetSkillFactor(Skills.SkillType.Ride);
            UnityEngine.Vector3 fwd = mount.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            float steer = UnityEngine.Mathf.Clamp(moveDir.x, -1f, 1f);
            bool fwdHeld = moveDir.z > 0.5f;

            Sadle.Speed speed;
            UnityEngine.Vector3 dir = UnityEngine.Vector3.zero;
            if (fwdHeld || (run && moveDir.z >= -0.5f))
            {
                dir = UnityEngine.Quaternion.AngleAxis(steer * 45f, UnityEngine.Vector3.up) * fwd;
                speed = run ? Sadle.Speed.Run : Sadle.Speed.Walk;
            }
            else if (UnityEngine.Mathf.Abs(steer) > 0.3f)
            {
                dir = UnityEngine.Quaternion.AngleAxis(steer * 60f, UnityEngine.Vector3.up) * fwd;
                speed = Sadle.Speed.Turn;
            }
            else
            {
                speed = Sadle.Speed.Stop;   // no input (or S) -> ease to a halt
            }

            __instance.m_nview.InvokeRPC("Controls", dir, (int)speed, skill);
            return false;
        }
    }

    /// <summary>
    /// The user's design, verbatim: the animation may bob the body (Y) but must NEVER drive it forward.
    /// CharacterAnimEvent.OnAnimatorMove feeds animator.deltaPosition into Character.AddRootMotion (applied x55)
    /// — if the clips carry ANY planted-feet positional delta, the body gets snapped "to where the feet say it
    /// should be" against the ride speed. Zero the horizontal part for our mounts; keep vertical.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.AddRootMotion))]
    internal static class FiresMountRootMotionFilter
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance, ref UnityEngine.Vector3 vel)
        {
            if (vel.x == 0f && vel.z == 0f) return;
            // 0.1.109 (hand test 4: "jump itself also slows the mount down"): the Mini Fader and the flyers too - their boss clips'
            // planted feet (the jump clips are tagged "attack") dragged the body back on landing and through turns.
            if (!MountProgress.IsOurMount(__instance)) return;   // the horse, the flyers, the Fader (one dictionary lookup, every frame)
            vel.x = 0f;
            vel.z = 0f;
        }
    }

    /// <summary>
    /// Gait speed ramp. Runs FiresMountController.UpdateGaitRamp right before UpdateWalking reads the move speed, so
    /// holding forward eases the horse from a walk up through the gaits to a gallop (blend tree sweeps naturally)
    /// instead of snapping to run speed. Non-mounts (no FiresMountController) are untouched.
    /// </summary>
    [HarmonyPatch(typeof(Character), "UpdateWalking")]
    internal static class FiresMountGaitRampPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance, float dt)
        {
            var ctrl = __instance.GetComponent<FiresMountController>();
            if (ctrl != null) { ctrl.UpdateGaitRamp(dt); return; }
            var fader = FiresFaderRide.LocalRide;   // 0.1.109: the Fader rides on the horse's ramp too (only the one being ridden here)
            if (fader != null && fader.IsBody(__instance)) fader.UpdateGaitRamp(dt);
        }
    }

    /// <summary>
    /// The mount's gait blend tree runs on a 0..1 normalized forward_speed (automatic thresholds). Vanilla UpdateWalking
    /// writes the RAW ground speed (m/s), which pins the blend to the sprint clip at any real speed above ~1 — so with a
    /// RunSpeed like 24 the horse only ever plays sprint. Normalize forward_speed to speed/RunSpeed for OUR mounts so the
    /// gaits scale to the configured speed range. This fires BEFORE ZSyncAnimation damps + writes the ZDO, so the local
    /// animator and remote riders both get the normalized gait. Non-mounts (no FiresMountController) pass through.
    /// </summary>
    [HarmonyPatch(typeof(ZSyncAnimation), nameof(ZSyncAnimation.SetFloat), new[] { typeof(int), typeof(float) })]
    internal static class FiresMountGaitNormalizePatch
    {
        private static readonly int ForwardHash = UnityEngine.Animator.StringToHash("forward_speed");

        [HarmonyPrefix]
        private static void Prefix(ZSyncAnimation __instance, int hash, ref float value)
        {
            if (hash != ForwardHash) return;
            var ctrl = __instance.GetComponent<FiresMountController>();
            if (ctrl != null) value = ctrl.NormalizeForwardSpeed(value);
        }
    }
}
