using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class OfferSidaoCompletedPairSnatchDescriptor:GuoSiDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.OfferSidaoCompletedPairSnatch;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
}
