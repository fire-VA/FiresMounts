using FiresPossession;
using HarmonyLib;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// Flying mounts, slice 2: the flight (Tools\FIRESMOUNTS_FLYING_PLAN.md §4-7; Fire 10-07 "we better get flying in").
    /// While the LOCAL player rides this flyer, Core's FiresPossession.FlightModel (Core 0.2.320, the controller Valcast uses)
    /// steers vanilla Character flight from Fire's keys, read by FiresCore.Flight.FlightKeys.ReadLocal():
    /// helicopter style (Fire 10-07, final set): W nose down + faster, S nose up + slower, A/D bank and slide, Q/E flat yaw,
    /// Shift faster (takes off from the ground), Space held climbs (double tap: hover lock), Ctrl held descends and lands (double
    /// tap: step off mid-air). No stamina, no stall: no input eases into a hover (FlightTuning.StaminaDrain stays null).
    /// FlyerFlightHints shows the keys on screen while the local player rides.
    /// - Ownership: vanilla Sadle hands the mount's ZDO to the rider's client on RequestControl, so the flight runs where the
    ///   keys are, in FixedUpdate, and position syncs as for any creature.
    /// - In the air, vanilla ground riding (Sadle.UpdateRiding) is held off, and Space / a click never dismounts (vanilla
    ///   dismounts on jump, attack and secondary attack). A double tap of Ctrl (or vanilla's dodge) steps off mid-air: the rider
    ///   falls with vanilla fall damage, the mount lands (SettleUnridden) and FiresFlyerFollow sends it after them.
    /// - The model's bank / pitch roll the "Visual" child on the rider's client; other clients copy them from the ZDO.
    /// </summary>
    // Early in LateUpdate (after the animator, before the camera follows the player), so the rider is glued to this frame's seat.
    [DefaultExecutionOrder(-200)]
    public class FiresFlyerFlight : MonoBehaviour
    {
        private const float SyncSeconds = 0.1f;
        private const float RemoteSmooth = 8f;
        private static readonly int s_pitchBank = "FiresMounts_FlightPitchBank".GetStableHashCode();

        // The saddle fit (FlyerFactory, from saddle_fit.json): the back bone's path, the seat point and the saddle's forward / up
        // in that bone's local space. Serialized on the prefab, so every instance has them.
        public string SeatBonePath = "";
        public Vector3 SeatLocal, SeatForward = Vector3.forward, SeatUp = Vector3.up;
        public float RiderRaise, RiderForward;   // 0.1.117: the fit's rider offset; [Mounts.Flyers] RiderRaise / RiderForward add to it

        private Character _character;
        private ZNetView _nview;
        private Sadle _sadle;
        private float _nextDiag;
        private Transform _seatBone;
        private Transform _visualNode;
        private Rigidbody _body;
        private RigidbodyInterpolation _bodyInterpolation;
        private FlightModel _model;
        private FlightTuning _tuning;
        private MountCombat _combat;
        private MountProgress _progress;
        private bool _active;
        private float _lastUse = -10f;
        private bool _dismountRequested;
        private float _nextSync;

        private Transform _remoteVisual;
        private Quaternion _remoteRest;
        private bool _remoteInit;

        /// <summary>The local rider is flying this mount: Space climbs instead of dismounting, ground riding is held off.</summary>
        internal bool InFlight => _active && _character != null && _character.IsFlying();

        /// <summary>The flyer the local player rides right now (FlyerFlightHints), or null.</summary>
        internal static FiresFlyerFlight LocalRide { get; private set; }
        internal bool Airborne => _character != null && _character.IsFlying();
        internal bool HoverLocked => _model != null && _model.HoverLocked;
        internal bool Tired => _progress != null && _progress.Tired;

        private void Awake()
        {
            _character = GetComponent<Character>();
            _nview = GetComponent<ZNetView>();
            _sadle = GetComponentInChildren<Sadle>(true);
            _seatBone = string.IsNullOrEmpty(SeatBonePath) ? null : transform.Find(SeatBonePath);
            _visualNode = transform.Find("Visual");
            _body = GetComponent<Rigidbody>();
            _combat = _character != null ? new MountCombat(_character, "Flight") : null;
            _progress = GetComponent<MountProgress>();
        }

        // Fire's ride feedback (10-07, "position the player a little better"): the rider's seat is the saddle fit's point on the back
        // bone plus [Mounts.Flyers] RiderRaise / RiderForward (live), turned with the body's tilted Visual rather than the bone,
        // so the wing beat's spine sway doesn't rock the rider off the spine.
        private void PoseSeat()
        {
            if (_seatBone == null || _sadle == null) return;
            float lossy = Mathf.Max(_seatBone.lossyScale.x, 0.0001f);
            Vector3 local = SeatLocal + SeatUp.normalized * ((RiderRaise + MountConfig.FlyerRiderRaise.Value) / lossy)
                            + SeatForward.normalized * ((RiderForward + MountConfig.FlyerRiderForward.Value) / lossy);
            _sadle.transform.SetPositionAndRotation(_seatBone.TransformPoint(local), (_visualNode != null ? _visualNode : transform).rotation);
        }

        // Fire's ride feedback ("it keeps snapping back under the player"): vanilla attaches the rider in the player's FixedUpdate,
        // at the seat bone as it was last rendered, so at speed the rider and the mount drew out of step. After this frame's
        // animation the seat is posed and the local rider put on it, and the ridden mount's Rigidbody interpolates like the player's.
        private void LateUpdate()
        {
            if (!_active) return;
            if (_dismountRequested) { _dismountRequested = false; Dismount("double E"); return; }
            EnsureSeated(_sadle, name, "Flight");
            FlyerFlightHints.KeepShown(LocalRide == this);
            PoseSeat();
            var player = Player.m_localPlayer;
            if (player != null && _sadle != null && ReferenceEquals(player.GetDoodadController(), _sadle))
                player.transform.SetPositionAndRotation(_sadle.transform.position, _sadle.transform.rotation);
        }

        private void Start()
        {
            if (_character != null && _nview != null && _nview.IsValid() && _nview.IsOwner())
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Flight] {name}: spawned {(_character.IsFlying() ? "in the air" : "on the ground")}");
        }

        private bool RiddenByLocalPlayer()
        {
            var player = Player.m_localPlayer;
            return player != null && _sadle != null && _nview.IsOwner()
                   && ReferenceEquals(player.GetDoodadController(), _sadle);
        }

        private void FixedUpdate()
        {
            if (_character == null || _nview == null || !_nview.IsValid()) return;
            bool ridden = RiddenByLocalPlayer();
            if (ridden && !_active) Begin();
            else if (!ridden && _active) End();

            if (_active)
            {
                PoseSeat();
                FlightInput input = FiresCore.Flight.FlightKeys.ReadLocal();
                if (_progress != null) _tuning.Ceiling = _progress.Ceiling;   // 0.1.106: 40 m at Lv 1 .. 100 m at Lv 20
                _model.Step(_character, in input, _tuning, Time.fixedDeltaTime);
                // 0.1.119 (round 22 fly_land: "Descending ... onGround True, above ground 0.0 m" and still flying): a flyer standing
                // on the ground, not climbing or taking off and not rising, has landed. Vanilla's Land(); the model reads Grounded next step.
                if (_character.IsFlying() && _character.IsOnGround() && !input.Climb && !input.Gas && _model.Vertical <= 0.01f
                    && new Vector3(_character.GetVelocity().x, 0f, _character.GetVelocity().z).magnitude < 3f)
                {
                    _character.Land();
                    FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Flight] {name} landed ({(input.Descend ? "descend" : "touched down")}) on the ground, was {_model.State}");
                }
                if (Time.time >= _nextDiag)
                {
                    // 0.1.117 (round 18 fly_pitch / fly_land): what the model asks for vs what the body does, once a second.
                    _nextDiag = Time.time + 1f;
                    Vector3 v = _character.GetVelocity();
                    float ground = ZoneSystem.instance != null ? transform.position.y - ZoneSystem.instance.GetGroundHeight(transform.position) : -1f;
                    FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Flight] {name} diag: state {_model.State}, flying {_character.IsFlying()}, onGround {_character.IsOnGround()}, throttle {_model.Throttle:0.00}, lateral {_model.Lateral:0.00}, vertical {_model.Vertical:0.00}, velocity {v.magnitude:0.0} m/s (flat fwd {Vector3.Dot(new Vector3(v.x, 0f, v.z), transform.forward):0.0}), above ground {ground:0.0} m, tired {(_progress != null && _progress.Tired)}");
                }
                _progress?.FlightTick(_model.State, Time.fixedDeltaTime);
                _combat?.HoldAim();
                if (Time.time >= _nextSync)
                {
                    _nextSync = Time.time + SyncSeconds;
                    _nview.GetZDO().Set(s_pitchBank, new Vector3(_model.Pitch, _model.Bank, 0f));
                }
            }
            else if (!_nview.IsOwner()) CopyRemoteVisual(Time.fixedDeltaTime);
            else SettleUnridden();
        }

        // Fire's hand test (10-07): a flyer nobody rides never stays up (after a mid-air dismount, or one that loaded flying). Its
        // owner lands it; vanilla Land() hands it to walking and gravity, and creatures take no fall damage.
        private void SettleUnridden()
        {
            if (!_character.IsFlying() || _nview.GetZDO().GetLong(ZDOVars.s_user, 0L) != 0L) return;
            float above = ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(transform.position, out float ground)
                ? transform.position.y - ground : -1f;
            _character.Land();
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Flight] {name}: nobody riding, landing from {above:0.0} m");
        }

        private void Begin()
        {
            _model = new FlightModel();
            _model.Reset(transform.eulerAngles.y);
            _model.StateChanged += state => FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Flight] {name}: {state}");
            // Ctrl double-tapped in the air (the model's own tap detection): step off; the mount lands and follows.
            _model.StepOffRequested += StepOff;
            LocalRide = this;
            FlyerFlightHints.Ensure();
            if (_progress != null) FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Levels] {name} range from full: {_progress.RangeLine()}");
            if (_body != null) { _bodyInterpolation = _body.interpolation; _body.interpolation = RigidbodyInterpolation.Interpolate; }
            // Our flyers walk and fly (0.1.102 turned their m_randomFly off, which the model's default CanLand reads), so Ctrl lands.
            // 0.1.106: the mount's stamina (MountProgress) drains by the model's state; at 0 the model's Tired sinks it and lands it.
            _tuning = new FlightTuning
            {
                CanLand = _ => true,
                Log = line => FiresMountsPlugin.Log.LogDebug("[FiresMounts] " + line),
                Tired = () => _progress != null && _progress.Tired,
            };
            _active = true;
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Flight] {name}: rider on, {(_character.IsFlying() ? "in the air" : "on the ground")}");
        }

        // The flight model's double Ctrl (in the air only): the step-off.
        private void StepOff()
        {
            if (InFlight) Dismount("double Ctrl");
        }

        // Fire 10-08: "e to mount and then double tap e to dismount". A double E (or, in the air, the double Ctrl) is the only way off:
        // on the ground a normal dismount, in the air the step-off (the rider falls, the mount lands and follows).
        private void Dismount(string how)
        {
            var player = Player.m_localPlayer;
            if (player == null || !ReferenceEquals(player.GetDoodadController(), _sadle)) return;
            FiresMountsPlugin.Log.LogDebug(InFlight
                ? $"[FiresMounts] [Flight] {name}: rider stepped off in the air ({how})"
                : $"[FiresMounts] [Flight] {name}: rider dismounted on the ground ({how})");
            KeepRiderInFlight.Allow = true;
            try { player.StopDoodadControl(); }
            finally { KeepRiderInFlight.Allow = false; }
        }

        /// <summary>A double tap of Use (E) while the local player rides a flyer or the Mini Fader: true on the second tap.</summary>
        internal static bool DoubleUse(ref float lastUse)
        {
            if (!FiresCore.Flight.FlightKeys.InputOpen || TypingGate.Typing() || !(ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse"))) return false;
            if (Time.time - lastUse <= FlightModel.DoubleTapSeconds) { lastUse = -10f; return true; }
            lastUse = Time.time;
            return false;
        }

        // Fire's hand test 3 (10-08): the rider came off at 228 m with no step-off ("Stop doodad controlls" from vanilla: Use with
        // nothing hovered, dodge, or the 5 m seat-distance check). Fire 10-08 later: E is a flight key, so on a flyer or the Mini Fader,
        // ground or air, vanilla's stops are refused and only the double E (or the double Ctrl in the air) dismounts. A dead rider, a
        // dead or gone mount or a teleport still stops it. One Debug line names the caller.
        // 0.1.117 (Fire, round 18: "standing on its back and still playing our running animations"): the rider keeps the doodad (we
        // refuse vanilla's stops) but can lose vanilla's attach (Player.m_attached: the seat pose, attach_lox, gravity off). Without it
        // the body stands and runs on the mount's back. Whenever our mount is ridden locally and the rider is not attached, seat them
        // the Sadle's own way (RPC_RequestRespons' AttachStart), and log it once a few seconds with the animator's state.
        private static float s_nextSeatLog;
        internal static void EnsureSeated(Sadle sadle, string mount, string tag)
        {
            var player = Player.m_localPlayer;
            if (player == null || sadle == null || sadle.m_attachPoint == null || !ReferenceEquals(player.GetDoodadController(), sadle)) return;
            if (player.IsAttached() || player.IsDead()) return;
            player.AttachStart(sadle.m_attachPoint, sadle.GetCharacter().gameObject, false, false, false, sadle.m_attachAnimation, sadle.m_detachOffset, null);
            if (Time.time < s_nextSeatLog) return;
            s_nextSeatLog = Time.time + 3f;
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [{tag}] {mount}: the rider had lost the seat (not attached); seated again ({sadle.m_attachAnimation}), attached {player.IsAttached()}");
        }

        [HarmonyPatch(typeof(Player), nameof(Player.StopDoodadControl))]
        internal static class KeepRiderInFlight
        {
            internal static bool Allow;
            private static float s_nextLog;

            [HarmonyPrefix]
            private static bool Prefix(Player __instance)
            {
                if (Allow || __instance == null || __instance != Player.m_localPlayer || __instance.IsDead() || __instance.IsTeleporting()) return true;
                var sadle = __instance.GetDoodadController() as Sadle;
                var mount = sadle != null ? sadle.GetCharacter() : null;
                if (mount == null || mount.IsDead()) return true;
                var flight = mount.GetComponent<FiresFlyerFlight>();
                var fader = flight == null ? mount.GetComponent<FiresFaderRide>() : null;
                if (!((flight != null && flight._active) || (fader != null && fader.Active))) return true;
                if (Time.time >= s_nextLog)
                {
                    s_nextLog = Time.time + 2f;
                    var frame = new System.Diagnostics.StackTrace(2, false).GetFrame(0)?.GetMethod();
                    string caller = frame != null ? (frame.DeclaringType != null ? frame.DeclaringType.Name + "." : "") + frame.Name : "?";
                    FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [{(flight != null ? "Flight" : "Fader")}] {mount.name}: kept the rider on ({(mount.IsFlying() ? "in the air" : "on the ground")}, vanilla stop from {caller}); double E dismounts");
                }
                return false;
            }
        }

        private void End()
        {
            _model?.ResetVisual();
            _active = false;
            if (LocalRide == this) LocalRide = null;
            if (_body != null) _body.interpolation = _bodyInterpolation;
            if (_nview.IsValid() && _nview.IsOwner()) _nview.GetZDO().Set(s_pitchBank, Vector3.zero);
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Flight] {name}: rider off, {(_character.IsFlying() ? "in the air" : "on the ground")}");
            MountPillion.LeaveFor(_character, "rider off");   // 0.1.130: whoever rides behind gets off too
        }

        // Another client's view: the owner's bank / pitch from the ZDO, on this copy's Visual child.
        private void CopyRemoteVisual(float dt)
        {
            if (!_remoteInit)
            {
                _remoteInit = true;
                _remoteVisual = transform.Find("Visual");
                if (_remoteVisual != null) _remoteRest = _remoteVisual.localRotation;
            }
            if (_remoteVisual == null) return;
            Vector3 pitchBank = _character.IsFlying() ? _nview.GetZDO().GetVec3(s_pitchBank, Vector3.zero) : Vector3.zero;
            // The same tilt as the rider's FlightModel: about the body's axes (+ pitch nose up, + bank right side down).
            Quaternion want = Quaternion.AngleAxis(-pitchBank.y, Vector3.forward) * Quaternion.AngleAxis(-pitchBank.x, Vector3.right) * _remoteRest;
            _remoteVisual.localRotation = Quaternion.Slerp(_remoteVisual.localRotation, want, 1f - Mathf.Exp(-RemoteSmooth * dt));
        }

        private void OnDestroy()
        {
            MountPillion.LeaveFor(_character, "mount gone");
            if (_active) _model?.ResetVisual();
            if (LocalRide == this) LocalRide = null;
        }

        private static FiresFlyerFlight FlightOf(Sadle sadle)
        {
            var character = sadle != null ? sadle.GetCharacter() : null;
            return character != null ? character.GetComponent<FiresFlyerFlight>() : null;
        }

        // In the air the FlightModel steers; vanilla's ground riding (MoveTowards on the rider's look) would fight it. Reporting
        // "riding" also keeps MonsterAI's own flying behaviour off while the rider is up.
        [HarmonyPatch(typeof(Sadle), nameof(Sadle.UpdateRiding))]
        internal static class HoldGroundRidingInFlight
        {
            [HarmonyPrefix]
            private static bool Prefix(Sadle __instance, ref bool __result)
            {
                var flight = FlightOf(__instance);
                if (flight == null || !flight.InFlight) return true;
                __result = true;
                return false;
            }
        }

        // A flyer looking straight up or down (its AI over or under its target) leaves vanilla SetLookDir a zero yaw vector, and
        // Unity logs "Look rotation viewing vector is zero" each time (round 8: ~110 lines per mounts_fly_test run). For our
        // flyers the look keeps its current heading by a tiny forward nudge; the vertical look itself is unchanged.
        [HarmonyPatch(typeof(Character), nameof(Character.SetLookDir))]
        internal static class KeepHeadingOnVerticalLook
        {
            private const float LogEverySeconds = 10f;
            private static int s_kept;
            private static float s_nextLog;

            [HarmonyPrefix]
            private static void Prefix(Character __instance, ref Vector3 dir, float transitionTime)
            {
                if (transitionTime > 0f) return;
                float total = dir.sqrMagnitude;
                if (total <= 1e-12f || dir.x * dir.x + dir.z * dir.z > total * 1e-8f) return;
                if (__instance == null || __instance.GetComponent<FiresFlyerFlight>() == null) return;
                Vector3 heading = __instance.transform.forward;
                heading.y = 0f;
                if (heading.sqrMagnitude < 1e-6f) heading = Vector3.forward;
                dir += heading.normalized * (Mathf.Sqrt(total) * 0.001f);
                s_kept++;
                if (Time.time < s_nextLog) return;
                s_nextLog = Time.time + LogEverySeconds;
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Flight] look guard: {s_kept} straight up/down look(s) on flyers kept their heading");
                // Round 9 (Fire's hand test): ~20 a second. Once a minute, name who asks (the AI's MoveTowards / LookAt chain).
                if (Time.time < s_nextCaller) return;
                s_nextCaller = Time.time + CallerEverySeconds;
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Flight] look guard: {__instance.name} asked by {Callers()}");
            }

            private const float CallerEverySeconds = 60f;
            private static float s_nextCaller;

            private static string Callers()
            {
                var frames = new System.Diagnostics.StackTrace(2, false).GetFrames();
                if (frames == null) return "?";
                var names = new System.Collections.Generic.List<string>();
                foreach (var f in frames)
                {
                    var m = f.GetMethod();
                    if (m == null) continue;
                    names.Add((m.DeclaringType != null ? m.DeclaringType.Name + "." : "") + m.Name);
                    if (names.Count == 5) break;
                }
                return string.Join(" < ", names);
            }
        }

        // Vanilla stops riding on jump, attack, secondary attack or dodge. On a flyer (ground or air) LMB / MMB are the MOUNT's
        // attacks (0.1.104) and never the rider's swing or a dismount. In the air Space climbs (a double tap locks a hover) and Ctrl
        // lands, so crouch is cleared there too; Ctrl's double tap is the flight model's StepOffRequested. Space and dodge never dismount
        // (Fire 10-08: the double E does, on the ground and in the air).
        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        internal static class FlightKeysOnTheMount
        {
            [HarmonyPrefix]
            private static void Prefix(Player __instance, ref bool jump, ref bool attack, ref bool attackHold,
                ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool block, ref bool blockHold, ref bool crouch, ref bool dodge)
            {
                if (__instance == null || __instance != Player.m_localPlayer) return;
                var flight = FlightOf(__instance.GetDoodadController() as Sadle);
                if (flight == null || !flight._active) return;
                jump = false;
                dodge = false;
                if (MountConfig.FlyerMountAttacks.Value && flight._combat != null)
                {
                    float heading = flight._model != null ? flight._model.Heading : float.NaN;
                    if (attack) flight._combat.Attack(MountCombat.Slot.Main, heading);
                    else if (secondaryAttack) flight._combat.Attack(MountCombat.Slot.Special, heading);
                    else if (block) flight._combat.Shield();
                }
                attack = attackHold = false;
                secondaryAttack = secondaryAttackHold = false;
                // RMB (vanilla's Block) is the mount's bubble shield (Fire 10-08), never the rider's own block.
                block = blockHold = false;
                if (!flight.InFlight) return;
                crouch = false;
            }
        }

        // The rider's LMB / MMB / RMB and Alt + wheel: the shared MountCombat (0.1.105; the same on the Mini Fader).
        private void Update()
        {
            if (!_active) return;
            _combat?.CycleSpecialOnAltWheel();
            if (DoubleUse(ref _lastUse)) _dismountRequested = true;   // done in LateUpdate, after vanilla's Use of the same press
        }
    }
}
