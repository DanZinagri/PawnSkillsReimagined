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
        private string bufQualityCap;
        private string bufMaxSkill;
        private string bufMaxChar;
        private string bufPointsPerLevel;
        private string bufExpertiseSlot;
        private string bufExpertisePoint;
        private string bufExpertiseAcquire;
        private string bufConversion;
        private string bufRequirement;
        private string bufStartingXp;
        private string bufScaleInterval;
        private string bufNpcStretch;
        private string bufTechNeo, bufTechMed, bufTechInd, bufTechSpacer, bufTechUltra, bufTechArch;
        private readonly Dictionary<string, string> bufPassionCosts = new Dictionary<string, string>();

        private Vector2 settingsScroll;
        private Vector2 costsScroll;
        private int settingsTab;

        public PawnSkillsReimaginedMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PawnSkillsReimaginedSettings>();
        }

        public override string SettingsCategory() => "PSR_Settings".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect body = new Rect(inRect.x, inRect.y + 34f, inRect.width, inRect.height - 34f);
            var tabs = new List<TabRecord>
            {
                new TabRecord("PSR_TabGeneral".Translate(), () => settingsTab = 0, settingsTab == 0),
                new TabRecord("PSR_TabSkillCosts".Translate(), () => settingsTab = 2, settingsTab == 2),
                new TabRecord("PSR_TabPawnGen".Translate(), () => settingsTab = 1, settingsTab == 1),
            };
            Widgets.DrawMenuSection(body);
            TabDrawer.DrawTabs(body, tabs);
            Rect content = body.ContractedBy(12f);
            if (settingsTab == 1)
            {
                DoPawnGenTab(content);
            }
            else if (settingsTab == 2)
            {
                DoSkillCostsTab(content);
            }
            else
            {
                DoGeneralTab(content);
            }
        }

        private void DoGeneralTab(Rect inRect)
        {
            float viewHeight = 13 * 32f + 40f;
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
            Rect maxExpRow = listing.GetRect(28f);
            TooltipHandler.TipRegion(maxExpRow, "PSR_ScaleMaxExpertise_Desc".Translate());
            Widgets.CheckboxLabeled(maxExpRow, "PSR_ScaleMaxExpertise".Translate(), ref Settings.overrideMaxExpertise);
            if (Settings.overrideMaxExpertise)
            {
                IntRow(listing, "PSR_ExpertiseSlotInterval".Translate(), ref Settings.expertiseSlotInterval, ref bufExpertiseSlot, 5, 200,
                    "PSR_ExpertiseSlotInterval_Desc".Translate());
            }
            IntRow(listing, "PSR_ExpertisePointInterval".Translate(), ref Settings.expertisePointInterval, ref bufExpertisePoint, 1, 100,
                "PSR_ExpertisePointInterval_Desc".Translate());
            IntRow(listing, "PSR_ExpertiseUnlockLevel".Translate(), ref Settings.expertiseAcquireLevel, ref bufExpertiseAcquire,
                0, Mathf.Max(20, Settings.maxSkillLevel), "PSR_ExpertiseUnlockLevel_Desc".Translate());
            FloatRow(listing, "PSR_TopEndTempering".Translate(), ref Settings.topEndRetention, ref bufRetention, 0.5f, 0.99f,
                "PSR_TopEndTempering_Desc".Translate());
            IntRow(listing, "PSR_QualityCapLevel".Translate(), ref Settings.qualityVanillaCapLevel, ref bufQualityCap,
                20, Mathf.Max(20, Settings.maxSkillLevel), "PSR_QualityCapLevel_Desc".Translate());
            FloatRow(listing, "PSR_XpConversion".Translate(), ref Settings.xpConversionRate, ref bufConversion, 0.01f, 5f,
                "PSR_XpConversion_Desc".Translate());
            FloatRow(listing, "PSR_XpRequirement".Translate(), ref Settings.xpRequirementMultiplier, ref bufRequirement, 0.1f, 10f,
                "PSR_XpRequirement_Desc".Translate());

            Rect normalRow = listing.GetRect(28f);
            TooltipHandler.TipRegion(normalRow, "PSR_SkillsLevelNormally_Desc".Translate());
            Widgets.CheckboxLabeled(normalRow, "PSR_SkillsLevelNormally".Translate(), ref Settings.skillsLevelNormally);

            listing.Gap(4f);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("PSR_CurveNote".Translate(PawnSkillsReimaginedGameComponent.MaxLevel));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.End();
            Widgets.EndScrollView();
        }

        private void DoSkillCostsTab(Rect inRect)
        {
            int passionRows = DefDatabase<PassionDef>.AllDefsListForReading.Count;
            float viewHeight = (passionRows + 7) * 34f + 90f;
            Rect viewRect = new Rect(0f, 0f, inRect.width - 20f, viewHeight);
            Widgets.BeginScrollView(inRect, ref costsScroll, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            Rect scaleRow = listing.GetRect(28f);
            TooltipHandler.TipRegion(scaleRow, "PSR_ScaleCost_Desc".Translate());
            Widgets.CheckboxLabeled(scaleRow, "PSR_ScaleCost".Translate(), ref Settings.scaleCostWithLevel);
            if (Settings.scaleCostWithLevel)
            {
                IntRow(listing, "PSR_ScaleInterval".Translate(), ref Settings.scaleCostInterval, ref bufScaleInterval, 1, 50,
                    "PSR_ScaleInterval_Desc".Translate());
            }

            listing.Gap(10f);
            Text.Font = GameFont.Medium;
            listing.Label("PSR_PassionCostsHeader".Translate());
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("PSR_PassionCostsNote".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(4f);

            // Our hand-tuned passions first, in a fixed order.
            foreach (string defName in PointCosts.DefinedDefNames)
            {
                PassionDef passion = DefDatabase<PassionDef>.GetNamedSilentFail(defName);
                if (passion != null)
                {
                    PassionCostRow(listing, passion.defName, PassionLabel(passion), passion.Icon, null);
                }
            }

            // Then a divider and every other (modded) passion the game has loaded.
            bool anyOther = false;
            foreach (PassionDef passion in DefDatabase<PassionDef>.AllDefsListForReading)
            {
                if (!PointCosts.IsDefined(passion.defName))
                {
                    anyOther = true;
                    break;
                }
            }
            if (anyOther)
            {
                listing.GapLine();
                foreach (PassionDef passion in DefDatabase<PassionDef>.AllDefsListForReading)
                {
                    if (!PointCosts.IsDefined(passion.defName))
                    {
                        PassionCostRow(listing, passion.defName, PassionLabel(passion), passion.Icon, null);
                    }
                }
            }

            listing.End();
            Widgets.EndScrollView();
        }

        private static string PassionLabel(PassionDef passion) =>
            passion.label.NullOrEmpty() ? passion.defName : passion.LabelCap;

        private void DoPawnGenTab(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            FloatRow(listing, "PSR_StartingXp".Translate(), ref Settings.startingXpMultiplier, ref bufStartingXp, 0f, 5f,
                "PSR_StartingXp_Desc".Translate());
            FloatRow(listing, "PSR_NpcRollStretch".Translate(), ref Settings.npcSkillRollStretch, ref bufNpcStretch, 1f, 3f,
                "PSR_NpcRollStretch_Desc".Translate());

            listing.Gap(10f);
            Text.Font = GameFont.Medium;
            listing.Label("PSR_TechHeader".Translate());
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("PSR_TechNote".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(4f);

            string tip = "PSR_TechMult_Desc".Translate();
            FloatRow(listing, "PSR_TechNeolithic".Translate(), ref Settings.techMultNeolithic, ref bufTechNeo, 0.1f, 5f, tip);
            FloatRow(listing, "PSR_TechMedieval".Translate(), ref Settings.techMultMedieval, ref bufTechMed, 0.1f, 5f, tip);
            FloatRow(listing, "PSR_TechIndustrial".Translate(), ref Settings.techMultIndustrial, ref bufTechInd, 0.1f, 5f, tip);
            FloatRow(listing, "PSR_TechSpacer".Translate(), ref Settings.techMultSpacer, ref bufTechSpacer, 0.1f, 5f, tip);
            FloatRow(listing, "PSR_TechUltra".Translate(), ref Settings.techMultUltra, ref bufTechUltra, 0.1f, 5f, tip);
            FloatRow(listing, "PSR_TechArchotech".Translate(), ref Settings.techMultArchotech, ref bufTechArch, 0.1f, 5f, tip);

            listing.End();
        }

        // Passion icon + label | slider | editable integer field, keyed by the
        // passionCosts dict key (the passion's defName).
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
            int slid = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect, value, 1f, 100f, true, null, null, null, 1f));
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
                    Settings.passionCosts[key] = Mathf.Clamp(parsed, 1, 100);
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
        public bool overrideMaxExpertise = true;              // true = our per-pawn scaled cap + forced overlap; false = defer to VSE's own limit
        public int expertiseSlotInterval = 50;                // character levels per +1 to a pawn's max expertise count
        public int expertisePointInterval = 5;                // character levels per +1 expertise point
        public int expertiseAcquireLevel = 0;                 // override for VSE's skill level to unlock an expertise; 0 = use VSE's own setting
        public float topEndRetention = 0.9f;                 // temper for the beyond-vanilla headroom
        public int qualityVanillaCapLevel = 80;              // skill level where crafting quality reaches vanilla's level-20 ceiling
        public float xpConversionRate = 1f;                   // skill XP -> pawn level XP multiplier
        public float xpRequirementMultiplier = 1f;            // scales XP needed per level
        public bool skillsLevelNormally = false;              // on = funneled XP also levels the skill itself (vanilla-style)
        public float startingXpMultiplier = 1f;               // generated pawns' rolled-XP seed; 0 disables
        public float npcSkillRollStretch = 1.5f;              // NPC skill roll extends past vanilla's 20 cap by this factor
        // NPC starting-XP multipliers by faction tech level (Animal->Neolithic,
        // Undefined->Industrial). Higher tech = better-schooled pawns.
        public float techMultNeolithic = 1f;
        public float techMultMedieval = 1.5f;
        public float techMultIndustrial = 2f;
        public float techMultSpacer = 2.5f;
        public float techMultUltra = 3f;
        public float techMultArchotech = 3.5f;
        public bool scaleCostWithLevel = true;                // rank cost rises with skill level
        public int scaleCostInterval = 10;                    // +1 cost every N ranks
        public Dictionary<string, int> passionCosts = new Dictionary<string, int>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref maxSkillLevel, "maxSkillLevel", 100);
            Scribe_Values.Look(ref maxCharacterLevel, "maxCharacterLevel", 999);
            Scribe_Values.Look(ref pointsPerLevel, "pointsPerLevel", 5);
            Scribe_Values.Look(ref overrideMaxExpertise, "overrideMaxExpertise", true);
            Scribe_Values.Look(ref expertiseSlotInterval, "expertiseSlotInterval", 50);
            Scribe_Values.Look(ref expertisePointInterval, "expertisePointInterval", 5);
            Scribe_Values.Look(ref expertiseAcquireLevel, "expertiseAcquireLevel", 0);
            Scribe_Values.Look(ref topEndRetention, "topEndRetention", 0.9f);
            Scribe_Values.Look(ref qualityVanillaCapLevel, "qualityVanillaCapLevel", 80);
            Scribe_Values.Look(ref xpConversionRate, "xpConversionRate", 1f);
            Scribe_Values.Look(ref xpRequirementMultiplier, "xpRequirementMultiplier", 1f);
            Scribe_Values.Look(ref skillsLevelNormally, "skillsLevelNormally", false);
            Scribe_Values.Look(ref startingXpMultiplier, "startingXpMultiplier", 1f);
            Scribe_Values.Look(ref npcSkillRollStretch, "npcSkillRollStretch", 1.5f);
            Scribe_Values.Look(ref techMultNeolithic, "techMultNeolithic", 1f);
            Scribe_Values.Look(ref techMultMedieval, "techMultMedieval", 1.5f);
            Scribe_Values.Look(ref techMultIndustrial, "techMultIndustrial", 2f);
            Scribe_Values.Look(ref techMultSpacer, "techMultSpacer", 2.5f);
            Scribe_Values.Look(ref techMultUltra, "techMultUltra", 3f);
            Scribe_Values.Look(ref techMultArchotech, "techMultArchotech", 3.5f);
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
