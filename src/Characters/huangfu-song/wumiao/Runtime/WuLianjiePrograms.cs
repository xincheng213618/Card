using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record WuHuangFuSongLianjieEntityEvent(long FrameId, int OwnerSeat, int CardId, long DrawSequence, WuHuangFuSongTurn Turn) : IGameEvent;
internal sealed class ResolveWuHuangFuSongLianjieDescriptor : WuHuangFuSongDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuHuangFuSongLianjie; public override ISkillProgramEffectHandler Handler { get; } = new WuHuangFuSongLianjieHandler();
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class WuHuangFuSongLianjieHandler : WuHuangFuSongHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuHuangFuSongLianjie; }
