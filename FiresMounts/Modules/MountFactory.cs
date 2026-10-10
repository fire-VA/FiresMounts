using System;
using System.Collections.Generic;
using System.Reflection;
using FiresCore.Logging;
using FiresCore.Services;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// Turns the baked FiresHorse VISUAL prefab into a full Valheim creature at runtime. Game MonoBehaviours
    /// (Humanoid/MonsterAI/Tameable/Sadle/ZSync*) can't be serialized into an AssetBundle, so we AddComponent
    /// them here and SEED their tuning from the live vanilla Lox prefab via reflection (safe scalar/enum fields
    /// only — never Lox's asset references), then override the horse-specific bits. This is the "scratch-build
    /// reusing vanilla components" path: our own prefab, vanilla-tested riding/taming behaviour. See
    /// reference_vanilla_mount_build.
    /// </summary>
    internal static class MountFactory
    {
        private static bool s_summaryEmitted;
        private static bool s_materialsRepointed;
        private static int s_texturesBound;

        // Re-apply slider-tunable values to the registered prefabs AND every live mount instance. Wired to the
        // configs' SettingChanged (Compose only runs at boot — without this, sliders did nothing until restart).
        public static void ApplyLiveTuning()
        {
            try
            {
                foreach (var c in Character.GetAllCharacters())
                    if (c != null && c.GetComponent<FiresMountController>() != null)
                    {
                        TuneOne(c.gameObject);
                        var coat = c.GetComponent<FiresMountCoat>();
                        if (coat != null) coat.ReapplyFromConfig();   // pinned coat applies live; Random leaves rolls alone
                    }
                if (ZNetScene.instance != null)
                {
                    var p = ZNetScene.instance.GetPrefab(MountNames.HorsePrefab);
                    if (p != null) TuneOne(p);
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresMounts] live tuning failed: {ex.Message}"); }
        }

        private static void TuneOne(GameObject horse)
        {
            var hum = horse.GetComponent<Humanoid>();
            if (hum != null)
            {
                hum.m_walkSpeed = MountConfig.WalkSpeed.Value * 0.5f;
                hum.m_speed = MountConfig.WalkSpeed.Value;
                hum.m_runSpeed = MountConfig.RunSpeed.Value;
                hum.m_turnSpeed = MountConfig.TurnSpeed.Value;
                hum.m_runTurnSpeed = MountConfig.TurnSpeed.Value * 2f;
                hum.m_acceleration = MountConfig.Acceleration.Value;
            }
            var sadle = horse.GetComponentInChildren<Sadle>(true);
            if (sadle != null) sadle.m_maxStamina = MountConfig.MaxStamina.Value;
            var tame = horse.GetComponent<Tameable>();
            if (tame != null) tame.m_tamingTime = Mathf.Max(1f, MountConfig.TameTimeMinutes.Value * 60f);
        }

        // Compose once per prefab. Returns true if the horse is (or became) a valid creature.
        // AUTHORED prefab (BuildAuthoredHorse) ships the full Valheim stack baked in Unity. Runtime work shrinks to our
        // CUSTOM pieces + the game-asset refs a bundle can't own (saddle item). Also logs the baked component presence
        // so we can confirm from the log that the bundle deserialized the Valheim components in-game.
        private static bool ComposeRuntimeOnly(GameObject horse)
        {
            FiresMountsPlugin.Log.LogInfo($"[FiresMounts] AUTHORED '{horse.name}': Humanoid={horse.GetComponent<Humanoid>() != null} " +
                $"Tameable={horse.GetComponent<Tameable>() != null} Sadle={horse.GetComponentInChildren<Sadle>(true) != null} " +
                $"MonsterAI={horse.GetComponent<MonsterAI>() != null} FootStep={horse.GetComponent<FootStep>() != null} ZNetView={horse.GetComponent<ZNetView>() != null}");
            {
                var bai = horse.GetComponent<MonsterAI>();
                var bhum = horse.GetComponent<Humanoid>();
                if (bai != null && bhum != null)
                    FiresMountsPlugin.Log.LogDebug($"[FiresMounts] AUTHORED '{horse.name}' AI: faction={bhum.m_faction} view={bai.m_viewRange:0} alert={bai.m_alertRange:0} " +
                        $"hear={bai.m_hearRange:0} huntPlayer={bai.m_enableHuntPlayer} foods={(bai.m_consumeItems?.Count ?? 0)} consumeSearch={bai.m_consumeSearchRange:0.0} (baked; calmed next)");
                if (bai != null) CalmAI(bai);   // the authored path skipped this, so a baked horse kept Lox senses and would not tame beside you
                try { ConfigureFood(horse); } catch { }
            }
            if (horse.GetComponent<FiresMountController>() == null) horse.AddComponent<FiresMountController>();
            if (horse.GetComponent<MountProgress>() == null) horse.AddComponent<MountProgress>();   // 0.1.106 levels + stamina
            if (horse.GetComponent<MountVariant>() == null) horse.AddComponent<MountVariant>();     // 0.1.109 colour variants
            if (horse.GetComponent<MountSaddleLook>() == null) horse.AddComponent<MountSaddleLook>();   // 0.1.117 saddle dyes
            if (horse.name.StartsWith(MountNames.HorsePrefab) && horse.GetComponent<FiresMountCoat>() == null)
                horse.AddComponent<FiresMountCoat>();
            var tame = horse.GetComponent<Tameable>();
            if (tame != null && tame.m_saddleItem == null && ObjectDB.instance != null)
            {
                var sp = ObjectDB.instance.GetItemPrefab("SaddleLox");
                if (sp != null) tame.m_saddleItem = sp.GetComponent<ItemDrop>();
            }
            try { TuneOne(horse); } catch { }
            try { FixEffectShaders(horse); } catch (Exception ex) { Debug.LogWarning($"[FiresMounts] effect shader fix: {ex.Message}"); }
            return true;
        }

        // The cloned Asksvin death/ragdoll/tame effect prefabs keep their shader NAMES in our bundle but the shaders
        // themselves are stripped (magenta). Register the game's real shaders (from live vanilla prefabs — creatures for
        // Custom/Creature, vfx_* for the particle shaders) into the shared FiresCore ShaderSwapper, then repoint each
        // effect prefab's materials by name. Beats the same-name bundle conflict; runs once per prefab at registration.
        private static bool _vanillaShadersRegistered;
        private static void EnsureVanillaShaders()
        {
            if (_vanillaShadersRegistered || ZNetScene.instance == null) return;
            foreach (var n in new[] { "Player", "Lox", "Troll", "Deer", "Boar", "Neck", "Skeleton", "Ragdoll_lox",
                                      "vfx_HitSparks", "vfx_torch_fire", "vfx_FireAdditive", "vfx_bonfire_AddFuel",
                                      "vfx_lox_death", "vfx_blood_hit_red", "fx_Death_Small", "vfx_sledge_hit" })
            {
                var p = ZNetScene.instance.GetPrefab(n);
                if (p == null) continue;
                foreach (var r in p.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && m.shader != null) ShaderSwapper.Register(m.shader.name, m.shader);
            }
            _vanillaShadersRegistered = ShaderSwapper.HasAny;
        }

        private static void FixEffectShaders(GameObject horse)
        {
            EnsureVanillaShaders();
            if (!ShaderSwapper.HasAny) return;
            var seen = new HashSet<GameObject>();
            foreach (var comp in horse.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                foreach (var f in comp.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f.FieldType != typeof(EffectList)) continue;
                    var el = f.GetValue(comp) as EffectList;
                    if (el == null || el.m_effectPrefabs == null) continue;
                    foreach (var ed in el.m_effectPrefabs)
                    {
                        var fx = ed != null ? ed.m_prefab : null;
                        if (fx == null || !seen.Add(fx)) continue;
                        foreach (var r in fx.GetComponentsInChildren<Renderer>(true))
                            ShaderSwapper.ApplyToRenderer(r);
                    }
                }
            }
        }

        // Calm horse (Fire 10-07): never alerted, never flees or hunts, no wander - so it eats and tames with the player
        // right beside it (vanilla Tameable/MonsterAI skip eating + the taming countdown while alerted). Food search
        // (MonsterAI.FindClosestConsumableItem) is a radius scan, not sight, so zero senses still find the food.
        internal static void CalmAI(MonsterAI ai)
        {
            ai.m_alertRange = 0f;
            ai.m_viewRange = 0f;
            ai.m_hearRange = 0f;
            ai.m_randomMoveInterval = 0f;
            ai.m_randomMoveRange = 0f;
            ai.m_randomCircleInterval = 0f;
            ai.m_fleeIfHurtWhenTargetCantBeReached = false;
            ai.m_fleeIfNotAlerted = false;
            ai.m_fleeIfLowHealth = 0f;
            ai.m_enableHuntPlayer = false;
            ai.m_aggravatable = false;
            ai.m_attackPlayerObjects = false;
        }

        public static bool Compose(GameObject horse)
        {
            if (horse == null) return false;
            if (horse.GetComponent<Humanoid>() != null) return ComposeRuntimeOnly(horse); // authored prefab -> runtime-only path

            var lox = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Lox") : null;
            if (lox == null) { Debug.LogWarning("[FiresMounts] Lox prefab unavailable — cannot compose horse yet."); return false; }

            Transform tf = horse.transform;
            Transform visual = tf.Find("Visual");
            Transform attach = FindDeep(tf, "attachpoint");
            Transform sadelNode = FindDeep(tf, "Sadel");
            Animator animator = horse.GetComponentInChildren<Animator>(true);
            if (visual == null || attach == null || sadelNode == null || animator == null)
            {
                Debug.LogError($"[FiresMounts] horse prefab missing nodes (visual={visual} attach={attach} sadel={sadelNode} animator={animator}).");
                return false;
            }
            // NOTE: model orientation is BAKED (FiresMountBuilder.ComputeForwardRotation) — no runtime rotation here.
            // A runtime localRotation set does not hold and caused the sprint-in-place + 90-spin-on-jump.

            // NO runtime scale. Setting transform.localScale here (after Character/Rigidbody already initialized at
            // scale 1) left the rigidbody's inertia tensor + Character's cached m_collider at scale-1 while the
            // geometry was 1.4x — a mismatched body that pinned on the ground (moved only mid-jump). The 1.4x size is
            // now BAKED into the prefab root in FiresMountBuilder, so every component initializes consistent at 1.4.

            // ── physics ─────────────────────────────────────────────────────────────
            // Rigidbody at STRICT vanilla-creature parity (Lox: mass 60, no interpolation, discrete collision).
            // Interpolation was a self-inflicted wound: the body renders between physics steps while the rider
            // hangs off an animation-time bone — permanent micro-oscillation. Vanilla creatures never interpolate.
            var loxRb = lox.GetComponent<Rigidbody>();
            var rb = Get<Rigidbody>(horse);
            if (loxRb != null) CopySafe(loxRb, rb);
            rb.mass = 80f;   // mm_horse_black_mount value (recipe)
            rb.linearDamping = 0f;      // cookie's exact rigidbody — CopySafe was inheriting Lox's drag, unmatched
            rb.angularDamping = 0.05f;
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            // EVIDENCE (tick trace 2026-07-02): without interpolation the body advances only on 50Hz physics
            // ticks and renders frozen on in-between frames — at 100+fps that's the move-freeze-move jerk the
            // rider feels as "snapping at every hoofbeat". Interpolate renders the body smoothly between ticks.
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // COOKIE'S PROVEN LAYOUT (MagicalMounts mm_horse, read from his Unity project 2026-07). The root capsule
            // IS Valheim's m_collider (GetComponent, root) but it's SHRUNK to a nub parked UP in the body (center
            // y=1.6, r0.2, height0) — it satisfies GetComponent<CapsuleCollider>() and does NO ground contact. The
            // FrontLeg/BackLeg capsules reach the feet and do the actual contact. Our old single fat pill (r0.5 h2.0,
            // bottom at the ground) was the wedge that pinned forward movement while grounded — replaced here.
            var cap = Get<CapsuleCollider>(horse);
            cap.center = new Vector3(0f, 1.6f, 0.7067447f);
            cap.radius = 0.2f;
            cap.height = 0f;
            cap.direction = 1;

            // Ground contact = the leg capsules. FrontLeg is cookie's EXACT values; BackLeg reuses them with Z
            // mirrored to the rear (his multi-select hid the back Z/height — refine from a single-select shot). Local
            // pos + capsule are forced here so whatever the bake put on these nodes doesn't matter.
            ConfigureLeg(tf, "FrontLeg", new Vector3(0f, -0.1f, 0f), new Vector3(0f, 0.682933f, 1.45f), 0.2131548f, 1.160477f);
            ConfigureLeg(tf, "BackLeg", new Vector3(0f, -0.1f, 0f), new Vector3(0f, 0.682933f, -1.45f), 0.2131548f, 1.160477f);
            AddBoneColliders(horse);

            // The builder (FiresMountBuilder) baked a SECOND body capsule onto the Visual node (direction=2, the Z
            // barrel). Valheim's Character uses ONLY the root CapsuleCollider (GetComponent, root) as m_collider — 'cap'
            // above IS that root one. The Visual capsule is a stray SOLID collider that rotates with ModelYaw (the
            // "collider 90 off" seen in ESP) and adds a phantom contact shape to the body rigidbody. One body collider
            // only: neutralize every capsule that isn't the root one and isn't a (disabled, direction=1) leg column.
            foreach (var strayCap in horse.GetComponentsInChildren<CapsuleCollider>(true))
                if (strayCap != cap && strayCap.direction == 2) strayCap.enabled = false;

            // ── networking ──────────────────────────────────────────────────────────
            var nview = Get<ZNetView>(horse);
            nview.m_persistent = true;
            nview.m_type = ZDO.ObjectType.Default;

            var loxZst = lox.GetComponent<ZSyncTransform>();
            var zst = Get<ZSyncTransform>(horse);
            if (loxZst != null) CopySafe(loxZst, zst);
            zst.m_syncBodyVelocity = true;   // blueprint: smooth remote motion for a fast rideable

            var loxZanim = lox.GetComponent<ZSyncAnimation>();
            var zanim = Get<ZSyncAnimation>(horse);
            if (loxZanim != null) CopySafe(loxZanim, zanim);
            // CopySafe skips collections — set the sync lists explicitly (blueprint values)
            zanim.m_syncBools = new System.Collections.Generic.List<string> { "alert", "inWater" };
            zanim.m_syncFloats = new System.Collections.Generic.List<string> { "forward_speed", "turn_speed" };
            zanim.m_syncInts = new System.Collections.Generic.List<string>();
            zanim.m_smoothCharacterSpeeds = true;

            // ── character / humanoid ─────────────────────────────────────────────────
            var loxHum = lox.GetComponent<Humanoid>();
            var hum = Get<Humanoid>(horse);
            if (loxHum != null) CopySafe(loxHum, hum);
            hum.m_name = "Horse";
            hum.m_group = "fireshorse";
            hum.m_walkSpeed = MountConfig.WalkSpeed.Value;
            hum.m_speed = MountConfig.WalkSpeed.Value;
            hum.m_runSpeed = MountConfig.RunSpeed.Value;
            hum.m_turnSpeed = MountConfig.TurnSpeed.Value;
            hum.m_runTurnSpeed = MountConfig.TurnSpeed.Value * 2f;   // blueprint feel: 110 walk / 220 run turn
            hum.m_acceleration = MountConfig.Acceleration.Value;     // snappy ramp — collapses the accel-phase gait-blend jerk
            hum.m_health = 500f;
            hum.m_aiSkipTarget = true;                               // blueprint: enemies don't waste attacks on it
            hum.m_eye = FindDeep(tf, "EyePos") ?? FindDeep(tf, "head") ?? attach; // aim/look origin — must be non-null

            // ── AI (peaceful, tamable, big quadruped pathing) ─────────────────────────
            var loxAI = lox.GetComponent<MonsterAI>();
            var ai = Get<MonsterAI>(horse);
            if (loxAI != null) CopySafe(loxAI, ai);
            // blueprint lobotomy: the horse NEVER moves on its own — no wander, no flee, no alert. Only the
            // vanilla Sadle doodad control (and taming consume) drives it. This is what keeps a ridden/parked
            // horse planted instead of fighting the rider.
            CalmAI(ai);
            ai.m_pathAgentType = (Pathfinding.AgentType)4;   // blueprint value (Lox's TrollSize differs)

            // ── taming + saddle ──────────────────────────────────────────────────────
            var loxTame = lox.GetComponent<Tameable>();
            var tame = Get<Tameable>(horse);
            if (loxTame != null)
            {
                CopySafe(loxTame, tame);
                tame.m_saddleItem = loxTame.m_saddleItem;   // reuse SaddleLox item for MVP (craftable, works today)
            }
            // Robustness: if the Lox reference didn't hand over the saddle item (null loxTame, or a mid-session command
            // spawn where the copy missed), pull SaddleLox straight from ObjectDB so Tameable.UseItem's m_saddleItem
            // precondition can never be null — that null is exactly what makes a tamed horse silently refuse the saddle.
            if (tame.m_saddleItem == null && ObjectDB.instance != null)
            {
                var saddlePrefab = ObjectDB.instance.GetItemPrefab("SaddleLox");
                if (saddlePrefab != null) tame.m_saddleItem = saddlePrefab.GetComponent<ItemDrop>();
            }
            tame.m_tamingTime = Mathf.Max(1f, MountConfig.TameTimeMinutes.Value * 60f);

            // Sadle rides on the Sadel node (mirrors vanilla). Auto-wires m_character/m_monsterAI/m_tambable in Awake.
            var loxSadle = FindSadle(lox);
            var sadle = sadelNode.GetComponent<Sadle>() ?? sadelNode.gameObject.AddComponent<Sadle>();
            if (loxSadle != null) CopySafe(loxSadle, sadle);
            sadle.m_attachPoint = attach;
            sadle.m_attachAnimation = "attach_lox";        // player already has this state
            sadle.m_detachOffset = Vector3.zero;   // attach node lossyScale ~100 (Malbers Mount) -> any offset is x100'd and launches the rider skyward; FiresMountController.PlaceRiderBeside handles dismount placement
            sadle.m_maxStamina = MountConfig.MaxStamina.Value;
            sadle.m_hoverText = "$hud_ride";
            if (sadelNode.GetComponent<SphereCollider>() == null)
            {
                var sc = sadelNode.gameObject.AddComponent<SphereCollider>();
                // The seat chain sits under a scale-100 Malbers 'Mount' node (mirrors MM's rig). A raw radius here
                // becomes a ~140m collider once the saddle node activates on tame — melting physics into the
                // "catches fire + 2fps" loop. Compensate for lossyScale so the hover sphere is ~0.5m in WORLD space.
                sc.radius = 0.5f / Mathf.Max(sadelNode.lossyScale.x, 0.0001f);
            }
            tame.m_saddle = sadle;

            // NATIVE materials: the bundle bakes Standard-shader materials with textures serialized in — the horse
            // renders as-authored with NO runtime shader/texture swapping (4 rounds of swap fixes were
            // nondeterministic thanks to the same-name stripped shader in our own bundle). Only the coat TINT is
            // applied here (a plain _Color set on the baked material — no shader involved).
            Color coat = ParseCoatColor();
            if (coat != Color.white)
                foreach (var r in horse.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        string mn = m.name.ToLowerInvariant();
                        if (mn.Contains("body") || mn.Contains("hair") || mn.Contains("material"))
                            m.SetColor("_Color", coat);
                    }

            // blueprint: CharacterAnimEvent on the animator node (anim events + head look), whole prefab on the
            // "character" layer (ground checks, camera, targeting all expect creatures there). Animator must
            // ALWAYS animate (vanilla creatures) — culling freezes bones and the bone-mounted seat with them.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (animator.gameObject.GetComponent<CharacterAnimEvent>() == null)
                animator.gameObject.AddComponent<CharacterAnimEvent>();

            // Vanilla per-biome footstep FX (dust/splash/snow VFX + the material SFX a player gets), layered under our
            // gait loops. FootStep lives on the Character root; FiresMountController feeds it the Player prefab's effect
            // list at runtime and drives OnFoot on a gait rhythm (our HAP clips carry no vanilla "footstep" curve).
            if (horse.GetComponent<FootStep>() == null)
            {
                var fs = horse.AddComponent<FootStep>();
                fs.m_footlessFootsteps = false;      // we drive it; don't let it auto-trigger on distance too
                fs.m_footstepCullDistance = 50f;
                // Foot points at the BASE of the FrontLeg/BackLeg capsules (Y-capsule bottom = center - height/2), so the
                // step FX spawns under the hooves instead of the body centre. The points ride with the leg nodes.
                var feet = new System.Collections.Generic.List<Transform>();
                AddFootPoint(horse.transform, "FrontLeg", "FiresFootFront", feet);
                AddFootPoint(horse.transform, "BackLeg", "FiresFootBack", feet);
                if (feet.Count > 0) fs.m_feet = feet.ToArray();
            }
            int charLayer = LayerMask.NameToLayer("character");
            if (charLayer >= 0) SetLayerRecursive(horse.transform, charLayer);

            // polish layer (mounted jump; mount/dismount intro anims once baked). Guarded, opt-in via config.
            if (horse.GetComponent<FiresMountController>() == null) horse.AddComponent<FiresMountController>();
            if (horse.GetComponent<FiresMountObstacleRoller>() == null) horse.AddComponent<FiresMountObstacleRoller>();
            if (horse.GetComponent<MountProgress>() == null) horse.AddComponent<MountProgress>();   // 0.1.106 levels + stamina
            if (horse.GetComponent<MountVariant>() == null) horse.AddComponent<MountVariant>();     // 0.1.109 colour variants
            if (horse.GetComponent<MountSaddleLook>() == null) horse.AddComponent<MountSaddleLook>();   // 0.1.117 saddle dyes
            // randomized coat: rolls once per spawned instance (ZDO-persisted), swapped onto the body mesh in Start.
            // ONLY the HAP FiresHorse — the loadout is built around HAP's coat set + mane/tail variant meshes.
            // FiresHorse2 is the verbatim reconstructed reference (cookie's single mane/tail + baked black coat);
            // running the loadout on it would hide his one mane/tail, so it ships exactly as authored.
            if (horse.name == MountNames.HorsePrefab && horse.GetComponent<FiresMountCoat>() == null)
                horse.AddComponent<FiresMountCoat>();

            ConfigureFood(horse); // best-effort now; re-applied on ObjectDB.Awake when items are guaranteed loaded

            // Collider defaults hand-tuned in-world via FiresDebugginTools ("Save as default") override the
            // hardcoded geometry above for every spawn. No file = code defaults stand.
            int baked = ColliderBakeReader.Apply(horse, horse.name);
            if (baked > 0) FiresLogger.LogVerbose($"applied {baked} baked collider override(s) to '{horse.name}'.");

            FiresLogger.LogVerbose($"composed horse '{horse.name}' (rideable+tameable).");
            EmitFirstComposeSummary(horse);
            return true;
        }

        // One-time load summary — first successful compose only; relogin re-composes stay verbose-gated.
        static void EmitFirstComposeSummary(GameObject horse)
        {
            if (s_summaryEmitted) return;
            s_summaryEmitted = true;
            LoadSummary.For("FiresMounts").EmitMiniBox("🐴 MOUNTS", new[]
            {
                $"horse composed: {horse.name}",
                s_materialsRepointed ? "materials repointed" : "materials pending shader",
                $"textures bound: {s_texturesBound}",
            });
        }

        // Set the foods the horse eats to tame/breed. Lives on MonsterAI.m_consumeItems. ObjectDB may not be ready
        // at ZNetScene.Awake, so this is idempotent and also called from ObjectDB.Awake. No-op until items resolve.
        public static void ConfigureFood(GameObject horse)
        {
            if (horse == null || ObjectDB.instance == null) return;
            var ai = horse.GetComponent<MonsterAI>();
            if (ai == null) return;

            var names = (MountConfig.TamingFood?.Value ?? "").Split(',');
            var list = new System.Collections.Generic.List<ItemDrop>();
            foreach (var raw in names)
            {
                string n = raw.Trim();
                if (n.Length == 0) continue;
                var p = ObjectDB.instance.GetItemPrefab(n);
                var it = p != null ? p.GetComponent<ItemDrop>() : null;
                if (it != null && !list.Contains(it)) list.Add(it);
            }
            if (list.Count > 0) ai.m_consumeItems = list;
        }

        // Reassign the live vanilla creature shader (from the Lox body material, else Shader.Find) to every horse
        // renderer material, keeping the bundled _MainTex/_BumpMap. Runs on the shared prefab materials (persists
        // to all instances), idempotent.
        static void FixMaterials(GameObject horse, GameObject lox)
        {
            // steal ONLY from the live Lox material — Shader.Find can return our bundle's stripped same-name copy
            Shader shader = null;
            foreach (var s in lox.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (s.sharedMaterial != null && s.sharedMaterial.shader != null && s.name.ToLowerInvariant().Contains("body"))
                { shader = s.sharedMaterial.shader; break; }
            if (shader == null) { Debug.LogWarning("[FiresMounts] no live creature shader found — deferring to texture-bind pass."); return; }

            foreach (var r in horse.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader == shader) continue;
                    Texture main = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                    Texture bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
                    m.shader = shader;
                    if (main != null && m.HasProperty("_MainTex")) m.SetTexture("_MainTex", main);
                    if (bump != null && m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", bump);
                }
            }
            s_materialsRepointed = true;
            FiresLogger.LogVerbose($"materials repointed to live shader '{shader.name}'.");
        }

        // Robust texture guarantee: load the horse textures packed in the firesmounts bundle and bind them onto any
        // renderer material whose _MainTex is still null. Survives even if the build-time material serialization
        // regresses (the baked .mat had empty m_TexEnvs). Keyword-map submesh material name -> texture.
        public static void BindHorseTextures(GameObject horse, AssetBundle bundle)
        {
            if (horse == null || bundle == null) return;
            Texture2D[] texes;
            try { texes = bundle.LoadAllAssets<Texture2D>(); } catch { return; }
            if (texes == null || texes.Length == 0) { Debug.LogWarning("[FiresMounts.tex] no textures in bundle"); return; }

            // Order-proof: material property values PERSIST across shader swaps, so we bind _MainTex
            // UNCONDITIONALLY (no HasProperty gate — the baked shader has no property metadata and gated out every
            // bind at ObjectDB.Awake). Whichever pass later lands the live shader, the stored texture becomes active.
            // CRITICAL: our bundle ships a STRIPPED shader with the SAME NAME "Custom/Creature" — never pick a
            // candidate by name alone; exclude the instances our own materials currently reference.
            var ownShaders = new System.Collections.Generic.HashSet<Shader>();
            foreach (var r0 in horse.GetComponentsInChildren<Renderer>(true))
                foreach (var m0 in r0.sharedMaterials)
                    if (m0 != null && m0.shader != null) ownShaders.Add(m0.shader);
            Shader live = ResolveCreatureShaderRuntime(ownShaders);
            Color coat = ParseCoatColor();

            int bound = 0;
            foreach (var r in horse.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (live != null && m.shader != live) m.shader = live;
                    string mn = m.name.ToLowerInvariant();
                    string key = mn.Contains("hair") || mn.Contains("mane") || mn.Contains("tail") ? "hair"
                               : mn.Contains("eye") ? "eye"
                               : mn.Contains("saddle") ? "saddle" : "body";
                    Texture2D pick = MatchTex(texes, key) ?? MatchTex(texes, "body");
                    if (pick != null) { m.SetTexture("_MainTex", pick); m.mainTexture = pick; bound++; }
                    m.SetColor("_Color", key == "body" || key == "hair" ? coat : Color.white);
                    FiresLogger.LogVerbose($"tex: mat={m.name} shader={m.shader.name} hasMainTex={m.HasProperty("_MainTex")} tex={(pick ? pick.name : "NONE")}");
                }
            }
            s_texturesBound = bound;
            if (live != null) s_materialsRepointed = true;
            FiresLogger.LogVerbose($"tex bind complete: liveShader={(live ? "OK" : "NULL")} boundTex={bound}");
        }

        // The only TRUSTWORTHY live Custom/Creature reference is one a VANILLA creature material points at —
        // name-based lookup (Shader.Find / shader scan) can return our bundle's stripped same-name copy.
        static Shader ResolveCreatureShaderRuntime(System.Collections.Generic.HashSet<Shader> exclude)
        {
            var lox = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Lox") : null;
            if (lox != null)
                foreach (var smr in lox.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var sh = smr.sharedMaterial != null ? smr.sharedMaterial.shader : null;
                    if (sh != null && !exclude.Contains(sh)) return sh;
                }
            foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (mat == null || mat.shader == null) continue;
                if (mat.shader.name != "Custom/Creature") continue;
                if (exclude.Contains(mat.shader)) continue;
                if (mat.name.StartsWith("FiresHorse")) continue;
                return mat.shader;
            }
            return null;   // leave shaders untouched — the stored textures activate when a later pass lands the live one
        }

        // The source albedo is a WHITE-coat horse; a tint over it recolors the breed (chestnut/bay/gray) without new art.
        static Color ParseCoatColor()
        {
            string s = MountConfig.CoatColor?.Value;
            return !string.IsNullOrEmpty(s) && ColorUtility.TryParseHtmlString(s, out Color c) ? c : Color.white;
        }

        static Texture2D MatchTex(Texture2D[] texes, string keyword)
        {
            foreach (var t in texes)
                if (t != null && t.name.ToLowerInvariant().Contains(keyword)) return t;
            return null;
        }

        static Sadle FindSadle(GameObject go)
        {
            foreach (var s in go.GetComponentsInChildren<Sadle>(true)) return s;
            return null;
        }

        static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayerRecursive(c, layer);
        }

        // Places a leg ground-contact capsule (cookie's Colliders/FrontLeg|BackLeg): sets the node's local position +
        // the capsule geometry, forces it SOLID and enabled. Creates the node under Colliders if the bake lacks it.
        static void ConfigureLeg(Transform root, string name, Vector3 localPos, Vector3 center, float radius, float height)
        {
            var t = FindDeep(root, name);
            if (t == null)
            {
                var colliders = FindDeep(root, "Colliders") ?? root;
                t = new GameObject(name).transform;
                t.SetParent(colliders, false);
            }
            t.localPosition = localPos;
            var cap = t.GetComponent<CapsuleCollider>() ?? t.gameObject.AddComponent<CapsuleCollider>();
            cap.center = center; cap.radius = radius; cap.height = height; cap.direction = 1;
            cap.isTrigger = false; cap.enabled = true;
        }

        // Empty transform at the ground-contact base of a leg capsule (Y-capsule bottom), reused for FootStep.m_feet so
        // the step FX drops under the hooves. Parented to the leg node so it tracks the horse.
        static void AddFootPoint(Transform root, string legName, string pointName, System.Collections.Generic.List<Transform> into)
        {
            var leg = FindDeep(root, legName);
            var col = leg != null ? leg.GetComponent<CapsuleCollider>() : null;
            if (col == null) return;
            var fp = leg.Find(pointName) ?? new GameObject(pointName).transform;
            fp.SetParent(leg, false);
            fp.localPosition = col.center - new Vector3(0f, col.height * 0.5f, 0f);
            into.Add(fp);
        }

        // mm_horse_black_mount's collider layout (mm-extract recipe) placed on the SKELETON bones. Bones share the
        // skinned mesh's frame, so these always line up with the visible body — unlike root-space capsules, which sit
        // 90 off because the mesh is skinned through the CG chain. Covers legs (UpperArm/Thigh) + body (Spine chain) +
        // Neck/Head. FindDeep matches exact names first, so "Spine" doesn't grab "Spine1". Skips a bone that already
        // has a collider (idempotent across re-compose).
        static void AddBoneColliders(GameObject horse)
        {
            BoneCap(horse, "Spine", new Vector3(-0.0242f, 0.0554f, 0f), 0.295f, 0.642f, 0);
            BoneCap(horse, "Spine1", new Vector3(0f, 0.05f, 0f), 0.3f, 0.77f, 0);
            BoneCap(horse, "Spine2", new Vector3(-0.1f, 0.05f, 0f), 0.29f, 0.83f, 0);
            BoneCap(horse, "Neck", new Vector3(-0.2138f, -0.0415f, 0f), 0.223f, 0.503f, 0);
            BoneCap(horse, "Head", new Vector3(0.0094f, 0.3543f, 0f), 0.13f, 0.416f, 1);
            BoneCap(horse, "L UpperArm", new Vector3(-0.2114f, -0.0005f, 0.003f), 0.073f, 0.573f, 0);
            BoneCap(horse, "R UpperArm", new Vector3(-0.2114f, -0.0005f, -0.003f), 0.073f, 0.573f, 0);
            BoneBox(horse, "L Thigh", new Vector3(-0.2714f, 0.0053f, -0.024f), new Vector3(0.5135f, 0.3106f, 0.1767f));
            BoneBox(horse, "R Thigh", new Vector3(-0.2714f, 0.0053f, -0.024f), new Vector3(0.5135f, 0.3106f, 0.1767f));
        }

        static void BoneCap(GameObject horse, string bone, Vector3 c, float r, float h, int dir)
        {
            var t = FindDeep(horse.transform, bone);
            if (t == null) { Debug.LogWarning($"[FiresMounts] collider bone '{bone}' not found."); return; }
            if (t.GetComponent<Collider>() != null) return;
            var cap = t.gameObject.AddComponent<CapsuleCollider>();
            cap.center = c; cap.radius = r; cap.height = h; cap.direction = dir;
            cap.isTrigger = true;   // hit-detection only — never solid: a bone capsule turned 90 must NOT prop/block movement on the ground (root capsule owns ground/movement)
        }

        static void BoneBox(GameObject horse, string bone, Vector3 c, Vector3 size)
        {
            var t = FindDeep(horse.transform, bone);
            if (t == null) { Debug.LogWarning($"[FiresMounts] collider bone '{bone}' not found."); return; }
            if (t.GetComponent<Collider>() != null) return;
            var box = t.gameObject.AddComponent<BoxCollider>();
            box.center = c; box.size = size;
            box.isTrigger = true;   // hit-detection only — never solid (see BoneCap)
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase)) return t;
            // loose contains-match fallback (e.g. "Head" bone)
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().Contains(name.ToLowerInvariant())) return t;
            return null;
        }

        static T Get<T>(GameObject go) where T : Component
            => go.GetComponent<T>() ?? go.AddComponent<T>();

        // Copy only SAFE fields (primitives, enums, string, Vector*, Color, LayerMask, Quaternion) across the whole
        // type hierarchy — transfers all tuning scalars while skipping asset references, collections and delegates.
        static void CopySafe(Component src, Component dst)
        {
            for (Type t = src.GetType(); t != null && t != typeof(MonoBehaviour) && t != typeof(Behaviour)
                 && t != typeof(Component) && t != typeof(UnityEngine.Object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!IsSafe(f.FieldType)) continue;
                    try { f.SetValue(dst, f.GetValue(src)); } catch { }
                }
            }
        }

        static bool IsSafe(Type t)
            => t.IsPrimitive || t.IsEnum || t == typeof(string)
            || t == typeof(Vector3) || t == typeof(Vector2) || t == typeof(Vector4)
            || t == typeof(Color) || t == typeof(Color32) || t == typeof(Quaternion) || t == typeof(LayerMask);
    }
}
