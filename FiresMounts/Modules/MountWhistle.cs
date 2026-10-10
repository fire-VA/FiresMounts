using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// The mount whistle (Fire 10-08: "create the mount whistle item, so we can double tap a key when its in our inventory and summon our
    /// last mount"). With a FiresMountWhistle in the inventory, a double tap of [Mounts.Whistle] Key (H) brings the last mount you rode
    /// to [Mounts.Whistle] Distance behind you, from anywhere in the world, loaded or not, and it follows you (Fire 10-08: it always
    /// teleports; 30 s cooldown).
    /// - "Last mount": while you ride one of our mounts, it gets its own id in its ZDO (firesmounts_mount_id; ZDOIDs are renumbered on
    ///   every world load, so they can't be kept), and your character keeps that id (Player.m_customData, saved with the character).
    /// - The call goes to the server, which finds the mount's ZDO (this session's ZDOID first, else a scan of the mount prefabs). A mount
    ///   some peer has loaded is moved by that owner (its instance writes the ZDO, so only it can); an unloaded one the server moves in
    ///   the ZDO itself. Either way the ZDO's vanilla "follow" is set to you, so vanilla Tameable picks you up as soon as it loads near you.
    /// </summary>
    internal sealed class MountWhistle : MonoBehaviour
    {
        private const string RpcCall = "FiresMounts_WhistleCall";
        private const string RpcArrive = "FiresMounts_WhistleArrive";
        private const string RpcResult = "FiresMounts_WhistleResult";
        private const string CustomDataKey = "FiresMounts_LastMount";
        private const string HitchedKey = "fires_hitched";   // FiresMountController's hitch: a summoned horse must not stand planted
        private const float TrackEverySeconds = 0.5f;
        private static readonly int s_mountId = MountNames.ZdoMountId.GetStableHashCode();

        private static ZRoutedRpc s_registeredOn;
        private static ZDOID s_lastZdoid = ZDOID.None;   // this session's fast path to the last mount's ZDO

        private float _lastTap = -10f;
        private float _readyAt;
        private float _nextTrack;

        internal static void Install(GameObject host)
        {
            if (host != null && host.GetComponent<MountWhistle>() == null) host.AddComponent<MountWhistle>();
        }

        private void Update()
        {
            EnsureRpcs();
            var player = Player.m_localPlayer;
            if (player == null) return;
            if (Time.time >= _nextTrack) { _nextTrack = Time.time + TrackEverySeconds; TrackRide(player); }
            if (MountConfig.WhistleEnabled == null || !MountConfig.WhistleEnabled.Value || !FiresCore.Flight.FlightKeys.InputOpen) return;
            if (TypingGate.Typing() || !ZInput.GetKeyDown(MountConfig.WhistleKey.Value)) return;
            float now = Time.time;
            if (now - _lastTap > MountConfig.WhistleDoubleTapSeconds.Value) { _lastTap = now; return; }
            _lastTap = -10f;
            Whistle(player, "double tap");
        }

        // Every peer (the server too: this component lives on the plugin's object): the three routed RPCs, once per ZRoutedRpc (one per session).
        private static void EnsureRpcs()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, s_registeredOn)) return;
            s_registeredOn = rpc;
            rpc.Register<long, ZDOID, Vector3, string>(RpcCall, RPC_Call);
            rpc.Register<ZDOID, Vector3, string>(RpcArrive, RPC_Arrive);
            rpc.Register<string>(RpcResult, RPC_Result);
        }

        // -- the rider's side ---------------------------------------------------------------------------------------------------------

        private static void TrackRide(Player player)
        {
            var sadle = player.GetDoodadController() as Sadle;
            var mount = sadle != null ? sadle.GetCharacter() : null;
            var nview = mount != null ? mount.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid() || !MountNames.IsMount(global::Utils.GetPrefabName(mount.gameObject))) return;
            ZDO zdo = nview.GetZDO();
            long id = zdo.GetLong(s_mountId, 0L);
            if (id == 0L)
            {
                if (!nview.IsOwner()) return;   // vanilla Sadle hands the ZDO to the rider a moment after mounting
                id = NewId();
                zdo.Set(s_mountId, id);
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Whistle] {mount.name} got mount id {id}");
            }
            s_lastZdoid = zdo.m_uid;
            string want = id.ToString();
            if (player.m_customData.TryGetValue(CustomDataKey, out string had) && had == want) return;
            player.m_customData[CustomDataKey] = want;
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Whistle] last mount is now {mount.name} (id {id})");
        }

        private static long NewId()
        {
            long id = System.DateTime.UtcNow.Ticks ^ ((long)Random.Range(1, int.MaxValue) << 21);
            return id == 0L ? 1L : id;
        }

        internal static bool HasWhistle(Player player)
        {
            var inv = player != null ? player.GetInventory() : null;
            if (inv == null) return false;
            foreach (var item in inv.GetAllItems())
                if (item != null && item.m_dropPrefab != null && item.m_dropPrefab.name == MountNames.WhistleItem) return true;
            return false;
        }

        /// <summary>Calls the last ridden mount (the double tap, or firesmounts_whistle). Returns what happened, for the console.</summary>
        internal string Whistle(Player player, string how)
        {
            string Refuse(string why, bool tell)
            {
                if (tell) player.Message(MessageHud.MessageType.TopLeft, why);
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Whistle] {how}: no call ({why})");
                return why;
            }
            if (player.IsDead()) return Refuse("dead", false);
            if (!HasWhistle(player)) return Refuse("no mount whistle in the inventory", false);
            if (player.GetDoodadController() is Sadle) return Refuse("You are riding already", true);
            if (Time.time < _readyAt) return Refuse($"The whistle is ready in {Mathf.CeilToInt(_readyAt - Time.time)} s", true);
            if (!player.m_customData.TryGetValue(CustomDataKey, out string raw) || !long.TryParse(raw, out long id) || id == 0L)
                return Refuse("Ride a mount first: the whistle calls the last one you rode", true);
            if (ZRoutedRpc.instance == null) return Refuse("not connected", false);

            Vector3 target = SummonPoint(player);
            _readyAt = Time.time + MountConfig.WhistleCooldown.Value;
            player.Message(MessageHud.MessageType.TopLeft, "You whistle for your mount");
            PlaySound(player);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcCall, id, s_lastZdoid, target, player.GetPlayerName());
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Whistle] {how}: calling mount id {id} to {target} (session hint {s_lastZdoid})");
            return $"whistled for mount id {id} (to {target})";
        }

        // [Mounts.Whistle] Distance behind the player on the floor at their level: a ray down from just above their height (a house's
        // floor, not its roof, which the solid-height lookup would give), else the solid height, else the player's own height.
        private static Vector3 SummonPoint(Player player)
        {
            Vector3 back = -player.transform.forward;
            back.y = 0f;
            back = back.sqrMagnitude > 1e-4f ? back.normalized : Vector3.back;
            Vector3 p = player.transform.position + back * Mathf.Max(1f, MountConfig.WhistleDistance.Value);
            int mask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "Default_small");
            if (Physics.Raycast(new Vector3(p.x, player.transform.position.y + 2.5f, p.z), Vector3.down, out RaycastHit hit, 12f, mask, QueryTriggerInteraction.Ignore))
                p.y = hit.point.y + 0.3f;
            else if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(p, out float h)) p.y = h + 0.3f;
            else p.y = player.transform.position.y + 0.3f;
            return p;
        }

        private static void PlaySound(Player player)
        {
            string name = MountConfig.WhistleSound.Value;
            if (string.IsNullOrEmpty(name) || ZNetScene.instance == null) return;
            GameObject sfx = ZNetScene.instance.GetPrefab(name);
            if (sfx == null) { FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Whistle] sound '{name}' is not a ZNetScene prefab; silent"); return; }
            Object.Instantiate(sfx, player.transform.position, Quaternion.identity);
        }

        private static void RPC_Result(long sender, string message)
        {
            if (Player.m_localPlayer != null && !string.IsNullOrEmpty(message)) Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, message);
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Whistle] server: {message}");
        }

        // -- the server ---------------------------------------------------------------------------------------------------------------

        private static void RPC_Call(long sender, long id, ZDOID hint, Vector3 target, string playerName)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
            ZDO zdo = FindMount(id, hint);
            if (zdo == null) { Reply(sender, "Your mount can't hear you (it is gone)"); return; }
            string mountName = MountName(zdo);
            if (!zdo.GetBool(ZDOVars.s_tamed, false)) { Reply(sender, $"{mountName} is not tame any more"); return; }
            if (zdo.GetLong(ZDOVars.s_user, 0L) != 0L) { Reply(sender, $"Someone is riding {mountName}"); return; }
            float distance = Vector3.Distance(zdo.GetPosition(), target);
            long owner = zdo.GetOwner();
            bool ownerHere = owner != 0L && (owner == ZDOMan.GetSessionID() || ZNet.instance.GetPeer(owner) != null);
            if (ownerHere)
                ZRoutedRpc.instance.InvokeRoutedRPC(owner, RpcArrive, zdo.m_uid, target, playerName);
            else
                MoveZdo(zdo, target, playerName, "server, not loaded");
            Reply(sender, $"{mountName} heard you ({distance:0} m away)");
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Whistle] server: {playerName} called {mountName} (id {id}) {distance:0} m, moved by {(ownerHere ? "its owner " + owner : "the server")}");
        }

        private static void Reply(long peer, string message)
        {
            if (ZRoutedRpc.instance != null) ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcResult, message);
        }

        private static ZDO FindMount(long id, ZDOID hint)
        {
            if (!hint.IsNone())
            {
                ZDO z = ZDOMan.instance.GetZDO(hint);
                if (z != null && z.IsValid() && z.GetLong(s_mountId, 0L) == id) return z;
            }
            var found = new System.Collections.Generic.List<ZDO>();
            foreach (string prefab in MountNames.MountPrefabs)
            {
                found.Clear();
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, found, ref index)) { }
                foreach (ZDO z in found)
                    if (z != null && z.IsValid() && z.GetLong(s_mountId, 0L) == id) return z;
            }
            return null;
        }

        private static string MountName(ZDO zdo)
        {
            string named = zdo.GetString(ZDOVars.s_tamedName, "");
            if (!string.IsNullOrEmpty(named)) return named;
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
            string m = prefab != null && prefab.GetComponent<Character>() != null ? prefab.GetComponent<Character>().m_name : "";
            return string.IsNullOrEmpty(m) ? "Your mount" : (Localization.instance != null ? Localization.instance.Localize(m) : m);
        }

        // The ZDO itself, for a mount no peer has loaded (or its own owner, when the instance is gone): the new spot, facing the player's
        // side, unhitched, following the caller (vanilla Tameable reads "follow" when it loads: UpdateSavedFollowTarget).
        private static void MoveZdo(ZDO zdo, Vector3 target, string playerName, string by)
        {
            zdo.SetPosition(target);
            zdo.Set(ZDOVars.s_follow, playerName);
            zdo.Set(HitchedKey, false);
            if (zdo.GetOwner() != 0L && ZNet.instance != null && ZNet.instance.IsServer() && zdo.GetOwner() != ZDOMan.GetSessionID()
                && ZNet.instance.GetPeer(zdo.GetOwner()) == null)
                zdo.SetOwner(0L);   // a stale owner: free it so the caller's client takes it
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Whistle] moved {zdo.m_uid} to {target} ({by}), following {playerName}");
        }

        // -- the mount's owner --------------------------------------------------------------------------------------------------------

        private static void RPC_Arrive(long sender, ZDOID id, Vector3 target, string playerName)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
            if (zdo == null) return;
            GameObject inst = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
            var nview = inst != null ? inst.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                if (zdo.IsOwner()) MoveZdo(zdo, target, playerName, "owner, no instance");
                return;
            }
            Player caller = null;
            foreach (Player p in Player.GetAllPlayers())
                if (p != null && p.GetPlayerName() == playerName) { caller = p; break; }
            Vector3 face = (caller != null ? caller.transform.position : target) - target;
            face.y = 0f;
            Quaternion rot = face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face) : inst.transform.rotation;
            var body = inst.GetComponent<Rigidbody>();
            inst.transform.SetPositionAndRotation(target, rot);
            if (body != null) { body.position = target; body.rotation = rot; body.linearVelocity = Vector3.zero; }
            zdo.SetPosition(target);
            zdo.SetRotation(rot);
            zdo.Set(ZDOVars.s_follow, playerName);
            zdo.Set(HitchedKey, false);
            var ai = inst.GetComponent<MonsterAI>();
            if (ai != null)
            {
                ai.ResetPatrolPoint();
                ai.SetFollowTarget(caller != null ? caller.gameObject : null);   // else vanilla restores it from "follow" near the caller
            }
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Whistle] {inst.name} arrived at {target}, following {(caller != null ? playerName : playerName + " (once it sees them)")}");
        }

        /// <summary>The installed whistle (the plugin's), for firesmounts_whistle.</summary>
        internal static MountWhistle Instance => FiresMountsPlugin.Instance != null ? FiresMountsPlugin.Instance.GetComponent<MountWhistle>() : null;
    }
}
