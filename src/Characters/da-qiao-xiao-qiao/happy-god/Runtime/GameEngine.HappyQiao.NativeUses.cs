using System.Text.Json;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsHappyQiaoShutuCardQuery(CharacterState actor, int? selectedPhysicalCardId)
    {
        if (!actor.IsAlive || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { HappyQiao: { Stage: HappyQiaoStage.UseChoice or HappyQiaoStage.IssuingUse } receipt } frame ||
            frame.OwnerSeat != actor.Seat || receipt.Operation != SkillProgramEffectOp.ResolveHappyQiaoShutu || !HappyQiaoOriginalHealthCandidate(frame)) return false;
        if (selectedPhysicalCardId is not { } id) return true;
        if (receipt.Stage == HappyQiaoStage.IssuingUse && receipt.DeclarationPassed && frame.AcceptedDeclarationPayment is { Claimed: false } payment && payment.Cost.CardId == id)
            return _cardZones.GetLocation(id) == CardLocation.Processing;
        return GetPlayableCards(actor).Concat(GetEquipment(actor)).Any(c => c.Id == id);
    }
    // This is an explicit pure union of the two pool-specific legal queries, not
    // a game HP value. No health change, decision, receipt or fact is produced.
    private IReadOnlyList<HappyQiaoPlan> HappyQiaoUsePlans(ProgramSkillFrame frame)
    {
        if (frame.HappyQiao is not { Stage: HappyQiaoStage.UseChoice } || !IsHappyQiaoShutuCardQuery(_players[frame.OwnerSeat], null) || _winner != Winner.None) return [];
        var actor = _players[frame.OwnerSeat]; var original = actor.HappyQiaoPoolQuery; var plans = new List<HappyQiaoPlan>();
        try
        {
            foreach (var pool in Enum.GetValues<HappyQiaoPool>())
            {
                actor.HappyQiaoPoolQuery = _ => pool;
                var previous = _actionQuery; _actionQuery = new ActionQuery(_players.Count);
                using var scope = new ActionQueryScope(this, previous);
                foreach (var action in BuildLegalActions(actor, includeProgramActions: false).Where(a => a.MinCardCount <= 1 && a.MaxCardCount <= 1 &&
                    (a.ConversionSource is null || ViewAsRule(a.ConversionSource) is not { } rule || rule.InputCount is 0 or 1) &&
                    a.Kind is not (LegalActionKind.EndPlay or LegalActionKind.Recast or LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect or LegalActionKind.RevealGeneral)))
                {
                    var cards = action.CardId is { } id ? new[] { id } : [];
                    if (cards.Any(cardId => !IsHappyQiaoShutuCardQuery(actor, cardId))) continue;
                    var kind = action.PlayedCardKind ?? (action.CardId is { } entity ? AdvancedEffectiveHandKind(actor, ApplyProgramUseAppearance(actor, GetAdvancedCard(entity), action.ConversionSource)) : CardKind.Slash);
                    plans.Add(new("native", FreezeHappyQiaoAction(action), kind) { Cards = cards, Targets = action.TargetSeats });
                }
                if (CanUseZhangbaSerpentSpear(actor))
                    foreach (var pair in GetZhangbaSlashPairs(actor))
                    foreach (var target in _players.Where(p => CanUseVirtualSlashTarget(actor, p, CardKind.Slash, PhysicalGroupSuit(actor, pair), PhysicalGroupColor(actor, pair), ZhangbaSpecificSlashRank(actor, pair), pair.Select(c => c.Id).ToArray())))
                    {
                        var ids = pair.Select(c => c.Id).ToArray();
                        var action = new LegalAction(LegalActionKind.UseEquipmentEffect, null, target.Seat, "丈八蛇矛：对" + target.Name + "使用杀", PlayedCardKind: CardKind.Slash, MinCardCount: 2, MaxCardCount: 2, MinTargetCount: 1, MaxTargetCount: 1, EquipmentKind: CardKind.ZhangbaSerpentSpear)
                        { SelectableCardIds = ids, SelectableTargetSeats = [target.Seat] };
                        plans.Add(new("zhangba", FreezeHappyQiaoAction(action), CardKind.Slash) { Cards = ids, Targets = [target.Seat] });
                    }
                foreach (var kind in SlashKinds.Concat(new[] { CardKind.Peach, CardKind.Alcohol }))
                foreach (var selection in GetProgramMultiCardViewAsSelections(actor, kind, false).Where(s => s.Cards.Count > 1 && s.Cards.All(c => IsHappyQiaoShutuCardQuery(actor, c.Id))))
                {
                    var ids = selection.Cards.Select(c => c.Id).ToArray(); var suit = PhysicalGroupSuit(actor, selection.Cards); var color = PhysicalGroupColor(actor, selection.Cards);
                    IEnumerable<IReadOnlyList<int>> targets = IsSlashCard(kind)
                        ? _players.Where(p => CanUseVirtualSlashTarget(actor, p, kind, suit, color, physicalCardIds: ids)).Select(p => (IReadOnlyList<int>)new[] { p.Seat })
                        : kind == CardKind.Peach && IsRuleWounded(actor) && !HasSelfCardTargetProhibition(actor.Seat) && !IsCardTargetProhibited(actor, kind, suit, color) && !HasBeneficiarySuitShield(actor.Seat, actor.Seat, suit)
                            ? new IReadOnlyList<int>[] { [actor.Seat] } : kind == CardKind.Alcohol && CanUseRoundDistinctBasicAlcohol(actor, selection.Cards)
                                ? new IReadOnlyList<int>[] { [] } : [];
                    foreach (var seats in targets)
                    {
                        var action = new LegalAction(IsSlashCard(kind) ? LegalActionKind.Slash : kind == CardKind.Peach ? LegalActionKind.Peach : LegalActionKind.Alcohol,
                            ids[0], seats.Count > 0 ? seats[0] : null, "殊途：将实体牌当【" + CardCatalog.Get(kind).DisplayName + "】使用", PlayedCardKind: kind)
                        { ConversionSource = selection.Source, TargetSeats = seats };
                        plans.Add(new("multi", FreezeHappyQiaoAction(action), kind) { Cards = ids, Targets = seats });
                    }
                }
            }
        }
        finally { actor.HappyQiaoPoolQuery = original; }
        return Array.AsReadOnly(plans.DistinctBy(p => JsonSerializer.Serialize(p)).ToArray());
    }
    private static LegalAction FreezeHappyQiaoAction(LegalAction action) => action with
    {
        TargetSeats = Array.AsReadOnly(action.TargetSeats.ToArray()), AdditionalConversionSources = Array.AsReadOnly((action.AdditionalConversionSources ?? []).ToArray()),
        SelectableCardIds = Array.AsReadOnly(action.SelectableCardIds.ToArray()), SelectableTargetSeats = Array.AsReadOnly(action.SelectableTargetSeats.ToArray()),
        SelectableCardTargets = action.SelectableCardTargets is null ? null : Array.AsReadOnly(action.SelectableCardTargets.ToArray()),
        SelectableTargetSequences = action.SelectableTargetSequences is null ? null : Array.AsReadOnly(action.SelectableTargetSequences.Select(t => (IReadOnlyList<int>)Array.AsReadOnly(t.ToArray())).ToArray())
    };
    private static bool SameHappyQiaoPlan(HappyQiaoPlan first, HappyQiaoPlan second) => JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);
    private void IssueHappyQiaoShutuUse(ProgramSkillFrame frame)
    {
        RequireHappyQiaoSkill(frame);
        if (frame.HappyQiao is not { Stage: HappyQiaoStage.IssuingUse, Native: { UseId: 0, ActionId: 0 } native } receipt) throw new InvalidOperationException("Shutu lost its accepted original native Use intent.");
        var actor = _players[frame.OwnerSeat]; var plan = native.Plan;
        if (!receipt.DeclarationPassed)
        {
            ReplaceRuntimeFrame(frame.Id, frame with { HappyQiao = receipt with { Stage = HappyQiaoStage.UseChoice } });
            var legal = HappyQiaoUsePlans(GetActiveProgramFrame(frame.Id)).Any(p => SameHappyQiaoPlan(p, plan)); ReplaceRuntimeFrame(frame.Id, frame);
            if (!legal) throw new InvalidOperationException("Shutu's actual native material, conversion or accepted target expired.");
        }
        if (plan.Form == "native" && plan.Cards is [var cardId] && plan.Action.ConversionSource is { } source && ViewAsRule(source)?.DeclarationValidation is not null && !receipt.DeclarationPassed)
        {
            ReplaceRuntimeTop(frame = frame with { HappyQiao = receipt with { Stage = HappyQiaoStage.Declaration, Before = _movementSequence } });
            if (!TryBeginCardDeclaration(actor, GetAdvancedCard(cardId), plan.Kind, source, DeclarationReturn(CardDeclarationPurpose.HappyQiaoShutuUse, actor.Seat, action: plan.Action, finalKind: plan.Kind), plan.Targets))
                throw new InvalidOperationException("Shutu cannot bypass an actual declaration challenge.");
            return;
        }
        if (plan.Form == "native") { ExecuteAction(actor, plan.Action); return; }
        var cards = plan.Cards.Select(GetAdvancedCard).ToArray();
        if (plan.Form == "zhangba") { ResolveZhangbaSlash(actor, _players[plan.Targets.Single()], cards); return; }
        var conversion = plan.Action.ConversionSource ?? throw new InvalidOperationException("A multi-material Shutu Use lost its real conversion.");
        if (FindProgramMultiCardViewAsSelection(actor, plan.Cards, plan.Kind, false, conversion) is null) throw new InvalidOperationException("Shutu's accepted multi-material conversion expired.");
        if (IsSlashCard(plan.Kind)) { ResolveSlashCore(actor, _players[plan.Targets.Single()], cards[0], plan.Kind, actor.Seat, physicalCards: cards, conversionSource: conversion); return; }
        if (plan.Kind == CardKind.Peach) { ResolveRecoveryCard(actor, actor, cards[0], "桃", CardKind.Peach, conversionSource: conversion, physicalCards: cards); return; }
        var useId = BeginCardUse(cards[0], actor.Seat, [], CardKind.Alcohol, physicalCardIds: plan.Cards, conversionSource: conversion);
        foreach (var card in cards) MoveCard(card, FindOwnedCardLocation(actor, card), CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        if (!HasIssuedRoundGainedBasicBonus(useId, actor.Seat)) RecordMouZhaJiangAwareAlcoholDebit(useId, actor);
        BeginSimpleCardUse(useId, new(cards[0].Id, SimpleCardUseEffect.Alcohol));
    }
    private IReadOnlyList<int> HappyQiaoExpectedUseTargets(HappyQiaoNativeUse native)
    {
        var actor = _players[native.Source.OwnerSeat]; var plan = native.Plan; var action = plan.Action;
        if (plan.Form != "native") return plan.Targets;
        if (plan.Cards.Count == 0) return GetImplicitSelfCardUseTargets(plan.Kind, actor.Seat, plan.Targets);
        var card = ApplyProgramUseAppearance(actor, GetAdvancedCard(plan.Cards.Single()), action.ConversionSource);
        if (action.ConversionSource is { } source && ViewAsRule(source) is { SingleCardTrickUse: true } rule)
            return BuildProgramOrdinaryTrickUseOptions(actor, rule.OutputKind, EffectiveSuit(actor, GetAdvancedCard(plan.Cards.Single())), rule.ExcludeOwnerEffects,
                    enforceUsePermission: true, beneficiaryShieldSuit: EffectiveSuit(actor, card), actualEffectiveColor: SuitColor(EffectiveSuit(actor, card)), hasActualColor: true, physicalCardIds: plan.Cards)
                .Where(o => o.ActionKind == action.Kind && o.TargetCardId == action.TargetCardId && (o.TargetSeats.SequenceEqual(plan.Targets) || action.ProgramActivationId == "red-additional-targets" && o.TargetSeats.SequenceEqual(plan.Targets.Take(o.TargetSeats.Count))))
                .OrderByDescending(o => o.TargetSeats.Count).Select(o => o.TargetSeats).First();
        if (GuanduExpectedNativeTargets(actor, card, action) is { } guandu) return guandu;
        if (DragonBoatExpectedNativeTargets(actor, card, action) is { } dragon) return dragon;
        if (plan.Kind is CardKind.BeamSwap or CardKind.WenheChaos or CardKind.SowBeans) return [actor.Seat];
        if (action.Kind is not (LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage or LegalActionKind.PeachGarden or LegalActionKind.FiveGrains))
            return GetImplicitSelfCardUseTargets(plan.Kind, actor.Seat, plan.Targets);
        var offensive = action.Kind is LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage;
        var targets = Enumerable.Range(offensive ? 1 : 0, _playerCount - (offensive ? 1 : 0)).Select(i => _players[TableStepSeat(actor.Seat, i)])
            .Where(p => p.IsAlive && (!offensive || !HasCardPolicy(p, SkillProgramCardPolicyKind.ExcludeGlobalTarget, plan.Kind)) &&
                !IsCardTargetProhibited(p, plan.Kind, card.Suit, SuitColor(EffectiveSuit(actor, card))) && !HasBeneficiarySuitShield(actor.Seat, p.Seat, EffectiveSuit(actor, card)) &&
                !TargetRules.IsDeclaredGlobalProhibited(new(actor.Seat, p.Seat, plan.Kind))).Select(p => p.Seat).ToArray();
        if (!offensive) targets = ApplyTurnCardGroupTargetRestrictions(actor, targets).ToArray();
        if (action.Kind == LegalActionKind.FiveGrains) targets = targets.Take(_cardZones.Count(CardLocation.DrawPile) + _cardZones.Count(CardLocation.DiscardPile)).ToArray();
        return Array.AsReadOnly(targets);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 42400)] private void BindHappyQiaoNativeUse(IGameEvent fact)
    {
        if (fact is not CardUseDeclaredEvent declared || LifecycleCardUse(declared.ResolutionId) is not { Action: { Type: CardActionType.Use } action } use) return;
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { HappyQiao: { Stage: HappyQiaoStage.IssuingUse, Native: { UseId: 0, ActionId: 0 } native } receipt } frame) return;
        RequireHappyQiaoSkill(frame);
        var expectedTargets = HappyQiaoExpectedUseTargets(native);
        if (action.ActorSeat != frame.OwnerSeat || action.ProviderSeat != frame.OwnerSeat || action.RequesterSeat is not null || use.SourceSeat != frame.OwnerSeat || use.CardKind != native.Plan.Kind ||
            !action.PhysicalCards.Select(c => c.CardId).Order().SequenceEqual(native.Plan.Cards.Order()) ||
            action.PhysicalCards.Any(c => !native.Costs.Any(cost => cost.CardId == c.CardId && cost.CardKind == c.CardKind && cost.From == c.From)) ||
            !(use.PhysicalCardIds ?? []).Order().SequenceEqual(native.Plan.Cards.Order()) || !use.TargetSeats.SequenceEqual(expectedTargets) ||
            !action.ConversionChain.SequenceEqual((native.Plan.Action.ConversionSource is { } source ? new[] { source } : []).Concat(native.Plan.Action.AdditionalConversionSources ?? [])))
            throw new InvalidOperationException("Shutu changed its actual actor, original owned materials, accepted conversion or native targets.");
        var bound = native with { UseId = use.Id, ActionId = action.ActionId, Targets = expectedTargets };
        var policy = use.IssuedNoResponse ?? new IssuedCardNoResponse(action.ActionId, use.Id, native.Source, _turnNumber, _cardUseDebitPhaseInstanceId);
        ReplaceRuntimeFrame(frame.Id, frame with { HappyQiao = receipt with { Stage = HappyQiaoStage.NativeUse, Native = bound } });
        UpdateLifecycleCardUse(use.Id, owner => owner with { HappyQiaoShutuUse = bound, UnlimitedUse = true, IssuedNoResponse = policy });
        if (use.IssuedNoResponse is null) AdvanceEventRulesAndQueueFact(new IssuedCardNoResponseEvent(policy));
        AdvanceEventRulesAndQueueFact(new HappyQiaoUseIssuedEvent(frame.Id, use.Id, action.ActionId, frame.OwnerSeat));
    }
    private bool HappyQiaoNativeUseRidesOn(CardUseFrame use, ProgramSkillFrame frame) => use.HappyQiaoShutuUse is { } native &&
        frame.HappyQiao is { Stage: HappyQiaoStage.NativeUse or HappyQiaoStage.ReturnedUse, Native: { } pending } receipt && native == pending &&
        native.FrameId == frame.Id && native.Instruction == frame.InstructionIndex && native.Source == receipt.Source && native.Hash == frame.GameplayHash && native.Turn == _turnNumber &&
        use.Id == native.UseId && use.SourceSeat == frame.OwnerSeat && use.CardKind == native.Plan.Kind && use.TargetSeats.SequenceEqual(native.Targets) &&
        use.Action is { Type: CardActionType.Use } action && action.ActionId == native.ActionId && action.ActorSeat == frame.OwnerSeat && action.ProviderSeat == frame.OwnerSeat &&
        action.PhysicalCards.Select(c => c.CardId).Order().SequenceEqual(native.Plan.Cards.Order()) && action.PhysicalCards.All(c => native.Costs.Any(cost => cost.CardId == c.CardId && cost.CardKind == c.CardKind && cost.From == c.From)) &&
        ProgramEventHistory<HappyQiaoUseIssuedEvent>().Count(e => e == new HappyQiaoUseIssuedEvent(frame.Id, use.Id, native.ActionId, frame.OwnerSeat)) == 1;
    [NativeUseValidation(42400)] private void ValidateHappyQiaoUse(CardUseFrame use)
    {
        if (use.HappyQiaoShutuUse is { } native && (use.Step != ResolutionFrameStep.Completed || _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == native.FrameId) is not { } parent || !HappyQiaoNativeUseRidesOn(use, parent)))
            throw new InvalidOperationException("Shutu cannot retire another or unfinished original native Use.");
    }
    [NativeUseReturn(42400)] private bool ReturnHappyQiaoUse(CardUseFrame use)
    {
        if (use.HappyQiaoShutuUse is not { } native) return false;
        var frame = GetActiveProgramFrame(native.FrameId);
        if (LifecycleCardUse(use.Id) is not null || use.Step != ResolutionFrameStep.Completed || !HappyQiaoNativeUseRidesOn(use, frame) ||
            ProgramEventHistory<HappyQiaoUseReturnedEvent>().Any(e => e.UseId == use.Id)) throw new InvalidOperationException("Shutu requires its exact once-completed typed native return.");
        ReplaceRuntimeTop(frame = frame with { HappyQiao = frame.HappyQiao! with { Stage = HappyQiaoStage.ReturnedUse } });
        AdvanceEventRulesAndQueueFact(new HappyQiaoUseReturnedEvent(frame.Id, use.Id, native.ActionId));
        if (use.CardAttack is not { Active: true }) ContinueHappyQiao(frame.Id);
        return true;
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 42400)] private bool ResumeHappyQiaoNativeAttack()
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HappyQiao: { Stage: HappyQiaoStage.ReturnedUse } } frame) return false; return ContinueHappyQiao(frame.Id); }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 42400)] private bool HappyQiaoOffTurnSlash(long useId, int actor) => actor != _currentSeat && LifecycleCardUse(useId) is { HappyQiaoShutuUse: { } native } use &&
        native.Source.OwnerSeat == actor && _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == native.FrameId) is { } frame && HappyQiaoNativeUseRidesOn(use, frame);
}
