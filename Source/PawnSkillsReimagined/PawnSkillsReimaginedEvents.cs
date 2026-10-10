using System;
using RimWorld;
using Verse;

namespace PawnSkillsReimagined
{
    // Hooks for optional integrations that live in separate, conditionally loaded
    // assemblies (e.g. 1.6/Mods/CharacterDevelopment). The main assembly can't
    // reference those, so they subscribe here instead.
    public static class PawnSkillsReimaginedEvents
    {
        // A skill rank was bought with points (player spending or NPC auto-spend).
        // Args: pawn, skill, new level. Point-bought ranks bypass SkillRecord.Learn,
        // so mods that watch Learn for skill increases need this instead.
        public static event Action<Pawn, SkillDef, int> SkillRankBought;

        // A mod settings window closed, so any setting may have changed.
        public static event Action SettingsClosed;

        internal static void RaiseSkillRankBought(Pawn pawn, SkillDef skill, int newLevel) =>
            SkillRankBought?.Invoke(pawn, skill, newLevel);

        internal static void RaiseSettingsClosed() => SettingsClosed?.Invoke();
    }
}
