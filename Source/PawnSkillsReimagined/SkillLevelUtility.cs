using RimWorld;
using UnityEngine;
using Verse;

namespace PawnSkillsReimagined
{
    // The level model: skill ranks run 0..maxSkillLevel (hard cap, default 100)
    public static class SkillLevelUtility
    {
        // Vanilla's stat curves end at level 20. Unprotected stats (no
        // postProcessCurve) stretch to this many vanilla-levels past that as a
        // maxed-skill headroom, with the extra tempered by topEndRetention.
        // Post-processed stats ignore Headroom and ride their curve to its top.
        public const float VanillaTop = 20f;
        public const float Headroom = 4f;

        // Un-Limited Reborn reshapes stat postProcessCurves itself and expects raw unclamped levels; when it's loaded the stretch mapping is bypassed
        // (per-need tempering decisions live in HarmonyPatches.RetentionFor).
        public static readonly bool UnlimitedRebornActive = ModsConfig.IsActive("NuanKi.UnlimitedReborn");

        // Map a real rank onto the vanilla stat-curve domain: rank/maxSkillLevel of the way to (VanillaTop + Headroom).
        public static float StretchedVanillaLevel(int rank)
        {
            if (rank <= 0)
            {
                return 0f;
            }
            if (UnlimitedRebornActive)
            {
                return rank;
            }
            int max = Mathf.Max(20, PawnSkillsReimaginedMod.Settings.maxSkillLevel);
            return rank * (VanillaTop + Headroom) / max;
        }

        // Effective skill rank: unclamped, never negative, includes aptitudes.
        public static int EffectiveLevel(SkillRecord record)
        {
            if (record == null || record.TotallyDisabled)
            {
                return 0;
            }
            int level = record.GetUnclampedLevel();
            return level > 0 ? level : 0;
        }

        // growth for the stretch past vanilla's curve end: the first vanilla-level of headroom is worth `retention` of the curve's final
        // per-level gain, the next retention^2, etc. Supports fractional levels.
        public static float GrowthPastCurveEnd(float levelsPast, float retention)
        {
            if (levelsPast <= 0f)
            {
                return 0f;
            }
            if (retention >= 0.999f)
            {
                return levelsPast;
            }
            return (1f - Mathf.Pow(retention, levelsPast)) / (1f - retention);
        }

        // Level to show in skill UI: the real rank.
        public static int DisplayLevel(SkillRecord record, bool includeAptitudes)
        {
            if (record.TotallyDisabled)
            {
                return 0;
            }
            int unclamped = record.GetUnclampedLevel();
            return unclamped > 0 ? unclamped : 0;
        }

        // Fill-bar divisor for the vanilla skill list (replaces the hardcoded 20).
        public static float MaxSkillLevelFloat()
        {
            return Mathf.Max(1, PawnSkillsReimaginedMod.Settings.maxSkillLevel);
        }
    }
}
