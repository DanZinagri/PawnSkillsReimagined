using System;
using HarmonyLib;
using Verse;

namespace PawnSkillsReimagined.CharacterDevelopment
{
    // Entry point for the Character Development integration. This assembly is only
    // loaded when Character Development is active (LoadFolders IfModActive), so
    // nothing here needs to check for it. Each feature is initialized separately
    // so one failing (e.g. after a Character Development update) can't take the
    // others down with it.
    [StaticConstructorOnStartup]
    public static class CharacterDevelopmentStartup
    {
        static CharacterDevelopmentStartup()
        {
            var harmony = new Harmony("DanZinagri.PawnSkillsReimagined.CharacterDevelopment");
            Try("lift the skill-reward cap", () => SkillRewardCap.Patch(harmony));
            Try("rescale skill-level wants", SkillLevelWants.Init);
            PawnSkillsReimaginedEvents.SkillRankBought += SkillIncreaseNotifier.Notify;
            PawnSkillsReimaginedEvents.SettingsClosed += SkillLevelWants.Sync;
        }

        private static void Try(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Skills Reimagined] Character Development integration failed to " + what + ": " + e);
            }
        }
    }
}
