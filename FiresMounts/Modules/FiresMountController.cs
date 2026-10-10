using System;
using System.Collections;
using FiresCore.Logging;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// Thin enhancement layer over the VANILLA Sadle ride (the dumped MagicalMounts blueprint proved vanilla
    /// doodad riding is the correct architecture when the prefab is built right — skeleton-mounted seat,
    /// lobotomized AI, character layer, in-place clips). This adds the mounted JUMP (vanilla mounts can't) via
    /// FiresMountJumpPatch.
    /// </summary>
    public class FiresMountController : MonoBehaviour
    {
        private Character _character;
        private MountProgress _progress;
        private ZNetView _nview;
        private ZSyncAnimation _zanim;
        private Animator _anim;
        private float _lastJump;
        private float _rideSince = -1f;
        private Collider[] _ownColliders;
        private Sadle _sadle;
        private MonsterAI _ai;

        // Hitch: a dismounted-and-hitched horse stands planted (no graze wander) until mounted or commanded to Follow.
        private const string HitchedKey = "fires_hitched";
        private float _lastRear;
        private bool _walkMode;   // tap C while riding: the horse holds its slow walk (resets on dismount)
        private bool _lastCrouchHeld;   // for our own rising-edge detect on ZInput.GetButton("Crouch")

        // Gait audio (client-only): a looping spatial source on the horse plays walk/trot/run by the synced gait speed.
        private MountLoopSource _footAudio;
        private bool _audioChecked;
        private float _audioQuietSince = -1f;
        private string _gaitClip;

        // Vanilla per-biome footstep FX (dust/splash + material SFX), driven at a gait rhythm and layered under the loop.
        private FootStep _footStep;
        private bool _footFxReady, _footAlt;
        private float _footFxDist, _lastFootFx;
        private static bool _footFxLogged;

        // 30s ride-summary accumulators (VerboseLog-gated) — replace the old per-0.5s RIDE/CONTACT log spam. Sampled
        // per LateUpdate while ridden; one banner every 30s and one on dismount, then reset. Owner-only.
        private float _mvWindow, _mvSpeedSum, _mvSpeedMax, _mvDist, _mvGaitLo = 999f, _mvGaitHi;
        private int _mvSamples, _mvGround, _mvStuck, _mvJumps, _mvRun;
        private readonly int[] _mvGait = new int[6];   // idle, walk, trot, canter, gallop, sprint (by speed)
        private bool _mvWasRiding, _mvWasGrounded = true;

        // Gait speed ramp (adapted from FiresValcast NaturalWalk): holding forward eases the horse from a walk up
        // through the gaits to a gallop, so forward_speed SWEEPS the blend tree (walk->trot->canter->gallop) instead
        // of snapping to run speed and galloping-in-place. Released -> decays toward walk; the ride decel then blends
        // the gaits back down. Owner-only (drives the authoritative physics).
        private float _gaitSpeed = 1.5f;
        private const float GaitRampDown = 6f;      // m/s shed per second on release (horse also decelerates physically)
        private const float ShiftSprintRampMult = 4f;   // Shift climbs this much faster than plain forward, but still SWEEPS the gaits (no hard snap)

        // The sprint gait's authored ground speed (blend tree top child). At/below it the blend plays clips at
        // their natural cadence (timeScale=1, set in FiresMountBuilder), so hooves match the ground 0..10 m/s.
        // Above it we scale global playback so a faster-than-gallop config speed still keeps its hooves matched.
        private const float TopBlendSpeed = 10f;

        private void Awake()
        {
            _character = GetComponent<Character>();
            _nview = GetComponent<ZNetView>();
            _zanim = GetComponent<ZSyncAnimation>();
            _anim = GetComponentInChildren<Animator>(true);
            _ownColliders = GetComponentsInChildren<Collider>(true);
            _sadle = GetComponentInChildren<Sadle>(true);
            _ai = GetComponent<MonsterAI>();
            try { EnsureSaddleItem(); } catch { }
        }

        // The authored prefab leaves Tameable.m_saddleItem null (SaddleLox is a shared game asset the bundle can't own).
        // Wire it from ObjectDB at spawn — retried from Update until ObjectDB is ready (registration ran too early).
        private bool _saddleWired;
        private void EnsureSaddleItem()
        {
            if (_saddleWired) return;
            var tame = GetComponent<Tameable>();
            if (tame == null) { _saddleWired = true; return; }
            if (tame.m_saddleItem != null) { _saddleWired = true; return; }
            if (ObjectDB.instance == null) return;   // not ready yet -> retry next frame
            var sp = ObjectDB.instance.GetItemPrefab("SaddleLox");
            if (sp != null) { tame.m_saddleItem = sp.GetComponent<ItemDrop>(); _saddleWired = true; }
        }

        // Sample this frame into the 30s ride-summary accumulators (VerboseLog-gated). Emits one banner every 30s
        // while ridden and one on dismount, then resets — the clean replacement for the old per-tick RIDE/CONTACT spam.
        private void MovementSample()
        {
            if (_character == null) return;
            bool riding = _nview != null && _nview.IsValid() && _nview.IsOwner()
                          && MountConfig.VerboseLog.Value && _sadle != null && _sadle.HaveValidUser();
            if (!riding)
            {
                if (_mvWasRiding && _mvSamples > 0) EmitMovementSummary(true);
                else if (_mvSamples > 0) ResetMv();
                _mvWasRiding = false;
                return;
            }
            _mvWasRiding = true;

            float dt = Time.deltaTime;
            Vector3 v = _character.GetVelocity(); v.y = 0f;
            float spd = v.magnitude;
            _mvSamples++;
            _mvWindow += dt;
            _mvSpeedSum += spd;
            if (spd > _mvSpeedMax) _mvSpeedMax = spd;
            _mvDist += spd * dt;
            bool grounded = _character.IsOnGround();
            if (grounded) _mvGround++;
            if (_mvWasGrounded && !grounded) _mvJumps++;
            _mvWasGrounded = grounded;
            float fs = 0f; try { if (_anim != null) fs = _anim.GetFloat("forward_speed"); } catch { }
            if (fs > 1f && spd < 0.5f) _mvStuck++;   // commanded forward but not translating = the old ground-pin (want 0)
            _mvGait[GaitBucket(spd)]++;
            if (_gaitSpeed < _mvGaitLo) _mvGaitLo = _gaitSpeed;   // ramp range: Lo..Hi spread = the commanded speed IS sweeping
            if (_gaitSpeed > _mvGaitHi) _mvGaitHi = _gaitSpeed;
            if (_character.m_run) _mvRun++;                        // how often run/Shift was engaged (drives the fast ramp)

            if (_mvWindow >= 30f) EmitMovementSummary(false);
        }

        // Speed -> gait bucket, aligned with the blend-tree thresholds (walk 1.5 / trot 3.5 / canter 6 / gallop 8 / sprint 10).
        private static int GaitBucket(float s)
            => s < 0.5f ? 0 : s < 2.5f ? 1 : s < 4f ? 2 : s < 6.5f ? 3 : s < 9f ? 4 : 5;

        private void ResetMv()
        {
            _mvWindow = 0f; _mvSpeedSum = 0f; _mvSpeedMax = 0f; _mvDist = 0f; _mvGaitLo = 999f; _mvGaitHi = 0f;
            _mvSamples = 0; _mvGround = 0; _mvStuck = 0; _mvJumps = 0; _mvRun = 0;
            for (int i = 0; i < _mvGait.Length; i++) _mvGait[i] = 0;
        }

        // One boxed banner summarizing the ride window: avg/max speed, distance, gait time %, ground %, jumps, and
        // "pins" (frames commanded-but-stuck — the movement-health number, should stay 0). Rendered via Core's Banner.
        private void EmitMovementSummary(bool dismounted)
        {
            try
            {
                int n = Mathf.Max(1, _mvSamples);
                float avg = _mvSpeedSum / n;
                int gPct = Mathf.RoundToInt(100f * _mvGround / n);
                int rPct = Mathf.RoundToInt(100f * _mvRun / n);
                float lo = _mvGaitLo > 900f ? 0f : _mvGaitLo;
                int[] p = new int[6];
                for (int i = 0; i < 6; i++) p[i] = Mathf.RoundToInt(100f * _mvGait[i] / n);
                string tag = name.Replace("(Clone)", "");
                var lines = new[]
                {
                    new[] { new Banner.Segment($"──── FIRES MOUNTS · {tag} · ride {(dismounted ? "(dismount)" : "30s")} ────", ConsoleColor.Cyan) },
                    new[] { new Banner.Segment($"  speed   avg {avg:0.0}   max {_mvSpeedMax:0.0} m/s    dist {_mvDist:0} m", ConsoleColor.Yellow) },
                    new[] { new Banner.Segment($"  gaits   idle {p[0]}  walk {p[1]}  trot {p[2]}  canter {p[3]}  gallop {p[4]}  sprint {p[5]}  (%)", ConsoleColor.Gray) },
                    new[] { new Banner.Segment($"  ramp    gaitSpeed {lo:0.0}..{_mvGaitHi:0.0} m/s    run {rPct}%    (a wide spread = the blend IS sweeping)", ConsoleColor.DarkCyan) },
                    new[]
                    {
                        new Banner.Segment($"  ground  {gPct}%    jumps {_mvJumps}    ", ConsoleColor.DarkGray),
                        new Banner.Segment($"pins {_mvStuck}", _mvStuck > 0 ? ConsoleColor.Red : ConsoleColor.Green),
                    },
                };
                Banner.Print(lines);
            }
            catch (Exception ex) { try { FiresMountsPlugin.Log.LogWarning($"[FiresMounts] ride summary failed: {ex.Message}"); } catch { } }
            ResetMv();
        }

        // Rider-collision fix: an attached rider (kinematic, parented to our saddle) embeds in our body collider and
        // wedges the horse. Vanilla's AttachStart collision-ignore is set on the RIDER'S client; the owner that
        // SIMULATES the mount (dedi/host) never got it, so it rams the kinematic rider and pins in place — only a jump
        // breaks the contact. Ignore any attached player that collides with us: that's the rider.
        private void OnCollisionStay(Collision c)
        {
            try
            {
                var rider = c.collider.GetComponentInParent<Character>();
                if (rider != null && rider != _character && rider.IsPlayer() && rider.IsAttached() && _ownColliders != null)
                    foreach (var own in _ownColliders)
                        if (own != null) Physics.IgnoreCollision(own, c.collider, true);
            }
            catch { }
        }

        // Overspeed only — and through ZSyncAnimation.SetSpeed, the sanctioned API (a raw animator.speed write is
        // stomped every Update by ZSyncAnimation re-applying the ZDO value; SetSpeed persists AND replicates).
        private void MatchAnimToSpeed()
        {
            if (_zanim == null || _anim == null || _character == null) return;
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            Vector3 vel = _character.GetVelocity();
            vel.y = 0f;
            float v = vel.magnitude;
            // forward_speed is fed to the blend NORMALIZED (speed/RunSpeed), so the chosen gait clip is authored for
            // ~1/RunSpeed of the real ground speed. Scale global playback by RunSpeed/TopBlendSpeed so the hooves keep
            // pace with the ground at ANY configured top speed (constant while moving; 1 at rest so idle is untouched).
            float scale = Mathf.Max(TopBlendSpeed, MountConfig.RunSpeed.Value) / TopBlendSpeed;
            float target = (v > 0.5f ? scale : 1f) * MountConfig.AnimSpeedTune.Value;
            target = Mathf.Clamp(target, 0.25f, 3f);
            if (Mathf.Abs(_anim.speed - target) > 0.02f) _zanim.SetSpeed(target);
        }

        // Ease the horse's move speed from a walk up to a gallop while it's driven forward, and back down on release,
        // so the gait blend tree sweeps naturally. Called from a Character.UpdateWalking PREFIX (correct timing: set
        // BEFORE the speed is read that frame). Sets all three speed fields so whichever gait branch UpdateWalking
        // takes reads the same ramped value. m_moveDir>0 = the rider is commanding forward; RunSpeed config = the cap.
        public void UpdateGaitRamp(float dt)
        {
            if (_character == null || _nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            float floor = MountConfig.GaitFloor.Value;
            // 0.1.106: +1.5% a mount level (MountProgress), on top of the configured walk / run.
            if (_progress == null) _progress = GetComponent<MountProgress>();
            float levelMul = _progress != null ? _progress.SpeedMul : 1f;
            float walk = Mathf.Max(floor, MountConfig.WalkSpeed.Value * levelMul);
            float run = Mathf.Max(walk, MountConfig.RunSpeed.Value * levelMul);
            if (_walkMode)
            {
                // hold the WALK gait: the blend's walk child sits at 1/6 of the normalized range (7 evenly spaced
                // children, idle..sprint), so RunSpeed/6 is the ground speed where the horse actually walks.
                float walkGait = Mathf.Max(floor, MountConfig.RunSpeed.Value / 6f);
                walk = Mathf.Min(walk, walkGait);
                run = Mathf.Min(run, walkGait);   // Shift can't break out of walk mode
            }
            bool driving = _character.m_moveDir.sqrMagnitude > 0.02f;
            bool running = _character.m_run;   // Shift -> RunSpeed; plain forward -> WalkSpeed (Shift now actually changes speed)
            float target = !driving ? floor : (running ? run : walk);
            // Shift also climbs faster (a quick sweep, not a snap) when ShiftSprint is on; plain forward eases up gently.
            float rate = MountConfig.GaitRampRate.Value * (running && MountConfig.ShiftSprint.Value ? ShiftSprintRampMult : 1f);
            _gaitSpeed = Mathf.MoveTowards(_gaitSpeed, target, (driving ? rate : GaitRampDown) * dt);
            _character.m_speed = _gaitSpeed;
            _character.m_walkSpeed = _gaitSpeed;
            _character.m_runSpeed = _gaitSpeed;
        }

        // The gait blend runs on a NORMALIZED parameter (idle 0 -> sprint 1; the blend tree uses automatic 0..1
        // thresholds). Vanilla UpdateWalking writes RAW m/s into forward_speed, so at any RunSpeed above ~1 the blend
        // pins to sprint (that's why "anything over 10" was always the sprint clip). Re-map raw speed to speed/RunSpeed
        // so the gaits spread across the WHOLE configured range: walk/trot/canter below, sprint only at the very top.
        // Called from the ZSyncAnimation.SetFloat prefix so the animator AND the synced ZDO carry the normalized value.
        public float NormalizeForwardSpeed(float raw)
        {
            float max = Mathf.Max(0.1f, MountConfig.RunSpeed.Value);
            return Mathf.Clamp01(Mathf.Max(0f, raw) / max);
        }

        // E = dismount (attack/dodge are combat, not dismount — FiresMountJumpPatch consumes them). Uses the
        // proper vanilla detach flow (StopDoodadControl -> Sadle.OnUseStop -> release RPC + AttachStop). Grace
        // period so the mounting E press can't insta-dismount.
        private void Update()
        {
            try
            {
                if (!_saddleWired) EnsureSaddleItem();   // retry saddle-item wire until ObjectDB is ready
                var p = Player.m_localPlayer;
                bool ridingThis = p != null && p.GetDoodadController() is Sadle s && s.GetCharacter() == _character;
                // reset _lastCrouchHeld to true (assume held) so mounting while crouch happens to be down can't fire a
                // spurious rising edge on the first ride frame — a real toggle needs a fresh press after mounting.
                if (!ridingThis) { _rideSince = -1f; _walkMode = false; _lastCrouchHeld = true; return; }
                if (_rideSince < 0f) _rideSince = Time.time;
                // Tap Crouch: walk mode — the horse holds its slow walk no matter how long forward is held. Crouch is
                // NOT in vanilla's dismount set (jump/attack/dodge), so it's a free rein control. Detect the rising
                // edge from the HELD state ourselves (the way vanilla reads crouch, PlayerController.cs:97) instead of
                // GetButtonDown: the down-edge can be consumed elsewhere in the frame while mounted, which silently
                // swallowed the tap (the log showed zero toggles even mid-ride). GetButton tracks whatever key the
                // player has bound to Crouch, so a rebind still works.
                bool crouchHeld = ZInput.instance != null && !TypingGate.Typing() && ZInput.GetButton("Crouch");
                if (MountConfig.VerboseLog.Value && crouchHeld && !_lastCrouchHeld)
                    FiresMountsPlugin.Log.LogInfo($"WALKMODE input: crouchHeld=1 down={(ZInput.instance != null && ZInput.GetButtonDown("Crouch"))} riding=1");
                if (crouchHeld && !_lastCrouchHeld)
                {
                    _walkMode = !_walkMode;
                    if (MountConfig.VerboseLog.Value)
                        FiresMountsPlugin.Log.LogInfo($"WALKMODE toggle -> {_walkMode} on '{_character?.name}' owner={(_nview != null && _nview.IsValid() ? _nview.IsOwner().ToString() : "n/a")}");
                    p.Message(MessageHud.MessageType.TopLeft, _walkMode ? "Walk mode — tap C to release" : "Walk mode off");
                }
                _lastCrouchHeld = crouchHeld;
                if (Time.time - _rideSince > 0.5f && ZInput.instance != null && !TypingGate.Typing() && ZInput.GetButtonDown("Use"))
                {
                    p.StopDoodadControl();
                    PlaceRiderBeside(p);
                    // Auto-hitch: the horse stands where you left it (the rider's client owns the horse while riding,
                    // so the ZDO write sticks). Pet it to Follow — that releases the hitch (see AiLeashTick).
                    if (MountConfig.AutoHitch.Value && _nview != null && _nview.IsValid())
                        _nview.GetZDO().Set(HitchedKey, true);
                }
            }
            catch { }
        }

        // SINGLE-WRITER leash (owner-side): a seated rider is the only mover — idle AI never wanders under them; a
        // hitched horse stands planted. Mounting or a Follow command (petting) releases the hitch.
        private void AiLeashTick()
        {
            if (_ai == null || _nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            bool ridden = _sadle != null && _sadle.HaveValidUser();
            bool hitched = _nview.GetZDO().GetBool(HitchedKey, false);
            if (ridden)
            {
                if (hitched) _nview.GetZDO().Set(HitchedKey, false);
                _ai.ResetRandomMovement();
                return;
            }
            if (!hitched) return;
            if (_ai.GetFollowTarget() != null) { _nview.GetZDO().Set(HitchedKey, false); return; }
            _ai.StopMoving();
            _ai.ResetRandomMovement();
        }

        // Vanilla AttachStop teleports the rider to attachPoint.TransformPoint(detachOffset). Our seat hangs under a
        // scale-~100 Malbers node, so a non-zero detachOffset is multiplied ~100x and launches the rider tens of metres
        // up (fall damage on landing). MountFactory zeroes the saddle detachOffset so no dismount launches; here we also
        // step the rider off to the horse's side at ground level and clear the fall tracker for a clean E-dismount.
        private void PlaceRiderBeside(Player p)
        {
            if (p == null || _character == null) return;
            try
            {
                Vector3 spot = _character.transform.position + _character.transform.right * 1.6f;
                p.transform.position = spot;
                if (p.m_body != null) { p.m_body.linearVelocity = Vector3.zero; p.m_body.useGravity = true; }
                p.m_maxAirAltitude = spot.y;   // cancel the (now-removed) drop so no fall damage
            }
            catch { }
        }

        // Called by FiresMountJumpPatch when the rider presses Jump (input consumed so vanilla never dismounts).
        // Moving: a physics hop. STATIONARY: the horse REARS instead (H_Attack_Front_Legs) — animation only, no damage.
        public bool TryRiderJump()
        {
            if (!MountConfig.EnableRiderJump.Value || _character == null) return false;
            if (Time.time - _lastJump < 0.75f) return false;
            if (!_character.IsOnGround()) return false;
            _lastJump = Time.time;
            if (_character.m_moveDir.sqrMagnitude < 0.02f)
            {
                if (_zanim != null) _zanim.SetTrigger("rear");   // replicates to every client
                return true;
            }
            _character.Jump();   // physics + jump animator trigger, replicated via ZSyncAnimation
            return true;
        }

        // Rider attack: rear up and strike — Asksvin-grade damage (read live from the vanilla Asksvin's attack item)
        // applied in a short arc in front of the horse as the hooves come down. Players and tames are never hit.
        public bool TryRiderAttack()
        {
            if (_character == null || _nview == null || !_nview.IsValid()) return false;
            if (Time.time - _lastRear < 1.4f) return false;
            if (!_character.IsOnGround()) return false;
            _lastRear = Time.time;
            if (_zanim != null) _zanim.SetTrigger("rear");
            StartCoroutine(RearStrike());
            return true;
        }

        private IEnumerator RearStrike()
        {
            yield return new WaitForSeconds(0.45f);   // hooves-down moment of H_Attack_Front_Legs
            if (_character == null) yield break;
            Vector3 center = _character.transform.position + _character.transform.forward * 1.8f + Vector3.up * 1.2f;
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c == _character || c.IsPlayer() || c.IsTamed()) continue;
                if (Vector3.Distance(c.transform.position + Vector3.up * 0.5f, center) > 2.2f) continue;
                var hit = new HitData();
                hit.m_damage = RearDamages();
                hit.m_point = c.transform.position + Vector3.up * 0.8f;
                hit.m_dir = (c.transform.position - _character.transform.position).normalized;
                hit.m_pushForce = 60f;
                hit.SetAttacker(_character);
                c.Damage(hit);
            }
        }

        // The Asksvin's own attack damages, read once from its first default attack item ("same attack damage as the
        // asksvins" — it IS a cloned Asksvin). Fallback ≈ its bite if the prefab/item shape ever changes.
        private static HitData.DamageTypes? s_rearDamages;
        private static HitData.DamageTypes RearDamages()
        {
            if (s_rearDamages.HasValue) return s_rearDamages.Value;
            var dmg = new HitData.DamageTypes { m_blunt = 60f };
            try
            {
                var ask = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Asksvin") : null;
                var hum = ask != null ? ask.GetComponent<Humanoid>() : null;
                if (hum != null && hum.m_defaultItems != null)
                    foreach (var it in hum.m_defaultItems)
                    {
                        var id = it != null ? it.GetComponent<ItemDrop>() : null;
                        if (id == null) continue;
                        dmg = id.m_itemData.m_shared.m_damages;
                        break;
                    }
            }
            catch { }
            s_rearDamages = dmg;
            return dmg;
        }

        // Overspeed animator scaling — above the sprint gait's authored speed, scale playback so hooves keep up
        // with a faster-than-gallop config speed (owner-only, deadbanded so it's not per-frame ZDO churn). At/below
        // sprint speed it's a no-op: the blend tree's timeScale=1 clips already match the ground.
        private void LateUpdate()
        {
            try { MatchAnimToSpeed(); } catch { }
            // Live model-yaw knob: set the animator/Visual node's yaw AFTER the animator runs (it doesn't overwrite its
            // own node's transform, so this holds). Lets the rider dial the visual until it faces the move direction;
            // the value is then baked into FiresMountBuilder. Overrides the baked yaw (default 90 == the current bake).
            try { if (_anim != null) _anim.transform.localRotation = Quaternion.Euler(0f, MountConfig.ModelYaw.Value, 0f); } catch { }
            try { MovementSample(); } catch { }
            try { AnimDiag(); } catch { }
            try { MountAudioTick(); } catch { }
            try { FootFxTick(); } catch { }
            try { AiLeashTick(); } catch { }
        }

        // Vanilla footstep FX: the effect list is copied from the Player prefab (identical on every client + the dedi, so
        // the networked "Step" index resolves the same everywhere). The OWNER drives OnFoot on a distance-paced, rate-
        // capped rhythm and it broadcasts; every peer spawns the biome-appropriate dust/splash + footstep SFX at the
        // horse, layering with our gait loop. Silent when parked or airborne.
        private void FootFxTick()
        {
            if (!MountConfig.FootstepFX.Value) return;
            if (MountConfig.GaitLoops.Value) return;   // the loops carry their own hoof hits; vanilla steps on top doubled them
            if (_footStep == null) { _footStep = GetComponent<FootStep>(); if (_footStep == null) return; }
            if (!_footFxReady) { _footFxReady = EnsureFootEffects(); if (!_footFxReady) return; }
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;   // owner drives; receivers just handle RPC_Step
            if (_character == null || !_character.IsOnGround()) { _footFxDist = 0f; return; }
            Vector3 v = _character.GetVelocity(); v.y = 0f;
            float speed = v.magnitude;
            if (speed < 0.6f) return;
            float norm = _anim != null ? Mathf.Clamp01(_anim.GetFloat("forward_speed")) : 0f;
            float stride = Mathf.Lerp(1.3f, 3.0f, norm);   // longer stride at faster gaits
            _footFxDist += speed * Time.deltaTime;
            if (_footFxDist < stride || Time.time - _lastFootFx < 0.2f) return;   // per stride, capped ~5/s (vanilla c_MinFootstepInterval)
            _footFxDist = 0f; _lastFootFx = Time.time;
            if (_footStep.m_feet != null && _footStep.m_feet.Length >= 2)
            { _footStep.OnFoot(_footAlt ? "FiresFootFront" : "FiresFootBack"); _footAlt = !_footAlt; }   // alternate hooves
            else _footStep.OnFoot();
        }

        // Copy the Player prefab's per-biome StepEffect list onto our FootStep. Runs on every peer (owner + receivers +
        // dedi) so RPC_Step's effect index is valid everywhere. Returns true once the list is in place.
        private bool EnsureFootEffects()
        {
            try
            {
                if (_footStep.m_effects != null && _footStep.m_effects.Count > 0) return true;
                var zs = ZNetScene.instance;
                var player = zs != null ? zs.GetPrefab("Player") : null;
                var pfs = player != null ? player.GetComponentInChildren<FootStep>(true) : null;
                if (pfs == null || pfs.m_effects == null || pfs.m_effects.Count == 0)
                {
                    if (!_footFxLogged && player != null)
                    { _footFxLogged = true; FiresMountsPlugin.Log.LogWarning($"[FiresMounts] footstep FX: no Player FootStep effects (footStep={pfs != null})"); }
                    return false;
                }
                _footStep.m_effects = pfs.m_effects;
                if (!_footFxLogged) { _footFxLogged = true; FiresMountsPlugin.Log.LogInfo($"[FiresMounts] footstep FX ready: copied {pfs.m_effects.Count} player step-effects."); }
                return true;
            }
            catch { return false; }
        }

        // Gait audio: on every CLIENT (skipped on the dedicated server), a looping spatial source on the horse plays the
        // walk/trot/run loop chosen by the SYNCED normalized forward_speed (0..1), so the rider and nearby players hear
        // the hooves. Attached lazily on first tick. Volume/pitch rise with the gait; silent when stopped or airborne.
        private void MountAudioTick()
        {
            if (!_audioChecked)
            {
                _audioChecked = true;
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                    _footAudio = MountLoopSource.Attach(gameObject);
            }
            if (_footAudio == null || _anim == null || _character == null) return;
            if (!MountConfig.GaitLoops.Value) { _footAudio.SetVolume(0f); _footAudio.Stop(); return; }
            float norm = Mathf.Clamp01(_anim.GetFloat("forward_speed"));   // synced 0..1 gait (see NormalizeForwardSpeed)
            // Echo fix (Fire 10-07): a one-frame ground-contact flicker or a speed dip used to Stop+Play the loop, restarting
            // the clip from its start over the old tail. Only stop after 0.3 s of stillness/air; keep playing through blips.
            if (norm <= 0.06f || !_character.IsOnGround())
            {
                if (_audioQuietSince < 0f) _audioQuietSince = Time.time;
                if (Time.time - _audioQuietSince >= 0.3f) { _footAudio.SetVolume(0f); _footAudio.Stop(); }
                return;
            }
            _audioQuietSince = -1f;
            // Hysteresis: the gait clip only changes 0.05 past a threshold, so speed jitter at a boundary can't swap
            // (and restart) the clip every frame.
            float up = _gaitClip == "walk" ? 0.40f : 0.75f, down = _gaitClip == "run" ? 0.65f : 0.30f;
            string gait = _gaitClip;
            if (gait == null) gait = norm < 0.35f ? "walk" : (norm < 0.7f ? "trot" : "run");
            else if (gait != "run" && norm >= up) gait = gait == "walk" && norm < 0.75f ? "trot" : "run";
            else if (gait != "walk" && norm <= down) gait = gait == "run" && norm > 0.30f ? "trot" : "walk";
            _gaitClip = gait;
            object clip = MountAudio.GetClip(gait) ?? MountAudio.GetClip("walk");
            if (clip == null) return;
            _footAudio.SetClip(clip);
            _footAudio.Play();
            _footAudio.SetVolume(Mathf.Lerp(0.65f, 1f, norm) * MountConfig.FootstepVolume.Value);
            _footAudio.SetPitch(0.95f + norm * 0.2f);
        }

        // Diagnostic (VerboseLog only): once/sec while the LOCAL player rides THIS horse, dump the numbers that decide
        // the gait-blend question — the animator's actual forward_speed param, my commanded gait speed, the physics
        // velocity, the current state, and the two top-weighted blend clips. If animFwd sweeps 0..10 but clips stay on
        // one gait, the blend tree is broken; if animFwd itself stays binary, forward_speed isn't reaching the animator.
        private float _animDiagT = -1f;
        private void AnimDiag()
        {
            if (!MountConfig.VerboseLog.Value || _anim == null || _character == null || _nview == null || !_nview.IsValid()) return;
            var p = Player.m_localPlayer;
            if (p == null || !(p.GetDoodadController() is Sadle sd) || sd.GetCharacter() != _character) return;
            if (_animDiagT >= 0f && Time.time - _animDiagT < 1f) return;
            _animDiagT = Time.time;
            float vel = _character.GetVelocity().magnitude;
            float animFwd = _anim.GetFloat("forward_speed");
            var st = _anim.GetCurrentAnimatorStateInfo(0);
            var ci = _anim.GetCurrentAnimatorClipInfo(0);
            string c1 = "-", c2 = "-"; float w1 = 0f, w2 = 0f;
            foreach (var e in ci)
            {
                if (e.clip == null) continue;
                if (e.weight > w1) { c2 = c1; w2 = w1; c1 = e.clip.name; w1 = e.weight; }
                else if (e.weight > w2) { c2 = e.clip.name; w2 = e.weight; }
            }
            FiresMountsPlugin.Log.LogInfo($"ANIMDIAG owner={_nview.IsOwner()} walkMode={_walkMode} vel={vel:0.0} gait={_gaitSpeed:0.0} animFwd={animFwd:0.0} " +
                $"moveDir2={_character.m_moveDir.sqrMagnitude:0.00} run={_character.m_run} " +
                $"state={(st.IsName("Movement") ? "Movement" : "0x" + st.fullPathHash.ToString("X"))} clips=[{c1}:{w1:0.00} {c2}:{w2:0.00}] aspd={_anim.speed:0.00}");
        }
    }

    /// <summary>
    /// Lets the horse run over anything lower than its belly WITHOUT losing ground support: on contact, a
    /// collider is ignored only when it (a) isn't terrain, (b) isn't a character, (c) isn't supporting us from
    /// below (no upward contact normal — floors/bridges/ground always keep collision), and (d) its top is below
    /// the belly line. Low rocks/stumps/fences stop mattering; trees, walls and anything tall still block.
    /// </summary>
    public class FiresMountObstacleRoller : MonoBehaviour
    {
        private Collider[] _own;
        private readonly System.Collections.Generic.HashSet<Collider> _ignored =
            new System.Collections.Generic.HashSet<Collider>();
        private int _terrainLayer;

        private void Awake()
        {
            _own = GetComponentsInChildren<Collider>(true);
            _terrainLayer = LayerMask.NameToLayer("terrain");
        }

        private void OnCollisionEnter(Collision c) { try { Evaluate(c); } catch { } }
        private void OnCollisionStay(Collision c) { try { Evaluate(c); } catch { } }

        private void Evaluate(Collision c)
        {
            var other = c.collider;
            if (other == null || _ignored.Contains(other)) return;
            if (other.gameObject.layer == _terrainLayer) return;
            if (other.GetComponentInParent<Character>() != null) return;   // never phase through creatures/players

            // Read by index: the contacts property hands back a fresh array on every access, and this runs per
            // collision per frame while a mount is touching anything.
            for (int i = 0; i < c.contactCount; i++)
                if (c.GetContact(i).normal.y > 0.5f) return;               // it's holding us up — keep it solid

            float bellyWorld = transform.position.y + MountConfig.BellyClearance.Value * transform.lossyScale.y;
            if (other.bounds.max.y > bellyWorld) return;                   // too tall — a real obstacle

            _ignored.Add(other);
            foreach (var own in _own)
                if (own != null) Physics.IgnoreCollision(own, other, true);
        }
    }
}
