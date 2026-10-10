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
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Flying Mounts", 2, BuildFlying);
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Mini Fader", 3, BuildFader);
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Mount Whistle", 4, BuildWhistle);
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Mount Levels & Stamina", 5, BuildLevels);
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Admin & Config", 6, BuildAdmin, adminOnly: true);
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

        private static void BuildFlying(FiresCore.Help.HelpContentWriter w)
        {
            w.Header("Flying Mounts");
            w.Paragraph("The mini Moder (hatched from a Moder egg in a mount nest) and the drake (from a Drake egg, which Mountain drakes " +
                "sometimes drop, hatched the same way) carry a rider through the air. " +
                "They wait on the ground until you ride them, and a tamed flyer follows you like any tame (E on it: follow / stay).");
            w.Bullet("Following you (not ridden, not on Stay), a tamed flyer or Mini Fader fights the enemies around you with its own attacks.");
            w.Bullet("Saddle looks: craft a Saddle dye at the workbench and use it on a saddled mount. Each mount takes three looks (horse: Classic, Meadow, Palomino; mini Moder: Classic, Frost, Royal; drake: Classic, Glacier, Wolf hide; Mini Fader: Classic, Ashlands, Black hide).");
            w.Bullet("Saddle: a Sky saddle (FiresSkySaddle), used on the tamed flyer. Once saddled, E anywhere on it mounts " +
                "(unsaddled, E is follow / stay). E twice quickly dismounts; a single E never does (it turns in the air).");
            w.Divider();

            w.SubHeader("Flight controls (helicopter style)");
            w.Bullet("Shift — speed up; on the ground it takes off.");
            w.Bullet("W — nose down, fly forward faster. S — nose up: slow down, then back off.");
            w.Bullet("A / D — bank and slide left / right.");
            w.Bullet("Q / E — turn flat left / right ([Flight] keys in Core's config).");
            w.Bullet("Space held — climb straight up. Space twice quickly — lock a hover in place (twice again, or any movement key, to release).");
            w.Bullet("Ctrl held — fly straight down and land. Ctrl or E twice quickly — step off in mid-air (you fall; the mount lands and follows you).");
            w.Bullet("Let go of everything and it settles into a hover. Space and dodge never dismount: E twice does.");
            w.Bullet("LMB / MMB — the mount's own main / special attack, aimed where your crosshair points while the flight keeps its heading. Alt + mouse wheel picks the special.");
            w.Bullet("RMB — a bubble shield on you and the mount (the Staff of Protection's: it soaks up about 200 damage); 30 s cooldown.");
            w.Paragraph("No stall. Flying spends the mount's stamina (see Mount Levels & Stamina); a Tired flyer sinks and lands. " +
                "If you die while riding, the mount waits at your tombstone.");
        }

        private static void BuildFader(FiresCore.Help.HelpContentWriter w)
        {
            w.Header("Mini Fader");
            w.Paragraph("A small, tame Fader that runs, leaps and glides. The Fader drops a Fader egg; set it in a mount nest " +
                "(the same nest as the Moder egg) and after a day it hatches a tame Mini Fader. Saddle it with a Sky saddle.");
            w.Divider();
            w.SubHeader("Riding");
            w.Bullet("E on the saddled Fader mounts; E twice quickly dismounts.");
            w.Bullet("On the ground it rides like the horse: W eases from a walk up to a run, Shift sprints, A / D turn at full speed, " +
                "C toggles walk mode.");
            w.Bullet("Space — jump. Hold it longer for a higher, further leap: about 14 m up and 20 m ahead at full charge, a small hop " +
                "on a tap. A running leap keeps its speed. Hold A or D for a side leap.");
            w.Bullet("Past the top of a leap it glides by itself, on the flying mounts' keys: W nose down (faster), S nose up (slower), " +
                "A / D bank and turn, Q / E turn, Shift faster, Ctrl drops out. Off a cliff, hold Space while falling to glide. " +
                "Neither you nor the Fader takes fall damage while you ride.");
            w.Bullet("LMB / MMB — the Fader's own bite and flame breath at your crosshair; Alt + mouse wheel picks the special " +
                "(claws, spin, fissure, wall of fire, meteors). RMB — the bubble shield on you and the Fader.");
            w.Bullet("W into a cliff, a rock face or a built wall (near-vertical: steeper than about 75 degrees; it holds on down to 60) — climb it: W / S up / down, A / D along it. " +
                "Over the top it pulls itself onto the flat; Space jumps off the wall; S at the bottom steps off. Climbing spends stamina; " +
                "a Tired Fader slides down.");
        }

        private static void BuildWhistle(FiresCore.Help.HelpContentWriter w)
        {
            w.Header("Mount Whistle");
            w.Paragraph("Craft a Mount whistle at the workbench (Wood 2, Leather scraps 2) and carry it. Double-tap H and the last mount " +
                "you rode appears a few metres behind you, from anywhere in the world, and follows you.");
            w.Bullet("30 s between calls. A mount someone else is riding can't come.");
            w.Bullet("Key, tap window, cooldown and distance: [Mounts.Whistle] in F8.");
        }

        private static void BuildLevels(FiresCore.Help.HelpContentWriter w)
        {
            w.Header("Mount Levels & Stamina");
            w.Paragraph("Every mount levels up from 1 to 20, and keeps its level wherever it goes. Its name shows it (\"Mini Moder - Lv 4\").");
            w.Bullet("XP: riding on the ground, time in the air, kills by the mount's own attacks (and yours while mounted), and feeding it.");
            w.Bullet("Each level: more stamina (+6%), faster stamina regen (+4%), more speed (+1.5%), health (+5%) and attack damage (+3%). " +
                "A flyer's height limit rises from 40 m at level 1 to 100 m at level 20. Level 10: Shift in the air boosts. Level 20: a gold name.");
            w.Divider();
            w.SubHeader("Stamina");
            w.Bullet("The bar on the mount panel. Climbing, Shift, cruising and hovering drain it; descending barely does; it refills on the ground.");
            w.Bullet("A Mini Fader's leap costs 10-25 (more for a higher leap); gliding refills it a little.");
            w.Bullet("At 0 the mount is Tired: no climbing or boost, a flyer sinks and lands, the Fader can't leap. It recovers on the ground.");
            w.Divider();
            w.SubHeader("Colours (essences)");
            w.Bullet("Brew an essence at the Cauldron, hold it and press E on your tamed mount: it takes that essence's colours " +
                "(Verdancy, Embers, Frost, Shadow, the Spectre, Crystal, Cinders), and its attacks glow to match. The essence is used up.");
            w.Bullet("Used on an egg in a mount nest, the hatchling comes out in that colour. An Essence of Clarity returns a mount or egg " +
                "to its own colours. A horse takes the coat that goes with the essence.");
        }

        private static void BuildAdmin(FiresCore.Help.HelpContentWriter w)
        {
            w.AdminHeader("Admin & Config");
            w.SubHeader("Commands");
            w.Bullet("firesmounts_spawn [dist] — spawn a horse in front of you (default 4 m)");
            w.Bullet("firesmounts_tame [nameFilter] — instantly tame the nearest horse (does NOT saddle)");
            w.Bullet("firesmounts_saddle — drop a SaddleLox pickup at your feet");
            w.Bullet("firesmounts_diag — dump the nearest mount's full state to the log");
            w.Bullet("firesmounts_spawnflyer [moder|drake|fader] [dist] — spawn a creature mount (on the ground) in front of you");
            w.Bullet("firesmounts_hatch — finish the nearest mount nest's incubation now");
            w.Bullet("firesmounts_whistle — whistle as the double tap does (needs a Mount whistle in the inventory)");
            w.Bullet("firesmounts_level <1-20> / firesmounts_xp <n> / firesmounts_mountinfo — the nearest mount's level, XP and stamina");
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
