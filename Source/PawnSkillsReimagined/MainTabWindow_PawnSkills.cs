using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using VSE;

namespace PawnSkillsReimagined
{
    // Bottom-bar button for the colony skills window; hidden when turned off in settings.
    public class MainButtonWorker_PawnSkills : MainButtonWorker_ToggleTab
    {
        private const string DefName = "PSR_PawnSkills";
        private const string IconPath = "UI/Buttons/MainButtons/PSR_PawnSkills";
        // MainButtonDef caches its loaded texture privately.
        private static readonly FieldInfo IconCache = AccessTools.Field(typeof(MainButtonDef), "icon");

        public override bool Visible => base.Visible && PawnSkillsReimaginedMod.Settings.showSkillsMainButton;

        // Icon or text button, per the setting. Vanilla draws the icon and drops the
        // label whenever the def has one, and lays the bar out from `minimized` every
        // frame, so both are switched together. Runs at startup and on settings close.
        public static void ApplyIconSetting()
        {
            MainButtonDef def = DefDatabase<MainButtonDef>.GetNamedSilentFail(DefName);
            if (def == null)
            {
                return;
            }
            bool useIcon = PawnSkillsReimaginedMod.Settings.skillsButtonIcon;
            def.iconPath = useIcon ? IconPath : null;
            def.minimized = useIcon;
            IconCache?.SetValue(def, null);
        }
    }

    // Colony-wide skills window: a narrow list of every free colonist with their
    // level, unspent skill and expertise points and expertise slots. Selecting a
    // colonist opens their skill-point panel beside the list; the panel's expertise
    // drawer opens further out, and the window widens to fit.
    public class MainTabWindow_PawnSkills : MainTabWindow
    {
        private const float ListWidth = 340f;
        private const float RowHeight = 28f;
        private const float HeaderHeight = 24f;
        private const float ScrollbarWidth = 16f;
        private const float Gap = 6f;
        private const float ColExpertise = 62f;
        private const float ColNumber = 44f;
        // The list is rebuilt a few times a second rather than every frame: finding
        // colonists scans every pawn, and the values change rarely.
        private const int RefreshFrames = 20;

        private class Row
        {
            public Pawn pawn;
            public string name;
            public string level;
            public string points;
            public string expertisePoints;
            public string expertise;
            public bool hasPoints;
            public bool hasExpertisePoints;
        }

        private readonly SkillPointsPanel panel = new SkillPointsPanel();
        private readonly List<Row> rows = new List<Row>();
        private int rowsFrame = -1;
        private Pawn selected;
        private Vector2 listScroll;

        private static string headerPawn, headerLevel, headerPoints, headerExpertisePoints, headerExpertise;
        private static string tipLevel, tipPoints, tipExpertisePoints, tipExpertise, noColonists;

        public override Vector2 RequestedTabSize =>
            new Vector2(WidthFor(selected != null), SkillPointsPanel.Height + Margin * 2f);

        private float WidthFor(bool withPanel) =>
            Margin * 2f + ListWidth + (withPanel ? Gap + panel.Width : 0f);

        public override void PreOpen()
        {
            base.PreOpen();
            rowsFrame = -1;
        }

        public override void DoWindowContents(Rect inRect)
        {
            RefreshRows();

            // Grow or shrink to fit the panel. The window is anchored to the left
            // edge, so only its width changes.
            float wanted = Mathf.Min(WidthFor(selected != null), UI.screenWidth);
            if (!Mathf.Approximately(windowRect.width, wanted))
            {
                windowRect.width = wanted;
            }

            DrawList(new Rect(inRect.x, inRect.y, ListWidth, inRect.height));
            if (selected != null)
            {
                float x = inRect.x + ListWidth + Gap;
                Color prev = GUI.color;
                GUI.color = UIStyle.Divider;
                Widgets.DrawLineVertical(x - Gap / 2f, inRect.y, inRect.height);
                GUI.color = prev;
                panel.Draw(new Rect(x, inRect.y, panel.Width, inRect.height), selected);
            }
        }

        private void RefreshRows()
        {
            if (rowsFrame >= 0 && Time.frameCount - rowsFrame < RefreshFrames)
            {
                return;
            }
            rowsFrame = Time.frameCount;
            rows.Clear();

            var comp = PawnSkillsReimaginedGameComponent.Instance;
            float nameWidth = ListWidth - ScrollbarWidth - ColExpertise - 3f * ColNumber - 8f;
            Text.Font = GameFont.Small;
            // PawnsFinder returns a shared list it clears on every call, so copy out of it.
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn.skills == null)
                {
                    continue;
                }
                int points = comp?.AvailableFor(pawn) ?? 0;
                int expertisePoints = comp?.AvailableExpertisePoints(pawn) ?? 0;
                int owned = pawn.Expertise()?.AllExpertise?.Count ?? 0;
                rows.Add(new Row
                {
                    pawn = pawn,
                    name = pawn.LabelShortCap.Truncate(nameWidth),
                    level = (comp?.GetProgressOrNull(pawn)?.level ?? 1).ToString(),
                    points = points.ToString(),
                    expertisePoints = expertisePoints.ToString(),
                    expertise = owned + "/" + ExpertiseUnlockCompat.MaxExpertise(pawn),
                    hasPoints = points > 0,
                    hasExpertisePoints = expertisePoints > 0,
                });
            }
            rows.Sort((a, b) => string.Compare(a.pawn.LabelShortCap, b.pawn.LabelShortCap,
                StringComparison.CurrentCultureIgnoreCase));

