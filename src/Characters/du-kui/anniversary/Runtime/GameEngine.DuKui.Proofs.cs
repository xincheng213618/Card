namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidDuKuiFrame(ProgramSkillFrame f)
    {
        if (f.DuKui is not { } r || f.WindowContext is not { } w || f.InstructionIndex != 1 || r.Source != DuKuiSource(f) || r.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || r.ActualTurn != _turnNumber || r.ParentId != w.ParentFrameId ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<DuKuiStartedEvent>().Count(e => e == new DuKuiStartedEvent(f.Id, r.Source, r.GameplayHash, r.ParentId, r.ActualTurn, r.Operation)) != 1)
            return false;
        var index = _resolutionStack.FindIndex(p => p.Id == f.Id);
        if (index < 1) return false;
        if (r.Operation == SkillProgramEffectOp.ResolveAnniversaryDuKuiFanyin)
        {
            if (f.SkillId != AnniversaryDuKuiComposition.Fanyin || w.Window != SkillProgramTriggerWindow.PlayPhaseStarting || f.OwnerSeat != _currentSeat ||
                _resolutionStack[index - 1] is not PlayPhaseStartingBoundaryFrame p || p.Id != r.ParentId || p.OwnerSeat != f.OwnerSeat ||
                p.ItemIndex < 0 || p.ItemIndex >= p.Items.Count || p.Items[p.ItemIndex].Candidate is not { } c || !MountObserverCandidateMatches(f, c) ||
                r.Iteration is < 0 or > 4 || r.Moves != 0 || r.Iteration > 0 && (r.Rank is < 1 or > 13 || r.CardId <= 0) ||
                r.Stage is DuKuiStage.MoveChoice or DuKuiStage.MoveChildren) return false;
        }
        else if (r.Operation != SkillProgramEffectOp.ResolveAnniversaryDuKuiPeiqi || f.SkillId != AnniversaryDuKuiComposition.Peiqi ||
            w.Window != SkillProgramTriggerWindow.AfterDamageApplied || w.TargetSeat != f.OwnerSeat ||
            _resolutionStack[index - 1] is not DamageTriggerWindowFrame d || d.Id != r.ParentId || d.TargetSeat != f.OwnerSeat ||
            d.CandidateIndex < 0 || d.CandidateIndex >= d.Candidates.Count || !MountObserverCandidateMatches(f, d.Candidates[d.CandidateIndex].ToProgramCandidate()) ||
            r.Moves is < 0 or > 2 || r.Stage is not (DuKuiStage.MoveChoice or DuKuiStage.MoveChildren)) return false;
        return true;
    }
    private bool DuKuiShownPaid(ProgramSkillFrame f)
    {
        if (f.DuKui is not { } r || r.Operation != SkillProgramEffectOp.ResolveAnniversaryDuKuiFanyin || r.Iteration < 1 ||
            ProgramEventHistory<DuKuiRevealedEvent>().Where(e => e.FrameId == f.Id && e.Iteration == r.Iteration).ToArray() is not [var shown] ||
            shown.CardId != r.CardId || shown.Rank != r.Rank || shown.Sequence <= 0 || shown.Sequence > _movementSequence ||
            !GetProgramCardSet(f, DuKuiShownBind(r)).CardIds.SequenceEqual([r.CardId])) return false;
        return _cardMovements.Count(m => m.Sequence == shown.Sequence && m.CardId == r.CardId && m.From == CardLocation.DrawPile &&
            m.To == CardLocation.Processing && m.Reason.Value == "anniversary.fanyin.reveal") == 1;
    }
    private bool DuKuiMovementPaid(ProgramSkillFrame f)
    {
        if (f.DuKui is not { } r || r.Stage is not (DuKuiStage.RevealMovement or DuKuiStage.DeclineMovement or DuKuiStage.MoveChildren) ||
            r.From is not { } from || r.To is not { } to || r.MovementSequence <= r.Before || r.MovementSequence > r.After) return false;
        var reason = r.Stage switch { DuKuiStage.RevealMovement => "anniversary.fanyin.reveal", DuKuiStage.DeclineMovement => "anniversary.fanyin.unused", _ => "anniversary.peiqi.move" };
        return _cardMovements.Count(m => m.Sequence == r.MovementSequence && m.CardId == r.CardId && m.From == from && m.To == to && m.Reason.Value == reason) == 1;
    }
    private bool DuKuiNativeBatchPaid(ProgramSkillFrame f, CardMovementBatchContext b) => f.DuKui is { Stage: DuKuiStage.NativeTail, NativeReturned: true } r &&
        r.NativeBatches.Contains(b.Id) && b.ParentFrameId == r.UseId && b.AwaitingProgramFrameId is null && b.OriginOwnerSeat == f.OwnerSeat &&
        b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After) &&
        ProgramEventHistory<DuKuiNativeBatchEvent>().Count(e => e == new DuKuiNativeBatchEvent(f.Id, r.UseId, b.Id, b.Movements.Count)) == 1;

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 43200)]
    private void RequireDuKui(ProgramSkillFrame f)
    {
        if (f.DuKui is not { } r) return;
        if (!ValidDuKuiFrame(f) || r.Operation == SkillProgramEffectOp.ResolveAnniversaryDuKuiFanyin && r.Iteration > 0 && !DuKuiShownPaid(f))
            throw new InvalidOperationException("Du Kui lost his original candidate, source/hash, actual turn or publicly revealed physical entity receipt.");
        if (r.Stage is DuKuiStage.NativeUse or DuKuiStage.NativeTail &&
            (ProgramEventHistory<DuKuiNativeIssuedEvent>().Count(e => e.Origin.FrameId == f.Id && e.Origin.Iteration == r.Iteration && e.Origin.CardId == r.CardId &&
                e.Origin.UseId == r.UseId && e.Origin.ActionId == r.ActionId && e.Origin.Source == r.Source && e.Origin.GameplayHash == r.GameplayHash) != 1 ||
             r.NativeReturned && ProgramEventHistory<DuKuiNativeReturnedEvent>().Count(e => e == new DuKuiNativeReturnedEvent(f.Id, r.UseId, r.ActionId)) != 1))
            throw new InvalidOperationException("Fanyin lost its exact issued and returned native use.");
        if (f.PendingMovementContinuation is { } pending && !DuKuiMovementProof(f, GetPausedPrivateOfferEffect(f), pending))
            throw new InvalidOperationException("Du Kui lost its actual paid movement or original native-use tail.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 43200)]
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 43200)]
    private bool DuKuiMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.DuKui is { } r && ValidDuKuiFrame(f) && effect?.Op == r.Operation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        (DuKuiMovementPaid(f) || r.Stage == DuKuiStage.NativeTail && r.NativeReturned &&
         ProgramEventHistory<DuKuiNativeReturnedEvent>().Count(e => e == new DuKuiNativeReturnedEvent(f.Id, r.UseId, r.ActionId)) == 1 &&
         _pendingCardsMovedBatches.Where(b => r.NativeBatches.Contains(b.Id)).All(b => DuKuiNativeBatchPaid(f, b)));

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 43200)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 43200)]
    private bool ContinuePaidDuKui(ProgramSkillFrame f) => f.DuKui is not null && ValidDuKuiFrame(f);
    private bool HasDuKuiPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { DuKui: { Stage: DuKuiStage.Choice or DuKuiStage.MoveChoice }, Step: ResolutionFrameStep.AwaitingResponse } f &&
        p.PlayerSeat == f.OwnerSeat && ValidDuKuiFrame(f) && AssistedChoicesEqual(p.Choices, DuKuiChoices(f));

    [ProgramCardUsePayment(ProgramCardUsePaymentStage.ProcessingAllowance, 43200)]
    private bool DuKuiRevealedProcessing(IReadOnlyList<Card> cards) => cards.Count == 1 &&
        _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(p => p.DuKui is not null) is { DuKui: { } r } f &&
        r.Operation == SkillProgramEffectOp.ResolveAnniversaryDuKuiFanyin && cards[0].Id == r.CardId && ValidDuKuiFrame(f) && DuKuiShownPaid(f);
}
