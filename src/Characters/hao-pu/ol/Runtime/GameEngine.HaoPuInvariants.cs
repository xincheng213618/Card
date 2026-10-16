namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int[] ZhenyingHandAt(long sequence, int seat) => _cardMovements.Where(m => m.Sequence <= sequence && m.From != m.To)
        .GroupBy(m => m.CardId).Select(g => g.MaxBy(m => m.Sequence)!).Where(m => m.To == CardLocation.Hand(seat)).Select(m => m.CardId).Order().ToArray();
    private bool ValidZhenying(ProgramSkillFrame f)
    {
        if (f.Zhenying is not { } r || f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex != 1 || r.InstructionIndex != 1 ||
            r.Source != ZhenyingSource(f) || r.GameplayHash != f.GameplayHash || r.ActualTurn != _turnNumber || r.PhaseInstanceId != _cardUseDebitPhaseInstanceId ||
            f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var other] || other != r.OtherSeat ||
            other == f.OwnerSeat || !IsValidPlayerSeat(other) || !Enum.IsDefined(r.Stage) || r.SelectionIndex is < 0 or > 1 || r.SlotIndex is < 0 or > 1 ||
            r.OwnerPlan.Seat != f.OwnerSeat || r.OtherPlan.Seat != other || r.OtherPlan.OriginalIds.Count > r.OwnerPlan.OriginalIds.Count ||
            r.StartMovementSequence < 0 || r.StartMovementSequence > _movementSequence ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveZhenyingSimultaneousHands ||
            CompleteProgramEventHistory().OfType<ZhenyingStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash &&
                e.ActualTurn == r.ActualTurn && e.PhaseInstanceId == r.PhaseInstanceId && e.OtherSeat == other && e.MovementSequence == r.StartMovementSequence &&
                e.OwnerHandCount == r.OwnerPlan.OriginalIds.Count && e.OtherHandCount == r.OtherPlan.OriginalIds.Count) != 1) return false;
        var selecting = r.Stage is ZhenyingStage.ChoosingCount or ZhenyingStage.ChoosingKeptCards;
        foreach (var p in new[] { r.OwnerPlan, r.OtherPlan })
        {
            if (p.OriginalIds.Distinct().Count() != p.OriginalIds.Count || !p.OriginalIds.Order().SequenceEqual(ZhenyingHandAt(r.StartMovementSequence, p.Seat)) ||
                p.ProtectedIds is null || p.ProtectedIds.Distinct().Count() != p.ProtectedIds.Count || p.ProtectedIds.Except(p.OriginalIds).Any() ||
                p.DesiredCount is < 0 or > 2 || p.KeptIds?.Distinct().Count() != p.KeptIds?.Count ||
                p.KeptIds is { } kept && (kept.Except(p.OriginalIds).Any() || p.ProtectedIds.Except(kept).Any()) ||
                p.DesiredCount is null && (p.Committed || p.KeptIds is not null) || p.DesiredCount is not null && p.KeptIds is null ||
                p.Committed && p.KeptIds!.Count != ZhenyingKeptCount(p) ||
                CompleteProgramEventHistory().OfType<ZhenyingChoiceCommittedEvent>().Count(e => e.FrameId == f.Id && e.Seat == p.Seat) != (p.Committed ? 1 : 0)) return false;
            if (selecting && !GetHand(_players[p.Seat]).Select(c => c.Id).Order().SequenceEqual(p.OriginalIds.Order())) return false;
        }
        if (selecting)
            return f.PendingMovementContinuation is null && r.PaymentBefore == 0 && r.PaymentAfter == 0 && r.Duel is null &&
                r.OwnerDrawCount == 0 && r.OtherDrawCount == 0 && (r.SelectionIndex == 0 ? !r.OwnerPlan.Committed && !r.OtherPlan.Committed : r.OwnerPlan.Committed && !r.OtherPlan.Committed) &&
                (r.Stage == ZhenyingStage.ChoosingCount ? r.SlotIndex == 0 && ZhenyingPlan(r).DesiredCount is null : ZhenyingPlan(r).DesiredCount is not null &&
                    ZhenyingPlan(r).KeptIds!.Count <= ZhenyingKeptCount(ZhenyingPlan(r)) &&
                    ZhenyingKeptCount(ZhenyingPlan(r)) - ZhenyingPlan(r).KeptIds!.Count <= 2 - r.SlotIndex &&
                    ZhenyingPlan(r).KeptIds!.Count - (ZhenyingPlan(r).OriginalIds.Count <= ZhenyingPlan(r).DesiredCount!.Value ? ZhenyingPlan(r).OriginalIds.Count : ZhenyingPlan(r).ProtectedIds!.Count) <= r.SlotIndex);
        if (!r.OwnerPlan.Committed || !r.OtherPlan.Committed || r.SelectionIndex != 1 || r.SlotIndex != 1 || r.PaymentBefore < r.StartMovementSequence || r.PaymentAfter < r.PaymentBefore || r.PaymentAfter > _movementSequence ||
            r.OwnerDrawCount < 0 || r.OtherDrawCount < 0 || r.OwnerDrawCount > Math.Max(0, r.OwnerPlan.DesiredCount!.Value - r.OwnerPlan.OriginalIds.Count) ||
            r.OtherDrawCount > Math.Max(0, r.OtherPlan.DesiredCount!.Value - r.OtherPlan.OriginalIds.Count)) return false;
        var paid = CompleteProgramEventHistory().OfType<ZhenyingAdjustmentPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (paid is not [var invoice] || invoice.Before != r.PaymentBefore || invoice.After != r.PaymentAfter ||
            invoice.OwnerDesiredCount != r.OwnerPlan.DesiredCount || invoice.OtherDesiredCount != r.OtherPlan.DesiredCount ||
            invoice.OwnerDrawCount != r.OwnerDrawCount || invoice.OtherDrawCount != r.OtherDrawCount ||
            invoice.OwnerActualCount != r.OwnerPlan.KeptIds!.Count + r.OwnerDrawCount || invoice.OtherActualCount != r.OtherPlan.KeptIds!.Count + r.OtherDrawCount) return false;
        foreach (var p in new[] { r.OwnerPlan, r.OtherPlan })
        {
            var moves = _cardMovements.Where(m => m.Sequence > r.PaymentBefore && m.Sequence <= r.PaymentAfter).ToArray();
            var discarded = moves.Where(m => m.From == CardLocation.Hand(p.Seat) && m.To == CardLocation.DiscardPile && m.Reason.Value == ZhenyingDiscardReason).ToArray();
            if (!discarded.Select(m => m.CardId).Order().SequenceEqual(p.OriginalIds.Except(p.KeptIds!).Order()) ||
                moves.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(p.Seat) && m.Reason.Value == ZhenyingDrawReason) !=
                    (p.Seat == f.OwnerSeat ? r.OwnerDrawCount : r.OtherDrawCount)) return false;
        }
        return r.Stage == ZhenyingStage.AdjustmentChildren ? r.Duel is null && f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } m && m.SubjectSeat == f.OwnerSeat :
            f.PendingMovementContinuation is null && r.Duel is { } d && d.ProgramFrameId == f.Id && d.InstructionIndex == r.InstructionIndex && d.Source == r.Source &&
            d.GameplayHash == r.GameplayHash && d.ActualTurn == r.ActualTurn && d.PhaseInstanceId == r.PhaseInstanceId && d.CardUseFrameId > f.Id &&
            d.OwnerCount >= 0 && d.OtherCount >= 0 && d.OwnerCount != d.OtherCount &&
            d.ActorSeat == (d.OwnerCount < d.OtherCount ? f.OwnerSeat : other) && d.TargetSeat == (d.ActorSeat == f.OwnerSeat ? other : f.OwnerSeat) &&
            CompleteProgramEventHistory().OfType<ZhenyingDuelIssuedEvent>().Count(e => e.Origin == d) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26500)]
    private void RequireZhenying(ProgramSkillFrame f)
    {
        if (f.Zhenying is null) return;
        if (!ValidZhenying(f)) throw new InvalidOperationException("Zhenying lost its exact source, simultaneous sealed hands, payment ledger or native Duel receipt.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && f.Zhenying.Stage is ZhenyingStage.ChoosingCount or ZhenyingStage.ChoosingKeptCards &&
            (_pendingDecision is not { IsPrivate: true } p || p.PlayerSeat != ZhenyingPlan(f.Zhenying).Seat || !AssistedChoicesEqual(p.Choices, ZhenyingChoices(f))))
            throw new InvalidOperationException("Zhenying lost its private chooser-owned unissued hand plan.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26500)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26500)]
    private bool ContinuePaidZhenying(ProgramSkillFrame f) => f.Zhenying is { Stage: ZhenyingStage.AdjustmentChildren or ZhenyingStage.DuelIssued } && ValidZhenying(f);
}
