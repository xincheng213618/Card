namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 39600)]
    private int AnniversaryHzDijiDamage(IDamageAttempt a) => ActiveAnniversaryHzBody(a.TargetSeat, AnniversaryHzBody.Diji).Count();
    [ProgramDamageHook(ProgramDamagePhase.AppliedBeforeDying, 39600)]
    private bool ObserveAnniversaryHzApplied(IDamageAttempt a, long damageId)
    {
        if (_resolutionStack.SingleOrDefault(f => f.Id == damageId) is not DamageFrame { Amount: > 0 } damage) return false;
        foreach (var status in ActiveAnniversaryHzBody(a.TargetSeat, AnniversaryHzBody.Diji).ToArray())
            AdvanceEventRulesAndQueueFact(new AnniversaryHzStatusConsumedEvent(status.Id, "damage", damageId));
        if (!a.IsSourceLess && ActiveAnniversaryHzZhanjue(a.SourceSeat, 2) is { } issue)
            AdvanceEventRulesAndQueueFact(new AnniversaryHzZhanjueSpentEvent(issue.Id, damageId, damage.Amount));
        return false;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 39600)]
    private void ObserveAnniversaryHzExpiryAndSuit(IGameEvent payload)
    {
        if (!HasAnniversaryHuangZhongCapability) return;
        if (payload is CardActionAcceptedEvent { Action.Type: CardActionType.Use or CardActionType.Response, Action.EffectiveSuit: Suit.Heart } accepted &&
            ActiveAnniversaryHzBody(accepted.Action.ActorSeat, AnniversaryHzBody.Qihai).Any())
            throw new InvalidOperationException("Qihai prohibits the actual effective Heart use or response.");
        int? target = payload switch { TurnEndedEvent e => e.ActorSeat, PlayerDiedEvent e => e.VictimSeat, _ => null };
        if (target is null) return;
        foreach (var issue in ProgramEventHistory<AnniversaryHzBodyIssuedEvent>().Where(e => e.Target == target &&
            e.Body is AnniversaryHzBody.Diji or AnniversaryHzBody.Zhongshu or AnniversaryHzBody.Qihai &&
            !ProgramEventHistory<AnniversaryHzStatusExpiredEvent>().Any(x => x.Id == e.Id)).ToArray())
            AdvanceEventRulesAndQueueFact(new AnniversaryHzStatusExpiredEvent(issue.Id, target.Value, _turnNumber));
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 39600)]
    private void ObserveAnniversaryHzActualUse(IGameEvent payload)
    {
        if (!HasAnniversaryHuangZhongCapability) return;
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { } use)
        {
            if (use.Action is { } action) IssueAnniversaryHzActualUse(action, use.Id);
            else if (IsActualTurnLegacyVirtualUse(use) && ProgramEventHistory<CurrentTurnCardUseKindsRecordedEvent>().Any(e => e.OriginFrameId == use.Id && e.CardActionId is null))
                IssueAnniversaryHzLegacyUse(use);
        }
        else if (payload is CardActionAcceptedEvent accepted && ProgramEventHistory<CurrentTurnCardUseKindsRecordedEvent>().Any(e => e.CardActionId == accepted.Action.ActionId))
        {
            var a = accepted.Action;
            var parent = a.Type == CardActionType.Use ? _resolutionStack.OfType<CardUseFrame>().LastOrDefault(f => f.Action?.ActionId == a.ActionId)?.Id :
                a.EffectiveKind == CardKind.Nullification ? ActiveNullificationWindow?.Id : _resolutionStack.OfType<CardUseFrame>().LastOrDefault(f => f.Action?.ActionId == a.ParentActionId && f.CardAttack?.TargetSeat == a.ActorSeat)?.Id;
            if (parent is { } id) IssueAnniversaryHzActualUse(a, id);
        }
    }
    private void IssueAnniversaryHzActualUse(CardActionContext a, long parent)
    {
        if (a.Type == CardActionType.Use && IsSlashCard(a.EffectiveKind) && ActiveAnniversaryHzZhanjue(a.ActorSeat, 1) is { } issue && LifecycleCardUse(parent) is { Action: { } current } use && current.ActionId == a.ActionId)
        {
            var noResponse = new IssuedCardNoResponse(a.ActionId, parent, issue.Source, _turnNumber, _cardUseDebitPhaseInstanceId);
            UpdateLifecycleCardUse(parent, f => f with { AnniversaryHzSlashIssueId = issue.Id, IssuedNoResponse = f.IssuedNoResponse ?? noResponse });
            AdvanceEventRulesAndQueueFact(new AnniversaryHzZhanjueSpentEvent(issue.Id, parent, 0));
            if (use.IssuedNoResponse is null) AdvanceEventRulesAndQueueFact(new IssuedCardNoResponseEvent(noResponse));
        }
        var statuses = ActiveAnniversaryHzBody(a.ActorSeat, AnniversaryHzBody.Zhongshu).ToArray();
        if (statuses.Length == 0) return;
        var proof = new AnniversaryHzInvalidation(parent, a) { StatusIds = statuses.Select(s => s.Id).ToArray() };
        foreach (var s in statuses) AdvanceEventRulesAndQueueFact(new AnniversaryHzStatusConsumedEvent(s.Id, "use", a.ActionId));
        if (a.Type == CardActionType.Use) UpdateLifecycleCardUse(parent, f => f with { AnniversaryHzInvalidation = proof });
        else if (a.EffectiveKind == CardKind.Dodge) UpdateLifecycleCardUse(parent, f => f with { AnniversaryHzResponses = (f.AnniversaryHzResponses ?? []).Append(proof).ToArray() });
        else if (a.EffectiveKind == CardKind.Nullification && ActiveNullificationWindow is { } counter && counter.Id == parent && counter.NamedAmbushResponse is not null)
            ReplaceNullificationWindowFrame(counter with { AnniversaryHzInvalidation = proof });
        else throw new InvalidOperationException("Zhongshu requires its exact true-use native response parent.");
    }
    private void IssueAnniversaryHzLegacyUse(CardUseFrame use)
    {
        var statuses = ActiveAnniversaryHzBody(use.SourceSeat, AnniversaryHzBody.Zhongshu).ToArray();
        if (statuses.Length == 0) return;
        var proof = new AnniversaryHzInvalidation(use.Id, null, use.SourceSeat, use.CardKind) { StatusIds = statuses.Select(s => s.Id).ToArray() };
        foreach (var s in statuses) AdvanceEventRulesAndQueueFact(new AnniversaryHzStatusConsumedEvent(s.Id, "legacy-use", use.Id));
        UpdateLifecycleCardUse(use.Id, f => f with { AnniversaryHzInvalidation = proof });
    }
    private bool ValidAnniversaryHzInvalidation(AnniversaryHzInvalidation proof, CardActionContext? action, long parent)
    {
        if (proof.ParentId != parent || proof.StatusIds.Count == 0 || proof.StatusIds.Distinct().Count() != proof.StatusIds.Count) return false;
        var legacy = proof.Action is null;
        var actor = legacy ? proof.Actor : proof.Action!.ActorSeat;
        if (legacy ? action is not null || LifecycleCardUse(parent) is not { Action: null } use || use.SourceSeat != actor || use.CardKind != proof.LegacyKind || !IsActualTurnLegacyVirtualUse(use) :
            action is null || !SameAnniversaryHzAction(proof.Action!, action)) return false;
        return proof.StatusIds.All(id => ProgramEventHistory<AnniversaryHzBodyIssuedEvent>().Count(s => s.Id == id && s.Target == actor && s.Body == AnniversaryHzBody.Zhongshu) == 1 &&
            ProgramEventHistory<AnniversaryHzStatusConsumedEvent>().Count(s => s.Id == id && s.Reason == (legacy ? "legacy-use" : "use") && s.Origin == (legacy ? parent : action!.ActionId)) == 1);
    }
    private static bool SameAnniversaryHzAction(CardActionContext a, CardActionContext b) =>
        (a.ActionId, a.ParentActionId, a.Type, a.ActorSeat, a.ProviderSeat, a.RequesterSeat, a.ResponderSeat, a.OpponentSeat, a.EffectiveKind, a.EffectiveSuit, a.EffectiveRank) ==
        (b.ActionId, b.ParentActionId, b.Type, b.ActorSeat, b.ProviderSeat, b.RequesterSeat, b.ResponderSeat, b.OpponentSeat, b.EffectiveKind, b.EffectiveSuit, b.EffectiveRank) &&
        a.PhysicalCards.SequenceEqual(b.PhysicalCards) && a.ConversionChain.SequenceEqual(b.ConversionChain);
    private bool IsAnniversaryHzUseIneffective(CardUseFrame use)
    {
        if (use.AnniversaryHzInvalidation is not { } proof) return false;
        if (!ValidAnniversaryHzInvalidation(proof, use.Action, use.Id)) throw new InvalidOperationException("Zhongshu lost its exact issued whole-use proof.");
        return true;
    }
    private bool HasAnniversaryHzIneffectiveResponse(CardAttackHandle attack, CardActionContext action, ProgramCardContinuation continuation)
    {
        if (continuation != ProgramCardContinuation.Dodge || LifecycleCardUse(attack.ResolutionId)?.AnniversaryHzResponses is not { } proofs) return false;
        var matches = proofs.Where(p => p.Action?.ActionId == action.ActionId).ToArray(); if (matches.Length == 0) return false;
        if (action.ParentActionId != LifecycleCardUse(attack.ResolutionId)?.Action?.ActionId || action.EffectiveKind != CardKind.Dodge || action.ActorSeat != attack.TargetSeat || matches.Any(p => !ValidAnniversaryHzInvalidation(p, action, attack.ResolutionId)))
            throw new InvalidOperationException("Zhongshu's ineffective Dodge changed its true response parent.");
        return true;
    }
    private bool ContinueAnniversaryHzIneffectiveDodge(CardAttackHandle attack, CardActionContext action, ProgramCardContinuation continuation)
    {
        if (!HasAnniversaryHzIneffectiveResponse(attack, action, continuation)) return false;
        ClearRoundOnceCardlessDodgeResponse(attack); ClearTieredRoundDodgeResponse(attack); ClearChainedStateBasicDodgeResponse(attack); ClearDrawFundedDistinctBasicDodgeResponse(attack);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect); if (!ApplyAttackDamage(attack)) CompleteAttack(attack); return true;
    }
    private bool AnniversaryHzCounterspellInvalidated(NullificationWindowFrame pending, CardActionContext action)
    {
        if (pending.AnniversaryHzInvalidation is not { } proof) return false;
        if (pending.NamedAmbushResponse?.ActionId != action.ActionId || !ValidAnniversaryHzInvalidation(proof, action, pending.Id)) throw new InvalidOperationException("Zhongshu's ineffective Nullification lost its exact paid chain node.");
        return true;
    }
    private bool TryAnniversaryHzEquipmentCommitted(long id, Card equipment)
    {
        if (LifecycleCardUse(id) is not { } use || !HasAnniversaryHzPendingInvalidation(use.SourceSeat) && use.AnniversaryHzInvalidation is null) return false;
        var continuation = new ProgramSimpleCardContinuation(equipment.Id, SimpleCardUseEffect.EquipmentPlacement);
        if (TryBeginCommittedCardUse(id, ProgramCardContinuation.CommittedSimpleCard, simple: continuation)) return true;
        return ContinueAnniversaryHzIneffectiveSimple(id, continuation);
    }
    private bool ContinueAnniversaryHzIneffectiveSimple(long id, ProgramSimpleCardContinuation continuation)
    {
        if (LifecycleCardUse(id) is not { } use || !IsAnniversaryHzUseIneffective(use)) return false;
        if (_resolutionStack.LastOrDefault()?.Id != id) throw new InvalidOperationException("Zhongshu lost its original ineffective simple Use return.");
        foreach (var card in GetCardUsePhysicalCards(id).Where(c => _cardZones.GetLocation(c.Id) == CardLocation.Processing)) MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, new("skill.anniversary-huang-zhong.zhongshu.cleanup"));
        var original = use.CardId == 0 ? new Card(0, use.CardKind, Suit.None, 0) : GetAdvancedCard(use.CardId);
        FinishCardUse(id, original, use.CardKind); return true;
    }
    private bool AnniversaryHzHeartForbidden(int actor, IEnumerable<int> ids)
    {
        if (!ActiveAnniversaryHzBody(actor, AnniversaryHzBody.Qihai).Any()) return false;
        var suits = ids.Distinct().Select(id => EffectiveSuit(_players[actor], GetAdvancedCard(id))).Distinct().ToArray();
        return suits is [Suit.Heart];
    }
    [ProgramCardLegality(ProgramCardLegalityStage.MaterialUseBan, 39600)] private bool AnniversaryHzMaterialBan(ProgramCardMaterialQuery q) => AnniversaryHzHeartForbidden(q.ActorSeat, q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan, 39600)] private bool AnniversaryHzResponseBan(ProgramNativeResponseQuery q) => AnniversaryHzHeartForbidden(q.Provider.Seat, q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.AfterLegalActionKind, 39600)] private bool AnniversaryHzProjectedBan(ProgramLegalActionQuery q) => q.Action.Kind != LegalActionKind.Recast && q.Action.CardId is { } id && AnniversaryHzHeartForbidden(q.Actor.Seat, [id]);
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation, 39600)] private void AnniversaryHzSubmittedBan(ProgramSubmittedUseQuery q)
    { if (AnniversaryHzHeartForbidden(q.ActorSeat, q.CardIds)) throw new InvalidOperationException("Qihai prohibits effective Heart use materials."); }
    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 39600)] private bool AnniversaryHzUseDistance(ProgramCardDistanceQuery q) => IsSlashCard(q.Kind) &&
        (q.CardUseFrameId is { } id ? LifecycleCardUse(id)?.AnniversaryHzSlashIssueId is not null : ActiveAnniversaryHzZhanjue(q.Actor.Seat, 1) is not null);
    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 39600)] [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 39600)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 39600)] [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption, 39600)]
    private bool AnniversaryHzSlashDistance(ProgramSlashDistanceQuery q) => IsSlashCard(q.Kind) &&
        (q.ExistingUseFrameId is { } id ? LifecycleCardUse(id)?.AnniversaryHzSlashIssueId is not null : ActiveAnniversaryHzZhanjue(q.Actor.Seat, 1) is not null);
}
