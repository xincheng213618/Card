namespace CardGame.Core;
public sealed record GuanLethalSparedEvent(long FrameId, int OwnerSeat, int TargetSeat, int TurnNumber) : IGameEvent;
internal sealed class SpareFirstLethalTargetDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SpareFirstLethalTarget; }
internal sealed class CancelSparedCardTargetsDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.CancelSparedCardTargets; }
