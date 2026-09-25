using UnityEngine;

namespace FiresMounts
{
    // FiresMounts' sections for the shared FiresCore help panel (registration only).
    internal static class MountHelpContent
    {
        private const string ModId = "FiresMounts";
        private static bool _registered;

        public static void Register()
        {
            if (_registered || Application.isBatchMode) return;
            _registered = true;

            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Horses & Riding", 0, BuildOverview);
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Riding Controls", 1, BuildControls);
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Admin & Config", 2, BuildAdmin, adminOnly: true);
        }

        private static void BuildOverview(FiresCore.Help.HelpContentWriter w)
        {
            w.Header("Horses & Riding");
            w.Paragraph("FiresMounts adds rideable horses with real reins steering, gaits that ramp " +
                "from walk to gallop, mounted jumps, and per-horse coats, manes, and tails.");
            w.Divider();

            w.SubHeader("Taming");
            w.Bullet("Feed a wild horse its taming food — Carrot, Turnip, Blueberries, Raspberries, " +
                "Barley, or Dandelion — and keep it calm, like taming a Lox.");
            w.Bullet("Taming takes about 20 real minutes by default ([Mounts.Taming] TameTimeMinutes).");
            w.Divider();

            w.SubHeader("Saddling");
            w.Bullet("A tamed horse still needs a SADDLE before you can ride.");
            w.Bullet("Hold a Lox saddle (SaddleLox) and press E on the tamed horse to saddle it.");
            w.Bullet("Alt+E on the saddle removes it again.");
            w.Divider();

            w.SubHeader("Looks");
            w.Paragraph("Every horse rolls its own coat (Brown, Gray, Gray2, Palomino, White), mane, " +
                "and tail — and keeps them forever. Pin a specific look, tint (CoatColor hex), or size " +
                "(HorseScale) under [Mounts.Body] in F8; changes apply live.");
        }

        private static void BuildControls(FiresCore.Help.HelpContentWriter w)
        {
            w.Header("Riding Controls");
            w.Bullet("E on a saddled horse — mount (the whole horse offers the Ride prompt, not just " +
                "the saddle). E again dismounts.");
            w.Bullet("W — forward. Speed RAMPS through the gaits: walk → trot → canter → gallop.");
            w.Bullet("A / D — reins steering: turn the horse itself (your camera is free to look around).");
            w.Bullet("Shift — sprint sweep to full gallop.");
            w.Bullet("S or no input — ease back down to a stop.");
            w.Bullet("Jump — hop over obstacles while moving; a proud REAR when standing still.");
            w.Bullet("Attack — rear-up strike (never harms players or tames).");
            w.Bullet("Tap C (crouch) — toggle walk mode: the horse holds a slow, steady walk.");
            w.Divider();

            w.Paragraph("Small rocks and stumps below belly height are rolled over rather than " +
                "becoming walls ([Mounts.Movement] BellyClearance). A dismounted horse stands where you " +
                "left it (AutoHitchOnDismount) instead of wandering. Your vanilla Ride skill improves " +
                "handling. Hoof audio follows the gait (walk/trot/run loops in " +
                "config/FiresMountSounds/, volume under [Mounts.Audio]).");
        }

        private static void BuildAdmin(FiresCore.Help.HelpContentWriter w)
        {
            w.AdminHeader("Admin & Config");
            w.SubHeader("Commands");
            w.Bullet("firesmounts_spawn [2|refab] [dist] — spawn a horse in front of you " +
                "(default FiresHorse; '2' = the reference-model FiresHorse2)");
            w.Bullet("firesmounts_tame [nameFilter] — instantly tame the nearest horse (does NOT saddle)");
            w.Bullet("firesmounts_saddle — drop a SaddleLox pickup at your feet");
            w.Bullet("firesmounts_diag — dump the nearest horse's full state to the log");
            w.AdminDivider();

            w.SubHeader("Key Config ([Mounts.*] in F8, live-apply)");
            w.Bullet("Movement: WalkSpeed (3), RunSpeed (10), TurnSpeed (120°/s), GaitRampRate, " +
                "ShiftSprint, EnableRiderJump, AutoHitchOnDismount");
            w.Bullet("UseVanillaSteering — switch reins steering back to vanilla look-direction steering");
            w.Bullet("Body: HorseScale (1.4), Coat/Mane/Tail pinning, CoatColor tint, seat position");
            w.Bullet("Taming: TameTimeMinutes (20), TamingFood list, MaxStamina");
            w.Bullet("General.Enabled — master switch; VerboseLog for ride telemetry");
        }
    }
}
