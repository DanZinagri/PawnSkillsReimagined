using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using Verse;
using WantsAndQuirks;

namespace PawnSkillsReimagined.CharacterDevelopment
{
    // Rescales Character Development's "reach skill level N" wants to the raised
    // skill cap. CD ships four (WQ_WantSkillLevel1-4 at 5/10/15/20); they are
    // re-pointed to 10/20/30/40 and further tiers are generated up to the cap, every
    // 10 ranks (or every 5 with the setting). CD's "become a master" wording and icon
    // always sit on the top tier. Below a cap of 40 the remap makes no sense, so CD
    // is left exactly as shipped and nothing is generated.
    //
    // WantWorker_SkillLevel is fully threshold-driven and CD picks wants from the
    // live DefDatabase, so runtime-added defs need no other changes.
    public static class SkillLevelWants
    {
        private const string TierPrefix = "PSR_WantSkillLevel_";
        // CD's four skill-level wants have a combined commonality of 1.8; that total
        // is shared across all active tiers so a long ladder doesn't flood the pool.
        private const float LadderWeight = 1.8f;
        // CD's reward range: its lowest skill-level want and its "master" want.
        private const float MinReward = 2000f;
        private const float MaxReward = 8000f;
        // Highest tier name a save could reference (maxSkillLevel's slider caps at 999).
        private const int MaxPossibleTier = 1000;
        private const string SkillIcon = "UI/WantIcons/Skill";

        private static readonly string[] OriginalDefs =
            { "WQ_WantSkillLevel1", "WQ_WantSkillLevel2", "WQ_WantSkillLevel3", "WQ_WantSkillLevel4" };
        private static readonly int[] OriginalTiers = { 10, 20, 30, 40 };
        // Rescaling only engages once the cap reaches the highest remapped tier.
        private const int MinCap = 40;

        // Private members; reflection is the only way in.
        private static readonly FieldInfo IconCache = AccessTools.Field(typeof(WantDef), "iconInt");
        private static readonly FieldInfo CdWantListCache =
            AccessTools.Field(typeof(WantsAndQuirksSettings), "normalWantDefsCache");

        private class Tier
        {
            public WantDef def;
            // Wording and icon used when this tier is not the top (master) tier.
            public string label;
            public string description;
            public string icon;
            // CD's shipped values, restored when rescaling is off (originals only).
            public bool original;
            public int cdThreshold;
            public string cdLabel;
            public string cdDescription;
            public string cdIcon;
            public int cdReward;
            public float cdCommonality;
        }

        // Every tier we manage (re-pointed originals + generated), by rescaled threshold.
        private static readonly SortedDictionary<int, Tier> tiers = new SortedDictionary<int, Tier>();

        // CD's master want text/icon, moved onto whichever tier is the top.
        private static string masterLabel;
        private static string masterDescription;
        private static int masterCdThreshold;
        private static string masterIcon;
        private static bool initialized;

        public static void Init()
        {
            for (int i = 0; i < OriginalDefs.Length; i++)
            {
                WantDef def = DefDatabase<WantDef>.GetNamedSilentFail(OriginalDefs[i]);
                if (def == null)
                {
                    continue;
                }
                var tier = new Tier
                {
                    def = def,
                    original = true,
                    cdThreshold = def.skillLevelThreshold,
                    cdLabel = def.label,
                    cdDescription = def.description,
                    cdIcon = def.iconPath,
                    cdReward = def.reward,
                    cdCommonality = def.commonality,
                };
                int now = OriginalTiers[i];
                tier.label = def.label;
                tier.description = ReplaceNumber(def.description, tier.cdThreshold, now);
                tier.icon = tier.cdIcon;
                if (i == OriginalDefs.Length - 1)
                {
                    // The master want becomes an ordinary rung; its wording and icon
                    // move to the top tier.
                    masterLabel = def.label;
                    masterDescription = def.description;
                    masterCdThreshold = tier.cdThreshold;
                    masterIcon = tier.cdIcon;
                    tier.label = GenericLabel(now);
                    tier.icon = SkillIcon;
                }
                tiers[now] = tier;
            }

            RegisterRemovedTierNames();
            initialized = true;
            Sync();
        }

