using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record WuHuangFuSongJiangxianArmedEvent(long FrameId, CardConversionSource Source, string GameplayHash, WuHuangFuSongTurn Turn) : IGameEvent;
public sealed record WuHuangFuSongJiangxianSettledEvent(long FrameId, int OwnerSeat, string? LostSkill, WuHuangFuSongTurn Turn) : IGameEvent;
public sealed record WuHuangFuSongSkillRemovedEvent(long FrameId, int OwnerSeat, string SkillId, string GrantId) : IGameEvent;
internal sealed class ArmWuHuangFuSongJiangxianDescriptor : WuHuangFuSongDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmWuHuangFuSongJiangxian; public override ISkillProgramEffectHandler Handler { get; } = new WuHuangFuSongArmHandler(); }
internal sealed class SettleWuHuangFuSongJiangxianDescriptor : WuHuangFuSongDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.SettleWuHuangFuSongJiangxian; public override ISkillProgramEffectHandler Handler { get; } = new WuHuangFuSongSettleHandler();
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class WuHuangFuSongArmHandler : WuHuangFuSongHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmWuHuangFuSongJiangxian; }
internal sealed class WuHuangFuSongSettleHandler : WuHuangFuSongHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SettleWuHuangFuSongJiangxian; }
