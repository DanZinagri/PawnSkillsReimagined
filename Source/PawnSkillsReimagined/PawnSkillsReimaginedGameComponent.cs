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
        private const float XpBase = 100f;
        private const float XpExponent = 1.5f;

        private Dictionary<Pawn, PawnProgress> progress = new Dictionary<Pawn, PawnProgress>();

        private List<string> tmpPawnIds;
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
            p.xp += amount;
            while (p.level < maxLevel && p.xp >= XpToNext(p.level))
            {
                p.xp -= XpToNext(p.level);
                p.level++;
            }
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
        // ranks. Used for generated world pawns.
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

            // Pawn keys are scribed as their unique load-ID strings under a
            // "PSR_progress" node, laid out exactly like Scribe_Collections.Look's
            // dictionary format (parallel "keys"/"values" lists), so old saves
            // load unchanged. Storing the keys as plain strings rather than
            // references means loading a key for a pawn that no longer exists
            // never invokes the cross-ref resolver and so never logs its "Could
            // not resolve reference" warning; we match the IDs back to pawns
            // ourselves in PostLoadInit and silently drop any that are gone.
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                tmpPawnIds = new List<string>();
                tmpProgress = new List<PawnProgress>();
                foreach (KeyValuePair<Pawn, PawnProgress> kvp in progress)
                {
                    if (kvp.Key == null || kvp.Key.Destroyed || kvp.Value == null)
                    {
                        continue;
                    }
                    tmpPawnIds.Add(kvp.Key.GetUniqueLoadID());
                    tmpProgress.Add(kvp.Value);
                }
            }

            if (Scribe.EnterNode("PSR_progress"))
            {
                try
                {
                    Scribe_Collections.Look(ref tmpPawnIds, "keys", LookMode.Value);
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
                if (tmpPawnIds != null && tmpProgress != null)
                {
                    // One pass over every loaded pawn builds the ID lookup; entries
                    // whose pawn isn't present (died and cleaned out, compressed
                    // away, left with a mod removed) simply find no match.
                    Dictionary<string, Pawn> byId = new Dictionary<string, Pawn>();
                    foreach (Pawn p in PawnsFinder.All_AliveOrDead)
                    {
                        if (p != null)
                        {
                            byId[p.GetUniqueLoadID()] = p;
                        }
                    }
                    int count = Mathf.Min(tmpPawnIds.Count, tmpProgress.Count);
                    for (int i = 0; i < count; i++)
                    {
                        PawnProgress prog = tmpProgress[i];
                        if (prog != null && tmpPawnIds[i] != null &&
                            byId.TryGetValue(tmpPawnIds[i], out Pawn pawn) &&
                            pawn != null && !pawn.Destroyed)
                        {
                            progress[pawn] = prog;
                        }
                    }
                }
                tmpPawnIds = null;
                tmpProgress = null;
                lastPawn = null;
                lastProgress = null;
            }
        }
    }
}
