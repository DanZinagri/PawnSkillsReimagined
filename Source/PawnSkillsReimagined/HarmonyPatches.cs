using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnSkillsReimagined
{
    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        static HarmonyPatches()
        {
            var harmony = new Harmony("DanZinagri.PawnSkillsReimagined");
            var self = typeof(HarmonyPatches);

            // -- XP funnel: skills never gain or lose XP; earned XP levels the pawn
            harmony.Patch(
                AccessTools.Method(typeof(SkillRecord), nameof(SkillRecord.Learn)),
                prefix: new HarmonyMethod(self, nameof(Learn_Prefix)) { priority = Priority.Last });

            // -- Deterministic starting skills (backstory sums only)
            harmony.Patch(
                AccessTools.Method(typeof(PawnGenerator), "GenerateSkills"),
                postfix: new HarmonyMethod(self, nameof(GenerateSkills_Postfix)));

            // -- Stat scaling across the full rank range --------------------------
            // Prefixes that skip the original: these run on the stat hot path,
            // and a postfix would waste the original's work (including its own
            // GetSkill list scan) just to overwrite the result.
            BuildStatNeedSets();
            harmony.Patch(
                AccessTools.Method(typeof(SkillNeed_Direct), nameof(SkillNeed_Direct.ValueFor)),
                prefix: new HarmonyMethod(self, nameof(SkillNeedDirect_Prefix)));
            harmony.Patch(
                AccessTools.Method(typeof(SkillNeed_BaseBonus), nameof(SkillNeed_BaseBonus.ValueFor)),
                prefix: new HarmonyMethod(self, nameof(SkillNeedBaseBonus_Prefix)));
            harmony.Patch(
                AccessTools.Method(typeof(SkillNeed_Curve), nameof(SkillNeed_Curve.ValueFor)),
                prefix: new HarmonyMethod(self, nameof(SkillNeedCurve_Prefix)));
            harmony.Patch(
                AccessTools.Method(typeof(QualityUtility), nameof(QualityUtility.GenerateQualityCreatedByPawn),
                    new[] { typeof(Pawn), typeof(SkillDef), typeof(bool) }),
                postfix: new HarmonyMethod(self, nameof(Quality_Postfix)),
                transpiler: new HarmonyMethod(self, nameof(QualityLevel_Transpiler)));

            // GetLevel/GetLevelForUI/the Level setter clamp to 0-20; raising that clamp to maxSkillLevel makes every third-party skill UI (RimHUD,
            // Modern Bio tab, character editors) show real ranks with no per-mod patches.
            var unclampTranspiler = new HarmonyMethod(self, nameof(LevelClamp_Transpiler));
            harmony.Patch(
                AccessTools.Method(typeof(SkillRecord), nameof(SkillRecord.GetLevel)),
                transpiler: unclampTranspiler);
            harmony.Patch(
                AccessTools.Method(typeof(SkillRecord), nameof(SkillRecord.GetLevelForUI)),
                transpiler: unclampTranspiler);
            harmony.Patch(
                AccessTools.PropertySetter(typeof(SkillRecord), nameof(SkillRecord.Level)),
                transpiler: unclampTranspiler);
            // Bills keep vanilla 0-20 semantics: their 0-20 range slider with the
            // level re-clamped means "max 20" naturally reads as "20 and up".
            harmony.Patch(
                AccessTools.Method(typeof(Bill), nameof(Bill.PawnAllowedToStartAnew)),
                transpiler: new HarmonyMethod(self, nameof(BillSkillCheck_Transpiler)));
            harmony.Patch(
                AccessTools.PropertyGetter(typeof(SkillRecord), nameof(SkillRecord.LevelDescriptor)),
                postfix: new HarmonyMethod(self, nameof(LevelDescriptor_Postfix)));

            // -- UI --------------------------------------------------------------
            harmony.Patch(
                AccessTools.Method(typeof(SkillUI), nameof(SkillUI.DrawSkill),
                    new[] { typeof(SkillRecord), typeof(Rect), typeof(SkillUI.SkillDrawMode), typeof(string) }),
                transpiler: new HarmonyMethod(self, nameof(DrawSkill_Transpiler)));
            harmony.Patch(
                AccessTools.Method(typeof(SkillUI), "GetSkillDescription"),
                postfix: new HarmonyMethod(self, nameof(GetSkillDescription_Postfix)));

            InjectSkillPointsTab();
        }

        // --------------------------------------------------------------------
        // XP funnel
        // --------------------------------------------------------------------

        // Skill XP is redirected into the pawn's level track. The full learn-rate pipeline (passions, global learning factor, daily saturation) is applied
        // first, so passionate skills level the pawn faster. Negative XP (skill decay) is discarded - skills never rust. Priority.Last so other mods'
        // prefixes that modify xp run before we capture it.
        public static bool Learn_Prefix(SkillRecord __instance, ref float xp, bool direct, bool ignoreLearnRate)
        {
            if (xp > 0f && !__instance.TotallyDisabled)
            {
                float funneled = ignoreLearnRate ? xp : xp * __instance.LearnRateFactor(direct);
                if (funneled > 0f)
                {
                    if (!direct)
                    {
                        // Keep the daily saturation bookkeeping alive so the
                        // 4000 xp/day soft cap still applies to funneled XP.
                        __instance.xpSinceMidnight += funneled;
                    }
                    PawnSkillsReimaginedGameComponent.Instance?.GainXP(__instance.Pawn, funneled);
                }
            }
            // Zero both gains and decay; the original runs as a no-op so other
            // mods' Learn postfixes still fire (and see xp = 0).
            xp = 0f;
            return true;
        }

        // --------------------------------------------------------------------
        // Starting skills
        // --------------------------------------------------------------------

        // Starting levels are rebuilt as pure backstory sums. Vanilla's FinalLevelOfSkill adds a random baseline (~0-4 per skill, age-scaled)
        // on top of backstories; instead of discarding that roll, its value is priced with vanilla's own skill XP curve and granted as starting
        // character XP - so pawns "keep" the life experience vanilla intended, expressed as levels and points. World pawns auto-spend the points
        // randomly (weighted toward their backstory skills); player starting pawns bank them for the player to spend.
        public static void GenerateSkills_Postfix(Pawn pawn)
        {
            if (pawn?.skills?.skills == null)
            {
                return;
            }
            List<BackstoryDef> backstories = pawn.story?.AllBackstories;
            float rolledXp = 0f;
            foreach (SkillRecord record in pawn.skills.skills)
            {
                int total = 0;
                if (backstories != null)
                {
                    for (int i = 0; i < backstories.Count; i++)
                    {
                        List<SkillGain> gains = backstories[i].skillGains;
                        if (gains == null)
                        {
                            continue;
                        }
                        for (int j = 0; j < gains.Count; j++)
                        {
                            if (gains[j].skill == record.def)
                            {
                                total += gains[j].amount;
                            }
                        }
                    }
                }
                total = Mathf.Max(0, total);

                // Price the stripped random roll in vanilla skill XP.
                int rolled = Mathf.Clamp(record.levelInt, 0, 20);
                for (int level = Mathf.Min(total, 20); level < rolled; level++)
                {
                    rolledXp += SkillRecord.XpRequiredToLevelUpFrom(level);
                }

                record.levelInt = total;
                record.xpSinceLastLevel = 0f;
            }

            var comp = PawnSkillsReimaginedGameComponent.Instance;
            float multiplier = PawnSkillsReimaginedMod.Settings.startingXpMultiplier;
            if (comp == null || rolledXp <= 0f || multiplier <= 0f)
            {
                return;
            }
            comp.GrantStartingXP(pawn, rolledXp * multiplier);
            // OfPlayerSilentFail: during world generation no player faction
            // exists yet and the loud accessor spams errors (null is fine here -
            // world pawns have their own faction and should auto-spend).
            if (pawn.Faction != Faction.OfPlayerSilentFail)
            {
                comp.AutoSpendPoints(pawn);
            }
        }

        private static readonly AccessTools.FieldRef<SkillNeed_BaseBonus, float> baseBonusBaseValue =
            AccessTools.FieldRefAccess<SkillNeed_BaseBonus, float>("baseValue");

        private static readonly AccessTools.FieldRef<SkillNeed_BaseBonus, float> baseBonusPerLevel =
            AccessTools.FieldRefAccess<SkillNeed_BaseBonus, float>("bonusPerLevel");

        private static readonly Dictionary<SkillNeed, float> postProcessTargetLevel =
            new Dictionary<SkillNeed, float>();

        // Work-output needs (speeds, yields, efficiency): no probability ceiling,
        // so they extend untempered. Tempering exists only to keep chance stats
        // (surgery/taming/construct success) from spiraling toward 100%.
        private static readonly HashSet<SkillNeed> untemperedNeeds = new HashSet<SkillNeed>();

        private const float DefaultTargetLevel = SkillLevelUtility.VanillaTop + SkillLevelUtility.Headroom;

        private static void BuildStatNeedSets()
        {
            foreach (StatDef stat in DefDatabase<StatDef>.AllDefsListForReading)
            {
                // Speeds/yields/efficiency scale freely - matched by name suffix
                // so modded work stats qualify automatically.
                if (IsWorkOutputStat(stat))
                {
                    AddNeeds(stat.skillNeedFactors, untemperedNeeds);
                    AddNeeds(stat.skillNeedOffsets, untemperedNeeds);
                }

                // Post-processed stats ride their own curve to its end level.
                if (stat.postProcessCurve != null && stat.postProcessCurve.PointsCount >= 2)
                {
                    float lastX = stat.postProcessCurve[stat.postProcessCurve.PointsCount - 1].x;
                    AddTargets(stat.skillNeedFactors, lastX);
                    AddTargets(stat.skillNeedOffsets, lastX);
                }
            }
        }

        private static bool IsWorkOutputStat(StatDef stat)
        {
            string n = stat.defName;
            return n.EndsWith("Speed") || n.EndsWith("Yield") || n.EndsWith("Efficiency");
        }

        private static void AddNeeds(List<SkillNeed> needs, HashSet<SkillNeed> set)
        {
            if (needs == null)
            {
                return;
            }
            for (int i = 0; i < needs.Count; i++)
            {
                set.Add(needs[i]);
            }
        }

        private static void AddTargets(List<SkillNeed> needs, float curveEndX)
        {
            if (needs == null)
            {
                return;
            }
            for (int i = 0; i < needs.Count; i++)
            {
                // Only BaseBonus needs have a defined raw-units-per-level slope to
                // translate the curve's end into a level; other types keep the
                // default mapping and tempering.
                if (needs[i] is SkillNeed_BaseBonus baseBonus)
                {
                    float perLevel = baseBonusPerLevel(baseBonus);
                    if (perLevel > 0f)
                    {
                        float targetLevel = (curveEndX - baseBonusBaseValue(baseBonus)) / perLevel;
                        postProcessTargetLevel[baseBonus] =
                            Mathf.Clamp(targetLevel, DefaultTargetLevel, 60f);
                    }
                }
            }
        }

        // Post-process-bounded needs and work-output needs extend untempered (curves bound the former; the latter have no ceiling to guard). Only
        // chance/factor stats keep the geometric tempering so they can't spiral toward 100%; including under Un-Limited Reborn's raw levels.
        private static float RetentionFor(SkillNeed need)
        {
            if (postProcessTargetLevel.ContainsKey(need) || untemperedNeeds.Contains(need))
            {
                return 1f;
            }
            return PawnSkillsReimaginedMod.Settings.topEndRetention;
        }

        // Single GetSkill lookup shared by the three prefixes; returns the vanilla-domain level for this need, or -1 to fall through to the
        // original. Post-processed needs stretch toward their curve's end level;
        private static float VanillaLevelFor(SkillNeed need, Pawn pawn)
        {
            if (pawn?.skills == null || need.skill == null)
            {
                return -1f;
            }
            int rank = SkillLevelUtility.EffectiveLevel(pawn.skills.GetSkill(need.skill));
            if (rank <= 0)
            {
                return 0f;
            }
            if (SkillLevelUtility.UnlimitedRebornActive)
            {
                return rank;
            }
            float target = postProcessTargetLevel.TryGetValue(need, out float t) ? t : DefaultTargetLevel;
            int max = Mathf.Max(20, PawnSkillsReimaginedMod.Settings.maxSkillLevel);
            return rank * target / max;
        }

        public static bool SkillNeedDirect_Prefix(SkillNeed_Direct __instance, Pawn pawn, ref float __result)
        {
            float vLevel = VanillaLevelFor(__instance, pawn);
            List<float> values = __instance.valuesPerLevel;
            if (vLevel < 0f || values == null || values.Count < 2)
            {
                return true; // vanilla fallback semantics
            }
            int last = values.Count - 1;
            if (vLevel <= last)
            {
                // Interpolate the (stepwise) vanilla list at the stretched level.
                int floor = Mathf.FloorToInt(vLevel);
                int ceil = Mathf.Min(floor + 1, last);
                __result = Mathf.Lerp(values[floor], values[ceil], vLevel - floor);
                return false;
            }
            float gradient = values[last] - values[last - 1];
            __result = values[last] + gradient *
                SkillLevelUtility.GrowthPastCurveEnd(vLevel - last, RetentionFor(__instance));
            return false;
        }

        public static bool SkillNeedBaseBonus_Prefix(SkillNeed_BaseBonus __instance, Pawn pawn, ref float __result)
        {
            float vLevel = VanillaLevelFor(__instance, pawn);
            if (vLevel < 0f)
            {
                return true;
            }
            float baseValue = baseBonusBaseValue(__instance);
            float perLevel = baseBonusPerLevel(__instance);
            float capped = Mathf.Min(vLevel, SkillLevelUtility.VanillaTop);
            __result = baseValue + perLevel * capped + perLevel *
                SkillLevelUtility.GrowthPastCurveEnd(vLevel - SkillLevelUtility.VanillaTop, RetentionFor(__instance));
            return false;
        }

        public static bool SkillNeedCurve_Prefix(SkillNeed_Curve __instance, Pawn pawn, ref float __result)
        {
            float vLevel = VanillaLevelFor(__instance, pawn);
            SimpleCurve curve = __instance.curve;
            if (vLevel < 0f || curve == null || curve.PointsCount < 2)
            {
                return true;
            }
            float lastX = curve[curve.PointsCount - 1].x;
            if (vLevel <= lastX)
            {
                __result = curve.Evaluate(vLevel);
                return false;
            }
            float atEnd = curve.Evaluate(lastX);
            float gradient = atEnd - curve.Evaluate(lastX - 1f);
            __result = atEnd + gradient *
                SkillLevelUtility.GrowthPastCurveEnd(vLevel - lastX, RetentionFor(__instance));
            return false;
        }

        // Feed the quality roll the stretched level instead of the raw rank, so quality progression follows the same 0..maxSkillLevel journey as the
        // stats (rank 30 of 100 rolls like a vanilla level-7 crafter, not 20).
        public static IEnumerable<CodeInstruction> QualityLevel_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getLevel = AccessTools.PropertyGetter(typeof(SkillRecord), nameof(SkillRecord.Level));
            var scaledLevel = AccessTools.Method(typeof(HarmonyPatches), nameof(ScaledQualityLevel));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(getLevel))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = scaledLevel;
                }
                yield return instruction;
            }
        }

        public static int ScaledQualityLevel(SkillRecord record)
        {
            float vLevel = SkillLevelUtility.StretchedVanillaLevel(SkillLevelUtility.EffectiveLevel(record));
            return Mathf.Clamp(Mathf.RoundToInt(vLevel), 0, 20);
        }

        // Once the stretched level passes vanilla's 20, each bonus vanilla-level
        // adds a small chance to bump quality one step.
        public static void Quality_Postfix(ref QualityCategory __result, Pawn pawn, SkillDef relevantSkill)
        {
            float chancePerLevel = PawnSkillsReimaginedMod.Settings.overCapQualityChancePerLevel;
            if (chancePerLevel <= 0f || pawn.RaceProps.IsMechanoid || pawn.skills == null)
            {
                return;
            }
            float vLevel = SkillLevelUtility.StretchedVanillaLevel(
                SkillLevelUtility.EffectiveLevel(pawn.skills.GetSkill(relevantSkill)));
            float chance = Mathf.Max(0f, vLevel - SkillLevelUtility.VanillaTop) * chancePerLevel;
            while (chance > 0f && __result < QualityCategory.Legendary)
            {
                if (!Rand.Chance(Mathf.Min(chance, 1f)))
                {
                    break;
                }
                __result += 1;
                chance -= 1f;
            }
        }

        // --------------------------------------------------------------------
        // Level unclamping
        // --------------------------------------------------------------------

        public static int MaxSkillLevelInt()
        {
            return Mathf.Max(20, PawnSkillsReimaginedMod.Settings.maxSkillLevel);
        }

        // Swap the 20 fed into Mathf.Clamp(value, 0, 20) for the configured max
        // skill level. Applied to GetLevel, GetLevelForUI and the Level setter.
        public static IEnumerable<CodeInstruction> LevelClamp_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var clampInt = AccessTools.Method(typeof(Mathf), nameof(Mathf.Clamp),
                new[] { typeof(int), typeof(int), typeof(int) });
            var maxLevel = AccessTools.Method(typeof(HarmonyPatches), nameof(MaxSkillLevelInt));
            for (int i = 0; i < list.Count - 1; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_I4_S && list[i].OperandIs((sbyte)20) &&
                    list[i + 1].Calls(clampInt))
                {
                    list[i].opcode = OpCodes.Call;
                    list[i].operand = maxLevel;
                }
            }
            return list;
        }

        // Bills compare against a 0-20 range; feed them the level re-clamped to
        // 20 so a rank-40 crafter still matches a max-20 range (vanilla behavior).
        public static IEnumerable<CodeInstruction> BillSkillCheck_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getLevel = AccessTools.PropertyGetter(typeof(SkillRecord), nameof(SkillRecord.Level));
            var billLevel = AccessTools.Method(typeof(HarmonyPatches), nameof(BillCheckLevel));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(getLevel))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = billLevel;
                }
                yield return instruction;
            }
        }

        public static int BillCheckLevel(SkillRecord record)
        {
            return Mathf.Min(record.GetLevel(), 20);
        }

        // GetLevelForUI above 20 falls out of the vanilla descriptor switch as
        // "Unknown"; reuse the top descriptor instead.
        public static void LevelDescriptor_Postfix(SkillRecord __instance, ref string __result)
        {
            if (__result == "Unknown" && __instance.GetLevelForUI() > 20)
            {
                __result = "Skill20".Translate();
            }
        }

        // --------------------------------------------------------------------
        // UI
        // --------------------------------------------------------------------

        // Make the vanilla skill list's fill bar span 0..maxSkillLevel instead
        // of 0..20, clamped for safety. (The level number itself is already
        // real - GetLevel is unclamped at the source.)
        public static IEnumerable<CodeInstruction> DrawSkill_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var maxSkillLevel = AccessTools.Method(typeof(SkillLevelUtility), nameof(SkillLevelUtility.MaxSkillLevelFloat));
            var mathfMax = AccessTools.Method(typeof(Mathf), nameof(Mathf.Max), new[] { typeof(float), typeof(float) });
            var clamp01 = AccessTools.Method(typeof(Mathf), nameof(Mathf.Clamp01));

            bool sawFillConstant = false;
            for (int i = 0; i < list.Count; i++)
            {
                // The fill-bar divisor: level / 20f -> level / maxSkillLevel.
                if (list[i].opcode == OpCodes.Ldc_R4 && list[i].OperandIs(20f) &&
                    i + 1 < list.Count && list[i + 1].opcode == OpCodes.Div)
                {
                    list[i].opcode = OpCodes.Call;
                    list[i].operand = maxSkillLevel;
                    continue;
                }
                if (list[i].opcode == OpCodes.Ldc_R4 && list[i].OperandIs(0.01f))
                {
                    sawFillConstant = true;
                    continue;
                }
                if (sawFillConstant && list[i].Calls(mathfMax))
                {
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Call, clamp01));
                    sawFillConstant = false;
                    i++;
                }
            }
            return list;
        }

        // <summary>Explain the rank, cap and point cost in the skill tooltip.</summary>
        public static void GetSkillDescription_Postfix(SkillRecord sk, ref string __result)
        {
            int level = SkillLevelUtility.EffectiveLevel(sk);
            int max = PawnSkillsReimaginedMod.Settings.maxSkillLevel;
            var passion = PointCosts.PassionOf(sk);
            __result += "\n\nRank " + level + " / " + max +
                        ". XP earned by this skill levels the pawn instead of the skill; higher passions earn faster.";
            if (passion != null)
            {
                __result += "\nRaising this skill costs " + PointCosts.CostFor(sk) +
                            " points (" + passion.label + ").";
            }
        }

        // <summary>Add the skill points tab to every humanlike race.</summary>
        private static void InjectSkillPointsTab()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.race == null || !def.race.Humanlike)
                {
                    continue;
                }
                if (def.inspectorTabs == null)
                {
                    def.inspectorTabs = new List<System.Type>();
                }
                if (!def.inspectorTabs.Contains(typeof(ITab_SkillPoints)))
                {
                    def.inspectorTabs.Add(typeof(ITab_SkillPoints));
                }
                if (def.inspectorTabsResolved == null)
                {
                    def.inspectorTabsResolved = new List<InspectTabBase>();
                }
                if (!def.inspectorTabsResolved.Any(t => t is ITab_SkillPoints))
                {
                    def.inspectorTabsResolved.Add(InspectTabManager.GetSharedInstance(typeof(ITab_SkillPoints)));
                }
            }
        }
    }
}
