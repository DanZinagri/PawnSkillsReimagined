using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace PawnSkillsReimagined
{
    // Overrides how Vanilla Skills Expanded's ExpertiseDef.CanApplyOn gates
    // acquiring an expertise. That one method reads three VSE settings we want to
    // replace with our own per-pawn rules, so we transpile all three reads at once
    public static class ExpertiseUnlockCompat
    {
        // Effective minimum skill level to acquire an expertise. Our override
        // wins when > 0; a value of 0 defers to VSE's own setting unchanged.
        public static int RequiredLevel()
        {
            int over = PawnSkillsReimaginedMod.Settings.expertiseAcquireLevel;
            return over > 0 ? over : VSE.SkillsMod.Settings.LevelToGetExpertise;
        }

        // Per-pawn maximum expertise count, replacing VSE's global MaxExpertise.
        public static int MaxExpertise(Pawn pawn)
        {
            return PawnSkillsReimaginedGameComponent.Instance?.MaxExpertiseFor(pawn)
                   ?? VSE.SkillsMod.Settings.MaxExpertise;
        }

        public static void Patch(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(VSE.Expertise.ExpertiseDef), "CanApplyOn");
                if (target == null)
                {
                    Log.Warning("[Pawn Skills Reimagined] VSE ExpertiseDef.CanApplyOn not found; " +
                                "expertise overrides are inactive.");
                    return;
                }
                harmony.Patch(target, transpiler:
                    new HarmonyMethod(typeof(ExpertiseUnlockCompat), nameof(CanApplyOn_Transpiler)));
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Skills Reimagined] Failed to patch VSE expertise gating: " + e);
            }
        }

        // Replace each VSE settings field read with our own value. Every read is
        // "ldsfld SkillsMod.Settings; ldfld <field>", so at the ldfld the Settings
        // instance is on the stack, then push our replacement.
        public static IEnumerable<CodeInstruction> CanApplyOn_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            MethodInfo requiredLevel = AccessTools.Method(typeof(ExpertiseUnlockCompat), nameof(RequiredLevel));
            MethodInfo maxExpertise = AccessTools.Method(typeof(ExpertiseUnlockCompat), nameof(MaxExpertise));
            bool level = false, max = false, overlap = false;

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Ldfld || !(codes[i].operand is FieldInfo f))
                {
                    continue;
                }

                CodeInstruction pop = new CodeInstruction(OpCodes.Pop);
                pop.labels.AddRange(codes[i].labels);

                if (f.Name == "LevelToGetExpertise")
                {
                    codes[i] = pop;
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, requiredLevel));
                    level = true;
                }
                else if (f.Name == "MaxExpertise")
                {
                    codes[i] = pop;
                    // Push the pawn argument (CanApplyOn is instance: arg0=this, arg1=pawn).
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_1));
                    codes.Insert(i + 2, new CodeInstruction(OpCodes.Call, maxExpertise));
                    max = true;
                }
                else if (f.Name == "AllowExpertiseOverlap")
                {
                    codes[i] = pop;
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldc_I4_1)); // true
                    overlap = true;
                }
            }

            if (!level || !max || !overlap)
            {
                Log.Warning("[Pawn Skills Reimagined] VSE CanApplyOn transpile incomplete " +
                            "(level=" + level + " max=" + max + " overlap=" + overlap +
                            "); some expertise overrides are inactive.");
            }
            return codes;
        }
    }
}
