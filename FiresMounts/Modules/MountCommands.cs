using UnityEngine;

namespace FiresMounts
{
    /// <summary>Console tools. Registered once in Setup. The dev spawn runs on host/server (so the ZDO persists);
    /// remote-client routing is added with the full creature pipeline (see task: runtime composer).</summary>
    internal static class MountCommands
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;

            new Terminal.ConsoleCommand("firesmounts_spawn",
                "Spawn a horse in front of you. Args: 2|refab|cookie = the reconstructed reference horse (FiresHorse2, cookie's proven body in OUR bundle), default = FiresHorse (HAP), [number] = forward distance.",
                (Terminal.ConsoleEventArgs args) =>
                {
                    string msg = SpawnHorse(args);
                    args.Context?.AddString("[FiresMounts] " + msg);
                });

            new Terminal.ConsoleCommand("firesmounts_tame",
                "Instantly tame the nearest mount (does NOT saddle it). Optional name filter, e.g. firesmounts_tame horse.",
                (Terminal.ConsoleEventArgs args) =>
                {
                    string filter = args.Args != null && args.Args.Length > 1 ? args.Args[1] : null;
                    string msg = TameNearest(filter);
                    args.Context?.AddString("[FiresMounts] " + msg);
                });

            new Terminal.ConsoleCommand("firesmounts_saddle",
                "Spawn a saddle (vanilla SaddleLox) at your feet. Pick it up, then with it in your inventory press E on your TAMED horse to put it on.",
                (Terminal.ConsoleEventArgs args) =>
                {
                    string msg = GiveSaddle();
                    args.Context?.AddString("[FiresMounts] " + msg);
                });

