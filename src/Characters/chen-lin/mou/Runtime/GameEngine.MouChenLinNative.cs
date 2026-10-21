namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouChenLinUsePlan? MouChenLinPlan(ProgramSkillFrame f, int id, int target)
    {
        if (!ValidMouChenLin(f) || f.MouChenLin is not { } r || !r.Cards.Any(c => c.Id == id) || r.Used.Contains(id) || !_players[f.OwnerSeat].IsAlive || !IsValidPlayerSeat(target) || !_players[target].IsAlive || _winner != Winner.None || _cardZones.GetLocation(id) != CardLocation.Processing) return null;
        var card = GetAdvancedCard(id); var kind = card.Kind; var actor = _players[f.OwnerSeat]; var suit = GetProgramEffectiveSuit(actor, card); var color = SuitColor(suit);
        if (!MouChenLinRuntime.DamageCard(kind) || IsCardUseForbidden(actor.Seat, kind, CardActionType.Use) || IsTurnPhysicalUseForbidden(actor.Seat, [id]) || !TargetRules.AllowsNativeUseMaterial(new(actor.Seat, kind, [id], r.Source)) || IsCuiYanNextUseForbidden(actor.Seat, [id], suit)) return null;
        if (IsSlashCard(kind)) return CanUseSlashTarget(actor, _players[target], card, effectiveKind: kind, physicalCardIds: [id], conversionSource: r.Source) ? new(id, kind, LegalActionKind.Slash, null) { EligibleTargets = [target] } : null;
        if (kind == CardKind.Lightning)
        {
            var t = _players[target];
            return target == actor.Seat && !t.JudgmentAreaAbolished && !HasJudgmentEffectiveCard(t, kind) && !HasCardPolicy(t, SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget) && !IsDirectedCardTargetProhibited(actor.Seat, target, kind) && !IsCardTargetProhibited(t, kind, suit, color) && !IsShenwuRobeTargetProhibited(actor.Seat, target, kind) && !HasBeneficiarySuitShield(actor.Seat, target, suit) && ApplyTurnCardGroupTargetRestrictions(actor, [target]).Contains(target) ? new(id, kind, LegalActionKind.Lightning, null) { EligibleTargets = [target] } : null;
        }
        if (!IsOrdinaryTrick(kind)) return null;
        var options = BuildProgramOrdinaryTrickUseOptions(actor, kind, suit, enforceUsePermission: true, beneficiaryShieldSuit: suit, actualEffectiveColor: color, hasActualColor: true, physicalCardIds: [id], conversionSource: r.Source);
        var group = kind is CardKind.BarbarianAssault or CardKind.ArrowBarrage;
        var option = options.FirstOrDefault(o => group ? o.TargetSeats.Contains(target) : o.TargetSeats.SequenceEqual([target]));
        return option is null ? null : new(id, kind, option.ActionKind, option.RequiredCardKind) { EligibleTargets = option.TargetSeats };
    }
    private void IssueMouChenLinUse(ProgramSkillFrame f)
    {
        if (!ValidMouChenLin(f) || f.MouChenLin is not { Stage: MouChenLinStage.IssuingUse, Plan: { } plan, Native: null } r || MouChenLinPlan(f, plan.CardId, r.Target) is not { } canonical || canonical.Kind != plan.Kind || canonical.Action != plan.Action || canonical.Required != plan.Required || !canonical.EligibleTargets.SequenceEqual(plan.EligibleTargets)) throw new InvalidOperationException("Zhuanwen needs its exact publicly revealed physical material and native fixed-target admission.");
        var card = GetAdvancedCard(plan.CardId); var actor = _players[f.OwnerSeat];
        var id = BeginCardUse(card, actor.Seat, [r.Target], plan.Kind, physicalCardIds: [card.Id], conversionSource: r.Source);
        if (LifecycleCardUse(id)?.MouChenLinWrittenUse is null) throw new InvalidOperationException("Zhuanwen must bind its real Processing entity before native declaration.");
        if (IsSlashCard(plan.Kind))
        {
            var target = _players[r.Target]; var attack = new CardAttackHandle(this, id, actor.Seat, target.Seat, card, damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: plan.Kind, physicalCards: [card], ignoresArmor: HasCardArmorBypass(actor, target, plan.Kind), programSkillCardUseFrameId: f.Id);
            ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack); MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, plan.Kind); CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
            AdvanceEventRulesAndQueueFact(new CardUsedEvent(card.Id, plan.Kind, actor.Seat, target.Seat, IgnoresArmor: attack.IgnoresArmor)); NotifyAiOfSlash(actor, target);
            if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted, [target.Seat], ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
            return;
        }
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, [r.Target], plan.Action, requiredCardKind: plan.Required, playedCardKind: plan.Kind);
    }
    private void BindMouChenLinNativeUse(CardUseFrame use)
    {
        var n = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (n < 1 || _resolutionStack[n - 1] is not ProgramSkillFrame { MouChenLin: { Stage: MouChenLinStage.IssuingUse, Plan: { } p, Native: null } r } f) return;
        if (!ValidMouChenLin(f) || !p.EligibleTargets.Contains(r.Target) || use.CardId != p.CardId || use.CardKind != p.Kind || use.SourceSeat != f.OwnerSeat || use.TargetSeats is not [var target] || target != r.Target || use.PhysicalCardIds is not [var id] || id != p.CardId || use.Action is not { Type: CardActionType.Use } a || a.ActorSeat != f.OwnerSeat || a.ProviderSeat != f.OwnerSeat || a.RequesterSeat is not null || a.PhysicalCards is not [{ CardId: var material, From: var from }] || material != id || from != CardLocation.Processing || !a.ConversionChain.SequenceEqual([r.Source]) || !a.TargetSeats.SequenceEqual([r.Target])) throw new InvalidOperationException("Zhuanwen cannot substitute its fixed target, original material or actual native actor/provider.");
        var origin = new MouChenLinNativeUse(f.Id, r.Source, r.GameplayHash, r.Turn, r.Index, target, id, p.Kind, use.Id, a.ActionId);
        ReplaceRuntimeFrame(f.Id, f with { MouChenLin = r with { Stage = MouChenLinStage.NativeUse, Native = origin } }); UpdateLifecycleCardUse(use.Id, u => u with { MouChenLinWrittenUse = origin, UnlimitedUse = true }); AdvanceEventRulesAndQueueFact(new MouChenLinUseIssuedEvent(origin));
    }
    private bool MouChenLinUseRidesOn(ProgramSkillFrame f, CardUseFrame u) => ValidMouChenLin(f) && f.MouChenLin is { Stage: MouChenLinStage.NativeUse, Native: { } o, Plan: { } p } r && u.MouChenLinWrittenUse == o && o.FrameId == f.Id && o.Source == r.Source && o.GameplayHash == f.GameplayHash && o.Turn == _turnNumber && o.Index == r.Index && o.Target == r.Target && p.CardId == o.CardId && p.Kind == o.Kind && p.EligibleTargets.Contains(o.Target) && u.Id == o.UseId && u.CardId == o.CardId && u.CardKind == o.Kind && u.SourceSeat == f.OwnerSeat && u.TargetSeats.SequenceEqual([o.Target]) && u.PhysicalCardIds is [var id] && id == o.CardId && u.Action is { Type: CardActionType.Use } a && a.ActionId == o.ActionId && a.ProviderSeat == f.OwnerSeat && a.RequesterSeat is null && ShownEntityUseActorMatches(u, f.OwnerSeat, f.OwnerSeat) && a.PhysicalCards is [{ CardId: var material, From: var from }] && material == id && from == CardLocation.Processing && a.ConversionChain.SequenceEqual([r.Source]) && ProgramEventHistory<MouChenLinUseIssuedEvent>().Count(e => e.Origin == o) == 1;
    [NativeUseValidation(37000)] private void ValidateMouChenLinUse(CardUseFrame use) { if (use.MouChenLinWrittenUse is { } o && (use.Step != ResolutionFrameStep.Completed || _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == o.FrameId) is not { } f || !MouChenLinUseRidesOn(f, use))) throw new InvalidOperationException("Zhuanwen cannot retire another or incomplete whole native Use."); }
    [NativeUseReturn(37000)] private bool ReturnMouChenLinUse(CardUseFrame use)
    {
        if (use.MouChenLinWrittenUse is not { } o) return false; var f = GetActiveProgramFrame(o.FrameId);
        if (!MouChenLinUseRidesOn(f, use) || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1 || ProgramEventHistory<MouChenLinUseReturnedEvent>().Any(e => e.UseId == use.Id)) throw new InvalidOperationException("Zhuanwen requires its exact once-completed whole native card return.");
        MouChenLinRules.UseReturned(o, use.CardAttack is { Active: true }); return true;
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 37000)] private bool ResumeMouChenLinAttack()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouChenLin: { Stage: MouChenLinStage.NativeUse, NativeReturned: true, Native: { } o } } f || LifecycleCardUse(o.UseId) is not null) return false;
        MouChenLinRules.SettleUse(o); return true;
    }
}
