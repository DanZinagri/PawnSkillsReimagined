using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using WantsAndQuirks;

namespace PawnSkillsReimagined.CharacterDevelopment
{
    // Character Development's RewardWorker_Skill (its "+1 to a skill" reward)
    // hardcodes 20 as the ceiling in CanBestowOn ("Level >= 20") and its
    // "Level < 20" LINQ predicates. Since we unclamped GetLevel, those compare the
    // real level against 20 and wrongly exclude any skill 20+.
    public static class SkillRewardCap
    {
        public static void Patch(Harmony harmony)
        {
            Type rw = typeof(RewardWorker_Skill);
            var lift = new HarmonyMethod(typeof(SkillRewardCap), nameof(LiftSkillCap_Transpiler));
            // Declared lookups: if a future version stopped overriding one of these,
            // a plain lookup would patch the base RewardWorker - every reward.
            TryTranspile(harmony, AccessTools.DeclaredMethod(rw, nameof(RewardWorker_Skill.CanBestowOn)), lift);
            TryTranspile(harmony, AccessTools.DeclaredMethod(rw, nameof(RewardWorker_Skill.OnAcquired)), lift);
            // The "Level < 20" predicates are non-capturing lambdas the compiler
            // emits into a nested <>c class.
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

        private static void TryTranspile(Harmony harmony, MethodBase method, HarmonyMethod transpiler)
        {
            if (method != null)
            {
                harmony.Patch(method, transpiler: transpiler);
            }
        }

        // Replace the constant 20 with our configured max skill level. Only applied
        // to the tightly-scoped methods above, whose only literal 20s are skill caps.
        public static IEnumerable<CodeInstruction> LiftSkillCap_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo maxLevel = AccessTools.Method(typeof(HarmonyPatches), nameof(HarmonyPatches.MaxSkillLevelInt));
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
