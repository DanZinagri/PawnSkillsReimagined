using RimWorld;
using Verse;
using VSE.Passions;

namespace PawnSkillsReimagined
{
    // Point cost to raise a skill one rank, driven by its passion.
    public static class PointCosts
    {
        public const int FallbackCost = 5;

        // Passions with hand-tuned defaults, listed first in the settings tab above
        // a divider that separates them from the auto-generated modded-passion rows.
        public static readonly string[] DefinedDefNames =
            { "None", "Minor", "Major", "VSE_Critical", "VSE_Apathy", "AS_FrozenPassion" };

        public static bool IsDefined(string defName) => System.Array.IndexOf(DefinedDefNames, defName) >= 0;

        // The settings/dict key a passion draws its cost from: its own defName, so
        // every passion (core or modded) is configured individually.
        public static string KeyFor(PassionDef def) => def?.defName ?? "None";

        public static int DefaultForKey(string key)
        {
            switch (key)
            {
                case "None": return 5;              // no passion
                case "Minor": return 3;             // interested
                case "Major": return 2;             // burning
                case "VSE_Critical": return 2;      // critical
                case "VSE_Apathy": return 8;        // uninterested
                case "AS_FrozenPassion": return 10; // Alpha Skills: no learning, most expensive
                default: return 4;                  // any other modded passion
            }
        }

        // Cost for a passion's settings key (its defName).
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
