using HarmonyLib;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// Mount levels + stamina (Tools\FIRESMOUNTS_LEVELS_AND_CLIMBER.md 1-2; Fire 10-08: "levels + stamina in 0.1.106 with your plan
    /// numbers as the start"). On every mount (the horse, the mini Moder, the drake, the Mini Fader).
    /// - Level + XP live in the mount's ZDO (FiresMounts_Level 1..20, FiresMounts_Xp), so a raised mount stays raised. XP to the next level
    ///   = 50 x L^1.6. XP: 1 per 25 m ridden on the ground, 1 per 3 s ridden in the air, kills by its own attacks (x0.5) or by its rider
    ///   while mounted (x0.25) at the victim's max health / 10, 5 per feed (once a minute). Only the ZDO owner writes; other peers send
    ///   the XP to it by RPC. [Mounts.Levels] XpRate scales all of it.
    /// - Each level above 1: stamina pool +6%, stamina regen +4%, ground and flight speed +1.5%, health +5%, attack damage +3% (linear,
    ///   so L20 = x2.14 pool, +28.5% speed). Flight ceiling 40 m at L1 up to 100 m at L20. L10+: Shift in the air boosts (+25% speed,
    ///   drains 15 a second instead of 10). L20: a gold name.
    /// - Stamina is vanilla Sadle's (its ZDO value; vanilla's mount HUD shows it). While the local player rides, this owns the rate:
    ///   standing +15 / walking +8 a second on the ground (vanilla's run drain stays), and the flight's drain by state. At 0 the mount
    ///   is Tired (no climb or boost; a flyer sinks and lands, the Fader can't leap) until it is back to 20%.
    /// </summary>
    public class MountProgress : MonoBehaviour
    {
        internal const int MaxLevel = 20;
        private const float RecoverAt = 0.2f;            // Tired until the bar is back to this share
        private const float StandRegen = 15f, WalkRegen = 8f;
        private const float GroundMetersPerXp = 25f, AirSecondsPerXp = 3f;
        private const float FeedXp = 5f, FeedEverySeconds = 60f;
        private const float VanillaBlock = 0.05f;        // Sadle's regen timer held just above 0: vanilla regen stays off while we own it
        private const string RpcAddXp = "FiresMounts_AddXp";
        private static readonly int s_level = "FiresMounts_Level".GetStableHashCode();
        private static readonly int s_xp = "FiresMounts_Xp".GetStableHashCode();
        private static readonly System.Collections.Generic.Dictionary<Character, MountProgress> s_live =
            new System.Collections.Generic.Dictionary<Character, MountProgress>();

        private Character _character;
        private ZNetView _nview;
        private Sadle _sadle;
        private float _baseWalk, _baseRun, _baseSpeed, _baseFlyFast, _baseFlySlow, _baseRegen;
        private int _applied = -1;
        private Vector3 _lastPos;
        private bool _hasLastPos;
        private float _groundMeters, _airSeconds, _nextFeedXp, _nextApply;
        private bool _tired, _boosting;

        internal bool Tired => _tired;
        internal int Level => _nview != null && _nview.IsValid() ? Mathf.Clamp(_nview.GetZDO().GetInt(s_level, 1), 1, MaxLevel) : 1;
        internal float Xp => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetFloat(s_xp, 0f) : 0f;
        internal float SpeedMul => Gain(0.015f);
        internal float DamageMul => Gain(0.03f);
        internal float Ceiling => 40f + 60f * (Level - 1) / (MaxLevel - 1);
        private float Gain(float perLevel) => Enabled ? 1f + perLevel * (Level - 1) : 1f;
        private static bool Enabled => MountConfig.LevelsEnabled == null || MountConfig.LevelsEnabled.Value;

        internal static float XpToNext(int level) => 50f * Mathf.Pow(Mathf.Max(1, level), 1.6f);

        private void Awake()
        {
            _character = GetComponent<Character>();
            _nview = GetComponent<ZNetView>();
            _sadle = GetComponentInChildren<Sadle>(true);
            if (_character != null)
            {
                _baseWalk = _character.m_walkSpeed; _baseRun = _character.m_runSpeed; _baseSpeed = _character.m_speed;
                _baseFlyFast = _character.m_flyFastSpeed; _baseFlySlow = _character.m_flySlowSpeed;
            }
            if (_sadle != null) _baseRegen = _sadle.m_staminaRegen;
            if (_nview != null && _nview.IsValid()) _nview.Register<float, string>(RpcAddXp, RPC_AddXp);
            if (_character != null) s_live[_character] = this;
        }

        private void OnDestroy()
        {
            if (_character != null) s_live.Remove(_character);
        }

        private static MountProgress Of(Character c) => c != null && s_live.TryGetValue(c, out var p) ? p : null;

        /// <summary>One of our mounts (every one carries a MountProgress): a dictionary lookup, for patches that run every frame.</summary>
        internal static bool IsOurMount(Character c) => c != null && s_live.ContainsKey(c);

        // 0.1.110 (hand test 4 follow-up): the size is the config's, not the size the mount was born at. Jere's Infinity Hammer / World
        // Edit Commands keep every object's spawn scale in its ZDO and put it back on load, so a Mini Fader born at 0.18 stayed 0.18 after
        // FaderScale went to 0.25 ("lossy 18" in round 13). Every peer resizes it; the owner rewrites the kept scale too.
        private void Start()
        {
            float want = ConfiguredScale();
            if (want <= 0f || _nview == null || !_nview.IsValid() || Mathf.Abs(transform.localScale.x - want) < 0.001f) return;
            float was = transform.localScale.x;
            transform.localScale = Vector3.one * want;
            if (_nview.IsOwner())
            {
                ZDO zdo = _nview.GetZDO();
                zdo.Set(ZDOVars.s_scaleScalarHash, want);
                if (zdo.GetVec3(ZDOVars.s_scaleHash, Vector3.zero) != Vector3.zero) zdo.Set(ZDOVars.s_scaleHash, Vector3.one * want);
            }
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Levels] {name}: resized from x{was:0.###} (kept from its spawn) to the configured x{want:0.###}");
        }

        /// <summary>
        /// A creature mount's configured size: its registered prefab's root scale (FlyerFactory scales the vanilla root by [Mounts.Flyers]
        /// ModerScale / DrakeScale, [Mounts.Fader] FaderScale at boot). -1 for the horse, which sizes itself (HorseScale).
        /// </summary>
        private float ConfiguredScale()
        {
            string pn = global::Utils.GetPrefabName(gameObject);
            if (!pn.StartsWith(MountNames.MiniFaderPrefab) && !pn.StartsWith(MountNames.MiniModerPrefab) && !pn.StartsWith(MountNames.DrakePrefab)) return -1f;
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(pn) : null;
            return prefab != null ? prefab.transform.localScale.x : -1f;
        }

        private void Update()
        {
            if (_nview == null || !_nview.IsValid() || Time.time < _nextApply) return;
            _nextApply = Time.time + 1f;
            ApplyLevel(true);   // cheap; also undoes the horse's live tuning, which resets the pool to [Mounts.Taming] MaxStamina
        }

        // Every peer: the pool (the HUD reads it locally), regen and speeds (the owner simulates; the others keep them for a hand-over).
        // The owner also sets the max health in the ZDO. The horse's gait ramp reads SpeedMul itself (it rewrites its speeds each tick).
        internal void ApplyLevel(bool force)
        {
            int level = Level;
            if (!force && level == _applied) return;
            _applied = level;
            if (_sadle != null)
            {
                _sadle.m_maxStamina = Pool();
                _sadle.m_staminaRegen = _baseRegen * Gain(0.04f);
            }
            if (_character != null && GetComponent<FiresMountController>() == null)
            {
                float m = SpeedMul;
                _character.m_walkSpeed = _baseWalk * m; _character.m_runSpeed = _baseRun * m; _character.m_speed = _baseSpeed * m;
            }
            ApplyFlySpeed();
            // Health only above level 1, so a level-1 mount keeps vanilla's own max health (world level, star level) untouched.
            if (level > 1 && _nview.IsOwner() && _character != null && !_character.IsDead())
            {
                float want = _character.GetMaxHealthBase() * Mathf.Max(1, _character.GetLevel()) * Gain(0.05f);
                if (Mathf.Abs(_character.GetMaxHealth() - want) > 0.5f)
                {
                    bool full = _character.GetHealth() >= _character.GetMaxHealth() - 0.5f;
                    _character.SetMaxHealth(want);
                    if (full) _character.SetHealth(want);
                }
            }
        }

        private void ApplyFlySpeed()
        {
            if (_character == null) return;
            float m = SpeedMul * (_boosting ? 1.25f : 1f);
            _character.m_flyFastSpeed = _baseFlyFast * m;
            _character.m_flySlowSpeed = _baseFlySlow * m;
        }

        /// <summary>The level-1 pool by mount ([Mounts.Levels]; the horse keeps [Mounts.Taming] MaxStamina), times the level's gain.</summary>
        internal float Pool()
        {
            string pn = global::Utils.GetPrefabName(gameObject);
            float basePool = pn.StartsWith(MountNames.MiniModerPrefab) ? MountConfig.PoolModer.Value
                : pn.StartsWith(MountNames.DrakePrefab) ? MountConfig.PoolDrake.Value
                : pn.StartsWith(MountNames.MiniFaderPrefab) ? MountConfig.PoolFader.Value
                : MountConfig.MaxStamina.Value;
            return basePool * Gain(0.06f);
        }

        private bool RiddenByLocalPlayer()
        {
            var player = Player.m_localPlayer;
            return player != null && _sadle != null && _nview != null && _nview.IsValid() && _nview.IsOwner()
                   && ReferenceEquals(player.GetDoodadController(), _sadle);
        }

        private void FixedUpdate()
        {
            if (_character == null || !RiddenByLocalPlayer()) { _hasLastPos = false; return; }
            float dt = Time.fixedDeltaTime;
            Vector3 pos = transform.position;
            bool air = _character.IsFlying() || (GetComponent<FiresFaderRide>()?.Airborne ?? false);
            if (_hasLastPos && !air && _character.IsOnGround())
            {
                Vector3 d = pos - _lastPos; d.y = 0f;
                float m = d.magnitude;
                if (m < 20f) _groundMeters += m;   // a teleport is not a ride
                if (_groundMeters >= GroundMetersPerXp) { AddXp(Mathf.Floor(_groundMeters / GroundMetersPerXp), "riding"); _groundMeters %= GroundMetersPerXp; }
            }
            if (air)
            {
                _airSeconds += dt;
                if (_airSeconds >= AirSecondsPerXp) { AddXp(Mathf.Floor(_airSeconds / AirSecondsPerXp), "flying"); _airSeconds %= AirSecondsPerXp; }
            }
            _lastPos = pos;
            _hasLastPos = true;
            if (!air) GroundStamina(dt);
            else if (_sadle != null && !_character.IsFlying() && Enabled)
                _sadle.m_staminaRegenTimer = Mathf.Max(_sadle.m_staminaRegenTimer, VanillaBlock);   // a Fader leap: no vanilla regen mid-air
        }

        // Riding on the ground: vanilla's run drain (it resets Sadle's regen timer to 1 s) stays; otherwise our regen replaces vanilla's.
        private void GroundStamina(float dt)
        {
            if (_sadle == null || !Enabled) return;
            _boosting = false;
            if (_sadle.m_staminaRegenTimer > 0.5f) return;   // vanilla just spent some (running, swimming): its own 1 s delay holds
            Vector3 v = _character.GetVelocity(); v.y = 0f;
            AddStamina((v.magnitude > 0.5f ? WalkRegen : StandRegen) * Gain(0.04f) * dt);
        }

        /// <summary>The flyer's drain by its model's state, after each FlightModel step while the local player rides (FiresFlyerFlight).</summary>
        /// <summary>0.1.128: the expected range from full at this level, by the drains above and the creature's fly speeds (Debug).</summary>
        internal string RangeLine()
        {
            float pool = Pool();
            float cruise = _character != null ? _character.m_flySlowSpeed : 0f, fast = _character != null ? _character.m_flyFastSpeed : 0f;
            return $"Lv {Level}, pool {pool:0}: cruise (W) {pool / 2.5f:0} s ~{pool / 2.5f * cruise:0} m at {cruise:0.0} m/s; Shift {pool / 6f:0} s ~{pool / 6f * fast:0} m at {fast:0.0} m/s; hover {pool / 2f:0} s";
        }

        internal void FlightTick(FiresPossession.FlightState state, float dt)
        {
            if (!Enabled || _sadle == null) return;
            bool boost = state == FiresPossession.FlightState.Powered && Level >= 10 && !_tired;
            if (boost != _boosting) { _boosting = boost; ApplyFlySpeed(); }
            float rate;
            switch (state)
            {
                // 0.1.128 (Fire: "much more stamina", ~200 m+ of sustained flight from full at level 1): the drains are lower and the
                // flyers' level-1 pools bigger (PoolModer 260 / PoolDrake 220, from 120 / 90).
                case FiresPossession.FlightState.Climbing: rate = -8f; break;
                case FiresPossession.FlightState.Powered:
                case FiresPossession.FlightState.Boosting: rate = boost ? -10f : -6f; break;
                case FiresPossession.FlightState.Glide: rate = -2.5f; break;  // FireHeli's Glide = W cruise without Shift
                case FiresPossession.FlightState.Hover: rate = -2f; break;
                case FiresPossession.FlightState.Descending: rate = -1f; break;
                // 0.1.129 (Fire: "must NOT lose stamina while climbing"): [Mounts.Fader] ClimbDrain, 0 by default (was 9 a second).
                case FiresPossession.FlightState.Clinging: rate = -Mathf.Max(0f, MountConfig.FaderClimbDrain.Value); break;
                case FiresPossession.FlightState.Diving: rate = 0f; break;
                default: return;                                              // Grounded: the ground rules
            }
            AddStamina(rate * dt);
        }

        /// <summary>The Mini Fader's glide (+2 a second) while it is in the air.</summary>
        internal void GlideTick(float dt)
        {
            if (Enabled && _sadle != null) AddStamina(2f * Gain(0.04f) * dt);
        }

        /// <summary>Pays a one-off cost (the Fader's leap). False (nothing paid) when the bar holds less, or the mount is Tired.</summary>
        internal bool Spend(float cost, string what)
        {
            if (!Enabled || _sadle == null) return true;
            if (_tired || _sadle.GetStamina() < cost)
            {
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Levels] {name}: too tired to {what} ({_sadle.GetStamina():0} of {cost:0} stamina)");
                return false;
            }
            AddStamina(-cost);
            return true;
        }

        private void AddStamina(float delta)
        {
            if (!_nview.IsOwner()) return;
            float max = Mathf.Max(1f, _sadle.GetMaxStamina());
            float now = _sadle.GetStamina();
            float s = Mathf.Clamp(now + delta, 0f, max);
            if (Mathf.Abs(s - now) > 1e-4f) _nview.GetZDO().Set(ZDOVars.s_stamina, s);
            _sadle.m_staminaRegenTimer = delta < 0f ? 1f : VanillaBlock;
            if (!_tired && s <= 0f)
            {
                _tired = true;
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Levels] {name}: Tired (stamina 0 of {max:0}, Lv {Level})");
                Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, $"{_character.GetHoverName()} is tired");
            }
            else if (_tired && s >= max * RecoverAt)
            {
                _tired = false;
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Levels] {name}: rested ({s:0} of {max:0})");
            }
        }

        // -- XP ------------------------------------------------------------------------------------------------------------------

        /// <summary>Adds XP from any peer: the owner writes it; others send it to the owner.</summary>
        internal void AddXp(float amount, string why)
        {
            if (!Enabled || amount <= 0f || _nview == null || !_nview.IsValid()) return;
            amount *= Mathf.Max(0f, MountConfig.LevelsXpRate.Value);
            if (_nview.IsOwner()) RPC_AddXp(0L, amount, why);
            else _nview.InvokeRPC(RpcAddXp, amount, why);
        }

        private void RPC_AddXp(long sender, float amount, string why)
        {
            if (!_nview.IsOwner()) return;
            ZDO zdo = _nview.GetZDO();
            int level = Mathf.Clamp(zdo.GetInt(s_level, 1), 1, MaxLevel);
            float xp = zdo.GetFloat(s_xp, 0f) + amount;
            int before = level;
            while (level < MaxLevel && xp >= XpToNext(level)) { xp -= XpToNext(level); level++; }
            if (level >= MaxLevel) xp = 0f;
            zdo.Set(s_xp, xp);
            if (level == before) return;
            zdo.Set(s_level, level);
            ApplyLevel(true);
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Levels] {name}: level up {before} -> {level} ({why}; {xp:0} of {XpToNext(level):0} XP to the next)");
            if (RiddenByLocalPlayer()) Player.m_localPlayer.Message(MessageHud.MessageType.Center, $"Level up! {_character.GetHoverName()}");
        }

        /// <summary>Sets the level (firesmounts_level, for tests): XP back to 0, the gains applied now. Owner only.</summary>
        internal string SetLevel(int level)
        {
            if (_nview == null || !_nview.IsValid()) return "no ZDO";
            if (!_nview.IsOwner()) _nview.ClaimOwnership();
            level = Mathf.Clamp(level, 1, MaxLevel);
            _nview.GetZDO().Set(s_level, level);
            _nview.GetZDO().Set(s_xp, 0f);
            ApplyLevel(true);
            // 0.1.116 (round 17 fly_land: a Tired Lv 20 mount stayed at 0): the admin command also fills the pool and ends Tired.
            if (_sadle != null) AddStamina(Pool());
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Levels] {name}: level set to {level} (pool {Pool():0}, speed x{SpeedMul:0.00}, ceiling {Ceiling:0} m)");
            return $"{name} is now Lv {level}: stamina pool {Pool():0}, speed x{SpeedMul:0.000}, damage x{DamageMul:0.00}, ceiling {Ceiling:0} m";
        }

        internal string Describe() =>
            $"{name} Lv {Level} ({Xp:0} of {XpToNext(Level):0} XP), stamina {(_sadle != null ? _sadle.GetStamina() : 0f):0} of {Pool():0}{(_tired ? ", Tired" : "")}";

        // Kill XP: the victim's max health / 10 (a stand-in until the classes' kill XP is shared through Core), on the victim's owner when
        // it dies. Mount kills (its own attacks): x0.5; the rider's kills while mounted: x0.25. Players and tames give none.
        [HarmonyPatch(typeof(Character), "CheckDeath")]
        internal static class KillXp
        {
            private static readonly int FmoNoDrops = "fmo_nodrops".GetStableHashCode();

            [HarmonyPrefix]
            private static void Prefix(Character __instance)
            {
                if (__instance == null || __instance.IsDead() || __instance.GetHealth() > 0f || __instance.IsPlayer() || __instance.IsTamed()) return;
                Character attacker = __instance.m_lastHit != null ? __instance.m_lastHit.GetAttacker() : null;
                if (attacker == null) return;
                float xp = __instance.GetMaxHealth() / 10f;
                // 0.1.109 ([release], doc 22 §4): a creature FiresMonsterOverhaul made (Summoner adds, boss adds, Splitting copies,
                // spirit-link and revived twins: its "fmo_nodrops" mark) pays a tenth, so a fight can't be dragged out to farm them.
                var victimView = __instance.GetComponent<ZNetView>();
                if (victimView != null && victimView.IsValid() && victimView.GetZDO().GetBool(FmoNoDrops, false)) xp *= 0.1f;
                var mount = Of(attacker);
                if (mount != null) { mount.AddXp(xp * 0.5f, "kill"); return; }
                if (!(attacker is Player rider)) return;
                long user = rider.GetZDOID().UserID;
                foreach (var p in s_live.Values)
                {
                    var nv = p != null ? p._nview : null;
                    if (nv == null || !nv.IsValid() || nv.GetZDO().GetLong(ZDOVars.s_user, 0L) != user) continue;
                    p.AddXp(xp * 0.25f, "rider kill");
                    return;
                }
            }
        }

        // Feeding (its taming food): 5 XP, once a minute.
        [HarmonyPatch(typeof(Tameable), "OnConsumedItem")]
        internal static class FeedXpPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Tameable __instance)
            {
                var p = __instance != null ? __instance.GetComponent<MountProgress>() : null;
                if (p == null || p._nview == null || !p._nview.IsValid() || !p._nview.IsOwner() || Time.time < p._nextFeedXp) return;
                if (p._character == null || !p._character.IsTamed()) return;
                p._nextFeedXp = Time.time + FeedEverySeconds;
                p.AddXp(FeedXp, "fed");
            }
        }

        // "Mini Moder - Lv 4" on the hover text and vanilla's mount HUD (both read GetHoverName); gold at level 20.
        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
        internal static class LevelInTheName
        {
            [HarmonyPostfix]
            private static void Postfix(Character __instance, ref string __result)
            {
                if (!Enabled || __instance == null || string.IsNullOrEmpty(__result)) return;
                var p = Of(__instance);
                if (p == null || !__instance.IsTamed()) return;
                int level = p.Level;
                __result = level >= MaxLevel ? $"<color=#ffd700>{__result} - Lv {level}</color>" : $"{__result} - Lv {level}";
            }
        }
    }
}
