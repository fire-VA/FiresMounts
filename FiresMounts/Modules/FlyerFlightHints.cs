using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresMounts
{
    /// <summary>
    /// The flight keys on screen while the local player rides a flyer. Fire's hand test 3 (10-08): "hideous" (a pixel font, text spilling
    /// out of the box, mid-screen), so it is vanilla-styled now: at the top left just under the hotbar, our UI's shared font (Core's
    /// UIFontConfig), vanilla's tooltip background (rounded, dark), the box sized to its text, and the keys in vanilla's hover-text
    /// yellow, laid out as a key column and an action column. On the ground it shows the take-off and the attacks; in the air the
    /// helicopter set, plus "Hover locked" while the double-tapped Space hover holds. On the Mini Fader (0.1.105) its own set, plus
    /// the leap's charge (in 10% steps, so the box is not re-sized every frame) and "Gliding". Hidden with the HUD and behind menus.
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

        // The Mini Fader (0.1.105): a ground mount that leaps and glides.
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

        // 0.1.120: Eikthyr, Bjorn, the brute (the Fader's ride without glide or climb); the title is the mount's.
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
        // 0.1.108: on a wall the climb's own keys replace the leap / glide lines' meaning.
        private const string ClimbingLine = "\n<color=#ffeb04><b>Climbing</b></color>  W / S up / down, A / D along, Space jump off";
        // 0.1.106: at 0 stamina the mount is Tired until 20% is back (MountProgress).
        private const string TiredLine = "\n<color=#ff6040><b>Tired</b></color>";
        private static string ChargeLine(float charge) => $"\n<color=#ffeb04><b>Leap {Mathf.RoundToInt(charge * 10f) * 10}%</b></color>";

        // 0.1.107 ([lead] for Fire): with a Mount whistle in the inventory, its key, as bound. Riding a mount makes it the whistle's
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
        // 0.1.121 ([lead], round 23: the panel was built, then found INACTIVE while riding): an inactive panel runs no Update of its
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
            // 0.1.118 ([lead], round 22: "never created while riding FiresMiniModer"): a panel left under an old HUD (a relog makes a
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
            catch (System.Exception e)
            {
                FiresMountsPlugin.Log.LogWarning($"[FiresMounts] [Hints] not built: {e.GetType().Name}: {e.Message}");
                Destroy(go);
                s_instance = null;
                return;
            }
            FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Hints] panel built under {root.name} (HUD root active {root.gameObject.activeInHierarchy})");
        }

        private void Build()
        {
            _panel = (RectTransform)transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 1f);
            _panel.pivot = new Vector2(0f, 1f);
            _panel.anchoredPosition = new Vector2(20f, -150f);

            var bg = gameObject.AddComponent<Image>();
            bg.raycastTarget = false;
            if (!CopyVanillaTooltipBackground(bg)) bg.color = FallbackBack;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(transform, false);
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
            gameObject.SetActive(false);
            enabled = true;
        }

        // Fire 10-08: "make it use the same font all our other ui uses" - Core's shared UI font (UIFontConfig's Body category,
        // Averia Serif by default, the one the Classes window uses), else the font vanilla's key hints use, else any Averia asset.
        private static void CopyVanillaFont(TextMeshProUGUI text)
        {
            TMP_FontAsset ours = null;
            try { ours = FiresCore.UI.UIFontConfig.GetFontForCategory(FiresCore.UI.UIFontConfig.FontCategory.Body); }
            catch (System.Exception) { }
            if (ours != null) { text.font = ours; return; }
            TMP_Text source = KeyHints.instance != null ? KeyHints.instance.GetComponentInChildren<TMP_Text>(true) : null;
            if (source != null && source.font != null)
            {
                text.font = source.font;
                text.fontSharedMaterial = source.fontSharedMaterial;
                return;
            }
            var averia = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null && f.name.IndexOf("Averia", System.StringComparison.OrdinalIgnoreCase) >= 0);
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
            NoteShown(show, !riding ? "not riding" : Hud.IsUserHidden() ? "the HUD is hidden" : Menu.IsVisible() ? "the menu is open"
                : InventoryGui.IsVisible() ? "the inventory is open" : Minimap.IsOpen() ? "the map is open" : (ride != null ? "riding a flyer" : "riding " + fader.Label));
            if (_text.gameObject.activeSelf != show) { _text.gameObject.SetActive(show); GetComponent<Image>().enabled = show; }
            if (!show) return;
            PlaceUnderHotbar();
            string want = fader != null ? (fader.Glides ? FaderText : fader.SwimOnly ? WaterMountText(fader.Label) : GroundMountText(fader.Label)) + (fader.Climbing ? ClimbingLine : fader.Gliding ? GlidingLine : fader.Charge >= 0f ? ChargeLine(fader.Charge) : "")
                : !ride.Airborne ? GroundText : ride.HoverLocked ? AirText + LockedLine : AirText;
            if (Time.time >= _nextWhistleCheck)
            {
                _nextWhistleCheck = Time.time + 0.5f;   // an inventory walk twice a second, not every frame
                _hasWhistle = MountConfig.WhistleEnabled.Value && MountWhistle.HasWhistle(Player.m_localPlayer);
            }
            if (_hasWhistle) want += WhistleLine();
            if (fader != null ? fader.Tired : ride.Tired) want += TiredLine;
            if (want == _shown) return;
            _shown = want;
            _text.text = want;
            Vector2 size = _text.GetPreferredValues(want, MaxWidth, 0f);
            _panel.sizeDelta = new Vector2(Mathf.Ceil(size.x) + PadX * 2f, Mathf.Ceil(size.y) + PadY * 2f);
        }

        // Just under vanilla's hotbar, left-aligned with it (it is wherever the HUD put it), else the default top-left spot.
        private void PlaceUnderHotbar()
        {
            if (_hotbar == null && Hud.instance != null) _hotbar = Hud.instance.GetComponentInChildren<HotkeyBar>(true);
            var barRect = _hotbar != null ? _hotbar.transform as RectTransform : null;
            if (barRect == null || !barRect.gameObject.activeInHierarchy) return;
            var corners = new Vector3[4];
            barRect.GetWorldCorners(corners);
            float scale = _panel.lossyScale.y > 0f ? _panel.lossyScale.y : 1f;
            _panel.position = new Vector3(corners[0].x, corners[0].y - GapBelowHotbar * scale, _panel.position.z);
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }
    }
}