            new Terminal.ConsoleCommand("firesmounts_diag",
                "Dump the nearest FiresHorse state (materials/shader/textures, saddle+ride wiring, owner, physics) to the log.",
                (Terminal.ConsoleEventArgs args) =>
                {
                    string msg = DiagNearest();
                    args.Context?.AddString("[FiresMounts] " + msg);
                });
        }

        private static Character FindNearestHorse(string filter = null)
        {
            var player = Player.m_localPlayer;
            if (player == null) return null;
            Character best = null;
            float bestDist = float.MaxValue;
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null) continue;
                string pn = global::Utils.GetPrefabName(c.gameObject);
                if (!pn.StartsWith(MountNames.HorsePrefab)) continue;
                if (!string.IsNullOrEmpty(filter) && pn.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                float d = Vector3.Distance(c.transform.position, player.transform.position);
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        private static string TameNearest(string filter = null)
        {
            var best = FindNearestHorse(filter);
            if (best == null) return string.IsNullOrEmpty(filter)
                ? "no mount nearby — spawn one first."
                : $"no mount matching '{filter}' nearby.";
            string name = global::Utils.GetPrefabName(best.gameObject);

            var nview = best.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();

            var ai = best.GetComponent<MonsterAI>();
            if (ai != null) ai.MakeTame(); else best.SetTamed(true);

            return $"tamed {name} (NOT saddled). Run firesmounts_saddle to put a saddle on it, then aim at the saddle and press E to ride.";
        }

        // Saddling is a PLAYER action now, not automatic: spawn the saddle item and you apply it yourself (press E on
        // the tamed horse holding it), exactly like a vanilla Lox. The saddle mesh on the horse stays hidden until
        // applied (FiresMountCoat), and the saddle item is the vanilla SaddleLox (tame.m_saddleItem). Spawned as a
        // world pickup at your feet so it works regardless of inventory space.
        private static string GiveSaddle()
        {
            var player = Player.m_localPlayer;
            if (player == null) return "no local player.";
            if (ObjectDB.instance == null) return "ObjectDB not ready.";
            var prefab = ObjectDB.instance.GetItemPrefab("SaddleLox");
            if (prefab == null) return "SaddleLox item not found in ObjectDB.";
            Vector3 pos = player.transform.position + player.transform.forward * 1.2f + Vector3.up * 0.6f;
            Object.Instantiate(prefab, pos, Quaternion.identity);
            return "spawned a saddle (SaddleLox) at your feet. Pick it up, then press E on your tamed horse to put it on.";
        }

        private static string DiagNearest()
        {
            var best = FindNearestHorse();
            if (best == null) return "no FiresHorse nearby.";
            var go = best.gameObject;
            var root = go.transform;
            Debug.Log("===== FiresMounts DIAG: " + go.name + " =====");

            var nview = best.GetComponent<ZNetView>();
            Debug.Log($"[diag] tamed={best.IsTamed()} valid={(nview != null && nview.IsValid())} owner={(nview != null && nview.IsValid() && nview.IsOwner())} haveSaddleZDO={(nview != null && nview.IsValid() && nview.GetZDO().GetBool(ZDOVars.s_haveSaddleHash))}");

            // ── ORIENTATION: where the ROOT (movement) faces vs where the VISUAL (animator/bones) actually faces.
            // A non-zero yaw here IS the "moves sideways / faces 90 off" bug. Signed yaw about world-up, in degrees.
            Vector3 rf = Flat(root.forward);
            Debug.Log($"[diag] ROOT pos={V(root.position)} euler={V(root.rotation.eulerAngles)} fwd={V(root.forward)} scale={V(root.localScale)}");

            var anim = go.GetComponentInChildren<Animator>(true);
            if (anim != null)
            {
                var vt = anim.transform;
                string clip = "none";
                if (anim.runtimeAnimatorController != null && anim.isActiveAndEnabled)
                {
                    var ci = anim.GetCurrentAnimatorClipInfo(0);
                    if (ci != null && ci.Length > 0 && ci[0].clip != null) clip = ci[0].clip.name;
                }
                Debug.Log($"[diag] ANIM node='{vt.name}' localEuler={V(vt.localRotation.eulerAngles)} worldEuler={V(vt.rotation.eulerAngles)} fwd={V(vt.forward)}");
                Debug.Log($"[diag] ANIM avatar={(anim.avatar ? anim.avatar.name : "NULL")} avatarValid={(anim.avatar != null && anim.avatar.isValid)} controller={(anim.runtimeAnimatorController ? anim.runtimeAnimatorController.name : "NULL")} rootMotion={anim.applyRootMotion} culling={anim.cullingMode} clip='{clip}'");
                Debug.Log($"[diag] >>> YAW root.fwd -> ANIMATOR node.fwd = {Vector3.SignedAngle(rf, Flat(vt.forward), Vector3.up):0.0} deg  (THE movement-vs-visual mismatch)");
            }
            else Debug.Log("[diag] ANIM none");

            // Key bones: local rotation + how far each is off root.fwd + world offset from root. The skeleton root
            // (CG) yaw tells us whether the animator is driving the whole rig off-axis (baked CG rotation stomped).
            foreach (var bn in new[] { "CG", "Spine", "Neck", "Head", "Tail", "tail" })
            {
                var b = FindDeepT(root, bn);
                if (b != null)
                    Debug.Log($"[diag] BONE '{bn}'->'{b.name}' localEuler={V(b.localRotation.eulerAngles)} yawVsRoot={Vector3.SignedAngle(rf, Flat(b.forward), Vector3.up):0.0} worldOff={V(root.InverseTransformPoint(b.position))}");
            }

            // THE crux the user sees in ESP: where the VISIBLE horse points (spine axis Pelvis->Head) vs root.fwd
            // (where physics/movement points). A ~90 here IS the collider-vs-visual mismatch, as one number.
            var pelvisB = FindDeepT(root, "Pelvis") ?? FindDeepT(root, "Spine");
            var headB = FindDeepT(root, "Head");
            if (pelvisB != null && headB != null)
                Debug.Log($"[diag] >>> VISUAL nose (Pelvis->Head) vs root.fwd = {Vector3.SignedAngle(rf, Flat(headB.position - pelvisB.position), Vector3.up):0.0} deg  (rendered horse vs where it moves — compare to the COL worldAxis below)");

            // ── SKINNED PARTS: where each mesh ACTUALLY renders, in root-local space. For attached parts this is
            // small and sensible (tail behind = -Z, head ahead = +Z). A part floating off to the SIDE shows a big
            // local X — that's the mane/tail flying off. rootBoneOff shows if the bone or the skinning is at fault.
            Debug.Log("[diag] --- SMRs: meshLocalPos = where the mesh renders in root-local space (big X = floating sideways) ---");
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Vector3 meshLocal = root.InverseTransformPoint(smr.bounds.center);
                string rb0 = smr.rootBone ? smr.rootBone.name : "NULL";
                Vector3 rbLocal = smr.rootBone ? root.InverseTransformPoint(smr.rootBone.position) : Vector3.zero;
                Debug.Log($"[diag] SMR '{smr.name}' bones={(smr.bones != null ? smr.bones.Length : 0)} enabled={smr.enabled} rootBone='{rb0}' rootBoneLocal={V(rbLocal)} meshLocalPos={V(meshLocal)}");
            }

            // ── PHYSICS / COLLIDERS: the "moves only when airborne" clue lives here. Log ground state, root motion,
            // and EVERY collider — is it a trigger? what layer? and (for capsules) its world-space long axis, so a
            // leg capsule turned 90 to the side shows up. m_collider (Character's movement capsule) = the root one.
            var rbb = best.GetComponent<Rigidbody>();
            Debug.Log($"[diag] PHYS onGround={best.IsOnGround()} vel={(rbb ? rbb.velocity.magnitude : 0f):0.00} rbRot={(rbb ? "free" : "-")} constraints={(rbb ? rbb.constraints.ToString() : "-")} animRootMotion={(anim != null && anim.applyRootMotion)}");

            // THE wedge answer: what the Character is actually standing/pinned on. m_groundContactNormal tilted off
            // straight-up = a horizontal wedge that kills forward velocity (only jump clears it). standingOn = the
            // collider under us; if its rb is the horse ITSELF (or a bone body), that's the self-collision. bodyVel is
            // the REAL velocity (forward_speed in the ride log is only the INTENDED speed, per Character.UpdateWalking).
            const System.Reflection.BindingFlags NP = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var lowest = typeof(Character).GetField("m_lowestContactCollider", NP)?.GetValue(best) as Collider;
            var gnF = typeof(Character).GetField("m_groundContactNormal", NP);
            Vector3 gn = gnF != null ? (Vector3)gnF.GetValue(best) : Vector3.zero;
            bool gcOn = (bool)(typeof(Character).GetField("m_groundContact", NP)?.GetValue(best) ?? false);
            string standOn = lowest != null ? $"{lowest.transform.name} (rb={(lowest.attachedRigidbody ? lowest.attachedRigidbody.transform.name : "none")})" : "NULL";
            Debug.Log($"[diag] WEDGE groundContact={gcOn} groundNormal={V(gn)} (y<1.0 = tilted = HORIZONTAL wedge) standingOn='{standOn}' bodyVel={(rbb ? V(rbb.velocity) : "-")}");
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
            {
                string extra = "";
                if (col is CapsuleCollider cc)
                {
                    Vector3 axis = cc.direction == 0 ? col.transform.right : cc.direction == 1 ? col.transform.up : col.transform.forward;
                    extra = $" capDir={cc.direction} worldAxis={V(axis)} r={cc.radius:0.00} h={cc.height:0.00}";
                }
                string rbName = col.attachedRigidbody ? col.attachedRigidbody.transform.name : "none";
                Debug.Log($"[diag] COL '{col.transform.name}' {col.GetType().Name} trigger={col.isTrigger} layer={LayerMask.LayerToName(col.gameObject.layer)} enabled={col.enabled} rb={rbName} worldMinY={col.bounds.min.y:0.00} localCenter={V(root.InverseTransformPoint(col.bounds.center))}{extra}");
            }

            var sadle = go.GetComponentInChildren<Sadle>(true);
            if (sadle != null)
                Debug.Log($"[diag] Sadle node='{sadle.name}' active={sadle.gameObject.activeInHierarchy} attachPoint={(sadle.m_attachPoint ? sadle.m_attachPoint.name : "NULL")} attachLocal={(sadle.m_attachPoint ? V(root.InverseTransformPoint(sadle.m_attachPoint.position)) : "-")}");
            else Debug.Log("[diag] Sadle MISSING");

            // SADDLE-ATTACH preconditions — mirrors Tameable.UseItem: it silently refuses (looks like "can't attach")
            // unless nview valid AND m_saddleItem != null AND IsTamed() AND you hold an item whose name == m_saddleItem's.
            var tame = go.GetComponentInChildren<Tameable>(true);
            if (tame != null)
            {
                string siName = tame.m_saddleItem != null && tame.m_saddleItem.m_itemData?.m_shared != null
                    ? tame.m_saddleItem.m_itemData.m_shared.m_name : "NULL";
                bool siInDb = ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab("SaddleLox") != null;
                Debug.Log($"[diag] SADDLE-ATTACH: IsTamed={best.IsTamed()} tameableValid={tame.m_nview != null && tame.m_nview.IsValid()} " +
                          $"m_saddleItem='{siName}' m_saddle={(tame.m_saddle != null ? tame.m_saddle.name : "NULL")} " +
                          $"SaddleLox-in-ObjectDB={siInDb}  (all must be non-NULL/true for UseItem to attach)");
            }
            else Debug.Log("[diag] Tameable MISSING -> cannot saddle (this is the bug if it prints)");

            return "state dumped to the log (grep '[diag]'). Look at the 'YAW root.fwd -> ANIMATOR' line and any SMR with a big meshLocalPos X.";
        }

        // world dir flattened to the XZ plane (for signed-yaw comparisons about world up)
        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized; }
        private static string V(Vector3 v) => $"({v.x:0.00},{v.y:0.00},{v.z:0.00})";
        private static Transform FindDeepT(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase)) return t;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0) return t;
            return null;
        }

        private static string SpawnHorse(Terminal.ConsoleEventArgs args)
        {
            if (ZNet.instance == null) return "not in a world.";
            if (ZNetScene.instance == null) return "scene not ready.";

            string prefabName = MountNames.HorsePrefab;
            if (args.Args != null && args.Args.Length > 1)
            {
                string a = args.Args[1].ToLowerInvariant();
                if (a == "2" || a == "refab" || a == "cookie") prefabName = MountNames.RefabHorsePrefab;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                bool horse = ZNetScene.instance.GetPrefab(MountNames.HorsePrefab) != null;
                bool refab = ZNetScene.instance.GetPrefab(MountNames.RefabHorsePrefab) != null;
                return $"'{prefabName}' NOT registered. Registered on this client: {MountNames.HorsePrefab}={horse} {MountNames.RefabHorsePrefab}={refab}. " +
                       (horse || refab
                           ? "Check the spelling (use '2' or 'refab' for the reconstructed horse)."
                           : "firesmounts bundle did not load — restart and check the log for a bundle load error.");
            }

            var player = Player.m_localPlayer;
            Vector3 pos;
            Quaternion rot = Quaternion.identity;
            if (player != null)
            {
                float forward = 4f;
                if (args.Args != null)
                    foreach (var s in args.Args)
                        if (float.TryParse(s, out float f)) { forward = f; break; }
                Vector3 dir = player.transform.forward; dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward; else dir.Normalize();
                pos = player.transform.position + dir * forward;
                rot = Quaternion.LookRotation(dir, Vector3.up);
            }
            else
            {
                if (!ZNet.instance.IsServer()) return "no local player and not the server.";
                pos = Vector3.zero;
            }
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(pos, out float gh)) pos.y = gh + 0.5f;

            Object.Instantiate(prefab, pos, rot);
            return $"spawned {prefabName} at {pos}.";
        }

    }
}
