using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// Flying mounts, slice 1 (Tools\FIRESMOUNTS_FLYING_PLAN.md): runtime CLONES of vanilla prefabs re-registered
    /// under our own names - the mini Moder ("Dragon"), the drake ("Hatchling"), the Sky saddle ("SaddleLox"), the
    /// Moder egg ("DragonEgg") and the Moder nest ("fire_pit"). Clones live under one inactive DontDestroyOnLoad
    /// holder, so no Awake (ZNetView/Character) ever runs on them and they survive relogins. Every entry point is
    /// idempotent: ZNetScene.Awake, ObjectDB.Awake and ObjectDB.CopyOtherDB all call EnsureRegistered, and whichever
    /// runs first builds what its database can already see. Flight (0.1.99): FiresFlyerFlight, on Core 0.2.320's FlightModel.
    /// </summary>
    internal static partial class FlyerFactory
    {
        private static GameObject _holder;
        private static GameObject _eikthyr, _bjorn, _brute, _serpent, _bonemaw;
        private static GameObject _moder, _drake, _fader, _skySaddle, _moderEgg, _faderEgg, _drakeEgg, _whistle, _nest;
        private static Recipe _skySaddleRecipe, _whistleRecipe;
        private static bool _eggDropAdded, _faderEggDropAdded, _drakeEggDropAdded;

        private static readonly MethodInfo s_updateRegisters =
            typeof(ObjectDB).GetMethod("UpdateRegisters", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo s_namedPrefabs =
            typeof(ZNetScene).GetField("m_namedPrefabs", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static GameObject SkySaddle => _skySaddle;
        internal static GameObject ModerEgg => _moderEgg;
        internal static GameObject Whistle => _whistle;

        public static void EnsureRegistered(ObjectDB db = null)
        {
            if (!MountConfig.Enabled.Value || MountConfig.FlyersEnabled == null || !MountConfig.FlyersEnabled.Value) return;
            if (db == null) db = ObjectDB.instance;
            try
            {
                EnsureHolder();
                BuildItems(db);
                BuildDyes(db);
                BuildGroundItems(db);
                if (db != null) { RegisterItemsInObjectDB(db); RegisterDyes(db); RegisterGroundItems(db); }
                BuildCreatures();
                BuildNest(db);
                if (ZNetScene.instance != null) RegisterInZNetScene(ZNetScene.instance);
                AddEggDropToModer();
                AddEggDropToFader();
                AddEggDropToDrake();
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresMounts] flyer registration skipped: {ex}"); }
        }

        private static void EnsureHolder()
        {
            if (_holder != null) return;
            _holder = new GameObject("FiresMounts_FlyerPrefabs");
            _holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_holder);
        }

        // Vanilla source lookup: ZNetScene holds creatures, pieces AND items; ObjectDB covers the menu-scene pass.
        private static GameObject FindVanilla(string name, ObjectDB db)
        {
            GameObject p = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if (p == null && db != null) p = db.GetItemPrefab(name);
            return p;
        }

        // Clone under the inactive holder and rename BEFORE anything registers it (ZNetView hashes the name).
        private static GameObject CloneUnderHolder(GameObject src, string newName)
        {
            var go = UnityEngine.Object.Instantiate(src, _holder.transform, false);
            go.name = newName;
            return go;
        }

        // -- items ----------------------------------------------------------------

        private static void BuildItems(ObjectDB db)
        {
            if (_skySaddle == null)
            {
                var src = FindVanilla("SaddleLox", db);
                if (src != null)
                {
                    _skySaddle = CloneUnderHolder(src, MountNames.SkySaddleItem);
                    var shared = _skySaddle.GetComponent<ItemDrop>().m_itemData.m_shared;
                    shared.m_name = "Sky saddle";
                    UseIcon(shared, MountNames.SkySaddleItem);
                    shared.m_description = "A saddle light enough for wings. Put it on a tamed mini Moder or drake to ride it.";
                    FiresMountsPlugin.Log.LogDebug($"[FiresMounts] flyer item registered: {MountNames.SkySaddleItem} (from SaddleLox)");
                }
            }
            if (_moderEgg == null)
            {
                var src = FindVanilla("DragonEgg", db);
                if (src != null)
                {
                    _moderEgg = CloneUnderHolder(src, MountNames.ModerEggItem);
                    var shared = _moderEgg.GetComponent<ItemDrop>().m_itemData.m_shared;
                    shared.m_name = "Moder egg";
                    UseIcon(shared, MountNames.ModerEggItem);
                    shared.m_description = "Still warm. Set it in a Moder nest and it hatches a tame mini Moder after a day.";
                    FiresMountsPlugin.Log.LogDebug($"[FiresMounts] flyer item registered: {MountNames.ModerEggItem} (from DragonEgg)");
                }
            }
            if (_faderEgg == null)
            {
                // The Ashlands egg for the look. Its EggGrow (it would hatch an Asksvin on warm ground) goes: the nest hatches this one.
                var src = FindVanilla("AsksvinEgg", db) ?? FindVanilla("DragonEgg", db);
                if (src != null)
                {
                    _faderEgg = CloneUnderHolder(src, MountNames.FaderEggItem);
                    foreach (var grow in _faderEgg.GetComponentsInChildren<EggGrow>(true)) UnityEngine.Object.DestroyImmediate(grow);
                    var shared = _faderEgg.GetComponent<ItemDrop>().m_itemData.m_shared;
                    shared.m_name = "Fader egg";
                    UseIcon(shared, MountNames.FaderEggItem);
                    shared.m_description = "It smoulders. Set it in a mount nest and it hatches a tame Mini Fader after a day.";
                    // One per slot (the Asksvin egg stacks to 20): vanilla stacking ignores m_customData, so a variant egg ("fires_variant",
                    // Tools\FIRES_VARIANTS_PLAN.md) must never merge into a plain stack. The Moder egg (DragonEgg) is 1 already.
                    shared.m_maxStackSize = 1;
                    FiresMountsPlugin.Log.LogDebug($"[FiresMounts] mount item registered: {MountNames.FaderEggItem} (from {src.name})");
                }
            }
            if (_drakeEgg == null)
            {
                // 0.1.112 (Fire 10-08: "C"): an egg vanilla Mountain drakes drop, hatched in the mount nest into a tame FiresDrake.
                var src = FindVanilla("AsksvinEgg", db) ?? FindVanilla("DragonEgg", db);
                if (src != null)
                {
                    _drakeEgg = CloneUnderHolder(src, MountNames.DrakeEggItem);
                    foreach (var grow in _drakeEgg.GetComponentsInChildren<EggGrow>(true)) UnityEngine.Object.DestroyImmediate(grow);
                    var shared = _drakeEgg.GetComponent<ItemDrop>().m_itemData.m_shared;
                    shared.m_name = "Drake egg";
                    shared.m_description = "Cold to the touch. Set it in a mount nest and it hatches a tame drake after a day.";
                    shared.m_maxStackSize = 1;   // a variant egg never merges into a plain stack
                    UseIcon(shared, MountNames.DrakeEggItem);
                    FiresMountsPlugin.Log.LogDebug($"[FiresMounts] mount item registered: {MountNames.DrakeEggItem} (from {src.name})");
                }
            }
            if (_whistle == null)
            {
                // Fire 10-08: a stand-in model (vanilla's silver necklace) until the decimated whistle is bundled.
                var src = FindVanilla("SilverNecklace", db);
                if (src != null)
                {
                    _whistle = CloneUnderHolder(src, MountNames.WhistleItem);
                    var shared = _whistle.GetComponent<ItemDrop>().m_itemData.m_shared;
                    shared.m_name = "Mount whistle";
                    UseIcon(shared, MountNames.WhistleItem);
                    shared.m_description = "Carry it and double-tap the whistle key (H) to call the last mount you rode to your side.";
                    shared.m_value = 0;         // no selling a workbench craft to Haldor
                    shared.m_maxStackSize = 1;
                    shared.m_teleportable = true;
                    FiresMountsPlugin.Log.LogDebug($"[FiresMounts] mount item registered: {MountNames.WhistleItem} (from SilverNecklace)");
                    UseWhistleModel(_whistle);
                }
            }
        }

        private static void RegisterItemsInObjectDB(ObjectDB db)
        {
            bool changed = false;
            foreach (var item in new[] { _skySaddle, _moderEgg, _faderEgg, _drakeEgg, _whistle })
                if (item != null && !db.m_items.Contains(item)) { db.m_items.Add(item); changed = true; }
            if (changed) s_updateRegisters?.Invoke(db, null);   // rebuilds m_itemByHash + m_itemByData, vanilla's own way

            AddWhistleRecipe(db);
            if (_skySaddle == null || db.m_recipes == null) return;
            if (_skySaddleRecipe == null)
            {
                var station = FindVanilla("piece_artisanstation", db);
                var reqs = new List<Piece.Requirement>();
                AddReq(reqs, db, "DragonTear", 5);
                AddReq(reqs, db, "Silver", 10);
                AddReq(reqs, db, "DeerHide", 10);
                AddReq(reqs, db, "LinenThread", 10);
                if (station == null || reqs.Count < 4) return;   // retried on the next ObjectDB pass
                _skySaddleRecipe = ScriptableObject.CreateInstance<Recipe>();
                _skySaddleRecipe.name = "Recipe_" + MountNames.SkySaddleItem;
                _skySaddleRecipe.m_item = _skySaddle.GetComponent<ItemDrop>();
                _skySaddleRecipe.m_amount = 1;
                _skySaddleRecipe.m_craftingStation = station.GetComponent<CraftingStation>();
                _skySaddleRecipe.m_minStationLevel = 1;
                _skySaddleRecipe.m_resources = reqs.ToArray();
                UnityEngine.Object.DontDestroyOnLoad(_skySaddleRecipe);
            }
            if (!db.m_recipes.Contains(_skySaddleRecipe)) db.m_recipes.Add(_skySaddleRecipe);
        }

        // Fire 10-08: the workbench, Wood 2 + LeatherScraps 2.
        private static void AddWhistleRecipe(ObjectDB db)
        {
            if (_whistle == null || db.m_recipes == null) return;
            if (_whistleRecipe == null)
            {
                var station = FindVanilla("piece_workbench", db);
                var reqs = new List<Piece.Requirement>();
                AddReq(reqs, db, "Wood", 2);
                AddReq(reqs, db, "LeatherScraps", 2);
                if (station == null || reqs.Count < 2) return;   // retried on the next ObjectDB pass
                _whistleRecipe = ScriptableObject.CreateInstance<Recipe>();
                _whistleRecipe.name = "Recipe_" + MountNames.WhistleItem;
                _whistleRecipe.m_item = _whistle.GetComponent<ItemDrop>();
                _whistleRecipe.m_amount = 1;
                _whistleRecipe.m_craftingStation = station.GetComponent<CraftingStation>();
                _whistleRecipe.m_minStationLevel = 1;
                _whistleRecipe.m_resources = reqs.ToArray();
                UnityEngine.Object.DontDestroyOnLoad(_whistleRecipe);
            }
            if (!db.m_recipes.Contains(_whistleRecipe)) db.m_recipes.Add(_whistleRecipe);
        }

        private static void AddReq(List<Piece.Requirement> into, ObjectDB db, string item, int amount)
        {
            var p = FindVanilla(item, db);
            var drop = p != null ? p.GetComponent<ItemDrop>() : null;
            if (drop == null) { FiresLogger.LogWarning($"recipe item '{item}' not found."); return; }
            into.Add(new Piece.Requirement { m_resItem = drop, m_amount = amount, m_amountPerLevel = 0, m_recover = true });
        }

        // -- creatures --------------------------------------------------------------

        private static void BuildCreatures()
        {
            if (ZNetScene.instance == null) return;
            if (_moder == null)
            {
                var src = ZNetScene.instance.GetPrefab("Dragon");
                if (src != null)
                {
                    _moder = CloneUnderHolder(src, MountNames.MiniModerPrefab);
                    Flyerize(_moder, MountConfig.ModerScale.Value, "Mini Moder", MountConfig.ModerSeatHeight.Value,
                             MountConfig.ModerSeatForward.Value, null, ModerSeat, ModerAttackOrder);
                }
            }
            if (_drake == null)
            {
                var src = ZNetScene.instance.GetPrefab("Hatchling");
                if (src != null)
                {
                    _drake = CloneUnderHolder(src, MountNames.DrakePrefab);
                    Flyerize(_drake, MountConfig.DrakeScale.Value, "Drake", MountConfig.DrakeSeatHeight.Value,
                             MountConfig.DrakeSeatForward.Value, MountConfig.DrakeFood.Value, DrakeSeat);
                }
            }
            if (_fader == null)
            {
                var src = ZNetScene.instance.GetPrefab("Fader");
                if (src != null)
                {
                    _fader = CloneUnderHolder(src, MountNames.MiniFaderPrefab);
                    // Its run damager chops and mines what it runs into (300 chop / pickaxe on props): a ridden mount must not fell a forest
                    // or a base, so it goes.
                    var runHit = _fader.transform.Find("RunHitDamager");
                    if (runHit != null) UnityEngine.Object.DestroyImmediate(runHit.gameObject);
                    Flyerize(_fader, MountConfig.FaderScale.Value, "Mini Fader", 0f, 0f, null, FaderSeat, FaderAttackOrder, flies: false);
                }
            }
            BuildGroundMounts();
            // The saddle clone may arrive after the creatures (ObjectDB-first order): wire it late.
            foreach (var c in new[] { _moder, _drake, _fader })
            {
                var t = c != null ? c.GetComponent<Tameable>() : null;
                if (t != null && t.m_saddleItem == null && _skySaddle != null) t.m_saddleItem = _skySaddle.GetComponent<ItemDrop>();
            }
            // 0.1.121 (Fire): the ground mounts take the Beast saddle (the Sky saddle until it exists).
            foreach (var c in new[] { _eikthyr, _bjorn, _brute, _serpent, _bonemaw })
            {
                var t = c != null ? c.GetComponent<Tameable>() : null;
                GameObject saddle = _beastSaddle != null ? _beastSaddle : _skySaddle;
                if (t != null && saddle != null && (t.m_saddleItem == null || t.m_saddleItem.gameObject != saddle)) t.m_saddleItem = saddle.GetComponent<ItemDrop>();
            }
        }

        // [worldgen]'s saddle fit (Tools\FiresMounts_Saddles\saddle_fit.json, 10-07): the back bone the saddle sits on, the seat
        // point in that bone's local space, and the saddle's forward / up there. The rider sits [Mounts.Flyers] RiderRaise above
        // and RiderForward ahead of it (FiresFlyerFlight.PoseSeat re-poses it live).
        private sealed class SeatBone
        {
            public string Path;
            public Vector3 Seat, Forward, Up;
            public string Gear;   // 0.1.108: the saddle mesh in the firesmountsgear bundle, fitted at this same pose (FiresSaddleVisual)
            public float FitLossy;   // 0.1.109: the bone's lossy scale [worldgen] fitted at; the saddle scales with the mount from there
            public float RiderRaise, RiderForward;   // 0.1.117: [saddles]' measured rider offset from the seat ("rider": [raise, forward], metres)
        }

        private static readonly SeatBone ModerSeat = new SeatBone
        {
            Path = "Visual/Armature/Root/Hips/Spine/Spine1",
            Gear = "FiresSaddle_Moder",
            FitLossy = 105f,
            // 0.1.117 ([saddles] 10-08 final fit, Fire's "v1": between the wings, on the feathers).
            Seat = new Vector3(7e-06f, 0.002284f, -0.003313f),
            Forward = new Vector3(-0.0004f, 0.978f, 0.2088f),
            Up = new Vector3(0.002f, 0.2088f, -0.978f),
            RiderRaise = 0.051f, RiderForward = 0.466f,
        };

        // [worldgen]'s Mini Fader fit (10-08, saddle_fit.json "fader", on the Walk clip at t 0): behind the shoulders, between spine_2 and chest.
        private static readonly SeatBone FaderSeat = new SeatBone
        {
            Path = "Visual/metarig/Root/CoG/hip/spine_1/spine_2",
            Gear = "FiresSaddle_Fader",
            FitLossy = 45f,   // 0.1.117: [saddles] refitted at x0.45 (the Fader's size since 0.1.113)
            Seat = new Vector3(0.000164f, 0.005867f, -0.004336f),
            Forward = new Vector3(-0.0013f, 0.9995f, 0.0331f),
            Up = new Vector3(0.0379f, 0.0331f, -0.9987f),
            RiderRaise = -0.059f, RiderForward = 0.467f,
        };

        // 0.1.120: [saddles]' new_mounts_fit.json (first pass: the skin top over the back at scale 1; no saddle mesh yet).
        private static readonly SeatBone EikthyrSeat = new SeatBone
        {
            Path = "Visual/Armature/Root2/Pelvis/Spine/Spine1/Spine2", FitLossy = 200f,
            Seat = new Vector3(0.000223f, 0.001292f, -0.002788f), Forward = new Vector3(-0.0508f, 0.9812f, 0.1861f), Up = new Vector3(0.021f, 0.1873f, -0.9821f),
        };
        private static readonly SeatBone BjornSeat = new SeatBone
        {
            Path = "Visual/Armature/Root/CoG/Hip/Spine_01/Spine_02/Chest", FitLossy = 100f,
            Seat = new Vector3(-0.000565f, 0.004276f, 0.007322f), Forward = new Vector3(-0.1027f, 0.994f, 0.0366f), Up = new Vector3(0.0257f, -0.0342f, 0.9991f),
        };
        // 0.1.126 (round 25/26: the Hildir brothers' body never drew on our clone, its "_hildir" materials, though every renderer was
        // enabled and visible): the plain GoblinBrute, same spine, no shaman. The seat is the old ShamanHip point - 0.1 m on Neck2.
        private static readonly SeatBone BruteSeat = new SeatBone
        {
            Path = "Visual/Armature/Root/Hip2/Hip/Spine1/Spine2/Spine3/Neck/Neck2", FitLossy = 70f,
            Seat = new Vector3(0.000336f, 0.000379f, 0.002925f), Forward = new Vector3(0.1132f, 0.9586f, -0.2614f), Up = new Vector3(0f, 0.2631f, 0.9648f),
        };
        private const float EikthyrScale = 0.59f, BjornScale = 0.629f, BruteScale = 0.57f, BruteWalkBoost = 1.6f;
        // 0.1.122: on Tail1 (the head bone yaws to bite), 0.55 m above the spine at scale 1; first pass, refined from Fire's shots.
        private static readonly SeatBone SerpentSeat = new SeatBone
        {
            Path = "Visual/Armature/Root/Main/Tail2/Tail1", FitLossy = 100f,
            Seat = new Vector3(0f, 0f, 0.0055f), Forward = new Vector3(0.0027f, 1f, 0f), Up = new Vector3(0f, 0f, 1f),
        };
        private static readonly SeatBone BonemawSeat = new SeatBone
        {
            Path = "Visual/Armature/Root/Main/Tail2/Tail1", FitLossy = 100f,
            Seat = new Vector3(0.001264f, -0.000148f, 0.005351f), Forward = new Vector3(0.1009f, 0.9949f, 0.0037f), Up = new Vector3(0.2298f, -0.027f, 0.9729f),
        };
        private const float SerpentScale = 0.4f, BonemawScale = 0.4f;

        // Eikthyr, Bjorn, the brute: the Fader's ground ride, its own attacks, no glide / climb / side leaps, a smaller jump.
        private static void BuildGroundMounts()
        {
            if (MountConfig.GroundMountsEnabled == null || !MountConfig.GroundMountsEnabled.Value) return;
            _eikthyr = _eikthyr ?? GroundMount("Eikthyr", MountNames.EikthyrPrefab, "Eikthyr", EikthyrScale, EikthyrSeat, null);
            _bjorn = _bjorn ?? GroundMount("Bjorn", MountNames.BjornPrefab, "Bjorn", BjornScale, BjornSeat, null);
            _serpent = _serpent ?? WaterMount("Serpent", MountNames.SerpentPrefab, "Sea serpent", SerpentScale, SerpentSeat);
            _bonemaw = _bonemaw ?? WaterMount("BonemawSerpent", MountNames.BonemawPrefab, "Bonemaw", BonemawScale, BonemawSeat);
            _brute = _brute ?? GroundMount("GoblinBrute", MountNames.BrutePrefab, "Fuling brute", BruteScale, BruteSeat, go =>
            {
                // Fire: "its walk sped up for travel".
                var ch = go.GetComponent<Character>();
                if (ch != null) { ch.m_walkSpeed *= BruteWalkBoost; ch.m_speed *= BruteWalkBoost; ch.m_runSpeed *= BruteWalkBoost; }
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] brute mount: plain GoblinBrute, speeds x{BruteWalkBoost}");
            });
        }

        // The water pair: the same ride on vanilla's swimmers (they keep to the surface by themselves), no jump, off at the shore.
        private static GameObject WaterMount(string vanilla, string name, string display, float scale, SeatBone seat)
        {
            var go = GroundMount(vanilla, name, display, scale, seat, null);
            var ride = go != null ? go.GetComponent<FiresFaderRide>() : null;
            if (ride != null) { ride.Jumps = false; ride.SwimOnly = true; }
            // 0.1.124 (round 24: "the camera ray hits 'WaterSurface' … FiresSerpentMount"): the swimmer's body and saddle node ride at
            // or under the water, so the hover ray met the water first. A trigger sphere under the saddle node, kept above the live
            // surface, is what E finds (the body's rigidbody makes it the mount for the hover).
            var sadle = go != null ? go.GetComponentInChildren<Sadle>(true) : null;
            if (sadle != null)
            {
                var lift = new GameObject("FiresAboveWaterInteract");
                lift.layer = sadle.gameObject.layer;
                lift.transform.SetParent(sadle.transform, false);
                var sphere = lift.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                lift.AddComponent<AboveWaterInteract>();
            }
            return go;
        }

        private static GameObject GroundMount(string vanilla, string name, string display, float scale, SeatBone seat, System.Action<GameObject> extra)
        {
            var src = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(vanilla) : null;
            if (src == null) { FiresLogger.LogWarning($"no vanilla '{vanilla}' for the {display} mount"); return null; }
            var go = CloneUnderHolder(src, name);
            extra?.Invoke(go);
            Flyerize(go, scale, display, 0f, 0f, null, seat, null, flies: false);
            var ride = go.GetComponent<FiresFaderRide>();
            if (ride != null)
            {
                ride.Glides = false; ride.Climbs = false; ride.SideLeaps = false;
                ride.LeapScale = 0.4f; ride.JumpTrigger = ""; ride.Label = display;
            }
            return go;
        }

        private static readonly SeatBone DrakeSeat = new SeatBone
        {
            Path = "Visual/Hatchling_mountain/Armature/Root/Spine1",
            Gear = "FiresSaddle_Drake",
            FitLossy = 160f,
            Seat = new Vector3(1e-06f, 0.001489f, 0.000778f),
            Forward = new Vector3(0.0001f, 0.9944f, 0.1054f),
            Up = new Vector3(0.001f, -0.1054f, 0.9944f),
            RiderRaise = -0.235f, RiderForward = 0.418f,
        };


        // Shared flyer treatment: scale (before any Awake, so Character/Rigidbody initialise at the final size), only its own
        // attack items (for the rider), no drops, no boss/global-key hooks, calm AI, Tameable + Sadle seat. They start on the ground with no AI
        // take-offs (0.1.102); the MonsterAI fly speeds are left as the vanilla prefab has them, for the rider's flight.
        // The mount's attacks in the rider's order. The possession engine takes the first enemy attack as Main (LMB) and the next
        // as Special (MMB). The vanilla Moder's first item is its taunt (a roar with no damage), so the order is set here: bite on
        // LMB, frost breath on MMB, then spit and claws on Alt + wheel.
        private static readonly string[] ModerAttackOrder = { "dragon_bite", "dragon_coldbreath", "dragon_spit_shotgun", "dragon_claw_left", "dragon_claw_right" };

        // The Fader's: bite on LMB, flame breath on MMB, then claws, spin, fissure, wall of fire and meteors on Alt + wheel. Its
        // phase-two "_Intense" versions and the roar (the boss's add summon) stay off a mount.
        private static readonly string[] FaderAttackOrder = { "Fader_Bite", "Fader_Flamebreath", "Fader_Claw_Left", "Fader_Claw_Right", "Fader_Spin",
                                                              "Fader_Fissure", "Fader_WallOfFire", "Fader_Meteors" };

        /// <summary>The creature's attack items without the no-damage ones (taunts: attack type None), in <paramref name="order"/> first.</summary>
        private static GameObject[] MountAttackItems(GameObject[] items, string[] order, bool dropUnlisted = false)
        {
            if (items == null) return new GameObject[0];
            var kept = new List<GameObject>();
            foreach (var item in items)
            {
                var drop = item != null ? item.GetComponent<ItemDrop>() : null;
                var attack = drop != null ? drop.m_itemData.m_shared.m_attack : null;
                if (drop == null || attack == null || attack.m_attackType == Attack.AttackType.None) continue;
                kept.Add(item);
            }
            if (order == null || order.Length == 0) return kept.ToArray();
            if (dropUnlisted) kept.RemoveAll(g => System.Array.IndexOf(order, g.name) < 0);
            int Rank(GameObject g) { int i = System.Array.IndexOf(order, g.name); return i < 0 ? order.Length + kept.IndexOf(g) : i; }
            kept.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            return kept.ToArray();
        }

        // flies = false (the Mini Fader): a ground mount with FiresFaderRide (leap + glide) instead of FiresFlyerFlight, its rider raised by
        // [Mounts.Fader] RiderRaise / RiderForward, and only the attacks its order names.
        private static void Flyerize(GameObject go, float scale, string displayName, float seatHeight, float seatForward, string foodCsv,
            SeatBone seatBone, string[] attackOrder = null, bool flies = true)
        {
            go.transform.localScale = go.transform.localScale * scale;

            var hum = go.GetComponent<Humanoid>();
            if (hum != null)
            {
                // 0.1.104 (Fire 10-07, "when riding the Mini Moder we do Mini Moder attacks"): the creature's own attack items stay
                // (m_defaultItems: the Moder's bite / claws / breath, the drake's spit) for the rider's LMB / MMB. CalmAI keeps it from
                // seeking fights, and [Mounts.Flyers] AttackDamage scales what they deal. The random gear is still dropped.
                hum.m_defaultItems = MountAttackItems(hum.m_defaultItems, attackOrder, dropUnlisted: !flies);
                hum.m_randomWeapon = new GameObject[0];
                hum.m_randomArmor = new GameObject[0];
                hum.m_randomShield = new GameObject[0];
                hum.m_randomSets = new Humanoid.ItemSet[0];
                hum.m_randomItems = new Humanoid.RandomItem[0];
                hum.m_unarmedWeapon = null;
            }
            var ch = go.GetComponent<Character>();
            if (ch != null)
            {
                ch.m_name = displayName;
                ch.m_group = "firesflyer";
                ch.m_boss = false;
                ch.m_bossEvent = "";
                ch.m_defeatSetGlobalKey = "";   // Moder sets "defeated_dragon" here - a clone must never set it
                ch.m_dontHideBossHud = false;
                ch.m_dreamCinematic = "";
                ch.m_faction = Character.Faction.AnimalsVeg;
                ch.m_aiSkipTarget = true;
                if (ch.m_health > 1500f) ch.m_health = 1500f;
                // Fire's hand test (10-07): the Dragon / Hatchling prefabs start in the air (m_flying), so every spawn, hatch and
                // reload took off. Ours start on the ground; they fly only under a rider (FiresFlyerFlight).
                ch.m_flying = false;
            }
            foreach (var cd in go.GetComponentsInChildren<CharacterDrop>(true)) UnityEngine.Object.DestroyImmediate(cd);

            var ai = go.GetComponent<MonsterAI>();
            if (ai != null)
            {
                MountFactory.CalmAI(ai);
                // No AI take-offs (the Moder's m_randomFly lifted off every 60 s and wandered 35 m out at 10-20 m up).
                ai.m_randomFly = false;
                ai.m_spawnMessage = "";
                ai.m_deathMessage = "";
                ai.m_alertedMessage = "";
                ai.m_consumeItems = ResolveItems(foodCsv);
            }

            var tame = go.GetComponent<Tameable>() ?? go.AddComponent<Tameable>();
            tame.m_commandable = true;
            tame.m_tamingTime = Mathf.Max(1f, MountConfig.DrakeTameMinutes.Value * 60f);
            if (_skySaddle != null) tame.m_saddleItem = _skySaddle.GetComponent<ItemDrop>();

            // Seat node on the back. Sadle.Awake finds the Character via GetComponentInParent; Tameable toggles the node
            // active with the saddle ZDO flag. On the back bone [worldgen] fitted the saddle to (0.1.99), so the rider moves
            // with the body when the flight model banks and pitches it; without that bone, pre-scale local on the root as before.
            var bone = seatBone != null ? go.transform.Find(seatBone.Path) : null;
            var sadelNode = go.transform.Find("Sadel") ?? (bone != null ? bone.Find("Sadel") : null) ?? new GameObject("Sadel").transform;
            if (bone != null)
            {
                sadelNode.SetParent(bone, false);
                float lossy = Mathf.Max(bone.lossyScale.x, 0.0001f);
                float raise = seatBone.RiderRaise + (flies ? MountConfig.FlyerRiderRaise.Value : MountConfig.FaderRiderRaise.Value);
                float ahead = seatBone.RiderForward + (flies ? MountConfig.FlyerRiderForward.Value : MountConfig.FaderRiderForward.Value);
                sadelNode.localPosition = seatBone.Seat + seatBone.Up.normalized * (raise / lossy) + seatBone.Forward.normalized * (ahead / lossy);
                sadelNode.localRotation = Quaternion.LookRotation(seatBone.Forward, seatBone.Up);
            }
            else
            {
                sadelNode.SetParent(go.transform, false);
                sadelNode.localPosition = new Vector3(0f, seatHeight, seatForward);
                sadelNode.localRotation = Quaternion.identity;
                FiresLogger.LogWarning($"{go.name}: no seat bone '{seatBone?.Path}', rider seat on the root");
            }
            var sadle = sadelNode.GetComponent<Sadle>() ?? sadelNode.gameObject.AddComponent<Sadle>();
            sadle.m_attachPoint = sadelNode;
            sadle.m_attachAnimation = "attach_lox";
            sadle.m_detachOffset = Vector3.zero;
            sadle.m_maxStamina = MountConfig.MaxStamina.Value;
            sadle.m_hoverText = "$hud_ride";
            if (sadelNode.GetComponent<SphereCollider>() == null)
            {
                var sc = sadelNode.gameObject.AddComponent<SphereCollider>();
                sc.radius = 0.5f / Mathf.Max(sadelNode.lossyScale.x, 0.0001f);   // ~0.5 m in WORLD space at any scale
            }
            tame.m_saddle = sadle;
            if (go.GetComponent<MountProgress>() == null) go.AddComponent<MountProgress>();   // 0.1.106 levels + stamina
            if (go.GetComponent<MountVariant>() == null) go.AddComponent<MountVariant>();     // 0.1.109 colour variants
            if (go.GetComponent<MountSaddleLook>() == null) go.AddComponent<MountSaddleLook>();   // 0.1.117 saddle dyes

            // FiresMountController assumes the horse (gait ramp, hoof audio/FX, forward_speed blend, hitch), so flyers get their
            // own: FiresFlyerFlight (Core's FlightModel while ridden) and FiresFlyerFollow (dismount / death rules).
            if (!flies)
            {
                var ride = go.GetComponent<FiresFaderRide>() ?? go.AddComponent<FiresFaderRide>();
                if (seatBone != null && bone != null)
                {
                    ride.SeatBonePath = seatBone.Path;
                    ride.SeatLocal = seatBone.Seat;
                    ride.SeatForward = seatBone.Forward;
                    ride.SeatUp = seatBone.Up;
                    ride.RiderRaise = seatBone.RiderRaise;
                    ride.RiderForward = seatBone.RiderForward;
                }
                if (go.GetComponent<FiresFlyerFollow>() == null) go.AddComponent<FiresFlyerFollow>();
                AddSaddleVisual(go, seatBone, bone);
                var kinds = new List<string>();
                foreach (var mb in go.GetComponents<MonoBehaviour>()) if (mb != null) kinds.Add(mb.GetType().Name);
                string attacks = hum != null ? string.Join(", ", System.Array.ConvertAll(hum.m_defaultItems, g => g != null ? g.name : "?")) : "";
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] mount prefab registered: {go.name} scale {scale:0.##}, attacks [{attacks}], components [{string.Join(", ", kinds)}]");
                return;
            }
            var flight = go.GetComponent<FiresFlyerFlight>() ?? go.AddComponent<FiresFlyerFlight>();
            if (seatBone != null && bone != null)
            {
                // Serialized onto the prefab's component, so every instance poses its own seat (FiresFlyerFlight.PoseSeat).
                flight.SeatBonePath = seatBone.Path;
                flight.SeatLocal = seatBone.Seat;
                flight.SeatForward = seatBone.Forward;
                flight.SeatUp = seatBone.Up;
                flight.RiderRaise = seatBone.RiderRaise;
                flight.RiderForward = seatBone.RiderForward;
            }
            if (go.GetComponent<FiresFlyerFollow>() == null) go.AddComponent<FiresFlyerFollow>();
            AddSaddleVisual(go, seatBone, bone);

            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] flyer prefab registered: {go.name} scale {scale:0.##}");
        }

        // 0.1.108: [worldgen]'s icon for a runtime clone (Fire approved 10-08), named as the item's prefab; the vanilla source's icon
        // stays when the gear bundle is missing.
        private static void UseIcon(ItemDrop.ItemData.SharedData shared, string item)
        {
            var icon = MountGear.Icon(item);
            if (icon == null) return;
            shared.m_icons = new[] { icon };
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Gear] {item}: its own icon");
        }

        // 0.1.108: [worldgen]'s saddle mesh at the fit's pose, shown while the mount wears a saddle (FiresSaddleVisual, every peer).
        private static void AddSaddleVisual(GameObject go, SeatBone seatBone, Transform bone)
        {
            if (seatBone == null || bone == null || string.IsNullOrEmpty(seatBone.Gear)) return;
            var look = go.GetComponent<FiresSaddleVisual>() ?? go.AddComponent<FiresSaddleVisual>();
            look.GearName = seatBone.Gear;
            look.BonePath = seatBone.Path;
            look.Seat = seatBone.Seat;
            look.Forward = seatBone.Forward;
            look.Up = seatBone.Up;
            look.FitLossy = seatBone.FitLossy;
        }

        // 0.1.108: the whistle's own model (Fire's download, decimated) in place of the silver necklace's meshes; its collider stays
        // for the pickup. Without the gear bundle the stand-in stays.
        private static void UseWhistleModel(GameObject item)
        {
            var model = MountGear.Get(MountGear.WhistleModel);
            if (model == null) return;
            foreach (var r in item.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
            var look = MountGear.Spawn(MountGear.WhistleModel, item.transform);
            if (look != null) FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Gear] {item.name}: whistle model in place of the stand-in");
        }

        private static List<ItemDrop> ResolveItems(string csv)
        {
            var list = new List<ItemDrop>();
            if (string.IsNullOrEmpty(csv)) return list;
            foreach (var raw in csv.Split(','))
            {
                string n = raw.Trim();
                if (n.Length == 0) continue;
                var p = FindVanilla(n, ObjectDB.instance);
                var it = p != null ? p.GetComponent<ItemDrop>() : null;
                if (it != null && !list.Contains(it)) list.Add(it);
            }
            return list;
        }

        // DrakeFood arrives from the server after boot: re-wire the prefab and every live drake.
        public static void RefreshDrakeFood()
        {
            try
            {
                var food = ResolveItems(MountConfig.DrakeFood.Value);
                if (_drake != null) { var a = _drake.GetComponent<MonsterAI>(); if (a != null) a.m_consumeItems = food; }
                foreach (var c in Character.GetAllCharacters())
                    if (c != null && global::Utils.GetPrefabName(c.gameObject) == MountNames.DrakePrefab)
                    { var a = c.GetComponent<MonsterAI>(); if (a != null) a.m_consumeItems = food; }
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresMounts] drake food resync: {ex.Message}"); }
        }

        public static void RefreshTameTime()
        {
            float t = Mathf.Max(1f, MountConfig.DrakeTameMinutes.Value * 60f);
            foreach (var p in new[] { _moder, _drake, _fader }) { var tm = p != null ? p.GetComponent<Tameable>() : null; if (tm != null) tm.m_tamingTime = t; }
        }

        // -- nest piece -------------------------------------------------------------

        private static void BuildNest(ObjectDB db)
        {
            if (_nest == null && ZNetScene.instance != null)
            {
                var src = ZNetScene.instance.GetPrefab("fire_pit") ?? ZNetScene.instance.GetPrefab("piece_brazierfloor01");
                var stone = FindVanilla("Stone", db);
                var crystal = FindVanilla("Crystal", db);
                var tear = FindVanilla("DragonTear", db);
                if (src != null && stone != null && crystal != null && tear != null)
                {
                    _nest = CloneUnderHolder(src, MountNames.ModerNestPiece);
                    // A nest must not burn whoever stands at it: drop the fire pit's damage areas (Aoe, Burning
                    // EffectArea); the Heat/warmth area and the flames stay.
                    foreach (var aoe in _nest.GetComponentsInChildren<Aoe>(true)) UnityEngine.Object.DestroyImmediate(aoe);
                    foreach (var ea in _nest.GetComponentsInChildren<EffectArea>(true))
                        if ((ea.m_type & EffectArea.Type.Burning) != 0) ea.m_type &= ~EffectArea.Type.Burning;
                    // The Fireplace would grab Interact/UseItem (fuel) first: drop it and leave its fire visuals lit.
                    foreach (var fp in _nest.GetComponentsInChildren<Fireplace>(true))
                    {
                        if (fp.m_enabledObject != null) fp.m_enabledObject.SetActive(true);
                        if (fp.m_enabledObjectHigh != null) fp.m_enabledObjectHigh.SetActive(true);
                        if (fp.m_fullObject != null) fp.m_fullObject.SetActive(true);
                        if (fp.m_emptyObject != null) fp.m_emptyObject.SetActive(false);
                        if (fp.m_halfObject != null) fp.m_halfObject.SetActive(false);
                        UnityEngine.Object.DestroyImmediate(fp);
                    }
                    var piece = _nest.GetComponent<Piece>();
                    piece.m_name = "Mount nest";
                    piece.m_description = "A warm brazier nest. Use a Moder egg or a Fader egg on it; after a day it hatches a tame mini Moder or Mini Fader.";
                    piece.m_craftingStation = null;
                    piece.m_resources = new[]
                    {
                        new Piece.Requirement { m_resItem = stone.GetComponent<ItemDrop>(), m_amount = 20, m_recover = true },
                        new Piece.Requirement { m_resItem = crystal.GetComponent<ItemDrop>(), m_amount = 5, m_recover = true },
                        new Piece.Requirement { m_resItem = tear.GetComponent<ItemDrop>(), m_amount = 2, m_recover = true },
                    };
                    if (_nest.GetComponent<FiresNestIncubator>() == null) _nest.AddComponent<FiresNestIncubator>();
                    FiresMountsPlugin.Log.LogDebug($"[FiresMounts] flyer piece registered: {MountNames.ModerNestPiece} (from {src.name})");
                }
            }
            if (_nest == null) return;
            var hammer = FindVanilla("Hammer", db);
            var table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table != null && !table.m_pieces.Contains(_nest)) table.m_pieces.Add(_nest);
        }

        // -- ZNetScene + the Moder egg drop -------------------------------------------

        private static void RegisterInZNetScene(ZNetScene znet)
        {
            var named = s_namedPrefabs?.GetValue(znet) as Dictionary<int, GameObject>;
            var all = new List<GameObject> { _moder, _drake, _fader, _eikthyr, _bjorn, _brute, _serpent, _bonemaw, _skySaddle, _moderEgg, _faderEgg, _drakeEgg, _whistle, _nest };
            all.AddRange(DyePrefabs());
            all.AddRange(GroundItems());
            foreach (var go in all)
            {
                if (go == null || znet.GetPrefab(go.name) != null) continue;
                znet.m_prefabs.Add(go);
                int hash = go.name.GetStableHashCode();
                if (named != null && !named.ContainsKey(hash)) named.Add(hash, go);
            }
        }

        // Vanilla Moder drops one Moder egg (its trophy/tears are untouched). Edits the live vanilla prefab once.
        private static void AddEggDropToModer()
        {
            if (_eggDropAdded || _moderEgg == null || ZNetScene.instance == null) return;
            var dragon = ZNetScene.instance.GetPrefab("Dragon");
            var cd = dragon != null ? dragon.GetComponent<CharacterDrop>() : null;
            if (cd == null) return;
            if (!cd.m_drops.Exists(d => d != null && d.m_prefab == _moderEgg))
                cd.m_drops.Add(new CharacterDrop.Drop
                {
                    m_prefab = _moderEgg, m_amountMin = 1, m_amountMax = 1, m_chance = 1f,
                    m_onePerPlayer = false, m_levelMultiplier = false, m_dontScale = true,
                });
            _eggDropAdded = true;
            FiresMountsPlugin.Log.LogDebug("[FiresMounts] Moder (Dragon) now drops 1 FiresModerEgg");
        }

        // Vanilla Fader drops one Fader egg (Fire 10-08: "like the Moder egg, one per kill"). Edits the live vanilla prefab once.
        private static void AddEggDropToFader()
        {
            if (_faderEggDropAdded || _faderEgg == null || ZNetScene.instance == null) return;
            var fader = ZNetScene.instance.GetPrefab("Fader");
            var cd = fader != null ? fader.GetComponent<CharacterDrop>() : null;
            if (cd == null) return;
            if (!cd.m_drops.Exists(d => d != null && d.m_prefab == _faderEgg))
                cd.m_drops.Add(new CharacterDrop.Drop
                {
                    m_prefab = _faderEgg, m_amountMin = 1, m_amountMax = 1, m_chance = 1f,
                    m_onePerPlayer = false, m_levelMultiplier = false, m_dontScale = true,
                });
            _faderEggDropAdded = true;
            FiresMountsPlugin.Log.LogDebug("[FiresMounts] Fader now drops 1 FiresFaderEgg");
        }

        // Vanilla Mountain drakes (Hatchling) drop a Drake egg at [Mounts.Flyers] DrakeEggChance (Fire 10-08). Edits the live prefab once.
        private static void AddEggDropToDrake()
        {
            if (_drakeEggDropAdded || _drakeEgg == null || ZNetScene.instance == null) return;
            var drake = ZNetScene.instance.GetPrefab("Hatchling");
            var cd = drake != null ? drake.GetComponent<CharacterDrop>() : null;
            if (cd == null) return;
            if (!cd.m_drops.Exists(d => d != null && d.m_prefab == _drakeEgg))
                cd.m_drops.Add(new CharacterDrop.Drop
                {
                    m_prefab = _drakeEgg, m_amountMin = 1, m_amountMax = 1, m_chance = Mathf.Clamp01(MountConfig.DrakeEggChance.Value),
                    m_onePerPlayer = false, m_levelMultiplier = false, m_dontScale = true,
                });
            _drakeEggDropAdded = true;
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] Hatchling now drops a FiresDrakeEgg at {MountConfig.DrakeEggChance.Value:P0}");
        }

        // -- drake taming gate --------------------------------------------------------

        // Drakes only eat (and so only tame) once Bonemass is down. MonsterAI.CanConsume is the single gate both the
        // food search and the eat step use; the global key is world state every peer has, so the zone owner decides.
        [HarmonyPatch(typeof(MonsterAI), "CanConsume")]
        internal static class DrakeConsumeGate
        {
            [HarmonyPrefix]
            private static bool Prefix(MonsterAI __instance, ref bool __result)
            {
                if (__instance == null || ZoneSystem.instance == null) return true;
                if (global::Utils.GetPrefabName(__instance.gameObject) != MountNames.DrakePrefab) return true;
                if (ZoneSystem.instance.GetGlobalKey("defeated_bonemass")) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
        internal static class ObjectDBCopyPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ObjectDB __instance) => EnsureRegistered(__instance);
        }
    }
}
