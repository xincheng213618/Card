namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CardActionContext PrepareZongluanSlashAction(CardActionContext action)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { TaoQian: { Paid: true, Stage: TaoQianStage.SlashIssuing } r }) return action;
        if (r.Op != SkillProgramEffectOp.ResolveZongluanPreparationSlashAndDiscard || r.UseId != 0 || action.Type != CardActionType.Use || action.EffectiveKind != CardKind.Slash || action.ActorSeat != r.ActorSeat || action.ProviderSeat != r.ActorSeat || action.RequesterSeat is not null || action.PhysicalCards.Count != 0 || action.ConversionChain is not [var source] || source != r.Source || !action.TargetSeats.SequenceEqual(r.Targets)) throw new InvalidOperationException("Zongluan cannot change its issued actor, source, zero cost or original ordered targets.");
        return new(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat, action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, action.EffectiveKind, action.TargetSeats, action.PhysicalCards, action.ConversionChain, action.DesignatedTargetSeats, Suit.None, 0, null, action.FactionOrigin);
    }
    private void IssueZongluanSlash(ProgramSkillFrame f)
    {
        var r = f.TaoQian!;
        if (!r.Paid || r.Stage != TaoQianStage.SlashTargets || r.Targets.Count == 0 || r.Targets.Distinct().Count() != r.Targets.Count || r.Targets.Any(s => !ZongluanTargets(r.ActorSeat).Contains(s))) throw new InvalidOperationException("Zongluan requires at least one real native legal target and cannot cancel its issued request.");
        var actor = _players[r.ActorSeat]; ReplaceRuntimeTop(f = f with { TaoQian = r with { Stage = TaoQianStage.SlashIssuing } });
        var id = BeginCardUse(new(0, CardKind.Slash, Suit.None, 0), actor.Seat, r.Targets, CardKind.Slash, ignoresArmor: HasArmorBypass(actor), physicalCardIds: [], conversionSource: r.Source);
        var use = LifecycleCardUse(id)!;
        if (use.ZongluanSlash is null) throw new InvalidOperationException("Zongluan was not bound before its real native declaration.");
        var target = use.TargetSeats[0]; var attack = new CardAttackHandle(this, id, actor.Seat, target, card: null, damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: CardKind.Slash, ignoresArmor: HasCardArmorBypass(actor, _players[target], CardKind.Slash), programSkillCardUseFrameId: f.Id);
        ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack); CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false; MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, CardKind.Slash);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actor.Seat, target, IgnoresArmor: attack.IgnoresArmor)); foreach (var seat in use.TargetSeats) NotifyAiOfSlash(actor, _players[seat]);
        if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted, LifecycleCardUse(id)!.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    private void BindZongluanSlashUse(long id)
    {
        var at = _resolutionStack.FindIndex(f => f.Id == id);
        if (at < 1 || _resolutionStack[at - 1] is not ProgramSkillFrame { TaoQian: { Paid: true, Stage: TaoQianStage.SlashIssuing } r } f) return;
        var use = LifecycleCardUse(id)!;
        if (r.Op != SkillProgramEffectOp.ResolveZongluanPreparationSlashAndDiscard || r.UseId != 0 || use.CardId != 0 || use.CardKind != CardKind.Slash || use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } a || a.ActorSeat != r.ActorSeat || a.ProviderSeat != r.ActorSeat || a.RequesterSeat is not null || a.ConversionChain is not [var source] || source != r.Source || !use.TargetSeats.SequenceEqual(r.Targets) || !a.TargetSeats.SequenceEqual(r.Targets) || a.EffectiveSuit != Suit.None || a.EffectiveRank != 0 || a.EffectiveIsRed is not null || ProgramEventHistory<ZongluanCommittedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.ActorSeat == r.ActorSeat) != 1) throw new InvalidOperationException("Zongluan lost the exact source, actual actor, materials or paid multi-target request.");
        var origin = new ZongluanSlashOrigin(f.Id, r.Source, r.GameplayHash, r.ActorSeat, id, a.ActionId, r.ActualTurn, r.TurnOwner);
        ReplaceRuntimeFrame(f.Id, f with { TaoQian = r with { Stage = TaoQianStage.NativeSlash, UseId = id, ActionId = a.ActionId } }); UpdateLifecycleCardUse(id, u => u with { ZongluanSlash = origin });
        AdvanceEventRulesAndQueueFact(new ZongluanSlashIssuedEvent(origin, r.Targets.Count));
        for (var i = 0; i < r.Targets.Count; i++) AdvanceEventRulesAndQueueFact(new ZongluanInitialTargetEvent(f.Id, id, i, r.Targets[i]));
    }
    private bool IsIssuedZongluanUse(CardUseFrame use)
    {
        if (use.ZongluanSlash is not { } o || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } a || !IsSlashCard(use.CardKind) || o.UseId != use.Id || o.ActionId != a.ActionId || a.ProviderSeat != o.ActorSeat || a.RequesterSeat is not null || _contentRegistry.GetSkill(o.Source.SkillId).Program?.GameplayHash != o.GameplayHash || ProgramEventHistory<ZongluanSlashIssuedEvent>().Count(e => e.Origin == o) != 1 || _resolutionStack.SingleOrDefault(f => f.Id == o.ProgramFrameId) is not ProgramSkillFrame { TaoQian: { Paid: true, Stage: TaoQianStage.NativeSlash, NativeReturned: false } r } || r.UseId != use.Id || r.ActionId != a.ActionId || r.Source != o.Source || r.GameplayHash != o.GameplayHash || r.ActorSeat != o.ActorSeat || r.ActualTurn != o.ActualTurn || r.TurnOwner != o.TurnOwner) return false;
        var original = a;
        if (use.CurrentSlashFirePolicy is { } fire) { AssertCurrentSlashFirePolicy(use); original = fire.OriginalAction; } else if (use.CardKind != CardKind.Slash) return false;
        if (original.EffectiveKind != CardKind.Slash || original.EffectiveSuit != Suit.None || original.EffectiveRank != 0 || original.EffectiveIsRed is not null || original.ConversionChain is not [var source] || source != o.Source || !ProgramEventHistory<ZongluanInitialTargetEvent>().Where(e => e.FrameId == o.ProgramFrameId && e.UseId == use.Id).OrderBy(e => e.Index).Select(e => e.TargetSeat).SequenceEqual(r.Targets)) return false;
        return ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id) ? ShownEntityUseActorMatches(use, o.ActorSeat, o.ActorSeat) : _resolutionStack.LastOrDefault()?.Id == use.Id && use.SourceSeat == o.ActorSeat && a.ActorSeat == o.ActorSeat;
    }
    private bool IsZongluanUse(long id) => LifecycleCardUse(id) is { } use && IsIssuedZongluanUse(use);
    private Card ZongluanRepresentation(long id) => LifecycleCardUse(id) is { } use && IsIssuedZongluanUse(use) ? new(0, use.CardKind, Suit.None, 0) : throw new InvalidOperationException("Zongluan requires its exact issued native zero-entity Slash.");
    private bool FinishZongluanAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false; if (attack.Card is not null || !IsZongluanUse(attack.ResolutionId)) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!; SetCardUseStep(use.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); } return true;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 28120)]
    private void CaptureZongluanDirectDamage(IGameEvent payload)
    {
        if (payload is not DamageAppliedEvent { Amount: > 0 } e || _resolutionStack.LastOrDefault() is not DamageFrame d || CurrentDamageAttempt is not CardAttackHandle a || a.ResolutionId != d.ParentFrameId || a.IsChainPropagation || a.DamageRedirected || a.IsProgramSkillDamage || a.IsProgramJudgmentDamage || d.SourceSeat != e.SourceSeat || d.TargetSeat != e.TargetSeat || d.Amount != e.Amount || LifecycleCardUse(a.ResolutionId) is not { ZongluanSlash: { } o } use || !IsIssuedZongluanUse(use)) return;
        if (ProgramEventHistory<ZongluanDirectDamageEvent>().Any(x => x.DamageFrameId == d.Id)) throw new InvalidOperationException("Zongluan cannot account for one actual Slash damage occurrence twice.");
        AdvanceEventRulesAndQueueFact(new ZongluanDirectDamageEvent(o.ProgramFrameId, use.Id, d.Id, d.SourceSeat, d.TargetSeat, e.Amount));
    }
    [NativeUseValidation(28120)] private void ValidateZongluanNative(CardUseFrame use) { if (use.ZongluanSlash is not null && (!IsIssuedZongluanUse(use) || use.Step != ResolutionFrameStep.Completed)) throw new InvalidOperationException("Zongluan cannot retire before its whole native Slash and real children finish."); }
    [NativeUseReturn(28120)]
    private void ReturnZongluanNative(CardUseFrame use)
    {
        if (use.ZongluanSlash is not { } o) return; var f = GetActiveProgramFrame(o.ProgramFrameId); var r = f.TaoQian!;
        if (r.Stage != TaoQianStage.NativeSlash || r.NativeReturned || r.UseId != use.Id || r.ActionId != o.ActionId || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1) throw new InvalidOperationException("Zongluan lost its once-completed whole native Use return.");
        ReplaceRuntimeTop(f with { TaoQian = r with { NativeReturned = true } }); if (use.CardAttack is not { Active: true }) SettleZongluanNative(o);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 28120)]
    private bool ReturnZongluanAttack(AttackCompletionReceipt c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { TaoQian: { Stage: TaoQianStage.NativeSlash, NativeReturned: true } r } f || c.ResolutionId != r.UseId) return false;
        if (c.ProgramFrameId != f.Id) throw new InvalidOperationException("Zongluan's native Slash returned to another program.");
        SettleZongluanNative(ProgramEventHistory<ZongluanSlashIssuedEvent>().Single(e => e.Origin.ProgramFrameId == f.Id).Origin); return true;
    }
    private void SettleZongluanNative(ZongluanSlashOrigin o)
    {
        var f = GetActiveProgramFrame(o.ProgramFrameId); var r = f.TaoQian!;
        if (!r.NativeReturned || r.UseId != o.UseId || LifecycleCardUse(o.UseId) is not null || ProgramEventHistory<ZongluanSlashReturnedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Zongluan cannot settle its whole Slash twice.");
        var count = ProgramEventHistory<ZongluanDirectDamageEvent>().Where(e => e.FrameId == f.Id && e.UseId == o.UseId && e.Amount > 0).Select(e => e.TargetSeat).Distinct().Count();
        ReplaceRuntimeTop(f with { TaoQian = r with { Stage = TaoQianStage.NativeChildren, DamageRecipientCount = count }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); AdvanceEventRulesAndQueueFact(new ZongluanSlashReturnedEvent(f.Id, o.UseId, o.ActionId, count));
    }
}
