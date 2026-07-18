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
        public static ConfigEntry<bool> AutoHitch;            // dismounting hitches the horse in place

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

            VerboseLog = cfg.Bind(GENERAL, "VerboseLog", false,
                "Log per-second mount position/velocity/owner (diagnosing movement). Turn off once movement is smooth.");
        }
    }
}
