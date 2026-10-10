using BepInEx.Configuration;

namespace FiresMounts
{
    /// <summary>
    /// BepInEx config for FiresMounts. Server-authoritative tuning values are read on the server; clients carry
    /// the binds harmlessly. Keep section/key names free of ' " [ ] = tab (see reference_bepinex_config_key_chars).
    /// </summary>
    public static class MountConfig
    {
        public static ConfigEntry<bool> Enabled;

        // Movement tuning (multipliers over the vanilla Sadle baseline our controller derives from).
        public static ConfigEntry<float> WalkSpeed;
        public static ConfigEntry<float> RunSpeed;
        public static ConfigEntry<float> TurnSpeed;
        public static ConfigEntry<float> Acceleration;

        // Body + seat (tuned via the seat-render loop; WYSIWYG with FiresMountBuilder.RenderSeat).
        public static ConfigEntry<float> HorseScale;
        public static ConfigEntry<float> ModelYaw;   // live yaw of the visual model (find the value where visual == move dir)
        public static ConfigEntry<float> SeatHeight;
        public static ConfigEntry<float> SeatForward;
        public static ConfigEntry<float> SeatPitch;
        public static ConfigEntry<string> CoatColor;
        public static ConfigEntry<string> Coat;   // randomized-per-horse coat texture selector
        public static ConfigEntry<string> Mane;   // randomized-per-horse mane style selector
        public static ConfigEntry<string> Tail;   // randomized-per-horse tail style selector

        // Mount stamina + taming.
        public static ConfigEntry<float> MaxStamina;
        public static ConfigEntry<float> TameTimeMinutes;
        public static ConfigEntry<string> TamingFood;   // CSV of item prefab names the horse eats to tame

        // Ride enhancements (superset over vanilla Sadle).
        public static ConfigEntry<bool> EnableRiderJump;
        public static ConfigEntry<float> AnimSpeedTune;
        public static ConfigEntry<float> BellyClearance;
        public static ConfigEntry<bool> UseVanillaSteering;   // A/B: vanilla look-dir steering vs Fires reins steering
        public static ConfigEntry<float> GaitRampRate;        // how fast the mount eases walk->gallop holding forward
        public static ConfigEntry<float> GaitFloor;           // walk speed the ramp starts at
        public static ConfigEntry<bool> ShiftSprint;          // Shift punches straight to full sprint (skip the ramp)
        public static ConfigEntry<float> FootstepVolume;      // looping hoof/gait sound volume
        public static ConfigEntry<bool> FootstepFX;           // vanilla per-biome footstep VFX + material SFX
        public static ConfigEntry<bool> GaitLoops;            // the custom walk/trot/run WAV loops (off: vanilla steps only)
        public static ConfigEntry<bool> AutoHitch;            // dismounting hitches the horse in place

        // Flying mounts (Mounts.Flyers). Scales + seats are boot-time/visual (local); hatch/food/tame are server-synced.
        public static ConfigEntry<bool> FlyersEnabled;
        public static ConfigEntry<float> ModerScale;
        public static ConfigEntry<float> DrakeScale;
        public static ConfigEntry<float> HatchSeconds;
        public static ConfigEntry<string> DrakeFood;
        public static ConfigEntry<float> DrakeEggChance;
        public static ConfigEntry<bool> FightWhenFollowing;
        public static ConfigEntry<bool> GroundMountsEnabled;
        public static ConfigEntry<float> DrakeTameMinutes;
        public static ConfigEntry<float> ModerSeatHeight, ModerSeatForward, DrakeSeatHeight, DrakeSeatForward;
        // The rider on a flyer's back bone (saddle_fit.json's seat point): metres above / ahead of it. Live.
        public static ConfigEntry<float> FlyerRiderRaise, FlyerRiderForward;
        // LMB / MMB while riding a flyer = the mount's own attacks; their damage x AttackDamage. Live.
        public static ConfigEntry<bool> FlyerMountAttacks;
        public static ConfigEntry<float> FlyerAttackDamage;
        // RMB while riding a flyer: vanilla's bubble shield (the Staff of Protection's) on the mount and the rider. Live.
        public static ConfigEntry<float> FlyerShieldSeconds, FlyerShieldCooldown;

