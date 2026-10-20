namespace CardGame.Core;
internal sealed class ReplenishShaJueAndClaimDyingCauseDescriptor:XuRongDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ReplenishShaJueAndClaimDyingCause;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)];
}
