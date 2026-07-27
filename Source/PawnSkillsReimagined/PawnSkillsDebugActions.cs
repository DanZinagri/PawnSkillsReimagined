using LudeonTK;
using RimWorld;
using Verse;

namespace PawnSkillsReimagined
{
    // Dev-mode tools for testing and for fixing pawns if their level or points
    // ever end up wrong. Each entry appears in the debug actions menu under the
    // "Pawn Skills Reimagined" category; select one, then click a pawn to apply.
    public static class PawnSkillsDebugActions
    {
        private const string Category = "Pawn Skills Reimagined";
        private const DebugActionType PerPawn = DebugActionType.ToolMapForPawns;
        private const AllowedGameStates OnMap = AllowedGameStates.PlayingOnMap;

        [DebugAction(Category, "Skill points: +1", actionType = PerPawn, allowedGameStates = OnMap)]
        private static void AddPoint(Pawn p) => Apply(p, comp => comp.AddSkillPoints(p, 1));

        [DebugAction(Category, "Skill points: -1", actionType = PerPawn, allowedGameStates = OnMap)]
        private static void RemovePoint(Pawn p) => Apply(p, comp => comp.AddSkillPoints(p, -1));

        [DebugAction(Category, "Skill points: +5", actionType = PerPawn, allowedGameStates = OnMap)]
        private static void AddFivePoints(Pawn p) => Apply(p, comp => comp.AddSkillPoints(p, 5));

        [DebugAction(Category, "Skill points: -5", actionType = PerPawn, allowedGameStates = OnMap)]
        private static void RemoveFivePoints(Pawn p) => Apply(p, comp => comp.AddSkillPoints(p, -5));

        [DebugAction(Category, "Level: +1 (with points)", actionType = PerPawn, allowedGameStates = OnMap)]
        private static void AddLevelWithPoints(Pawn p) => Apply(p, comp => comp.AddLevels(p, 1));

        [DebugAction(Category, "Level: -1 (with points)", actionType = PerPawn, allowedGameStates = OnMap)]
        private static void RemoveLevelWithPoints(Pawn p) => Apply(p, comp => comp.AddLevels(p, -1));

        [DebugAction(Category, "Level: +1 (no points)", actionType = PerPawn, allowedGameStates = OnMap)]
        private static void AddLevelNoPoints(Pawn p) => Apply(p, comp => comp.AddLevelsNoPoints(p, 1));

        [DebugAction(Category, "Level: -1 (no points)", actionType = PerPawn, allowedGameStates = OnMap)]
        private static void RemoveLevelNoPoints(Pawn p) => Apply(p, comp => comp.AddLevelsNoPoints(p, -1));

        // Runs the change, then floats the resulting level/point totals over the
        // pawn so the effect is visible immediately.
        private static void Apply(Pawn pawn, System.Action<PawnSkillsReimaginedGameComponent> change)
        {
            PawnSkillsReimaginedGameComponent comp = PawnSkillsReimaginedGameComponent.Instance;
            if (comp == null || pawn == null)
            {
                return;
            }
            change(comp);
            if (pawn.Spawned && pawn.Map != null)
            {
                PawnProgress p = comp.For(pawn);
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map,
                    "Lv " + p.level + "  |  " + comp.AvailableFor(pawn) + " pts");
            }
        }
    }
}
