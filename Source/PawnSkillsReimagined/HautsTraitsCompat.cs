using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace PawnSkillsReimagined
{
    // Optional integration with Hauts' Added Traits (Hautarche.HautsTraits). Its
    // Latent Psychic trait awakens when any skill reaches 15, which comes far too
    // early under this mod's raised cap. The threshold is a def field
    // (HediffCompProperties_LPMastery.skillLevel on HVT_LatentPsyHediff) that the
    // comp reads live every 60 ticks, so it is simply set from our setting. The Man
    // in Black trait (Hediff_Aptenodytes) checks the same "skill 15" awakening
    // condition with a hardcoded literal, so that one is transpiled to match.
    // Both live in Hauts' Royalty assembly, which only loads with Royalty.
    public static class HautsTraitsCompat
    {
        public const string PackageId = "Hautarche.HautsTraits";
        private const int HautsThreshold = 15;

        private static object latentPsychicProps;
        private static FieldInfo skillLevelField;

        public static bool Active => ModsConfig.IsActive(PackageId);

        public static void Init(Harmony harmony)
        {
            if (!Active)
            {
                return;
            }
            try
            {
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("HVT_LatentPsyHediff");
                if (def == null)
                {
                    return; // Royalty inactive: Hauts' psychic traits aren't loaded
                }
                Type propsType = AccessTools.TypeByName("HautsTraitsRoyalty.HediffCompProperties_LPMastery");
                skillLevelField = propsType == null ? null : AccessTools.Field(propsType, "skillLevel");
                latentPsychicProps = def.comps?.FirstOrDefault(c => propsType != null && propsType.IsInstanceOfType(c));
                if (latentPsychicProps == null || skillLevelField == null)
                {
                    Log.Warning("[Pawn Skills Reimagined] Hauts' Added Traits is active but its Latent Psychic " +
                                "skill threshold was not found; it stays at " + HautsThreshold + ".");
                }

                Type aptenodytes = AccessTools.TypeByName("HautsTraitsRoyalty.Hediff_Aptenodytes");
                MethodInfo tick = aptenodytes == null ? null : AccessTools.DeclaredMethod(aptenodytes, "PostTickInterval");
                if (tick != null)
                {
                    harmony.Patch(tick, transpiler: new HarmonyMethod(typeof(HautsTraitsCompat), nameof(Threshold_Transpiler)));
                }

                Apply();
                PawnSkillsReimaginedEvents.SettingsClosed += Apply;
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Skills Reimagined] Hauts' Added Traits integration failed: " + e);
            }
        }

        // The configured awakening skill level, kept within the skill cap so it
        // stays reachable.
        public static int Threshold()
        {
            var settings = PawnSkillsReimaginedMod.Settings;
            return Mathf.Clamp(settings.hautsAwakeningSkillLevel, 1, Mathf.Max(20, settings.maxSkillLevel));
        }

        public static void Apply()
        {
            if (latentPsychicProps != null)
            {
                skillLevelField.SetValue(latentPsychicProps, Threshold());
            }
        }

        // The method's only int 15 is the skill check ("GetLevel(true) >= 15").
        public static IEnumerable<CodeInstruction> Threshold_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo threshold = AccessTools.Method(typeof(HautsTraitsCompat), nameof(Threshold));
            bool swapped = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.OperandIs((sbyte)HautsThreshold))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = threshold;
                    swapped = true;
                }
                yield return instruction;
            }
            if (!swapped)
            {
                Log.Warning("[Pawn Skills Reimagined] Hauts' Man in Black trait no longer matches the expected " +
                            "code; its skill condition stays at " + HautsThreshold + ".");
            }
        }
    }
}
