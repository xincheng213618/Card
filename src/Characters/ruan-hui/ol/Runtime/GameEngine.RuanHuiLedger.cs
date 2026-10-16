namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasRuanHuiCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMingchaRevealDrawChoice) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ArmJingzhongNextActualPlay);
    private static CardConversionSource RuanHuiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private ActualDiscardRecoveryPhaseKey? RuanHuiPlay => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p ? p : null;
    private ActualDiscardRecoveryPhaseKey? JingzhongDuePhase(JingzhongLeaseEvent lease) => ProgramEventHistory<ActualDiscardRecoveryPhaseStartedEvent>()
        .Select(e => e.Phase).FirstOrDefault(p => p.Token > lease.AfterPhaseToken && p.ActorSeat == lease.RecipientSeat && p.Kind == ActualDiscardRecoveryPhaseKind.Play);
    private bool SameJingzhongLeaseOwner(JingzhongLeaseEvent a,JingzhongLeaseEvent b)=>a.Source.OwnerSeat==b.Source.OwnerSeat&&a.Source.SkillId==b.Source.SkillId&&a.Source.SkillInstanceId==b.Source.SkillInstanceId&&a.GameplayHash==b.GameplayHash;
    private int JingzhongUsedCount(long lease)
    {
        var grant=ProgramEventHistory<JingzhongLeaseEvent>().Single(e=>e.FrameId==lease);var phase=JingzhongDuePhase(grant)?.Token;
        return ProgramEventHistory<JingzhongClaimIssuedEvent>().Count(e=>e.PhaseToken==phase&&ProgramEventHistory<JingzhongLeaseEvent>().Any(l=>l.FrameId==e.LeaseId&&SameJingzhongLeaseOwner(l,grant)));
    }
    private bool JingzhongClaimAlreadyIssued(JingzhongLeaseEvent lease,long action)=>ProgramEventHistory<JingzhongClaimIssuedEvent>().Any(e=>e.PhaseToken==JingzhongDuePhase(lease)?.Token&&e.ActionId==action&&
        ProgramEventHistory<JingzhongLeaseEvent>().Any(l=>l.FrameId==e.LeaseId&&SameJingzhongLeaseOwner(l,lease)));
    private JingzhongLeaseEvent[] JingzhongPhaseLeases(long phase) => ProgramEventHistory<JingzhongLeaseEvent>().Where(l =>
        JingzhongDuePhase(l)?.Token == phase && JingzhongUsedCount(l.FrameId) < 3 && _players[l.Source.OwnerSeat].IsAlive).ToArray();

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 27080)]
    private void ObserveRuanHuiUse(IGameEvent fact)
    {
        if (!HasRuanHuiCapability || RuanHuiPlay is not { } phase) return;
        if (fact is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { } use)
        {
            var action = use.Action;
            var costs = action?.PhysicalCards ?? (use.PhysicalCardIds ?? (use.CardId > 0 ? [use.CardId] : []))
                .Select(id => new CardActionCost(id, GetAdvancedCard(id).Kind, _cardZones.GetLocation(id))).ToArray();
            RecordJingzhongUse(phase, action?.ActionId ?? -use.Id, use.Id, use.SourceSeat, false, costs); return;
        }
        if (fact is not CardActionAcceptedEvent { Action.Type: CardActionType.Response } accepted) return;
        var a = accepted.Action;
        if (a.ActorSeat != a.ProviderSeat || a.ResponderSeat != a.ActorSeat || a.RequesterSeat is not null ||
            !(a.EffectiveKind == CardKind.Nullification || a.EffectiveKind == CardKind.Dodge && IsProgramResponseCardUse(_players[a.ActorSeat], a.EffectiveKind))) return;
        var parent = _resolutionStack.OfType<CardUseFrame>().LastOrDefault(u => u.Action?.ActionId == a.ParentActionId) ??
            (ActiveNullificationWindow is { DelayedReturn: not null } n ? LifecycleCardUse(n.ParentFrameId) : null);
        RecordJingzhongUse(phase, a.ActionId, parent?.Id ?? 0, a.ActorSeat, true, a.PhysicalCards);
        // Physical Nullification commits its movement before accepting its response action.
        foreach (var batch in _pendingCardsMovedBatches.Where(b => b.Movements.Any(m => a.PhysicalCards.Any(c => c.CardId == m.CardId))).ToArray())
            CaptureRuanHuiMovementBatch(batch);
    }
    private void RecordJingzhongUse(ActualDiscardRecoveryPhaseKey phase, long action, long use, int actor, bool response, IReadOnlyList<CardActionCost> costs)
    {
        if (ProgramEventHistory<JingzhongUseEntityEvent>().Any(e => e.ActionId == action)) return;
        for (var i = 0; i < costs.Count; i++)
        {
            var c = costs[i];
            var paid = response ? _cardMovements.LastOrDefault(m => m.CardId == c.CardId && m.From == c.From &&
                (m.Reason == CardMoveReasons.Respond || m.Reason == CardMoveReasons.Nullification) && m.TurnNumber == phase.TurnNumber) : null;
            AdvanceEventRulesAndQueueFact(new JingzhongUseEntityEvent(phase.Token, action, use, actor, response, i, costs.Count,
                c.CardId, c.CardKind, c.From, _movementSequence, paid?.Sequence));
        }
    }
    private int JingzhongFinalActor(JingzhongUseEntityEvent e)
    {
        var actor = e.OriginalActor;
        if (e.Response) return actor;
        foreach (var change in ProgramEventHistory<ProgramCardUseActorReplacedEvent>().Where(c => c.CardUseFrameId == e.UseId))
        {
            if (change.PreviousActorSeat != actor) throw new InvalidOperationException("Jingzhong lost its original Use actor replacement chain.");
            actor = change.ActorSeat;
        }
        return actor;
    }
    private bool JingzhongMaterialStillOwnsDiscard(JingzhongUseEntityEvent fact, CardMovementRecord last)
    {
        if (last.To != CardLocation.DiscardPile || _cardZones.GetLocation(fact.CardId) != CardLocation.DiscardPile ||
            _cardMovements.Last(m => m.CardId == fact.CardId).Sequence != last.Sequence || last.CardKind != fact.PhysicalKind ||
            ProgramEventHistory<JingzhongUseEntityEvent>().Any(e => e.CardId == fact.CardId && e.ActionId != fact.ActionId &&
                (e.CapturedAfterSequence > fact.CapturedAfterSequence || e.CapturedAfterSequence == fact.CapturedAfterSequence && e.ActionId > fact.ActionId))) return false;
        var paid = fact.PaidEntrySequence is { } sequence ? _cardMovements.SingleOrDefault(m => m.Sequence == sequence && m.CardId == fact.CardId) :
            _cardMovements.FirstOrDefault(m => m.CardId == fact.CardId && m.Sequence > fact.CapturedAfterSequence && m.From == fact.OriginalFrom &&
                (fact.Response ? m.Reason == CardMoveReasons.Respond || m.Reason == CardMoveReasons.Nullification : m.Reason == CardMoveReasons.Use || m.Reason == CardMoveReasons.EquipmentUse || m.Reason == CardMoveReasons.IronChainUse));
        if (paid is null || paid.Sequence > last.Sequence || paid.From != fact.OriginalFrom || paid.To.Zone is not (CardZoneKind.Processing or CardZoneKind.Equipment or CardZoneKind.Judgment)) return false;
        var location = paid.To;
        foreach (var move in _cardMovements.Where(m => m.CardId == fact.CardId && m.Sequence > paid.Sequence && m.Sequence <= last.Sequence))
        {
            if (move.From != location) return false;
            if (move.Sequence == last.Sequence)
                return location.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment || location == CardLocation.Processing &&
                    (fact.Response ? move.Reason == CardMoveReasons.ResponseFinished || move.Reason == CardMoveReasons.NullificationFinished :
                        move.Reason == CardMoveReasons.UseFinished || move.Reason == CardMoveReasons.IronChainFinished || JingzhongAbolishedEquipmentUse(fact,move));
            if (location == CardLocation.Processing && move.To.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment &&
                (move.Reason == CardMoveReasons.Use || move.Reason == CardMoveReasons.EquipmentEnter || move.Reason == CardMoveReasons.DelayedCardPlace) ||
                move.From == move.To && location.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment)
            { location = move.To; continue; }
            return false;
        }
        return false;
    }
    private bool JingzhongAbolishedEquipmentUse(JingzhongUseEntityEvent fact,CardMovementRecord move)=>!fact.Response&&move.Reason.Value=="area.abolished.equipment"&&
        LifecycleCardUse(fact.UseId)is{Action:{Type:CardActionType.Use}action}use&&action.ActionId==fact.ActionId&&EquipmentCatalog.IsEquipment(use.CardKind)&&
        action.PhysicalCards.Any(c=>c.CardId==fact.CardId&&c.CardKind==fact.PhysicalKind&&c.From==fact.OriginalFrom)&&use.PhysicalCardIds?.Contains(fact.CardId)==true;
    private void CaptureRuanHuiMovementBatch(CardMovementBatchContext batch)
    {
        if (!HasRuanHuiCapability) return;
        if (batch.DiscardRecoveryPhase is { Kind: ActualDiscardRecoveryPhaseKind.Discard } discard)
            foreach (var m in batch.Movements.Where(m => GetProgramDiscardSource(m)?.OwnerSeat == discard.ActorSeat &&
                SuitColor(EffectiveSuit(_players[discard.ActorSeat], GetAdvancedCard(m.CardId))) == false))
                if (!ProgramEventHistory<JingzhongDiscardEvent>().Any(e => e.MovementSequence == m.Sequence))
                    AdvanceEventRulesAndQueueFact(new JingzhongDiscardEvent(discard.Token, discard.ActorSeat, m.Sequence, m.CardId));
        if (batch.DiscardRecoveryPhase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase) return;
        foreach (var lease in JingzhongPhaseLeases(phase.Token))
        foreach (var group in ProgramEventHistory<JingzhongUseEntityEvent>().Where(e => e.PhaseToken == phase.Token && JingzhongFinalActor(e) == lease.RecipientSeat).GroupBy(e => e.ActionId).ToArray())
        {
            if (JingzhongClaimAlreadyIssued(lease,group.Key) ||
                ProgramEventHistory<JingzhongSettledEntityEvent>().Any(e => e.LeaseId == lease.FrameId && e.ActionId == group.Key)) continue;
            var facts = group.ToArray();
            // A multi-material cleanup may span several atomic batches. Wait until its processing materials have settled.
            if (facts.Any(e => _cardZones.GetLocation(e.CardId) == CardLocation.Processing)) continue;
            var eligible = facts.Select(e => (Fact:e, Move:_cardMovements.LastOrDefault(m => m.CardId == e.CardId)))
                .Where(x => x.Move is not null && JingzhongMaterialStillOwnsDiscard(x.Fact, x.Move)).ToArray();
            if (!eligible.Any(x => batch.Movements.Contains(x.Move!))) continue;
            foreach (var x in eligible)
                AdvanceEventRulesAndQueueFact(new JingzhongSettledEntityEvent(lease.FrameId, phase.Token, group.Key, batch.Id, x.Fact.CardId, x.Move!.Sequence));
        }
    }
    private JingzhongSettledEntityEvent[] RuanHuiClaimFacts(CardMovementBatchContext batch, ProgramTriggerCandidate c)
    {
        if(c.OccurrenceIndex<0||c.OccurrenceIndex>=batch.Movements.Count)return [];
        var move=batch.Movements[c.OccurrenceIndex];
        var matching=ProgramEventHistory<JingzhongSettledEntityEvent>().Where(e => e.BatchId == batch.Id &&
            ProgramEventHistory<JingzhongLeaseEvent>().Any(l => l.FrameId == e.LeaseId && l.Source.OwnerSeat == c.OwnerSeat && l.Source.SkillInstanceId == c.SkillInstanceId && l.GameplayHash == c.GameplayHash) &&
            !JingzhongClaimAlreadyIssued(ProgramEventHistory<JingzhongLeaseEvent>().Single(l=>l.FrameId==e.LeaseId),e.ActionId)).OrderBy(e => e.LeaseId).ThenBy(e => e.ActionId).ThenBy(e => e.MovementSequence).ToArray();
        var anchor=matching.FirstOrDefault(e=>e.CardId==move.CardId&&e.MovementSequence==move.Sequence);
        return anchor is null?[]:matching.Where(e=>e.LeaseId==anchor.LeaseId&&e.ActionId==anchor.ActionId).ToArray();
    }
    private IReadOnlyList<ProgramTriggerCandidate> CollectRuanHuiClaimCandidates(CardMovementBatchContext batch)
    {
        if (!HasRuanHuiCapability) return [];
        return ProgramEventHistory<JingzhongSettledEntityEvent>().Where(e => e.BatchId == batch.Id).GroupBy(e => (e.LeaseId,e.ActionId)).Select(g => g.First(e=>batch.Movements.Any(m=>m.CardId==e.CardId&&m.Sequence==e.MovementSequence))).Select(e =>
        {
            var lease = ProgramEventHistory<JingzhongLeaseEvent>().Single(l => l.FrameId == e.LeaseId);
            var t = _contentRegistry.GetSkill(lease.Source.SkillId).Program!.Triggers.Single(t => t.Effects is [{ Op:SkillProgramEffectOp.ObtainJingzhongUsedEntities }]);
            var occurrence = batch.Movements.ToList().FindIndex(m => m.CardId == e.CardId);
            return new ProgramTriggerCandidate(lease.Source.OwnerSeat,lease.Source.SkillId,t.Id,lease.Source.SkillInstanceId,lease.GameplayHash,t.Priority,Math.Max(0,occurrence));
        }).Distinct().ToArray();
    }
    private bool IsIssuedRuanHuiCandidate(ProgramTriggerCandidate c) => _contentRegistry.Skills.GetValueOrDefault(c.SkillId)?.Program?.Triggers.Any(t =>
        t.Id == c.BindingId && t.Effects is [{ Op:SkillProgramEffectOp.ObtainJingzhongUsedEntities }]) == true;
    private bool CanRunIssuedRuanHuiCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!IsIssuedRuanHuiCandidate(c) || !_players[c.OwnerSeat].IsAlive || context.OwnerSeat != c.OwnerSeat || context.Window != SkillProgramTriggerWindow.DiscardPileReceived ||
            context.MovementBatch is not { } b || context.ParentFrameId != b.Id || _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == b.Id) is not { } w ||
            w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || w.Candidates[w.CandidateIndex] != c || !w.Batch.Movements.SequenceEqual(b.Movements)) return false;
        return RuanHuiClaimFacts(b,c).Any(e => JingzhongUsedCount(e.LeaseId) < 3 && _cardZones.GetLocation(e.CardId) == CardLocation.DiscardPile &&
            _cardMovements.Last(m => m.CardId == e.CardId).Sequence == e.MovementSequence);
    }
}
