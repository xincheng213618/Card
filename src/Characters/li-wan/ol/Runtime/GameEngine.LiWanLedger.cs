namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasLiWanCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferLianjuSameColorGift) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage);
    private static CardConversionSource LiWanSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int LianjuEndingSerial => ProgramEventHistory<LianjuEndingReachedEvent>().LastOrDefault()?.Serial ?? 0;

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
    private void RecordLianjuEntityUse(int actor, long? action, long nativeUse, int card)
    {
        if (card <= 0 || !IsValidPlayerSeat(actor) || _turnNumber < 1 || ProgramEventHistory<LianjuEntityUsedEvent>().Any(e =>
                e.ActualTurn == _turnNumber && e.ActionId == action && e.UseFrameId == nativeUse && e.CardId == card)) return;
        AdvanceEventRulesAndQueueFact(new LianjuEntityUsedEvent(_turnNumber, actor, action, nativeUse, card));
    }
    private IReadOnlyList<int> LianjuEligibleCards(int actor, bool red) => Array.AsReadOnly(
        ProgramEventHistory<LianjuEntityUsedEvent>().Where(e => e.ActualTurn == _turnNumber && LianjuFinalUseActor(e) == actor)
            .Select(e => e.CardId).Distinct().Where(id => _cardZones.GetLocation(id) == CardLocation.DiscardPile &&
                SuitColor(GetAdvancedCard(id).Suit) == red).Order().ToArray());

    private LianjuPromiseGrantedEvent[] LianjuDuePromises()
    {
        var endings = ProgramEventHistory<LianjuEndingReachedEvent>().ToArray();
        return ProgramEventHistory<LianjuPromiseGrantedEvent>().Where(p => endings.FirstOrDefault(e => e.Serial > p.EndingSerial && e.ActorSeat == p.RecipientSeat)?.Serial == LianjuEndingSerial)
            .OrderBy(p => p.FrameId).ToArray();
    }
    private void AppendLianjuEndingItems(List<TurnEndingBoundaryItem> items)
    {
        if (!HasLiWanCapability) return;
        items.RemoveAll(item => item.Candidate is { } c && GetProgramTrigger(c).Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveLianjuOppositeColorReturn));
        var due = LianjuDuePromises();
        for (var i = 0; i < due.Length; i++)
        {
            var p = due[i];
            if (ProgramEventHistory<LianjuPromiseConsumedEvent>().Any(e => e.PromiseFrameId == p.FrameId) || !_players[p.Source.OwnerSeat].IsAlive ||
                _contentRegistry.GetSkill(p.Source.SkillId).Program is not { } program || program.GameplayHash != p.GameplayHash) continue;
            var trigger = program.Triggers.Single(t => t.Effects is [{ Op: SkillProgramEffectOp.ResolveLianjuOppositeColorReturn }]);
            items.Add(new(TurnEndingBoundaryItemKind.Program, trigger.Priority, $"lianju:{p.FrameId}",
                new(p.Source.OwnerSeat, p.Source.SkillId, trigger.Id, p.Source.SkillInstanceId, p.GameplayHash, trigger.Priority, i),
                CaptureProgramTriggerFacts(_players[p.Source.OwnerSeat])));
        }
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
    private SilvEntityMarkedEvent? SilvMark(int id) => ProgramEventHistory<SilvEntityMarkedEvent>().FirstOrDefault(e => e.CardId == id && ValidSilvMark(e));
    private bool HasSilvEntityMark(int cardId) => HasLiWanCapability && SilvMark(cardId) is not null;
    private bool ValidSilvMark(SilvEntityMarkedEvent e) => e.CardId > 0 && e.Source.SkillId == "ol:silv" &&
        _contentRegistry.Skills.GetValueOrDefault(e.Source.SkillId)?.Program is { } program && program.GameplayHash == e.GameplayHash &&
        _cardMovements.Any(m => m.Sequence == e.GainMovementSequence && m.CardId == e.CardId && m.To.Zone == CardZoneKind.Hand &&
            m.Reason.Value is LianjuGainReason or SilvDrawReason) && ProgramEventHistory<LiWanPaidEvent>().Any(p => p.FrameId == e.ProducerFrameId &&
                e.GainMovementSequence > p.Before && e.GainMovementSequence <= p.After);
    private bool IsRefundableSilvMovement(CardMovementRecord m) => m.To == CardLocation.DiscardPile && GetProgramDiscardSource(m)?.OwnerSeat is { } loser &&
        IsValidPlayerSeat(loser) && _players[loser].IsAlive && SilvMark(m.CardId) is not null && _cardZones.GetLocation(m.CardId) == CardLocation.DiscardPile &&
        _cardMovements.Last(x => x.CardId == m.CardId).Sequence == m.Sequence;
    private IReadOnlyList<ProgramTriggerCandidate> CollectSilvRefundCandidates(CardMovementBatchContext batch)
    {
        if (!HasLiWanCapability) return [];
        var result = new List<ProgramTriggerCandidate>();
        for (var i = 0; i < batch.Movements.Count; i++)
        {
            var move = batch.Movements[i];
            if (!IsRefundableSilvMovement(move) || ProgramEventHistory<LiWanStartedEvent>().Any(e => e.RefundSequence == move.Sequence)) continue;
            var mark = SilvMark(move.CardId)!; var p = _contentRegistry.GetSkill(mark.Source.SkillId).Program!;
            var t = p.Triggers.Single(t => t.Effects is [{ Op: SkillProgramEffectOp.ReturnDiscardedSilvEntity }]);
            result.Add(new(mark.Source.OwnerSeat, mark.Source.SkillId, t.Id, mark.Source.SkillInstanceId, mark.GameplayHash, t.Priority, i));
        }
        return Array.AsReadOnly(result.ToArray());
    }
}
