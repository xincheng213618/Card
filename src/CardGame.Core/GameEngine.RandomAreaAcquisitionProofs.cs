namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool SameRandomAreaStep(RandomAreaAcquisitionStepReceipt a, RandomAreaAcquisitionStepReceipt b) =>
        a.SeatIndex == b.SeatIndex && a.TargetSeat == b.TargetSeat && a.SelectedZone == b.SelectedZone && a.RandomBefore == b.RandomBefore && a.RandomAfter == b.RandomAfter &&
        a.MovementBatchId == b.MovementBatchId && a.SequenceBefore == b.SequenceBefore && a.SequenceAfter == b.SequenceAfter &&
        a.EligibleZones.SequenceEqual(b.EligibleZones) && a.Materials.SequenceEqual(b.Materials) && a.Movements.SequenceEqual(b.Movements);
    private bool ValidRandomAreaStep(ProgramSkillFrame frame, RandomAreaAcquisitionStepReceipt step)
    {
        if (step.SeatIndex < 0 || step.SeatIndex >= frame.RandomAreaAcquisition!.OtherSeats.Count || frame.RandomAreaAcquisition.OtherSeats[step.SeatIndex] != step.TargetSeat ||
            step.EligibleZones.Count == 0 || !step.EligibleZones.SequenceEqual(ObtainRandomAreaFromEachOtherThenTurnOverDescriptor.Areas.Where(step.EligibleZones.Contains)) ||
            step.RandomBefore == 0 || step.MovementBatchId <= 0 || step.SequenceBefore < 0 || step.SequenceAfter <= step.SequenceBefore || step.SequenceAfter > _movementSequence ||
            step.Materials.Count == 0 || step.Materials.Select(m => m.CardId).Distinct().Count() != step.Materials.Count || step.Movements.Count != step.Materials.Count ||
            !step.Movements.Select(m => new DamageTargetRegionMaterial(m.CardId, m.CardKind)).SequenceEqual(step.Materials) ||
            !step.Movements.SequenceEqual(_cardMovements.Where(m => m.Sequence > step.SequenceBefore && m.Sequence <= step.SequenceAfter))) return false;
        var replay = new DeterministicRandom(unchecked((int)step.RandomBefore));
        if (step.EligibleZones[replay.Next(step.EligibleZones.Count)] != step.SelectedZone || replay.State != step.RandomAfter) return false;
        foreach (var movement in step.Movements)
        {
            var card = GetAdvancedCard(movement.CardId); var from = new CardLocation(step.SelectedZone, step.TargetSeat);
            var expected = card.IsGeneralWeapon && from.Zone == CardZoneKind.Equipment || IsGuanduThunderChariotDeparture(card, from, CardLocation.Hand(frame.OwnerSeat))
                ? CardLocation.OutsideGame : CardLocation.Hand(frame.OwnerSeat);
            expected = NormalizeDiouCarrierDestination(card, from, expected);
            if (movement.From != from || movement.To != expected || movement.Reason != RandomAreaReason(frame, step.SeatIndex)) return false;
        }
        return ProgramEventHistory<RandomAreaAcquisitionRegionPaidEvent>().Where(e => e.ProgramFrameId == frame.Id && e.Step.SeatIndex == step.SeatIndex).ToArray() is [var issued] &&
            SameRandomAreaStep(step, issued.Step);
    }
    private bool ValidRandomAreaAcquisition(ProgramSkillFrame frame)
    {
        if (frame.RandomAreaAcquisition is not { } r || !ExactRandomAreaParent(frame, out var window, out var damage) ||
            r.FrameId != frame.Id || r.InstructionIndex != frame.InstructionIndex || r.Source != RandomAreaSource(frame) || r.GameplayHash != frame.GameplayHash ||
            _contentRegistry.GetSkill(frame.SkillId).Program?.GameplayHash != r.GameplayHash || r.DamageWindowId != window.Id || r.DamageFrameId != damage.Id ||
            r.OccurrenceIndex != frame.WindowContext!.OccurrenceIndex || r.DamageAmount != damage.Amount || !Enum.IsDefined(r.Stage) ||
            !r.OtherSeats.SequenceEqual(_players.Where(p => p.Seat != frame.OwnerSeat).OrderBy(p => (p.Seat - frame.OwnerSeat + _playerCount) % _playerCount).Select(p => p.Seat)) ||
            r.NextSeatIndex < 0 || r.NextSeatIndex > r.OtherSeats.Count || r.Steps.Select(s => s.SeatIndex).Distinct().Count() != r.Steps.Count ||
            !r.Steps.Select(s => s.SeatIndex).SequenceEqual(r.Steps.Select(s => s.SeatIndex).Order()) || r.Steps.Any(s => !ValidRandomAreaStep(frame, s))) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var starts = history.OfType<RandomAreaAcquisitionStartedEvent>().Where(e => e.Receipt.FrameId == frame.Id).ToArray();
        if (starts is not [var started] || started.Receipt is not { Stage: RandomAreaAcquisitionStage.Visiting, NextSeatIndex: 0, Steps.Count: 0, TurnoverIssued: false } initial ||
            initial.FrameId != r.FrameId || initial.InstructionIndex != r.InstructionIndex || initial.Source != r.Source || initial.GameplayHash != r.GameplayHash ||
            initial.DamageWindowId != r.DamageWindowId || initial.DamageFrameId != r.DamageFrameId || initial.OccurrenceIndex != r.OccurrenceIndex ||
            initial.DamageAmount != r.DamageAmount || !initial.OtherSeats.SequenceEqual(r.OtherSeats) ||
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == frame.Id && e.OwnerSeat == frame.OwnerSeat && e.SkillId == frame.SkillId &&
                e.SkillInstanceId == frame.SkillInstanceId && e.BindingId == frame.TriggerId && e.Window == SkillProgramTriggerWindow.AfterDamageApplied) != 1 ||
            history.OfType<RandomAreaAcquisitionRegionPaidEvent>().Count(e => e.ProgramFrameId == frame.Id) != r.Steps.Count) return false;
        var skipped = history.OfType<RandomAreaAcquisitionSeatSkippedEvent>().Where(e => e.ProgramFrameId == frame.Id).ToArray();
        var paidThrough = r.Stage == RandomAreaAcquisitionStage.MovementChildren ? r.NextSeatIndex + 1 : r.NextSeatIndex;
        if (r.Steps.Select(s => s.SeatIndex).Concat(skipped.Select(s => s.SeatIndex)).Order().SequenceEqual(Enumerable.Range(0, paidThrough)) == false ||
            skipped.Any(s => s.SeatIndex < 0 || s.SeatIndex >= r.OtherSeats.Count || r.OtherSeats[s.SeatIndex] != s.TargetSeat)) return false;
        for (var i = 1; i < r.Steps.Count; i++)
            if (r.Steps[i].SequenceBefore < r.Steps[i - 1].SequenceAfter) return false;
        var turns = history.OfType<RandomAreaAcquisitionTurnoverIssuedEvent>().Where(e => e.ProgramFrameId == frame.Id).ToArray();
        if (r.TurnoverIssued ? r.NextSeatIndex != r.OtherSeats.Count || r.FaceDownBefore is not { } before || r.FaceDownAfter is not { } after ||
                after != (r.TurnoverPrevented ? before : !before) || turns is not [var turn] || turn != new RandomAreaAcquisitionTurnoverIssuedEvent(frame.Id, before, after, r.TurnoverPrevented)
            : r.FaceDownBefore is not null || r.FaceDownAfter is not null || r.TurnoverPrevented || turns.Length != 0) return false;
        var completed = history.OfType<RandomAreaAcquisitionCompletedEvent>().Where(e => e.ProgramFrameId == frame.Id).ToArray();
        if (r.Stage == RandomAreaAcquisitionStage.Finished)
            return frame.PendingMovementContinuation is null && completed is [var done] && done == new RandomAreaAcquisitionCompletedEvent(frame.Id,
                r.Steps.Count, r.Steps.Sum(s => s.ActualObtained), r.TurnoverIssued, r.Cancelled);
        if (completed.Length != 0 || r.Cancelled) return false;
        return r.Stage == RandomAreaAcquisitionStage.MovementChildren
            ? !r.TurnoverIssued && r.Steps.Count > 0 && r.Steps[^1].SeatIndex == r.NextSeatIndex &&
                frame.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending && pending.SubjectSeat == frame.OwnerSeat
            : frame.PendingMovementContinuation is null && (r.Stage == RandomAreaAcquisitionStage.TurnoverChildren) == r.TurnoverIssued;
    }
    private void RequireRandomAreaAcquisition(ProgramSkillFrame frame)
    {
        if (!ValidRandomAreaAcquisition(frame)) throw new InvalidOperationException("Random area acquisition lost its exact damage point, seat cursor, random choice or issued atomic region.");
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 1245)]
    private void AssertRandomAreaAcquisition(ProgramSkillFrame frame)
    { if (frame.RandomAreaAcquisition is not null) RequireRandomAreaAcquisition(frame); }
}
