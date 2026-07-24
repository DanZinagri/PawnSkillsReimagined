using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using VSE;
using VSE.Passions;

namespace PawnSkillsReimagined
{
    // Inspect-pane tab (next to Bio/Social/Gear) for viewing a pawn's level and
    // spending skill points. Changes are pending until Apply; Cancel discards.
    // Rank costs follow the skill's passion (icon + cost shown per row).
    public class ITab_SkillPoints : ITab
    {
        private Vector2 scrollPosition;

        // Pending allocations, committed on Apply. Reset when selection changes.
        private readonly Dictionary<SkillRecord, int> pendingSkills = new Dictionary<SkillRecord, int>();
        private readonly Dictionary<ExpertiseRecord, int> pendingExpertise = new Dictionary<ExpertiseRecord, int>();
        private Pawn pendingFor;

        private static readonly Color PendingGreen = new Color(0.5f, 0.9f, 0.5f);
        private static readonly Color Gold = new Color(1f, 0.85f, 0.3f);

        private const float RowHeight = 28f;

        public ITab_SkillPoints()
        {
            size = new Vector2(480f, 560f);
            labelKey = "PSR_TabSkills";
        }

        public override bool IsVisible
        {
            get
            {
                Pawn pawn = SelThing as Pawn;
                return pawn?.skills != null;
            }
        }

        private int PendingCost()
        {
            int total = 0;
            foreach (KeyValuePair<SkillRecord, int> kvp in pendingSkills)
            {
                total += PendingSkillCost(kvp.Key, kvp.Value);
            }
            foreach (KeyValuePair<ExpertiseRecord, int> kvp in pendingExpertise)
            {
                total += kvp.Value * PawnSkillsReimaginedMod.Settings.expertisePointCost;
            }
            return total;
        }

        // Sum the cost of the next `count` ranks from the skill's current level,
        // walking the level up so cost scaling is priced exactly (each rank can
        // cross an interval boundary and cost more than the last).
        private static int PendingSkillCost(SkillRecord record, int count)
        {
            int total = 0;
            int level = Mathf.Max(0, record.levelInt);
            for (int i = 0; i < count; i++)
            {
                total += PointCosts.CostAtLevel(record, level + i);
            }
            return total;
        }

        protected override void FillTab()
        {
            Pawn pawn = SelThing as Pawn;
            var comp = PawnSkillsReimaginedGameComponent.Instance;
            if (pawn?.skills == null || comp == null)
            {
                return;
            }
            if (pendingFor != pawn)
            {
                pendingSkills.Clear();
                pendingExpertise.Clear();
                pendingFor = pawn;
            }

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
            PawnProgress p = comp.For(pawn);
            int maxLevel = PawnSkillsReimaginedGameComponent.MaxLevel;
            int maxSkill = PawnSkillsReimaginedMod.Settings.maxSkillLevel;
            int available = comp.AvailableFor(pawn) - PendingCost();
            bool canSpend = (pawn.Faction == Faction.OfPlayerSilentFail || pawn.IsPrisonerOfColony) && !pawn.Dead;

            // Header: level + XP bar + points
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 30f),
                pawn.LabelShortCap + " — Level " + p.level + (p.level >= maxLevel ? " (MAX)" : ""));
            Text.Font = GameFont.Small;

            Rect xpBar = new Rect(rect.x, rect.y + 32f, rect.width, 14f);
            if (p.level >= maxLevel)
            {
                Widgets.FillableBar(xpBar, 1f);
            }
            else
            {
                float required = PawnSkillsReimaginedGameComponent.XpToNext(p.level);
                Widgets.FillableBar(xpBar, Mathf.Clamp01(p.xp / required));
                TooltipHandler.TipRegion(xpBar, p.xp.ToString("F0") + " / " + required.ToString("F0") + " XP to next level");
            }

            GUI.color = available > 0 ? Gold : Color.gray;
            Widgets.Label(new Rect(rect.x, rect.y + 50f, rect.width, 22f),
                "Skill points: " + available);
            GUI.color = Color.white;

            // Skill + expertise list
            List<SkillRecord> skills = pawn.skills.skills;
            List<ExpertiseRecord> expertise = pawn.Expertise()?.AllExpertise;
            int expertiseRows = expertise != null && expertise.Count > 0 ? expertise.Count + 1 : 0;

            float bottomButtons = 40f;
            Rect outRect = new Rect(rect.x, rect.y + 76f, rect.width, rect.height - 76f - bottomButtons - 24f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, (skills.Count + expertiseRows) * RowHeight);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            float y = 0f;
            foreach (SkillRecord record in skills)
            {
                DrawSkillRow(new Rect(0f, y, viewRect.width, RowHeight), record, canSpend, ref available, maxSkill);
                y += RowHeight;
            }

