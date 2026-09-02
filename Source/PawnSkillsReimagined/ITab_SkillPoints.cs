using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using VSE;
using VSE.Expertise;
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

        // Colours come from the active UI style (vanilla or a restyle provider).
        private static Color PendingGreen => UIStyle.Positive;
        private static Color Gold => UIStyle.Accent;

        private const float RowHeight = 28f;
        private const float BaseWidth = 480f;

        // When true, the tab widens and draws our acquire-expertise panel in the
        // extra region on the right (a drawer for picking up new expertise).
        private bool expertiseExpanded;
        private Vector2 acquireScroll;

        public ITab_SkillPoints()
        {
            size = new Vector2(BaseWidth, 560f);
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

        // Pending skill-rank cost, in skill points.
        private int PendingSkillTotal()
        {
            int total = 0;
            foreach (KeyValuePair<SkillRecord, int> kvp in pendingSkills)
            {
                total += PendingSkillCost(kvp.Key, kvp.Value);
            }
            return total;
        }

        // Pending expertise levels, in expertise points (1 point per level).
        private int PendingExpertiseTotal()
        {
            int total = 0;
            foreach (KeyValuePair<ExpertiseRecord, int> kvp in pendingExpertise)
            {
                total += kvp.Value;
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

            // Widen the tab when the expertise drawer is open; the main content
            // stays at BaseWidth and our acquire panel fills the extra region.
            float panelW = expertiseExpanded ? 360f : 0f;
            size.x = BaseWidth + panelW;

            Rect rect = new Rect(0f, 0f, BaseWidth, size.y).ContractedBy(12f);
            PawnProgress p = comp.For(pawn);
            int maxLevel = PawnSkillsReimaginedGameComponent.MaxLevel;
            int maxSkill = PawnSkillsReimaginedMod.Settings.maxSkillLevel;
            int available = comp.AvailableFor(pawn) - PendingSkillTotal();
            int availableExpertise = comp.AvailableExpertisePoints(pawn) - PendingExpertiseTotal();
            bool canSpend = (pawn.Faction == Faction.OfPlayerSilentFail || pawn.IsPrisonerOfColony) && !pawn.Dead;
            IUIStyle style = UIStyle.Current;

            // Header: level + XP bar + points
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 118f, 30f),
                pawn.LabelShortCap + " — Level " + p.level + (p.level >= maxLevel ? " (MAX)" : ""));
            Text.Font = GameFont.Small;

            // Expertise drawer toggle (top-right of the main content).
            Rect toggleRect = new Rect(rect.xMax - 112f, rect.y + 2f, 112f, 26f);
            if (UIStyle.Button(toggleRect, expertiseExpanded ? "« Expertise" : "Expertise »"))
            {
                expertiseExpanded = !expertiseExpanded;
            }
            if (expertiseExpanded)
            {
                Color prevDiv = GUI.color;
                GUI.color = UIStyle.Divider;
                Widgets.DrawLineVertical(BaseWidth, 8f, size.y - 16f);
                GUI.color = prevDiv;
                DrawAcquirePanel(new Rect(BaseWidth, 0f, panelW, size.y).ContractedBy(12f), pawn, canSpend);
            }

            Rect xpBar = new Rect(rect.x, rect.y + 32f, rect.width, 14f);
            if (p.level >= maxLevel)
            {
                UIStyle.Bar(xpBar, 1f);
            }
            else
            {
                float required = PawnSkillsReimaginedGameComponent.XpToNext(p.level);
                UIStyle.Bar(xpBar, p.xp / required);
                if (Mouse.IsOver(xpBar))
                {
                    TooltipHandler.TipRegion(xpBar,
                        p.xp.ToString("F0") + " / " + required.ToString("F0") + " XP to next level");
                }
            }

            GUI.color = available > 0 ? Gold : Color.gray;
            Widgets.Label(new Rect(rect.x, rect.y + 50f, rect.width * 0.5f, 22f),
                "Skill points: " + available);
            GUI.color = availableExpertise > 0 ? Gold : Color.gray;
            Widgets.Label(new Rect(rect.x + rect.width * 0.5f, rect.y + 50f, rect.width * 0.5f, 22f),
                "Expertise points: " + availableExpertise);
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
                DrawSkillRow(new Rect(0f, y, viewRect.width, RowHeight), record, canSpend, ref available, maxSkill,
                    style);
                y += RowHeight;
            }

            if (expertiseRows > 0)
            {
                GUI.color = Gold;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(4f, y, viewRect.width, RowHeight),
                    "Expertise (max " + PawnSkillsReimaginedGameComponent.ExpertiseCap + ", 1 pt each)");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                y += RowHeight;
                foreach (ExpertiseRecord record in expertise)
                {
                    DrawExpertiseRow(new Rect(0f, y, viewRect.width, RowHeight), record, canSpend, ref availableExpertise,
                        style);
                    y += RowHeight;
                }
            }

            Widgets.EndScrollView();

            // Apply / Cancel
            bool anyPending = pendingSkills.Count > 0 || pendingExpertise.Count > 0;
            float btnY = rect.yMax - bottomButtons - 18f;
            if (anyPending)
            {
                if (UIStyle.Button(new Rect(rect.x, btnY, 130f, 32f), "Apply"))
                {
                    ApplyPending(pawn, comp);
                    SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
                }
                if (UIStyle.Button(new Rect(rect.x + 140f, btnY, 130f, 32f), "Cancel"))
                {
                    pendingSkills.Clear();
                    pendingExpertise.Clear();
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
            }

            // Respec: refunds every point-bought rank / expertise level using one
            // banked respec. Confirmation-gated since it rewrites the pawn's build.
            if (canSpend)
            {
                int respecs = comp.RespecPointsFor(pawn);
                Rect respecRect = new Rect(rect.xMax - 130f, btnY, 130f, 32f);
                if (Mouse.IsOver(respecRect))
                {
                    TooltipHandler.TipRegion(respecRect,
                        "Refund all point-bought skill ranks and expertise levels so the points can be re-spent. " +
                        "Uses 1 banked respec. New colonists start with 1; +1 every " +
                        PawnSkillsReimaginedMod.Settings.respecLevelInterval + " levels earned in play.");
                }
                if (style.Button(respecRect, RespecLabel(respecs), respecs > 0, null) && respecs > 0)
                {
                    Pawn target = pawn;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "Respec " + target.LabelShortCap +
                        "? All point-bought skill ranks and expertise levels are refunded for re-spending. " +
                        "This uses 1 banked respec.",
                        () =>
                        {
                            if (comp.TryRespec(target))
                            {
                                pendingSkills.Clear();
                                pendingExpertise.Clear();
                                acquireFor = null;
                                SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
                            }
                        },
                        destructive: true));
                }
            }

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x, rect.yMax - 20f, rect.width, 20f),
                "Rank costs follow passions. Changes are pending until applied.");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawSkillRow(Rect row, SkillRecord record, bool canSpend, ref int available, int maxSkill,
            IUIStyle style)
        {
            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }
            bool disabled = record.TotallyDisabled;
            pendingSkills.TryGetValue(record, out int pending);
            // Base = bought/backstory ranks (what costs scale on). Aptitude =
            // gene/trait offset, shown separately so the cost basis is clear.
            int baseLevel = Mathf.Max(0, record.levelInt);
            int aptitude = record.Aptitude;
            int shownBase = baseLevel + pending;
            PassionDef passion = PointCosts.PassionOf(record);

            // Passion icon
            if (passion?.Icon != null)
            {
                GUI.color = disabled ? new Color(1f, 1f, 1f, 0.3f) : Color.white;
                Widgets.DrawTextureFitted(new Rect(4f, row.y + 3f, 22f, 22f), passion.Icon, 1f);
            }

            GUI.color = disabled ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(32f, row.y, 128f, row.height), SkillLabel(record.def));

            Rect levelRect = new Rect(164f, row.y, 110f, row.height);
            if (disabled)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Widgets.Label(levelRect, "-");
            }
            else
            {
                GUI.color = Color.white;
                if (pending == 0 && aptitude == 0)
                {
                    // Common case: no markup needed, so skip the colour-hex and
                    // string building this would otherwise redo every frame.
                    Widgets.Label(levelRect, baseLevel.ToString());
                }
                else
                {
                    // Rich text so the aptitude offset stays visually distinct from
                    // the (green) pending change and never reads as a bought rank.
                    string basePart = pending > 0 ? baseLevel + " → " + shownBase : baseLevel.ToString();
                    string text = pending > 0
                        ? "<color=#" + ColorUtility.ToHtmlStringRGB(PendingGreen) + ">" + basePart + "</color>"
                        : basePart;
                    if (aptitude != 0)
                    {
                        Color aptColor = aptitude > 0 ? new Color(0.55f, 0.8f, 1f) : new Color(1f, 0.55f, 0.55f);
                        text += " <color=#" + ColorUtility.ToHtmlStringRGB(aptColor) + ">(" +
                                aptitude.ToStringWithSign() + ")</color>";
                    }
                    Widgets.Label(levelRect, text);
                }
            }
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            if (!canSpend || disabled)
            {
                return;
            }

            // Minus reduces the pending queue; plus queues a rank at this cost.
            float btnY = row.y + 2f;
            Rect minusRect = new Rect(row.width - 96f, btnY, 24f, 24f);
            if (pending > 0 && Mouse.IsOver(minusRect))
            {
                TooltipHandler.TipRegion(minusRect, "Un-queue 1 rank.\nShift-click: up to 5.");
            }
            if (pending > 0 && style.Button(minusRect, "-", true, null))
            {
                int toRemove = Event.current.shift ? Mathf.Min(5, pending) : 1;
                pendingSkills[record] = pending - toRemove;
                if (pendingSkills[record] <= 0)
                {
                    pendingSkills.Remove(record);
                }
            }
            // Cost of the next rank to queue, priced at the level after any
            // already-pending ranks (scaling can make it climb).
            int nextCost = PointCosts.CostAtLevel(record, Mathf.Max(0, record.levelInt) + pending);
            bool canQueue = available >= nextCost && shownBase < maxSkill;
            Rect plusRect = new Rect(row.width - 68f, btnY, 64f, 24f);
            // Built only while hovered - this concatenation was running for
            // every row every frame.
            if (Mouse.IsOver(plusRect))
            {
                TooltipHandler.TipRegion(plusRect,
                    "Queue +1 rank for " + nextCost + " points (" + (passion?.label ?? "no passion") +
                    ").\nShift-click: +5 ranks.");
            }
            if (style.Button(plusRect, PlusLabel(nextCost), canQueue, null) && canQueue)
            {
                int toQueue = Event.current.shift ? 5 : 1;
                for (int i = 0; i < toQueue; i++)
                {
                    pendingSkills.TryGetValue(record, out int cur);
                    int stepCost = PointCosts.CostAtLevel(record, Mathf.Max(0, record.levelInt) + cur);
                    if (available < stepCost || baseLevel + cur >= maxSkill)
                    {
                        break;
                    }
                    pendingSkills[record] = cur + 1;
                    available -= stepCost;
                }
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }

        private void DrawExpertiseRow(Rect row, ExpertiseRecord record, bool canSpend, ref int availableExpertise,
            IUIStyle style)
        {
            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }
            pendingExpertise.TryGetValue(record, out int pending);
            int shownLevel = record.Level + pending;
            bool maxed = shownLevel >= PawnSkillsReimaginedGameComponent.ExpertiseCap;

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
            if (pending > 0 && style.Button(new Rect(row.width - 96f, btnY, 24f, 24f), "-", true, null))
            {
                pendingExpertise[record] = pending - 1;
                if (pendingExpertise[record] <= 0)
                {
                    pendingExpertise.Remove(record);
                }
            }
            // One expertise point raises one expertise level.
            bool canQueue = availableExpertise >= 1 && !maxed;
            if (style.Button(new Rect(row.width - 68f, btnY, 64f, 24f), "+ (1)", canQueue, null) && canQueue)
            {
                pendingExpertise[record] = pending + 1;
                availableExpertise -= 1;
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
            acquireFor = null; // spent ranks can change expertise eligibility
        }

        // Our own "acquire new expertise" drawer. Redrawn rather than reusing VSE's
        // DoExpertisePanel (which bakes in a redundant close button), but it uses
        // VSE's data + gating so our slot-cap / overlap / acquire-level overrides
        // apply. Acquiring is free (slot-limited); leveling is done with expertise
        // points in the main panel.
        private struct AcquireRow
        {
            public ExpertiseDef def;
            public bool can;
            public string tip;
            public string effects;
            public float height;
        }

        // Cached row model for the acquire drawer. Building it walks every expertise
        // def and calls VSE's CanApplyOn plus Effects() (a LINQ string build) and
        // Text.CalcHeight - far too expensive to redo every frame. It is rebuilt
        // only when the pawn, its expertise count or the panel width changes, plus a
        // periodic refresh so skill/level drift is picked up while the tab is open.
        private readonly List<AcquireRow> acquireRows = new List<AcquireRow>();
        private Pawn acquireFor;
        private int acquireOwned = -1;
        private int acquireFrame = -1;
        private float acquireWidth = -1f;
        private float acquireTotal;
        private const int AcquireRefreshFrames = 30;

        // Effects text depends only on the def, so it is built once and shared
        // across pawns rather than rebuilt per row per frame. It does depend on
        // VSE's stat multiplier, so a settings write clears it (see
        // InvalidateCaches) and bumps the generation to force a row rebuild.
        private static readonly Dictionary<ExpertiseDef, string> EffectsText =
            new Dictionary<ExpertiseDef, string>();
        private static int cacheGeneration;
        private int builtGeneration = -1;

        // Small string caches: these were rebuilt for every row every frame. Both
        // are keyed by values that fully determine the text, so they never go stale
        // (a changed cost simply lands on a different key).
        private static readonly Dictionary<SkillDef, string> SkillLabels =
            new Dictionary<SkillDef, string>();
        private static readonly Dictionary<int, string> PlusLabels = new Dictionary<int, string>();

        private static string SkillLabel(SkillDef def)
        {
            if (!SkillLabels.TryGetValue(def, out string label))
            {
                label = def.skillLabel.CapitalizeFirst();
                SkillLabels[def] = label;
            }
            return label;
        }

        private static string PlusLabel(int cost)
        {
            if (!PlusLabels.TryGetValue(cost, out string label))
            {
                label = "+ (" + cost + ")";
                PlusLabels[cost] = label;
            }
            return label;
        }

        private static readonly Dictionary<int, string> RespecLabels = new Dictionary<int, string>();

        private static string RespecLabel(int count)
        {
            if (!RespecLabels.TryGetValue(count, out string label))
            {
                label = "Respec (" + count + ")";
                RespecLabels[count] = label;
            }
            return label;
        }

        // Called when any mod's settings window closes.
        public static void InvalidateCaches()
        {
            EffectsText.Clear();
            cacheGeneration++;
        }

        private void RebuildAcquireRows(Pawn pawn, ExpertiseTracker tracker, float bodyW)
        {
            acquireRows.Clear();
            var owned = new HashSet<ExpertiseDef>();
            foreach (ExpertiseRecord er in tracker.AllExpertise)
            {
                owned.Add(er.def);
            }

            Text.Font = GameFont.Tiny;
            foreach (ExpertiseDef def in DefDatabase<ExpertiseDef>.AllDefs)
            {
                if (def.hide || owned.Contains(def))
                {
                    continue;
                }
                if (!EffectsText.TryGetValue(def, out string effects))
                {
                    effects = def.Effects(1, "  - ").TrimStart('\n');
                    EffectsText[def] = effects;
                }
                bool can = def.CanApplyOn(pawn, out string reason);
                acquireRows.Add(new AcquireRow
                {
                    def = def,
                    can = can,
                    tip = can ? def.description : reason + "\n\n" + def.description,
                    effects = effects,
                    height = 28f + (effects.NullOrEmpty() ? 0f : Text.CalcHeight(effects, bodyW)) + 10f,
                });
            }
            Text.Font = GameFont.Small;

            // Eligible first, then by the pawn's level in that skill. Sorts on the
            // values captured above, so CanApplyOn isn't re-run per comparison.
            acquireRows.Sort((a, b) =>
            {
                if (a.can != b.can)
                {
                    return b.can.CompareTo(a.can);
                }
                return pawn.skills.GetSkill(b.def.skill).levelInt
                    .CompareTo(pawn.skills.GetSkill(a.def.skill).levelInt);
            });

            acquireTotal = 0f;
            for (int i = 0; i < acquireRows.Count; i++)
            {
                acquireTotal += acquireRows[i].height + 4f;
            }

            acquireFor = pawn;
            acquireOwned = tracker.AllExpertise.Count;
            acquireWidth = bodyW;
            acquireFrame = Time.frameCount;
            builtGeneration = cacheGeneration;
        }

        private void DrawAcquirePanel(Rect inRect, Pawn pawn, bool canSpend)
        {
            ExpertiseTracker tracker = pawn.Expertise();
            if (tracker == null || pawn.skills == null)
            {
                return;
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Gold;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 24f), "Acquire expertise");
            GUI.color = Color.white;
            inRect.yMin += 28f;

            const float btnW = 132f;
            float bodyW = inRect.width - 30f;
            if (acquireFor != pawn || acquireOwned != tracker.AllExpertise.Count ||
                !Mathf.Approximately(acquireWidth, bodyW) ||
                builtGeneration != cacheGeneration ||
                Time.frameCount - acquireFrame > AcquireRefreshFrames)
            {
                RebuildAcquireRows(pawn, tracker, bodyW);
            }

            IUIStyle style = UIStyle.Current;
            string selectLabel = "VSE.SelectExpertise".Translate();
            Rect view = new Rect(0f, 0f, inRect.width - 18f, Mathf.Max(acquireTotal, inRect.height));
            Widgets.BeginScrollView(inRect, ref acquireScroll, view);

            // Only rows inside the visible scroll window are drawn; the list is far
            // taller than the panel, so this skips most of the per-row work.
            float top = acquireScroll.y - 8f;
            float bottom = acquireScroll.y + inRect.height + 8f;
            float y = 0f;
            for (int i = 0; i < acquireRows.Count; i++)
            {
                AcquireRow r = acquireRows[i];
                if (y > bottom)
                {
                    break; // rows are in order, so nothing below is visible
                }
                if (y + r.height >= top)
                {
                    Rect row = new Rect(0f, y, view.width, r.height);
                    style.Card(row, Mouse.IsOver(row));
                    Rect body = row.ContractedBy(6f);

                    Text.Font = GameFont.Small;
                    Widgets.Label(new Rect(body.x, body.y, body.width - btnW - 6f, 22f), r.def.LabelCap);

                    Rect btn = new Rect(body.xMax - btnW, body.y, btnW, 26f);
                    bool enabled = r.can && canSpend;
                    if (style.Button(btn, selectLabel, enabled, null) && enabled)
                    {
                        tracker.AddExpertise(r.def);
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                        acquireFor = null; // rebuild next frame
                    }

                    if (!r.effects.NullOrEmpty())
                    {
                        GUI.color = style.Dim;
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(body.x, body.y + 26f, body.width, body.height - 26f), r.effects);
                        Text.Font = GameFont.Small;
                        GUI.color = Color.white;
                    }

                    if (Mouse.IsOver(row))
                    {
                        TooltipHandler.TipRegion(row, r.tip);
                    }
                }
                y += r.height + 4f;
            }
            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
