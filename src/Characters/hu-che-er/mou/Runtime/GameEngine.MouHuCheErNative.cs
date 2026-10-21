namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool CanMouHuCheErSlash(ProgramSkillFrame f) => f.MouHuCheEr is { Stage: MouHuCheErStage.SlashIssuing } r && ValidMouHuCheEr(f) &&
        _players[f.OwnerSeat].IsAlive && _players[r.Target].IsAlive && !IsCardUseForbidden(f.OwnerSeat, CardKind.Slash, CardActionType.Use) && !IsSlashProhibited(_players[r.Target]) &&
        TargetRules.AllowsSlash(new(_players[f.OwnerSeat], _players[r.Target], CardKind.Slash, r.Source, [])) && !HasTurnCardTargetRestriction(f.OwnerSeat, SkillProgramCardTargetRestriction.SelfOnly) &&
        !IsDirectedCardTargetProhibited(f.OwnerSeat, r.Target, CardKind.Slash) && !IsCardTargetProhibited(_players[r.Target], CardKind.Slash, Suit.None, null) && !HasBeneficiarySuitShield(f.OwnerSeat, r.Target, Suit.None) && !IsCuiYanNextUseForbidden(f.OwnerSeat, [], Suit.None);
    private CardActionContext PrepareMouHuCheErSlashAction(CardActionContext action)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouHuCheEr: { Stage: MouHuCheErStage.SlashIssuing } r } f) return action;
        if (!ValidMouHuCheEr(f) || action.Type != CardActionType.Use || action.ActorSeat != f.OwnerSeat || action.ProviderSeat != f.OwnerSeat || action.RequesterSeat is not null || action.EffectiveKind != CardKind.Slash || action.PhysicalCards.Count != 0 || action.ConversionChain is not [var source] || source != r.Source || !action.TargetSeats.SequenceEqual([r.Target]) || r.Index >= r.Count || r.UseId != 0) throw new InvalidOperationException("Kongwu cannot declare another source, target or uncounted Slash.");
        return new(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat, action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, action.EffectiveKind, action.TargetSeats, action.PhysicalCards, action.ConversionChain, action.DesignatedTargetSeats, Suit.None, 0, null, action.FactionOrigin);
    }
    private void IssueMouHuCheErSlash(ProgramSkillFrame f)
    {
        if (!CanMouHuCheErSlash(f)) throw new InvalidOperationException("Kongwu Slash lost its exact accepted forced target.");
        var r = f.MouHuCheEr!; var actor = _players[f.OwnerSeat]; var target = _players[r.Target];
        var id = BeginCardUse(new(0, CardKind.Slash, Suit.None, 0), actor.Seat, [r.Target], CardKind.Slash, ignoresArmor: HasCardArmorBypass(actor, target, CardKind.Slash), physicalCardIds: [], conversionSource: r.Source);
        var use = LifecycleCardUse(id)!; if (use.MouHuCheErSlash is null) throw new InvalidOperationException("Kongwu native Slash must bind before its real declaration.");
        var attack = new CardAttackHandle(this, id, actor.Seat, r.Target, card: null, damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: CardKind.Slash, ignoresArmor: HasCardArmorBypass(actor, target, CardKind.Slash), programSkillCardUseFrameId: f.Id);
        ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack); CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, CardKind.Slash);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actor.Seat, r.Target, IgnoresArmor: attack.IgnoresArmor)); NotifyAiOfSlash(actor, target);
        if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted, LifecycleCardUse(id)!.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    private void BindMouHuCheErSlashUse(CardUseFrame supplied)
    {
        var at = _resolutionStack.FindIndex(f => f.Id == supplied.Id);
        if (at < 1 || _resolutionStack[at - 1] is not ProgramSkillFrame { MouHuCheEr: { Stage: MouHuCheErStage.SlashIssuing } r } parent) return;
        var use = LifecycleCardUse(supplied.Id)!;
        if (!ValidMouHuCheEr(parent) || r.UseId != 0 || r.Index >= r.Count || use.CardId != 0 || use.CardKind != CardKind.Slash || use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } a || a.ActorSeat != parent.OwnerSeat || a.ProviderSeat != parent.OwnerSeat || a.RequesterSeat is not null || a.ConversionChain is not [var source] || source != r.Source || !a.TargetSeats.SequenceEqual([r.Target]) || !use.TargetSeats.SequenceEqual([r.Target]) || a.EffectiveSuit != Suit.None || a.EffectiveRank != 0 || a.EffectiveIsRed is not null) throw new InvalidOperationException("Kongwu cannot bind a different native counted Slash.");
        var origin = new MouHuCheErSlashOrigin(parent.Id, r.Source, r.GameplayHash, r.Index, r.Target, use.Id, a.ActionId);
        ReplaceRuntimeFrame(parent.Id, parent with { MouHuCheEr = r with { Stage = MouHuCheErStage.NativeSlash, UseId = use.Id, ActionId = a.ActionId, Returned = false } });
        UpdateLifecycleCardUse(use.Id, u => u with { MouHuCheErSlash = origin, UnlimitedUse = true }); AdvanceEventRulesAndQueueFact(new MouHuCheErSlashIssuedEvent(origin));
    }
    private bool IsIssuedMouHuCheErUse(CardUseFrame use)
    {
        if (use.MouHuCheErSlash is not { } o || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } current || !IsSlashCard(use.CardKind) || use.UnlimitedUse != true || o.UseId != use.Id || o.ActionId != current.ActionId || current.ProviderSeat != o.Source.OwnerSeat || current.RequesterSeat is not null || _contentRegistry.GetSkill(o.Source.SkillId).Program?.GameplayHash != o.GameplayHash || ProgramEventHistory<MouHuCheErSlashIssuedEvent>().Count(e => e.Origin == o) != 1 ||
            _resolutionStack.SingleOrDefault(f => f.Id == o.FrameId) is not ProgramSkillFrame { MouHuCheEr: { Stage: MouHuCheErStage.NativeSlash, Returned: false } r } parent || !ValidMouHuCheEr(parent) || r.UseId != use.Id || r.ActionId != current.ActionId || r.Source != o.Source || r.GameplayHash != o.GameplayHash || r.Index != o.Index || r.Target != o.Target) return false;
        var original = current; if (use.CurrentSlashFirePolicy is { } fire) { AssertCurrentSlashFirePolicy(use); original = fire.OriginalAction; } else if (use.CardKind != CardKind.Slash) return false;
        if (original.EffectiveKind != CardKind.Slash || original.EffectiveSuit != Suit.None || original.EffectiveRank != 0 || original.EffectiveIsRed is not null || original.ConversionChain is not [var source] || source != o.Source) return false;
        return ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id) ? ShownEntityUseActorMatches(use, o.Source.OwnerSeat, o.Source.OwnerSeat) : _resolutionStack.LastOrDefault()?.Id == use.Id && use.SourceSeat == o.Source.OwnerSeat && current.ActorSeat == o.Source.OwnerSeat;
    }
    private bool IsMouHuCheErUse(long id) => LifecycleCardUse(id) is { } use && IsIssuedMouHuCheErUse(use);
    private Card MouHuCheErRepresentation(long id) => LifecycleCardUse(id) is { } use && IsIssuedMouHuCheErUse(use) ? new(0, use.CardKind, Suit.None, 0) : throw new InvalidOperationException("Kongwu requires its own native zero-material representation.");
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 35800)] private bool FinishMouHuCheErAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false; if (attack.Card is not null || !IsMouHuCheErUse(attack.ResolutionId)) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!; SetCardUseStep(use.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); } return true;
    }
    [NativeUseValidation(35800)] private void ValidateMouHuCheErNative(CardUseFrame use) { if (use.MouHuCheErSlash is not null && (!IsIssuedMouHuCheErUse(use) || use.Step != ResolutionFrameStep.Completed)) throw new InvalidOperationException("Kongwu cannot retire an incomplete or unrelated native use."); }
    [NativeUseReturn(35800)] private void ReturnMouHuCheErNative(CardUseFrame use)
    {
        if (use.MouHuCheErSlash is not { } o) return; var f = GetActiveProgramFrame(o.FrameId); var r = f.MouHuCheEr!;
        if (r.Stage != MouHuCheErStage.NativeSlash || r.Returned || r.UseId != use.Id || r.ActionId != o.ActionId || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1) throw new InvalidOperationException("Kongwu lost its unique typed whole-use return.");
        ReplaceRuntimeTop(f with { MouHuCheEr = r with { Returned = true } }); if (use.CardAttack is not { Active: true }) SettleMouHuCheErNative(o);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 35800)] private bool ReturnMouHuCheErAttack(AttackCompletionReceipt c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouHuCheEr: { Returned: true, Stage: MouHuCheErStage.NativeSlash } r } f || c.ResolutionId != r.UseId) return false;
        if (c.ProgramFrameId != f.Id) throw new InvalidOperationException("Kongwu attack returned to another owner.");
        SettleMouHuCheErNative(ProgramEventHistory<MouHuCheErSlashIssuedEvent>().Single(e => e.Origin.FrameId == f.Id && e.Origin.Index == r.Index).Origin); return true;
    }
    private void SettleMouHuCheErNative(MouHuCheErSlashOrigin o)
    {
        var f = GetActiveProgramFrame(o.FrameId); var r = f.MouHuCheEr!;
        if (!r.Returned || r.UseId != o.UseId || r.ActionId != o.ActionId || LifecycleCardUse(o.UseId) is not null || ProgramEventHistory<MouHuCheErSlashReturnedEvent>().Any(e => e.FrameId == f.Id && e.Index == r.Index)) throw new InvalidOperationException("Kongwu cannot settle a native Slash twice.");
        ReplaceRuntimeTop(f with { MouHuCheEr = r with { Index = r.Index + 1, Stage = MouHuCheErStage.SlashChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouHuCheErSlashReturnedEvent(f.Id, r.Index, o.UseId, o.ActionId)); ContinueMouHuCheEr(f.Id);
    }
}
