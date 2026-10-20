using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record WuHuangFuSongChaozhenDisabledEvent(long FrameId, int OwnerSeat, WuHuangFuSongTurn Turn) : IGameEvent;
internal sealed class ResolveWuHuangFuSongChaozhenDescriptor : WuHuangFuSongDescriptor
{
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen; public override ISkillProgramEffectHandler Handler { get; } = new WuHuangFuSongChaozhenHandler();
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class WuHuangFuSongChaozhenHandler : WuHuangFuSongHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen; }
