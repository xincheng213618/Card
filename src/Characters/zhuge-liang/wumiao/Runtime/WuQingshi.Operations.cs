using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record WuZhugeLiangQingshiAcceptedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int TurnOwner, long ActionId, CardKind Name) : IGameEvent;
public sealed record WuZhugeLiangQingshiBranchEvent(long FrameId, string Branch) : IGameEvent;
public sealed record WuZhugeLiangQingshiDamageEvent(long FrameId, CardConversionSource Source, string GameplayHash, long ActionId, long UseId, int TargetSeat) : IGameEvent;
public sealed record WuZhugeLiangQingshiTargetEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record WuZhugeLiangQingshiDisabledEvent(long FrameId, int OwnerSeat, int ActualTurn, int TurnOwner) : IGameEvent;
internal sealed class ResolveWuZhugeLiangQingshiDescriptor : WuZhugeLiangDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuZhugeLiangQingshi;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
