namespace CardGame.Core;

public sealed record ProgramActionCategoryGiftResolvedEvent(long FrameId, string SkillId, int OwnerSeat,
    int ProviderSeat, CardKind UsedKind, int? GiftCardId) : IGameEvent;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome BeginDifferentActionCategoryGift(ProgramSkillFrame frame, int provider,
        string resultBind, IReadOnlyList<CardZoneKind> zones)
    {
        if (provider == frame.OwnerSeat || !_players[provider].IsAlive || frame.WindowContext?.CardUse is not { } use)
            throw new InvalidOperationException("A category gift requires a living other provider and the completed real action.");
        var excluded = GetProgramCardCategory(use.EffectiveKind);
        var choices = new List<PromptChoice>();
        foreach (var zone in zones)
        foreach (var card in _cardZones.CardsAt(new CardLocation(zone, provider))
                     .Where(card => GetProgramCardCategory(card.Kind) != excluded).OrderBy(card => card.Id))
            choices.Add(new PromptChoice(new ChoiceId($"action-category-gift.{frame.Id}.card-{card.Id}"),
                $"交给 {_players[frame.OwnerSeat].Name} 【{card.DisplayName}】（{GetSuitDisplayName(card.Suit)} {card.RankText}）", [card.Id], [],
                GiftChoiceParameters(frame, resultBind, "different-action-category-gift")));
        choices.Add(new PromptChoice(new ChoiceId($"action-category-gift.{frame.Id}.decline"), "不交牌", [], [],
            GiftChoiceParameters(frame, resultBind, "different-action-category-decline")));
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, provider,
            $"【{skill.Name}】请选择与【{CardCatalog.Get(use.EffectiveKind).DisplayName}】类型不同的一张牌交给 {_players[frame.OwnerSeat].Name}。",
            choices.SelectMany(choice => choice.Cards).ToArray(), [], SourceSeat: frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = provider,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name + " · 交牌", skill.Description), Choices = choices.ToArray() };
        _status = _players[provider].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private static IReadOnlyDictionary<string, string> GiftChoiceParameters(ProgramSkillFrame frame,
        string resultBind, string action) => new Dictionary<string, string>
        { ["program-action"] = action, ["frame-id"] = frame.Id.ToString(), ["result-bind"] = resultBind };

    private void ResolveDifferentActionCategoryGift(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The category gift lost its frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.ChooseDifferentActionCategoryGift ||
            choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString() ||
            choice.Parameters.GetValueOrDefault("result-bind") != effect.ResultBind ||
            frame.ChoiceBindings.Any(binding => binding.Name == effect.ResultBind) ||
            frame.SelectedTargetSeats.Count != 1 || frame.WindowContext?.CardUse is not { } use)
            throw new InvalidOperationException("The category gift does not match its suspended real action.");
        var provider = frame.SelectedTargetSeats.Single();
        if (provider == frame.OwnerSeat || !_players[provider].IsAlive || !_players[frame.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
            throw new InvalidOperationException("The gift participants or exact skill instance are no longer available.");
        Card? card = null; CardLocation? location = null;
        var action = choice.Parameters.GetValueOrDefault("program-action");
        if (action == "different-action-category-gift")
        {
            var id = choice.Cards.Single(); location = _cardZones.GetLocation(id);
            if (location.Value.OwnerSeat != provider || !effect.Zones.Contains(location.Value.Zone))
                throw new InvalidOperationException("The gift no longer belongs to the provider's allowed zone.");
            card = _cardZones.CardsAt(location.Value).Single(item => item.Id == id);
            if (GetProgramCardCategory(card.Kind) == GetProgramCardCategory(use.EffectiveKind))
                throw new InvalidOperationException("The gift matches the used card's category.");
        }
        else if (action != "different-action-category-decline" || choice.Cards.Count != 0)
            throw new InvalidOperationException("The category gift has an invalid branch.");
        ClearPendingDecision();
        ReplaceRuntimeTop(frame with
        { ChoiceBindings = frame.ChoiceBindings.Append(new ProgramChoiceResultBinding(effect.ResultBind!, card is null ? "declined" : "gifted", provider)).ToArray() });
        AdvanceEventRulesAndQueueFact(new ProgramActionCategoryGiftResolvedEvent(frame.Id, frame.SkillId, frame.OwnerSeat, provider, use.EffectiveKind, card?.Id));
        if (card is not null)
        {
            MoveCard(card, location!.Value, CardLocation.Hand(frame.OwnerSeat), new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
            if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.AwaitChild) return;
        }
        AdvanceRuntimeProgram(frame.Id);
    }

    private void AssertDifferentActionCategoryGift(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (paused.Op != SkillProgramEffectOp.ChooseDifferentActionCategoryGift ||
            frame.ChoiceBindings.Any(binding => binding.Name == paused.ResultBind) ||
            !ReferenceEquals(frame, _resolutionStack.LastOrDefault())) return;
        if (frame.SelectedTargetSeats.Count != 1 || frame.SelectedTargetSeats[0] == frame.OwnerSeat ||
            frame.WindowContext?.CardUse is null || _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt ||
            prompt.PlayerSeat != frame.SelectedTargetSeats[0] || prompt.Choices.Count == 0 ||
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") is not
                ("different-action-category-gift" or "different-action-category-decline") ||
                choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString() ||
                choice.Parameters.GetValueOrDefault("result-bind") != paused.ResultBind))
            throw new InvalidOperationException("The private category gift lost its source, entity prompt or participants.");
    }
}
