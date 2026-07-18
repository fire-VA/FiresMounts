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

        // The reconstructed reference horse: cookie's proven MagicalMounts body, run through OUR Steamy Dumps →
        // FiresYoinkReconstruct pipeline and baked into OUR firesmounts bundle (NOT an external file). Composed
        // with the same vanilla stack + our handling as FiresHorse, but keeps cookie's verbatim appearance.
        public const string RefabHorsePrefab = "FiresHorse2";

        // Detachable saddle: the mesh child toggled on the horse when saddled, and the pick-up-able item.
        public const string HorseSaddleMesh = "FiresHorseSaddle";   // SkinnedMeshRenderer child on the horse
        public const string SaddleItem = "FiresSaddle";             // ItemDrop the player crafts + attaches

        // Custom ZDO keys written onto spawned mounts (live alongside vanilla ZDOVars without collision).
        public const string ZdoBreed = "firesmounts_breed";         // string — which model/breed variant
        public const string ZdoOwner = "firesmounts_owner";         // long   — taming owner player id
    }
}
