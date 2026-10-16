namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasWuJingLiubing => _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ClaimLiubingUndamagingSlash) == true;
    private bool HasWuJingHeji => _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.UseHejiAtCompletedSingleTarget) == true;
    private bool WuJingOwnsLiubing(int actor) => IsValidPlayerSeat(actor) && _players[actor].IsAlive && (GetSkillBindingShard(_players[actor])?.GetInstanceTriggers(SkillProgramTriggerWindow.DiscardPileReceived) ?? []).Any(b => b.Trigger.Effects is [{ Op: SkillProgramEffectOp.ClaimLiubingUndamagingSlash }]);
    private static bool WuJingPhysicalSlash(Card card, CardKind kind, IReadOnlyList<int> ids, IReadOnlyList<CardConversionSource>? conversions) =>
        card.Id > 0 && ids is [var id] && id == card.Id && IsSlashCard(card.Kind) && kind == card.Kind && conversions is null or { Count: 0 };
    private bool WuJingFirstPhysicalSlash(int actor) => !ProgramEventHistory<WuJingActualPhysicalSlashEvent>().Any(e => e.ActualTurn == _turnNumber && e.TurnOwner == _currentSeat && e.ActorSeat == actor);
    private Suit? WuJingLiubingUsedSuit(int actorSeat, Card card, CardKind effectiveKind, IReadOnlyList<int> ids, IReadOnlyList<CardConversionSource>? conversions, Suit? ordinary) =>
        HasWuJingLiubing && _turnNumber > 0 && WuJingOwnsLiubing(actorSeat) && WuJingPhysicalSlash(card, effectiveKind, ids, conversions) && WuJingFirstPhysicalSlash(actorSeat) ? Suit.Diamond : ordinary;
    private Card WuJingLiubingUseCard(CharacterState actor, Card card, CardKind kind, CardConversionSource? conversion = null, IReadOnlyList<int>? ids = null, long? existingUseId = null)
    {
        if (!HasWuJingLiubing) return card;
        if (existingUseId is { } useId && LifecycleCardUse(useId) is { WuJingLiubing: { } issued } use && issued.CardId == card.Id && issued.Kind == kind && use.SourceSeat == actor.Seat)
            return card with { Suit = issued.Suit };
        var suit = WuJingLiubingUsedSuit(actor.Seat, card, kind, ids ?? (card.Id > 0 ? [card.Id] : []), conversion is null ? [] : [conversion], card.Suit);
        return suit is { } value && value != card.Suit ? card with { Suit = value } : card;
    }
    private Card? WuJingLiubingIssuedAppearance(long useId, Card? card)
    {
        if (card is null || LifecycleCardUse(useId) is not { WuJingLiubing: { } r, Action: { } a } use || r.CardId != card.Id) return card;
        if (r.UseId != use.Id || r.ActionId != a.ActionId || r.Kind != use.CardKind || a.EffectiveSuit != r.Suit || r.Suit != Suit.Diamond || ProgramEventHistory<WuJingLiubingAppearanceIssuedEvent>().Count(e => e.Receipt == r) != 1)
            throw new InvalidOperationException("Liubing lost its exact issued first physical Slash appearance.");
        return card with { Suit = r.Suit };
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 27130)]
    private void ObserveWuJingNativeUse(IGameEvent payload)
    {
        if (!HasWuJingLiubing || payload is not CardUseDeclaredEvent declared || LifecycleCardUse(declared.ResolutionId) is not { Action: { Type: CardActionType.Use } action } use ||
            action.ActorSeat != use.SourceSeat || use.CardKind != action.EffectiveKind || use.CardId != declared.CardId || use.SourceSeat != declared.SourceSeat || _turnNumber < 1) return;
        if (action.PhysicalCards is [var material] && WuJingPhysicalSlash(GetAdvancedCard(material.CardId), action.EffectiveKind, [material.CardId], action.ConversionChain))
        {
            var ordinal = ProgramEventHistory<WuJingActualPhysicalSlashEvent>().Count(e => e.ActualTurn == _turnNumber && e.TurnOwner == _currentSeat && e.ActorSeat == action.ActorSeat) + 1;
            AdvanceEventRulesAndQueueFact(new WuJingActualPhysicalSlashEvent(use.Id, action.ActionId, _turnNumber, _currentSeat, action.ActorSeat, material.CardId, action.EffectiveKind, ordinal));
            if (ordinal == 1 && WuJingOwnsLiubing(action.ActorSeat))
            {
                var binding = GetSkillBindingShard(_players[action.ActorSeat]).GetInstanceTriggers(SkillProgramTriggerWindow.DiscardPileReceived).First(b => b.Trigger.Effects is [{ Op: SkillProgramEffectOp.ClaimLiubingUndamagingSlash }]);
                if (action.EffectiveSuit != Suit.Diamond || action.EffectiveIsRed != true) throw new InvalidOperationException("The first Liubing physical Slash must freeze Diamond before native declaration and response.");
                var r = new WuJingLiubingAppearance(use.Id, action.ActionId, new(binding.SkillId, binding.Trigger.Id, action.ActorSeat, binding.SkillInstanceId), _turnNumber, _currentSeat, action.ActorSeat, material.CardId, action.EffectiveKind, Suit.Diamond);
                UpdateLifecycleCardUse(use.Id, u => u with { WuJingLiubing = r }); AdvanceEventRulesAndQueueFact(new WuJingLiubingAppearanceIssuedEvent(r));
            }
        }
        var phase = CurrentActualDiscardRecoveryPhase();
        for (var i = 0; i < action.PhysicalCards.Count; i++)
        {
            var c = action.PhysicalCards[i]; AdvanceEventRulesAndQueueFact(new WuJingUseMaterialEvent(use.Id, action.ActionId, _turnNumber, phase is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == action.ActorSeat && action.ActorSeat == _currentSeat && _phase == TurnPhase.Play ? p.Token : null, action.ActorSeat, i, action.PhysicalCards.Count, c.CardId, c.CardKind, c.From, _movementSequence));
        }
    }
    private bool WuJingSettledMaterial(WuJingUseMaterialEvent fact, CardMovementRecord last)
    {
        if (last.To != CardLocation.DiscardPile || _cardZones.GetLocation(fact.CardId) != CardLocation.DiscardPile || _cardMovements.Last(m => m.CardId == fact.CardId).Sequence != last.Sequence || last.CardKind != fact.PrintedKind ||
            ProgramEventHistory<WuJingUseMaterialEvent>().Any(e => e.CardId == fact.CardId && e.UseId != fact.UseId && (e.Before > fact.Before || e.Before == fact.Before && e.UseId > fact.UseId))) return false;
        var paid = _cardMovements.FirstOrDefault(m => m.CardId == fact.CardId && m.Sequence > fact.Before && m.From == fact.From && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use);
        return paid is not null && _cardMovements.Where(m => m.CardId == fact.CardId && m.Sequence > paid.Sequence && m.Sequence <= last.Sequence).ToArray() is [var finish] && finish == last && finish.From == CardLocation.Processing && finish.Reason == CardMoveReasons.UseFinished;
    }
    private void CaptureWuJingLiubingBatch(CardMovementBatchContext batch)
    {
        if (!HasWuJingLiubing || !batch.Movements.Any(m => m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished)) return;
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.Action is { Type: CardActionType.Use, PhysicalCards.Count: > 0 } && IsSlashCard(u.CardKind) && !u.CausedDamage).ToArray())
        {
            var action = use.Action!; var facts = ProgramEventHistory<WuJingUseMaterialEvent>().Where(e => e.UseId == use.Id && e.ActionId == action.ActionId).OrderBy(e => e.Index).ToArray();
            if (facts.Length == 0 || facts.Length != action.PhysicalCards.Count || facts[0].PlayToken is not { } token || facts.Any(e => e.PlayToken != token || e.Count != facts.Length || e.ActualTurn != batch.TurnNumber) ||
                action.EffectiveIsRed == false || !ProgramEventHistory<ActualDiscardRecoveryPhaseStartedEvent>().Any(e => e.Phase.Token == token && e.Phase.ActorSeat == action.ActorSeat && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play) ||
                facts.Any(e => _cardZones.GetLocation(e.CardId) == CardLocation.Processing) || ProgramEventHistory<WuJingLiubingSettledEvent>().Any(e => e.UseId == use.Id)) continue;
            var settled = facts.Select(e => (Fact: e, Move: _cardMovements.LastOrDefault(m => m.CardId == e.CardId))).Where(x => x.Move is not null && WuJingSettledMaterial(x.Fact, x.Move)).ToArray();
            if (!settled.Any(x => batch.Movements.Contains(x.Move!))) continue;
            foreach (var x in settled) AdvanceEventRulesAndQueueFact(new WuJingLiubingSettledEvent(use.Id, action.ActionId, batch.Id, token, action.ActorSeat, x.Fact.CardId, x.Move!.Sequence));
        }
    }
    private WuJingLiubingSettledEvent[] WuJingClaimFacts(CardMovementBatchContext batch, ProgramTriggerCandidate c)
    {
        if (c.OccurrenceIndex < 0 || c.OccurrenceIndex >= batch.Movements.Count) return [];
        var m = batch.Movements[c.OccurrenceIndex]; var anchor = ProgramEventHistory<WuJingLiubingSettledEvent>().FirstOrDefault(e => e.BatchId == batch.Id && e.CardId == m.CardId && e.MovementSequence == m.Sequence && e.FinalActor != c.OwnerSeat);
        if (anchor is null) return [];
        return ProgramEventHistory<WuJingLiubingSettledEvent>().Where(e => e.BatchId == batch.Id && e.UseId == anchor.UseId && e.ActionId == anchor.ActionId).OrderBy(e => e.MovementSequence).ToArray();
    }
    private IReadOnlyList<ProgramTriggerCandidate> CollectWuJingLiubingCandidates(CardMovementBatchContext batch)
    {
        if (!HasWuJingLiubing) return [];
        return ProgramEventHistory<WuJingLiubingSettledEvent>().Where(e => e.BatchId == batch.Id).GroupBy(e => e.UseId).SelectMany(group =>
        {
            var anchor = group.First(e => batch.Movements.Any(m => m.CardId == e.CardId && m.Sequence == e.MovementSequence));
            var occurrence = batch.Movements.ToList().FindIndex(m => m.CardId == anchor.CardId && m.Sequence == anchor.MovementSequence);
            return _players.Where(p => p.IsAlive && p.Seat != anchor.FinalActor).SelectMany(p => CollectProgramTriggerCandidates(p, SkillProgramTriggerWindow.DiscardPileReceived)).Where(c => GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.ClaimLiubingUndamagingSlash }]).Select(c => c with { OccurrenceIndex = occurrence });
        }).Distinct().ToArray();
    }
    private ProgramSkillWindowContext CreateWuJingLiubingContext(CardsMovedTriggerWindowFrame frame, ProgramTriggerCandidate c)
    {
        var fact = WuJingClaimFacts(frame.Batch, c).First(); return new(SkillProgramTriggerWindow.DiscardPileReceived, frame.Id, c.OwnerSeat, SourceSeat: fact.FinalActor, TargetSeat: c.OwnerSeat, OccurrenceIndex: c.OccurrenceIndex, Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]), MovementBatch: frame.Batch, MovementIndex: c.OccurrenceIndex);
    }
    private bool IsWuJingLiubingCandidate(ProgramTriggerCandidate c) => _contentRegistry.Skills.GetValueOrDefault(c.SkillId)?.Program?.Triggers.Any(t => t.Id == c.BindingId && t.Effects is [{ Op: SkillProgramEffectOp.ClaimLiubingUndamagingSlash }]) == true;
    private bool CanRunWuJingLiubingCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!IsWuJingLiubingCandidate(c) || !_players[c.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[c.OwnerSeat], c.SkillId, c.SkillInstanceId) || context.Window != SkillProgramTriggerWindow.DiscardPileReceived || context.OwnerSeat != c.OwnerSeat || context.MovementBatch is not { } batch || context.ParentFrameId != batch.Id || _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == batch.Id) is not { } parent || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != c) return false;
        return WuJingClaimFacts(batch, c).Any(e => _cardZones.GetLocation(e.CardId) == CardLocation.DiscardPile && _cardMovements.Last(m => m.CardId == e.CardId).Sequence == e.MovementSequence);
    }
}
