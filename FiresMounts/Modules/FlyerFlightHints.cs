using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FiresCore.UI.Shell;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresMounts
{
    /// <summary>
    /// The flight keys on screen while the local player rides a flyer, vanilla-styled: at the top left just under the hotbar, our UI's shared font (Core's
    /// UIFontConfig), vanilla's tooltip background (rounded, dark), the box sized to its text, and the keys in vanilla's hover-text
    /// yellow, laid out as a key column and an action column. On the ground it shows the take-off and the attacks; in the air the
    /// helicopter set, plus "Hover locked" while the double-tapped Space hover holds. On the Mini Fader its own set, plus
    /// the leap's charge (in 10% steps, so the box is not re-sized every frame) and "Gliding". Hidden with the HUD and behind menus.
    /// With the unified UI override on, the box is an element of Core's HUD dock (moved in layout mode) instead of following the hotbar.
    /// </summary>
    internal sealed class FlyerFlightHints : MonoBehaviour
    {
        private const string ObjectName = "FiresMounts_FlightHints";
        private const float FontSize = 15f;
        private const float PadX = 12f, PadY = 8f, GapBelowHotbar = 6f, MaxWidth = 420f;
        private static readonly Color TextColor = new Color(0.93f, 0.90f, 0.82f, 1f);
        private static readonly Color FallbackBack = new Color(0f, 0f, 0f, 0.72f);

        private static FlyerFlightHints s_instance;

        private RectTransform _panel;
        private TextMeshProUGUI _text;
        private string _shown;
        private object _kWho; private int _kMode = -1, _kCharge = -2; private bool _kTired; private string _kWhistle;
        private HotkeyBar _hotbar;
        private bool _hasWhistle;
        private float _nextWhistleCheck;

        private static readonly string AirText = Title("Flying") +
            Row("W / S", "nose down, faster / up, slower") +
            Row("A / D", "bank and slide") +
            Row("Q / E", "turn") +
            Row("Shift", "faster") +
            Row("Space", "hold: climb    2x: hover lock") +
            Row("Ctrl", "hold: land    2x: step off") +
            Row("E", "2x: step off (Q / E still turn)") +
            Row("LMB", "bite / main attack") +
            Row("MMB", "special attack") +
            Row("RMB", "bubble shield", last: true);

        private static readonly string GroundText = Title("Flying mount") +
            Row("Shift", "take off") +
            Row("E", "2x: dismount") +
            Row("LMB", "bite / main attack") +
            Row("MMB", "special attack") +
            Row("RMB", "bubble shield", last: true);

        // The Mini Fader: a ground mount that leaps and glides.
        private static readonly string FaderText = Title("Mini Fader") +
            Row("W", "forward (walk to run)") +
            Row("A / D", "turn") +
            Row("Shift", "sprint") +
            Row("C", "walk mode") +
            Row("Space", "jump (hold: higher)") +
            Row("Space", "in the air: a second jump; then hold: glide, let go: drop") +
            Row("W", "into a cliff or wall: climb") +
            Row("E", "2x: dismount") +
            Row("LMB", "bite / main attack") +
            Row("MMB", "special attack") +
            Row("RMB", "bubble shield", last: true);

        // Eikthyr, Bjorn, the brute (the Fader's ride without glide or climb); the title is the mount's.
        private static string WaterMountText(string label) => Title(label) +
            Row("W", "swim forward") +
            Row("A / D", "turn") +
            Row("Shift", "faster") +
            Row("Shore", "it can't leave the water: you get off") +
            Row("E", "2x: dismount") +
            Row("LMB", "bite") +
            Row("RMB", "bubble shield", last: true);

        private static string GroundMountText(string label) => Title(label) +
            Row("W", "forward (walk to run)") +
            Row("A / D", "turn") +
            Row("Shift", "sprint") +
            Row("C", "walk mode") +
            Row("Space", "jump (hold: further)") +
            Row("E", "2x: dismount") +
            Row("LMB", "main attack") +
            Row("MMB", "special attack") +
            Row("RMB", "bubble shield", last: true);

        private const string LockedLine = "\n<color=#ffeb04><b>Hover locked</b></color>";
        private const string GlidingLine = "\n<color=#ffeb04><b>Gliding</b></color>";
        // On a wall the climb's own keys replace the leap / glide lines' meaning.
        private const string ClimbingLine = "\n<color=#ffeb04><b>Climbing</b></color>  W / S up / down, A / D along, Space jump off";
        // At 0 stamina the mount is Tired until 20% is back (MountProgress).
        private const string TiredLine = "\n<color=#ff6040><b>Tired</b></color>";
        private static string ChargeLine(float charge) => $"\n<color=#ffeb04><b>Leap {Mathf.RoundToInt(charge * 10f) * 10}%</b></color>";

        // With a Mount whistle in the inventory, its key, as bound. Riding a mount makes it the whistle's
        // "last mount", so the line says what the whistle will do with this one.
        private static string s_whistleKey, s_whistleLine;
        private static string WhistleLine()
        {
            string key = MountConfig.WhistleKey.Value.ToString();
            if (key != s_whistleKey) { s_whistleKey = key; s_whistleLine = "\n" + Row($"{key} 2x", "whistle: calls this mount back", last: true); }
            return s_whistleLine;
        }

        // Vanilla's hover texts show keys as "[<color=yellow><b>E</b></color>]"; Unity's yellow is #ffeb04.
        private static string Title(string text) => $"<b>{text}</b>\n";
        private static string Row(string keys, string what, bool last = false) =>
            $"<color=#ffeb04><b>{keys}</b></color><pos=4.6em>{what}{(last ? "" : "\n")}";

        /// <summary>Makes sure the panel exists under the HUD (called when the local player mounts a flyer).</summary>
        // The panel could end up inactive while riding: an inactive panel runs no Update of its
        // own, so the rides call this each frame. It puts the panel back on while someone rides, and logs each change with its reason.
        private static bool s_lastShown;
        internal static void KeepShown(bool riding)
        {
            if (!riding) return;
            Ensure();
            if (s_instance != null && !s_instance.gameObject.activeSelf)
            {
                s_instance.gameObject.SetActive(true);
                FiresMountsPlugin.Log.LogDebug("[FiresMounts] [Hints] shown: the panel had been switched off while riding; on again");
            }
        }

        private static void NoteShown(bool show, string why)
        {
            if (show == s_lastShown) return;
            s_lastShown = show;
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Hints] {(show ? "shown" : "hidden")}: {why}");
        }

        internal static void Ensure()
        {
            // A panel left under an old HUD (a relog makes a
            // new one) is rebuilt under the current one, a failed build is rebuilt, and each outcome says why once.
            if (Hud.instance == null || Hud.instance.m_rootObject == null)
            {
                FiresMountsPlugin.Log.LogDebug("[FiresMounts] [Hints] not built: no HUD yet");
                return;
            }
            Transform root = Hud.instance.m_rootObject.transform;
            if (s_instance != null && s_instance.transform.parent == root && s_instance._text != null) return;
            if (s_instance != null) { Destroy(s_instance.gameObject); s_instance = null; }
            var go = new GameObject(ObjectName, typeof(RectTransform));
            go.transform.SetParent(root, false);
            s_instance = go.AddComponent<FlyerFlightHints>();
            try { s_instance.Build(); }
            catch (Exception e)
            {
                FiresMountsPlugin.Log.LogWarning($"[FiresMounts] [Hints] not built: {e.GetType().Name}: {e.Message}");
                Destroy(go);
                s_instance = null;
                return;
            }
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Hints] panel built under {root.name} (HUD root active {root.gameObject.activeInHierarchy})");
        }

        // The host stays active (its Update runs, the dock keeps its rect); the visible box is the Panel child, which is what
        // Core's HUD dock moves and what is switched on and off.
        private void Build()
        {
            var host = (RectTransform)transform;
            host.anchorMin = host.anchorMax = host.pivot = new Vector2(0f, 1f);
            host.sizeDelta = Vector2.zero;
            host.anchoredPosition = Vector2.zero;

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(transform, false);
            _panel = (RectTransform)panelGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 1f);
            _panel.pivot = new Vector2(0f, 1f);
            _panel.anchoredPosition = new Vector2(20f, -150f);

            var bg = panelGo.AddComponent<Image>();
            bg.raycastTarget = false;
            if (!CopyVanillaTooltipBackground(bg)) bg.color = FallbackBack;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(_panel, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(PadX, PadY);
            textRect.offsetMax = new Vector2(-PadX, -PadY);
            _text = textGo.AddComponent<TextMeshProUGUI>();
            CopyVanillaFont(_text);
            _text.fontSize = FontSize;
            _text.color = TextColor;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.richText = true;
            _text.enableWordWrapping = false;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.raycastTarget = false;
            _panel.gameObject.SetActive(false);
            enabled = true;
        }

        // The same font as the rest of our UI: Core's shared UI font (UIFontConfig's Body category,
        // Averia Serif by default, the one the Classes window uses), else the font vanilla's key hints use, else any Averia asset.
        private static void CopyVanillaFont(TextMeshProUGUI text)
        {
            TMP_FontAsset ours = null;
            try { ours = FiresCore.UI.UIFontConfig.GetFontForCategory(FiresCore.UI.UIFontConfig.FontCategory.Body); }
            catch (Exception) { }
            if (ours != null) { text.font = ours; return; }
            TMP_Text source = KeyHints.instance != null ? KeyHints.instance.GetComponentInChildren<TMP_Text>(true) : null;
            if (source != null && source.font != null)
            {
                text.font = source.font;
                text.fontSharedMaterial = source.fontSharedMaterial;
                return;
            }
            var averia = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null && f.name.IndexOf("Averia", StringComparison.OrdinalIgnoreCase) >= 0);
            if (averia != null) text.font = averia;
        }

        // Vanilla's tooltip panel (the inventory tooltip's "Bkg": dark, rounded, 9-sliced).
        private static bool CopyVanillaTooltipBackground(Image target)
        {
            GameObject prefab = null;
            var element = InventoryGui.instance != null && InventoryGui.instance.m_playerGrid != null ? InventoryGui.instance.m_playerGrid.m_elementPrefab : null;
            var tip = element != null ? element.GetComponent<UITooltip>() : null;
            if (tip != null) prefab = AccessTools.Field(typeof(UITooltip), "m_tooltipPrefab")?.GetValue(tip) as GameObject;
            Image source = prefab != null ? prefab.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.sprite != null) : null;
            if (source == null) return false;
            target.sprite = source.sprite;
            target.type = source.type;
            target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
            target.color = source.color;
            return true;
        }

        private void Update()
        {
            FiresFlyerFlight ride = FiresFlyerFlight.LocalRide;
            FiresFaderRide fader = ride == null ? FiresFaderRide.LocalRide : null;
            bool riding = ride != null || fader != null;
            bool show = riding && !Hud.IsUserHidden() && !Menu.IsVisible() && !InventoryGui.IsVisible() && !Minimap.IsOpen();
            if (show != s_lastShown) NoteShown(show, !riding ? "not riding" : Hud.IsUserHidden() ? "the HUD is hidden" : Menu.IsVisible() ? "the menu is open"
                : InventoryGui.IsVisible() ? "the inventory is open" : Minimap.IsOpen() ? "the map is open" : (ride != null ? "riding a flyer" : "riding " + fader.Label));
            SyncDock();
            // Docked, Core's layout mode shows the box (a sample when nothing was shown yet) so it can be moved without riding.
            bool layout = s_layoutShown = s_docked && !show && InLayoutMode();
            bool visible = show || layout;
            if (_panel.gameObject.activeSelf != visible) _panel.gameObject.SetActive(visible);
            if (layout && _shown == null) SetText(GroundText);
            if (!show) return;
            if (!s_docked) PlaceUnderHotbar();
            if (Time.time >= _nextWhistleCheck)
            {
                _nextWhistleCheck = Time.time + 0.5f;   // an inventory walk twice a second, not every frame
                _hasWhistle = MountConfig.WhistleEnabled.Value && MountWhistle.HasWhistle(Player.m_localPlayer);
            }
            // Rebuilt only when what it shows changes - the mount, its mode, the leap charge at the shown 10% steps, the whistle
            // line and tiredness.
            int mode = fader != null ? (fader.Glides ? 0 : fader.SwimOnly ? 1 : 2) * 4 + (fader.Climbing ? 1 : fader.Gliding ? 2 : fader.Charge >= 0f ? 3 : 0)
                : !ride.Airborne ? 20 : ride.HoverLocked ? 21 : 22;
            int charge = fader != null && fader.Charge >= 0f ? Mathf.RoundToInt(fader.Charge * 10f) : -1;
            bool tired = fader != null ? fader.Tired : ride.Tired;
            string whistle = _hasWhistle ? WhistleLine() : null;
            object who = (object)fader ?? ride;
            if (_shown != null && who == _kWho && mode == _kMode && charge == _kCharge && tired == _kTired && (object)whistle == _kWhistle) return;
            _kWho = who; _kMode = mode; _kCharge = charge; _kTired = tired; _kWhistle = whistle;
            string want = fader != null ? (fader.Glides ? FaderText : fader.SwimOnly ? WaterMountText(fader.Label) : GroundMountText(fader.Label)) + (fader.Climbing ? ClimbingLine : fader.Gliding ? GlidingLine : fader.Charge >= 0f ? ChargeLine(fader.Charge) : "")
                : !ride.Airborne ? GroundText : ride.HoverLocked ? AirText + LockedLine : AirText;
            if (whistle != null) want += whistle;
            if (tired) want += TiredLine;
            SetText(want);
        }

        private void SetText(string want)
        {
            if (want == _shown) return;
            _shown = want;
            _text.text = want;
            Vector2 size = _text.GetPreferredValues(want, MaxWidth, 0f);
            _panel.sizeDelta = new Vector2(Mathf.Ceil(size.x) + PadX * 2f, Mathf.Ceil(size.y) + PadY * 2f);
        }

        // Core's layout mode, found by reflection: DockService.LayerActive is not public API, and an older Core lacks it.
        private static Func<string, bool> s_layerActive;
        private static bool s_layerTried, s_layoutShown;

        private static bool InLayoutMode()
        {
            if (!s_layerTried)
            {
                s_layerTried = true;
                try
                {
                    MethodInfo m = Type.GetType("FiresCore.UI.Dock.DockService, FiresUnifiedCore")?.GetMethod("LayerActive",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
                    if (m != null && m.ReturnType == typeof(bool))
                        s_layerActive = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), m);
                }
                catch (Exception) { s_layerActive = null; }
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Hints] layout-mode probe {(s_layerActive != null ? "found (DockService.LayerActive)" : "NOT found: the hints appear in layout mode only while riding")}");
            }
            if (s_layerActive == null) return false;
            try { return s_layerActive("hud"); }
            catch (Exception) { s_layerActive = null; return false; }
        }

        // Just under vanilla's hotbar, left-aligned with it (it is wherever the HUD put it), else the default top-left spot.
        private static readonly Vector3[] s_corners = new Vector3[4];

        private void PlaceUnderHotbar()
        {
            if (_hotbar == null && Hud.instance != null) _hotbar = Hud.instance.GetComponentInChildren<HotkeyBar>(true);
            var barRect = _hotbar != null ? _hotbar.transform as RectTransform : null;
            if (barRect == null || !barRect.gameObject.activeInHierarchy) return;
            var corners = s_corners;   // one shared buffer, not a new array a frame
            barRect.GetWorldCorners(corners);
            float scale = _panel.lossyScale.y > 0f ? _panel.lossyScale.y : 1f;
            _panel.position = new Vector3(corners[0].x, corners[0].y - GapBelowHotbar * scale, _panel.position.z);
        }

        // -- Core's HUD dock ----------------------------------------------------------------------------------------------------------

        // With the unified UI override on, the Panel is a Core HUD dock element (layout mode moves it, the dock keeps its spot);
        // off, it follows the hotbar as before. Any failure (an older Core) leaves it on the hotbar for the session.
        private const string DockId = "firesmounts.flighthints";
        private static bool s_docked, s_dockBroken;

        private void SyncDock()
        {
            if (s_dockBroken) return;
            bool want;
            try { want = OverrideOn(); }
            catch (Exception) { s_dockBroken = true; return; }
            if (want == s_docked) return;
            try
            {
                if (want) Register(_panel);
                else Unregister();
                s_docked = want;
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Hints] {(want ? "registered with Core's HUD dock as " + DockId + " (layout mode moves it)" : "unregistered from Core's HUD dock (follows the hotbar again)")}");
            }
            catch (Exception e)
            {
                s_dockBroken = true;
                FiresMountsPlugin.Log.LogWarning($"[FiresMounts] [Hints] HUD dock unavailable ({e.GetType().Name}: {e.Message}); the hints follow the hotbar (Core < 0.2.522?)");
            }
        }

        // Core's types only inside these, so a missing member throws here (caught above), not when Update is compiled.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool OverrideOn() => FiresShell.OverrideOn;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Register(RectTransform rect)
        {
            var options = new HudElementOptions
            {
                Label = "Flight hints",
                Owner = "FiresMounts",
                Resizable = false,
                Visible = () => s_lastShown || s_layoutShown,
            };
            // ShowInLayout is newer than the dock itself: set it only where Core has it.
            FieldInfo showInLayout = typeof(HudElementOptions).GetField("ShowInLayout");
            if (showInLayout != null && showInLayout.FieldType == typeof(bool)) showInLayout.SetValue(options, true);
            FiresShell.RegisterHudElement(DockId, rect, options);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Unregister() => FiresShell.UnregisterHudElement(DockId);

        private void OnDestroy()
        {
            if (s_docked)
            {
                try { Unregister(); }
                catch (Exception) { }
                s_docked = false;
            }
            if (s_instance == this) s_instance = null;
        }

        // Built when the HUD starts, so the dock knows the element before the first ride (layout mode can place it at once).
        [HarmonyPatch(typeof(Hud), "Awake")]
        private static class HudAwakePatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                try { Ensure(); }
                catch (Exception e) { FiresMountsPlugin.Log.LogWarning($"[FiresMounts] [Hints] HUD-start build failed: {e.GetType().Name}: {e.Message}"); }
            }
        }
    }
}
