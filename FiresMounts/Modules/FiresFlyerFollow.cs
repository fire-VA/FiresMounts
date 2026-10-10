using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// Flyer dismount/death rules (FIRESMOUNTS_FLYING_PLAN 5, DECIDED Fire 10-07). Any dismount: the riderless mount
    /// follows its rider (vanilla tame follow, MonsterAI.SetFollowTarget). Rider dies: the follow target switches to the
    /// rider's tombstone, and within ~4 m the mount STAYS there (vanilla "stay": no follow target + patrol point), so
    /// the respawned player finds it at the body. Only vanilla follow state is used; nothing new is persisted.
    /// Every peer tracks the rider from the Sadle "user" ZDO value; only the ZDO owner acts (while ridden, Sadle hands
    /// ownership to the rider's client, so the owner after a dismount is the rider's own peer).
    /// </summary>
    public class FiresFlyerFollow : MonoBehaviour
    {
        private enum Mode { Idle, Ridden, FollowRider, ToTombstone }

        private const float StayRadius = 4f;
        private const float TombSearchSeconds = 30f;
        private const float FirstFollowRange = 30f;
        // Set once a flyer has been sent to follow after its taming, so a later "stay" (E) is never undone.
        private static readonly int s_firstFollow = "FiresMounts_FirstFollow".GetStableHashCode();

        private ZNetView _nview;
        private MonsterAI _ai;
        private Character _character;
        private Tameable _tame;
        private Mode _mode;
        private long _riderUser;          // Sadle user (ZDOID.UserID) while ridden
        private long _riderPlayerId;      // profile player id (what TombStone stores as owner)
        private Player _rider;
        private Vector3 _lastRiderPos;
        private float _deathAt;
        private TombStone _tomb;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _ai = GetComponent<MonsterAI>();
            _character = GetComponent<Character>();
            _tame = GetComponent<Tameable>();
            InvokeRepeating(nameof(Tick), 1f, 0.5f);
        }

        private void Tick()
        {
            if (_nview == null || !_nview.IsValid() || _ai == null) return;
            long user = _nview.GetZDO().GetLong(ZDOVars.s_user, 0L);
            if (_nview.IsOwner()) UpdateFightSenses(user == 0L);

            if (user != 0L)
            {
                if (_mode != Mode.Ridden || user != _riderUser) { _riderUser = user; _rider = null; }
                _mode = Mode.Ridden;
                if (_rider == null) _rider = FindPlayerByUser(user);
                if (_rider != null) { _riderPlayerId = _rider.GetPlayerID(); _lastRiderPos = _rider.transform.position; }
                return;
            }

            if (_nview.IsOwner()) FollowOnceTamed();

            if (_mode == Mode.Ridden)
            {
                // Dismounted (anywhere, incl. mid-air above the 3 m line once flight exists): follow the rider down.
                _mode = Mode.FollowRider;
                if (_nview.IsOwner() && _rider != null && !_rider.IsDead())
                {
                    _ai.ResetPatrolPoint();
                    _ai.SetFollowTarget(_rider.gameObject);
                }
            }
            if (!_nview.IsOwner()) return;

            if (_mode == Mode.FollowRider)
            {
                if (_rider != null && !_rider.IsDead()) { _lastRiderPos = _rider.transform.position; return; }
                if (_riderPlayerId == 0L) { _mode = Mode.Idle; return; }
                _mode = Mode.ToTombstone;   // rider died (or its body object is gone): go wait at the grave
                _deathAt = Time.time;
                _tomb = null;
                _ai.SetFollowTarget(null);
            }

            if (_mode == Mode.ToTombstone)
            {
                if (_tomb == null)
                {
                    _tomb = FindTomb();
                    if (_tomb == null)
                    {
                        if (Time.time - _deathAt > TombSearchSeconds) { StayHere(); }
                        return;
                    }
                    _ai.SetFollowTarget(_tomb.gameObject);
                }
                Vector3 d = _tomb.transform.position - transform.position; d.y = 0f;
                if (d.magnitude <= StayRadius) StayHere();
            }
        }

        // Fire's hand test (10-07): a tamed flyer follows like a pet. The first time it is tamed (fed, firesmounts_tame, a nest hatch)
        // the nearest player within 30 m commands it to follow, through vanilla Tameable.Command (so ZDO "follow" carries it across
        // reloads); from then on E toggles follow / stay as for any vanilla tame.
        private void FollowOnceTamed()
        {
            if (_tame == null || _character == null || !_character.IsTamed()) return;
            ZDO zdo = _nview.GetZDO();
            if (zdo.GetBool(s_firstFollow, false)) return;
            if (_ai.GetFollowTarget() != null) { zdo.Set(s_firstFollow, true); return; }
            Player nearest = null;
            float best = FirstFollowRange;
            foreach (var p in Player.GetAllPlayers())
            {
                if (p == null || p.IsDead()) continue;
                float d = Vector3.Distance(p.transform.position, transform.position);
                if (d < best) { best = d; nearest = p; }
            }
            if (nearest == null) return;
            _tame.Command(nearest, message: true);
            zdo.Set(s_firstFollow, true);
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] flyer {name}: tamed, following {nearest.GetPlayerName()} (first follow, {best:0.0} m)");
        }

        // 0.1.113 (Fire 10-08: "mounts should fight alongside us when following"): a tamed mount that follows someone (not ridden, not on
        // Stay) gets its senses back, so vanilla's tame AI picks up the enemies around it and fights with its own attack items (the same
        // ones the rider's LMB / MMB fire; AttackDamage scales them; its kills pay mount XP). Ridden or staying it is calm again (CalmAI's 0s).
        private const float FightView = 25f, FightHear = 20f, FightAlert = 15f;
        private bool _fighting;

        private void UpdateFightSenses(bool unridden)
        {
            bool fight = unridden && MountConfig.FightWhenFollowing.Value && _character != null && _character.IsTamed() && _ai.GetFollowTarget() != null;
            if (fight == _fighting) return;
            _fighting = fight;
            _ai.m_viewRange = fight ? FightView : 0f;
            _ai.m_hearRange = fight ? FightHear : 0f;
            _ai.m_alertRange = fight ? FightAlert : 0f;
            if (!fight) _ai.SetAlerted(false);
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Follow] {name}: {(fight ? "following: fights beside its owner" : "calm (ridden, staying or not following)")}");
        }

        private void StayHere()
        {
            _ai.SetFollowTarget(null);
            _ai.SetPatrolPoint();   // vanilla tame "stay" (Tameable.Command): hold this spot
            _mode = Mode.Idle;
            _rider = null;
            _tomb = null;
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] flyer {name} staying at {transform.position} (rider died)");
        }

        private static Player FindPlayerByUser(long user)
        {
            foreach (var p in Player.GetAllPlayers())
                if (p != null && p.GetZDOID().UserID == user) return p;
            return null;
        }

        // The rider's newest tombstone: owner = profile player id, nearest to where the rider was last seen.
        private TombStone FindTomb()
        {
            TombStone best = null;
            float bestD = float.MaxValue;
            foreach (var t in FindObjectsByType<TombStone>(FindObjectsSortMode.None))
            {
                var nv = t != null ? t.GetComponent<ZNetView>() : null;
                if (nv == null || !nv.IsValid()) continue;
                if (nv.GetZDO().GetLong(ZDOVars.s_owner, 0L) != _riderPlayerId) continue;
                float dd = Vector3.Distance(t.transform.position, _lastRiderPos);
                if (dd < bestD) { bestD = dd; best = t; }
            }
            return best;
        }
    }
}
