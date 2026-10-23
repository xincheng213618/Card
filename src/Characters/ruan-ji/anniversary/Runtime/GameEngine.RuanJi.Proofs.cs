namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 42800)]
    private void RequireRuanJi(ProgramSkillFrame f)
    {
        if (f.RuanJi is not { } r) return;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (r.Source != RuanJiSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex ||
            r.InstructionIndex != 1 || !AnniversaryRuanJiComposition.Own(r.Operation) || !Enum.IsDefined(r.Stage) ||
            r.ActualTurn != _turnNumber || r.ParentId != f.WindowContext?.ParentFrameId || index < 1 || _resolutionStack[index - 1].Id != r.ParentId ||
            ProgramEventHistory<RuanJiStartedEvent>().Count(e => e == new RuanJiStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation,
                r.ParentId, r.ActualTurn, r.UseId, r.ActionId, r.WasDrunk)) != 1)
            throw new InvalidOperationException("Ruan Ji lost his original owning frame, source, actual turn or issued continuation.");
        if (r.Operation == SkillProgramEffectOp.ResolveAnniversaryRuanJiZhaowen)
        {
            if (_resolutionStack[index - 1] is not PlayPhaseStartingBoundaryFrame p || p.OwnerSeat != f.OwnerSeat ||
                p.ItemIndex < 0 || p.ItemIndex >= p.Items.Count || p.Items[p.ItemIndex].Candidate is not { } candidate ||
                !MountObserverCandidateMatches(f, candidate) || r.Hand.Count == 0 || r.Hand.Select(c => c.Id).Distinct().Count() != r.Hand.Count ||
                r.Hand.Any(c => c.Id <= 0) || r.Stage != RuanJiStage.Revealing)
                throw new InvalidOperationException("Zhaowen lost its frozen whole-hand reveal and actual Play boundary.");
            return;
        }
        if (_resolutionStack[index - 1] is not ProgramCardTriggerWindowFrame window || window.Action.ActionId != r.ActionId ||
            window.ParentFrameId != r.UseId || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, ToSharedCandidate(window.Candidates[window.CandidateIndex])))
            throw new InvalidOperationException("Ruan Ji lost his original native card action window and exact issued candidate.");
        if (r.Operation == SkillProgramEffectOp.DrawAnniversaryRuanJiShownRedUse)
        {
            if (GetCardActionWindow(window) != SkillProgramTriggerWindow.CardUseCommitted ||
                !HasRuanJiShownRedUse(f.OwnerSeat, f.SkillInstanceId, window.Action) || r.WasDrunk)
                throw new InvalidOperationException("Zhaowen's red reward lost its actual original shown-entity Use.");
        }
        else if (GetCardActionWindow(window) != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            window.Action.Type != CardActionType.Use || window.Action.ActorSeat == f.OwnerSeat || window.Action.EffectiveIsRed != false ||
            !window.Action.EffectiveDesignatedTargetSeats.Contains(f.OwnerSeat) || LifecycleCardUse(r.UseId)?.Action?.ActionId != r.ActionId ||
            r.WasDrunk && r.Stage is RuanJiStage.Drawing or RuanJiStage.DrawChildren or RuanJiStage.WineIssued ||
            !r.WasDrunk && r.Stage is RuanJiStage.DiscardChoice or RuanJiStage.DiscardChildren)
            throw new InvalidOperationException("Jiudun lost its frozen other-owner black target or original sober/drunk branch.");
        if (r.Draw is { } draw && (r.Stage is not (RuanJiStage.DrawChildren or RuanJiStage.WineIssued) || draw.RecipientSeat != f.OwnerSeat || draw.RequestedCount != 1 ||
            ProgramEventHistory<RuanJiDrawReturnedEvent>().Count(e => e == new RuanJiDrawReturnedEvent(f.Id, draw.AttemptFrameId,
                draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter)) != 1))
            throw new InvalidOperationException("Ruan Ji lost his already-returned exact native Draw1 receipt.");
        if (r.Stage == RuanJiStage.DiscardChildren && !RuanJiDiscardPaid(f, r))
            throw new InvalidOperationException("Jiudun lost its already-paid real logical hand discard.");
        if (r.Stage == RuanJiStage.WineIssued && (r.WineId is not { } wine || r.WineActionId is not { } action ||
            ProgramEventHistory<RuanJiWineIssuedEvent>().Count(e => e == new RuanJiWineIssuedEvent(f.Id, wine, action, r.UseId)) != 1))
            throw new InvalidOperationException("Jiudun lost the exact issued zero-entity Alcohol Use.");
    }
    private bool RuanJiDiscardPaid(ProgramSkillFrame f, RuanJiReceipt r) => r.DiscardId > 0 && r.BatchId is { } batch &&
        r.After > r.Before && r.After <= _movementSequence &&
        ProgramEventHistory<RuanJiDiscardPaidEvent>().Count(e => e == new RuanJiDiscardPaidEvent(f.Id, r.DiscardId, batch, r.Before, r.After)) == 1 &&
        _cardMovements.Count(m => m.CardId == r.DiscardId && m.Sequence == r.After && m.From == CardLocation.Hand(f.OwnerSeat) &&
            m.Reason.Value == "skill-program.anniversary:jiudun.discard" &&
            (m.To == CardLocation.DiscardPile || IsRedirectedPaidDiscardMovement(m, CardLocation.Hand(f.OwnerSeat), new("skill-program.anniversary:jiudun.discard")))) == 1;
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 42800)]
    private bool RuanJiMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.RuanJi is { Operation: SkillProgramEffectOp.ResolveAnniversaryRuanJiJiudun, Stage: RuanJiStage.DiscardChildren } r &&
        effect?.Op == r.Operation && f.PendingMovementContinuation == pending && pending.SubjectSeat == f.OwnerSeat &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && RuanJiDiscardPaid(f, r);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 42800)]
    private bool ContinuePaidRuanJiAfterSkillLoss(ProgramSkillFrame f)
    { if (f.RuanJi is null) return false; RequireRuanJi(f); return true; }
    private bool IsExactRuanJiVirtualBasicDraft(ProgramSkillFrame f, ProgramVirtualBasicDraft draft)
    {
        if (f.RuanJi is not { Operation: SkillProgramEffectOp.ResolveAnniversaryRuanJiJiudun, Stage: RuanJiStage.WineIssued, WasDrunk: false } r ||
            r.WineId != draft.ChildFrameId || r.InstructionIndex != draft.InstructionIndex || f.InstructionIndex != draft.InstructionIndex ||
            r.Source != RuanJiSource(f) || r.GameplayHash != f.GameplayHash || r.Draw is null) return false;
        RequireRuanJi(f); return true;
    }
    [NativeUseValidation(42800)]
    private void ValidateRuanJiWine(CardUseFrame use)
    {
        if (use.VirtualBasicReturn is not { } returned || _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == returned.ParentFrameId)
            is not { RuanJi: { Stage: RuanJiStage.WineIssued } r } parent) return;
        RequireRuanJi(parent);
        if (use.Id != r.WineId || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } action ||
            action.ActionId != r.WineActionId || action.ActorSeat != parent.OwnerSeat || action.ProviderSeat != parent.OwnerSeat ||
            action.ParentActionId != r.ActionId || !action.ConversionChain.SequenceEqual([r.Source]) || use.CardId != 0 ||
            use.CardKind != CardKind.Alcohol || use.PhysicalCardIds is not { Count: 0 } || !use.TargetSeats.SequenceEqual([parent.OwnerSeat]) ||
            returned.InstructionIndex != r.InstructionIndex || use.Step != ResolutionFrameStep.Completed)
            throw new InvalidOperationException("Jiudun cannot replace the exact native Alcohol Use or its original typed parent return.");
    }
}
