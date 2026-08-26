using System;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace PawnSkillsReimagined
{
    // Optional UIStyle provider backed by Radius UI (astryl.RadiusUI.Inspector and
    // its framework). Radius UI is not a hard dependency, so everything is bound by
    // reflection - compiled into delegates once, so there is no per-call reflection
    // cost - and Available stays false when it isn't installed. Nothing outside
    // UIStyle references this type.
    //
    // Token choices follow the framework's documented semantics:
    //   accent   -> RadiusTheme.AccentFor(packageId): the user's chosen accent
    //               ("consumers never hardcode an accent"), read LIVE since the
    //               user can change it at runtime.
    //   Surface3 -> "bar tracks and insets"
    //   Wash     -> "default separator, 1px card border"
    //   Good     -> dark-panel positive (GoodBright is for HUD overlays)
    //   TextDim  -> secondary text
    public class RadiusUIStyle : IUIStyle
    {
        private const string OurPackageId = "DanZinagri.PawnSkillsReimagined";

        public static readonly bool Available;
        public static readonly RadiusUIStyle Instance = new RadiusUIStyle();

        // (rect, label, style, enabled, tip) -> clicked. style: 0 Primary, 1 Solid,
        // 2 Ghost (matches the framework's ButtonStyle enum order).
        private static readonly Func<Rect, string, int, bool, string, bool> button;
        private static readonly Action<Rect, Color> pill;      // Spatial.Pill
        private static readonly Action<Rect, bool> card;       // CardChrome.Card
        private static readonly Func<string, Color> accentFor; // RadiusTheme.AccentFor

        // Fixed palette tokens (documented as invariant, so caching is safe).
        // Only the accent is theme-driven, and that is read live.
        private static readonly Color trackCol;
        private static readonly Color goodCol;
        private static readonly Color dimCol;
        private static readonly Color washCol;

        static RadiusUIStyle()
        {
            trackCol = new Color(0.125f, 0.145f, 0.18f); // Surface3
            goodCol = new Color(0.4f, 0.85f, 0.4f);      // Good
            dimCol = new Color(0.62f, 0.62f, 0.62f);     // TextDim
            washCol = new Color(1f, 1f, 1f, 0.06f);      // Wash

            if (!ModsConfig.IsActive("astryl.RadiusUI.Inspector"))
            {
                return;
            }
            try
            {
                Type uikit = AccessTools.TypeByName("RadiusUI.Framework.UIKit");
                Type styleEnum = AccessTools.TypeByName("RadiusUI.Framework.ButtonStyle");
                if (uikit == null || styleEnum == null)
                {
                    return; // framework not loaded
                }
                MethodInfo m = uikit.GetMethod("Button", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(Rect), typeof(string), styleEnum, typeof(bool), typeof(string) }, null);
                if (m == null)
                {
                    Log.Warning("[Pawn Skills Reimagined] Radius UI is active but UIKit.Button was not found; " +
                                "its styling is unavailable.");
                    return;
                }

                // Compile: (r, label, style, enabled, tip) => UIKit.Button(r, label, (ButtonStyle)style, enabled, tip)
                var r = Expression.Parameter(typeof(Rect));
                var label = Expression.Parameter(typeof(string));
                var style = Expression.Parameter(typeof(int));
                var enabled = Expression.Parameter(typeof(bool));
                var tip = Expression.Parameter(typeof(string));
                var call = Expression.Call(m, r, label, Expression.Convert(style, styleEnum), enabled, tip);
                button = Expression.Lambda<Func<Rect, string, int, bool, string, bool>>(
                    call, r, label, style, enabled, tip).Compile();

                Type palette = AccessTools.TypeByName("RadiusUI.Framework.Palette");
                if (palette != null)
                {
                    trackCol = ColorMember(palette, "Surface3", trackCol);
                    goodCol = ColorMember(palette, "Good", goodCol);
                    dimCol = ColorMember(palette, "TextDim", dimCol);
                    washCol = ColorMember(palette, "Wash", washCol);
                }

                pill = RectColorDelegate(AccessTools.TypeByName("RadiusUI.Framework.Spatial"), "Pill");

                Type chrome = AccessTools.TypeByName("RadiusUI.Framework.CardChrome");
                MethodInfo cardM = chrome?.GetMethod("Card", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(Rect), typeof(bool) }, null);
                if (cardM != null)
                {
                    card = (Action<Rect, bool>)Delegate.CreateDelegate(typeof(Action<Rect, bool>), cardM);
                }

                // Per-mod accent override, falling back to the suite accent.
                Type theme = AccessTools.TypeByName("RadiusUI.Framework.RadiusTheme");
                MethodInfo accentM = theme?.GetMethod("AccentFor", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string) }, null);
                if (accentM != null)
                {
                    accentFor = (Func<string, Color>)Delegate.CreateDelegate(typeof(Func<string, Color>), accentM);
                }

                Available = true;
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Skills Reimagined] Radius UI styling failed to initialize: " + e);
            }
        }

        // Live read: the accent is the one theme-driven token, so a theme change
        // applies immediately.
        public Color Accent => accentFor != null ? accentFor(OurPackageId) : goodCol;
        public Color Positive => goodCol;
        public Color Dim => dimCol;
        public Color Divider => washCol;

        public bool Button(Rect rect, string label, bool enabled, string tip) =>
            button(rect, label, 1, enabled, tip); // Solid

        public void Card(Rect rect, bool hovered)
        {
            if (card != null)
            {
                card(rect, hovered);
            }
            else
            {
                Widgets.DrawMenuSection(rect);
            }
        }

        // A Surface3 pill track with a rounded accent pill fill - the same
        // construction the framework's own bars use.
        public void Bar(Rect rect, float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            if (pill == null)
            {
                Widgets.FillableBar(rect, fraction);
                return;
            }
            pill(rect, trackCol);
            float w = rect.width * fraction;
            if (w >= 1f)
            {
                pill(new Rect(rect.x, rect.y, w, rect.height), Accent);
            }
        }

        private static Color ColorMember(Type t, string name, Color fallback)
        {
            FieldInfo f = AccessTools.Field(t, name);
            if (f != null && f.GetValue(null) is Color fc)
            {
                return fc;
            }
            PropertyInfo p = AccessTools.Property(t, name);
            return p != null && p.GetValue(null) is Color pc ? pc : fallback;
        }

        private static Action<Rect, Color> RectColorDelegate(Type t, string name)
        {
            MethodInfo m = t?.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Rect), typeof(Color) }, null);
            return m != null ? (Action<Rect, Color>)Delegate.CreateDelegate(typeof(Action<Rect, Color>), m) : null;
        }
    }
}
