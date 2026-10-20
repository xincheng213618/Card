namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidHuanpeiPayment(HuanpeiPaidOrigin p)
    {
        if (p.Source.SkillId != "ol:huanpei" || p.Source.BindingId != "adjust-and-use-basic" || p.Source.OwnerSeat != p.Request.ActorSeat || p.GameplayHash != _contentRegistry.GetSkill(p.Source.SkillId).Program?.GameplayHash || !HuanpeiKinds.Contains(p.Kind) || p.SequenceBefore < 0 || p.SequenceAfter < p.SequenceBefore || p.Targets is not System.Collections.IList { IsReadOnly: true }) return false;
        return ProgramEventHistory<HuanShuJunStartedEvent>().Count(e => e.FrameId == p.ProgramFrameId && e.Source == p.Source && e.GameplayHash == p.GameplayHash && e.Op == SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse) == 1 &&
            ProgramEventHistory<HuanpeiAdjustmentAcceptedEvent>().Where(e => e.FrameId == p.ProgramFrameId).ToArray() is [var paid] && paid.OwnerSeat == p.Source.OwnerSeat && paid.Round == p.Round && paid.HandCount == p.FrozenHandCount && paid.Hp == p.FrozenHp && paid.RequestedCount == p.AdjustmentCount && paid.Before == p.SequenceBefore &&
            ProgramEventHistory<HuanpeiAdjustmentCompletedEvent>().Count(e => e.FrameId == p.ProgramFrameId && e.Before == p.SequenceBefore && e.After == p.SequenceAfter) == 1 &&
            ProgramEventHistory<HuanpeiOutputChosenEvent>().Count(e => e.FrameId == p.ProgramFrameId && e.Kind == p.Kind && e.TargetCount == p.Targets.Count) == 1 &&
            ProgramEventHistory<HuanpeiTargetChosenEvent>().Where(e => e.FrameId == p.ProgramFrameId).OrderBy(e => e.Index).Select(e => e.TargetSeat).SequenceEqual(p.Targets);
    }
    private static bool SameHuanpeiPayment(HuanpeiPaidOrigin a, HuanpeiPaidOrigin b) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_huan_shu_jun_ol_HuanpeiNative.SameHuanpeiPayment(a,b);
    private CardActionContext PrepareHuanpeiAction(CardActionContext a, HuanpeiPaidOrigin? p) => p is null ? a :
        CaptureFactionAction(new CardActionContext(a.ActionId, a.ParentActionId, a.Type, a.ActorSeat, a.ProviderSeat, a.RequesterSeat, a.ResponderSeat, a.OpponentSeat, a.EffectiveKind, a.TargetSeats, a.PhysicalCards, a.ConversionChain, a.DesignatedTargetSeats, effectiveSuit: Suit.None, effectiveRank: 0, effectiveIsRed: null));
    private void IssueHuanpeiUse(CardUseFrame use, HuanpeiPaidOrigin? p)
    {
        if (p is null) return;
        if (!ValidHuanpeiPayment(p) || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: null } a || a.ActorSeat != p.Source.OwnerSeat || a.ProviderSeat != a.ActorSeat || a.RequesterSeat is not null || a.EffectiveKind != p.Kind || a.ConversionChain is not [var source] || source != p.Source || ProgramEventHistory<HuanpeiIssuedEvent>().Any(e => e.PaymentFrameId == p.ProgramFrameId)) throw new InvalidOperationException("Huanpei requires its exact paid, zero-material native Use.");
        UpdateLifecycleCardUse(use.Id, u => u with { HuanpeiUse = new(p, use.Id, a.ActionId) });
        if (p.Request.ParentFrameId is { } parentId && _resolutionStack.SingleOrDefault(x => x.Id == parentId) is ProgramSkillFrame parent)
            ReplaceRuntimeFrame(parent.Id, parent with { HuanpeiReturn = new(p, use.Id, a.ActionId) });
        AdvanceEventRulesAndQueueFact(new HuanpeiIssuedEvent(p.ProgramFrameId, use.Id, a.ActionId, a.ActorSeat, p.Kind, p.Request.Intent));
    }
    private bool IsIssuedHuanpeiUse(CardUseFrame use)
    {
        if (use.HuanpeiUse is not { } r || r.OwnerFrameId != use.Id || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use } current || !ValidHuanpeiPayment(r.Payment)) return false;
        var a = current;
        if (use.CurrentSlashFirePolicy is { } fire) { AssertCurrentSlashFirePolicy(use); a = fire.OriginalAction; }
        var p = r.Payment;
        return a.ActionId == r.CardActionId && a.ProviderSeat == p.Source.OwnerSeat && a.RequesterSeat is null && a.PhysicalCards.Count == 0 && a.ConversionChain is [var source] && source == p.Source && a.EffectiveKind == p.Kind && a.EffectiveSuit == Suit.None && a.EffectiveRank == 0 && a.EffectiveIsRed is null && (a.ActorSeat == p.Source.OwnerSeat && use.SourceSeat == a.ActorSeat || MatchesDeclaredActualDamageUse(use, a.ActorSeat, p.Source.OwnerSeat)) &&
            ProgramEventHistory<HuanpeiIssuedEvent>().Count(e => e.PaymentFrameId == p.ProgramFrameId && e.UseId == use.Id && e.ActionId == a.ActionId && e.ActorSeat == p.Source.OwnerSeat && e.Kind == p.Kind && e.Intent == p.Request.Intent) == 1;
    }
    private bool IsHuanpeiUse(long id) => LifecycleCardUse(id) is { } use && IsIssuedHuanpeiUse(use);
    private Card HuanpeiRepresentation(long id) => IsHuanpeiUse(id) ? new(0, LifecycleCardUse(id)!.CardKind, Suit.None, 0) : throw new InvalidOperationException("Huanpei's neutral native representation lost its paid Use.");
    private bool SkipHuanpeiFinishedMovement(long id, Card c) => c.Id == 0 && IsHuanpeiUse(id) && c.Kind == LifecycleCardUse(id)!.CardKind;
    private bool IsHuanpeiAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { HuanpeiUse: { } r } use || !IsIssuedHuanpeiUse(use) || attack.Card is not null || attack.PhysicalCards.Count != 0 || !IsSlashCard(use.CardKind) || attack.SourceSeat != use.SourceSeat || attack.EffectiveCardKind != use.CardKind) return false;
        var position = _resolutionStack.FindIndex(f => f.Id == use.Id);
        var ancestors = _resolutionStack.Take(position).OfType<CardUseFrame>().SelectMany(f => f.PhysicalCardIds ?? []).Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).Distinct().Order().ToArray();
        return processing.Select(c => c.Id).Distinct().Order().SequenceEqual(ancestors) && (r.Payment.Request.ParentSkillId is null || attack.ProgramSkillCardUseFrameId == r.Payment.Request.ParentFrameId && _resolutionStack.SingleOrDefault(f => f.Id == r.Payment.Request.ParentFrameId) is ProgramSkillFrame p && HasHuanpeiProgramPayment(p, use));
    }
    private bool ContinueHuanpeiSimpleUse(long id, ProgramSimpleCardContinuation c)
    {
        if (c.CardId != 0 || !IsHuanpeiUse(id)) return false;
        var use = LifecycleCardUse(id)!; var owner = _players[use.SourceSeat]; var card = HuanpeiRepresentation(id); SetCardUseStep(id, ResolutionFrameStep.ResolvingEffect);
        if (!owner.IsAlive || _winner != Winner.None) { FinishCardUse(id, card, use.CardKind); return true; }
        if (c.Effect == SimpleCardUseEffect.Recovery && use.CardKind is CardKind.Peach or CardKind.Alcohol) CompleteRecoveryCardUse(owner, _players[use.TargetSeats[use.TargetIndex]], card, id, use.CardKind, c.RecoveryAmount, c.RecoveryPolicySources ?? []);
        else if (c.Effect == SimpleCardUseEffect.Alcohol && use.CardKind == CardKind.Alcohol) CompleteAlcoholUse(owner, card, id);
        else throw new InvalidOperationException("Huanpei changed its accepted native basic effect."); return true;
    }
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 40)]
    private bool FinishHuanpeiAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false; if (attack.Card is not null || !IsHuanpeiUse(attack.ResolutionId) || !IsSlashCard(attack.EffectiveCardKind ?? CardKind.Dodge)) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!; SetCardUseStep(use.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); } return true;
    }
    [NativeUseValidation(28100)] private void ValidateHuanpeiUse(CardUseFrame use)
    { if (use.HuanpeiUse is not null && (!IsIssuedHuanpeiUse(use) || use.Step != ResolutionFrameStep.Completed || use.PendingRecoveryAttempts is { Count: > 0 } || use.RecoveryPaidContinuation is not null)) throw new InvalidOperationException("Huanpei cannot return before its whole native Use and children."); }
    [NativeUseReturn(17)] private void ReturnHuanpeiUse(CardUseFrame use)
    {
        if (use.HuanpeiUse is not { } r) return;
        if (LifecycleCardUse(use.Id) is not null || use.Step != ResolutionFrameStep.Completed || ProgramEventHistory<HuanpeiReturnedEvent>().Any(e => e.PaymentFrameId == r.Payment.ProgramFrameId)) throw new InvalidOperationException("Huanpei can return only once after the real native Use.");
        if (r.Payment.Request.ParentFrameId is { } id && _resolutionStack.SingleOrDefault(x => x.Id == id) is ProgramSkillFrame { HuanpeiReturn: { } lease } p)
        { if (!SameHuanpeiPayment(lease.Payment, r.Payment) || lease.UseId != use.Id || lease.ActionId != r.CardActionId) throw new InvalidOperationException("Huanpei returned to another native program request."); ReplaceRuntimeFrame(p.Id, p with { HuanpeiReturn = lease with { Returned = true } }); }
        AdvanceEventRulesAndQueueFact(new HuanpeiReturnedEvent(r.Payment.ProgramFrameId, use.Id, r.CardActionId));
        if (r.Payment.Request.ParentFrameId is { } genericId && _resolutionStack.SingleOrDefault(x => x.Id == genericId) is ProgramSkillFrame generic &&
            r.Payment.Request.ParentOperation is SkillProgramEffectOp.RequestSlashByTarget or SkillProgramEffectOp.RequestSlashByNearest or SkillProgramEffectOp.RequestSlashAgainstChosenTarget or SkillProgramEffectOp.RequestLegalSlashByNearest or SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner)
        {
            if (!HasHuanpeiProgramPayment(generic) || generic.HuanpeiReturn?.Returned != true) throw new InvalidOperationException("Huanpei's generic native request has not actually returned.");
            ReplaceRuntimeFrame(generic.Id, generic with { HuanpeiReturn = null });
        }
    }
    private bool HasHuanpeiProgramPayment(ProgramSkillFrame f, CardUseFrame? use = null)
    {
        if (f.HuanpeiReturn is not { } r || !ValidHuanpeiPayment(r.Payment) || r.Payment.Request is not { ParentFrameId: { } parent, RequestFrameId: { } request } n || parent != f.Id || request != f.Id || n.Cursor != f.InstructionIndex || n.ParentSkillId != f.SkillId || n.ParentSkillInstanceId != f.SkillInstanceId || n.ParentGameplayHash != f.GameplayHash || n.ParentOperation != GetPausedPrivateOfferEffect(f)?.Op) return false;
        if (use is not null && (use.HuanpeiUse is not { } u || !SameHuanpeiPayment(u.Payment, r.Payment) || u.OwnerFrameId != r.UseId || u.CardActionId != r.ActionId || !IsIssuedHuanpeiUse(use))) return false;
        return ProgramEventHistory<HuanpeiIssuedEvent>().Count(e => e.PaymentFrameId == r.Payment.ProgramFrameId && e.UseId == r.UseId && e.ActionId == r.ActionId) == 1 && (!r.Returned || LifecycleCardUse(r.UseId) is null && ProgramEventHistory<HuanpeiReturnedEvent>().Count(e => e.PaymentFrameId == r.Payment.ProgramFrameId && e.UseId == r.UseId && e.ActionId == r.ActionId) == 1);
    }
    private void ExecuteHuanpeiPaid(HuanpeiPaidOrigin p)
    {
        if (!ValidHuanpeiPayment(p) || !_players[p.Source.OwnerSeat].IsAlive || !HuanpeiRequestParentCurrent(p.Request)) return;
        PrepareHuanpeiOriginalParent(p);
        var actor = _players[p.Source.OwnerSeat]; var kind = p.Kind; var card = new Card(0, kind, Suit.None, 0);
        if (p.Request.Intent == HuanpeiIntent.OwnSlashDodge) { ResolveHuanpeiDodge(p); return; }
        if (p.Request.Intent == HuanpeiIntent.Dying)
        {
            var d = ActiveDying!;
            var policies = kind == CardKind.Peach && actor.Seat != d.VictimSeat ? CardPolicies(_players[d.VictimSeat], SkillProgramCardPolicyKind.RescueRecoveryBonus, CardKind.Peach).Where(policy => string.Equals(GetEffectiveFactionId(actor), policy.Policy.FactionId, StringComparison.Ordinal)).ToArray() : [];
            SetDyingFrameStep(d.FrameId, ResolutionFrameStep.ResolvingEffect);
            ResolveRecoveryCard(actor, _players[d.VictimSeat], card, kind == CardKind.Peach ? "桃" : "酒", kind, 1 + policies.Sum(policy => policy.Policy.Value), recoveryPolicySources: policies.Select(policy => (policy.Source.SkillId, policy.Policy.Id)).ToArray(), conversionSource: p.Source, dyingResponse: new(d.FrameId, actor.Seat, kind == CardKind.Peach, kind == CardKind.Peach ? 0 : null, kind == CardKind.Alcohol, kind == CardKind.Alcohol ? 0 : null), physicalCards: [], huanpeiOrigin: p); return;
        }
        if (p.Request.Intent == HuanpeiIntent.WenyiPeach)
        {
            var parent = GetActiveProgramFrame(p.Request.ParentFrameId!.Value);
            if (!parent.HuanShuJun!.Paid) PrepareWenyiSuppliedPeach(parent, actor.Seat, [], p.Source);
            ResolveRecoveryCard(actor, _players[p.Request.FixedTargetSeat!.Value], card, "桃", CardKind.Peach, conversionSource: p.Source, physicalCards: [], huanpeiOrigin: p); return;
        }
        if (p.Request.Intent is HuanpeiIntent.Play or HuanpeiIntent.KeBiNengBasic)
        {
            if (kind is CardKind.Peach or CardKind.Alcohol && p.Targets.Count > 1) _selectedNextCardTargetSeats = p.Targets;
            if (kind == CardKind.Peach) { ResolveRecoveryCard(actor, actor, card, "桃", kind, conversionSource: p.Source, physicalCards: [], huanpeiOrigin: p); return; }
            if (kind == CardKind.Alcohol) { var id = BeginCardUse(card, actor.Seat, [], kind, physicalCardIds: [], conversionSource: p.Source, huanpeiOrigin: p); RecordMouZhaJiangAwareAlcoholDebit(id, actor); BeginSimpleCardUse(id, new(0, SimpleCardUseEffect.Alcohol)); return; }
            BeginHuanpeiSlash(p, p.Request.Intent == HuanpeiIntent.Play, null); return;
        }
        if (p.Request.Intent == HuanpeiIntent.BorrowedSword)
        { var b = ActiveBorrowedSword!; PopResponseWindow(b.ResolutionId); SetCardUseStep(b.ResolutionId, ResolutionFrameStep.ResolvingEffect); b.AwaitingSlashChoice = false; b.SlashCardId = 0; b.EffectiveSlashKind = kind; BeginHuanpeiSlash(p, true, b); return; }
        if (p.Request.Intent == HuanpeiIntent.Qinglong)
        { var outer = ActiveQinglongCrescentBlade!.Attack; ActiveQinglongCrescentBlade = null; SetCardUseStep(outer.ResolutionId, ResolutionFrameStep.ResolvingEffect); AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(outer.ResolutionId, actor.Seat, p.Request.FixedTargetSeat!.Value, true, [], kind)); SuspendContinuationForQinglongFollowup(outer.ResolutionId); BeginHuanpeiSlash(p, false, null); return; }
        var parentProgram = GetActiveProgramFrame(p.Request.ParentFrameId!.Value); var effect = GetPausedPrivateOfferEffect(parentProgram)!;
        if (IsSayingSlashRequest(parentProgram)) PrepareSayingSuppliedSlash(parentProgram, actor.Seat, actor.Seat, p.Targets.Single(), kind, [], p.Source);
        else if (IsChanshuangSlashRequest(parentProgram)) PrepareChanshuangSuppliedSlash(parentProgram, actor.Seat, actor.Seat, p.Targets.Single(), kind, [], p.Source);
        else if (IsQigongRequest(parentProgram) || IsWuJingHejiRequest(parentProgram) || parentProgram.CaoChun?.Stage == CaoChunStage.NativeSlash) { }
        else if (p.Request.Intent == HuanpeiIntent.ProgramSlash) CommitProgramChoiceResult(parentProgram.Id, effect.ResultBind!, "used-slash", actor.Seat, "环佩：视为使用杀。");
        else if (effect.Op == SkillProgramEffectOp.RequestSlashAgainstChosenTarget) { ReplaceRuntimeTop(parentProgram = parentProgram with { AssistedSlashRequest = null }); CommitProgramChoiceResult(parentProgram.Id, effect.ResultBind!, "used-slash", actor.Seat, "环佩：视为使用杀。"); }
        else if (effect.Op == SkillProgramEffectOp.RequestLegalSlashByNearest) ReplaceRuntimeTop(parentProgram with { NearestLegalSlashRequest = null });
        else if (p.Request.Intent == HuanpeiIntent.FixedSlash) ReplaceRuntimeTop(parentProgram with { FixedTargetSlash = parentProgram.FixedTargetSlash! with { Issued = true } });
        BeginHuanpeiSlash(p, false, null);
    }
    private void BeginHuanpeiSlash(HuanpeiPaidOrigin p, bool counts, BorrowedSwordHandle? borrowed)
    {
        var actor = _players[p.Source.OwnerSeat]; var card = new Card(0, p.Kind, Suit.None, 0); var program = p.Request.ParentSkillId is null ? (long?)null : p.Request.ParentFrameId;
        var id = BeginCardUse(card, actor.Seat, p.Targets, p.Kind, physicalCardIds: [], conversionSource: p.Source, huanpeiOrigin: p); var targets = LifecycleCardUse(id)!.TargetSeats;
        var attack = new CardAttackHandle(this, id, actor.Seat, targets[0], card: null, damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: p.Kind, ignoresArmor: HasCardArmorBypass(actor, _players[targets[0]], p.Kind), programSkillCardUseFrameId: program);
        if (borrowed is not null) borrowed.ActiveAttack = attack; ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack);
        if (counts && _phase == TurnPhase.Play && actor.Seat == _currentSeat && LifecycleCardUse(id)?.UnlimitedUse != true && !IgnoresProgramSlashLimit(actor, p.Source)) RecordSlashUseDebit(id, actor.Seat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, p.Kind); CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, p.Kind, actor.Seat, targets[0], IgnoresArmor: attack.IgnoresArmor)); NotifyAiOfSlash(actor, _players[targets[0]]);
        if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted, targets, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
}
