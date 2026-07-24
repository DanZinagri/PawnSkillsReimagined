using System.Collections.Generic;
using UnityEngine;
using Verse;
using VSE.Passions;

namespace PawnSkillsReimagined
{
    public class PawnSkillsReimaginedMod : Mod
    {
        public static PawnSkillsReimaginedSettings Settings;

        // Text buffers for the numeric fields, kept between frames.
        private string bufRetention;
        private string bufQuality;
        private string bufMaxSkill;
        private string bufMaxChar;
        private string bufPointsPerLevel;
        private string bufExpertiseCost;
        private string bufConversion;
        private string bufRequirement;
        private string bufStartingXp;
        private string bufScaleInterval;
        private readonly Dictionary<string, string> bufPassionCosts = new Dictionary<string, string>();

        private Vector2 settingsScroll;

        public PawnSkillsReimaginedMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PawnSkillsReimaginedSettings>();
        }

        public override string SettingsCategory() => "PSR_Settings".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Core passion rows that actually exist, plus the "Other" row.
            int passionRows = 1;
            foreach (string defName in PointCosts.CoreDefNames)
            {
                if (DefDatabase<PassionDef>.GetNamedSilentFail(defName) != null)
                {
                    passionRows++;
                }
            }
            float viewHeight = 13 * 32f + passionRows * 34f + 130f;
            Rect viewRect = new Rect(0f, 0f, inRect.width - 20f, viewHeight);
            Widgets.BeginScrollView(inRect, ref settingsScroll, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            IntRow(listing, "PSR_MaxSkillLevel".Translate(), ref Settings.maxSkillLevel, ref bufMaxSkill, 20, 999,
                "PSR_MaxSkillLevel_Desc".Translate());
            IntRow(listing, "PSR_MaxCharacterLevel".Translate(), ref Settings.maxCharacterLevel, ref bufMaxChar, 20, 9999,
                "PSR_MaxCharacterLevel_Desc".Translate());
            IntRow(listing, "PSR_PointsPerLevel".Translate(), ref Settings.pointsPerLevel, ref bufPointsPerLevel, 1, 20,
                "PSR_PointsPerLevel_Desc".Translate());
            IntRow(listing, "PSR_ExpertiseCost".Translate(), ref Settings.expertisePointCost, ref bufExpertiseCost, 1, 20,
                "PSR_ExpertiseCost_Desc".Translate());
            FloatRow(listing, "PSR_TopEndTempering".Translate(), ref Settings.topEndRetention, ref bufRetention, 0.5f, 0.99f,
                "PSR_TopEndTempering_Desc".Translate());
            PercentRow(listing, "PSR_QualityBump".Translate(), ref Settings.overCapQualityChancePerLevel, ref bufQuality, 0f, 0.05f,
                "PSR_QualityBump_Desc".Translate());
            FloatRow(listing, "PSR_XpConversion".Translate(), ref Settings.xpConversionRate, ref bufConversion, 0.01f, 5f,
                "PSR_XpConversion_Desc".Translate());
            FloatRow(listing, "PSR_XpRequirement".Translate(), ref Settings.xpRequirementMultiplier, ref bufRequirement, 0.1f, 10f,
                "PSR_XpRequirement_Desc".Translate());
            FloatRow(listing, "PSR_StartingXp".Translate(), ref Settings.startingXpMultiplier, ref bufStartingXp, 0f, 5f,
                "PSR_StartingXp_Desc".Translate());

            Rect scaleRow = listing.GetRect(28f);
            TooltipHandler.TipRegion(scaleRow, "PSR_ScaleCost_Desc".Translate());
            Widgets.CheckboxLabeled(scaleRow, "PSR_ScaleCost".Translate(), ref Settings.scaleCostWithLevel);
            if (Settings.scaleCostWithLevel)
            {
                IntRow(listing, "PSR_ScaleInterval".Translate(), ref Settings.scaleCostInterval, ref bufScaleInterval, 1, 50,
                    "PSR_ScaleInterval_Desc".Translate());
            }

            listing.Gap(4f);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("PSR_CurveNote".Translate(PawnSkillsReimaginedGameComponent.MaxLevel));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.Gap(10f);
            Text.Font = GameFont.Medium;
            listing.Label("PSR_PassionCostsHeader".Translate());
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("PSR_PassionCostsNote".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(4f);

            // One row per core passion that exists, then a single "Other" row
            // covering every modded / variable-rate passion.
            foreach (string defName in PointCosts.CoreDefNames)
            {
                PassionDef passion = DefDatabase<PassionDef>.GetNamedSilentFail(defName);
                if (passion != null)
                {
                    PassionCostRow(listing, passion.defName, passion.LabelCap, passion.Icon, null);
                }
            }
            PassionCostRow(listing, PointCosts.OtherKey, "PSR_OtherPassions".Translate(), null,
                "PSR_OtherPassions_Desc".Translate());

            listing.End();
            Widgets.EndScrollView();
        }

        // Passion icon + label | slider | editable integer field, keyed by the
        // passionCosts dict key (a core defName or PointCosts.OtherKey).
        private void PassionCostRow(Listing_Standard listing, string key, string label, Texture2D icon, string tooltip)
        {
            Rect row = listing.GetRect(32f);
            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }
            if (!tooltip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(row, tooltip);
            }

            if (icon != null)
            {
                GUI.color = Color.white;
                Widgets.DrawTextureFitted(new Rect(row.x, row.y + 4f, 24f, 24f), icon, 1f);
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(row.x + 30f, row.y + 4f, row.width * 0.38f - 30f, 24f), label);
            Text.Anchor = TextAnchor.UpperLeft;

            int value = PointCosts.CostForKey(key);

            Rect sliderRect = new Rect(row.x + row.width * 0.44f, row.y + 6f, row.width * 0.36f, 22f);
            int slid = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect, value, 1f, 20f, true, null, null, null, 1f));
            if (slid != value)
            {
                value = slid;
                Settings.passionCosts[key] = value;
                bufPassionCosts.Remove(key);
            }

            if (!bufPassionCosts.TryGetValue(key, out string buffer))
            {
                buffer = value.ToString();
                bufPassionCosts[key] = buffer;
            }
            Rect fieldRect = new Rect(row.x + row.width * 0.82f, row.y + 4f, row.width * 0.12f, 24f);
            string edited = Widgets.TextField(fieldRect, buffer);
            if (edited != buffer)
            {
                bufPassionCosts[key] = edited;
                if (int.TryParse(edited, out int parsed))
                {
                    Settings.passionCosts[key] = Mathf.Clamp(parsed, 1, 20);
                }
            }
        }

        // Label | slider | editable plain-number text field.
        private static void FloatRow(Listing_Standard listing, string label, ref float value, ref string buffer, float min, float max, string tooltip)
        {
            Rect row = listing.GetRect(30f);
            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }
            TooltipHandler.TipRegion(row, tooltip);

            Widgets.Label(new Rect(row.x, row.y + 3f, row.width * 0.42f, 24f), label);

            Rect sliderRect = new Rect(row.x + row.width * 0.44f, row.y + 5f, row.width * 0.36f, 22f);
            float slid = Widgets.HorizontalSlider(sliderRect, value, min, max, true);
            if (!Mathf.Approximately(slid, value))
            {
                value = slid;
                buffer = null;
            }

            if (buffer == null)
            {
                buffer = value.ToString("0.###");
            }
            Rect fieldRect = new Rect(row.x + row.width * 0.82f, row.y + 3f, row.width * 0.12f, 24f);
            string edited = Widgets.TextField(fieldRect, buffer);
            if (edited != buffer)
            {
                buffer = edited;
                if (float.TryParse(edited, out float parsed))
                {
                    value = Mathf.Clamp(parsed, min, max);
                }
            }
        }

        // Label | slider | editable percent text field.
        private static void PercentRow(Listing_Standard listing, string label, ref float value, ref string buffer, float min, float max, string tooltip)
        {
            Rect row = listing.GetRect(30f);
            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }
            TooltipHandler.TipRegion(row, tooltip);

            Widgets.Label(new Rect(row.x, row.y + 3f, row.width * 0.42f, 24f), label);

            Rect sliderRect = new Rect(row.x + row.width * 0.44f, row.y + 5f, row.width * 0.36f, 22f);
            float slid = Widgets.HorizontalSlider(sliderRect, value, min, max, true);
            if (!Mathf.Approximately(slid, value))
            {
                value = slid;
                buffer = null;
            }

            if (buffer == null)
            {
                buffer = (value * 100f).ToString("0.###");
            }
            Rect fieldRect = new Rect(row.x + row.width * 0.82f, row.y + 3f, row.width * 0.12f, 24f);
            string edited = Widgets.TextField(fieldRect, buffer);
            if (edited != buffer)
            {
                buffer = edited;
                if (float.TryParse(edited, out float pct))
                {
                    value = Mathf.Clamp(pct / 100f, min, max);
                }
            }
            Widgets.Label(new Rect(fieldRect.xMax + 2f, row.y + 3f, 18f, 24f), "%");
        }

        // Label | slider | editable integer text field.
        private static void IntRow(Listing_Standard listing, string label, ref int value, ref string buffer, int min, int max, string tooltip)
        {
            Rect row = listing.GetRect(30f);
            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }
            TooltipHandler.TipRegion(row, tooltip);

            Widgets.Label(new Rect(row.x, row.y + 3f, row.width * 0.42f, 24f), label);

            Rect sliderRect = new Rect(row.x + row.width * 0.44f, row.y + 5f, row.width * 0.36f, 22f);
            int slid = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect, value, min, max, true, null, null, null, 1f));
            if (slid != value)
            {
                value = slid;
                buffer = null;
            }

            if (buffer == null)
            {
                buffer = value.ToString();
            }
            Rect fieldRect = new Rect(row.x + row.width * 0.82f, row.y + 3f, row.width * 0.12f, 24f);
            string edited = Widgets.TextField(fieldRect, buffer);
            if (edited != buffer)
            {
                buffer = edited;
                if (int.TryParse(edited, out int parsed))
                {
                    value = Mathf.Clamp(parsed, min, max);
                }
            }
        }
    }

    public class PawnSkillsReimaginedSettings : ModSettings
    {
        public int maxSkillLevel = 100;                       // hard cap on skill ranks; stat curve spans this range
        public int maxCharacterLevel = 999;                   // pawn level cap, separate from skills
        public int pointsPerLevel = 5;                        // points granted per character level
        public int expertisePointCost = 5;                    // cost per VSE expertise level
        public float topEndRetention = 0.9f;                 // temper for the beyond-vanilla headroom
        public float overCapQualityChancePerLevel = 0.0025f;  // quality bump chance per bonus vanilla-level
        public float xpConversionRate = 1f;                   // skill XP -> pawn level XP multiplier
        public float xpRequirementMultiplier = 1f;            // scales XP needed per level
        public float startingXpMultiplier = 1f;               // generated pawns' rolled-XP seed; 0 disables
        public bool scaleCostWithLevel = true;                // rank cost rises with skill level
        public int scaleCostInterval = 10;                    // +1 cost every N ranks
        public Dictionary<string, int> passionCosts = new Dictionary<string, int>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref maxSkillLevel, "maxSkillLevel", 100);
            Scribe_Values.Look(ref maxCharacterLevel, "maxCharacterLevel", 999);
            Scribe_Values.Look(ref pointsPerLevel, "pointsPerLevel", 5);
            Scribe_Values.Look(ref expertisePointCost, "expertisePointCost", 5);
            Scribe_Values.Look(ref topEndRetention, "topEndRetention", 0.9f);
            Scribe_Values.Look(ref overCapQualityChancePerLevel, "overCapQualityChancePerLevel", 0.0025f);
            Scribe_Values.Look(ref xpConversionRate, "xpConversionRate", 1f);
            Scribe_Values.Look(ref xpRequirementMultiplier, "xpRequirementMultiplier", 1f);
            Scribe_Values.Look(ref startingXpMultiplier, "startingXpMultiplier", 1f);
            Scribe_Values.Look(ref scaleCostWithLevel, "scaleCostWithLevel", true);
            Scribe_Values.Look(ref scaleCostInterval, "scaleCostInterval", 10);
            Scribe_Collections.Look(ref passionCosts, "passionCosts", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && passionCosts == null)
            {
                passionCosts = new Dictionary<string, int>();
            }
        }
    }
}
