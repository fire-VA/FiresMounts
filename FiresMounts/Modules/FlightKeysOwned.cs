using System;
using BepInEx.Configuration;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// 0.1.148 (Fire 10-30: "Core is our Jotunn: shared code; mods own their configs"; [lead] / [wishbone]): the 6 [Flight] keys of
    /// Core's SHARED creature-flight controller (mounts + possession) are FiresMounts'. Bound here under Core's own section + key names,
    /// each value copied once from Core's cfg on the first load, handed to Core with CoreSettings.Provide; possession (FCC / Valcast)
    /// reads the same values through Core. The flight code stays in Core. Same shape as FAT's FatCoreSettingsOwned; on a Core older
    /// than 0.2.1036 it logs once and Core keeps its own keys.
    /// </summary>
    internal static class FlightKeysOwned
    {
        private const string Owner = "FiresMounts";

        internal static void Bind(ConfigFile config)
        {
            try { BindAll(config); }
            catch (Exception e) when (e is MissingMethodException || e is TypeLoadException || e is MissingMemberException)
            {
                FiresMountsPlugin.Log?.LogInfo("[CoreSettings] the loaded Core has no CoreSettings (older than 0.2.1036): Core keeps its own [Flight] keys");
            }
        }

        private static void BindAll(ConfigFile config)
        {
            Own(config, "Yaw left key", KeyCode.Q, "While flying: turn left flat, without banking.");
            Own(config, "Yaw right key", KeyCode.E, "While flying: turn right flat, without banking.");
            Own(config, "Preset", FiresPossession.FlightPreset.Auto,
                "How flying is steered. Auto: each flyer flies its own natural way. Any other value gives every flyer that preset's key map.");
            Own(config, "Ability 1 key", KeyCode.V,
                "While flying: the preset's first ability (boost, dash, flap...). The vanilla action on this key (auto-pickup) is held back while you fly.");
            Own(config, "Ability 2 key", KeyCode.C,
                "While flying: the preset's second ability. The vanilla action on this key (toggle walk) is held back while you fly.");
            Own(config, "Stop key", KeyCode.X,
                "While flying: stop / brake where the preset has it. The vanilla action on this key (sit) is held back while you fly.");
        }

        private static void Own<T>(ConfigFile config, string key, T def, string description)
        {
            const string section = "Flight";
            var d = new ConfigDefinition(section, key);
            bool hadOwn = HasSaved(config, d);
            ConfigEntry<T> entry = config.Bind(d, def, new ConfigDescription(description));
            if (!hadOwn && FiresCore.Config.CoreSettings.TryReadCoreSaved(section, key, out string old))
            {
                try
                {
                    entry.Value = (T)TomlTypeConverter.ConvertToValue(old, typeof(T));
                    FiresMountsPlugin.Log?.LogInfo($"[CoreSettings] migrated {section}/{key} from Core: {entry.Value}");
                }
                catch (Exception ex) { FiresMountsPlugin.Log?.LogWarning($"[CoreSettings] {section}/{key}: Core's saved value '{old}' not read ({ex.Message}); the default stays"); }
            }
            FiresCore.Config.CoreSettings.Provide(section, key, () => entry.Value, Owner);
        }

        // whether FiresMounts' cfg already holds the key (then it is never overwritten from Core's)
        private static bool HasSaved(ConfigFile config, ConfigDefinition def)
        {
            try
            {
                var orphans = HarmonyLib.AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(config) as System.Collections.Generic.Dictionary<ConfigDefinition, string>;
                return orphans != null && orphans.ContainsKey(def);
            }
            catch { return false; }
        }
    }
}
