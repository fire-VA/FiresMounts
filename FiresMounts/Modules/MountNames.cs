namespace FiresMounts
{
    /// <summary>
    /// Stable string identifiers shared across the mod. Prefab names MUST match the names baked into the
    /// 'firesmounts' AssetBundle (FiresMountBuilder.cs in the Unity project) — they are hashed into
    /// ZNetScene by GetStableHashCode, so renaming one orphans every placed/spawned ZDO. One uniquely-named
    /// bundle per mod (see reference_bundle_ownership).
    /// </summary>
    internal static class MountNames
    {
        public const string BundleResource = "firesmounts";

        // The hero horse creature (the visual + animator baked into the bundle; the Valheim component stack
        // is composed onto it at runtime — see MountFactory). Frozen name (hashed into ZDOs).
        public const string HorsePrefab = "FiresHorse";
        // 1.0 ships ONE horse (Fire 10-07): the FiresHorse2 reference body was never in the shipping bundle.

        // Detachable saddle: the mesh child toggled on the horse when saddled, and the pick-up-able item.
        public const string HorseSaddleMesh = "FiresHorseSaddle";   // SkinnedMeshRenderer child on the horse
        public const string SaddleItem = "FiresSaddle";             // ItemDrop the player crafts + attaches

        // Flying mounts (FIRESMOUNTS_FLYING_PLAN). Runtime clones of vanilla prefabs, frozen names (hashed into ZDOs).
        public const string MiniModerPrefab = "FiresMiniModer";   // clone of "Dragon" (Moder)
        public const string DrakePrefab = "FiresDrake";           // clone of "Hatchling" (drake)
        public const string SkySaddleItem = "FiresSkySaddle";     // clone of "SaddleLox"; m_saddleItem of both flyers
        public const string ModerEggItem = "FiresModerEgg";       // clone of "DragonEgg"; Moder drops one
        public const string ModerNestPiece = "FiresModerNest";    // clone of "fire_pit"; hatches the egg
        public const string ZdoNestEgg = "firesmounts_nest_egg";  // long - ZNet time ticks the egg was placed (0 = empty)
        public const string ZdoNestEggItem = "firesmounts_nest_egg_item";   // string - which egg (NestHatchTable); empty = a Moder egg

        // Mini Fader (Tools\FIRESMOUNTS_LEVELS_AND_CLIMBER.md 3): a ground mount that leaps and glides. Frozen names.
        public const string MiniFaderPrefab = "FiresMiniFader";   // clone of "Fader"
        public const string FaderEggItem = "FiresFaderEgg";       // clone of "AsksvinEgg" (its EggGrow removed); Fader drops one
        public const string DrakeEggItem = "FiresDrakeEgg";

        // 0.1.120 (Fire 10-08: "Eikthyr first, then Bjorn", "keep the goblin brute"): ground mounts on the Mini Fader's ride (no glide or
        // climb). Runtime clones of "Eikthyr", "Bjorn", "GoblinBruteBros". Frozen names (hashed into ZDOs).
        public const string EikthyrPrefab = "FiresEikthyrMount";
        public const string BjornPrefab = "FiresBjornMount";
        public const string BrutePrefab = "FiresBruteMount";
        // 0.1.122 (Fire 10-08): swim-only water mounts, clones of "Serpent" and "BonemawSerpent".
        public const string SerpentPrefab = "FiresSerpentMount";
        public const string BonemawPrefab = "FiresBonemawMount";
        // 0.1.121 (Fire 10-08): the ground mounts' items. Frozen names.
        public const string BeastSaddleItem = "FiresBeastSaddle";
        public const string EikthyrCallItem = "FiresEikthyrCall";
        public const string BjornCallItem = "FiresBjornCall";
        public const string BruteCallItem = "FiresBruteCall";
        public const string SerpentCallItem = "FiresSerpentCall";   // 0.1.123 (Fire: the water pair's calls, trophy + leather like the others)
        public const string BonemawCallItem = "FiresBonemawCall";       // 0.1.112: clone of "AsksvinEgg"; vanilla Mountain drakes (Hatchling) drop it

        // Mount whistle (Fire 10-08): in the inventory, a double tap of [Mounts.Whistle] Key calls the last mount you rode.
        public const string WhistleItem = "FiresMountWhistle";    // stand-in clone of "SilverNecklace" until the whistle model is bundled
        public const string ZdoMountId = "firesmounts_mount_id";  // long - a mount's own id, kept across world reloads (ZDOIDs are not)

        /// <summary>Our rideable mounts by prefab name (the horse, the mini Moder, the drake, the Mini Fader).</summary>
        public static bool IsMount(string prefabName) =>
            !string.IsNullOrEmpty(prefabName) && (prefabName.StartsWith(HorsePrefab) || prefabName.StartsWith(MiniModerPrefab)
                || prefabName.StartsWith(DrakePrefab) || prefabName.StartsWith(MiniFaderPrefab)
                || prefabName.StartsWith(EikthyrPrefab) || prefabName.StartsWith(BjornPrefab) || prefabName.StartsWith(BrutePrefab)
                || prefabName.StartsWith(SerpentPrefab) || prefabName.StartsWith(BonemawPrefab));

        public static readonly string[] MountPrefabs = { HorsePrefab, MiniModerPrefab, DrakePrefab, MiniFaderPrefab, EikthyrPrefab, BjornPrefab, BrutePrefab,
            SerpentPrefab, BonemawPrefab };

        // Custom ZDO keys written onto spawned mounts (live alongside vanilla ZDOVars without collision).
        public const string ZdoBreed = "firesmounts_breed";         // string — which model/breed variant
        public const string ZdoOwner = "firesmounts_owner";         // long   — taming owner player id
    }
}
