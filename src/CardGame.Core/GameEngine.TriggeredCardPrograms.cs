namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsProgramDiscardedDelayedTrickTarget(int ownerSeat, CharacterState target, ProgramSkillWindowContext? context)
    {
        if (context is not { Window: SkillProgramTriggerWindow.DiscardPileReceived, MovementBatch: { } batch, MovementIndex: { } index } ||
            index < 0 || index >= batch.Movements.Count || !_players[ownerSeat].IsAlive || !target.IsAlive || target.Seat == ownerSeat ||
            target.JudgmentAreaAbolished || HasJudgmentEffectiveCard(target, CardKind.SupplyShortage) ||
            IsCardUseForbidden(ownerSeat, CardKind.SupplyShortage, CardActionType.Use) ||
            IsDirectedCardTargetProhibited(ownerSeat, target.Seat, CardKind.SupplyShortage)) return false;
        var movement = batch.Movements[index];
        if (GetProgramDiscardSource(movement)?.OwnerSeat != ownerSeat || _cardZones.GetLocation(movement.CardId) != CardLocation.DiscardPile)
            return false;
        var card = _cardZones.CardsAt(CardLocation.DiscardPile).Single(item => item.Id == movement.CardId);
        return card.Suit is Suit.Spade or Suit.Club &&
            ProgramCardSetFilter.Matches(card.Kind, card.Suit, [], [SkillProgramCardCategory.Basic], [], []) &&
            !IsCardTargetProhibited(target, CardKind.SupplyShortage, card.Suit, SuitColor(EffectiveSuit(_players[ownerSeat], card)));
    }

    private SkillProgramStepOutcome UseTriggeredProgramVirtualSlash(ProgramSkillFrame frame, int targetSeat)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (!IsValidPlayerSeat(targetSeat) || !_players[active.OwnerSeat].IsAlive || !_players[targetSeat].IsAlive ||
            targetSeat == active.OwnerSeat || IsCardUseForbidden(active.OwnerSeat, CardKind.Slash, CardActionType.Use) ||
            IsDirectedCardTargetProhibited(active.OwnerSeat, targetSeat, CardKind.Slash) || IsSlashProhibited(_players[targetSeat]))
            return SkillProgramStepOutcome.Continue;
        if (ActiveCardAttack is not null || ActiveDuel is not null)
            throw new InvalidOperationException("A triggered virtual Slash cannot replace a pending attack.");
        var source = _players[active.OwnerSeat];
        var target = _players[targetSeat];
        var resolutionId = ++_resolutionSequence;
        var provenance = new CardConversionSource(active.SkillId, GetProgramBindingId(active), active.OwnerSeat, active.SkillInstanceId);
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId,
            CardActionType.Use, source.Seat, source.Seat, null, null, null, CardKind.Slash, [targetSeat], [], [provenance], effectiveSuit: Suit.None, effectiveRank: 0));
        PushRuntimeFrame(new CardUseFrame(resolutionId, source.Seat, 0, CardKind.Slash, [targetSeat], PhysicalCardIds: []) { Action = action });
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(resolutionId, 0, CardKind.Slash, source.Seat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(resolutionId, [targetSeat]));
        var attack = new CardAttackHandle(this, resolutionId, source.Seat, targetSeat, card: null,
            damageAmount: source.HasAlcoholEffect ? 2 : 1, playedCardKind: CardKind.Slash,
            ignoresArmor: HasCardArmorBypass(source, target, CardKind.Slash), programSkillCardUseFrameId: active.Id);
        CaptureProgramAlcoholConsumption(resolutionId, source);
        source.HasAlcoholEffect = false;
        ActiveCardAttack = attack;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, source.Seat, targetSeat));
        TryMarkProgramUseCommitted(resolutionId);
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, action.TargetSeats, ProgramCardContinuation.CommittedSlash))
            BeginSlashTargetResolution(attack);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private SkillProgramStepOutcome UseProgramDiscardedCardAsDelayedTrick(ProgramSkillFrame frame, int targetSeat, CardKind kind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (kind != CardKind.SupplyShortage || active.WindowContext is not
            { Window: SkillProgramTriggerWindow.DiscardPileReceived, MovementBatch: { } batch, MovementIndex: { } index } ||
            index < 0 || index >= batch.Movements.Count)
            throw new InvalidOperationException("A discarded-card conversion lost its frozen discard occurrence.");
        var movement = batch.Movements[index];
        if (GetProgramDiscardSource(movement)?.OwnerSeat != active.OwnerSeat || movement.To != CardLocation.DiscardPile ||
            movement.From == movement.To)
            throw new InvalidOperationException("A discarded-card conversion requires the owner's actual discarded card.");
        if (_cardZones.GetLocation(movement.CardId) != CardLocation.DiscardPile || !_players[active.OwnerSeat].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var card = _cardZones.CardsAt(CardLocation.DiscardPile).Single(item => item.Id == movement.CardId);
        if (IsRedSuit(card.Suit) || card.Suit == Suit.None || !ProgramCardSetFilter.Matches(card.Kind, card.Suit, [], [SkillProgramCardCategory.Basic], [], []))
            throw new InvalidOperationException("The frozen discarded card is not a black basic card.");
        if (!IsValidPlayerSeat(targetSeat) || !_players[targetSeat].IsAlive || targetSeat == active.OwnerSeat ||
            _players[targetSeat].JudgmentAreaAbolished || HasJudgmentEffectiveCard(_players[targetSeat], kind) ||
            IsCardUseForbidden(active.OwnerSeat, kind, CardActionType.Use) || IsDirectedCardTargetProhibited(active.OwnerSeat, targetSeat, kind) || IsCardTargetProhibited(_players[targetSeat], kind, card.Suit, SuitColor(EffectiveSuit(_players[active.OwnerSeat], card))))
            return SkillProgramStepOutcome.Continue;
        var source = new CardConversionSource(active.SkillId, GetProgramBindingId(active), active.OwnerSeat, active.SkillInstanceId);
        var resolutionId = BeginCardUse(card, active.OwnerSeat, [targetSeat], kind, conversionSource: source);
        MoveCard(card, CardLocation.DiscardPile, CardLocation.Processing, CardMoveReasons.Use);
        BeginJizhiOrNullificationWindow(resolutionId, card, active.OwnerSeat, [targetSeat], LegalActionKind.SupplyShortage, playedCardKind: kind);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private Card[] EquipmentColorDiscardCandidates(int ownerSeat)
    {
        var owner = _players[ownerSeat];
        var colors = _cardZones.CardsAt(CardLocation.Equipment(ownerSeat)).Select(card => IsRedSuit(EffectiveSuit(owner, card))).Distinct().ToArray();
        return GetHand(_players[ownerSeat]).Concat(_cardZones.CardsAt(CardLocation.Equipment(ownerSeat)))
            .Where(card => !ProgramCardSetFilter.Matches(card.Kind, card.Suit, [], [SkillProgramCardCategory.Basic], [], []) && EffectiveSuit(owner, card) != Suit.None && colors.Contains(IsRedSuit(EffectiveSuit(owner, card))))
            .OrderBy(card => card.Id).ToArray();
    }

    private SkillProgramStepOutcome PayProgramEquipmentColorDiscard(ProgramSkillFrame frame, string resultBind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.CardSetBindings.Any(binding => binding.Name == resultBind))
            return SkillProgramStepOutcome.Continue;
        var candidates = EquipmentColorDiscardCandidates(active.OwnerSeat);
        if (candidates.Length == 0)
        {
            CancelProgramBindingAndCleanup(active, "没有可支付的与装备颜色相同的非基本牌。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var choices = candidates.Select(card => new PromptChoice(new ChoiceId($"equipment-color-discard.frame-{active.Id}.card-{card.Id}"),
            $"弃置 {card.DisplayName}", [card.Id], [], new Dictionary<string, string>
            { ["program-action"] = "equipment-color-discard", ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["result-bind"] = resultBind })).ToArray();
        var skill = _contentRegistry!.GetSkill(active.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, active.OwnerSeat,
            "弃置一张与装备区任意牌颜色相同的非基本牌。", candidates.Select(card => card.Id).ToArray(), [], active.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = Array.AsReadOnly(choices), SkillPrompt = new SkillPromptPresentation(active.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[active.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void AssertProgramEquipmentColorDiscardChoice(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        if (effect.Op != SkillProgramEffectOp.PayEquipmentColorDiscard || !ReferenceEquals(frame, _resolutionStack.LastOrDefault())) return;
        var eligible = EquipmentColorDiscardCandidates(frame.OwnerSeat).Select(card => card.Id).ToHashSet();
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision || decision.PlayerSeat != frame.OwnerSeat || decision.Choices.Count == 0 ||
            decision.Choices.Any(choice => choice.Cards.Count != 1 || !eligible.Contains(choice.Cards[0]) || choice.Targets.Count != 0 ||
                choice.Parameters.GetValueOrDefault("program-action") != "equipment-color-discard" || choice.Parameters.GetValueOrDefault("result-bind") != effect.ResultBind ||
                choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            throw new InvalidOperationException("An equipment-color discard prompt changed its owner, candidates or binding.");
    }

    private void ResolveProgramEquipmentColorDiscardChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("A discard payment lost its program frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        AssertProgramEquipmentColorDiscardChoice(frame, effect);
        if (effect.Op != SkillProgramEffectOp.PayEquipmentColorDiscard || choice.Cards.Count != 1 || choice.Targets.Count != 0 ||
            choice.Parameters.GetValueOrDefault("result-bind") != effect.ResultBind || choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The discard payment changed its frozen instruction.");
        var card = EquipmentColorDiscardCandidates(frame.OwnerSeat).SingleOrDefault(card => card.Id == choice.Cards[0]) ?? throw new InvalidOperationException("The payment card is no longer eligible.");
        var location = _cardZones.GetLocation(card.Id);
        ClearPendingDecision();
        SetProgramCardSet(frame.Id, effect.ResultBind!, [card.Id], SkillProgramCardSetVisibility.Public, [location], EffectiveSuit(_players[frame.OwnerSeat], card));
        MoveCard(card, location, CardLocation.DiscardPile, new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
        if (!TryBeginCardsMovedProgramWindow()) AdvanceRuntimeProgram(frame.Id);
    }

    private sealed partial class ProgramSkillHost : ITriggeredCardProgramHost
    {
        public SkillProgramStepOutcome UseTriggeredVirtualSlash(ProgramSkillFrame frame, int targetSeat) => engine.UseTriggeredProgramVirtualSlash(frame, targetSeat);
        public SkillProgramStepOutcome UseDiscardedCardAsDelayedTrick(ProgramSkillFrame frame, int targetSeat, CardKind kind) => engine.UseProgramDiscardedCardAsDelayedTrick(frame, targetSeat, kind);
        public SkillProgramStepOutcome PayEquipmentColorDiscard(ProgramSkillFrame frame, string resultBind) => engine.PayProgramEquipmentColorDiscard(frame, resultBind);
        public void GrantPlayPhaseColorRestriction(ProgramSkillFrame frame, string sourceBind, int targetSeat) => engine.GrantProgramPlayPhaseColorRestriction(frame, sourceBind, targetSeat);
    }
}
