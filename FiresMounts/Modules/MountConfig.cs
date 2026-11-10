using System.ComponentModel;
using BepInEx.Configuration;
using FiresCore.Sync;

namespace FiresMounts
{
    /// <summary>
    /// BepInEx config for FiresMounts. Server-authoritative tuning values are read on the server; clients carry
    /// the binds harmlessly. Keep section/key names free of ' " [ ] = tab (BepInEx rejects them).
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

        // Player-facing config: plain short descriptions; tuning knobs sit behind Configuration Manager's "Show advanced",
        // developer-only ones are hidden from it entirely (still in the .cfg file).
        internal static ConfigDescription AdvDesc(string d, AcceptableValueBase range = null) => Adv(d, range);

        private static ConfigDescription Adv(string d, AcceptableValueBase range = null) =>
            new ConfigDescription(d, range, new ConfigurationManagerAttributes { IsAdvanced = true });

        private static ConfigDescription Hidden(string d, AcceptableValueBase range = null) =>
            new ConfigDescription(d, range, new BrowsableAttribute(false), new ConfigurationManagerAttributes { IsAdvanced = true });

        public static void Bind(ConfigFile cfg)
        {
            const string GENERAL = "Mounts.General";
            const string MOVE = "Mounts.Movement";
            const string TAME = "Mounts.Taming";

            Enabled = cfg.Bind(GENERAL, "Enabled", true, "Turns the whole mod on or off. Off: no mounts at all.");

            // numeric binds carry AcceptableValueRange so Configuration Manager renders SLIDERS, not text boxes
            const string BODY = "Mounts.Body";
            HorseScale = cfg.Bind(BODY, "HorseScale", 1.4f, Adv("Size of the horse.", new AcceptableValueRange<float>(0.5f, 3f)));
            ModelYaw = cfg.Bind(BODY, "ModelYaw", 0f, Hidden("Turns the horse model on its body (degrees). Leave at 0.", new AcceptableValueRange<float>(-180f, 180f)));
            SeatHeight = cfg.Bind(BODY, "SeatHeight", 1.6f, Adv("How high you sit on the horse.", new AcceptableValueRange<float>(0.5f, 2.5f)));
            SeatForward = cfg.Bind(BODY, "SeatForward", 0.3f, Adv("How far forward you sit on the horse.", new AcceptableValueRange<float>(-1.5f, 1.5f)));
            SeatPitch = cfg.Bind(BODY, "SeatPitch", 11f, Adv("How far you lean forward on the horse (degrees).", new AcceptableValueRange<float>(-45f, 45f)));
            CoatColor = cfg.Bind(BODY, "CoatColor", "#FFFFFF", Adv("Colour tint over the horse (hex, #FFFFFF = no tint)."));
            Coat = cfg.Bind(BODY, "Coat", "Random", Adv("Horse coat: Random (each horse rolls its own) or Brown, Gray, Gray2, Palomino, White for every horse."));
            Mane = cfg.Bind(BODY, "Mane", "Random", Adv("Horse mane: Random (each horse rolls its own) or V, Long, Short, Old, None for every horse."));
            Tail = cfg.Bind(BODY, "Tail", "Random", Adv("Horse tail: Random (each horse rolls its own) or New, Old for every horse."));

            WalkSpeed = cfg.Bind(MOVE, "WalkSpeed", 3f, Adv("Horse speed holding forward (metres a second).", new AcceptableValueRange<float>(0.5f, 60f)));
            RunSpeed = cfg.Bind(MOVE, "RunSpeed", 10f, Adv("Horse speed holding forward and Shift (metres a second).", new AcceptableValueRange<float>(1f, 120f)));
            TurnSpeed = cfg.Bind(MOVE, "TurnSpeed", 120f, Adv("How fast the horse turns (degrees a second).", new AcceptableValueRange<float>(30f, 300f)));
            Acceleration = cfg.Bind(MOVE, "Acceleration", 5f, Adv("How quickly the horse reaches its speed. Higher = snappier.", new AcceptableValueRange<float>(1f, 20f)));

            MaxStamina = cfg.Bind(TAME, "MaxStamina", 120f, Adv("The horse's stamina for running.", new AcceptableValueRange<float>(20f, 1000f)));
            TameTimeMinutes = cfg.Bind(TAME, "TameTimeMinutes", 20f, Adv("Minutes of feeding to tame a wild horse.", new AcceptableValueRange<float>(0.1f, 120f)));
            TamingFood = cfg.Bind(TAME, "TamingFood", "Carrot,Turnip,Blueberries,Raspberries,Barley,Dandelion",
                Adv("Items a horse eats to tame and breed (comma-separated item names)."));

            EnableRiderJump = cfg.Bind(MOVE, "EnableRiderJump", true, "Jump on the horse with the Jump key. Standing still, the horse rears instead.");

            AutoHitch = cfg.Bind(MOVE, "AutoHitchOnDismount", true, "Getting off leaves the horse standing where you left it. Off: it grazes and wanders.");
            UseVanillaSteering = cfg.Bind(MOVE, "UseVanillaSteering", false, Hidden("On: the horse goes where you look, like a Lox. Off: A / D turn the horse."));
            AnimSpeedTune = cfg.Bind(MOVE, "AnimSpeedTune", 1f, Adv("Speed of the leg animation. Raise if the legs look slow, lower if fast.", new AcceptableValueRange<float>(0.1f, 3f)));
            BellyClearance = cfg.Bind(MOVE, "BellyClearance", 1f, Adv("Small obstacles lower than this (metres) are run straight over.", new AcceptableValueRange<float>(0f, 3f)));

            GaitRampRate = cfg.Bind(MOVE, "GaitRampRate", 2.5f, Adv("How fast the horse builds from a walk to a gallop while you hold forward.", new AcceptableValueRange<float>(0.5f, 12f)));
            GaitFloor = cfg.Bind(MOVE, "GaitFloor", 1.5f, Adv("The horse's slowest speed when it starts moving (metres a second).", new AcceptableValueRange<float>(0.5f, 6f)));
            ShiftSprint = cfg.Bind(MOVE, "ShiftSprint", true, "Holding Shift gets the horse to full sprint much faster.");

            GaitLoops = cfg.Bind("Mounts.Audio", "GaitLoops", false, "Extra walk / trot / run hoof sound loops on top of the normal footsteps.");
            FootstepVolume = cfg.Bind("Mounts.Audio", "FootstepVolume", 0.5f, Adv("Volume of the hoof sound loops. 0 = silent.", new AcceptableValueRange<float>(0f, 2f)));

            FootstepFX = cfg.Bind("Mounts.Audio", "FootstepFX", true, "Dust, splash and snow footstep effects and ground sounds under the horse.");

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
            FlyersEnabled = cfg.Bind(FLY, "Enabled", true, "Flying mounts: mini Moder and drake, with the Sky saddle, eggs and the mount nest. Restart to apply.");
            ModerScale = cfg.Bind(FLY, "ModerScale", 0.35f, Adv("Size of the mini Moder. Restart to apply.", new AcceptableValueRange<float>(0.1f, 1f)));
            DrakeScale = cfg.Bind(FLY, "DrakeScale", 1.6f, Adv("Size of the rideable drake. Restart to apply.", new AcceptableValueRange<float>(0.5f, 4f)));
            HatchSeconds = cfg.Bind(FLY, "HatchSeconds", 0f, Adv("Seconds an egg takes to hatch in a mount nest. 0 = one in-game day.", new AcceptableValueRange<float>(0f, 86400f)));
            GroundMountsEnabled = cfg.Bind(FLY, "GroundMounts", true,
                "Ride Eikthyr, Bjorn and the Fuling brute: craft their call at the Workbench, then ride with a Beast saddle. Restart to apply.");
            FightWhenFollowing = cfg.Bind(FLY, "FightWhenFollowing", true, "A tame mini Moder, drake or Mini Fader that follows you fights the enemies around you.");
            DrakeEggChance = cfg.Bind(FLY, "DrakeEggChance", 0.05f, Adv("Chance a Mountain drake drops a Drake egg (0.05 = 5%). Restart to apply.", new AcceptableValueRange<float>(0f, 1f)));
            DrakeFood = cfg.Bind(FLY, "DrakeFood", "FreezeGland,Crystal", Adv("Items a drake eats to tame (comma-separated item names). Works once Bonemass is defeated."));
            DrakeTameMinutes = cfg.Bind(FLY, "DrakeTameMinutes", 10f, Adv("Minutes of feeding to tame a drake.", new AcceptableValueRange<float>(0.1f, 120f)));
            ModerSeatHeight = cfg.Bind(FLY, "ModerSeatHeight", 4.5f, Adv("How high you sit on the mini Moder. Restart to apply.", new AcceptableValueRange<float>(0f, 12f)));
            ModerSeatForward = cfg.Bind(FLY, "ModerSeatForward", 0.5f, Adv("How far forward you sit on the mini Moder. Restart to apply.", new AcceptableValueRange<float>(-6f, 6f)));
            DrakeSeatHeight = cfg.Bind(FLY, "DrakeSeatHeight", 1.1f, Adv("How high you sit on the drake. Restart to apply.", new AcceptableValueRange<float>(0f, 5f)));
            DrakeSeatForward = cfg.Bind(FLY, "DrakeSeatForward", 0.2f, Adv("How far forward you sit on the drake. Restart to apply.", new AcceptableValueRange<float>(-3f, 3f)));
            FlyerRiderRaise = cfg.Bind(FLY, "RiderRaiseAdjust", 0f, Adv("Raise or lower yourself on a flyer's saddle (metres).", new AcceptableValueRange<float>(-0.5f, 1f)));
            FlyerRiderForward = cfg.Bind(FLY, "RiderForwardAdjust", 0f, Adv("Move yourself forward or back on a flyer's saddle (metres).", new AcceptableValueRange<float>(-1f, 1f)));
            FlyerMountAttacks = cfg.Bind(FLY, "MountAttacks", true,
                "Riding a flyer, LMB / MMB use the mount's own attacks and RMB raises a bubble shield instead of your weapon and block.");
            FlyerShieldSeconds = cfg.Bind(FLY, "ShieldSeconds", 10f, Adv("How long the RMB bubble shield lasts.", new AcceptableValueRange<float>(1f, 60f)));
            FlyerShieldCooldown = cfg.Bind(FLY, "ShieldCooldown", 30f, Adv("Seconds before the shield can be raised again.", new AcceptableValueRange<float>(0f, 300f)));
            FlyerAttackDamage = cfg.Bind(FLY, "AttackDamage", 0.25f,
                Adv("Damage of a ridden mount's attacks compared to the real creature's (0.25 = a quarter).", new AcceptableValueRange<float>(0f, 2f)));

            const string FADER = "Mounts.Fader";
            FaderScale = cfg.Bind(FADER, "FaderScale", 0.45f, Adv("Size of the Mini Fader. Restart to apply.", new AcceptableValueRange<float>(0.08f, 0.6f)));
            // Around horse or Lox size: x0.45 stands ~3.15 m (the Fader's capsule is 7 m; the Lox's 3.5 m). A cfg still
            // holding an old default (0.18, 0.25), never chosen, takes the new one.
            if (UnityEngine.Mathf.Abs(FaderScale.Value - 0.18f) < 0.0001f || UnityEngine.Mathf.Abs(FaderScale.Value - 0.25f) < 0.0001f) FaderScale.Value = 0.45f;
            FaderRiderRaise = cfg.Bind(FADER, "RiderRaiseAdjust", 0f, Adv("Raise or lower yourself on the Mini Fader's saddle (metres).", new AcceptableValueRange<float>(-0.5f, 1f)));
            FaderRiderForward = cfg.Bind(FADER, "RiderForwardAdjust", 0f, Adv("Move yourself forward or back on the Mini Fader's saddle (metres).", new AcceptableValueRange<float>(-1f, 1f)));
            FaderLeapHeight = cfg.Bind(FADER, "LeapHeight", 14f, Adv("Height of a fully charged leap (metres).", new AcceptableValueRange<float>(3f, 40f)));
            FaderLeapForward = cfg.Bind(FADER, "LeapForward", 20f, Adv("How far forward a fully charged leap goes (metres).", new AcceptableValueRange<float>(5f, 60f)));
            FaderLeapChargeSeconds = cfg.Bind(FADER, "LeapChargeSeconds", 1.2f, Adv("Seconds to hold Space for a full leap.", new AcceptableValueRange<float>(0.4f, 3f)));
            FaderGlideSink = cfg.Bind(FADER, "GlideSink", 2.5f, Adv("How fast a glide sinks (metres a second).", new AcceptableValueRange<float>(0.5f, 10f)));
            FaderGlideSpeed = cfg.Bind(FADER, "GlideSpeed", 9f, Adv("Forward speed of a glide (metres a second).", new AcceptableValueRange<float>(2f, 30f)));
            FaderGlideTurn = cfg.Bind(FADER, "GlideTurn", 60f, Adv("How fast A / D turn a glide (degrees a second).", new AcceptableValueRange<float>(10f, 180f)));
            FaderClimb = cfg.Bind(FADER, "WallClimb", true, "The Mini Fader climbs steep walls: walk into one, W / S up and down, A / D along it, Space jumps off.");
            FaderClimbSpeed = cfg.Bind(FADER, "ClimbSpeed", 3.5f, Adv("How fast the Mini Fader climbs (metres a second).", new AcceptableValueRange<float>(0.5f, 12f)));
            FaderClimbDrain = cfg.Bind(FADER, "ClimbDrain", 0f, Adv("Stamina a second spent while climbing. 0 = free.", new AcceptableValueRange<float>(0f, 50f)));

            const string LEVELS = "Mounts.Levels";
            LevelsEnabled = cfg.Bind(LEVELS, "Enabled", true, "Mounts gain levels (1-20) from riding, flying, kills and feeding, and use stamina for their moves.");
            LevelsXpRate = cfg.Bind(LEVELS, "XpRate", 1f, Adv("Multiplier on all mount XP.", new AcceptableValueRange<float>(0f, 20f)));
            PoolModer = cfg.Bind(LEVELS, "PoolModer", 260f, Adv("The mini Moder's stamina at level 1 (+6% a level).", new AcceptableValueRange<float>(10f, 1000f)));
            PoolDrake = cfg.Bind(LEVELS, "PoolDrake", 220f, Adv("The drake's stamina at level 1 (+6% a level).", new AcceptableValueRange<float>(10f, 1000f)));
            // The pools were raised: a cfg still holding the old defaults (never chosen) takes the new ones.
            if (UnityEngine.Mathf.Abs(PoolModer.Value - 120f) < 0.01f) PoolModer.Value = 260f;
            if (UnityEngine.Mathf.Abs(PoolDrake.Value - 90f) < 0.01f) PoolDrake.Value = 220f;
            PoolFader = cfg.Bind(LEVELS, "PoolFader", 140f, Adv("The Mini Fader's stamina at level 1 (+6% a level).", new AcceptableValueRange<float>(10f, 1000f)));

            const string WHISTLE = "Mounts.Whistle";
            WhistleEnabled = cfg.Bind(WHISTLE, "Enabled", true, "With a Mount whistle in your inventory, double-tap the whistle key to call the last mount you rode.");
            WhistleKey = cfg.Bind(WHISTLE, "Key", UnityEngine.KeyCode.H, "The key you double-tap to whistle.");
            WhistleDoubleTapSeconds = cfg.Bind(WHISTLE, "DoubleTapSeconds", 0.35f, Adv("Most seconds between the two taps.", new AcceptableValueRange<float>(0.15f, 1f)));
            WhistleCooldown = cfg.Bind(WHISTLE, "Cooldown", 30f, Adv("Seconds before the whistle works again.", new AcceptableValueRange<float>(0f, 600f)));
            WhistleDistance = cfg.Bind(WHISTLE, "Distance", 6f, Adv("How far behind you the called mount appears (metres).", new AcceptableValueRange<float>(2f, 30f)));
            WhistleSound = cfg.Bind(WHISTLE, "Sound", MountWhistle.DefaultWhistleSound,
                Adv("Sound effect played when you whistle (a game sfx prefab name). Empty or unknown plays sfx_lox_shout."));

            VerboseLog = cfg.Bind(GENERAL, "VerboseLog", false, Hidden("Extra mount movement logging for troubleshooting."));
        }
    }
}
