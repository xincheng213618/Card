using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsMouGyzExactCardQuery(CharacterState actor, int? selectedId)
    {
        if (IsMouGyzDeclarationQuery(actor, selectedId)) return true;
        if (selectedId is not { } id || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouGuoYouZhi: { Stage: MouGuoYouZhiStage.UseChoice or MouGuoYouZhiStage.IssuingUse } r } f || r.ActorSeat != actor.Seat || !actor.IsAlive || id != r.CardId || _cardZones.GetLocation(id) != CardLocation.Hand(actor.Seat) || !ValidMouGyz(f)) return false;
        return r.Stage == MouGuoYouZhiStage.UseChoice || r.Native is { UseId: 0, ActionId: 0 } n && ValidMouGyzNative(n, f, false);
    }
    private IReadOnlyList<LegalAction> MouGyzNativeActions(ProgramSkillFrame f)
    {
        if (f.MouGuoYouZhi is not { Stage: MouGuoYouZhiStage.UseChoice } r || !_players[r.ActorSeat].IsAlive || _winner != Winner.None || _cardZones.GetLocation(r.CardId) != CardLocation.Hand(r.ActorSeat)) return [];
        return Array.AsReadOnly(BuildLegalActions(_players[r.ActorSeat], false, r.CardId).Where(a => a.CardId == r.CardId && a.MinCardCount <= 1 && a.MaxCardCount <= 1 &&
            (a.ConversionSource is null || ViewAsRule(a.ConversionSource) is not { } rule || rule.InputCount == 1) && a.Kind is not (LegalActionKind.EndPlay or LegalActionKind.Recast or LegalActionKind.UseEquipmentEffect or LegalActionKind.UseProgramSkill or LegalActionKind.RevealGeneral))
            .Select(a => a with { TargetSeats = Array.AsReadOnly(a.TargetSeats.ToArray()), AdditionalConversionSources = Array.AsReadOnly((a.AdditionalConversionSources ?? []).ToArray()), SelectableCardIds = Array.AsReadOnly(a.SelectableCardIds.ToArray()), SelectableTargetSeats = Array.AsReadOnly(a.SelectableTargetSeats.ToArray()), SelectableCardTargets = a.SelectableCardTargets is null ? null : Array.AsReadOnly(a.SelectableCardTargets.ToArray()), SelectableTargetSequences = a.SelectableTargetSequences is null ? null : Array.AsReadOnly(a.SelectableTargetSequences.Select(t => (IReadOnlyList<int>)Array.AsReadOnly(t.ToArray())).ToArray()) }).ToArray());
    }
    private void IssueMouGyzNativeUse(ProgramSkillFrame f, int index)
    {
        var r = f.MouGuoYouZhi!; var action = MouGyzNativeActions(f)[index];
        var receipt = new MouGuoYouZhiNativeReceipt(f.Id, r.Source, r.GameplayHash, f.InstructionIndex, r.ActorSeat, r.CardId, r.ActualTurn, _currentSeat, _cardUseDebitPhaseInstanceId, action, 0, 0);
        ReplaceRuntimeTop(f with { MouGuoYouZhi = r with { Stage = MouGuoYouZhiStage.IssuingUse, Native = receipt }, PendingMovementContinuation = null }); ExecuteAction(_players[r.ActorSeat], action);
    }
    private bool SameMouGyzNative(MouGuoYouZhiNativeReceipt a, MouGuoYouZhiNativeReceipt b) =>
        (a with { Action = b.Action }) == b && a.Action.Kind == b.Action.Kind && a.Action.CardId == b.Action.CardId && a.Action.PlayedCardKind == b.Action.PlayedCardKind && a.Action.TargetCardId == b.Action.TargetCardId && a.Action.ConversionSource == b.Action.ConversionSource && a.Action.TargetSeats.SequenceEqual(b.Action.TargetSeats) && (a.Action.AdditionalConversionSources ?? []).SequenceEqual(b.Action.AdditionalConversionSources ?? []);
    private bool ValidMouGyzNative(MouGuoYouZhiNativeReceipt n, ProgramSkillFrame f, bool bound) =>
        f.MouGuoYouZhi is { Paid: true } r && r.Op == SkillProgramEffectOp.ResolveMouGuoYouZhiZhongchun && n.ProgramFrameId == f.Id && n.Source == r.Source && n.GameplayHash == f.GameplayHash && n.InstructionIndex == f.InstructionIndex && n.ActorSeat == r.ActorSeat && n.CardId == r.CardId && n.ActualTurn == r.ActualTurn && n.Action.CardId == n.CardId && ValidMouGyz(f) &&
        ProgramEventHistory<MouGuoYouZhiMovementEvent>().Count(e => e.FrameId == f.Id && e.CardId == n.CardId && e.From == CardLocation.DiscardPile && e.To == CardLocation.Hand(n.ActorSeat) && e.Reason.Value == MouGyzObtainReason) == 1 &&
        (!bound || n.UseId > 0 && n.ActionId > 0 && ProgramEventHistory<MouGuoYouZhiNativeIssuedEvent>().Count(e => e == new MouGuoYouZhiNativeIssuedEvent(f.Id, n.UseId, n.ActionId, n.ActorSeat, n.CardId)) == 1);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 32401)]
    private void BindMouGyzNativeUse(IGameEvent fact)
    {
        if (fact is not CardUseDeclaredEvent declared || LifecycleCardUse(declared.ResolutionId) is not { } use || _resolutionStack.Count < 2 || _resolutionStack[^2] is not ProgramSkillFrame { MouGuoYouZhi: { Stage: MouGuoYouZhiStage.IssuingUse, Native: { UseId: 0, ActionId: 0 } n } r } parent) return;
        var a = use.Action ?? throw new InvalidOperationException("Zhongchun native Use lost its actual action.");
        if (!ValidMouGyzNative(n, parent, false) || use.SourceSeat != n.ActorSeat || use.CardId != n.CardId || use.CardKind != (n.Action.PlayedCardKind ?? ApplyProgramUseAppearance(_players[n.ActorSeat], GetAttackCard(n.CardId), n.Action.ConversionSource).Kind) || use.PhysicalCardIds is not [var id] || id != n.CardId || a.Type != CardActionType.Use || a.ActorSeat != n.ActorSeat || a.ProviderSeat != n.ActorSeat || a.RequesterSeat is not null || a.PhysicalCards is not [var cost] || cost.CardId != n.CardId || !a.ConversionChain.SequenceEqual((n.Action.ConversionSource is { } source ? new[] { source } : Array.Empty<CardConversionSource>()).Concat(n.Action.AdditionalConversionSources ?? [])) || !use.TargetSeats.SequenceEqual(MouGyzExpectedNativeTargets(n))) throw new InvalidOperationException("Zhongchun changed its original obtained entity, real actor/provider or target plan.");
        var bound = n with { UseId = use.Id, ActionId = a.ActionId }; ReplaceRuntimeFrame(parent.Id, parent with { MouGuoYouZhi = r with { Stage = MouGuoYouZhiStage.NativeUse, Native = bound } }); UpdateLifecycleCardUse(use.Id, u => u with { MouGuoYouZhiUse = bound });
        AdvanceEventRulesAndQueueFact(new MouGuoYouZhiNativeIssuedEvent(parent.Id, use.Id, a.ActionId, n.ActorSeat, n.CardId));
    }
    private IReadOnlyList<int> MouGyzExpectedNativeTargets(MouGuoYouZhiNativeReceipt r)
    {
        var a = r.Action; var actor = _players[r.ActorSeat]; var card = ApplyProgramUseAppearance(actor, GetAttackCard(r.CardId), a.ConversionSource);
        if (a.ConversionSource is { } source && ViewAsRule(source) is { SingleCardTrickUse: true } rule)
            return BuildProgramOrdinaryTrickUseOptions(actor, rule.OutputKind, EffectiveSuit(actor, GetAttackCard(r.CardId)), rule.ExcludeOwnerEffects, enforceUsePermission: true, beneficiaryShieldSuit: EffectiveSuit(actor, card), actualEffectiveColor: SuitColor(EffectiveSuit(actor, card)), hasActualColor: true, physicalCardIds: [r.CardId])
                .Where(o => o.ActionKind == a.Kind && o.TargetCardId == a.TargetCardId && (o.TargetSeats.SequenceEqual(a.TargetSeats) || a.ProgramActivationId == "red-additional-targets" && o.TargetSeats.SequenceEqual(a.TargetSeats.Take(o.TargetSeats.Count)))).OrderByDescending(o => o.TargetSeats.Count).Select(o => o.TargetSeats).First();
        if (GuanduExpectedNativeTargets(actor, card, a) is { } guandu) return guandu;
        if (DragonBoatExpectedNativeTargets(actor, card, a) is { } boat) return boat;
        if (a.Kind == LegalActionKind.Peach) return [actor.Seat];
        var effective = a.PlayedCardKind ?? card.Kind;
        if (a.Kind is not (LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage or LegalActionKind.PeachGarden or LegalActionKind.FiveGrains)) return GetImplicitSelfCardUseTargets(effective, actor.Seat, a.TargetSeats);
        var offensive = a.Kind is LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage;
        var seats = Enumerable.Range(offensive ? 1 : 0, _playerCount - (offensive ? 1 : 0)).Select(i => _players[(actor.Seat + i) % _playerCount]).Where(p => p.IsAlive && (!offensive || !HasCardPolicy(p, SkillProgramCardPolicyKind.ExcludeGlobalTarget, effective)) && !IsCardTargetProhibited(p, effective, card.Suit, SuitColor(EffectiveSuit(actor, card))) && !HasBeneficiarySuitShield(actor.Seat, p.Seat, EffectiveSuit(actor, card))).Select(p => p.Seat).ToArray();
        if (!offensive) seats = ApplyTurnCardGroupTargetRestrictions(actor, seats).ToArray(); if (a.Kind == LegalActionKind.FiveGrains) seats = seats.Take(_cardZones.Count(CardLocation.DrawPile) + _cardZones.Count(CardLocation.DiscardPile)).ToArray(); return seats;
    }
    private bool MouGyzNativeRidesOn(CardUseFrame use, ProgramSkillFrame parent) => use.MouGuoYouZhiUse is { } n && parent.MouGuoYouZhi is { Native: { } pending } && SameMouGyzNative(n, pending) && ValidMouGyzNative(n, parent, true) && use.Action is { Type: CardActionType.Use } a && a.ActionId == n.ActionId && a.ActorSeat == n.ActorSeat && a.ProviderSeat == n.ActorSeat && a.PhysicalCards is [var cost] && cost.CardId == n.CardId;
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 32401)]
    private bool IsMouGyzNonPlayNative(long id, int actor) => (_phase != TurnPhase.Play || actor != _currentSeat) && LifecycleCardUse(id) is { MouGuoYouZhiUse: { } n } use && n.ActorSeat == actor && _resolutionStack.SingleOrDefault(f => f.Id == n.ProgramFrameId) is ProgramSkillFrame parent && MouGyzNativeRidesOn(use, parent);
    [NativeUseValidation(32401)]
    private void AssertMouGyzNativeCompletion(CardUseFrame use)
    { if (use.MouGuoYouZhiUse is { } n && (use.Step != ResolutionFrameStep.Completed || _resolutionStack.LastOrDefault()?.Id != use.Id || _resolutionStack.SingleOrDefault(f => f.Id == n.ProgramFrameId) is not ProgramSkillFrame parent || !MouGyzNativeRidesOn(use, parent))) throw new InvalidOperationException("Zhongchun must complete its exact issued native Use."); }
    [NativeUseReturn(32401)]
    private bool ReturnMouGyzNativeUse(CardUseFrame use)
    {
        if (use.MouGuoYouZhiUse is not { } n) return false;
        if (_resolutionStack.Any(f => f.Id == use.Id) || use.Step != ResolutionFrameStep.Completed || _resolutionStack.SingleOrDefault(f => f.Id == n.ProgramFrameId) is not ProgramSkillFrame parent || !MouGyzNativeRidesOn(use, parent) || ProgramEventHistory<MouGuoYouZhiNativeReturnedEvent>().Any(e => e.UseId == use.Id)) throw new InvalidOperationException("Zhongchun cannot return or pay its native entity twice.");
        ReplaceRuntimeFrame(parent.Id, parent with { MouGuoYouZhi = parent.MouGuoYouZhi! with { Stage = MouGuoYouZhiStage.Returned } }); AdvanceEventRulesAndQueueFact(new MouGuoYouZhiNativeReturnedEvent(parent.Id, use.Id, n.ActionId, n.ActorSeat, n.CardId)); if (use.CardAttack is not { Active: true }) AdvanceRuntimeProgram(parent.Id); return true;
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 32401)]
    private bool ResumeMouGyzAttack() { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouGuoYouZhi: { Stage: MouGuoYouZhiStage.Returned, Native: not null } } f) return false; AdvanceRuntimeProgram(f.Id); return true; }
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedAfterCategory, 32401)]
    [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 32401)]
    private bool MouGyzTargetWindows(ProgramTargetCardWindowQuery q) => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouGuoYouZhiZhongchun);
}
