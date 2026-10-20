namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void BindWuJingHejiUse(long id)
    {
        var use = LifecycleCardUse(id); var at = _resolutionStack.FindIndex(frame => frame.Id == id);
        if (use is null || at < 1 || _resolutionStack[at - 1] is not ProgramSkillFrame { WuJing: { Stage: WuJingStage.IssuingUse } r } f) return;
        if (r.Operation != SkillProgramEffectOp.UseHejiAtCompletedSingleTarget || r.NativeOrigin is not null || r.Selection is not { } selected || use.Action is not { Type: CardActionType.Use } a ||
            a.ActorSeat != f.OwnerSeat || use.CardKind != selected.Kind || !use.TargetSeats.SequenceEqual([r.TargetSeat]) || !a.PhysicalCards.Select(c => c.CardId).SequenceEqual(selected.CardIds) ||
            r.FactionPolicy is null && !a.ConversionChain.SequenceEqual(selected.Conversions) || a.PhysicalCards.Count == 0 && !WuJingZeroPaymentProof(f, r) || a.ParentActionId != r.ActionId)
            throw new InvalidOperationException("Heji must bind one exact native Slash/Duel, original target and actually chosen physical conversion.");
        if (LifecycleCardUse(r.UseId) is { CardKind: CardKind.Duel, Continuations.Duel: { Active: true } } completedDuel)
        {
            if (at < 3 || !ReferenceEquals(_resolutionStack[at - 3], completedDuel) || completedDuel.Step != ResolutionFrameStep.Completed || completedDuel.Action?.ActionId != r.ActionId ||
                !completedDuel.TargetSeats.SequenceEqual([r.TargetSeat]) || f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } completed } || completed.ParentCardUseFrameId != completedDuel.Id || completed.CardActionId != r.ActionId ||
                _resolutionStack[at - 2] is not ProgramCardTriggerWindowFrame window || window.Id != r.WindowId || window.ParentFrameId != completedDuel.Id || window.AttackOwnerFrameId != completedDuel.Id || window.Action.ActionId != r.ActionId || window.Continuation != ProgramCardContinuation.CompletedSlash ||
                window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(f, ToSharedCandidate(window.Candidates[window.CandidateIndex])) ||
                ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == completedDuel.Id && e.CardId == completedDuel.CardId && e.CardKind == CardKind.Duel) != 1)
                throw new InvalidOperationException("Heji may retire only its exact already-finished Duel response continuation.");
            UpdateCardContinuations(completedDuel.Id, state => state with { Duel = state.Duel! with { Active = false } });
        }
        var material = a.PhysicalCards.Select(c => new WuJingMaterial(c.CardId, c.CardKind, c.From)).ToArray();
        var reward = material is [var one] && one.PrintedKind == a.EffectiveKind && a.ConversionChain.Count == 0 && !selected.Zhangba && !selected.UsesFan;
        var origin = new WuJingHejiOrigin(f.Id, id, a.ActionId, r.UseId, r.ActionId, r.Source, r.GameplayHash, r.ActualTurn, f.OwnerSeat, r.TargetSeat, selected.Kind, reward) { Materials = material, Conversions = a.ConversionChain };
        ReplaceRuntimeFrame(f.Id, f with { WuJing = r with { Stage = WuJingStage.NativeUse, NativeOrigin = origin } });
        UpdateLifecycleCardUse(id, u => u with { WuJingHeji = new(origin) });
        AdvanceEventRulesAndQueueFact(new WuJingHejiNativeIssuedEvent(f.Id, id, a.ActionId, r.UseId, r.ActionId, r.Source, r.GameplayHash, r.ActualTurn, f.OwnerSeat, r.TargetSeat, origin.Kind, material.Length, reward));
        for (var i = 0; i < material.Length; i++) AdvanceEventRulesAndQueueFact(new WuJingHejiMaterialIssuedEvent(f.Id, id, i, material[i]));
        if (material.Length == 0) AdvanceEventRulesAndQueueFact(new WuJingHejiPaidEvent(f.Id, id, r.Before, r.After));
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashPaymentCompleted, 50)]
    private void CompleteWuJingHejiPayment(long id)
    {
        if (LifecycleCardUse(id) is not { WuJingHeji: { Origin: { } origin } } use) return;
        var f = _resolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == origin.ProgramFrameId); var r = f.WuJing!;
        if (r.Stage != WuJingStage.NativeUse || r.NativeOrigin != origin || ProgramEventHistory<WuJingHejiPaidEvent>().Any(e => e.UseId == id)) throw new InvalidOperationException("Heji cannot pay one accepted native Use twice.");
        foreach (var entity in origin.Materials)
        {
            var moves = _cardMovements.Where(m => m.Sequence > r.Before && m.CardId == entity.CardId && m.From == entity.From && m.Reason == CardMoveReasons.Use).ToArray();
            var expected = GetAdvancedCard(entity.CardId).IsGeneralWeapon && entity.From.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : IsProgramAlternativeCost(use.Action!, entity.CardId) ? CardLocation.DrawPile : CardLocation.Processing;
            if (moves is not [var paid] || paid.To != expected) throw new InvalidOperationException("Heji lost the actual native material payment destination.");
        }
        ReplaceRuntimeFrame(f.Id, f with { WuJing = r with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new WuJingHejiPaidEvent(f.Id, id, r.Before, _movementSequence));
    }
    private bool IsWuJingHejiUse(CardUseFrame use)
    {
        if (use.WuJingHeji is not { Origin: { } r } || use.Action is not { Type: CardActionType.Use } current) return false;
        var a = current;
        if (use.CurrentSlashFirePolicy is { } fire)
        {
            if (!IsSlashCard(r.Kind) || fire.OriginalKind != r.Kind || fire.OriginalAction.ActionId != r.ActionId) return false;
            AssertCurrentSlashFirePolicy(use); a = fire.OriginalAction;
        }
        else if (r.Kind != use.CardKind) return false;
        if (r.UseId != use.Id || r.ActionId != a.ActionId || r.Kind != a.EffectiveKind ||
            !ShownEntityUseActorMatches(use, r.ActorSeat, r.Materials.FirstOrDefault()?.From.OwnerSeat ?? r.ActorSeat) || a.ParentActionId != r.IncomingActionId || !a.ConversionChain.SequenceEqual(r.Conversions) ||
            !a.PhysicalCards.Select(c => new WuJingMaterial(c.CardId, c.CardKind, c.From)).SequenceEqual(r.Materials) ||
            ProgramEventHistory<WuJingHejiNativeIssuedEvent>().Count(e => e.ProgramFrameId == r.ProgramFrameId && e.UseId == r.UseId && e.ActionId == r.ActionId && e.IncomingUseId == r.IncomingUseId && e.IncomingActionId == r.IncomingActionId && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.ActualTurn == r.ActualTurn && e.ActorSeat == r.ActorSeat && e.TargetSeat == r.TargetSeat && e.Kind == r.Kind && e.MaterialCount == r.Materials.Count && e.RewardEligible == r.RewardEligible) != 1 ||
            !ProgramEventHistory<WuJingHejiMaterialIssuedEvent>().Where(e => e.UseId == use.Id).OrderBy(e => e.Index).Select(e => e.Material).SequenceEqual(r.Materials)) return false;
        var at = _resolutionStack.FindIndex(frame => frame.Id == use.Id);
        return at > 0 && _resolutionStack[at - 1] is ProgramSkillFrame { WuJing: { Stage: WuJingStage.NativeUse, NativeCompleted: false } parent } f && f.Id == r.ProgramFrameId && parent.NativeOrigin == r && parent.Source == r.Source && parent.GameplayHash == r.GameplayHash && (r.Materials.Count > 0 || WuJingZeroPaymentProof(f, parent, use));
    }
    private long? WuJingHejiProgramParent(long id) => LifecycleCardUse(id) is { WuJingHeji: { Origin: { } r } } use && IsWuJingHejiUse(use) ? r.ProgramFrameId : null;
    private bool HasIssuedWuJingHejiDistance(long? useId, int actor) => useId is { } id && LifecycleCardUse(id) is { WuJingHeji: { Origin: { } r } } use && r.ActorSeat == actor && IsWuJingHejiUse(use);
    private bool IsWuJingHejiRemovedMaterial(long useId, int cardId) => LifecycleCardUse(useId) is { WuJingHeji: { Origin: { } r } } use && IsWuJingHejiUse(use) &&
        r.Materials.Any(m => m.CardId == cardId && m.From.Zone == CardZoneKind.Equipment && GetAdvancedCard(m.CardId).IsGeneralWeapon && _cardZones.Contains(CardLocation.OutsideGame, cardId));
    [NativeUseValidation(27130)] private void ValidateWuJingUse(CardUseFrame use)
    { if (use.WuJingHeji is not null && !IsWuJingHejiUse(use)) throw new InvalidOperationException("Heji's native return lost its original actor, chosen materials and typed owning parent."); }
    [NativeUseReturn(27130)] private void ReturnWuJingUse(CardUseFrame use)
    {
        if (use.WuJingHeji is not { Origin: { } origin }) return;
        var f = GetActiveProgramFrame(origin.ProgramFrameId); var r = f.WuJing;
        if (r is not { Stage: WuJingStage.NativeUse, NativeCompleted: false } || r.NativeOrigin != origin) throw new InvalidOperationException("Heji lost its once-only complete-use typed return.");
        ReplaceRuntimeTop(f with { WuJing = r with { NativeCompleted = true } });
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 27130)] private bool ReturnWuJingAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuJing: { Stage: WuJingStage.NativeUse, NativeCompleted: true, NativeOrigin: { } origin } r } f || completion.ResolutionId != origin.UseId) return false;
        if (completion.ProgramFrameId != f.Id || LifecycleCardUse(origin.UseId) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == origin.UseId) != 1 || ProgramEventHistory<WuJingHejiReturnedEvent>().Any(e => e.UseId == origin.UseId)) throw new InvalidOperationException("Heji cannot return before or twice after the actual whole native attack.");
        AdvanceEventRulesAndQueueFact(new WuJingHejiReturnedEvent(f.Id, origin.UseId));
        ReplaceRuntimeTop(f with { WuJing = r with { Stage = WuJingStage.NativeChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); return true;
    }
    private void CollectIssuedWuJingHejiReward(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (window != SkillProgramTriggerWindow.CardUseCompleted || _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Action?.ActionId == action.ActionId) is not { WuJingHeji: { Rewarded: false, Origin: { RewardEligible: true } origin }, Action.ConversionChain.Count: 0 } use || !IsWuJingHejiUse(use) || !_players[origin.Source.OwnerSeat].IsAlive) return;
        var t = _contentRegistry.GetSkill(origin.Source.SkillId).Program!.Triggers.Single(t => t.Effects is [{ Op: SkillProgramEffectOp.RewardHejiUnconvertedUse }]);
        var context = CreateCardActionProgramContext(action, window, 0, origin.Source.OwnerSeat, action.TargetSeats is [var target] ? target : -1, CaptureProgramTriggerFacts(_players[origin.Source.OwnerSeat], action));
        result.Add(new(origin.Source.OwnerSeat, context.TargetSeat ?? -1, origin.Source.SkillId, t.Id, origin.GameplayHash, origin.Source.SkillInstanceId, t.Priority, context));
    }
    private bool IsIssuedWuJingHejiReward(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (GetProgramTrigger(c).Effects is not [{ Op: SkillProgramEffectOp.RewardHejiUnconvertedUse }] || WuJingCompletedUse(context) is not { WuJingHeji: { Rewarded: false, Origin: { RewardEligible: true } r }, Action.ConversionChain.Count: 0 } use || !IsWuJingHejiUse(use) || !_players[c.OwnerSeat].IsAlive ||
            r.Source.OwnerSeat != c.OwnerSeat || r.Source.SkillId != c.SkillId || r.Source.SkillInstanceId != c.SkillInstanceId || r.GameplayHash != c.GameplayHash) return false;
        return context.ParentFrameId == 0 || _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is ProgramCardTriggerWindowFrame w && w.ParentFrameId == r.UseId && w.Action.ActionId == r.ActionId && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && ToSharedCandidate(w.Candidates[w.CandidateIndex]) == c;
    }
    private bool WuJingHejiTrickMaterialsMatch(CardUseFrame use, CardMovementBatchContext batch)
    {
        if (use.CardKind != CardKind.Duel || !IsWuJingHejiUse(use) || use.Action is not { } a || batch.ParentFrameId != use.Id || batch.ParentBatchId is not null || batch.AwaitingProgramFrameId is not null || batch.NativeDrawInvocation is not null || batch.NativeBuZhiDiscardInvocation is not null || batch.Movements.Count != a.PhysicalCards.Count || batch.Movements.Count == 0) return false;
        return batch.Movements.All(m => a.PhysicalCards.SingleOrDefault(c => c.CardId == m.CardId) is { } c && c.From == m.From && c.CardKind == m.CardKind && m.Reason == CardMoveReasons.Use && m.TurnNumber == batch.TurnNumber && m.Sequence > 0 && m.Sequence <= _cardMovements.Count && _cardMovements[m.Sequence - 1] == m &&
            m.To == (GetAdvancedCard(m.CardId).IsGeneralWeapon && c.From.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : IsProgramAlternativeCost(a, m.CardId) ? CardLocation.DrawPile : CardLocation.Processing));
    }
    private void CaptureWuJingHejiTrickPaymentBatch(CardMovementBatchContext batch)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame use || !WuJingHejiTrickMaterialsMatch(use, batch)) return;
        if (use.PaymentMovementReceipt is not null) throw new InvalidOperationException("One Heji Duel cannot issue its native physical payment twice.");
        ReplaceRuntimeFrame(use.Id, use with { PaymentMovementReceipt = new(use.Id, use.Action!.ActionId, batch.Movements[0].Sequence - 1, batch.Movements[^1].Sequence) { Batch = batch } });
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.MatchBatch, 50)]
    private bool IsWuJingHejiTrickPaymentBatch(CardUseFrame use, CardMovementBatchContext batch) => WuJingHejiTrickMaterialsMatch(use, batch) && CardUsePaymentBatchMatchesReceipt(use, batch);
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.TrailingTail, 40)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.HpObserverTail, 40)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SharedProcessingTail, 40)]
    private bool HasWuJingHejiTrickPaymentTail(CardUseFrame use, RecoveryPaidCardUseContinuation continuation) => continuation is { Kind: RecoveryPaidCardUseKind.Trick, CardId: { } card, ActionKind: LegalActionKind.Duel, TargetSeats: { } targets } && card == use.CardId && continuation.SourceSeat == use.SourceSeat && targets.SequenceEqual(use.TargetSeats) && use.PaymentMovementReceipt is { } receipt && IsWuJingHejiTrickPaymentBatch(use, receipt.Batch);

    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 30)]
    private bool DistancePhysicalSlash30HasIssuedWuJingHejiDistance(ProgramSlashDistanceQuery q) =>
        HasIssuedWuJingHejiDistance(q.ExistingUseFrameId, q.Actor.Seat);

}
