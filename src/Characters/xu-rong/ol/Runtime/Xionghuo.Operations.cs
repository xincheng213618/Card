namespace CardGame.Core;
internal sealed class InitializeXionghuoPoolDescriptor:XuRongDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InitializeXionghuoPool;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)];
}
internal sealed class TransferXionghuoMarkDescriptor:XuRongDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.TransferXionghuoMark;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget()];
}
internal sealed class SettleXionghuoMarkedPlayDescriptor:XuRongDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.SettleXionghuoMarkedPlay;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}
