namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void IssueBoundaryLiuFengSlash(ProgramSkillFrame f)
    {
        var r = f.BoundaryLiuFeng!;
        if (!ValidBoundaryLiuFeng(f) || r.Stage != BoundaryLiuFengStage.Issuing || r.Targets is not [var target]) throw new InvalidOperationException("Qinling must issue from its exact once-paid cost frame.");
        var actor = _players[f.OwnerSeat];
        var id = BeginCardUse(new(0, CardKind.Slash, Suit.None, 0), actor.Seat, [target], CardKind.Slash, physicalCardIds: [], conversionSource: r.Need is null ? null : r.Source);
        var use = LifecycleCardUse(id)!;
        if (use.BoundaryLiuFeng is not { FrameId: var producer } || producer != f.Id) throw new InvalidOperationException("Qinling lost its original zero-material use origin.");
        var attack = new CardAttackHandle(this, id, actor.Seat, use.TargetSeats[0], card: null, damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: CardKind.Slash, ignoresArmor: HasCardArmorBypass(actor, _players[use.TargetSeats[0]], CardKind.Slash), programSkillCardUseFrameId: f.Id);
        ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack); CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
        if (r.Need?.Intent == BoundaryLiuFengIntent.BorrowedSword) ActiveBorrowedSword!.ActiveAttack = attack;
        CompleteQigongNativePayment(id); CompleteCaoChunNativePayment(id);
        if (BoundaryLiuFengCountsSlash(f) && use.UnlimitedUse != true) RecordSlashUseDebit(id, actor.Seat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, CardKind.Slash); AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actor.Seat, use.TargetSeats[0], IgnoresArmor: attack.IgnoresArmor)); NotifyAiOfSlash(actor, _players[use.TargetSeats[0]]);
        if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted, LifecycleCardUse(id)!.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 50201)] private void BindBoundaryLiuFengUse(CardUseDeclaredEvent e)
    { if (LifecycleCardUse(e.ResolutionId) is { } use) BindBoundaryLiuFengNativeUse(use); }
    private void BindBoundaryLiuFengNativeUse(CardUseFrame use)
    {
        if (use.BoundaryLiuFeng is not null || use.Action is not { Type: CardActionType.Use } a) return;
        var at = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (at < 1 || _resolutionStack[at - 1] is not ProgramSkillFrame { BoundaryLiuFeng: { Stage: BoundaryLiuFengStage.Issuing } r } f) return;
        if (!ValidBoundaryLiuFeng(f) || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || a.PhysicalCards.Count != 0 || a.ActorSeat != f.OwnerSeat || a.ProviderSeat != f.OwnerSeat || a.EffectiveKind != CardKind.Slash || !a.ConversionChain.SequenceEqual(r.Need is null ? [] : new[] { r.Source }) || !r.Targets.SequenceEqual(use.TargetSeats) || r.CostSequence <= 0) throw new InvalidOperationException("Qinling cannot turn its discarded cost into use materials or change its real actor.");
        var origin = new BoundaryLiuFengUseOrigin(f.Id, r.Source, r.Hash, use.Id, a.ActionId, r.Turn, r.TurnOwner, f.OwnerSeat, 1 + _players[f.OwnerSeat].AlcoholDamageBonus);
        ReplaceRuntimeFrame(f.Id, f with { BoundaryLiuFeng = r with { Stage = BoundaryLiuFengStage.NativeUse, UseId = use.Id, ActionId = a.ActionId, BaseDamage = origin.BaseDamage } });
        UpdateLifecycleCardUse(use.Id, u => u with { BoundaryLiuFeng = origin }); AdvanceEventRulesAndQueueFact(new BoundaryLiuFengUseIssuedEvent(origin));
        if (r.Need is { } n)
        {
            if (_resolutionStack.SingleOrDefault(x => x.Id == n.OwnerFrameId) is ProgramSkillFrame { BoundaryLiuFengCaller: { } caller } parent)
                ReplaceRuntimeFrame(parent.Id, parent with { BoundaryLiuFengCaller = caller with { UseId = use.Id, ActionId = a.ActionId } });
            if (n.Intent == BoundaryLiuFengIntent.Qinglong)
            { var original = LifecycleCardUse(n.OwnerFrameId)!; var suspension = original.Continuations.QinglongFollowup ?? throw new InvalidOperationException("Qinling lost its suspended Qinglong request."); if (suspension.FollowupUseId is not null) throw new InvalidOperationException("Qinling cannot issue two follow-ups for one Qinglong request."); UpdateCardContinuations(original.Id, s => s with { QinglongFollowup = suspension with { FollowupUseId = use.Id } }); }
        }
    }
    private bool IsBoundaryLiuFengUse(CardUseFrame use)
    {
        if (use.BoundaryLiuFeng is not { } o || o.UseId != use.Id || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } current || current.ActionId != o.ActionId || current.ProviderSeat != o.Actor || !IsSlashCard(use.CardKind) || current.EffectiveKind != use.CardKind) return false;
        var a = current;
        if (use.CurrentSlashFirePolicy is { } fire) { AssertCurrentSlashFirePolicy(use); a = fire.OriginalAction; }
        var actorMatches = ShownEntityUseActorMatches(use, o.Actor, o.Actor) || !ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id) && use.SourceSeat == o.Actor && a.ActorSeat == o.Actor;
        return actorMatches && a.EffectiveKind == CardKind.Slash && (a.ConversionChain.Count == 0 || a.ConversionChain is [var s] && s == o.Source) && _contentRegistry.GetSkill(o.Source.SkillId).Program?.GameplayHash == o.Hash && ProgramEventHistory<BoundaryLiuFengUseIssuedEvent>().Count(e => e.Origin == o) == 1 && ProgramEventHistory<BoundaryLiuFengCostPaidEvent>().Count(e => e.FrameId == o.FrameId && e.Source == o.Source && e.Hash == o.Hash && e.Turn == o.Turn && e.TurnOwner == o.TurnOwner) == 1;
    }
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 50201)] private bool CompleteBoundaryLiuFengAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (attack.Card is not null || LifecycleCardUse(attack.ResolutionId) is not { BoundaryLiuFeng: { } o } use || !IsBoundaryLiuFengUse(use)) return false;
        var next = use.TargetIndex + 1; while (next < use.TargetSeats.Count && !_players[use.TargetSeats[next]].IsAlive) next++;
        if (!IsTargetScopedZixiEffectRound(use.Id) && _winner == Winner.None && next < use.TargetSeats.Count)
        {
            SetCardUseTargetIndex(use.Id, next); SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect);
            var continued = new CardAttackHandle(this, use.Id, use.SourceSeat, use.TargetSeats[next], card: null, damageAmount: use.ProgramAdjustedSlashBaseDamage ?? o.BaseDamage, playedCardKind: use.CardKind, ignoresArmor: HasCardArmorBypass(_players[use.SourceSeat], _players[use.TargetSeats[next]], use.CardKind), programSkillCardUseFrameId: o.FrameId);
            continued.SetCardUseCausedDamage(attack.CardUseCausedDamage); ActiveCardAttack = continued; ActiveDuel = null; ClearPendingDecision(); BeginSlashTargetResolution(continued); paused = true; return true;
        }
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }
    [NativeUseValidation(50201)] private void ValidateBoundaryLiuFengUse(CardUseFrame use) { if (use.BoundaryLiuFeng is not null && (!IsBoundaryLiuFengUse(use) || use.Step != ResolutionFrameStep.Completed)) throw new InvalidOperationException("Qinling must await the whole real Slash and its native children."); }
    [NativeUseReturn(1)] private void ReturnBoundaryLiuFengUse(CardUseFrame use)
    {
        if (use.BoundaryLiuFeng is not { } o) return;
        var f = _resolutionStack.OfType<ProgramSkillFrame>().Single(x => x.Id == o.FrameId); var r = f.BoundaryLiuFeng!;
        if (!IsBoundaryLiuFengUse(use) || r.Returned || r.UseId != use.Id || r.ActionId != o.ActionId || LifecycleCardUse(use.Id) is not null) throw new InvalidOperationException("Qinling native return changed its original once-paid frame.");
        ReplaceRuntimeFrame(f.Id, f with { BoundaryLiuFeng = r with { Returned = true, Damaged = use.CausedDamage } }); AdvanceEventRulesAndQueueFact(new BoundaryLiuFengUseReturnedEvent(f.Id, use.Id, o.ActionId, use.CausedDamage));
        if (r.Need is { } n && _resolutionStack.SingleOrDefault(x => x.Id == n.OwnerFrameId) is ProgramSkillFrame { BoundaryLiuFengCaller: { } caller } parent) ReplaceRuntimeFrame(parent.Id, parent with { BoundaryLiuFengCaller = caller with { Returned = true } });
        if (use.CardAttack is not { Active: true }) ReturnBoundaryLiuFengTail(GetActiveProgramFrame(f.Id));
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 50201)] private bool ReturnBoundaryLiuFengAttack()
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { BoundaryLiuFeng: { Stage: BoundaryLiuFengStage.NativeUse, Returned: true } } f) return false; ReturnBoundaryLiuFengTail(f); return true; }
    private void ReturnBoundaryLiuFengTail(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { BoundaryLiuFeng = f.BoundaryLiuFeng! with { Stage = BoundaryLiuFengStage.NativeTail, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); ContinueBoundaryLiuFeng(f.Id); }
    private void SettleBoundaryLiuFeng(ProgramSkillFrame f)
    {
        var r = f.BoundaryLiuFeng!; var move = !r.Damaged && _players[f.OwnerSeat].IsAlive && r.Cards is [var card] && _cardZones.GetLocation(card) == CardLocation.DiscardPile;
        ReplaceRuntimeTop(f = f with { BoundaryLiuFeng = r with { ConsequencePaid = true, Movement = "reward", Before = _movementSequence, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new BoundaryLiuFengConsequenceEvent(f.Id, r.UseId, r.Damaged, move));
        if (move) { BeginBoundaryLiuFengMovement(f, [r.Cards[0]], CardLocation.Authority(f.OwnerSeat), "inverse"); return; }
        if (r.Damaged && _players[f.OwnerSeat].IsAlive) { DrawCards(_players[f.OwnerSeat], 1, log: true, reason: new("skill-program.boundary:qinling.reward")); f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { BoundaryLiuFeng = f.BoundaryLiuFeng! with { After = _movementSequence } }); }
        ContinueBoundaryLiuFeng(f.Id);
    }
}
