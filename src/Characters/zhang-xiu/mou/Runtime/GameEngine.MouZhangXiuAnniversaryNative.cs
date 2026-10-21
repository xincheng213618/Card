namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool CanMouZhangXiuAnniversarySlash(ProgramSkillFrame f) => f.MouZhangXiuAnniversary is { Stage: MouZhangXiuAnniversaryStage.SlashIssuing } r && ValidMouZhangXiuAnniversary(f) &&
        _players[f.OwnerSeat].IsAlive && _players[r.Target].IsAlive && !IsCardUseForbidden(f.OwnerSeat, CardKind.Slash, CardActionType.Use) && !IsSlashProhibited(_players[r.Target]) &&
        TargetRules.AllowsSlash(new(_players[f.OwnerSeat], _players[r.Target], CardKind.Slash, r.Source, [])) && !HasTurnCardTargetRestriction(f.OwnerSeat, SkillProgramCardTargetRestriction.SelfOnly) &&
        !IsDirectedCardTargetProhibited(f.OwnerSeat, r.Target, CardKind.Slash) && !IsCardTargetProhibited(_players[r.Target], CardKind.Slash, Suit.None, null) && !HasBeneficiarySuitShield(f.OwnerSeat, r.Target, Suit.None) && !IsCuiYanNextUseForbidden(f.OwnerSeat, [], Suit.None);
    private CardActionContext PrepareMouZhangXiuAnniversarySlashAction(CardActionContext action)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouZhangXiuAnniversary: { Stage: MouZhangXiuAnniversaryStage.SlashIssuing } r } f) return action;
        if (!ValidMouZhangXiuAnniversary(f) || action.Type != CardActionType.Use || action.ActorSeat != f.OwnerSeat || action.ProviderSeat != f.OwnerSeat || action.RequesterSeat is not null || action.EffectiveKind != CardKind.Slash || action.PhysicalCards.Count != 0 || action.ConversionChain is not [var source] || source != r.Source || !action.TargetSeats.SequenceEqual([r.Target]) || r.Index >= r.Count || r.UseId != 0) throw new InvalidOperationException("Fuxi cannot declare another source, target or uncounted Slash.");
        return new(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat, action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, action.EffectiveKind, action.TargetSeats, action.PhysicalCards, action.ConversionChain, action.DesignatedTargetSeats, Suit.None, 0, null, action.FactionOrigin);
    }
    private void IssueMouZhangXiuAnniversarySlash(ProgramSkillFrame f)
    {
        if (!CanMouZhangXiuAnniversarySlash(f)) throw new InvalidOperationException("Fuxi Slash lost its exact accepted forced target.");
        var r = f.MouZhangXiuAnniversary!; var actor = _players[f.OwnerSeat]; var target = _players[r.Target];
        var id = BeginCardUse(new(0, CardKind.Slash, Suit.None, 0), actor.Seat, [r.Target], CardKind.Slash, ignoresArmor: HasCardArmorBypass(actor, target, CardKind.Slash), physicalCardIds: [], conversionSource: r.Source);
        var use = LifecycleCardUse(id)!; if (use.MouZhangXiuAnniversarySlash is null) throw new InvalidOperationException("Fuxi native Slash must bind before its real declaration.");
        var attack = new CardAttackHandle(this, id, actor.Seat, r.Target, card: null, damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: CardKind.Slash, ignoresArmor: HasCardArmorBypass(actor, target, CardKind.Slash), programSkillCardUseFrameId: f.Id);
        ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack); CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, CardKind.Slash);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actor.Seat, r.Target, IgnoresArmor: attack.IgnoresArmor)); NotifyAiOfSlash(actor, target);
        if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted, LifecycleCardUse(id)!.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    private void BindMouZhangXiuAnniversarySlashUse(CardUseFrame supplied)
    {
        var at = _resolutionStack.FindIndex(f => f.Id == supplied.Id);
        if (at < 1 || _resolutionStack[at - 1] is not ProgramSkillFrame { MouZhangXiuAnniversary: { Stage: MouZhangXiuAnniversaryStage.SlashIssuing } r } parent) return;
        var use = LifecycleCardUse(supplied.Id)!;
        if (!ValidMouZhangXiuAnniversary(parent) || r.UseId != 0 || r.Index >= r.Count || use.CardId != 0 || use.CardKind != CardKind.Slash || use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } a || a.ActorSeat != parent.OwnerSeat || a.ProviderSeat != parent.OwnerSeat || a.RequesterSeat is not null || a.ConversionChain is not [var source] || source != r.Source || !a.TargetSeats.SequenceEqual([r.Target]) || !use.TargetSeats.SequenceEqual([r.Target]) || a.EffectiveSuit != Suit.None || a.EffectiveRank != 0 || a.EffectiveIsRed is not null) throw new InvalidOperationException("Fuxi cannot bind a different native counted Slash.");
        var origin = new MouZhangXiuAnniversarySlashOrigin(parent.Id, r.Source, r.GameplayHash, r.Index, r.Target, use.Id, a.ActionId);
        ReplaceRuntimeFrame(parent.Id, parent with { MouZhangXiuAnniversary = r with { Stage = MouZhangXiuAnniversaryStage.NativeSlash, UseId = use.Id, ActionId = a.ActionId, Returned = false } });
        UpdateLifecycleCardUse(use.Id, u => u with { MouZhangXiuAnniversarySlash = origin, UnlimitedUse = true }); AdvanceEventRulesAndQueueFact(new MouZhangXiuAnniversarySlashIssuedEvent(origin));
    }
    private bool IsIssuedMouZhangXiuAnniversaryUse(CardUseFrame use)
    {
        if (use.MouZhangXiuAnniversarySlash is not { } o || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } current || !IsSlashCard(use.CardKind) || use.UnlimitedUse != true || o.UseId != use.Id || o.ActionId != current.ActionId || current.ProviderSeat != o.Source.OwnerSeat || current.RequesterSeat is not null || _contentRegistry.GetSkill(o.Source.SkillId).Program?.GameplayHash != o.GameplayHash || ProgramEventHistory<MouZhangXiuAnniversarySlashIssuedEvent>().Count(e => e.Origin == o) != 1 ||
            _resolutionStack.SingleOrDefault(f => f.Id == o.FrameId) is not ProgramSkillFrame { MouZhangXiuAnniversary: { Stage: MouZhangXiuAnniversaryStage.NativeSlash, Returned: false } r } parent || !ValidMouZhangXiuAnniversary(parent) || r.UseId != use.Id || r.ActionId != current.ActionId || r.Source != o.Source || r.GameplayHash != o.GameplayHash || r.Index != o.Index || r.Target != o.Target) return false;
        var original = current; if (use.CurrentSlashFirePolicy is { } fire) { AssertCurrentSlashFirePolicy(use); original = fire.OriginalAction; } else if (use.CardKind != CardKind.Slash) return false;
        if (original.EffectiveKind != CardKind.Slash || original.EffectiveSuit != Suit.None || original.EffectiveRank != 0 || original.EffectiveIsRed is not null || original.ConversionChain is not [var source] || source != o.Source) return false;
        return ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id) ? ShownEntityUseActorMatches(use, o.Source.OwnerSeat, o.Source.OwnerSeat) : _resolutionStack.LastOrDefault()?.Id == use.Id && use.SourceSeat == o.Source.OwnerSeat && current.ActorSeat == o.Source.OwnerSeat;
    }
    private bool IsMouZhangXiuAnniversaryUse(long id) => LifecycleCardUse(id) is { } use && IsIssuedMouZhangXiuAnniversaryUse(use);
    private Card MouZhangXiuAnniversaryRepresentation(long id) => LifecycleCardUse(id) is { } use && IsIssuedMouZhangXiuAnniversaryUse(use) ? new(0, use.CardKind, Suit.None, 0) : throw new InvalidOperationException("Fuxi requires its own native zero-material representation.");
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 36600)] private bool FinishMouZhangXiuAnniversaryAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false; if (attack.Card is not null || !IsMouZhangXiuAnniversaryUse(attack.ResolutionId)) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!; SetCardUseStep(use.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); } return true;
    }
    [NativeUseValidation(36600)] private void ValidateMouZhangXiuAnniversaryNative(CardUseFrame use) { if (use.MouZhangXiuAnniversarySlash is not null && (!IsIssuedMouZhangXiuAnniversaryUse(use) || use.Step != ResolutionFrameStep.Completed)) throw new InvalidOperationException("Fuxi cannot retire an incomplete or unrelated native use."); }
    [NativeUseReturn(36600)] private void ReturnMouZhangXiuAnniversaryNative(CardUseFrame use)
    {
        if (use.MouZhangXiuAnniversarySlash is not { } o) return; var f = GetActiveProgramFrame(o.FrameId); var r = f.MouZhangXiuAnniversary!;
        if (r.Stage != MouZhangXiuAnniversaryStage.NativeSlash || r.Returned || r.UseId != use.Id || r.ActionId != o.ActionId || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1) throw new InvalidOperationException("Fuxi lost its unique typed whole-use return.");
        ReplaceRuntimeTop(f with { MouZhangXiuAnniversary = r with { Returned = true } }); if (use.CardAttack is not { Active: true }) SettleMouZhangXiuAnniversaryNative(o);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 36600)] private bool ReturnMouZhangXiuAnniversaryAttack(AttackCompletionReceipt c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouZhangXiuAnniversary: { Returned: true, Stage: MouZhangXiuAnniversaryStage.NativeSlash } r } f || c.ResolutionId != r.UseId) return false;
        if (c.ProgramFrameId != f.Id) throw new InvalidOperationException("Fuxi attack returned to another owner.");
        SettleMouZhangXiuAnniversaryNative(ProgramEventHistory<MouZhangXiuAnniversarySlashIssuedEvent>().Single(e => e.Origin.FrameId == f.Id && e.Origin.Index == r.Index).Origin); return true;
    }
    private void SettleMouZhangXiuAnniversaryNative(MouZhangXiuAnniversarySlashOrigin o)
    {
        var f = GetActiveProgramFrame(o.FrameId); var r = f.MouZhangXiuAnniversary!;
        if (!r.Returned || r.UseId != o.UseId || r.ActionId != o.ActionId || LifecycleCardUse(o.UseId) is not null || ProgramEventHistory<MouZhangXiuAnniversarySlashReturnedEvent>().Any(e => e.FrameId == f.Id && e.Index == r.Index)) throw new InvalidOperationException("Fuxi cannot settle a native Slash twice.");
        ReplaceRuntimeTop(f with { MouZhangXiuAnniversary = r with { Index = r.Index + 1, Stage = MouZhangXiuAnniversaryStage.SlashChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouZhangXiuAnniversarySlashReturnedEvent(f.Id, r.Index, o.UseId, o.ActionId)); ContinueMouZhangXiuAnniversary(f.Id);
    }
}
