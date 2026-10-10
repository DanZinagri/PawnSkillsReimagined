using System.Linq;
using RimWorld;
using Verse;
using VSE.Passions;
using WantsAndQuirks;

namespace PawnSkillsReimagined.CharacterDevelopment
{
    // Legendary Character Development reward: raises one random skill's passion a
    // single step along VSE's ladder - Frozen/Apathy -> none -> Interested ->
    // Burning -> Critical. Any other passion (e.g. Alpha Skills' situational ones,
    // which sit around Interested) becomes Burning. Skills already Critical are
    // never picked. Like CD's own passion reward, the passion is granted directly.
    public class RewardWorker_UpgradePassion : RewardWorker
    {
        public override bool CanBestowOn(Pawn pawn, ThingDef item = null, Pawn targetPawn = null)
        {
            return base.CanBestowOn(pawn, item, targetPawn) && pawn.skills != null &&
                   pawn.skills.skills.Any(CanUpgrade);
        }

        public override void OnAcquired(Pawn pawn, Quirk quirk)
        {
            if (pawn.skills == null || !pawn.skills.skills.Where(CanUpgrade).TryRandomElement(out SkillRecord skill))
            {
                return;
            }
            PassionDef from = PassionManager.PassionToDef(skill.passion);
            PassionDef to = NextPassion(from);
            skill.passion = (Passion)(byte)to.index;
            Messages.Message("PSR_PassionUpgraded".Translate(pawn.Named("PAWN"), skill.def.label, from.label, to.label),
                pawn, MessageTypeDefOf.PositiveEvent);
        }

        private static bool CanUpgrade(SkillRecord skill)
        {
            return !skill.TotallyDisabled && NextPassion(PassionManager.PassionToDef(skill.passion)) != null;
        }

        private static PassionDef NextPassion(PassionDef from)
        {
            switch (from?.defName)
            {
                case null:
                case "VSE_Critical":
                    return null;
                case "AS_FrozenPassion":
                case "VSE_Apathy":
                    return Named("None");
                case "None":
                    return Named("Minor");
                case "Minor":
                    return Named("Major");
                case "Major":
                    return Named("VSE_Critical");
                default:
                    return Named("Major");
            }
        }

        private static PassionDef Named(string defName) => DefDatabase<PassionDef>.GetNamedSilentFail(defName);
    }
}
