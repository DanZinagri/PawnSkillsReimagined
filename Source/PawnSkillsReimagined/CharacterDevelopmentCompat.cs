using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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

        // --------------------------------------------------------------------
        // Reward cap lift (startup Harmony transpile)
        // --------------------------------------------------------------------

        // CD's RewardWorker_Skill (its "+1 to a skill" quirk reward) hardcodes 20
        // as the ceiling in CanBestowOn ("Level >= 20") and its "Level < 20" LINQ
        // predicates. Since we unclamped GetLevel, those compare the real level
        // against 20 and wrongly exclude any skill 20+.
        public static void PatchRewardCap(Harmony harmony)
        {
            if (!ModsConfig.IsActive("ferny.characterdevelopment"))
            {
                return;
            }
            try
            {
                Type rw = AccessTools.TypeByName("WantsAndQuirks.RewardWorker_Skill");
                if (rw == null)
                {
                    Log.Warning("[Pawn Skills Reimagined] Character Development is active but RewardWorker_Skill " +
                                "was not found; its skill reward will stay capped at 20.");
                    return;
                }
                var lift = new HarmonyMethod(typeof(CharacterDevelopmentCompat), nameof(LiftSkillCap_Transpiler));
                TryTranspile(harmony, AccessTools.Method(rw, "CanBestowOn"), lift);
                TryTranspile(harmony, AccessTools.Method(rw, "OnAcquired"), lift);
                // The "Level < 20" predicates are non-capturing lambdas emitted into
                // a nested <>c class; patch those methods too.
                foreach (Type nested in rw.GetNestedTypes(AccessTools.all))
                {
                    foreach (MethodInfo m in AccessTools.GetDeclaredMethods(nested))
                    {
                        if (m.Name.Contains("b__"))
                        {
                            TryTranspile(harmony, m, lift);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Skills Reimagined] Failed to lift Character Development skill-reward cap: " + e);
            }
        }

        private static void TryTranspile(Harmony harmony, MethodBase method, HarmonyMethod transpiler)
        {
            if (method != null)
            {
                harmony.Patch(method, transpiler: transpiler);
            }
        }

        // Replace the constant 20 with our configured max skill level. Only applied
        // to the tightly-scoped CD reward methods above, whose only literal 20s are
        // skill-level caps.
        public static IEnumerable<CodeInstruction> LiftSkillCap_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var maxLevel = AccessTools.Method(typeof(HarmonyPatches), nameof(HarmonyPatches.MaxSkillLevelInt));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.operand != null &&
                    (instruction.opcode == OpCodes.Ldc_I4_S || instruction.opcode == OpCodes.Ldc_I4) &&
                    Convert.ToInt32(instruction.operand) == 20)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = maxLevel;
                }
                yield return instruction;
            }
        }
    }
}
