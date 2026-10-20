namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CardActionContext PrepareWuLuKangKegouSlashAction(CardActionContext action)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuLuKangKegou: { Stage: WuLuKangKegouStage.SlashIssuing } r } f) return action;
        if (action.Type != CardActionType.Use || action.EffectiveKind != CardKind.Slash || action.ActorSeat != r.OpponentSeat!.Value || action.ProviderSeat != r.OpponentSeat!.Value || action.RequesterSeat is not null || action.PhysicalCards.Count != 0 || action.ConversionChain is not [var source] || source != r.Source || !action.TargetSeats.SequenceEqual(r.NativeTargets) || r.NativeUseId != 0 || ProgramEventHistory<WuLuKangKegouPindianReturnedEvent>().Count(e => e.FrameId == f.Id && e.Iteration == r.Iteration) != 1) throw new InvalidOperationException("Kegou cannot prepare a different actor, target, source or material group before native use recording.");
        return new(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat, action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, action.EffectiveKind, action.TargetSeats, action.PhysicalCards, action.ConversionChain, action.DesignatedTargetSeats, Suit.None, 0, null, action.FactionOrigin);
    }
    private void IssueWuLuKangKegouSlash(ProgramSkillFrame f)
    {
        var r = f.WuLuKangKegou!; var actor = _players[r.OpponentSeat!.Value]; var target = _players[f.OwnerSeat];
        if (!actor.IsAlive || !target.IsAlive || _winner != Winner.None || !CanWuLuKangKegouSlash(f)) { ContinueWuLuKangKegouAfterSkippedSlash(f); return; }
        ReplaceRuntimeTop(f = f with { WuLuKangKegou = r with { Stage = WuLuKangKegouStage.SlashIssuing, Before = _movementSequence, After = _movementSequence } });
        var id = BeginCardUse(new(0, CardKind.Slash, Suit.None, 0), actor.Seat, r.NativeTargets, CardKind.Slash, ignoresArmor: HasCardArmorBypass(actor, target, CardKind.Slash), physicalCardIds: [], conversionSource: r.Source);
        var use = LifecycleCardUse(id)!;
        if (use.WuLuKangKegouSlash is null) throw new InvalidOperationException("Kegou's native Slash was not bound before its declaration.");
        var attack = new CardAttackHandle(this, id, actor.Seat, use.TargetSeats[0], card: null, damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: CardKind.Slash, ignoresArmor: HasCardArmorBypass(actor, _players[use.TargetSeats[0]], CardKind.Slash), programSkillCardUseFrameId: f.Id);
        ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack); CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
        if (_phase == TurnPhase.Play && actor.Seat == _currentSeat && use.UnlimitedUse != true && !IgnoresProgramSlashLimit(actor, r.Source)) RecordSlashUseDebit(id, actor.Seat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, CardKind.Slash);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actor.Seat, use.TargetSeats[0], IgnoresArmor: attack.IgnoresArmor)); NotifyAiOfSlash(actor, _players[use.TargetSeats[0]]);
        if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted, LifecycleCardUse(id)!.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    private void BindWuLuKangKegouSlashUse(CardUseFrame supplied)
    {
        var at = _resolutionStack.FindIndex(f => f.Id == supplied.Id);
        if (at < 1 || _resolutionStack[at - 1] is not ProgramSkillFrame { WuLuKangKegou: { Stage: WuLuKangKegouStage.SlashIssuing } r } parent) return;
        var use = LifecycleCardUse(supplied.Id)!;
        if (r.NativeUseId != 0 || use.CardId != 0 || use.CardKind != CardKind.Slash || use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } a || a.ActorSeat != r.OpponentSeat!.Value || a.ProviderSeat != r.OpponentSeat!.Value || a.RequesterSeat is not null || a.ConversionChain is not [var source] || source != r.Source || !use.TargetSeats.SequenceEqual(r.NativeTargets) || !a.TargetSeats.SequenceEqual(use.TargetSeats) || a.EffectiveSuit != Suit.None || a.EffectiveRank != 0 || a.EffectiveIsRed is not null || ProgramEventHistory<WuLuKangKegouPindianReturnedEvent>().Count(e => e.FrameId == parent.Id && e.Iteration == r.Iteration) != 1) throw new InvalidOperationException("Kegou cannot change the real loser, winner, zero output or original Pindian payment.");
        var origin = new WuLuKangKegouSlashOrigin(parent.Id, r.Source, r.GameplayHash, r.Iteration, r.PindianId!.Value, r.OpponentSeat!.Value, parent.OwnerSeat, use.Id, a.ActionId);
        ReplaceRuntimeFrame(parent.Id, parent with { WuLuKangKegou = r with { Stage = WuLuKangKegouStage.NativeSlash, NativeUseId = use.Id, ActionId = a.ActionId, NativeReturned = false } }); UpdateLifecycleCardUse(use.Id, u => u with { WuLuKangKegouSlash = origin });
        AdvanceEventRulesAndQueueFact(new WuLuKangKegouSlashIssuedEvent(origin));
    }
    private bool IsIssuedWuLuKangKegouUse(CardUseFrame use)
    {
        if (use.WuLuKangKegouSlash is not { } o || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } current || !IsSlashCard(use.CardKind) || o.UseId != use.Id || o.ActionId != current.ActionId || current.ProviderSeat != o.ActorSeat || current.RequesterSeat is not null || _contentRegistry.GetSkill(o.Source.SkillId).Program?.GameplayHash != o.GameplayHash || ProgramEventHistory<WuLuKangKegouSlashIssuedEvent>().Count(e => e.Origin == o) != 1 || _resolutionStack.SingleOrDefault(f => f.Id == o.ProgramFrameId) is not ProgramSkillFrame { WuLuKangKegou: { Stage: WuLuKangKegouStage.NativeSlash, NativeReturned: false } r } parent || r.NativeUseId != use.Id || r.ActionId != o.ActionId || r.Source != o.Source || r.GameplayHash != o.GameplayHash || r.Iteration != o.Iteration || r.PindianId != o.PindianId || r.OpponentSeat != o.ActorSeat || parent.OwnerSeat != o.TargetSeat) return false;
        var original = current; if (use.CurrentSlashFirePolicy is { } fire) { AssertCurrentSlashFirePolicy(use); original = fire.OriginalAction; } else if (use.CardKind != CardKind.Slash) return false;
        if (original.EffectiveKind != CardKind.Slash || original.EffectiveSuit != Suit.None || original.EffectiveRank != 0 || original.EffectiveIsRed is not null || original.ConversionChain is not [var source] || source != o.Source) return false;
        return ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id) ? ShownEntityUseActorMatches(use, o.ActorSeat, o.ActorSeat) : _resolutionStack.LastOrDefault()?.Id == use.Id && use.SourceSeat == o.ActorSeat && current.ActorSeat == o.ActorSeat;
    }
    private bool IsWuLuKangKegouUse(long id) => LifecycleCardUse(id) is { } use && IsIssuedWuLuKangKegouUse(use);
    private Card WuLuKangKegouRepresentation(long id) => LifecycleCardUse(id) is { } use && IsIssuedWuLuKangKegouUse(use) ? new(0, use.CardKind, Suit.None, 0) : throw new InvalidOperationException("Kegou requires its issued native zero-entity representation.");
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 60)]
    private bool FinishWuLuKangKegouAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false; if (attack.Card is not null || !IsWuLuKangKegouUse(attack.ResolutionId)) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!; SetCardUseStep(use.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); } return true;
    }
    [NativeUseValidation(29700)] private void ValidateWuLuKangKegouNative(CardUseFrame use) { if (use.WuLuKangKegouSlash is not null && (!IsIssuedWuLuKangKegouUse(use) || use.Step != ResolutionFrameStep.Completed)) throw new InvalidOperationException("Kegou cannot retire before its exact whole native Slash completes."); }
    [NativeUseReturn(29700)] private void ReturnWuLuKangKegouNative(CardUseFrame use)
    {
        if (use.WuLuKangKegouSlash is not { } o) return; var f = GetActiveProgramFrame(o.ProgramFrameId); var r = f.WuLuKangKegou!;
        if (r.Stage != WuLuKangKegouStage.NativeSlash || r.NativeReturned || r.NativeUseId != use.Id || r.ActionId != o.ActionId || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1) throw new InvalidOperationException("Kegou lost its unique typed native return.");
        ReplaceRuntimeTop(f with { WuLuKangKegou = r with { NativeReturned = true } }); if (use.CardAttack is not { Active: true }) SettleWuLuKangKegouNative(o);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 29700)]
    private bool ReturnWuLuKangKegouAttack(AttackCompletionReceipt c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuLuKangKegou: { NativeReturned: true, Stage: WuLuKangKegouStage.NativeSlash } r } f || c.ResolutionId != r.NativeUseId) return false;
        if (c.ProgramFrameId != f.Id) throw new InvalidOperationException("Kegou's native attack returned to a different program.");
        var o = ProgramEventHistory<WuLuKangKegouSlashIssuedEvent>().Single(e => e.Origin.ProgramFrameId == f.Id && e.Origin.Iteration == r.Iteration).Origin; SettleWuLuKangKegouNative(o); return true;
    }
    private void ContinueWuLuKangKegouAfterSkippedSlash(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { WuLuKangKegou = f.WuLuKangKegou! with { Stage = WuLuKangKegouStage.RepeatChoice } }); ContinueWuLuKangKegou(f.Id); }
    private void SettleWuLuKangKegouNative(WuLuKangKegouSlashOrigin o)
    {
        var f = GetActiveProgramFrame(o.ProgramFrameId); var r = f.WuLuKangKegou!;
        if (!r.NativeReturned || r.NativeUseId != o.UseId || r.ActionId != o.ActionId || LifecycleCardUse(o.UseId) is not null || ProgramEventHistory<WuLuKangKegouSlashReturnedEvent>().Any(e => e.FrameId == f.Id && e.Iteration == r.Iteration)) throw new InvalidOperationException("Kegou cannot settle its native Use twice.");
        ReplaceRuntimeTop(f with { WuLuKangKegou = r with { Stage = WuLuKangKegouStage.NativeSlashChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); AdvanceEventRulesAndQueueFact(new WuLuKangKegouSlashReturnedEvent(f.Id, r.Iteration, o.UseId, o.ActionId)); ContinueWuLuKangKegou(f.Id);
    }
}
