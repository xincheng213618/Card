namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasLiWanCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferLianjuSameColorGift) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage);
    private static CardConversionSource LiWanSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_li_wan_ol_LiWanLedger.LiWanSource(f);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26720)]
    private void ObserveLiWanActualUseAndEnding(IGameEvent fact)
    {
        if (!HasLiWanCapability) return;
        if (fact is ActualDiscardRecoveryPhaseStartedEvent { Phase.Kind: ActualDiscardRecoveryPhaseKind.Ending } ending)
        {
            if (ProgramEventHistory<LianjuEndingReachedEvent>().Any(e => e.NativePhaseToken == ending.Phase.Token))
                throw new InvalidOperationException("Lianju cannot enter the same actual Ending phase twice.");
            AdvanceEventRulesAndQueueFact(new LianjuEndingReachedEvent(LianjuEndingSerial + 1, ending.Phase.TurnNumber, ending.Phase.ActorSeat, ending.Phase.Token));
            return;
        }
        if (fact is CardUseDeclaredEvent declared)
        {
            var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Id == declared.ResolutionId);
            if (use is null || use.SourceSeat != declared.SourceSeat || use.CardKind != declared.CardKind || use.CardId != declared.CardId)
                throw new InvalidOperationException("Lianju lost the exact native declared use.");
            if (use.Action is { Type: not CardActionType.Use }) return;
            foreach (var id in use.Action?.PhysicalCards.Select(c => c.CardId) ?? use.PhysicalCardIds ?? [use.CardId])
                RecordLianjuEntityUse(declared.SourceSeat, use.Action?.ActionId, use.Id, id);
            return;
        }
        if (fact is not CardActionAcceptedEvent { Action.Type: CardActionType.Response } accepted) return;
        var action = accepted.Action;
        if (action.ActorSeat != action.ProviderSeat || action.ResponderSeat != action.ActorSeat || action.RequesterSeat is not null ||
            !(action.EffectiveKind == CardKind.Nullification || action.EffectiveKind == CardKind.Dodge && IsProgramResponseCardUse(_players[action.ActorSeat], action.EffectiveKind))) return;
        // The same native predicate distinguishes a used Slash-defense Dodge from an AOE played-out Dodge.
        var parent = _resolutionStack.OfType<CardUseFrame>().LastOrDefault(u => u.Action?.ActionId == action.ParentActionId) ??
            (ActiveNullificationWindow is { DelayedReturn: not null } n ? _resolutionStack.OfType<CardUseFrame>().LastOrDefault(u => u.Id == n.ParentFrameId) : null);
        foreach (var material in action.PhysicalCards) RecordLianjuEntityUse(action.ActorSeat, action.ActionId, parent?.Id ?? 0, material.CardId);
    }
    private bool IsIssuedLiWanCandidate(ProgramTriggerCandidate c) => _contentRegistry.Skills.GetValueOrDefault(c.SkillId)?.Program?.Triggers
        .Any(t => t.Id == c.BindingId && t.Effects is [{ } e] && e.Op is SkillProgramEffectOp.ResolveLianjuOppositeColorReturn or SkillProgramEffectOp.ReturnDiscardedSilvEntity) == true;
    private bool CanRunIssuedLiWanCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!HasLiWanCapability || context.OwnerSeat != c.OwnerSeat || !IsValidPlayerSeat(c.OwnerSeat) || _winner != Winner.None ||
            _contentRegistry.Skills.GetValueOrDefault(c.SkillId)?.Program is not { } program || program.GameplayHash != c.GameplayHash) return false;
        var op = GetProgramTrigger(c).Effects[0].Op;
        if (op == SkillProgramEffectOp.ResolveLianjuOppositeColorReturn)
        {
            if (context.Window != SkillProgramTriggerWindow.TurnEnding || !_players[c.OwnerSeat].IsAlive ||
                _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } parent ||
                parent.OwnerSeat != _currentSeat || parent.TurnNumber != _turnNumber || parent.ItemIndex < 0 || parent.ItemIndex >= parent.Items.Count ||
                parent.Items[parent.ItemIndex].Candidate != c) return false;
            var due = LianjuDuePromises();
            return c.OccurrenceIndex >= 0 && c.OccurrenceIndex < due.Length && due[c.OccurrenceIndex] is { } p &&
                p.Source.OwnerSeat == c.OwnerSeat && p.Source.SkillId == c.SkillId && p.Source.SkillInstanceId == c.SkillInstanceId && p.GameplayHash == c.GameplayHash &&
                p.RecipientSeat == _currentSeat && context.SourceSeat == p.RecipientSeat && context.TargetSeat == p.RecipientSeat &&
                !ProgramEventHistory<LianjuPromiseConsumedEvent>().Any(e => e.PromiseFrameId == p.FrameId);
        }
        if (op != SkillProgramEffectOp.ReturnDiscardedSilvEntity || context.Window != SkillProgramTriggerWindow.DiscardPileReceived ||
            context.MovementBatch is not { } batch || context.MovementIndex != c.OccurrenceIndex || context.ParentFrameId != batch.Id ||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == batch.Id) is not { } movement ||
            movement.CandidateIndex < 0 || movement.CandidateIndex >= movement.Candidates.Count || movement.Candidates[movement.CandidateIndex] != c ||
            !movement.Batch.Movements.SequenceEqual(batch.Movements) || c.OccurrenceIndex < 0 || c.OccurrenceIndex >= batch.Movements.Count) return false;
        var move = batch.Movements[c.OccurrenceIndex]; var mark = SilvMark(move.CardId);
        return mark is not null && mark.Source.OwnerSeat == c.OwnerSeat && mark.Source.SkillId == c.SkillId && mark.Source.SkillInstanceId == c.SkillInstanceId && mark.GameplayHash == c.GameplayHash &&
            IsRefundableSilvMovement(move) && context.SourceSeat == GetProgramDiscardSource(move)?.OwnerSeat &&
            !ProgramEventHistory<LiWanStartedEvent>().Any(e => e.RefundSequence == move.Sequence);
    }
}
