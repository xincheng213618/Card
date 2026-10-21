namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidPingluStarted(ProgramSkillFrame frame)
    {
        if (frame.Pinglu is not { } r || r.Source != PingluSource(frame) || r.GameplayHash != frame.GameplayHash ||
            frame.SkillId != "le:pinglu" || frame.ActivationId != "all-in-range-random-hand" || frame.TriggerId is not null ||
            frame.WindowContext is not null || frame.InstructionIndex != 1 || r.InstructionIndex != frame.InstructionIndex ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            _contentRegistry.GetSkill(frame.SkillId).Program?.GameplayHash != r.GameplayHash ||
            r.Phase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != frame.OwnerSeat ||
            phase.TurnNumber != r.ActualTurn || phase.ActualTurnOwnerSeat != r.TurnOwner || !IsRecordedActualDiscardRecoveryPhase(phase)) return false;
        return ProgramEventHistory<PingluStartedEvent>().Where(e => e.FrameId == frame.Id).ToArray() is [var started] &&
            started == new PingluStartedEvent(frame.Id, r.Source, r.GameplayHash, r.Phase, r.ActualTurn,
                r.TurnOwner, r.Targets.Count, r.SequenceBefore) &&
            // Active programs publish ProgramSkillStarted, while lifecycle
            // triggers publish ProgramBindingStarted. The signed Pinglu fact
            // above retains this activation's exact source instance and hash.
            ProgramEventHistory<ProgramSkillStartedEvent>().Count(e => e.FrameId == frame.Id && e.SkillId == frame.SkillId &&
                e.ActivationId == frame.ActivationId && e.OwnerSeat == frame.OwnerSeat) == 1;
    }

    private bool ValidPingluPaid(ProgramSkillFrame frame)
    {
        if (!ValidPingluStarted(frame) || frame.Pinglu is not { ActualCount: >= 0, BatchId: > 0 } r ||
            r.Before != r.SequenceBefore || r.After <= r.Before || r.After > _movementSequence) return false;
        var selected = r.Targets.Where(s => s.CardId is not null).ToArray();
        var entries = ProgramEventHistory<PingluEntityObtainedEvent>().Where(e => e.FrameId == frame.Id).OrderBy(e => e.Index).ToArray();
        if (selected.Length == 0 || entries.Length != selected.Length || r.ActualCount != entries.Count(e => e.To == CardLocation.Hand(frame.OwnerSeat)) ||
            ProgramEventHistory<PingluObtainPaidEvent>().Where(e => e.FrameId == frame.Id).ToArray() is not [var paid] ||
            paid != new PingluObtainPaidEvent(frame.Id, r.BatchId, selected.Length, r.ActualCount, r.Before, r.After)) return false;
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i]; var sample = selected[i];
            if (entry.Index != i || entry.TargetSeat != sample.TargetSeat || entry.CardId != sample.CardId ||
                entry.PrintedKind != sample.PrintedKind || entry.From != sample.From || entry.From != CardLocation.Hand(sample.TargetSeat) ||
                entry.To == entry.From || entry.BatchId != r.BatchId || entry.Sequence <= r.Before || entry.Sequence > r.After ||
                _cardMovements.Count(m => m.CardId == entry.CardId && m.CardKind == entry.PrintedKind && m.From == entry.From &&
                    m.To == entry.To && m.Sequence == entry.Sequence && m.Reason.Value == PingluObtainReason) != 1) return false;
        }
        return _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.Reason.Value == PingluObtainReason) == entries.Length;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28820)]
    private void AssertPinglu(ProgramSkillFrame frame)
    {
        if (frame.Pinglu is not { } r) return;
        if (!ValidPingluStarted(frame) || r.Stage != PingluStage.ObtainChildren || r.Before != r.SequenceBefore ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.Targets is not System.Collections.IList { IsReadOnly: true } || r.Targets.Count == 0 ||
            r.Targets.Select(s => s.TargetSeat).Distinct().Count() != r.Targets.Count ||
            r.Targets.Any(s => !IsValidPlayerSeat(s.TargetSeat) || s.TargetSeat == frame.OwnerSeat) ||
            !r.Targets.Select(s => s.TargetSeat).SequenceEqual(r.Targets.Select(s => s.TargetSeat)
                .OrderBy(seat => RelativeTableOrder(frame.OwnerSeat, seat))) ||
            r.Targets.Where(s => s.CardId is not null).Select(s => s.CardId).Distinct().Count() != r.Targets.Count(s => s.CardId is not null))
            throw new InvalidOperationException("Pinglu lost its exact source, actual Play or frozen all-target original Hand samples.");
        var samples = ProgramEventHistory<PingluTargetSampledEvent>().Where(e => e.FrameId == frame.Id).OrderBy(e => e.Index).ToArray();
        if (samples.Length != r.Targets.Count) throw new InvalidOperationException("Pinglu lost an original target's one random sample.");
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = r.Targets[i];
            if (sample.Index != i || sample.HandCount < 0 || sample.RandomBefore == 0 ||
                i > 0 && sample.RandomBefore != r.Targets[i - 1].RandomAfter ||
                samples[i] != new PingluTargetSampledEvent(frame.Id, i, sample.TargetSeat, sample.HandCount, sample.CardId,
                    sample.PrintedKind, sample.From, sample.RandomBefore, sample.RandomAfter, r.SequenceBefore))
                throw new InvalidOperationException("Pinglu changed its original random cursor or sampled participant.");
            if (sample.HandCount == 0)
            {
                if (sample.CardId is not null || sample.PrintedKind is not null || sample.From is not null || sample.RandomBefore != sample.RandomAfter)
                    throw new InvalidOperationException("An empty target cannot pay a fictional Hand card or consume random selection.");
            }
            else
            {
                var random = new DeterministicRandom(unchecked((int)sample.RandomBefore)); random.Next(sample.HandCount);
                if (sample.CardId is not > 0 || sample.PrintedKind is null || sample.From != CardLocation.Hand(sample.TargetSeat) ||
                    random.State != sample.RandomAfter)
                    throw new InvalidOperationException("Pinglu requires one original private Hand entity and exactly one real random selection per nonempty target.");
            }
        }
        if (r.ActualCount >= 0)
        { if (!ValidPingluPaid(frame)) throw new InvalidOperationException("Pinglu lost its exact paid native obtain batch or actual destinations."); }
        else if (r.ActualCount != -1 || r.BatchId != 0 || r.After != r.Before)
            throw new InvalidOperationException("Pinglu cannot enter child return before its actual obtain is signed.");
        if (frame.PendingMovementContinuation is { } pending && !IsPingluMovement(frame, GetPausedPrivateOfferEffect(frame), pending))
            throw new InvalidOperationException("Pinglu lost its original paid movement continuation.");
    }

    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 640)]
    private bool IsPingluMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        frame.Pinglu is { Stage: PingluStage.ObtainChildren } &&
        effect?.Op == SkillProgramEffectOp.ResolvePingluInRangeRandomHands &&
        frame.PendingMovementContinuation == pending && pending.SubjectSeat == frame.OwnerSeat &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && ValidPingluPaid(frame);
}
