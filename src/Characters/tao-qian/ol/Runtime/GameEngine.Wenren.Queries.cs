using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 28122, SkillProgramEffectOp.ResolveWenrenSequentialDraw)]
    private bool CanActivateWenren(ProgramActivationLegalityContext c) => c.Owner.IsAlive && _phase == TurnPhase.Play && _currentSeat == c.OwnerSeat && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == c.OwnerSeat && !ProgramEventHistory<WenrenCommittedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat && e.Phase == p);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveWenrenSequentialDraw)]
    private void ReturnWenrenDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.TaoQian!;
        if (!r.Paid || r.Stage != TaoQianStage.Drawing || r.Cursor >= r.Targets.Count || outcome.RecipientSeat != r.Targets[r.Cursor] || outcome.RequestedCount != r.DrawAmount) throw new InvalidOperationException("Wenren's native draw returned to a different frozen recipient or amount.");
        ReplaceRuntimeTop(f = f with { TaoQian = r with { Stage = TaoQianStage.DrawChildren, DrawOutcome = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new WenrenDrawReturnedEvent(f.Id, r.Cursor, outcome.RecipientSeat, outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId)); ContinueTaoQian(f.Id);
    }
}
