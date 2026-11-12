using RimWorld;
using UnityEngine;
using Verse;

namespace PawnSkillsReimagined
{
    // Inspect-pane tab (next to Bio/Social/Gear) showing the skill-point panel for
    // the selected pawn.
    public class ITab_SkillPoints : ITab
    {
        private readonly SkillPointsPanel panel = new SkillPointsPanel();

        public ITab_SkillPoints()
        {
            size = new Vector2(panel.Width, SkillPointsPanel.Height);
            labelKey = "PSR_TabSkills";
        }

        public override bool IsVisible
        {
            get
            {
                if (!(SelThing is Pawn pawn) || pawn.skills == null)
                {
                    return false;
                }
                var settings = PawnSkillsReimaginedMod.Settings;
                // Players can hide the tab in favour of the bottom-bar window, but only
                // for pawns that window lists (free colonists), and only while its
                // button is shown - so there is always a way to spend points.
                return settings.showSkillsInspectTab || !settings.showSkillsMainButton || !pawn.IsFreeColonist;
            }
        }

        protected override void FillTab()
        {
            if (!(SelThing is Pawn pawn))
            {
                return;
            }
            // The panel widens while its expertise drawer is open.
            size.x = panel.Width;
            panel.Draw(new Rect(0f, 0f, size.x, size.y), pawn);
        }
    }
}
