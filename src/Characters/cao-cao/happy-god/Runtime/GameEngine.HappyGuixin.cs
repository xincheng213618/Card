namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HappyGuixinReason = "skill-program.happy:guixin.obtain";
    private static CardConversionSource HappyGuixinSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
    private static int HappyGuixinActualObtained(ProgramSkillFrame f) => f.HappyGuixin!.Payments.Sum(p =>
        p.Movement.Movements.Count(m => m.To == CardLocation.Hand(f.OwnerSeat)));
    [ProgramCapability] private sealed class HappyGuixinCapability(GameEngine e) : IHappyGuixinHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => e.BeginHappyGuixin(f); }
    private SkillProgramStepOutcome BeginHappyGuixin(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.HappyGuixin is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !ExactOrderedPrintedSkillLossParent(f, out var window, out var damage, out _) || damage.TargetSeat != f.OwnerSeat ||
            f.WindowContext!.OccurrenceIndex < 0 || f.WindowContext.OccurrenceIndex >= damage.Amount)
            throw new InvalidOperationException("Happy Guixin requires its exact original positive damage-point candidate.");
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var r = new HappyGuixinReceipt(HappyGuixinSource(f), f.GameplayHash, window.Id, damage.Id, f.WindowContext.OccurrenceIndex, damage.Amount)
        { OtherSeats = _players.Where(p => p.Seat != f.OwnerSeat).OrderBy(p => RelativeTableOrder(f.OwnerSeat, p.Seat)).Select(p => p.Seat).ToArray() };
        ReplaceRuntimeTop(f with { HappyGuixin = r });
        AdvanceEventRulesAndQueueFact(new HappyGuixinStartedEvent(f.Id, r.Source, r.GameplayHash, window.Id, damage.Id, r.OccurrenceIndex, damage.Amount, string.Join(",", r.OtherSeats)));
        ResumeHappyGuixin(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 41400)] private bool ResumeHappyGuixin(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HappyGuixin: not null } initial || initial.Id != id) return false;
        while (_pendingDecision is null)
        {
            var f = GetActiveProgramFrame(id); RequireHappyGuixin(f); var r = f.HappyGuixin!;
            if (r.Stage is HappyGuixinStage.MovementChildren or HappyGuixinStage.TurnoverChildren)
            {
                if (DrainMobileGuixinChildren(f)) return true;
                if (r.Stage == HappyGuixinStage.TurnoverChildren) { FinishHappyGuixin(f, false); return true; }
                ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null,
                    HappyGuixin = r = r with { Stage = HappyGuixinStage.Visiting, Cursor = r.Cursor + 1 } });
            }
            if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || r.Payments.Count == 0 &&
                !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            { FinishHappyGuixin(f, true); return true; }
            if (r.Cursor == r.OtherSeats.Count)
            {
                var obtained = HappyGuixinActualObtained(f);
                var before = _players[f.OwnerSeat].IsFaceDown;
                if (obtained <= 4 || before) { FinishHappyGuixin(f, false); return true; }
                ReplaceRuntimeTop(f = f with { HappyGuixin = r with { Stage = HappyGuixinStage.TurnoverChildren, TurnoverIssued = true,
                    FaceDownBefore = before, FaceDownAfter = ShenwuCannotTurnOver(_players[f.OwnerSeat]) ? before : !before } });
                TurnOverProgramTarget(id, f.OwnerSeat, f.OwnerSeat);
                AdvanceEventRulesAndQueueFact(new HappyGuixinTurnoverIssuedEvent(id, obtained, before, _players[f.OwnerSeat].IsFaceDown)); continue;
            }
            var target = r.OtherSeats[r.Cursor];
            // The established HEJ obtain helper enforces current ownership, area access and pending-judgment exclusions.
            var candidates = _players[target].IsAlive ? OlGuixinCandidates(f, target) : [];
            if (candidates.Count == 0)
            {
                ReplaceRuntimeTop(f with { HappyGuixin = r with { Cursor = r.Cursor + 1 } });
                AdvanceEventRulesAndQueueFact(new HappyGuixinSeatSkippedEvent(id, r.Cursor, target)); continue;
            }
            var randomBefore = _random.State;
            var selected = candidates[_random.Next(candidates.Count)];
            var randomAfter = _random.State;
            var beforeMove = _movementSequence;
            ReplaceRuntimeTop(f with { HappyGuixin = r with { Stage = HappyGuixinStage.MovementChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveProgramCardsFromMultipleSources([selected.CardId], CardLocation.Hand(f.OwnerSeat), new(HappyGuixinReason), (batch, records) =>
            {
                var parent = GetActiveProgramFrame(id);
                var payment = new HappyGuixinPayment(new(r.Cursor, target, selected.CardId, selected.From, batch, beforeMove, _movementSequence)
                    { Movements = records }, randomBefore, randomAfter) { Candidates = candidates };
                ReplaceRuntimeTop(parent with { HappyGuixin = parent.HappyGuixin! with { Payments = parent.HappyGuixin.Payments.Append(payment).ToArray() } });
                AdvanceEventRulesAndQueueFact(new HappyGuixinMovementPaidEvent(id, r.Cursor, target, batch, beforeMove, _movementSequence));
            }, disclosure: selected.From.Zone == CardZoneKind.Hand ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
        }
        return true;
    }
    private void FinishHappyGuixin(ProgramSkillFrame f, bool cancelled)
    {
        var r = f.HappyGuixin!;
        if (f.PendingMovementContinuation is not null || r.Stage == HappyGuixinStage.MovementChildren)
            throw new InvalidOperationException("Happy Guixin cannot retire a paid gain before its children return.");
        ReplaceRuntimeTop(f = f with { HappyGuixin = r with { Stage = HappyGuixinStage.Finished } });
        AdvanceEventRulesAndQueueFact(new HappyGuixinCompletedEvent(f.Id, r.Payments.Count, HappyGuixinActualObtained(f), r.TurnoverIssued, cancelled));
        FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 41400)] private bool ReturnHappyGuixinMovement(ProgramSkillFrame f) => f.HappyGuixin is not null && ResumeHappyGuixin(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 41400)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 41400)] private bool RetainHappyGuixinPaid(ProgramSkillFrame f) =>
        f.HappyGuixin is { } r && (r.Payments.Count > 0 || r.TurnoverIssued) && ValidHappyGuixin(f);
}
