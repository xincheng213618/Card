using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record XinyouMaterialFrozenEvent(long FrameId, int Index, int CardId, CardKind PrintedKind, CardKind BasicName) : IGameEvent;
public sealed record XinyouPaidEvent(long FrameId, int OwnerSeat, int ActualTurn, int TurnOwner, long PhaseToken,
    int NamesMask, int MaterialCount, long Before, long After) : IGameEvent;
public sealed record XinyouMaterialDiscardedEvent(long FrameId, int Index, int CardId, long MovementSequence) : IGameEvent;
public sealed record XinyouGainedEntityEvent(long FrameId, int Request, int Index, int OwnerSeat, int ActualTurn,
    int TurnOwner, int CardId, CardKind PrintedKind, long MovementSequence, bool Replacement) : IGameEvent;
internal sealed class ResolveXinyouBasicCycleDescriptor() : XieLingYuDescriptor(SkillProgramEffectOp.ResolveXinyouBasicCycle) {
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeTrueUseProcessingEntry);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.XieLingYu is { Paid: true };
}