            if (selected != null && !rows.Exists(r => r.pawn == selected))
            {
                selected = null;
            }
        }

        private void DrawList(Rect rect)
        {
            EnsureStrings();
            Text.Font = GameFont.Small;

            Rect header = new Rect(rect.x, rect.y, rect.width - ScrollbarWidth, HeaderHeight);
            GUI.color = Color.gray;
            DrawColumns(header, headerPawn, headerLevel, headerPoints, headerExpertisePoints, headerExpertise,
                Color.gray, Color.gray);
            GUI.color = Color.white;
            HeaderTips(header);

            Rect outRect = new Rect(rect.x, rect.y + HeaderHeight, rect.width, rect.height - HeaderHeight);
            if (rows.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(outRect.ContractedBy(4f), noColonists);
                GUI.color = Color.white;
                return;
            }

            Rect viewRect = new Rect(0f, 0f, outRect.width - ScrollbarWidth, rows.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref listScroll, viewRect);
            Color accent = UIStyle.Accent;
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                Rect r = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                if (i % 2 == 1)
                {
                    Widgets.DrawLightHighlight(r);
                }
                if (row.pawn == selected)
                {
                    Widgets.DrawHighlightSelected(r);
                }
                else
                {
                    Widgets.DrawHighlightIfMouseover(r);
                }
                DrawColumns(r, row.name, row.level, row.points, row.expertisePoints, row.expertise,
                    row.hasPoints ? accent : Color.gray, row.hasExpertisePoints ? accent : Color.gray);
                if (Widgets.ButtonInvisible(r))
                {
                    selected = selected == row.pawn ? null : row.pawn;
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
            }
            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // Columns right to left: expertise X/Y, expertise points, skill points, level;
        // the name takes the rest.
        private static void DrawColumns(Rect r, string name, string level, string points, string expertisePoints,
            string expertise, Color pointsColor, Color expertisePointsColor)
        {
            Color baseColor = GUI.color;
            float x = r.xMax - ColExpertise;
            Rect expertiseRect = new Rect(x, r.y, ColExpertise, r.height);
            x -= ColNumber;
            Rect expertisePointsRect = new Rect(x, r.y, ColNumber, r.height);
            x -= ColNumber;
            Rect pointsRect = new Rect(x, r.y, ColNumber, r.height);
            x -= ColNumber;
            Rect levelRect = new Rect(x, r.y, ColNumber, r.height);
            Rect nameRect = new Rect(r.x + 6f, r.y, x - r.x - 8f, r.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, name);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(levelRect, level);
            GUI.color = pointsColor;
            Widgets.Label(pointsRect, points);
            GUI.color = expertisePointsColor;
            Widgets.Label(expertisePointsRect, expertisePoints);
            GUI.color = baseColor;
            Widgets.Label(expertiseRect, expertise);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void HeaderTips(Rect header)
        {
            if (!Mouse.IsOver(header))
            {
                return;
            }
            float x = header.xMax - ColExpertise;
            TooltipHandler.TipRegion(new Rect(x, header.y, ColExpertise, header.height), tipExpertise);
            x -= ColNumber;
            TooltipHandler.TipRegion(new Rect(x, header.y, ColNumber, header.height), tipExpertisePoints);
            x -= ColNumber;
            TooltipHandler.TipRegion(new Rect(x, header.y, ColNumber, header.height), tipPoints);
            x -= ColNumber;
            TooltipHandler.TipRegion(new Rect(x, header.y, ColNumber, header.height), tipLevel);
        }

        // Translated once rather than every frame.
        private static void EnsureStrings()
        {
            if (headerPawn != null)
            {
                return;
            }
            headerPawn = "PSR_ColPawn".Translate();
            headerLevel = "PSR_ColLevel".Translate();
            headerPoints = "PSR_ColPoints".Translate();
            headerExpertisePoints = "PSR_ColExpertisePoints".Translate();
            headerExpertise = "PSR_ColExpertise".Translate();
            tipLevel = "PSR_ColLevel_Desc".Translate();
            tipPoints = "PSR_ColPoints_Desc".Translate();
            tipExpertisePoints = "PSR_ColExpertisePoints_Desc".Translate();
            tipExpertise = "PSR_ColExpertise_Desc".Translate();
            noColonists = "PSR_NoColonists".Translate();
        }
    }
}
