using System;
using System.Reflection;

namespace FiresMounts
{
    /// <summary>
    /// 0.1.131 (Fire 10-09, team-wide: typing in any input field blocks every mod hotkey): Core 0.2.376's
    /// FiresCore.Input.FiresInputBlock.IsTyping() answers; it is found by name so an older Core still loads us, and then vanilla's
    /// own text inputs (console, chat, text input, map pin) answer instead.
    /// </summary>
    internal static class TypingGate
    {
        private static readonly Func<bool> s_core = FindCore();

        private static Func<bool> FindCore()
        {
            try
            {
                var t = Type.GetType("FiresCore.Input.FiresInputBlock, FiresUnifiedCore");
                var m = t?.GetMethod("IsTyping", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                var f = m != null ? (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), m) : null;
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Typing] Core FiresInputBlock.IsTyping(): {(f != null ? "found, used" : "not found, vanilla's inputs checked")}");
                return f;
            }
            catch { return null; }
        }

        internal static bool Typing()
        {
            if (s_core != null) { try { return s_core(); } catch { } }
            return (global::Console.instance != null && global::Console.IsVisible()) || (Chat.instance != null && Chat.instance.HasFocus())
                || TextInput.IsVisible() || Minimap.InTextInput();
        }
    }
}
