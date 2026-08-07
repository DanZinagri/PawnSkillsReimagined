using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace PawnSkillsReimagined
{
    // Vanilla Skills Expanded gates *acquiring* an expertise behind a minimum
    // skill level -- its "LevelToGetExpertise" setting, whose own slider stops at
    // 20 (ExpertiseDef.CanApplyOn: "if (skill.Level < Settings.LevelToGetExpertise)").
    // Since we uncap skills well past 20, this lets that unlock requirement be
    // pushed higher. We redirect VSE's own check to our setting instead of
    // writing VSE's config, so VSE's saved value is left untouched; our value is
    // authoritative only while it is set (> 0), otherwise VSE's own value stands.
    public static class ExpertiseUnlockCompat
    {
        // Effective minimum skill level to acquire an expertise. Our override
        // wins when > 0; a value of 0 defers to VSE's own setting unchanged.
        public static int RequiredLevel()
        {
            int over = PawnSkillsReimaginedMod.Settings.expertiseAcquireLevel;
            return over > 0 ? over : VSE.SkillsMod.Settings.LevelToGetExpertise;
        }

        public static void Patch(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(VSE.Expertise.ExpertiseDef), "CanApplyOn");
                if (target == null)
                {
                    Log.Warning("[Pawn Skills Reimagined] VSE ExpertiseDef.CanApplyOn not found; " +
                                "expertise unlock-level override is inactive.");
                    return;
                }
                harmony.Patch(target, transpiler:
                    new HarmonyMethod(typeof(ExpertiseUnlockCompat), nameof(CanApplyOn_Transpiler)));
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Skills Reimagined] Failed to patch VSE expertise unlock level: " + e);
            }
        }

        // Replace the read of SkillsMod.Settings.LevelToGetExpertise with a call
        // to RequiredLevel(). The preceding ldsfld leaves the Settings instance on
        // the stack, so we pop it before pushing our value in its place.
        public static IEnumerable<CodeInstruction> CanApplyOn_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            MethodInfo getter = AccessTools.Method(typeof(ExpertiseUnlockCompat), nameof(RequiredLevel));
            bool patched = false;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldfld && codes[i].operand is FieldInfo f &&
                    f.Name == "LevelToGetExpertise")
                {
                    CodeInstruction pop = new CodeInstruction(OpCodes.Pop);
                    pop.labels.AddRange(codes[i].labels);
                    codes[i] = pop;
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, getter));
                    patched = true;
                    break;
                }
            }
            if (!patched)
            {
                Log.Warning("[Pawn Skills Reimagined] Could not find LevelToGetExpertise in " +
                            "ExpertiseDef.CanApplyOn; expertise unlock-level override is inactive.");
            }
            return codes;
        }
    }
}
