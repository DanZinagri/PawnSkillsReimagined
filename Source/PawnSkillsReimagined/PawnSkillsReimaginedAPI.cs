using Verse;

namespace PawnSkillsReimagined
{
    // Public interop surface for other mods. Every signature uses only vanilla
    // types (Pawn / int / float), so this can be called either by a hard
    // assembly reference or reflectively without depending on our own types.
    //
    // All methods are null-safe and degrade to no-ops / defaults when no game is
    // loaded or the component is missing, so callers never need to guard for
    // load state. Reads never create tracking entries.
    //
    // Reflection example (no hard dependency):
    //   var api  = AccessTools.TypeByName("PawnSkillsReimagined.PawnSkillsReimaginedAPI");
    //   int lvl  = (int)AccessTools.Method(api, "GetLevel").Invoke(null, new object[] { pawn });
    public static class PawnSkillsReimaginedAPI
    {
        private static PawnSkillsReimaginedGameComponent Comp => PawnSkillsReimaginedGameComponent.Instance;

        // The configured character-level ceiling (maxCharacterLevel setting).
        public static int MaxLevel => PawnSkillsReimaginedGameComponent.MaxLevel;

        // ---- Reads --------------------------------------

        // Character level. Untracked pawns report the base level of 1.
        public static int GetLevel(Pawn pawn)
        {
            return Comp?.GetProgressOrNull(pawn)?.level ?? 1;
        }

        // XP accumulated toward the next level.
        public static float GetXp(Pawn pawn)
        {
            return Comp?.GetProgressOrNull(pawn)?.xp ?? 0f;
        }

        // XP required to advance from the pawn's current level to the next.
        public static float GetXpForNextLevel(Pawn pawn)
        {
            return PawnSkillsReimaginedGameComponent.XpToNext(GetLevel(pawn));
        }

        // Unspent skill points available to spend on skill ranks.
        public static int GetAvailablePoints(Pawn pawn)
        {
            return Comp?.AvailableFor(pawn) ?? 0;
        }

        // Skill points already spent on ranks.
        public static int GetSpentPoints(Pawn pawn)
        {
            return Comp?.GetProgressOrNull(pawn)?.spentPoints ?? 0;
        }

        // True if this pawn has any tracked progress at all (has earned XP or had
        // starting XP granted). Lets callers distinguish "level 1, tracked" from
        // "never tracked" if they care.
        public static bool IsTracked(Pawn pawn)
        {
            return Comp?.GetProgressOrNull(pawn) != null;
        }

        // ---- Mutations -----------------------------------------------------

        // Funnel raw XP into the pawn, leveling up as thresholds are crossed.
        // Behaves exactly like earned XP: colonists get the usual level-up
        // message. Use this for XP-style rewards.
        public static void AddXp(Pawn pawn, float amount)
        {
            Comp?.GainXP(pawn, amount);
        }

        // Grant (or remove, if negative) whole character levels directly, along
        // with the skill points those levels carry. Silent (no message), clamped
        // to [1, MaxLevel]. Use this for "instant +N levels" rewards.
        public static void AddLevels(Pawn pawn, int count)
        {
            Comp?.AddLevels(pawn, count);
        }

        // Set the pawn's character level exactly, clamped to [1, MaxLevel].
        // Silent. Skill points are derived from level, so raising the level
        // grants points and lowering it can leave available points at zero.
        public static void SetLevel(Pawn pawn, int level)
        {
            Comp?.SetLevel(pawn, level);
        }
    }
}