        // Mini Fader (Mounts.Fader): scale is boot-time (local), the rider's seat live (local), leap / glide gameplay (server-synced).
        public static ConfigEntry<float> FaderScale, FaderRiderRaise, FaderRiderForward;
        public static ConfigEntry<float> FaderLeapHeight, FaderLeapForward, FaderLeapChargeSeconds;
        public static ConfigEntry<float> FaderGlideSink, FaderGlideSpeed, FaderGlideTurn;
        public static ConfigEntry<bool> FaderClimb;
        public static ConfigEntry<float> FaderClimbSpeed, FaderClimbDrain;

        // Mount levels + stamina (Mounts.Levels, all server-synced): the switch, an XP multiplier, the level-1 pools by mount.
        public static ConfigEntry<bool> LevelsEnabled;
        public static ConfigEntry<float> LevelsXpRate, PoolModer, PoolDrake, PoolFader;

        // Mount whistle (Mounts.Whistle): key + tap window + sound local; cooldown + distance server-synced.
        public static ConfigEntry<bool> WhistleEnabled;
        public static ConfigEntry<UnityEngine.KeyCode> WhistleKey;
        public static ConfigEntry<float> WhistleDoubleTapSeconds, WhistleCooldown, WhistleDistance;
        public static ConfigEntry<string> WhistleSound;

        // Diagnostics.
        public static ConfigEntry<bool> VerboseLog;

