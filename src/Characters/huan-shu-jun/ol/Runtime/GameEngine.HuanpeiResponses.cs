namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ResolveHuanpeiDodge(HuanpeiPaidOrigin p)
    {
        var attack = ActiveCardAttack ?? throw new InvalidOperationException("Huanpei lost its original incoming Slash."); var use = LifecycleCardUse(attack.ResolutionId)!; var actor = _players[p.Source.OwnerSeat];
        if (!ValidHuanpeiPayment(p) || p.Request.Intent != HuanpeiIntent.OwnSlashDodge || p.Request.ParentFrameId != use.Id || p.Request.ParentActionId != use.Action?.ActionId || p.Request.Cursor != attack.SuccessfulDodgeResponses || attack.TargetSeat != actor.Seat || ActiveFactionDefense is not null || !IsProgramResponseCardUse(actor, CardKind.Dodge)) throw new InvalidOperationException("Huanpei cannot replace a pure Played or another native Dodge request.");
        PopResponseWindow(use.Id); SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect);
        var a = CaptureFactionAction(new CardActionContext(++_cardActionSequence, use.Action?.ActionId, CardActionType.Response, actor.Seat, actor.Seat, null, actor.Seat, attack.SourceSeat, CardKind.Dodge, [], [], [p.Source], effectiveSuit: Suit.None, effectiveRank: 0, effectiveIsRed: null));
        UpdateLifecycleCardUse(use.Id, u => u with { HuanpeiResponse = new(p, use.Id, a.ActionId) });
        AdvanceEventRulesAndQueueFact(new HuanpeiIssuedEvent(p.ProgramFrameId, use.Id, a.ActionId, actor.Seat, CardKind.Dodge, p.Request.Intent));
        RecordProgramUsedBasicCard(actor.Seat, CardKind.Dodge); RecordActualPlayPhaseUse(a);
        AdvanceEventRulesAndQueueFact(new CardRespondedEvent(0, actor.Seat, attack.SourceSeat, CardKind.Dodge)); AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(a));
        if (TryBeginCommittedResponseUsePrograms(attack, a, ProgramCardContinuation.Dodge)) return;
        if (TryBeginProgramCardWindow(attack, a, SkillProgramTriggerWindow.CardResponseAccepted, [attack.SourceSeat], ProgramCardContinuation.Dodge)) return;
        if (HasResponseUseCompletionObserver(a, ProgramCardContinuation.Dodge) || HasCardResponseCompletedObserver(a, ProgramCardContinuation.Dodge)) ContinueAcceptedCardResponse(attack, a, ProgramCardContinuation.Dodge); else CompleteSuccessfulDodgeResponse(attack);
    }
    private CardActionContext RequireHuanpeiResponseAction(CardUseFrame use)
    {
        var r = use.HuanpeiResponse ?? throw new InvalidOperationException("Huanpei's own Dodge has no issued receipt."); var p = r.Payment;
        var accepted = ProgramEventHistory<CardActionAcceptedEvent>().Where(e => e.Action.ActionId == r.CardActionId).ToArray();
        if (!ValidHuanpeiPayment(p) || r.OwnerFrameId != use.Id || p.Kind != CardKind.Dodge || p.Request.Intent != HuanpeiIntent.OwnSlashDodge || use.CardAttack is not { } attack || attack.TargetSeat != p.Source.OwnerSeat || attack.SuccessfulDodgeResponses != p.Request.Cursor || p.Request.ParentFrameId != use.Id || p.Request.ParentActionId != use.Action?.ActionId || accepted is not [var fact] || fact.Action is not { Type: CardActionType.Response, EffectiveKind: CardKind.Dodge, PhysicalCards.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: null } a || a.ActorSeat != p.Source.OwnerSeat || a.ProviderSeat != a.ActorSeat || a.ResponderSeat != a.ActorSeat || a.RequesterSeat is not null || a.OpponentSeat != attack.SourceSeat || a.ConversionChain is not [var source] || source != p.Source || a.ParentActionId != use.Action?.ActionId || ProgramEventHistory<HuanpeiIssuedEvent>().Count(e => e.PaymentFrameId == p.ProgramFrameId && e.ActionId == a.ActionId && e.UseId == use.Id && e.Intent == p.Request.Intent) != 1) throw new InvalidOperationException("Huanpei's true Dodge changed its accepted action or original native cursor.");
        return a;
    }
    private void ClearHuanpeiDodgeResponse(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { HuanpeiResponse: { } r } use) return;
        var a = RequireHuanpeiResponseAction(use);
        if (_resolutionStack.LastOrDefault()?.Id != use.Id || _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w => w.Action.ActionId == a.ActionId) || use.RecoveryPaidContinuation is not null || use.PendingRecoveryAttempts is { Count: > 0 }) throw new InvalidOperationException("Huanpei cannot clear its native Dodge while a child remains.");
        AdvanceEventRulesAndQueueFact(new HuanpeiReturnedEvent(r.Payment.ProgramFrameId, use.Id, a.ActionId)); UpdateLifecycleCardUse(use.Id, u => u with { HuanpeiResponse = null });
    }
    private bool HasHuanpeiNativeRequestContinuation(long parentId) => HuanpeiActiveDraft() is { HuanShuJun.Request: { } n } f && n.ParentFrameId == parentId && HuanpeiRequestParentCurrent(n) && HuanShuJunPaidOrPreparing(f) && (_resolutionStack.LastOrDefault()?.Id == f.Id || HuanShuJunObserverRoot()?.Id == f.Id);
    private void AssertHuanpeiRequests()
    {
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.HuanShuJun?.Op == SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse))
        { AssertHuanShuJun(f); if (f.HuanShuJun!.Request is not { } n || n.Intent != HuanpeiIntent.Play && (!HuanpeiRequestParentCurrent(n) || f.HuanShuJun.OriginalDecision is not { } original || original.PromptId != n.OriginalPromptId || original.Revision != n.OriginalRevision)) throw new InvalidOperationException("Huanpei lost its frozen genuine native request."); }
    }
    private bool HuanpeiDyingRescueRide(int index, DyingFrame dying)
    {
        if (index < 0 || index + 1 >= _resolutionStack.Count || _resolutionStack[index] != dying || ActiveDying?.FrameId != dying.Id) return false;
        if (_resolutionStack[index + 1] is ProgramSkillFrame { HuanShuJun: { Op: SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse, Request: { Intent: HuanpeiIntent.Dying } n } } preparation)
            return n.ParentFrameId == dying.Id && n.RequestFrameId == dying.Id && n.ActorSeat == dying.ResponderSeat && n.Cursor == dying.ResponderIndex && n.FixedTargetSeat == dying.VictimSeat && HuanShuJunPaidOrPreparing(preparation) && HuanShuJunObserverSuffix(index + 1);
        if (_resolutionStack[index + 1] is not CardUseFrame { HuanpeiUse: { } receipt } use || !IsIssuedHuanpeiUse(use) || receipt.Payment.Request.Intent != HuanpeiIntent.Dying || receipt.Payment.Request.ParentFrameId != dying.Id || receipt.Payment.Request.Cursor != dying.ResponderIndex || use.SourceSeat != dying.ResponderSeat || use.DyingResponse is not { } token || token.ResolutionId != dying.Id || !use.TargetSeats.SequenceEqual([dying.VictimSeat]) || token.ResponderSeat != dying.ResponderSeat) return false;
        for (var i = index + 2; i < _resolutionStack.Count; i++) if (!ParticipantHandRescueObserverRide(_resolutionStack[i], _resolutionStack[i - 1], use)) return false;
        return true;
    }
    private bool HasHuanpeiForcedSlash(CharacterState actor, CharacterState target, BorrowedSwordHandle pending) => SameContinuationOwner(ActiveBorrowedSword, pending) && LifecycleCardUse(pending.ResolutionId) is { } use && _resolutionStack.LastOrDefault()?.Id == use.Id && pending.ActiveAttack is null && pending.WeaponOwnerSeat == actor.Seat && pending.SlashTargetSeat == target.Seat && GetWeapon(actor) is not null && HasHuanpeiForcedSlash(actor, target, false);
    private bool HasHuanpeiForcedSlash(CharacterState actor, CharacterState target, QinglongCrescentBladeHandle pending) => SameAttackOwner(ActiveCardAttack, pending.Attack) && LifecycleCardUse(pending.Attack.ResolutionId) is { } use && _resolutionStack.LastOrDefault()?.Id == use.Id && pending.Attack.SourceSeat == actor.Seat && pending.Attack.TargetSeat == target.Seat && HasHuanpeiForcedSlash(actor, target, true);
    private bool HuanpeiPausedRequestRide(ProgramSkillFrame f, long owner)
    {
        if (f.HuanShuJun is not { Paid: true, Request: { } n, OriginalDecision: { } original } r || n.ParentFrameId != owner || !HuanpeiRequestParentCurrent(n) || !HuanShuJunPaidOrPreparing(f) ||
            ProgramEventHistory<HuanpeiAdjustmentAcceptedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.Round == r.Round) != 1 ||
            ProgramEventHistory<HuanpeiRequestCapturedEvent>().Count(e => e.FrameId == f.Id && e.Request == n && e.DecisionHash == HuanShuJunDecisionHash(original)) != 1) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (i < 1 || _resolutionStack[i - 1].Id != n.RequestFrameId) return false;
        return _resolutionStack.LastOrDefault()?.Id == f.Id || HuanShuJunObserverRoot()?.Id == f.Id;
    }
    private bool HasHuanpeiBorrowedPaymentRide(BorrowedSwordHandle pending) => HuanpeiActiveDraft() is { HuanShuJun: { Request: { Intent: HuanpeiIntent.BorrowedSword } n, OriginalDecision: { Kind: DecisionKind.RespondSlash } original } } f &&
        SameContinuationOwner(ActiveBorrowedSword, pending) && pending.ActiveAttack is null && pending.AwaitingSlashChoice && pending.WeaponOwnerSeat == f.OwnerSeat && pending.SlashTargetSeat == n.FixedTargetSeat && n.Cursor == 0 && original.PlayerSeat == f.OwnerSeat && original.TargetSeat == pending.SlashTargetSeat && LifecycleCardUse(pending.ResolutionId) is { Action: { } action } && n.ParentActionId == action.ActionId && HuanpeiPausedRequestRide(f, pending.ResolutionId);
    private bool HasHuanpeiQinglongPaymentRide(CardAttackHandle attack) => HuanpeiActiveDraft() is { HuanShuJun: { Request: { Intent: HuanpeiIntent.Qinglong } n, OriginalDecision: { Kind: DecisionKind.QinglongCrescentBlade } original } } f &&
        ActiveQinglongCrescentBlade is { } blade && SameAttackOwner(blade.Attack, attack) && SameAttackOwner(ActiveCardAttack, attack) && attack.SourceSeat == f.OwnerSeat && attack.TargetSeat == n.FixedTargetSeat && n.Cursor == 0 && original.PlayerSeat == f.OwnerSeat && original.TargetSeat == attack.TargetSeat && original.IncomingCard == attack.EffectiveCardKind && original.ValidTargetSeats.SequenceEqual([attack.TargetSeat]) && LifecycleCardUse(attack.ResolutionId) is { Action: { } action } && n.ParentActionId == action.ActionId && HuanpeiPausedRequestRide(f, attack.ResolutionId);
}
