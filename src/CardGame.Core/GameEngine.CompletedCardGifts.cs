namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string CompletedGiftFactionResultBind = "completed-card-gift-slash";
    private CardActionContext CompletedGiftAction(ProgramSkillFrame frame)
    {
        var context = frame.WindowContext;
        if (context is not { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } use } ||
            use.ActorSeat != frame.OwnerSeat)
            throw new InvalidOperationException("A completed-card gift requires the owner's completed use.");
        return _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == use.ParentCardUseFrameId &&
            frame.Action?.ActionId == use.CardActionId).Action!;
    }

    private IReadOnlyList<CardActionContext> CompletedGiftSources(ProgramSkillFrame frame)
    {
        var use = CompletedGiftAction(frame);
        var sources = new List<CardActionContext> { use };
        sources.AddRange(_events.Select(envelope => envelope.Payload).Concat(_pendingEvents).OfType<CardActionAcceptedEvent>()
            .Select(item => item.Action).Where(response => response.Type == CardActionType.Response &&
                response.ParentActionId == use.ActionId && response.EffectiveKind == CardKind.Dodge &&
                response.ResponderSeat is { } responder && use.EffectiveDesignatedTargetSeats.Contains(responder)));
        return sources.Where(action => action.PhysicalCards.Count > 0 && action.PhysicalCards.All(cost =>
            _cardZones.GetLocation(cost.CardId) is { Zone: CardZoneKind.Processing or CardZoneKind.DiscardPile })).ToArray();
    }

    private int[] CompletedGiftSlashTargets(ProgramSkillFrame frame, int actorSeat) => _players.Where(target =>
        _players[frame.OwnerSeat].IsAlive && _players[actorSeat].IsAlive && target.IsAlive &&
        target.Seat != actorSeat && target.Seat != frame.OwnerSeat &&
        IsWithinAttackRange(frame.OwnerSeat, target.Seat)).Select(target => target.Seat).ToArray();

    private SkillProgramStepOutcome OfferProgramCompletedCardGift(ProgramSkillFrame frame)
    {
        var action = CompletedGiftAction(frame);
        if (frame.CompletedCardGiftDraft is not null) throw new InvalidOperationException("A completed gift was offered twice.");
        if (!IsSlashCard(action.EffectiveKind) || CompletedGiftSources(frame).Count == 0 ||
            !_players.Any(player => player.IsAlive && player.Seat != frame.OwnerSeat)) return SkillProgramStepOutcome.Continue;
        frame = frame with { CompletedCardGiftDraft = new(action.ActionId) };
        ReplaceRuntimeTop(frame);
        PublishCompletedCardGiftPrompt(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> CompletedCardGiftChoices(ProgramSkillFrame frame)
    {
        var draft = frame.CompletedCardGiftDraft ?? throw new InvalidOperationException("Completed gift lost its draft.");
        Dictionary<string, string> Parameters(string option) => new()
        { ["program-action"] = "completed-card-gift", ["gift-option"] = option, ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var choices = new List<PromptChoice>();
        if (draft.RecipientSeat is null)
        {
            foreach (var source in CompletedGiftSources(frame))
            foreach (var recipient in _players.Where(player => player.IsAlive && player.Seat != frame.OwnerSeat))
                choices.Add(new(new ChoiceId($"completed-gift.{frame.Id}.{source.ActionId}.{recipient.Seat}"),
                    $"交给 {recipient.Name}【{CardCatalog.Get(source.EffectiveKind).DisplayName}】", source.PhysicalCards.Select(card => card.CardId).ToArray(),
                    [recipient.Seat], Parameters("give")));
        }
        else
        {
            var actor = _players[draft.RecipientSeat.Value];
            if (GetFactionResponsePolicy(actor, CardKind.Slash) is { } policy)
                foreach (var target in CompletedGiftSlashTargets(frame, actor.Seat))
                {
                    var parameters = Parameters("faction"); parameters["skill"] = policy.SkillId;
                    choices.Add(new(new ChoiceId($"completed-gift.{frame.Id}.faction.{target}"), $"请求同势力角色提供杀，对 {_players[target].Name} 使用", [], [target], parameters));
                }
            foreach (var target in CompletedGiftSlashTargets(frame, actor.Seat))
            foreach (var card in GetPlayableCards(actor).Concat(GetEquipment(actor)).DistinctBy(card => card.Id))
            foreach (var kind in SlashKinds)
            foreach (var conversion in GetProgramViewAsConversions(actor, card, kind, forResponse: false))
            {
                if (IsTurnHandCardRestricted(actor, card) || !CanUseSlashTarget(actor, _players[target], card, conversion, kind, ignoreDistance: true)) continue;
                var parameters = Parameters("slash"); parameters["effective-kind"] = kind.ToString(); AddConversionParameters(parameters, conversion);
                choices.Add(new(new ChoiceId($"completed-gift.{frame.Id}.typed.{kind}.{conversion.SkillId}.{conversion.BindingId}.{card.Id}.{target}"),
                    $"将牌当【{CardCatalog.Get(kind).DisplayName}】对 {_players[target].Name} 使用", [card.Id], [target], parameters));
            }
            foreach (var target in CompletedGiftSlashTargets(frame, actor.Seat))
            foreach (var pair in GetZhangbaSlashPairs(actor))
            {
                if (!CanUseSlashTarget(actor, _players[target], pair[0], effectiveKind: CardKind.Slash, ignoreDistance: true)) continue;
                var parameters = Parameters("slash"); parameters["effective-kind"] = CardKind.Slash.ToString(); parameters["equipment"] = CardKind.ZhangbaSerpentSpear.ToString();
                var ids = pair.Select(card => card.Id).ToArray();
                choices.Add(new(new ChoiceId($"completed-gift.{frame.Id}.zhangba.{string.Join('-', ids)}.{target}"),
                    $"使用丈八蛇矛对 {_players[target].Name} 使用杀", ids, [target], parameters));
            }
            foreach (var target in CompletedGiftSlashTargets(frame, actor.Seat))
            foreach (var card in GetSlashUseCards(actor))
            {
                var identity = GetProgramCardIdentityMatches(actor, card).FirstOrDefault(item => IsSlashCard(item.Identity.OutputKind));
                var baseKind = identity?.Identity.OutputKind ?? (IsSlashCard(card.Kind) ? card.Kind : CardKind.Slash);
                foreach (var kind in GetSlashUseKinds(actor, baseKind))
                {
                    if (!CanUseSlashTarget(actor, _players[target], card, effectiveKind: kind, ignoreDistance: true)) continue;
                    var parameters = Parameters("slash"); parameters["effective-kind"] = kind.ToString();
                    choices.AddRange(CreateConversionChoiceVariants(actor, card, kind, false, $"completed-gift.{frame.Id}.slash.{card.Id}.{kind}.{target}",
                        $"对 {_players[target].Name} 使用【{CardCatalog.Get(kind).DisplayName}】", [card.Id], [target], parameters));
                }
            }
            foreach (var target in CompletedGiftSlashTargets(frame, actor.Seat))
            foreach (var kind in SlashKinds)
            foreach (var selection in GetProgramMultiCardViewAsSelections(actor, kind, false))
            {
                if (!CanUseSlashTarget(actor, _players[target], selection.Cards[0], selection.Source, kind, ignoreDistance: true)) continue;
                var parameters = Parameters("slash"); parameters["effective-kind"] = kind.ToString(); AddConversionParameters(parameters, selection.Source);
                var ids = selection.Cards.Select(card => card.Id).ToArray();
                choices.Add(new(new ChoiceId($"completed-gift.{frame.Id}.multi.{selection.Source.SkillId}.{selection.Source.BindingId}.{string.Join('-', ids)}.{target}"),
                    $"对 {_players[target].Name} 使用【{CardCatalog.Get(kind).DisplayName}】", ids, [target], parameters));
            }
        }
        choices.Add(new(new ChoiceId($"completed-gift.{frame.Id}.decline"), draft.RecipientSeat is null ? "不交出牌" : "不使用杀", [], [], Parameters("decline")));
        return choices.Where(c => draft.RecipientSeat is not { } actorSeat || c.Parameters.GetValueOrDefault("gift-option") != "slash" || !IsTurnPhysicalUseForbidden(actorSeat,c.Cards)).ToArray();
    }

    private void PublishCompletedCardGiftPrompt(ProgramSkillFrame frame)
    {
        var choices = CompletedCardGiftChoices(frame);
        var chooser = frame.CompletedCardGiftDraft!.RecipientSeat ?? frame.OwnerSeat;
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, chooser == frame.OwnerSeat ? "将本次杀或目标响应的闪交给一名其他角色。" : "可以对技能拥有者攻击范围内的角色使用一张杀。",
            choices.SelectMany(choice => choice.Cards).Distinct().ToArray(), choices.SelectMany(choice => choice.Targets).Distinct().ToArray(), frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = choices, SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveCompletedCardGiftChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Completed gift lost its frame.");
        var draft = frame.CompletedCardGiftDraft ?? throw new InvalidOperationException("Completed gift lost its draft.");
        selected = CompletedCardGiftChoices(frame).SingleOrDefault(choice => choice.Id == selected.Id) ?? throw new InvalidOperationException("Completed gift option is no longer available.");
        var instruction = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (instruction.Op != SkillProgramEffectOp.OfferCompletedCardGift || _pendingDecision?.PlayerSeat != (draft.RecipientSeat ?? frame.OwnerSeat))
            throw new InvalidOperationException("Completed gift lost its frozen instruction or chooser.");
        ClearPendingDecision();
        if (selected.Parameters["gift-option"] == "decline" || !_players[frame.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        { CompleteProgramCardGift(frame); return; }
        if (draft.RecipientSeat is null)
        {
            var recipient = selected.Targets.Single();
            var cards = selected.Cards.Select(id => { var from = _cardZones.GetLocation(id); return _cardZones.CardsAt(from).Single(card => card.Id == id); }).ToArray();
            var red = cards.All(card => card.Suit is Suit.Heart or Suit.Diamond);
            var moves = cards.Select(card => (Card: card, From: _cardZones.GetLocation(card.Id))).ToArray();
            var batch = BeginCardMovementBatch(moves.Select(move => move.From), [CardLocation.Hand(recipient)]);
            var movements = new List<CardMovementRecord>(); var committed = false;
            try
            {
                foreach (var move in moves) _cardZones.Move(move.Card.Id, move.From, CardLocation.Hand(recipient));
                foreach (var move in moves) movements.Add(RecordMovement(move.Card, move.From, CardLocation.Hand(recipient),
                    new CardMoveReason($"skill-program.{frame.SkillId}.completed-card-gift")));
                committed = true;
            }
            finally { CompleteCardMovementBatch(batch, movements, committed); }
            AdvanceEventRulesAndQueueFact(new CompletedCardGiftedEvent(frame.Id, draft.CardActionId, frame.OwnerSeat, recipient, selected.Cards, red));
            if (!red || !_players[recipient].IsAlive) { CompleteProgramCardGift(frame); return; }
            frame = frame with { CompletedCardGiftDraft = draft with { RecipientSeat = recipient, GiftWasRed = true }, SelectedTargetSeats = [recipient] };
            ReplaceRuntimeTop(frame);
            PublishCompletedCardGiftPrompt(frame);
            return;
        }
        var actor = _players[draft.RecipientSeat.Value]; var target = selected.Targets.Single();
        if (selected.Parameters["gift-option"] == "faction")
        {
            ReplaceRuntimeTop(frame = frame with { CompletedCardGiftDraft = draft with { RequestTargetSeat = target } });
            BeginAssistedProgramFactionSlashRequest(frame, actor.Seat, target, CompletedGiftFactionResultBind);
            return;
        }
        var kind = Enum.Parse<CardKind>(selected.Parameters["effective-kind"]);
        TryReadConversionSource(selected.Parameters, out var conversion);
        IReadOnlyList<Card> paid = selected.Cards.Count == 1
            ? [GetPlayableCards(actor).Concat(GetEquipment(actor)).Single(card => card.Id == selected.Cards[0])]
            : selected.Parameters.GetValueOrDefault("equipment") == CardKind.ZhangbaSerpentSpear.ToString()
                ? GetZhangbaSlashPairs(actor).Single(pair => pair.Select(card => card.Id).SequenceEqual(selected.Cards))
                : FindProgramMultiCardViewAsSelection(actor, selected.Cards, kind, false, conversion!)?.Cards ?? throw new InvalidOperationException("Completed gift Slash lost its physical payment.");
        if (!CompletedGiftSlashTargets(frame, actor.Seat).Contains(target) || !CanUseSlashTarget(actor, _players[target], paid[0], conversion, kind, ignoreDistance: true))
            throw new InvalidOperationException("Completed gift Slash target is no longer legal.");
        ResolveSlashCore(actor, _players[target], paid[0], kind, actor.Seat, physicalCards: paid, countsTowardSlashLimit: false,
            usesZhuqueFan: kind == CardKind.FireSlash && paid[0].Kind == CardKind.Slash && HasZhuqueFan(actor), conversionSource: conversion, programSkillCardUseFrameId: frame.Id);
    }

    private void CompleteProgramCardGift(ProgramSkillFrame frame)
    {
        ReplaceRuntimeTop(frame with { CompletedCardGiftDraft = null });
        AdvanceRuntimeProgram(frame.Id);
    }

    private bool IsValidCompletedGiftTargetSelection(ProgramSkillFrame frame) =>
        frame.CompletedCardGiftDraft is { RecipientSeat: { } recipient, GiftWasRed: true } draft &&
        IsValidPlayerSeat(recipient) && recipient != frame.OwnerSeat && frame.SelectedTargetSeats.SequenceEqual(new[] { recipient }) &&
        frame.WindowContext is { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } use } &&
        use.CardActionId == draft.CardActionId && use.ActorSeat == frame.OwnerSeat &&
        _events.Select(item => item.Payload).Concat(_pendingEvents).OfType<CompletedCardGiftedEvent>().Any(gift =>
            gift.FrameId == frame.Id && gift.CardActionId == draft.CardActionId && gift.OwnerSeat == frame.OwnerSeat && gift.RecipientSeat == recipient && gift.IsRed);

    private void AssertCompletedCardGiftDraft(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.CompletedCardGiftDraft is not { } draft) return;
        if (paused.Op != SkillProgramEffectOp.OfferCompletedCardGift || CompletedGiftAction(frame).ActionId != draft.CardActionId ||
            (draft.RecipientSeat is null ? draft.GiftWasRed || frame.SelectedTargetSeats.Count != 0 || draft.RequestTargetSeat is not null : !IsValidCompletedGiftTargetSelection(frame)))
            throw new InvalidOperationException("A completed gift lost its action, recipient or physical gift provenance.");
        if (draft.RequestTargetSeat is { } target &&
            (ActiveFactionCardRequest is not { IsAssistedProgramUse: true } faction || faction.ProgramSkillFrameId != frame.Id || faction.TargetSeat != target || faction.OwnerSeat != draft.RecipientSeat))
            throw new InvalidOperationException("A completed gift faction request lost its frozen target or recipient.");
        if (!ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && draft.RequestTargetSeat is null &&
            !_resolutionStack.OfType<CardUseFrame>().Any(child => child.Action is { } action && action.ParentActionId == draft.CardActionId &&
                action.ActorSeat == draft.RecipientSeat && IsSlashCard(action.EffectiveKind) && action.PhysicalCards.Count > 0))
            throw new InvalidOperationException("A completed gift lost its real recipient-owned Slash child.");
        if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && draft.RequestTargetSeat is null &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat != (draft.RecipientSeat ?? frame.OwnerSeat) ||
             !AssistedChoicesEqual(prompt.Choices, CompletedCardGiftChoices(frame))))
            throw new InvalidOperationException("A completed gift prompt changed its frozen physical cards, recipient or chooser.");
    }

    private sealed partial class ProgramSkillHost : ICompletedCardGiftProgramHost
    {
        public SkillProgramStepOutcome OfferCompletedCardGift(ProgramSkillFrame frame) => engine.OfferProgramCompletedCardGift(frame);
    }
}