        // Applies the current settings: below the minimum cap, restores CD as
        // shipped; otherwise creates any missing tier up to the cap and re-weights
        // every tier. Out-of-range tiers get commonality 0 (never rolled) rather than
        // being removed, because active wants in the save may still reference them.
        // Called at startup and whenever a settings window closes.
        public static void Sync()
        {
            if (!initialized)
            {
                return;
            }
            var settings = PawnSkillsReimaginedMod.Settings;
            int cap = settings.maxSkillLevel;
            if (cap < MinCap)
            {
                RestoreShipped();
                return;
            }
            int step = settings.cdSkillWantsEvery5 ? 5 : 10;

            int active = 0;
            int top = step;
            for (int t = step; t <= cap; t += step)
            {
                active++;
                top = t;
                if (!tiers.ContainsKey(t))
                {
                    CreateTier(t);
                }
            }

            float weight = LadderWeight / Mathf.Max(1, active);
            foreach (KeyValuePair<int, Tier> kvp in tiers)
            {
                int t = kvp.Key;
                Tier tier = kvp.Value;
                bool on = t <= cap && t % step == 0;
                if (t == top)
                {
                    Apply(tier.def, t, masterLabel, ReplaceNumber(masterDescription, masterCdThreshold, t),
                        masterIcon);
                }
                else
                {
                    Apply(tier.def, t, tier.label, tier.description, tier.icon);
                }
                tier.def.commonality = on ? weight : 0f;
                tier.def.reward = RewardFor(t, top);
            }
            InvalidateCdSettingsCache();
        }

        // CD exactly as shipped: originals back to their own values, generated tiers
        // switched off.
        private static void RestoreShipped()
        {
            foreach (Tier tier in tiers.Values)
            {
                if (tier.original)
                {
                    Apply(tier.def, tier.cdThreshold, tier.cdLabel, tier.cdDescription, tier.cdIcon);
                    tier.def.reward = tier.cdReward;
                    tier.def.commonality = tier.cdCommonality;
                }
                else
                {
                    tier.def.commonality = 0f;
                }
            }
            InvalidateCdSettingsCache();
        }

        private static void Apply(WantDef def, int threshold, string label, string description, string icon)
        {
            def.skillLevelThreshold = threshold;
            def.label = label;
            def.description = description;
            def.iconPath = icon;
            IconCache?.SetValue(def, null);
            def.ClearCachedData();
        }

        private static void CreateTier(int threshold)
        {
            string name = TierPrefix + threshold;
            WantDef def = DefDatabase<WantDef>.GetNamedSilentFail(name);
            if (def == null)
            {
                def = new WantDef
                {
                    defName = name,
                    workerClass = typeof(WantWorker_SkillLevel),
                    ignoreIllegalLabelCharacterConfigError = true,
                };
                // Def.GetHashCode/Equals use this; left at 0, every generated tier
                // would compare equal to every other in hashed collections.
                def.ResolveDefNameHash();
                DefDatabase<WantDef>.Add(def);
            }
            tiers[threshold] = new Tier
            {
                def = def,
                label = GenericLabel(threshold),
                description = "I'd like to reach level " + threshold + " in my {0} skill.",
                icon = SkillIcon,
            };
        }

        private static string GenericLabel(int threshold) => "reach level " + threshold + " in {0}";

        // Swaps a level number inside CD's text in place, so translations survive.
        private static string ReplaceNumber(string text, int from, int to) =>
            text == null ? null : Regex.Replace(text, @"\b" + from + @"\b", to.ToString());

        // CD's reward range, eased so the top (master) tier pays the full amount.
        private static int RewardFor(int threshold, int top)
        {
            float f = Mathf.Clamp01((float)threshold / top);
            float reward = MinReward + (MaxReward - MinReward) * Mathf.Pow(f, 1.5f);
            return Mathf.RoundToInt(reward / 100f) * 100;
        }

        // A save made with a higher cap (or the every-5 option on) can reference a
        // tier this session didn't generate. Marking every possible tier name as
        // "removed" makes vanilla skip it quietly instead of logging a red error;
        // CD then drops wants whose def didn't load. Only consulted on failed loads.
        private static void RegisterRemovedTierNames()
        {
            FieldInfo f = AccessTools.Field(typeof(BackCompatibility), "RemovedDefs");
            if (!(f?.GetValue(null) is List<Tuple<string, Type>> removed))
            {
                return;
            }
            for (int t = 5; t <= MaxPossibleTier; t += 5)
            {
                removed.Add(new Tuple<string, Type>(TierPrefix + t, typeof(WantDef)));
            }
        }

        // CD caches its settings-window want list (sorted by label) on first open;
        // clear it so new tiers and moved labels show up there.
        private static void InvalidateCdSettingsCache()
        {
            if (WantsAndQuirksMod.settings != null)
            {
                CdWantListCache?.SetValue(WantsAndQuirksMod.settings, null);
            }
        }
    }
}
