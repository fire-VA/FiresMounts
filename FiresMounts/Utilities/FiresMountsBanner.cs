using System;
using FiresCore.Logging;
using UnityEngine;

namespace FiresMounts
{
    // FiresMounts' console banners, rendered through Core's shared Banner engine.
    //
    //   PrintBig()  → knight-piece horse head with the FIRES MOUNTS brand. Fires
    //                 at the very top of Setup.
    //   Print()     → compact horseshoe over a status frame ("SADDLED UP").
    //                 Fires once at first WorldStart.
    internal static class FiresMountsBanner
    {
        private static bool s_compactPrinted;

        public static void PrintBig()
        {
            try
            {
                EmitHeader("Fires Mounts Loading...");
                Banner.Print(ToSegments(s_bigLines));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresMounts] Big banner failed: {ex.Message}");
            }
        }

        public static void Print()
        {
            if (s_compactPrinted) return;
            s_compactPrinted = true;
            try
            {
                EmitHeader("Fires Mounts Loaded.");
                Banner.Print(s_compactLines);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresMounts] Compact banner failed: {ex.Message}");
            }
        }

        // Routes the one-line header through the plugin's BepInEx log source so it
        // prints colored by source name without a raw white stdout duplicate.
        private static void EmitHeader(string text)
        {
            if (FiresMountsPlugin.Log != null) FiresMountsPlugin.Log.LogInfo(text);
            else Debug.Log($"[FiresMounts] {text}");
        }

        private static Banner.Segment[][] ToSegments((string text, ConsoleColor color)[] lines)
        {
            var result = new Banner.Segment[lines.Length][];
            for (int i = 0; i < lines.Length; i++)
                result[i] = new[] { new Banner.Segment(lines[i].text, lines[i].color) };
            return result;
        }

        // Compact banner — horseshoe over a stable-status frame. 31-char frame body
        // (3 lead + ╔ + 26 ═ + ╗); 🐴 is a surrogate pair, but the console
        // ADVANCES ONE column for it (no VS16), however wide the font paints the glyph, so this row pads it as one cell.
        private static readonly Banner.Segment[][] s_compactLines =
        {
            new[] { new Banner.Segment("              ▄█▀▀▀█▄",        ConsoleColor.DarkYellow) },
            new[] { new Banner.Segment("             ██     ██",       ConsoleColor.DarkYellow) },
            new[] { new Banner.Segment("             █o     o█",       ConsoleColor.Gray) },
            new[] { new Banner.Segment("             ██     ██",       ConsoleColor.DarkYellow) },
            new[] { new Banner.Segment("             ▀o     o▀",       ConsoleColor.Gray) },
            new[] { new Banner.Segment("   ╔══════════════════════════╗", ConsoleColor.DarkGray) },
            new[]
            {
                new Banner.Segment("   ║   ",              ConsoleColor.DarkGray),
                new Banner.Segment("[",                    ConsoleColor.Gray),
                new Banner.Segment("██████████████████",   ConsoleColor.Green),
                new Banner.Segment("]",                    ConsoleColor.Gray),
                new Banner.Segment("   ║",                 ConsoleColor.DarkGray),
            },
            new[]
            {
                new Banner.Segment("   ║      ",           ConsoleColor.DarkGray),
                new Banner.Segment("🐴 SADDLED UP",        ConsoleColor.Yellow),
                new Banner.Segment("        ║",             ConsoleColor.DarkGray),
            },
            new[] { new Banner.Segment("   ╚══════════════════════════╝", ConsoleColor.DarkGray) },
            new[] { new Banner.Segment("    FIRES MOUNTS",             ConsoleColor.Cyan) },
            new Banner.Segment[0],
            new[] { new Banner.Segment("   Fires Mounts Loaded",       ConsoleColor.Magenta) },
        };

        private static readonly (string text, ConsoleColor color)[] s_bigLines =
        {
            ("", ConsoleColor.Gray),
            ("%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%%@@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%@%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%", ConsoleColor.DarkYellow),
            ("%%%%%%%%%%%@%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%--=%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%--%%%%%%=...:@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@...:-%%%-.....-%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%@%%%%%%%", ConsoleColor.DarkYellow),
            ("@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%:....:-%:......-%%%%%%%%@%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%@%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%:......-........:%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkYellow),
            ("@%@%%%%%%%%@%%%%%%%%%%%%%%%%%@%%%%%@@%@-:................-%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%-:....................-%%%%%%%@%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%:.......................:-%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%@%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%-..........................:-%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%-:.....-%%--:.................:#%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%-:.......:........................-%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%@%%", ConsoleColor.DarkYellow),
            ("%%%%%%%%%%%%%%%%%@%%%%%%%%@%%-:...................................:-%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%@%%%%@%%%%%%%%@%%%-:................................::.....-%%%%%%@%%%%%%%%%%%%%%%%%%@%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%-...................................:-.....:%%%%%%%%%%%%%%%%%%%%%%%%@%%@%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%-:.....................................:-:....:#%%%%%%%%%%%%%%%%%%%%%@%%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%%%%%%%%%%%%%%%@%%-:........................................:+:....:@%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%%%%%=:...........................................-+:....:%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkYellow),
            ("%@%%%%%%%%%%%%%%%%%%:....:--::....................................=-.....:%%%%%%%%%%@%%%%%%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%%%%%-....::...................:--:................:@-.....-%%%%%%%%%%%%%%%@%%%%%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%%%%%%%%%%%%%%%%%:.................:----#%%=:..................%%:.....+%%%%%%%%%%%%%@%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%%%%%%+:...........:::-%%%%%%%%-....................-%%.....:%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%-........:-%%%%%%%%%%-:.....................:%%-.....-@%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%@%@%%%%%%%%%%%%%%%--:::--%%%%%%%%%%-:.......................:%%#.....:%%%%%%%%%%%%%%@%%%%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%*-..........................:%%%:....:%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@:............................:%%%:.....%%%%%%%%%%%@%%%%%%%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%%%%%%%%%%%@%%%%%%%@%%%%%%%%%%%%-..............................:%%%-.....%%%@%%%%%%%%%%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%@:...............................:%%%-.....%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%%%%%%%%%%@%%%%%%%%@%%%%%%%%%+:................................-%%%:.....%%%%%%%%%%%%%%%%%@%%%%%", ConsoleColor.Yellow),
            ("%%%@%%%%%%%%%%%%@@%%%%%%%%%%%%%%%:.................................%@%%:.....%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkYellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%-.................................:%@%%:....:%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@:.................................-%%%=.....:%%%%%%%%%%%%%%%%%%@%%%%", ConsoleColor.DarkYellow),
            ("@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%:................................:----:.....:@%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkGray),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%-...........................................-%%%%%%%%%%%%%%%%%%%@%%%", ConsoleColor.Gray),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%*...........................................%%%%%%%@%%%%%%%%%%%%%%%%", ConsoleColor.DarkGray),
            ("%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%-.........................................:%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.Gray),
            ("%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkGray),
            ("@%%%%%--------%%-------*%%%%@------%%%%%------%------%%-----%%-----%-------------%%%-------%%%%%%%@%", ConsoleColor.Gray),
            ("%%%%%%:......:%#.......*%%-:........:+%%:....:%:....:%%.....-%:....%-...........-%:........:-%%%%%%%", ConsoleColor.DarkGray),
            ("%%%@%%:.......*-.......*%-.....::....:%%:....:%:....:%%.....:%:....@-...........--.....:.....-%%%@%%", ConsoleColor.White),
            ("%%%%@%:.......-:.......*%-.....%-.....%%:....:%:....:%%......-:....%%%%=.....-%%%:....:%:....-%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%:.......::.......*%-.....@-.....%%:....:%:....:@%.......:....%%%%=.....-%%%-.....:--%%%%%%%%%%", ConsoleColor.White),
            ("%%%%%%:....:......:....*%-.....%-.....@%:....:%:....:%%............%%%%=.....-%%%%:.......:-%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%:....:......:....*%-.....%-.....%%:....:%:....:%%............%%%%=.....-@%%%%--:.......-%%%%%%", ConsoleColor.White),
            ("%%%%%%:....-.....::....*%-.....%-.....%%:....:%:....:%%....:.......%%%%=.....-@%%-::::::.....:%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%:....-:....-:....*%-.....%-.....%%:....:%:....:%%....:-......%%%%=.....-%%%-....:%:....:%%%%%%", ConsoleColor.White),
            ("%%%%%%:....--....+:....*%-.....::....:%%:.....:.....:%%....:%:.....%%%%=.....-%%@-.....::....:%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%:....-%...:%:....*%%-:........:-%%%:.........:%%%....:%-.....%%%%=.....-%%%%-.........:-%%%%%%", ConsoleColor.White),
            ("%%%%%%------%----%-----*%%%%--:::--*%%%%%%%--:::--%%%%%-----%%-----%%%%=------@%%%%%--:::--+%%%%%@%%", ConsoleColor.Yellow),
            ("%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@", ConsoleColor.White),
            ("%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.White),
            ("%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%", ConsoleColor.Yellow),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%@%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%", ConsoleColor.White),
            ("%%%%%%%%%%%%%%%%%%%%@%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%", ConsoleColor.DarkGray),
            ("%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkGray),
            ("%%%%%%%%%%%%%%%%%%@%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkGray),
            ("%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%@%%%%%%%%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%", ConsoleColor.DarkGray),
            ("%%%%%%%%%%%%%%%%%@%%%%%%%%%%%%%%@%%@%@%%@%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%@%%@@%%%%%%%%%%%%%%%%%", ConsoleColor.DarkGray),
            ("", ConsoleColor.Gray),
        };
    }
}
