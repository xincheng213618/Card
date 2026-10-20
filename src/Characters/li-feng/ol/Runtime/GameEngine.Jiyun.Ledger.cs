namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsJiyunPile(PublicPersistentPileSource p) => p.SkillId == "ol:jiyun" && p.Capacity == 3 && _publicPersistentPiles.GetValueOrDefault((p.OwnerSeat, p.SkillId, p.SkillInstanceId)) == p;
    private bool IsLiveJiyunPile(PublicPersistentPileSource p) => IsJiyunPile(p) && _players[p.OwnerSeat].IsAlive && _players[p.OwnerSeat].SkillGrants.Grants.Any(g => g.SkillId == p.SkillId && g.SkillInstanceId == p.SkillInstanceId);
    private IEnumerable<JiyunLeaseIssuedEvent> JiyunLeases() => ProgramEventHistory<JiyunLeaseIssuedEvent>().OrderBy(e => e.LeaseId);
    private JiyunLeaseDepartedEvent? JiyunDeparture(long id) => ProgramEventHistory<JiyunLeaseDepartedEvent>().SingleOrDefault(e => e.LeaseId == id);
    private bool HasLiveJiyunHandLease(JiyunLeaseIssuedEvent l) => !ProgramEventHistory<JiyunLeaseResolvedEvent>().Any(e => e.LeaseId == l.LeaseId) && JiyunDeparture(l.LeaseId) is null && _cardZones.GetLocation(l.CardId) == CardLocation.Hand(l.RecipientSeat);
    private bool HasJiyunCardMark(int ownerSeat, int cardId) => HasLiFengCapability && JiyunLeases().Any(l => _players[l.Source.OwnerSeat].IsAlive && l.RecipientSeat == ownerSeat && l.CardId == cardId && HasLiveJiyunHandLease(l));
    private void CaptureJiyunMovementBatch(CardMovementBatchContext batch)
    {
        if (!HasLiFengCapability) return;
        foreach (var lease in JiyunLeases().Where(l => !ProgramEventHistory<JiyunLeaseResolvedEvent>().Any(e => e.LeaseId == l.LeaseId)).ToArray())
        foreach (var m in batch.Movements.Where(m => m.CardId == lease.CardId && m.Sequence > lease.MovementSequence && m.From != m.To))
        {
            if (ProgramEventHistory<JiyunLeaseMovementEvent>().Any(e => e.LeaseId == lease.LeaseId && e.MovementSequence == m.Sequence)) continue;
            var departure = JiyunDeparture(lease.LeaseId);
            if (departure is null && m.From == CardLocation.Hand(lease.RecipientSeat))
                AdvanceEventRulesAndQueueFact(new JiyunLeaseDepartedEvent(lease.LeaseId, batch.Id, batch.ParentFrameId, m.CardId, m.From, m.To, m.Reason, m.Sequence));
            AdvanceEventRulesAndQueueFact(new JiyunLeaseMovementEvent(lease.LeaseId, batch.Id, batch.ParentFrameId, m.CardId, m.From, m.To, m.Reason, m.Sequence,
                m.To == CardLocation.DiscardPile && IsNativeTrueUseDiscardMovement(batch, m)));
        }
    }
    private bool RetainIssuedJiyunPile(PublicPersistentPileSource pile)
    {
        if (!HasLiFengCapability || pile.SkillId != "ol:jiyun" || !_players[pile.OwnerSeat].IsAlive) return false;
        if (_resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.LiFeng is { Paid: true } r && r.Pile == pile && r.Op is SkillProgramEffectOp.ResolveJiyunPreparationDrawLeases or SkillProgramEffectOp.CollectJiyunCompletedUseEntities)) return true;
        foreach (var l in JiyunLeases().Where(l => l.Pile == pile && !ProgramEventHistory<JiyunLeaseResolvedEvent>().Any(e => e.LeaseId == l.LeaseId)))
        {
            if (HasLiveJiyunHandLease(l)) return true;
            if (JiyunDeparture(l.LeaseId) is { } d && _resolutionStack.Any(f => f.Id == d.ParentFrameId || f.AcceptedDeclarationPayment is { Claimed: true } paid && paid.Cost.CardId == l.CardId || f is CardUseFrame { Action: { Type: CardActionType.Use } a } && a.PhysicalCards.Any(c => c.CardId == l.CardId && c.From == d.From))) return true;
        }
        return false;
    }
    private bool IsIssuedJiyunRetainedEntity(PublicPersistentPileSource pile, int cardId) => _players[pile.OwnerSeat].IsAlive && _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.LiFeng is { Paid: true, Stage: LiFengStage.CollectChildren } r && r.Pile == pile && r.ActiveCardId == cardId && f.PendingMovementContinuation is not null && ProgramEventHistory<JiyunClaimedEntityEvent>().Any(e => e.FrameId == f.Id && e.CardId == cardId && e.To == pile.Location && ValidJiyunClaim(e)));
    private (long NativeId, ProgramCompletedResponseKind? ResponseKind)? JiyunCompletedOrigin(CardActionContext action, ProgramCardContinuation? continuation)
    {
        if (action.Type == CardActionType.Use)
        {
            var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Action?.ActionId == action.ActionId);
            return use is { Step: ResolutionFrameStep.Completed } && use.Action!.ActorSeat == action.ActorSeat && ProgramEventHistory<CardUseFinishedEvent>().Any(e => e.ResolutionId == use.Id) ? (use.Id, null) : null;
        }
        continuation ??= action.EffectiveKind == CardKind.Dodge ? ProgramCardContinuation.Dodge : action.EffectiveKind == CardKind.Nullification ? ProgramCardContinuation.NullificationResponse : null;
        if (continuation is null || !IsCompletedResponseUse(action, continuation.Value)) return null;
        var completed = ProgramEventHistory<CardResponseCompletedEvent>().SingleOrDefault(e => e.ActionId == action.ActionId && e.ProviderSeat == action.ProviderSeat && e.NativeActorSeat == action.ActorSeat);
        if (completed is null) return null;
        return (completed.ParentFrameId, continuation == ProgramCardContinuation.Dodge ? ProgramCompletedResponseKind.Dodge : ProgramCompletedResponseKind.Nullification);
    }
    private CardMovementRecord? JiyunActualPayment(JiyunLeaseIssuedEvent lease, CardActionContext action, long nativeId, ProgramCompletedResponseKind? responseKind)
    {
        if (action.ActorSeat != lease.RecipientSeat || action.ProviderSeat != lease.RecipientSeat || action.PhysicalCards.SingleOrDefault(c => c.CardId == lease.CardId) is not { } cost || cost.CardKind != lease.PrintedKind || cost.From != CardLocation.Hand(lease.RecipientSeat) || JiyunDeparture(lease.LeaseId) is not { } d || d.From != cost.From) return null;
        var entry = _cardMovements.SingleOrDefault(m => m.Sequence == d.MovementSequence && m.CardId == lease.CardId && m.CardKind == cost.CardKind && m.From == cost.From);
        if (entry is null) return null;
        if (responseKind is { } response)
        {
            var continuation = response == ProgramCompletedResponseKind.Dodge ? ProgramCardContinuation.Dodge : ProgramCardContinuation.NullificationResponse;
            return IsResponseCompletionEntry(action, continuation, nativeId, cost, entry) ? entry : null;
        }
        if (_resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Id == nativeId) is not { Action: { } a } use || a.ActionId != action.ActionId) return null;
        if (d.ParentFrameId == nativeId && (entry.To == CardLocation.Processing && (entry.Reason == CardMoveReasons.Use || entry.Reason == CardMoveReasons.IronChainUse || entry.Reason == CardMoveReasons.EquipmentUse) || entry.To.Zone == CardZoneKind.Judgment && entry.Reason == CardMoveReasons.DelayedCardPlace)) return entry;
        if (use.AcceptedDeclarationPayment is { Claimed: true } paid && paid.Cost == cost && paid.ActorSeat == action.ActorSeat && paid.ProviderSeat == action.ProviderSeat && entry.To == CardLocation.Processing && entry.Reason.Value == "conversion.declaration.pay" && action.ConversionChain.Contains(paid.Source)) return entry;
        return IsKenshangRemovedMaterial(nativeId, lease.CardId) && entry.To == CardLocation.DiscardPile ? entry : null;
    }
    private bool JiyunTerminalEntity(JiyunLeaseIssuedEvent lease, CardActionContext action, long nativeId, ProgramCompletedResponseKind? responseKind)
    {
        var location = _cardZones.GetLocation(lease.CardId);
        if (location.Zone is not (CardZoneKind.Equipment or CardZoneKind.DiscardPile) || location.Zone == CardZoneKind.Equipment && location.OwnerSeat != action.ActorSeat) return false;
        var last = ProgramEventHistory<JiyunLeaseMovementEvent>().Where(e => e.LeaseId == lease.LeaseId).OrderBy(e => e.MovementSequence).LastOrDefault();
        if (last is null || last.To != location || _cardMovements.LastOrDefault(m => m.CardId == lease.CardId)?.Sequence != last.MovementSequence) return false;
        if (location.Zone == CardZoneKind.Equipment) return responseKind is null && last.ParentFrameId == nativeId && last.Reason == CardMoveReasons.EquipmentEnter;
        return last.ParentFrameId == nativeId && last.TrueUseDiscard || responseKind is null && IsKenshangRemovedMaterial(nativeId, lease.CardId) && JiyunDeparture(lease.LeaseId)?.MovementSequence == last.MovementSequence;
    }
    private bool HasJiyunResponseCompletion(CardActionContext action, ProgramCardContinuation continuation)
    {
        if (!HasLiFengCapability || !IsCompletedResponseUse(action, continuation)) return false;
        var nativeId = continuation == ProgramCardContinuation.Dodge ? _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Action?.ActionId == action.ParentActionId)?.Id : ActiveNullificationWindow?.Id;
        if (nativeId is null) return false;
        var kind = continuation == ProgramCardContinuation.Dodge ? ProgramCompletedResponseKind.Dodge : ProgramCompletedResponseKind.Nullification;
        return JiyunLeases().Any(l => _players[l.Source.OwnerSeat].IsAlive && !ProgramEventHistory<JiyunLeaseResolvedEvent>().Any(e => e.LeaseId == l.LeaseId) && JiyunActualPayment(l, action, nativeId.Value, kind) is not null);
    }
    private IReadOnlyList<ProgramCardTriggerCandidate> CollectJiyunCompletedUseCandidates(CardActionContext action, ProgramCardContinuation? continuation)
    {
        if (!HasLiFengCapability || JiyunCompletedOrigin(action, continuation) is not { } origin) return [];
        var result = new List<ProgramCardTriggerCandidate>();
        var leases = JiyunLeases().Where(l => _players[l.Source.OwnerSeat].IsAlive && IsJiyunPile(l.Pile) && !ProgramEventHistory<JiyunLeaseResolvedEvent>().Any(e => e.LeaseId == l.LeaseId) && JiyunActualPayment(l, action, origin.NativeId, origin.ResponseKind) is not null && JiyunTerminalEntity(l, action, origin.NativeId, origin.ResponseKind)).ToArray();
        foreach (var group in leases.GroupBy(l => (l.Source.OwnerSeat, l.Source.SkillInstanceId, l.GameplayHash, l.Pile)))
        {
            var first = group.First(); var t = _contentRegistry.GetSkill("ol:jiyun").Program!.Triggers.Single(t => t.Id == "completed-drawn-entity");
            foreach (var l in group)
            {
                var entry = JiyunActualPayment(l, action, origin.NativeId, origin.ResponseKind)!;
                var fact = new JiyunCompletedUseLeaseIssuedEvent(l.Source, l.GameplayHash, l.Pile, origin.NativeId, action.ActionId, origin.ResponseKind, l.LeaseId, l.CardId, l.PrintedKind, entry.Sequence);
                if (!ProgramEventHistory<JiyunCompletedUseLeaseIssuedEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
            }
            result.Add(new(first.Source.OwnerSeat, action.ActorSeat, "ol:jiyun", t.Id, first.GameplayHash, first.Source.SkillInstanceId, t.Priority));
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private ProgramSkillWindowContext CreateJiyunCompletedUseContext(ProgramCardTriggerWindowFrame mother, ProgramCardTriggerCandidate candidate, ProgramSkillWindowContext ordinary)
    {
        if (candidate.SkillId != "ol:jiyun" || candidate.TriggerId != "completed-drawn-entity") return ordinary;
        var facts = ProgramEventHistory<JiyunCompletedUseLeaseIssuedEvent>().Where(e => e.ActionId == mother.Action.ActionId && e.Source.OwnerSeat == candidate.OwnerSeat && e.Source.SkillInstanceId == candidate.SkillInstanceId && e.GameplayHash == candidate.GameplayHash).OrderBy(e => e.LeaseId).ToArray();
        if (facts.Length == 0) throw new InvalidOperationException("Jiyun lost its frozen completed-use lease entitlement.");
        var first = facts[0];
        return ordinary with { Jiyun = new(first.Source, first.GameplayHash, first.Pile, first.NativeUseId, first.ActionId, first.ResponseKind, facts.Length) };
    }
    private JiyunCompletedEntity[] JiyunCompletedEntities(JiyunCompletedUseContext r) => ProgramEventHistory<JiyunCompletedUseLeaseIssuedEvent>().Where(e => e.ActionId == r.ActionId && e.Source == r.Source && e.GameplayHash == r.GameplayHash).OrderBy(e => e.LeaseId).Select(e => new JiyunCompletedEntity(e.LeaseId, e.CardId, e.PrintedKind, e.PaymentSequence)).ToArray();
    private bool IsIssuedJiyunCompletedUseCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (!HasLiFengCapability || candidate.SkillId != "ol:jiyun" || candidate.BindingId != "completed-drawn-entity" || context.Window != SkillProgramTriggerWindow.CardUseCompleted || context.OwnerSeat != candidate.OwnerSeat || context.Jiyun is not { } r || r.Source.OwnerSeat != candidate.OwnerSeat || r.Source.SkillInstanceId != candidate.SkillInstanceId || r.GameplayHash != candidate.GameplayHash || _contentRegistry.GetSkill(candidate.SkillId).Program!.GameplayHash != r.GameplayHash || context.CardUse?.CardActionId != r.ActionId || _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } parent || parent.Action.ActionId != r.ActionId || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] is not { } c || c.OwnerSeat != candidate.OwnerSeat || c.SkillId != candidate.SkillId || c.SkillInstanceId != candidate.SkillInstanceId || c.TriggerId != candidate.BindingId || c.GameplayHash != candidate.GameplayHash || r.EntityCount <= 0) return false;
        return JiyunCompletedEntities(r).Length == r.EntityCount && (_players[candidate.OwnerSeat].IsAlive || _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.LiFeng is { Paid: true, CompletedUse: { } paid } && paid == r && f.WindowContext == context));
    }
    private bool ValidJiyunClaim(JiyunClaimedEntityEvent e) => ProgramEventHistory<LiFengStartedEvent>().SingleOrDefault(s => s.FrameId == e.FrameId && s.Op == SkillProgramEffectOp.CollectJiyunCompletedUseEntities) is { } started && ProgramEventHistory<LiFengPaidEvent>().Count(p => p.FrameId == e.FrameId && p.Op == started.Op) == 1 && ProgramEventHistory<JiyunCompletedUseLeaseIssuedEvent>().Any(i => i.LeaseId == e.LeaseId && i.ActionId == e.ActionId && i.NativeUseId == e.NativeUseId && i.ResponseKind == e.ResponseKind && i.CardId == e.CardId && i.PrintedKind == e.PrintedKind && i.Source.OwnerSeat == started.Source.OwnerSeat && i.Source.SkillInstanceId == started.Source.SkillInstanceId && i.GameplayHash == started.GameplayHash && i.Pile == started.Pile && i.Pile.Location == e.To) && ProgramEventHistory<JiyunClaimedEntityEvent>().Count(c => c.LeaseId == e.LeaseId) == 1 && _cardMovements.Any(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.CardKind == e.PrintedKind && m.From == e.From && m.To == e.To && m.Reason.Value == JiyunCollectReason);
    private bool IsJiyunClaimedUseEntity(long nativeUseId, int cardId) => ProgramEventHistory<JiyunClaimedEntityEvent>().Any(e => e.NativeUseId == nativeUseId && e.ResponseKind is null && e.CardId == cardId && ValidJiyunClaim(e));
    private bool IsJiyunClaimedResponseEntity(CardActionContext action, ProgramCompletedResponseKind kind, int cardId) => ProgramEventHistory<JiyunClaimedEntityEvent>().Any(e => e.ActionId == action.ActionId && e.ResponseKind == kind && e.CardId == cardId && action.PhysicalCards.Any(c => c.CardId == cardId) && ValidJiyunClaim(e));
}
