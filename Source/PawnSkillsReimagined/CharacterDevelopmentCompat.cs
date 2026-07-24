using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnSkillsReimagined
{
    // Optional integration with Character Development (ferny.characterdevelopment).
    // (Learn is bypassed because it clamps at 20 and our own prefix zeroes its XP), so its patch never fires for bought ranks. We re-emit the exact notification
    // so gotsa patch to call that.
    public static class CharacterDevelopmentCompat
    {
        public static readonly bool Active;

        private static readonly MethodInfo checkWants;
        private static readonly MethodInfo canHaveWants;
        private static readonly ConstructorInfo contextCtor;
        private static readonly object skillIncreasedTrigger;

        static CharacterDevelopmentCompat()
        {
            if (!ModsConfig.IsActive("ferny.characterdevelopment"))
            {
                return;
            }
            try
            {
                Type util = AccessTools.TypeByName("WantsAndQuirks.WantsAndQuirksUtility");
                Type contextType = AccessTools.TypeByName("WantsAndQuirks.WantWorkerContext");
                Type triggerType = AccessTools.TypeByName("WantsAndQuirks.WantTriggerType");
                if (util == null || contextType == null || triggerType == null)
                {
                    return;
                }

                checkWants = AccessTools.Method(util, "CheckWants", new[] { typeof(Pawn), contextType });
                canHaveWants = AccessTools.Method(util, "CanHaveWants", new[] { typeof(Pawn) });
                contextCtor = AccessTools.Constructor(contextType,
                    new[] { triggerType, typeof(Def), typeof(Pawn), typeof(int), typeof(string) });
                skillIncreasedTrigger = Enum.Parse(triggerType, "SkillIncreased");

                Active = checkWants != null && canHaveWants != null &&
                         contextCtor != null && skillIncreasedTrigger != null;
                if (!Active)
                {
                    Log.Warning("[Pawn Skills Reimagined] Character Development is active but its skill-want API " +
                                "could not be resolved; skill-increase wants will not fire for point-bought ranks.");
                }
            }
            catch (Exception e)
            {
                Active = false;
                Log.Warning("[Pawn Skills Reimagined] Character Development integration failed to initialize: " + e);
            }
        }

        // Fire Character Development's SkillIncreased want-check for a rank raised
        // through our point-buy. CanHaveWants naturally excludes non-colonists, so
        // NPC auto-spend during world gen bails cheaply here.
        public static void NotifySkillIncreased(Pawn pawn, SkillDef skill, int newLevel)
        {
            if (!Active || pawn == null || skill == null)
            {
                return;
            }
            try
            {
                if (!(bool)canHaveWants.Invoke(null, new object[] { pawn }))
                {
                    return;
                }
                object context = contextCtor.Invoke(new object[] { skillIncreasedTrigger, skill, null, newLevel, null });
                checkWants.Invoke(null, new object[] { pawn, context });
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Pawn Skills Reimagined] Character Development skill-want notify failed: " + e.Message, 84421007);
            }
        }
    }
}
