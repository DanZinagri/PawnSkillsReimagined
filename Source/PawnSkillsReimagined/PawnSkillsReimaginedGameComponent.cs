using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnSkillsReimagined
{
    // Per-pawn leveling progress. Levels start at 1; every level-up grants
    // pointsPerLevel skill points, spent at passion-based costs.
    public class PawnProgress : IExposable
    {
        public int level = 1;
        public float xp;
        public int spentPoints;

        public void ExposeData()
        {
            Scribe_Values.Look(ref level, "level", 1);
            Scribe_Values.Look(ref xp, "xp", 0f);
            Scribe_Values.Look(ref spentPoints, "spentPoints", 0);
        }
    }

    // The leveling system: skill XP earned by pawns is funneled here (the skill
    // itself gains nothing), levels follow an Isekai-style power curve, and each
    // level grants a point to spend on any skill (uncapped) or VSE expertise
    // (capped at 20).
    public class PawnSkillsReimaginedGameComponent : GameComponent
    {
        // XP to go from `level` to the next: 100 * level^1.5, floor 100.
        // Same curve Isekai Leveling uses (level 7 -> 1.85k, level 10 -> 3.16k).
        private const float XpBase = 100f;
        private const float XpExponent = 1.5f;

        private Dictionary<Pawn, PawnProgress> progress = new Dictionary<Pawn, PawnProgress>();

        private List<Pawn> tmpPawns;
        private List<PawnProgress> tmpProgress;

        // Cached statically: Instance is read from the Learn hot path (every work
        // tick), and Game.GetComponent<T>() is a linear scan of the component
        // list. A new Game constructs a new component, refreshing the cache.
        private static PawnSkillsReimaginedGameComponent cached;

        public PawnSkillsReimaginedGameComponent(Game game)
        {
            cached = this;
        }

        public static PawnSkillsReimaginedGameComponent Instance => cached;

        // Pawn level cap - its own setting, independent from the skill rank cap.
        public static int MaxLevel =>
            Mathf.Max(1, PawnSkillsReimaginedMod.Settings.maxCharacterLevel);

        public static float XpToNext(int level)
        {
            return Mathf.Max(XpBase, XpBase * Mathf.Pow(level, XpExponent)) *
                   PawnSkillsReimaginedMod.Settings.xpRequirementMultiplier;
        }

        // Single-entry memo: Learn fires every work tick and consecutive calls
        // are almost always the same pawn, so this skips the dictionary hash on
        // the hot path.
        private Pawn lastPawn;
        private PawnProgress lastProgress;

        public PawnProgress For(Pawn pawn)
        {
            if (pawn == lastPawn && lastProgress != null)
            {
                return lastProgress;
            }
            if (!progress.TryGetValue(pawn, out PawnProgress p))
            {
                p = new PawnProgress();
                progress[pawn] = p;
            }
            lastPawn = pawn;
            lastProgress = p;
            return p;
        }

        public int AvailableFor(Pawn pawn)
        {
            PawnProgress p = For(pawn);
            return Mathf.Max(0, (p.level - 1) * PawnSkillsReimaginedMod.Settings.pointsPerLevel - p.spentPoints);
        }

        // Skill XP funneled from SkillRecord.Learn. Already passion/learn-rate
        // modified, so passionate skills level the pawn faster.
        public void GainXP(Pawn pawn, float amount)
        {
            if (pawn == null || amount <= 0f)
            {
                return;
            }
            PawnProgress p = For(pawn);
            int maxLevel = MaxLevel;
            if (p.level >= maxLevel)
            {
                return;
            }
            p.xp += amount * PawnSkillsReimaginedMod.Settings.xpConversionRate;
            while (p.level < maxLevel && p.xp >= XpToNext(p.level))
            {
                p.xp -= XpToNext(p.level);
                p.level++;
                if (pawn.IsColonist)
                {
                    Messages.Message(pawn.LabelShortCap + " reached level " + p.level + " (+" +
                        PawnSkillsReimaginedMod.Settings.pointsPerLevel + " skill points)",
                        pawn, MessageTypeDefOf.PositiveEvent, historical: false);
                }
            }
            if (p.level >= maxLevel)
            {
                p.xp = 0f;
            }
        }

        // Raise a skill one rank at its passion-based cost. Hard-capped at the
        // configured max skill level.
        public bool TrySpendPoint(Pawn pawn, SkillRecord record)
        {
            if (record == null || record.TotallyDisabled ||
                record.levelInt >= PawnSkillsReimaginedMod.Settings.maxSkillLevel)
            {
                return false;
            }
            int cost = PointCosts.CostFor(record);
            if (AvailableFor(pawn) < cost)
            {
                return false;
            }
            record.levelInt++;
            For(pawn).spentPoints += cost;
            return true;
        }

        // Maximum level for point-bought expertise; their stat effects scale per
        // level with no internal cap, so they stay capped.
        public const int ExpertiseCap = 20;

        public bool TrySpendPoint(Pawn pawn, VSE.ExpertiseRecord record)
        {
            if (record == null || record.Level >= ExpertiseCap)
            {
                return false;
            }
            int cost = PawnSkillsReimaginedMod.Settings.expertisePointCost;
            if (AvailableFor(pawn) < cost)
            {
                return false;
            }
            record.Level++;
            For(pawn).spentPoints += cost;
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref progress, "PSR_progress",
                LookMode.Reference, LookMode.Deep, ref tmpPawns, ref tmpProgress);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (progress == null)
                {
                    progress = new Dictionary<Pawn, PawnProgress>();
                }
                progress.RemoveAll(kvp => kvp.Key == null || kvp.Key.Destroyed || kvp.Value == null);
                lastPawn = null;
                lastProgress = null;
            }
        }
    }
}
