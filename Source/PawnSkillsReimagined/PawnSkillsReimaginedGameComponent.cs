using System.Collections.Generic;
using System.Linq;
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
        // Expertise points are a granted balance (not derived from level like skill
        // points): +1 each time a level-up crosses an expertisePointInterval
        // breakpoint, spent 1-per-expertise-level, and player-adjustable via dev
        // tools. Not retroactively rebuilt, so old saves start at 0 and accrue from
        // their next breakpoint.
        public int expertisePoints;

        public void ExposeData()
        {
            Scribe_Values.Look(ref level, "level", 1);
            Scribe_Values.Look(ref xp, "xp", 0f);
            Scribe_Values.Look(ref spentPoints, "spentPoints", 0);
            Scribe_Values.Look(ref expertisePoints, "expertisePoints", 0);
        }
    }

    // The leveling system: skill XP earned by pawns is funneled here (the skill
    // itself gains nothing), levels follow an Isekai-style power curve, and each
    // level grants skill points to buy uncapped skill ranks. Character levels also
    // feed the VSE expertise system: breakpoints grant expertise points (spent to
    // raise expertise, capped at 20) and raise how many expertise a pawn may hold.
    public class PawnSkillsReimaginedGameComponent : GameComponent
    {
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

        // Side-effect-free read: returns the pawn's progress or null without
        // creating an entry. Use for queries (API, display) so polling doesn't
        // bloat the dictionary with entries for pawns that never earned XP.
        public PawnProgress GetProgressOrNull(Pawn pawn)
        {
            return pawn != null && progress.TryGetValue(pawn, out PawnProgress p) ? p : null;
        }

        public int AvailableFor(Pawn pawn)
        {
            PawnProgress p = GetProgressOrNull(pawn);
            if (p == null)
            {
                return 0;
            }
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
            int before = p.level;
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
            GrantExpertisePoints(p, before);
            if (p.level >= maxLevel)
            {
                p.xp = 0f;
            }
        }

        // Silent XP grant used at pawn generation: levels up without posting
        // level-up messages (a freshly generated pawn "arriving" at level 12
        // shouldn't fire 11 notifications).
        public void GrantStartingXP(Pawn pawn, float amount)
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
            int before = p.level;
            p.xp += amount;
            while (p.level < maxLevel && p.xp >= XpToNext(p.level))
            {
                p.xp -= XpToNext(p.level);
                p.level++;
            }
            GrantExpertisePoints(p, before);
            if (p.level >= maxLevel)
            {
                p.xp = 0f;
            }
        }

        // Grant (or remove, if negative) whole levels directly. Silent, clamped
        // to [1, MaxLevel]. Public API entry point for other mods.
        public void AddLevels(Pawn pawn, int count)
        {
            if (pawn == null || count == 0)
            {
                return;
            }
            PawnProgress p = For(pawn);
            p.level = Mathf.Clamp(p.level + count, 1, MaxLevel);
            if (p.level >= MaxLevel)
            {
                p.xp = 0f;
            }
        }

        // Set the pawn's level exactly, clamped to [1, MaxLevel]. Silent. Public
        // API entry point for other mods.
        public void SetLevel(Pawn pawn, int level)
        {
            if (pawn == null)
            {
                return;
            }
            PawnProgress p = For(pawn);
            p.level = Mathf.Clamp(level, 1, MaxLevel);
            if (p.level >= MaxLevel)
            {
                p.xp = 0f;
            }
        }

        // Grant (delta > 0) or revoke (delta < 0) spendable skill points without
        // touching the level. Points are derived as (level-1)*pointsPerLevel -
        // spentPoints, so we move spentPoints the opposite way.
        public void AddSkillPoints(Pawn pawn, int delta)
        {
            if (pawn == null || delta == 0)
            {
                return;
            }
            For(pawn).spentPoints -= delta;
        }

        // Change the level by count while keeping available points unchanged: the
        // level moves but the points those levels would grant are offset out, so
        // this raises/lowers the level "for free". Clamped to [1, MaxLevel].
        public void AddLevelsNoPoints(Pawn pawn, int count)
        {
            if (pawn == null || count == 0)
            {
                return;
            }
            PawnProgress p = For(pawn);
            int before = p.level;
            p.level = Mathf.Clamp(p.level + count, 1, MaxLevel);
            int applied = p.level - before;
            p.spentPoints += applied * PawnSkillsReimaginedMod.Settings.pointsPerLevel;
            if (p.level >= MaxLevel)
            {
                p.xp = 0f;
            }
        }

        // Spend all affordable points randomly across usable skills, weighted
        // toward existing (backstory) ranks so builds follow the pawn's story.
        // Passion costs apply, so cheap passionate skills naturally soak up more
        // ranks. NPCs pay the same cost the player would, so a recruited pawn's
        // point/skill numbers stay consistent. Used for generated world pawns.
        public void AutoSpendPoints(Pawn pawn)
        {
            if (pawn?.skills?.skills == null)
            {
                return;
            }
            int maxSkill = PawnSkillsReimaginedMod.Settings.maxSkillLevel;
            List<SkillRecord> skills = pawn.skills.skills;
            while (true)
            {
                int available = AvailableFor(pawn);
                if (available <= 0)
                {
                    return;
                }
                if (!skills.Where(s => !s.TotallyDisabled && s.levelInt < maxSkill &&
                        PointCosts.CostFor(s) <= available)
                        .TryRandomElementByWeight(s => 1f + s.levelInt, out SkillRecord pick) ||
                    !TrySpendPoint(pawn, pick))
                {
                    return;
                }
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
            // Skills raised via points bypass SkillRecord.Learn, so mods watching
            // it for skill-increase rewards (Character Development) miss the event
            // - re-emit it. No-ops when that mod isn't loaded.
            CharacterDevelopmentCompat.NotifySkillIncreased(pawn, record.def, record.levelInt);
            return true;
        }

        // Maximum level for point-bought expertise; their stat effects scale per
        // level with no internal cap, so they stay capped.
        public const int ExpertiseCap = 20;

        // Raise one expertise level for one expertise point. Capped at ExpertiseCap.
        public bool TrySpendPoint(Pawn pawn, VSE.ExpertiseRecord record)
        {
            if (record == null || record.Level >= ExpertiseCap)
            {
                return false;
            }
            PawnProgress p = For(pawn);
            if (p.expertisePoints < 1)
            {
                return false;
            }
            record.Level++;
            p.expertisePoints -= 1;
            return true;
        }

        // Grant expertise points for every expertisePointInterval breakpoint the
        // level crossed since oldLevel. Called after a level-up loop settles.
        private static void GrantExpertisePoints(PawnProgress p, int oldLevel)
        {
            if (p.level <= oldLevel)
            {
                return;
            }
            int interval = Mathf.Max(1, PawnSkillsReimaginedMod.Settings.expertisePointInterval);
            p.expertisePoints += p.level / interval - oldLevel / interval;
        }

        // Unspent expertise points available to raise expertise levels.
        public int AvailableExpertisePoints(Pawn pawn)
        {
            return GetProgressOrNull(pawn)?.expertisePoints ?? 0;
        }

        // Grant (delta > 0) or revoke (delta < 0) expertise points directly, floored
        // at zero. Dev-tool / API entry point.
        public void AddExpertisePoints(Pawn pawn, int delta)
        {
            if (pawn == null || delta == 0)
            {
                return;
            }
            PawnProgress p = For(pawn);
            p.expertisePoints = Mathf.Max(0, p.expertisePoints + delta);
        }

        // How many expertise a pawn may hold: a base of 1 plus one per
        // expertiseSlotInterval character levels. Computed live from the pawn's
        // level, so it is inherently per-pawn and always current with no stored
        // state or level-up bookkeeping. Consumed by the VSE CanApplyOn override.
        public int MaxExpertiseFor(Pawn pawn)
        {
            int level = GetProgressOrNull(pawn)?.level ?? 1;
            int interval = Mathf.Max(1, PawnSkillsReimaginedMod.Settings.expertiseSlotInterval);
            return 1 + level / interval;
        }

        public override void ExposeData()
        {
            base.ExposeData();

            // Scribed as two parallel lists ("keys" pawn references + "values"
            // deep progress) under a "PSR_progress" node -- the exact layout
            // Scribe_Collections.Look uses for a dictionary, so old saves load
            // unchanged. We zip them back together ourselves in PostLoadInit
            // rather than letting the dictionary builder do it, so a reference
            // that fails to resolve is skipped quietly instead of logging a red
            // "Null key" error. And we prune stale pawns at save time (below) so
            // an unresolvable reference is never written -- that is what stops the
            // yellow "Could not resolve reference" warning at its source. Keys use
            // LookMode.Reference (not raw load-IDs) because references resolve
            // through the cross-ref directory during load; a manual load-ID scan
            // would run in PostLoadInit before maps have spawned their pawns and
            // find nothing, wiping every pawn's level.
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                progress.RemoveAll(kvp => kvp.Key == null || kvp.Key.Destroyed ||
                                          kvp.Key.Discarded || kvp.Value == null);
                tmpPawns = progress.Keys.ToList();
                tmpProgress = progress.Values.ToList();
            }

            if (Scribe.EnterNode("PSR_progress"))
            {
                try
                {
                    Scribe_Collections.Look(ref tmpPawns, "keys", LookMode.Reference);
                    Scribe_Collections.Look(ref tmpProgress, "values", LookMode.Deep);
                }
                finally
                {
                    Scribe.ExitNode();
                }
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                progress = new Dictionary<Pawn, PawnProgress>();
                if (tmpPawns != null && tmpProgress != null)
                {
                    int count = Mathf.Min(tmpPawns.Count, tmpProgress.Count);
                    for (int i = 0; i < count; i++)
                    {
                        Pawn pawn = tmpPawns[i];
                        PawnProgress prog = tmpProgress[i];
                        // Skip references that failed to resolve (pawn gone).
                        if (pawn != null && !pawn.Destroyed && prog != null)
                        {
                            progress[pawn] = prog;
                        }
                    }
                }
                tmpPawns = null;
                tmpProgress = null;
                lastPawn = null;
                lastProgress = null;
            }
        }
    }
}
