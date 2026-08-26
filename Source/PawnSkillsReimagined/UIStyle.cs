using UnityEngine;
using Verse;

namespace PawnSkillsReimagined
{
    // The drawing surface our UI talks to. Our tab calls UIStyle and never a
    // specific mod's integration: one implementation draws vanilla widgets, and
    // optional providers (currently Radius UI) restyle the same calls. Colours are
    // owned by the style, so call sites don't carry their own fallbacks.
    public interface IUIStyle
    {
        Color Accent { get; }    // headings, point totals, emphasis
        Color Positive { get; }  // pending/queued changes
        Color Dim { get; }       // secondary text
        Color Divider { get; }   // separators

        bool Button(Rect rect, string label, bool enabled, string tip);
        void Card(Rect rect, bool hovered);
        void Bar(Rect rect, float fraction);
    }

    // Default look: plain RimWorld widgets and our own palette.
    public class VanillaStyle : IUIStyle
    {
        public Color Accent => new Color(1f, 0.85f, 0.3f);
        public Color Positive => new Color(0.5f, 0.9f, 0.5f);
        public Color Dim => new Color(0.68f, 0.68f, 0.68f);
        public Color Divider => Color.white;

        public bool Button(Rect rect, string label, bool enabled, string tip)
        {
            if (tip != null)
            {
                TooltipHandler.TipRegion(rect, tip);
            }
            if (!enabled)
            {
                GUI.color = Color.gray;
            }
            bool clicked = Widgets.ButtonText(rect, label, active: enabled);
            if (!enabled)
            {
                GUI.color = Color.white;
            }
            return clicked;
        }

        public void Card(Rect rect, bool hovered) => Widgets.DrawMenuSection(rect);

        public void Bar(Rect rect, float fraction) => Widgets.FillableBar(rect, Mathf.Clamp01(fraction));
    }

    // Resolves which style is in effect and forwards the drawing calls. The
    // provider is cached and only rebuilt when the relevant setting changes, so
    // the availability/enabled test isn't repeated per call.
    public static class UIStyle
    {
        private static readonly IUIStyle Vanilla = new VanillaStyle();
        private static IUIStyle current = Vanilla;
        private static bool builtStyled;

        public static IUIStyle Current
        {
            get
            {
                bool styled = RadiusUIStyle.Available && PawnSkillsReimaginedMod.Settings.radiusUIStyle;
                if (styled != builtStyled)
                {
                    current = styled ? RadiusUIStyle.Instance : Vanilla;
                    builtStyled = styled;
                }
                return current;
            }
        }

        public static Color Accent => Current.Accent;
        public static Color Positive => Current.Positive;
        public static Color Dim => Current.Dim;
        public static Color Divider => Current.Divider;

        public static bool Button(Rect rect, string label, bool enabled = true, string tip = null) =>
            Current.Button(rect, label, enabled, tip);

        public static void Card(Rect rect, bool hovered = false) => Current.Card(rect, hovered);

        public static void Bar(Rect rect, float fraction) => Current.Bar(rect, fraction);
    }
}
