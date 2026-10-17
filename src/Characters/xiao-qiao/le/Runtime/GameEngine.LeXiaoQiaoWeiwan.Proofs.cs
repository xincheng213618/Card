namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasPaidWeiwanSelection(ProgramSkillFrame f)
    {
        if (f.Weiwan is not { CostSequence: > 0 } r || f.SkillId != "le:weiwan" || f.ActivationId != "discard-qin-obtain-suits" ||
            f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex != 1 || r.InstructionIndex != f.InstructionIndex ||
            r.Source != WeiwanSource(f) || r.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            !f.SelectedCardIds.SequenceEqual([r.CardId]) || !f.SelectedTargetSeats.SequenceEqual([r.TargetSeat]) ||
            r.CostFrom.OwnerSeat != f.OwnerSeat || r.CostFrom.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            r.CostSequence <= r.SequenceBefore || r.CostSequence > _movementSequence || !HasQiqinEntityMark(r.CardId) ||
            ProgramEventHistory<WeiwanStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start != new WeiwanStartedEvent(f.Id, r.Source, r.GameplayHash, r.Phase, r.ActualTurn, r.TurnOwner,
                r.CardId, r.PrintedKind, r.CostFrom, r.CostSuit, r.TargetSeat, r.SequenceBefore) ||
            ProgramEventHistory<WeiwanCostPaidEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paid] ||
            paid != new WeiwanCostPaidEvent(f.Id, r.CostBatchId, r.CardId, r.PrintedKind, r.CostFrom, r.CostTo, r.CostSequence)) return false;
        return _cardMovements.Count(m => m.Sequence == r.CostSequence && m.CardId == r.CardId && m.CardKind == r.PrintedKind &&
            m.From == r.CostFrom && m.To == r.CostTo && m.Reason.Value == WeiwanCostReason) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28620)]
    private void AssertWeiwan(ProgramSkillFrame f)
    {
        if (f.Weiwan is not { } r) return;
        if (f.SkillId != "le:weiwan" || f.ActivationId != "discard-qin-obtain-suits" || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != 1 || r.InstructionIndex != f.InstructionIndex || r.Source != WeiwanSource(f) || r.GameplayHash != f.GameplayHash ||
            r.Phase.Kind != ActualDiscardRecoveryPhaseKind.Play || r.Phase.ActorSeat != f.OwnerSeat ||
            r.Phase.TurnNumber != r.ActualTurn || r.Phase.ActualTurnOwnerSeat != r.TurnOwner || !IsRecordedActualDiscardRecoveryPhase(r.Phase) ||
            !Enum.IsDefined(r.Stage) || !Enum.IsDefined(r.CostSuit) || !IsValidPlayerSeat(r.TargetSeat) || r.TargetSeat == f.OwnerSeat ||
            !f.SelectedCardIds.SequenceEqual([r.CardId]) || !f.SelectedTargetSeats.SequenceEqual([r.TargetSeat]) ||
            r.Before < r.SequenceBefore || r.After < r.Before || r.After > _movementSequence ||
            r.Samples is not System.Collections.IList { IsReadOnly: true } ||
            r.CostSequence > 0 && !HasPaidWeiwanSelection(f) || r.CostSequence == 0 &&
                (r.Stage != WeiwanStage.CostChildren || r.CostBatchId != 0 || ProgramEventHistory<WeiwanCostPaidEvent>().Any(e => e.FrameId == f.Id)))
            throw new InvalidOperationException("Weiwan lost its exact original Qin cost, source, selected target or native phase.");
        var sampled = ProgramEventHistory<WeiwanSuitSampledEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == WeiwanStage.CostChildren)
        {
            if (r.Samples.Count != 0 || sampled.Length != 0 || r.ActualCount != -1 || r.ObtainBatchId != 0 || r.Branch is not null)
                throw new InvalidOperationException("Weiwan cannot sample its target before the cost children finish.");
            return;
        }
        if (!HasPaidWeiwanSelection(f) || r.Before < r.CostSequence || r.Samples.Count > 4 ||
            r.Samples.Select(s => s.Suit).Distinct().Count() != r.Samples.Count || sampled.Length != r.Samples.Count ||
            r.Samples.Where(s => s.CardId is not null).Select(s => s.CardId).Distinct().Count() != r.Samples.Count(s => s.CardId is not null))
            throw new InvalidOperationException("Weiwan lost its once-only original random suit group.");
        for (var i = 0; i < r.Samples.Count; i++)
        {
            var s = r.Samples[i];
            if (s.Index != i || !WeiwanSuits.Contains(s.Suit) || s.Suit == r.CostSuit || s.CandidateCount < 0 ||
                (s.CardId is null) != (s.CandidateCount == 0) || (s.CardId is null) != (s.PrintedKind is null) ||
                (s.CardId is null) != (s.From is null) || s.From is { } from &&
                    (from.OwnerSeat != r.TargetSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)) ||
                sampled[i] != new WeiwanSuitSampledEvent(f.Id, s.Index, s.Suit, s.CardId, s.PrintedKind, s.From,
                    s.CandidateCount, s.RandomBefore, s.RandomAfter, r.Before))
                throw new InvalidOperationException("Weiwan's private random sample changed its suit, original area or scalar invoice.");
            var random = new DeterministicRandom(unchecked((int)s.RandomBefore));
            if (s.CandidateCount > 0) _ = random.Next(s.CandidateCount);
            if (random.State != s.RandomAfter || i > 0 && s.RandomBefore != r.Samples[i - 1].RandomAfter)
                throw new InvalidOperationException("Weiwan cannot redraw or replace its original deterministic choice.");
        }
        var moves = ProgramEventHistory<WeiwanEntityObtainedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var selected = r.Samples.Where(s => s.CardId is not null).ToArray();
        if (r.ActualCount == -1)
        {
            if (r.Stage != WeiwanStage.ObtainChildren || moves.Length != 0 || r.ObtainBatchId != 0 || r.Branch is not null)
                throw new InvalidOperationException("Weiwan cannot finish before the actual original entity obtain batch.");
            return;
        }
        if (moves.Length != selected.Length || r.ActualCount != moves.Count(m => m.To == CardLocation.Hand(f.OwnerSeat)) ||
            ProgramEventHistory<WeiwanObtainPaidEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paidObtain] ||
            paidObtain != new WeiwanObtainPaidEvent(f.Id, r.ObtainBatchId, selected.Length, r.ActualCount, r.Before, r.After) ||
            selected.Length == 0 && (r.ObtainBatchId != 0 || r.Before != r.After))
            throw new InvalidOperationException("Weiwan's count must be actual primary entities entering its owner's Hand, excluding destruction or spill.");
        for (var i = 0; i < selected.Length; i++)
        {
            var s = selected[i]; var m = moves[i];
            if (m.Index != s.Index || m.CardId != s.CardId || m.PrintedKind != s.PrintedKind || m.From != s.From || m.BatchId != r.ObtainBatchId ||
                m.MovementSequence <= r.Before || m.MovementSequence > r.After ||
                _cardMovements.Count(x => x.Sequence == m.MovementSequence && x.CardId == m.CardId && x.CardKind == m.PrintedKind &&
                    x.From == m.From && x.To == m.To && x.Reason.Value == WeiwanObtainReason) != 1)
                throw new InvalidOperationException("Weiwan lost the real destination or single payment of a sampled entity.");
        }
        if (r.Branch is { } branch && branch != (r.ActualCount switch { 1 => WeiwanBranch.LoseHp, 2 => WeiwanBranch.UnlimitedUse, 3 => WeiwanBranch.ForbidUse, _ => WeiwanBranch.None }) ||
            r.Stage is WeiwanStage.HealthIssuing or WeiwanStage.HealthChildren && r.Branch is null ||
            r.HpLossCaptured && (r.Branch != WeiwanBranch.LoseHp || r.Stage != WeiwanStage.HealthChildren ||
                ProgramEventHistory<WeiwanHealthAppliedEvent>().Count(e => e.FrameId == f.Id && e.TargetSeat == r.TargetSeat &&
                    e.Amount == (r.AppearancePrevented ? 0 : 1) && e.PreventedByAppearance == r.AppearancePrevented) != 1))
            throw new InvalidOperationException("Weiwan repeated or changed the branch of its exact actual obtained count.");
    }
    private bool IsWeiwanMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.Weiwan is { } r && HasPaidWeiwanSelection(f) && effect?.Op == SkillProgramEffectOp.ResolveWeiwanQinDiscardAndSuitObtain &&
        pending == f.PendingMovementContinuation && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        pending.SubjectSeat == (r.Stage is WeiwanStage.CostChildren or WeiwanStage.ObtainChildren ? f.OwnerSeat : r.TargetSeat);
    private bool WeiwanPrimaryMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        var r = f.Weiwan!;
        if (!_cardMovements.Contains(m)) return false;
        return r.Stage == WeiwanStage.CostChildren ? m.Sequence == r.CostSequence && m.CardId == r.CardId &&
            m.From == r.CostFrom && m.To == r.CostTo && m.Reason.Value == WeiwanCostReason :
            r.Stage == WeiwanStage.ObtainChildren && ProgramEventHistory<WeiwanEntityObtainedEvent>().Any(e => e.FrameId == f.Id &&
                e.CardId == m.CardId && e.MovementSequence == m.Sequence && e.From == m.From && e.To == m.To && m.Reason.Value == WeiwanObtainReason);
    }
    private bool WeiwanOxSpill(ProgramSkillFrame f, CardMovementBatchContext batch)
    {
        var r = f.Weiwan!;
        var originalBatch = r.Stage == WeiwanStage.CostChildren ? r.CostBatchId : r.Stage == WeiwanStage.ObtainChildren ? r.ObtainBatchId : 0;
        if (originalBatch <= 0 || batch.ParentBatchId != originalBatch || batch.Movements.Count == 0) return false;
        var oxOwners = r.Stage == WeiwanStage.CostChildren
            ? r.PrintedKind == CardKind.WoodenOx && r.CostFrom.Zone == CardZoneKind.Equipment ? new[] { f.OwnerSeat } : []
            : r.Samples.Where(s => s.PrintedKind == CardKind.WoodenOx && s.From is { Zone: CardZoneKind.Equipment }).Select(s => s.From!.Value.OwnerSeat!.Value).ToArray();
        return batch.Movements.All(m => m.From is { Zone: CardZoneKind.WoodenOxGrain, OwnerSeat: { } owner } && oxOwners.Contains(owner) &&
            m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard && _cardMovements.Contains(m));
    }
}
