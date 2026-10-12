using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using WantsAndQuirks;

namespace PawnSkillsReimagined.CharacterDevelopment
{
    // Character Development hardcodes its skill rewards at +1 (skill.Level++ in
    // RewardWorker_Skill.OnAcquired), which is negligible against a raised skill
    // cap. This swaps that literal 1 for the cdSkillRewardAmount setting and leaves
    // the rest of CD's reward logic alone; reward descriptions are rewritten to match.
    public static class SkillRewardAmounts
    {
        private const string MessageKey = "PSR_CdSkillIncreased";

        // CD's own description for each skill reward, so the text can be re-derived
        // (or restored) every time the setting changes.
        private static readonly Dictionary<RewardDef, string> shippedDescriptions =
            new Dictionary<RewardDef, string>();

        public static void Patch(Harmony harmony)
        {
            MethodInfo onAcquired =
                AccessTools.DeclaredMethod(typeof(RewardWorker_Skill), nameof(RewardWorker_Skill.OnAcquired));
            if (onAcquired != null)
            {
                harmony.Patch(onAcquired,
                    transpiler: new HarmonyMethod(typeof(SkillRewardAmounts), nameof(Amount_Transpiler)));
            }

            foreach (RewardDef def in DefDatabase<RewardDef>.AllDefsListForReading)
            {
                if (def.description != null && typeof(RewardWorker_Skill).IsAssignableFrom(def.workerClass))
                {
                    shippedDescriptions[def] = def.description;
                }
            }
            SyncDescriptions();
        }

        // Levels a skill reward grants.
        public static int Amount()
        {
            return Mathf.Max(1, PawnSkillsReimaginedMod.Settings.cdSkillRewardAmount);
        }

        // "Level + 1" -> "Level + Amount()", and CD's message key (whose text says
        // "increased by 1") -> ours.
        public static IEnumerable<CodeInstruction> Amount_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo setLevel = AccessTools.PropertySetter(typeof(SkillRecord), nameof(SkillRecord.Level));
            MethodInfo amount = AccessTools.Method(typeof(SkillRewardAmounts), nameof(Amount));
            bool swapped = false;
            for (int i = 0; i < list.Count; i++)
            {
                CodeInstruction instruction = list[i];
                if (!swapped && instruction.opcode == OpCodes.Ldc_I4_1 && i + 2 < list.Count &&
                    list[i + 1].opcode == OpCodes.Add && list[i + 2].Calls(setLevel))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = amount;
                    swapped = true;
                }
                else if (swapped && instruction.opcode == OpCodes.Ldstr &&
                         (string)instruction.operand == "WQ_SkillIncreased")
                {
                    instruction.operand = MessageKey;
                }
                yield return instruction;
            }
            if (!swapped)
            {
                Log.Warning("[Pawn Skills Reimagined] Character Development's skill reward no longer matches the " +
                            "expected code; it will keep granting +1 level.");
            }
        }

        // Reward cards say "by 1 level"; rewrite the number from the setting so the
        // text matches what is granted. Derived from CD's shipped text each time and
        // patched in place, so translated descriptions keep their wording. Called at
        // startup and whenever a settings window closes.
        public static void SyncDescriptions()
        {
            int amount = Amount();
            foreach (KeyValuePair<RewardDef, string> kvp in shippedDescriptions)
            {
                string text = kvp.Value;
                if (amount != 1)
                {
                    text = Regex.Replace(kvp.Value, @"\b1 level\b", amount + " levels");
                    if (text == kvp.Value)
                    {
                        text = Regex.Replace(kvp.Value, @"\b1\b", amount.ToString());
                    }
                }
                kvp.Key.description = text;
            }
        }
    }
}
