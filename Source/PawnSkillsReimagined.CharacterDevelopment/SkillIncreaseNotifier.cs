using System;
using RimWorld;
using Verse;
using WantsAndQuirks;

namespace PawnSkillsReimagined.CharacterDevelopment
{
    // Character Development checks skill-increase wants from a patch on
    // SkillRecord.Learn, which point-bought ranks bypass (Learn clamps at 20 and our
    // prefix zeroes its XP). This re-emits the same notification for each bought rank.
    public static class SkillIncreaseNotifier
    {
        public static void Notify(Pawn pawn, SkillDef skill, int newLevel)
        {
            // CanHaveWants excludes non-colonists, so NPC auto-spend during world
            // generation bails cheaply here.
            if (pawn == null || skill == null || !pawn.CanHaveWants())
            {
                return;
            }
            // Runs inside our point spending; an exception from Character
            // Development must not abort the spend.
            try
            {
                WantsAndQuirksUtility.CheckWants(pawn,
                    new WantWorkerContext(WantTriggerType.SkillIncreased, skill, null, newLevel));
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Pawn Skills Reimagined] Character Development skill-want notify failed: " + e.Message,
                    84421007);
            }
        }
    }
}