            if (expertiseRows > 0)
            {
                GUI.color = Gold;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(4f, y, viewRect.width, RowHeight),
                    "Expertise (max " + PawnSkillsReimaginedGameComponent.ExpertiseCap +
                    ", " + PawnSkillsReimaginedMod.Settings.expertisePointCost + " pts each)");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                y += RowHeight;
                foreach (ExpertiseRecord record in expertise)
                {
                    DrawExpertiseRow(new Rect(0f, y, viewRect.width, RowHeight), record, canSpend, ref available);
                    y += RowHeight;
                }
            }

            Widgets.EndScrollView();

            // Apply / Cancel
            bool anyPending = pendingSkills.Count > 0 || pendingExpertise.Count > 0;
            float btnY = rect.yMax - bottomButtons - 18f;
            if (anyPending)
            {
                if (Widgets.ButtonText(new Rect(rect.x, btnY, 130f, 32f), "Apply"))
                {
                    ApplyPending(pawn, comp);
                    SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
                }
                if (Widgets.ButtonText(new Rect(rect.x + 140f, btnY, 130f, 32f), "Cancel"))
                {
                    pendingSkills.Clear();
                    pendingExpertise.Clear();
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
            }

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x, rect.yMax - 20f, rect.width, 20f),
                "Rank costs follow passions. Changes are pending until applied.");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawSkillRow(Rect row, SkillRecord record, bool canSpend, ref int available, int maxSkill)
        {
            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }
            bool disabled = record.TotallyDisabled;
            pendingSkills.TryGetValue(record, out int pending);
            int level = SkillLevelUtility.EffectiveLevel(record);
            int shownLevel = level + pending;
            PassionDef passion = PointCosts.PassionOf(record);

            // Passion icon
            if (passion?.Icon != null)
            {
                GUI.color = disabled ? new Color(1f, 1f, 1f, 0.3f) : Color.white;
                Widgets.DrawTextureFitted(new Rect(4f, row.y + 3f, 22f, 22f), passion.Icon, 1f);
            }

            GUI.color = disabled ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(32f, row.y, 128f, row.height), record.def.skillLabel.CapitalizeFirst());

            GUI.color = pending > 0 ? PendingGreen : (disabled ? new Color(1f, 1f, 1f, 0.4f) : Color.white);
            Widgets.Label(new Rect(164f, row.y, 76f, row.height),
                disabled ? "-" : (pending > 0 ? level + " → " + shownLevel : level.ToString()));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            if (!canSpend || disabled)
            {
                return;
            }

            // Minus reduces the pending queue; plus queues a rank at this cost.
            float btnY = row.y + 2f;
            if (pending > 0 && Widgets.ButtonText(new Rect(row.width - 96f, btnY, 24f, 24f), "-"))
            {
                pendingSkills[record] = pending - 1;
                if (pendingSkills[record] <= 0)
                {
                    pendingSkills.Remove(record);
                }
            }
            // Cost of the next rank to queue, priced at the level after any
            // already-pending ranks (scaling can make it climb).
            int nextCost = PointCosts.CostAtLevel(record, Mathf.Max(0, record.levelInt) + pending);
            bool canQueue = available >= nextCost && shownLevel < maxSkill;
            Rect plusRect = new Rect(row.width - 68f, btnY, 64f, 24f);
            TooltipHandler.TipRegion(plusRect,
                "Queue +1 rank for " + nextCost + " points (" + (passion?.label ?? "no passion") +
                ").\nShift-click: +5 ranks.");
            if (Widgets.ButtonText(plusRect, "+ (" + nextCost + ")", active: canQueue) && canQueue)
            {
                int toQueue = Event.current.shift ? 5 : 1;
                for (int i = 0; i < toQueue; i++)
                {
                    pendingSkills.TryGetValue(record, out int cur);
                    int stepCost = PointCosts.CostAtLevel(record, Mathf.Max(0, record.levelInt) + cur);
                    if (available < stepCost || level + cur >= maxSkill)
                    {
                        break;
                    }
                    pendingSkills[record] = cur + 1;
                    available -= stepCost;
                }
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }

        private void DrawExpertiseRow(Rect row, ExpertiseRecord record, bool canSpend, ref int available)
        {
            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }
            pendingExpertise.TryGetValue(record, out int pending);
            int shownLevel = record.Level + pending;
            bool maxed = shownLevel >= PawnSkillsReimaginedGameComponent.ExpertiseCap;
            int cost = PawnSkillsReimaginedMod.Settings.expertisePointCost;

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(4f, row.y, 156f, row.height), record.def.LabelCap);
            GUI.color = pending > 0 ? PendingGreen : (maxed ? Gold : Color.white);
            Widgets.Label(new Rect(164f, row.y, 80f, row.height),
                shownLevel + " / " + PawnSkillsReimaginedGameComponent.ExpertiseCap);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            if (!canSpend)
            {
                return;
            }
            float btnY = row.y + 2f;
            if (pending > 0 && Widgets.ButtonText(new Rect(row.width - 96f, btnY, 24f, 24f), "-"))
            {
                pendingExpertise[record] = pending - 1;
                if (pendingExpertise[record] <= 0)
                {
                    pendingExpertise.Remove(record);
                }
            }
            bool canQueue = available >= cost && !maxed;
            if (Widgets.ButtonText(new Rect(row.width - 68f, btnY, 64f, 24f), "+ (" + cost + ")", active: canQueue) && canQueue)
            {
                pendingExpertise[record] = pending + 1;
                available -= cost;
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }

        private void ApplyPending(Pawn pawn, PawnSkillsReimaginedGameComponent comp)
        {
            foreach (KeyValuePair<SkillRecord, int> kvp in pendingSkills)
            {
                for (int i = 0; i < kvp.Value; i++)
                {
                    if (!comp.TrySpendPoint(pawn, kvp.Key))
                    {
                        break;
                    }
                }
            }
            foreach (KeyValuePair<ExpertiseRecord, int> kvp in pendingExpertise)
            {
                for (int i = 0; i < kvp.Value; i++)
                {
                    if (!comp.TrySpendPoint(pawn, kvp.Key))
                    {
                        break;
                    }
                }
            }
            pendingSkills.Clear();
            pendingExpertise.Clear();
        }
    }
}