        public static void Bind(ConfigFile cfg)
        {
            const string GENERAL = "Mounts.General";
            const string MOVE = "Mounts.Movement";
            const string TAME = "Mounts.Taming";

            Enabled = cfg.Bind(GENERAL, "Enabled", true,
                "Master switch. When false the mod registers nothing and no mounts spawn.");

            // numeric binds carry AcceptableValueRange so Configuration Manager renders SLIDERS, not text boxes
            const string BODY = "Mounts.Body";
            HorseScale = cfg.Bind(BODY, "HorseScale", 1.4f, new ConfigDescription(
                "Uniform scale applied to the horse (1.0 = imported size, too small next to the player).",
                new AcceptableValueRange<float>(0.5f, 3f)));
            ModelYaw = cfg.Bind(BODY, "ModelYaw", 0f, new ConfigDescription(
                "Yaw (deg) of the visual model on the animator node. 0 = the rig faces straight down its own forward, " +
                "which matches the move/steer axis (a small non-zero value made the horse crab-walk slightly off-center " +
                "while going straight). Live-tweakable if the rig is ever re-baked to a different facing.",
                new AcceptableValueRange<float>(-180f, 180f)));
            SeatHeight = cfg.Bind(BODY, "SeatHeight", 1.6f, new ConfigDescription(
                "Rider seat height on the horse (pre-scale local metres).", new AcceptableValueRange<float>(0.5f, 2.5f)));
            SeatForward = cfg.Bind(BODY, "SeatForward", 0.3f, new ConfigDescription(
                "Rider seat forward offset (pre-scale local metres; + toward head).", new AcceptableValueRange<float>(-1.5f, 1.5f)));
            SeatPitch = cfg.Bind(BODY, "SeatPitch", 11f, new ConfigDescription(
                "Rider lean in degrees (positive leans forward, toward the head).", new AcceptableValueRange<float>(-45f, 45f)));
            CoatColor = cfg.Bind(BODY, "CoatColor", "#FFFFFF",
                "Tint over the horse body/mane (HTML hex). The source albedo is a white coat, so this recolors the " +
                "breed: #FFFFFF white, #8B5A2B chestnut, #4A3728 dark bay, #B8B8B8 dapple gray.");
            Coat = cfg.Bind(BODY, "Coat", "Random",
                "Which coat each spawned horse wears (real HAP coat textures, not a tint). 'Random' rolls one per " +
                "horse and locks it in (persists per animal, same for all players). Or pin one for every horse: " +
                "Brown, Gray, Gray2, Palomino, White.");
            Mane = cfg.Bind(BODY, "Mane", "Random",
                "Mane style per spawned horse. 'Random' rolls one per horse and locks it in (persists per animal); a " +
                "bald (mane-less) horse is a rare ~4% roll. Or pin for every horse: V, Long, Short, Old, None. " +
                "(Tails always spawn — every horse has one.)");
            Tail = cfg.Bind(BODY, "Tail", "Random",
                "Tail style per spawned horse. 'Random' rolls one per horse and locks it in (persists per animal). " +
                "Or pin one for every horse: New, Old.");

            WalkSpeed = cfg.Bind(MOVE, "WalkSpeed", 3f, new ConfigDescription(
                "Speed holding W (m/s). The gait blend is cadence-matched now: ~1.7 = walk, ~3.5 = trot, ~5.5 = " +
                "canter — pick a value in that range for a walking/trotting ride (the horse actually walks when slow).",
                new AcceptableValueRange<float>(0.5f, 60f)));
            RunSpeed = cfg.Bind(MOVE, "RunSpeed", 10f, new ConfigDescription(
                "Speed holding W+Shift (m/s). ~8 = gallop, ~10 = sprint (the animation's top authored speed). Above " +
                "10 the hooves are scaled up to keep pace (clean to ~30, then it starts to foot-slide).",
                new AcceptableValueRange<float>(1f, 120f)));
            TurnSpeed = cfg.Bind(MOVE, "TurnSpeed", 120f, new ConfigDescription(
                "Horse turn rate (deg/s).", new AcceptableValueRange<float>(30f, 300f)));
            Acceleration = cfg.Bind(MOVE, "Acceleration", 5f, new ConfigDescription(
                "How hard the horse ramps to its target speed (vanilla creatures use ~1 = a slow ~2s ramp). That " +
                "ramp is where the gait blend sweeps through every gear and jerks — raise this so the horse snaps to " +
                "speed and the jerky window collapses. High = near-instant (the physics force cap keeps it safe).",
                new AcceptableValueRange<float>(1f, 20f)));

            MaxStamina = cfg.Bind(TAME, "MaxStamina", 120f, new ConfigDescription(
                "Mount stamina pool used while running.", new AcceptableValueRange<float>(20f, 1000f)));
            TameTimeMinutes = cfg.Bind(TAME, "TameTimeMinutes", 20f, new ConfigDescription(
                "Real minutes of feeding to fully tame a wild horse.", new AcceptableValueRange<float>(0.1f, 120f)));
            TamingFood = cfg.Bind(TAME, "TamingFood", "Carrot,Turnip,Blueberries,Raspberries,Barley,Dandelion",
                "Comma-separated item prefab names the horse eats to tame/breed.");

            EnableRiderJump = cfg.Bind(MOVE, "EnableRiderJump", true,
                "Allow the rider to make the horse jump with the Jump key (vanilla mounts cannot). Jumping while " +
                "standing still makes the horse REAR instead of hopping.");

            AutoHitch = cfg.Bind(MOVE, "AutoHitchOnDismount", true,
                "Dismounting hitches the horse: it stands exactly where you left it (no grazing wander) until you " +
                "mount it again or pet it to Follow. OFF = a dismounted horse grazes/wanders like any tame.");
            UseVanillaSteering = cfg.Bind(MOVE, "UseVanillaSteering", false,
                "A/B TEST. ON = vanilla Lox steering (the horse moves toward where you LOOK, camera-relative). OFF = " +
                "Fires reins steering (A/D turn the horse, W drives the horse's OWN forward). Toggle live, no restart — " +
                "if the horse moves correctly on vanilla but not Fires, the bug is in our steering direction.");
            AnimSpeedTune = cfg.Bind(MOVE, "AnimSpeedTune", 1f, new ConfigDescription(
                "Multiplier on the hoof-to-ground animation speed matching. Raise if legs look too slow, lower if too fast.",
                new AcceptableValueRange<float>(0.1f, 3f)));
            BellyClearance = cfg.Bind(MOVE, "BellyClearance", 1f, new ConfigDescription(
                "Obstacles whose top is below this height (pre-scale metres above the hooves) stop colliding on contact — " +
                "the horse runs straight over them. Terrain, floors and creatures always stay solid.",
                new AcceptableValueRange<float>(0f, 3f)));

            GaitRampRate = cfg.Bind(MOVE, "GaitRampRate", 2.5f, new ConfigDescription(
                "How fast the mount eases from a walk up to a gallop while you hold forward (m/s each second). Higher " +
                "reaches a gallop sooner; lower gives a longer, more gradual climb through the gaits.",
                new AcceptableValueRange<float>(0.5f, 12f)));
            GaitFloor = cfg.Bind(MOVE, "GaitFloor", 1.5f, new ConfigDescription(
                "The starting speed the ramp begins at, i.e. the slowest gait (m/s). ~1.5 = a walk; raise for a faster " +
                "minimum. The gallop cap at the top of the ramp is RunSpeed.",
                new AcceptableValueRange<float>(0.5f, 6f)));
            ShiftSprint = cfg.Bind(MOVE, "ShiftSprint", true,
                "ON: holding Shift climbs to full sprint much faster (a quick sub-second sweep UP through the gaits, so " +
                "walk/trot/canter/gallop still play) — plain forward eases up gently. OFF: Shift does nothing special; " +
                "forward always ramps at the normal rate. (A hard snap is intentionally avoided: it would skip straight " +
                "to the gallop clip and hide the gaits.)");

            GaitLoops = cfg.Bind("Mounts.Audio", "GaitLoops", false,
                "Play the custom walk/trot/run hoof loops (BepInEx/config/FiresMountSounds/). OFF (default): the horse uses the " +
                "game's normal per-step footstep sounds and dust only. The loops stacked on those steps and sounded echoey before 1.0.");
            FootstepVolume = cfg.Bind("Mounts.Audio", "FootstepVolume", 0.5f, new ConfigDescription(
                "Volume of the horse's looping hoof/gait sounds (walk/trot/run), scaled by speed. 0 = silent. The clips " +
                "live in BepInEx/config/FiresMountSounds/ (walk.wav / trot.wav / run.wav, 16-bit PCM) and can be swapped " +
                "without a rebuild.", new AcceptableValueRange<float>(0f, 2f)));

            FootstepFX = cfg.Bind("Mounts.Audio", "FootstepFX", true,
                "Spawn the vanilla per-biome footstep effects (dust/splash/snow VFX + the ground-material footstep SFX a " +
                "player gets) at the horse as it moves, layered under the gait loops. OFF = gait loops only.");

            // sliders apply LIVE to prefabs + spawned mounts (Compose only runs at boot)
            WalkSpeed.SettingChanged += (s, e) => MountFactory.ApplyLiveTuning();
            RunSpeed.SettingChanged += (s, e) => MountFactory.ApplyLiveTuning();
            TurnSpeed.SettingChanged += (s, e) => MountFactory.ApplyLiveTuning();
            Acceleration.SettingChanged += (s, e) => MountFactory.ApplyLiveTuning();
            MaxStamina.SettingChanged += (s, e) => MountFactory.ApplyLiveTuning();
            TameTimeMinutes.SettingChanged += (s, e) => MountFactory.ApplyLiveTuning();
            Coat.SettingChanged += (s, e) => MountFactory.ApplyLiveTuning();
            Mane.SettingChanged += (s, e) => MountFactory.ApplyLiveTuning();
            Tail.SettingChanged += (s, e) => MountFactory.ApplyLiveTuning();

            MountSpawns.Bind(cfg);

            const string FLY = "Mounts.Flyers";
            FlyersEnabled = cfg.Bind(FLY, "Enabled", true,
                "Register the flying mounts (mini Moder, drake), the Sky saddle, the Moder egg and the Moder nest. Restart to apply.");
            ModerScale = cfg.Bind(FLY, "ModerScale", 0.35f, new ConfigDescription(
                "Uniform scale of the mini Moder (a clone of Moder). Restart to apply.", new AcceptableValueRange<float>(0.1f, 1f)));
            DrakeScale = cfg.Bind(FLY, "DrakeScale", 1.6f, new ConfigDescription(
                "Uniform scale of the rideable drake (a clone of the Mountains drake). Restart to apply.", new AcceptableValueRange<float>(0.5f, 4f)));
            HatchSeconds = cfg.Bind(FLY, "HatchSeconds", 0f, new ConfigDescription(
                "Seconds of world time a Moder egg incubates in a Moder nest. 0 = one in-game day.", new AcceptableValueRange<float>(0f, 86400f)));
            GroundMountsEnabled = cfg.Bind(FLY, "GroundMounts", true,
                "Eikthyr, Bjorn and the Fuling brute as rideable ground mounts (tame with firesmounts_tame for now; a Sky saddle). Restart to apply.");
            FightWhenFollowing = cfg.Bind(FLY, "FightWhenFollowing", true,
                "A tamed mini Moder, drake or Mini Fader that follows you (not ridden, not on Stay) fights the enemies around you with its own attacks.");
            DrakeEggChance = cfg.Bind(FLY, "DrakeEggChance", 0.05f, new ConfigDescription(
                "Chance a vanilla Mountain drake (Hatchling) drops a Drake egg, which hatches a tame drake in a mount nest. Restart to apply.",
                new AcceptableValueRange<float>(0f, 1f)));
            DrakeFood = cfg.Bind(FLY, "DrakeFood", "FreezeGland,Crystal",
                "Comma-separated item prefab names a drake eats to tame. Taming only works once Bonemass is defeated.");
            DrakeTameMinutes = cfg.Bind(FLY, "DrakeTameMinutes", 10f, new ConfigDescription(
                "Real minutes of feeding to fully tame a drake.", new AcceptableValueRange<float>(0.1f, 120f)));
            ModerSeatHeight = cfg.Bind(FLY, "ModerSeatHeight", 4.5f, new ConfigDescription(
                "Rider seat height on the mini Moder (pre-scale local metres). Restart to apply.", new AcceptableValueRange<float>(0f, 12f)));
            ModerSeatForward = cfg.Bind(FLY, "ModerSeatForward", 0.5f, new ConfigDescription(
                "Rider seat forward offset on the mini Moder (pre-scale local metres). Restart to apply.", new AcceptableValueRange<float>(-6f, 6f)));
            DrakeSeatHeight = cfg.Bind(FLY, "DrakeSeatHeight", 1.1f, new ConfigDescription(
                "Rider seat height on the drake (pre-scale local metres). Restart to apply.", new AcceptableValueRange<float>(0f, 5f)));
            DrakeSeatForward = cfg.Bind(FLY, "DrakeSeatForward", 0.2f, new ConfigDescription(
                "Rider seat forward offset on the drake (pre-scale local metres, behind the neck). Restart to apply.", new AcceptableValueRange<float>(-3f, 3f)));
            FlyerRiderRaise = cfg.Bind(FLY, "RiderRaiseAdjust", 0f, new ConfigDescription(
                "Metres added to the rider's height on a flyer's saddle (the saddle fit already seats them). Applies live.", new AcceptableValueRange<float>(-0.5f, 1f)));
            FlyerRiderForward = cfg.Bind(FLY, "RiderForwardAdjust", 0f, new ConfigDescription(
                "Metres added to how far forward the rider sits on a flyer's saddle (the saddle fit already seats them). Applies live.", new AcceptableValueRange<float>(-1f, 1f)));
            FlyerMountAttacks = cfg.Bind(FLY, "MountAttacks", true,
                "While riding a flyer, LMB / MMB fire the mount's own main / special attack at the crosshair (Alt + mouse wheel picks the special) and RMB raises a bubble shield, instead of your weapon and block. Applies live.");
            FlyerShieldSeconds = cfg.Bind(FLY, "ShieldSeconds", 10f, new ConfigDescription(
                "How long RMB's bubble shield lasts on the mount and the rider (vanilla's Staff of Protection shield: it absorbs about 200 damage, then breaks). Applies live.",
                new AcceptableValueRange<float>(1f, 60f)));
            FlyerShieldCooldown = cfg.Bind(FLY, "ShieldCooldown", 30f, new ConfigDescription(
                "Seconds before RMB's shield can be raised again. Applies live.", new AcceptableValueRange<float>(0f, 300f)));
            FlyerAttackDamage = cfg.Bind(FLY, "AttackDamage", 0.25f, new ConfigDescription(
                "Damage of a ridden creature mount's attacks as a share of the vanilla creature's (the mini Moder carries the Moder's own breath and claws, the Mini Fader the Fader's). Applies live.",
                new AcceptableValueRange<float>(0f, 2f)));

            const string FADER = "Mounts.Fader";
            FaderScale = cfg.Bind(FADER, "FaderScale", 0.45f, new ConfigDescription(
                "Uniform scale of the Mini Fader (a clone of the Fader). Restart to apply.", new AcceptableValueRange<float>(0.08f, 0.6f)));
            // Fire 10-08 hand test 4: "a bit too mini". A cfg still holding the old default (0.18, never chosen) takes the new one.
            // Fire 10-08 again: "around horse or lox size". x0.45 stands ~3.15 m (the Fader's capsule is 7 m; the Lox's 3.5 m). A cfg still
            // holding an old default (0.18, 0.25), never chosen, takes the new one.
            if (UnityEngine.Mathf.Abs(FaderScale.Value - 0.18f) < 0.0001f || UnityEngine.Mathf.Abs(FaderScale.Value - 0.25f) < 0.0001f) FaderScale.Value = 0.45f;
            FaderRiderRaise = cfg.Bind(FADER, "RiderRaiseAdjust", 0f, new ConfigDescription(
                "Metres added to the rider's height on the Mini Fader's saddle (the saddle fit already seats them). Applies live.", new AcceptableValueRange<float>(-0.5f, 1f)));
            FaderRiderForward = cfg.Bind(FADER, "RiderForwardAdjust", 0f, new ConfigDescription(
                "Metres added to how far forward the rider sits on the Mini Fader's saddle (the saddle fit already seats them). Applies live.", new AcceptableValueRange<float>(-1f, 1f)));
            FaderLeapHeight = cfg.Bind(FADER, "LeapHeight", 14f, new ConfigDescription(
                "Height in metres of a fully charged leap (Space held for LeapChargeSeconds, then released). A tap hops 3 m.", new AcceptableValueRange<float>(3f, 40f)));
            FaderLeapForward = cfg.Bind(FADER, "LeapForward", 20f, new ConfigDescription(
                "Distance in metres a fully charged leap carries forward (a running leap keeps its run speed if that is faster). A tap hops 5 m.", new AcceptableValueRange<float>(5f, 60f)));
            FaderLeapChargeSeconds = cfg.Bind(FADER, "LeapChargeSeconds", 1.2f, new ConfigDescription(
                "Seconds Space must be held on the ground for a full leap (charging starts after 0.3 s).", new AcceptableValueRange<float>(0.4f, 3f)));
            FaderGlideSink = cfg.Bind(FADER, "GlideSink", 2.5f, new ConfigDescription(
                "Metres per second a gliding Mini Fader sinks at most (Space held while falling).", new AcceptableValueRange<float>(0.5f, 10f)));
            FaderGlideSpeed = cfg.Bind(FADER, "GlideSpeed", 9f, new ConfigDescription(
                "Forward speed in metres per second a glide eases to.", new AcceptableValueRange<float>(2f, 30f)));
            FaderGlideTurn = cfg.Bind(FADER, "GlideTurn", 60f, new ConfigDescription(
                "Degrees per second A / D turn a glide.", new AcceptableValueRange<float>(10f, 180f)));
            FaderClimb = cfg.Bind(FADER, "WallClimb", true,
                "W into a surface steeper than about 75 degrees climbs it, and it holds on down to 60 (W / S up / down, A / D along it, Space jumps off, over the top it mantles).");
            FaderClimbSpeed = cfg.Bind(FADER, "ClimbSpeed", 3.5f, new ConfigDescription(
                "Metres per second the Mini Fader climbs a wall.", new AcceptableValueRange<float>(0.5f, 12f)));
            FaderClimbDrain = cfg.Bind(FADER, "ClimbDrain", 0f, new ConfigDescription(
                "Stamina a second a mount spends while it climbs a wall (Fire 10-09: none). Applies live.", new AcceptableValueRange<float>(0f, 50f)));

            const string LEVELS = "Mounts.Levels";
            LevelsEnabled = cfg.Bind(LEVELS, "Enabled", true,
                "Mount levels (1-20, XP from riding, flying, kills and feeding) and per-action stamina while ridden. Off: no levels, no flight stamina (vanilla's run drain only).");
            LevelsXpRate = cfg.Bind(LEVELS, "XpRate", 1f, new ConfigDescription(
                "Multiplier on all mount XP.", new AcceptableValueRange<float>(0f, 20f)));
            PoolModer = cfg.Bind(LEVELS, "PoolModer", 260f, new ConfigDescription(
                "The mini Moder's stamina at level 1 (+6% a level). The horse's is [Mounts.Taming] MaxStamina.", new AcceptableValueRange<float>(10f, 1000f)));
            PoolDrake = cfg.Bind(LEVELS, "PoolDrake", 220f, new ConfigDescription(
                "The drake's stamina at level 1 (+6% a level).", new AcceptableValueRange<float>(10f, 1000f)));
            // 0.1.128 (Fire: "much more stamina"): a cfg still holding the old defaults (never chosen) takes the new ones.
            if (UnityEngine.Mathf.Abs(PoolModer.Value - 120f) < 0.01f) PoolModer.Value = 260f;
            if (UnityEngine.Mathf.Abs(PoolDrake.Value - 90f) < 0.01f) PoolDrake.Value = 220f;
            PoolFader = cfg.Bind(LEVELS, "PoolFader", 140f, new ConfigDescription(
                "The Mini Fader's stamina at level 1 (+6% a level).", new AcceptableValueRange<float>(10f, 1000f)));

            const string WHISTLE = "Mounts.Whistle";
            WhistleEnabled = cfg.Bind(WHISTLE, "Enabled", true,
                "With a Mount whistle (FiresMountWhistle) in the inventory, a double tap of Key calls the last mount you rode to your side.");
            WhistleKey = cfg.Bind(WHISTLE, "Key", UnityEngine.KeyCode.H, "The key tapped twice to whistle.");
            WhistleDoubleTapSeconds = cfg.Bind(WHISTLE, "DoubleTapSeconds", 0.35f, new ConfigDescription(
                "Most seconds between the two taps.", new AcceptableValueRange<float>(0.15f, 1f)));
            WhistleCooldown = cfg.Bind(WHISTLE, "Cooldown", 30f, new ConfigDescription(
                "Seconds before the whistle works again.", new AcceptableValueRange<float>(0f, 600f)));
            WhistleDistance = cfg.Bind(WHISTLE, "Distance", 6f, new ConfigDescription(
                "Metres behind you the called mount appears.", new AcceptableValueRange<float>(2f, 30f)));
            WhistleSound = cfg.Bind(WHISTLE, "Sound", "",
                "A ZNetScene effect prefab played where you whistle (empty = silent, until the whistle has its own sound).");

            VerboseLog = cfg.Bind(GENERAL, "VerboseLog", false,
                "Log per-second mount position/velocity/owner (diagnosing movement). Turn off once movement is smooth.");
        }
    }
}
