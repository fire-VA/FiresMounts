using HarmonyLib;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// The Mini Fader's ride (Tools\FIRESMOUNTS_LEVELS_AND_CLIMBER.md 3; Fire 10-08 "lets get the fader mount into the next test").
    /// A ground mount, ridden through vanilla Sadle with the HORSE's ground controls (Fire 10-08: "we want fader on the ground to react
    /// like our horse mount ... and then fader gliding gets its flight controls"): reins steering at full speed through a turn, the gait
    /// ramp from a walk to a run (Shift sprints), C toggles walk mode, the horse's speeds ([Mounts.Movement]), no root-motion drag.
    /// While the LOCAL player rides it:
    /// - Space never dismounts. Held on the ground it charges a leap ([Mounts.Fader] LeapChargeSeconds); on release the Fader leaps
    ///   up to LeapHeight high and LeapForward ahead at full charge (a tap: a small hop), playing its own Jump Forward (Jump Left /
    ///   Right with A / D held). A running leap keeps its run speed if that is faster.
    /// - Past a leap's top it GLIDES by itself (Fire: "i assumed it would just glide automatically"), on the flyers' keys: W / S nose
    ///   down (faster, sinks more) / up (slower, sinks less), A / D bank-turn, Q / E turn, Shift faster, Ctrl drops out. Off a ledge,
    ///   Space held while falling starts one. The pose is the jump clip held near its top (synced). Touching ground ends it. Mounts and
    ///   riders take no fall damage (vanilla: only an unattached player does).
    /// - LMB / MMB / RMB / Alt + wheel: the Fader's own attacks at the crosshair and the bubble shield (MountCombat, as on the flyers).
    /// - Wall climbing (0.1.108): W into a surface steeper than 60 degrees (rock, cliff, a built wall), on the ground or in the air, sticks to
    ///   it. W / S climb up / down at [Mounts.Fader] ClimbSpeed, A / D move along it; the body turns to the wall (the Visual tilted nose up,
    ///   synced). -9 stamina a second; Tired, it slides down at 3 m/s. Over the top edge it mantles onto the flat; Space jumps off the wall
    ///   (back and up); S at the bottom steps off. Losing the wall drops it.
    /// The ZDO goes to the rider's client while ridden (vanilla Sadle), so the leap and the climb run where the keys are.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class FiresFaderRide : MonoBehaviour
    {
        private enum Air { None, Leap, Glide, Climb }

        private const float MinCharge = 0.3f;        // held shorter than this: the smallest leap
        private const float MinLeapHeight = 3f, MinLeapForward = 5f;
        private const float SideLeapDegrees = 35f;
        private const float GroundGraceSeconds = 0.3f;   // ground contact this soon after take-off is the take-off itself
        private const float CliffGlideAfter = 0.25f;     // falling this long without a leap lets Space glide
        private const float GlideEase = 12f;             // m/s per second toward GlideSpeed
        private const float GlideFreezeAt = 0.45f;       // the jump clip's normalized time the glide pose holds
        private static readonly int s_gliding = "FiresMounts_Gliding".GetStableHashCode();
        private static readonly int s_climbPitch = "FiresMounts_ClimbPitch".GetStableHashCode();
        private static readonly int s_forwardSpeed = Animator.StringToHash("forward_speed");
        private const float StartSteep = 0.26f;          // a climb starts on a face steeper than 75 degrees (normal.y below this)...
        private const float HoldSteep = 0.5f;            // ...and holds while the face stays steeper than 60 degrees
        private const float WallReach = 0.6f;            // metres past the capsule the wall probe reaches
        private const float WallStick = 0.8f;            // m/s pressed into the wall while climbing
        private const float ClimbSlide = 3f;             // m/s a Tired climber slides down
        private const float WallJumpBack = 6f, WallJumpUp = 8f;
        private const float MantleUp = 4.5f, MantleIn = 3f;
        private const float TiltRate = 360f;             // degrees a second the body turns to / from the wall

        // [worldgen]'s saddle fit (FlyerFactory, saddle_fit.json "fader"): the back bone's path, the seat point and the saddle's forward /
        // up in that bone's local space. Serialized on the prefab, so every instance poses its own seat; [Mounts.Fader] RiderRaise /
        // RiderForward move the rider live.
        public string SeatBonePath = "";
        public Vector3 SeatLocal, SeatForward = Vector3.forward, SeatUp = Vector3.up;
        public float RiderRaise, RiderForward;   // 0.1.117: the fit's rider offset; [Mounts.Fader] RiderRaise / RiderForward add to it
        // 0.1.120: the same ground ride for Eikthyr, Bjorn and the brute, without the Fader's own tricks. Serialized on the prefab.
        public bool Glides = true, Climbs = true, SideLeaps = true;
        public float LeapScale = 1f;
        public string JumpTrigger = "jump_forward";
        public string Label = "Mini Fader";
        // 0.1.122: the water mounts. No jump; out of the water (on ground, not swimming) for SwimOnlyGrace seconds the rider gets off.
        public bool Jumps = true, SwimOnly;
        // 0.1.129 (Fire: "the jump still plays the bite attack animation"): the Fader's Jump Forward / Left / Right clips ARE its leap
        // attack (tagged "attack", the jaws snap), so muting their Hit event (0.1.128) left the bite motion. The ride no longer plays
        // them: the body keeps its locomotion pose in the air. Off by default for every mount on this ride.
        public bool PlayJumpClips;
        // 0.1.128: the double jump and the hold-glide.
        private const float AirJumpHeight = 6f, LeapAttackQuiet = 1.5f;
        private bool _airJumped, _holdGlide;
        private float _leapAt = float.NegativeInfinity, _lastMountAttack = float.NegativeInfinity;
        private const float SwimOnlyGrace = 0.6f, ShoreDepth = 0.6f;
        private WaterVolume _shoreVolume;

        private float WaterDepth()
        {
            Vector3 p = transform.position;
            float surface = Floating.GetWaterLevel(p, ref _shoreVolume);
            float ground = ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(p, out float g) ? g : p.y;
            return surface - ground;
        }
        private float _ashoreSince = -1f;

        private Character _character;
        private ZNetView _nview;
        private Sadle _sadle;
        private Transform _seatBone;
        private Rigidbody _body;
        private RigidbodyInterpolation _bodyInterpolation;
        private Animator _anim;
        private ZSyncAnimation _zanim;
        private MountCombat _combat;
        private MountProgress _progress;
        private bool _active;
        private float _lastUse = -10f;
        private bool _dismountRequested;
        private MonsterAI _ai;
        private float _aiMoveMinAngle;
        private bool _aiSmooth;
        private float _gaitSpeed;
        private bool _walkMode, _lastCrouchHeld = true;
        private const float GaitRampDown = 6f, ShiftSprintRampMult = 4f;   // the horse's (FiresMountController)

        private float _chargeStart = -1f;
        private bool _needRelease;   // Space still down from a glide's landing (or from mounting): it must come up before a new charge
        private Air _air;
        private float _airSince, _fallingSince = -1f, _heading, _peak;
        private Vector3 _takeoff;
        // The leap's launch velocity across the ground, held until it lands or glides (hand test 4: something in vanilla's air step bled
        // a third of it, so leaps fell short of LeapForward). Zero = a plain fall (a released glide, a ledge, a lost wall).
        private Vector3 _leapFlat;
        private bool _autoGlide;   // this leap glides by itself past its top
        private Vector3 _move;
        private bool _frozen;
        private Vector3 _wallNormal;
        private bool _spaceWasDown;
        private Transform _visual;
        private Vector3 _visualRestPos, _bodyCentre;
        private Quaternion _visualRestRot;
        private float _tilt, _nextTiltSync;
        private CapsuleCollider _capsule;
        private Collider[] _visualColliders;   // the bone colliders under the Visual: off while the body is tilted (they'd push it off the wall)
        private bool _visualCollidersOff;

        /// <summary>The Mini Fader the local player rides right now (FlyerFlightHints), or null.</summary>
        internal static FiresFaderRide LocalRide { get; private set; }
        internal bool Airborne => _air != Air.None;
        internal bool Gliding => _air == Air.Glide;
        internal bool Climbing => _air == Air.Climb;
        internal bool Active => _active;
        internal bool Tired => _progress != null && _progress.Tired;
        /// <summary>0..1 while Space charges a leap on the ground, else -1.</summary>
        internal float Charge => _chargeStart < 0f ? -1f : Charge01(Time.time - _chargeStart);

        private void Awake()
        {
            _character = GetComponent<Character>();
            _nview = GetComponent<ZNetView>();
            _sadle = GetComponentInChildren<Sadle>(true);
            _seatBone = string.IsNullOrEmpty(SeatBonePath) ? null : transform.Find(SeatBonePath);
            _body = GetComponent<Rigidbody>();
            _anim = GetComponentInChildren<Animator>(true);
            _zanim = GetComponent<ZSyncAnimation>();
            _combat = _character != null ? new MountCombat(_character, "Fader") : null;
            _progress = GetComponent<MountProgress>();
            _ai = GetComponent<MonsterAI>();
            _capsule = GetComponent<CapsuleCollider>();
            _visual = transform.Find("Visual");
            if (_visual != null) { _visualRestPos = _visual.localPosition; _visualRestRot = _visual.localRotation; }
            // The tilt turns the body about its middle (the capsule's centre, root-local), so the belly ends up against the wall.
            _bodyCentre = _capsule != null ? _capsule.center : Vector3.up;
            _visualColliders = _visual != null ? System.Array.FindAll(_visual.GetComponentsInChildren<Collider>(true), c => c != null && !c.isTrigger) : new Collider[0];
        }

        private bool RiddenByLocalPlayer()
        {
            var player = Player.m_localPlayer;
            return player != null && _sadle != null && _nview.IsOwner() && ReferenceEquals(player.GetDoodadController(), _sadle);
        }

        private void PoseSeat()
        {
            if (_seatBone == null || _sadle == null) return;
            float lossy = Mathf.Max(_seatBone.lossyScale.x, 0.0001f);
            Vector3 local = SeatLocal + SeatUp.normalized * ((RiderRaise + MountConfig.FaderRiderRaise.Value) / lossy)
                            + SeatForward.normalized * ((RiderForward + MountConfig.FaderRiderForward.Value) / lossy);
            // Turned with the body, not the bone, so the walk's spine sway doesn't rock the rider (as on the flyers).
            _sadle.transform.SetPositionAndRotation(_seatBone.TransformPoint(local), transform.rotation);
        }

        // As on the flyers: after this frame's animation the seat is posed and the local rider put on it, so a leap never draws the
        // rider and the mount out of step (vanilla attaches in the player's FixedUpdate, at the last rendered seat).
        private void LateUpdate()
        {
            if (!_active) return;
            if (_dismountRequested) { _dismountRequested = false; Dismount(); return; }
            FiresFlyerFlight.EnsureSeated(_sadle, name, "Fader");
            if (SwimOnly)
            {
                // 0.1.125 (round 25: the serpent was put off in 1.0 m of water, touching the seabed): ashore means the water at the
                // mount is under ShoreDepth deep (the live surface over the ground), not just "on ground".
                bool ashore = _character.IsOnGround() && WaterDepth() < ShoreDepth;
                if (!ashore) _ashoreSince = -1f;
                else if (_ashoreSince < 0f) _ashoreSince = Time.time;
                else if (Time.time - _ashoreSince >= SwimOnlyGrace)
                {
                    _ashoreSince = -1f;
                    FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: reached the shore; the rider gets off (swim-only)");
                    Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, Label + " can't leave the water");
                    Dismount("shore");
                    return;
                }
            }
            FlyerFlightHints.KeepShown(LocalRide == this);
            PoseSeat();
            var player = Player.m_localPlayer;
            if (player != null && _sadle != null && ReferenceEquals(player.GetDoodadController(), _sadle))
                player.transform.SetPositionAndRotation(_sadle.transform.position, _sadle.transform.rotation);
        }

        private void Update()
        {
            if (_nview == null || !_nview.IsValid()) return;
            UpdateGlidePose();
            UpdateClimbPose(Time.deltaTime);
            if (_active) _combat?.CycleSpecialOnAltWheel();
            if (_active && FiresFlyerFlight.DoubleUse(ref _lastUse)) _dismountRequested = true;   // Fire 10-08: double E dismounts
            if (_active)
            {
                // The horse's C: walk mode, from the held state's rising edge (FiresMountController; vanilla's down edge can be swallowed
                // while riding). Tracked in the air too, so a Ctrl held to drop out of a glide doesn't toggle it on landing.
                bool crouchHeld = FiresCore.Flight.FlightKeys.InputOpen && !TypingGate.Typing() && ZInput.GetButton("Crouch");
                if (crouchHeld && !_lastCrouchHeld && _air == Air.None) ToggleWalkMode();
                _lastCrouchHeld = crouchHeld;
            }
            // The walk blend at the climb's speed (vanilla's UpdateWalking sets forward_speed from the ground speed, ~0 on a wall).
            if (_active && _air == Air.Climb && _zanim != null && _body != null) _zanim.SetFloat(s_forwardSpeed, _body.linearVelocity.magnitude);
        }

        private void FixedUpdate()
        {
            if (_character == null || _nview == null || !_nview.IsValid()) return;
            bool ridden = RiddenByLocalPlayer();
            if (ridden && !_active) Begin();
            else if (!ridden && _active) End();
            if (!_active) return;
            PoseSeat();
            // 0.1.127 (Fire's hand test: "Lv 1 (Frightened)" while ridden): Tameable shows Frightened for an alerted MonsterAI; a ridden
            // mount is not on the lookout.
            if (_ai != null && _ai.IsAlerted()) _ai.SetAlerted(false);
            if (SwimOnly) RideHigh();
            StepJump(Time.fixedDeltaTime);
            _combat?.HoldAim();
        }

        // 0.1.127 (Fire's hand test on the Bonemaw: "a little weird to ride underwater", "does not sit high enough to keep the player
        // out of the water"): vanilla holds a swimmer at the liquid level - m_swimDepth. Ridden, the depth is steered so the saddle
        // point stays RideAbove over the live surface (Floating.GetWaterLevel: the tide included), which also ends the dives; the
        // creature's own depth comes back at the dismount.
        private const float RideAbove = 0.35f, RideSteer = 0.15f, RideDepthMin = -4f;
        private float _swimDepthOrig = float.NaN;
        private WaterVolume _rideVolume;

        private void RideHigh()
        {
            if (_sadle == null) return;
            if (float.IsNaN(_swimDepthOrig)) _swimDepthOrig = _character.m_swimDepth;
            float surface = Floating.GetWaterLevel(transform.position, ref _rideVolume);
            float error = surface + RideAbove - _sadle.transform.position.y;
            _character.m_swimDepth = Mathf.Clamp(_character.m_swimDepth - error * RideSteer, RideDepthMin, _swimDepthOrig);
        }

        private void Begin()
        {
            _active = true;
            _air = Air.None;
            _chargeStart = -1f;
            _needRelease = SpaceHeld();
            _walkMode = false;
            _lastCrouchHeld = true;   // a held C at mounting is not a tap
            _gaitSpeed = MountConfig.GaitFloor.Value;
            // Hand test 4: W + A / D "jams up the mount". Vanilla BaseAI.MoveTowards stops (smooth movement: scales to 0) a creature
            // whose wanted direction is over m_moveMinAngle off its facing, and the Fader's is 30 while the reins ask ~32 off. Ridden,
            // it moves at full speed in the reined direction while it turns, as the horse does.
            if (_ai != null) { _aiMoveMinAngle = _ai.m_moveMinAngle; _aiSmooth = _ai.m_smoothMovement; _ai.m_moveMinAngle = 180f; _ai.m_smoothMovement = false; }
            LocalRide = this;
            FlyerFlightHints.Ensure();
            if (_body != null) { _bodyInterpolation = _body.interpolation; _body.interpolation = RigidbodyInterpolation.Interpolate; }
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: rider on");
        }

        private void End()
        {
            _active = false;
            _chargeStart = -1f;
            if (_air != Air.None) SetAir(Air.None);
            if (_ai != null) { _ai.m_moveMinAngle = _aiMoveMinAngle; _ai.m_smoothMovement = _aiSmooth; }
            if (!float.IsNaN(_swimDepthOrig)) { _character.m_swimDepth = _swimDepthOrig; _swimDepthOrig = float.NaN; }
            _walkMode = false;
            if (LocalRide == this) LocalRide = null;
            if (_body != null) _body.interpolation = _bodyInterpolation;
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: rider off");
            MountPillion.LeaveFor(_character, "rider off");   // 0.1.130: whoever rides behind gets off too
        }

        // Fire 10-08: "double tap e to dismount" (a single E never does: vanilla's stops are refused, FiresFlyerFlight.KeepRiderInFlight).
        private void Dismount(string how = "double E")
        {
            var player = Player.m_localPlayer;
            if (player == null || !ReferenceEquals(player.GetDoodadController(), _sadle)) return;
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: rider dismounted {(Airborne ? "in the air" : "on the ground")} ({how})");
            FiresFlyerFlight.KeepRiderInFlight.Allow = true;
            try { player.StopDoodadControl(); }
            finally { FiresFlyerFlight.KeepRiderInFlight.Allow = false; }
        }

        /// <summary>
        /// The horse's gait ramp (FiresMountController.UpdateGaitRamp) on the Fader's ground ride: forward eases from a walk up to a run
        /// ([Mounts.Movement] WalkSpeed / RunSpeed, the mount level's speed on top), Shift sweeps faster, release eases back; C's walk
        /// mode holds a walk. Before vanilla UpdateWalking reads the speed (FiresMountGaitRampPatch).
        /// </summary>
        internal void UpdateGaitRamp(float dt)
        {
            if (!_active || _air != Air.None || _character == null) return;
            float floor = MountConfig.GaitFloor.Value;
            float levelMul = _progress != null ? _progress.SpeedMul : 1f;
            float walk = Mathf.Max(floor, MountConfig.WalkSpeed.Value * levelMul);
            float run = Mathf.Max(walk, MountConfig.RunSpeed.Value * levelMul);
            if (_walkMode) run = walk;
            bool driving = _character.m_moveDir.sqrMagnitude > 0.02f;
            bool running = _character.m_run;
            float target = !driving ? floor : (running ? run : walk);
            float rate = MountConfig.GaitRampRate.Value * (running && MountConfig.ShiftSprint.Value ? ShiftSprintRampMult : 1f);
            _gaitSpeed = Mathf.MoveTowards(_gaitSpeed, target, (driving ? rate : GaitRampDown) * dt);
            _character.m_speed = _gaitSpeed;
            _character.m_walkSpeed = _gaitSpeed;
            _character.m_runSpeed = _gaitSpeed;
            _character.m_turnSpeed = MountConfig.TurnSpeed.Value;
            _character.m_runTurnSpeed = MountConfig.TurnSpeed.Value * 2f;
        }

        internal bool IsBody(Character c) => c != null && c == _character;

        internal void ToggleWalkMode()
        {
            _walkMode = !_walkMode;
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, _walkMode ? "Walk mode — tap C to release" : "Walk mode off");
        }

        private void OnDestroy()
        {
            MountPillion.LeaveFor(_character, "mount gone");
            if (LocalRide == this) LocalRide = null;
            s_airborne.Remove(_character);
        }

        // The Fader's jump clips are tagged "attack", so vanilla feeds their root motion into the body (Character.AddRootMotion, applied
        // when it beats the velocity) and could cut a leap short. While one of ours leaps or glides, its root motion is dropped.
        private static readonly System.Collections.Generic.HashSet<Character> s_airborne = new System.Collections.Generic.HashSet<Character>();

        [HarmonyPatch(typeof(Character), nameof(Character.AddRootMotion))]
        internal static class NoRootMotionInTheAir
        {
            [HarmonyPrefix]
            private static void Prefix(Character __instance, ref Vector3 vel)
            {
                if (s_airborne.Count != 0 && s_airborne.Contains(__instance)) vel = Vector3.zero;
            }
        }

        private static bool SpaceHeld() =>
            FiresCore.Flight.FlightKeys.InputOpen && !TypingGate.Typing() && (ZInput.GetButton("Jump") || ZInput.GetButton("JoyJump"));

        private static float Charge01(float held)
        {
            float full = Mathf.Max(MinCharge + 0.05f, MountConfig.FaderLeapChargeSeconds.Value);
            return Mathf.Clamp01((held - MinCharge) / (full - MinCharge));
        }

        private void StepJump(float dt)
        {
            bool held = SpaceHeld();
            bool pressed = held && !_spaceWasDown;
            _spaceWasDown = held;
            bool ground = _character.IsOnGround() || _character.IsSwimming();
            Vector3 v = _body != null ? _body.linearVelocity : Vector3.zero;

            if (_air == Air.Climb) { ClimbStep(dt, pressed); return; }
            if (!Jumps) { _chargeStart = -1f; return; }
            if (Climbs && _move.z > 0.5f && !Tired && MountConfig.FaderClimb.Value && !_character.IsSwimming()
                && WallAhead(FlatForward(), StartSteep, out Vector3 wall)) { StartClimb(wall); return; }

            if (_air == Air.None)
            {
                if (ground)
                {
                    _fallingSince = -1f;
                    if (_needRelease) { _needRelease = held; return; }
                    if (held && _chargeStart < 0f) _chargeStart = Time.time;
                    else if (!held && _chargeStart >= 0f) { Leap(Time.time - _chargeStart, v); _chargeStart = -1f; }
                    return;
                }
                _chargeStart = -1f;
                // Off a cliff (no leap): Space glides once it has been falling a moment.
                if (v.y < -2f) { if (_fallingSince < 0f) _fallingSince = Time.time; }
                else _fallingSince = -1f;
                if (Glides && held && _fallingSince >= 0f && Time.time - _fallingSince >= CliffGlideAfter) StartGlide("off a ledge");
                return;
            }

            _peak = Mathf.Max(_peak, transform.position.y);
            if (ground && Time.time - _airSince > GroundGraceSeconds) { Land(); _needRelease = held; return; }
            _character.SetMoveDir(Vector3.zero);   // no air control toward the ride speed: the leap is ballistic, the glide steered here
            var flightKeys = FiresCore.Flight.FlightKeys.ReadLocal();
            if (_air == Air.Leap)
            {
                // 0.1.128 (Fire: a double jump; hold Space after the air jump to glide slowly, let go to stop): Space pressed again in the
                // leap is the air jump (once a leap); after it, Space held while falling glides. The leap no longer glides by itself (it
                // would start the glide before the air jump could happen).
                if (pressed && !_airJumped && _body != null)
                {
                    _airJumped = true;
                    float g = Mathf.Max(1f, -Physics.gravity.y);
                    float up = Mathf.Sqrt(2f * g * AirJumpHeight * LeapScale);
                    _body.linearVelocity = new Vector3(v.x, up, v.z);
                    _peak = transform.position.y;
                    FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: jump state leap -> air jump (up {up:0.0} m/s, ~{AirJumpHeight * LeapScale:0.0} m)");
                    return;
                }
                if (Glides && held && _airJumped && v.y < 0f) { _leapFlat = Vector3.zero; _holdGlide = true; StartGlide("Space held after the air jump"); return; }
                if (_leapFlat.sqrMagnitude > 0.01f && _body != null) _body.linearVelocity = _leapFlat + Vector3.up * v.y;
                return;
            }
            if (_holdGlide && !held)
            {
                _holdGlide = false; _leapFlat = Vector3.zero; _autoGlide = false; SetAir(Air.Leap);
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: jump state glide -> falling (Space released)");
                return;
            }
            if (flightKeys.Descend) { _holdGlide = false; _leapFlat = Vector3.zero; _autoGlide = false; SetAir(Air.Leap); FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: glide dropped (Ctrl), falling"); return; }
            Glide(v, dt, in flightKeys);
        }

        private void Leap(float held, Vector3 v)
        {
            if (_body == null) return;
            float t = Charge01(held);
            // 0.1.106 (the plan's numbers): a leap costs 10 stamina, 25 at full charge; a Tired Fader can't leap.
            float cost = 10f + 15f * t;
            if (_progress != null && !_progress.Spend(cost, "leap"))
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "Too tired to leap");
                return;
            }
            float g = Mathf.Max(1f, -Physics.gravity.y);
            float height = Mathf.Lerp(MinLeapHeight, Mathf.Max(MinLeapHeight, MountConfig.FaderLeapHeight.Value), t) * LeapScale;
            float forward = Mathf.Lerp(MinLeapForward, Mathf.Max(MinLeapForward, MountConfig.FaderLeapForward.Value), t) * LeapScale;
            float up = Mathf.Sqrt(2f * g * height);
            float flight = 2f * up / g;
            string trigger = JumpTrigger;
            float side = 0f;
            if (SideLeaps && _move.x < -0.5f) { side = -SideLeapDegrees; trigger = "jump_left"; }
            else if (SideLeaps && _move.x > 0.5f) { side = SideLeapDegrees; trigger = "jump_right"; }
            Vector3 fwd = transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 dir = Quaternion.AngleAxis(side, Vector3.up) * fwd;
            float speed = Mathf.Max(forward / flight, Vector3.Dot(new Vector3(v.x, 0f, v.z), dir));
            _character.ForceJump(dir * speed + Vector3.up * up);
            _leapFlat = dir * speed;
            _autoGlide = false;   // 0.1.128: the glide is Space held after the air jump now
            _airJumped = !Glides; // the air jump is the Mini Fader's (Fire); Eikthyr / Bjorn / the brute keep one jump
            _holdGlide = false;
            _leapAt = Time.time;
            if (PlayJumpClips && !string.IsNullOrEmpty(trigger)) _zanim?.SetTrigger(trigger);
            _heading = Quaternion.LookRotation(dir).eulerAngles.y;
            _takeoff = transform.position;
            _peak = transform.position.y;
            SetAir(Air.Leap);
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: leap ({trigger}, charge {t * 100f:0}% after {held:0.00} s): up {up:0.0} m/s for ~{height:0.0} m, {speed:0.0} m/s ahead for ~{speed * flight:0} m");
        }

        private void StartGlide(string how)
        {
            if (_air == Air.None)
            {
                _takeoff = transform.position;
                _peak = transform.position.y;
                Vector3 fwd = transform.forward; fwd.y = 0f;
                _heading = fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd).eulerAngles.y : transform.eulerAngles.y;
            }
            if (PlayJumpClips && _anim != null && !InJumpClip(_anim.GetCurrentAnimatorStateInfo(0))) _zanim?.SetTrigger("jump_forward");
            SetAir(Air.Glide);
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: gliding ({how}, {AboveGround():0.0} m up)");
        }

        // The flyers' keys (FiresCore FlightKeys, FireHeli): W / S nose down / up, A / D bank-turn, Q / E turn, Shift faster.
        private void Glide(Vector3 v, float dt, in FiresPossession.FlightInput keys)
        {
            _heading += (Mathf.Clamp(keys.Bank, -1f, 1f) * MountConfig.FaderGlideTurn.Value + Mathf.Clamp(keys.Yaw, -1f, 1f) * 90f) * dt;
            Vector3 dir = Quaternion.Euler(0f, _heading, 0f) * Vector3.forward;
            float nose = Mathf.Clamp(keys.Pitch, -1f, 1f);   // +1 nose up (S), -1 nose down (W)
            float speedMul = (keys.Gas ? 1.3f : 1f) * (nose < 0f ? 1.5f : nose > 0f ? 0.6f : 1f);
            float sinkMul = nose < 0f ? 2f : nose > 0f ? 0.6f : 1f;
            Vector3 flat = Vector3.MoveTowards(new Vector3(v.x, 0f, v.z), dir * (MountConfig.FaderGlideSpeed.Value * speedMul), GlideEase * dt);
            float sink = Mathf.Max(0.1f, MountConfig.FaderGlideSink.Value) * sinkMul;
            _body.linearVelocity = flat + Vector3.up * Mathf.Max(v.y, -sink);
            _character.SetLookDir(dir);
            _progress?.GlideTick(dt);
        }

        private void Land()
        {
            _airJumped = false; _holdGlide = false;
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: jump state {_air} -> ground");
            Vector3 d = transform.position - _takeoff;
            float flatDist = new Vector2(d.x, d.z).magnitude;
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: landed after the {(_air == Air.Glide ? "glide" : "leap")}: {flatDist:0} m from take-off, peak {_peak - _takeoff.y:0.0} m above it, {Time.time - _airSince:0.0} s in the air");
            SetAir(Air.None);
        }

        private void SetAir(Air air)
        {
            if (air != Air.None && _air == Air.None) _airSince = Time.time;
            if (air != Air.Leap) _leapFlat = Vector3.zero;   // only a leap holds its launch speed
            _air = air;
            if (air == Air.None) s_airborne.Remove(_character); else s_airborne.Add(_character);
            if (_nview != null && _nview.IsValid() && _nview.IsOwner()) _nview.GetZDO().Set(s_gliding, air == Air.Glide);
        }

        // -- the wall climb (0.1.108) ----------------------------------------------------------------------------------------------

        private Vector3 FlatForward()
        {
            Vector3 f = transform.forward; f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        }

        // The nearest steep surface (not the mount, not its rider) along 'dir' from the body's middle, within the capsule plus WallReach:
        // |normal.y| below 'steep'. A voxel cliff is faceted, so a shallow facet in the way is skipped, not taken as "no wall".
        private bool WallAhead(Vector3 dir, float steep, out Vector3 normal)
        {
            normal = Vector3.zero;
            float scale = transform.lossyScale.y;
            float radius = _capsule != null ? _capsule.radius * scale : 0.3f;
            Vector3 from = transform.position + Vector3.up * (_bodyCentre.y * scale);
            int mask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "Default_small");
            RaycastHit[] hits = Physics.RaycastAll(from, dir, radius + WallReach, mask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || (_body != null && hit.collider.attachedRigidbody == _body)) continue;
                if (hit.collider.GetComponentInParent<Player>() != null) continue;
                if (Mathf.Abs(hit.normal.y) >= steep) continue;
                normal = hit.normal;
                return true;
            }
            return false;
        }

        private void StartClimb(Vector3 normal)
        {
            if (_air == Air.None) { _takeoff = transform.position; _peak = transform.position.y; }
            _chargeStart = -1f;
            _wallNormal = normal;
            SetAir(Air.Climb);   // clears the glide flag too, so a held glide pose plays on into the walk blend the climb uses
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: climbing (wall {Vector3.Angle(Vector3.up, normal):0} degrees, {AboveGround():0.0} m up, climb drain {MountConfig.FaderClimbDrain.Value:0.#}/s)");
        }

        private void ClimbStep(float dt, bool spacePressed)
        {
            Vector3 into = -new Vector3(_wallNormal.x, 0f, _wallNormal.z).normalized;
            if (into.sqrMagnitude < 0.5f) into = FlatForward();
            Vector3 toWall = -_wallNormal.normalized;   // the probe goes into the face itself, so a face that leans back keeps contact
            _peak = Mathf.Max(_peak, transform.position.y);

            if (spacePressed)
            {
                Vector3 jump = -into * WallJumpBack + Vector3.up * WallJumpUp;
                _character.ForceJump(jump);
                _leapFlat = new Vector3(jump.x, 0f, jump.z);
                _autoGlide = true;
                if (PlayJumpClips) _zanim?.SetTrigger("jump_forward");
                _heading = Quaternion.LookRotation(-into).eulerAngles.y;
                SetAir(Air.Leap);
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: wall jump ({AboveGround():0.0} m up)");
                return;
            }
            if (_move.z < -0.5f && _character.IsOnGround())
            {
                SetAir(Air.None);
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: stepped off the wall at its foot");
                return;
            }
            if (!WallAhead(toWall, HoldSteep, out Vector3 normal) && !WallAhead(into, HoldSteep, out normal))
            {
                if (_move.z > 0.5f && !Tired)
                {
                    // Over the top edge: up and in, then the leap's ballistic fall onto the flat.
                    _body.linearVelocity = Vector3.up * MantleUp + into * MantleIn;
                    _leapFlat = into * MantleIn;
                    _heading = Quaternion.LookRotation(into).eulerAngles.y;
                    SetAir(Air.Leap);
                    FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: mantled over the top ({transform.position.y - _takeoff.y:0.0} m above the climb's start)");
                }
                else
                {
                    _leapFlat = Vector3.zero;
                    SetAir(Air.Leap);
                    FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {name}: lost the wall, falling");
                }
                return;
            }
            _wallNormal = normal;
            // Along the face: "up" is straight up the slope of this facet, "right" across it.
            Vector3 upAlong = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
            if (upAlong.sqrMagnitude < 0.5f) upAlong = Vector3.up;
            Vector3 right = Vector3.Cross(upAlong, -normal).normalized;
            float speed = Mathf.Max(0.5f, MountConfig.FaderClimbSpeed.Value);
            Vector3 v = Tired
                ? -upAlong * ClimbSlide
                : upAlong * (Mathf.Clamp(_move.z, -1f, 1f) * speed) + right * (Mathf.Clamp(_move.x, -1f, 1f) * speed * 0.8f);
            v += -normal * WallStick;
            v.y += -Physics.gravity.y * dt;   // gravity still acts on the body this step (vanilla walking keeps it on): cancel it
            _body.linearVelocity = v;
            _character.SetMoveDir(Vector3.zero);
            _character.SetLookDir(into);
            _progress?.FlightTick(FiresPossession.FlightState.Clinging, dt);
        }

        // Every peer: the owner tilts the body to the wall while climbing (and back after) and syncs the angle; the others copy it.
        private void UpdateClimbPose(float dt)
        {
            if (_visual == null) return;
            float want = _nview.IsOwner() ? (_air == Air.Climb ? 90f : 0f) : _nview.GetZDO().GetFloat(s_climbPitch, 0f);
            if (Mathf.Approximately(_tilt, want) && Mathf.Approximately(want, 0f)) return;
            _tilt = Mathf.MoveTowards(_tilt, want, TiltRate * dt);
            Quaternion r = Quaternion.Euler(-_tilt, 0f, 0f);   // nose up
            _visual.localRotation = r * _visualRestRot;
            _visual.localPosition = _visualRestPos + (_bodyCentre - r * _bodyCentre);
            // The bone colliders ride the Visual: tilted, they'd swing into the wall and shove the body off it, so they rest until it's upright.
            bool off = _tilt > 1f;
            if (off != _visualCollidersOff)
            {
                _visualCollidersOff = off;
                foreach (var c in _visualColliders) if (c != null) c.enabled = !off;
            }
            if (_nview.IsOwner() && Time.time >= _nextTiltSync)
            {
                _nextTiltSync = Time.time + 0.1f;
                _nview.GetZDO().Set(s_climbPitch, want);
            }
        }

        // The height over what's below (a ray down: the solid-height lookup failed on the voxel pad, "-1.0 m up" in hand test 4).
        private float AboveGround()
        {
            int mask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "Default_small");
            foreach (RaycastHit hit in Physics.RaycastAll(transform.position + Vector3.up * 0.2f, Vector3.down, 300f, mask, QueryTriggerInteraction.Ignore))
                if (hit.collider != null && (_body == null || hit.collider.attachedRigidbody != _body) && hit.collider.GetComponentInParent<Player>() == null)
                    return Mathf.Max(0f, hit.distance - 0.2f);
            return ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(transform.position, out float h) ? transform.position.y - h : -1f;
        }

        private static bool InJumpClip(AnimatorStateInfo st) => st.IsName("Jump Forward") || st.IsName("Jump Left") || st.IsName("Jump Right");

        // Every peer: while the owner glides (ZDO flag), the jump clip holds near its top; it plays on as soon as the glide ends.
        private void UpdateGlidePose()
        {
            if (_anim == null) return;
            bool gliding = _nview.GetZDO().GetBool(s_gliding, false);
            if (!gliding)
            {
                if (_frozen) { _anim.speed = 1f; _frozen = false; }
                return;
            }
            if (_frozen) return;
            AnimatorStateInfo st = _anim.GetCurrentAnimatorStateInfo(0);
            if (InJumpClip(st) && st.normalizedTime >= GlideFreezeAt) { _anim.speed = 0f; _frozen = true; }
        }

        private static FiresFaderRide RideOf(Sadle sadle)
        {
            var character = sadle != null ? sadle.GetCharacter() : null;
            return character != null ? character.GetComponent<FiresFaderRide>() : null;
        }

        /// <summary>A Mini Fader (the reins steering patch drives it like the horse).</summary>
        internal static bool IsFader(Character c) => c != null && c.GetComponent<FiresFaderRide>() != null;

        // Vanilla stops riding on jump, attack, secondary attack or dodge. On the Mini Fader, Space is the leap / glide (read in FixedUpdate)
        // and LMB / MMB / RMB are the mount's attacks and shield. Dodge never dismounts; the double E does (Fire 10-08).
        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        internal static class FaderKeysOnTheMount
        {
            [HarmonyPrefix]
            private static void Prefix(Player __instance, Vector3 movedir, ref bool jump, ref bool attack, ref bool attackHold,
                ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool block, ref bool blockHold, ref bool dodge, ref bool crouch)
            {
                if (__instance == null || __instance != Player.m_localPlayer) return;
                var ride = RideOf(__instance.GetDoodadController() as Sadle);
                if (ride == null || !ride._active) return;
                ride._move = movedir;
                if (MountConfig.FlyerMountAttacks.Value && ride._combat != null)
                {
                    float heading = ride.transform.eulerAngles.y;
                    if (attack || secondaryAttack) ride._lastMountAttack = Time.time;
                    if (attack) ride._combat.Attack(MountCombat.Slot.Main, heading);
                    else if (secondaryAttack) ride._combat.Attack(MountCombat.Slot.Special, heading);
                    else if (block) ride._combat.Shield();
                }
                attack = attackHold = false;
                secondaryAttack = secondaryAttackHold = false;
                block = blockHold = false;
                jump = false;
                dodge = false;
                crouch = false;   // C is the horse's walk mode on the ground (Update), Ctrl the glide's drop in the air
            }
        }

        // 0.1.128 (Fire: "every jump plays his bite attack as if Mouse1 was pressed"): the Fader's Jump Left / Jump Right clips carry a
        // "Hit" animation event (vanilla's leap is an attack), and CharacterAnimEvent's Hit runs Humanoid.OnAttackTrigger: the attack
        // the mount used last, the bite. Ridden, in the air or just after a leap, a Hit with no mount attack pressed is swallowed.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnAttackTrigger))]
        internal static class NoBiteOnJump
        {
            [HarmonyPrefix]
            private static bool Prefix(Humanoid __instance)
            {
                var ride = __instance != null ? __instance.GetComponent<FiresFaderRide>() : null;
                if (ride == null || !ride._active) return true;
                bool jumping = ride.Airborne || Time.time - ride._leapAt < LeapAttackQuiet;
                if (!jumping || Time.time - ride._lastMountAttack < LeapAttackQuiet) return true;
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Fader] {ride.name}: a jump clip's Hit event swallowed (no attack pressed)");
                return false;
            }
        }

        // In a leap or a glide this component steers; vanilla's ground riding (MoveTowards on the reins) would pull the velocity back to
        // the ride speed by air control. Reporting "riding" keeps MonsterAI's own movement off too.
        [HarmonyPatch(typeof(Sadle), nameof(Sadle.UpdateRiding))]
        internal static class HoldGroundRidingInTheAir
        {
            [HarmonyPrefix]
            private static bool Prefix(Sadle __instance, ref bool __result)
            {
                var ride = RideOf(__instance);
                if (ride == null || !ride.Airborne) return true;
                __result = true;
                return false;
            }
        }
    }
}
