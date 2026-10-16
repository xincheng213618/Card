namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsFuRongAwaitedMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation pending) =>
        e?.Op == SkillProgramEffectOp.DiscardBasicPairAndReuse && f.FuRongPair is
            { Stage: FuRongPairStage.OwnerChildren or FuRongPairStage.TargetChildren or FuRongPairStage.DrawChildren or FuRongPairStage.NativeChildren } r &&
        pending == f.PendingMovementContinuation && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        pending.SubjectSeat == (r.Stage == FuRongPairStage.TargetChildren ? r.TargetSeat : f.OwnerSeat);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26310)]
    private void AssertFuRongPair(ProgramSkillFrame f)
    {
        if (f.FuRongPair is not { } r) return;
        var p = _contentRegistry.GetSkill(f.SkillId).Program!;
        var plan = ProgramInstructionResolver.Default.Resolve(f, p);
        if (r.Source != FuRongSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex || r.InstructionIndex != 1 ||
            f.TriggerId is not null || f.WindowContext is not null || f.SelectedCardIds.Count != 0 ||
            f.SelectedTargetSeats is not [var selected] || selected != r.TargetSeat || !IsValidPlayerSeat(selected) || selected == f.OwnerSeat ||
            !Enum.IsDefined(r.Stage) || r.TurnNumber != _turnNumber || r.PhaseInstanceId != _cardUseDebitPhaseInstanceId ||
            _currentSeat != f.OwnerSeat || _phase != TurnPhase.Play || p.GameplayHash != r.GameplayHash ||
            plan.Activation?.UsageGroup != r.UsageGroup || plan.Instructions is not [{ Op: SkillProgramEffectOp.DiscardBasicPairAndReuse }] ||
            _programPhaseUses.GetValueOrDefault((f.OwnerSeat, f.SkillId, r.UsageGroup)) != 1 ||
            CompleteProgramEventHistory().OfType<FuRongPairStartedEvent>().Count(e => e.ProgramFrameId == f.Id &&
                e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.UsageGroup == r.UsageGroup &&
                e.TargetSeat == r.TargetSeat && e.TurnNumber == r.TurnNumber && e.PhaseInstanceId == r.PhaseInstanceId) != 1)
            throw new InvalidOperationException("Xiaosi lost its immutable source, original target, phase debit or sole owning instruction.");
        var paid = CompleteProgramEventHistory().OfType<FuRongPairPaidEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        void Payment(FuRongPayment? payment, bool owner)
        {
            var entries = paid.Where(e => e.Owner == owner).ToArray();
            if (payment is null) { if (entries.Length != 0) throw new InvalidOperationException("Unpaid Xiaosi contains a payment fact."); return; }
            if (entries is not [var e] || e.Payment != payment || payment.Seat != (owner ? f.OwnerSeat : r.TargetSeat) || payment.CardId <= 0 ||
                payment.From.OwnerSeat != payment.Seat || payment.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                !MatchesSkillProgramCardCategory(payment.EffectiveKind, SkillProgramCardCategory.Basic) || payment.Sequence <= 0 ||
                _cardMovements.Count(m => m.Sequence == payment.Sequence && m.CardId == payment.CardId && m.CardKind == payment.PrintedKind &&
                    m.From == payment.From && m.To == CardLocation.DiscardPile && m.Reason.Value == FuRongCostReason) != 1)
                throw new InvalidOperationException("Xiaosi lost a once-paid actual basic discard and its original physical entity.");
        }
        Payment(r.OwnerPayment, true); Payment(r.TargetPayment, false);
        if (r.Stage == FuRongPairStage.OwnerChoice && (r.OwnerPayment is not null || r.TargetPayment is not null || r.DrawIssued || r.TargetUnable) ||
            r.Stage != FuRongPairStage.OwnerChoice && r.OwnerPayment is null ||
            r.TargetPayment is not null && r.TargetUnable || r.DrawIssued != r.TargetUnable || r.ActualDraw is < 0 or > 1 ||
            !r.DrawIssued && r.ActualDraw != 0 ||
            r.UsedCardIds.Count > 2 || r.UsedCardIds.Distinct().Count() != r.UsedCardIds.Count ||
            r.UsedCardIds.Any(id => id != r.OwnerPayment?.CardId && id != r.TargetPayment?.CardId))
            throw new InvalidOperationException("Xiaosi has an inconsistent paid/unpayable branch or reused material set.");
        var unable = CompleteProgramEventHistory().OfType<FuRongTargetUnableEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        var draws = CompleteProgramEventHistory().OfType<FuRongCompensationDrawEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if ((r.TargetUnable ? unable is not [var u] || u.TargetSeat != r.TargetSeat : unable.Length != 0) ||
            (r.DrawIssued ? draws is not [var d] || d.OwnerSeat != f.OwnerSeat || d.ActualCount != r.ActualDraw : draws.Length != 0))
            throw new InvalidOperationException("Xiaosi's compensation must be a single real draw for the unpayable participant.");
        var uses = CompleteProgramEventHistory().OfType<FuRongNativeIssuedEvent>().Where(e => e.Return.ProgramFrameId == f.Id).ToArray();
        if (uses.Length != r.UsedCardIds.Count || uses.Select(e => e.Return.CardId).Distinct().Count() != uses.Length ||
            !uses.Select(e => e.Return.CardId).Order().SequenceEqual(r.UsedCardIds.Order()) ||
            uses.Any(e => e.Return.CardUseFrameId <= f.Id || e.Return.ActionId <= 0))
            throw new InvalidOperationException("Xiaosi's native action history does not match its once-spent physical entities.");
        if (r.Stage is FuRongPairStage.NativeUse or FuRongPairStage.NativeChildren)
        {
            if (r.NativeReturn is not { } ret || ret.ProgramFrameId != f.Id || ret.CardUseFrameId != r.ReservedUseId ||
                ret.CardId != r.SelectedUseCardId || !r.UsedCardIds.Contains(ret.CardId) || uses.Count(e => e.Return == ret) != 1)
                throw new InvalidOperationException("Xiaosi lost its typed native return.");
            if (r.Stage == FuRongPairStage.NativeUse && !r.NativeCompleted && LifecycleCardUse(ret.CardUseFrameId) is not
                { FuRongReturn: var nativeReturn, Action: { } action } ||
                r.Stage == FuRongPairStage.NativeUse && !r.NativeCompleted &&
                (LifecycleCardUse(ret.CardUseFrameId)!.FuRongReturn != ret || LifecycleCardUse(ret.CardUseFrameId)!.Action!.ActionId != ret.ActionId))
                throw new InvalidOperationException("Xiaosi's outstanding child is not its exact issued native use.");
            if (r.Stage == FuRongPairStage.NativeChildren && (!r.NativeCompleted || LifecycleCardUse(ret.CardUseFrameId) is not null ||
                CompleteProgramEventHistory().OfType<FuRongNativeReturnedEvent>().Count(e => e.Return == ret) != 1))
                throw new InvalidOperationException("Xiaosi resumed before its whole native card use returned.");
        }
        if (f.PendingMovementContinuation is { } pending && !IsFuRongAwaitedMovement(f, plan.Instructions[0], pending))
            throw new InvalidOperationException("Xiaosi movement must return to its exact paid owning stage and participant.");
    }
}
