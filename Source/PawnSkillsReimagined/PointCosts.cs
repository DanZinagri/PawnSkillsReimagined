using System;
using RimWorld;
using Verse;
using VSE.Passions;

namespace PawnSkillsReimagined
{
    // Point cost to raise a skill one rank, driven by its passion.
    public static class PointCosts
    {
        public const int FallbackCost = 5;

        // Settings key + synthetic passion key for the shared "Other" cost.
        // Not a real defName, so it never collides in the passionCosts dict.
        public const string OtherKey = "PSR_Other";

        // Passions that get their own configurable row; everything else folds
        // into OtherKey.
        public static readonly string[] CoreDefNames =
            { "None", "Minor", "Major", "VSE_Critical", "VSE_Apathy" };

        public static bool IsCore(string defName) => Array.IndexOf(CoreDefNames, defName) >= 0;

        // The settings/dict key a passion draws its cost from.
        public static string KeyFor(PassionDef def)
        {
            return def != null && IsCore(def.defName) ? def.defName : OtherKey;
        }

        public static int DefaultForKey(string key)
        {
            switch (key)
            {
                case "None": return 5;          // no passion
                case "Minor": return 3;         // interested
                case "Major": return 2;         // burning
                case "VSE_Critical": return 2;  // critical
                case "VSE_Apathy": return 8;    // uninterested
                default: return 4;              // Other: modded / variable-rate passions
            }
        }

        // Cost for a settings key (a core defName or OtherKey).
        public static int CostForKey(string key)
        {
            var costs = PawnSkillsReimaginedMod.Settings.passionCosts;
            if (costs != null && costs.TryGetValue(key, out int cost) && cost > 0)
            {
                return cost;
            }
            return DefaultForKey(key);
        }

        public static int CostFor(PassionDef def)
        {
            return def == null ? FallbackCost : CostForKey(KeyFor(def));
        }

        public static PassionDef PassionOf(SkillRecord record)
        {
            if (record == null)
            {
                return null;
            }
            int index = (int)record.passion;
            PassionDef[] passions = PassionManager.Passions;
            return passions != null && index >= 0 && index < passions.Length ? passions[index] : null;
        }

        public static int CostFor(SkillRecord record)
        {
            return record == null ? FallbackCost : CostAtLevel(record, record.levelInt);
        }

        // Cost to raise a skill from a given level. The passion base cost rises
        // by 1 every scaleCostInterval ranks (when scaling is enabled), so ranks
        // get progressively more expensive - buying the 20th rank costs more
        // than the 1st even at the same passion. Uses the raw bought level so
        // gene aptitudes don't inflate the price.
        public static int CostAtLevel(SkillRecord record, int level)
        {
            int cost = CostFor(PassionOf(record));
            var settings = PawnSkillsReimaginedMod.Settings;
            if (settings.scaleCostWithLevel && settings.scaleCostInterval > 0 && level > 0)
            {
                cost += level / settings.scaleCostInterval;
            }
            return cost;
        }
    }
}
