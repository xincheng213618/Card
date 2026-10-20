using System.Text.Json.Serialization;

namespace CardGame.Core;
internal sealed class RecordBixiongActualDiscardDescriptor() : DuXiDescriptor(SkillProgramEffectOp.RecordBixiongActualDiscard)
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)];
}
public sealed record DuXiBixiongSuitRecordedEvent(long BatchId, CardConversionSource Source, int OwnerSeat, int ActualTurnNumber,
    long ActualDiscardPhaseToken, int CardId, int MovementSequence, Suit Suit) : IGameEvent;
public sealed record DuXiBixiongExpiredEvent(int OwnerSeat, int ActualTurnNumber) : IGameEvent;
