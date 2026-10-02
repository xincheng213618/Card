namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome BeginProgramTopReorder(ProgramSkillFrame frame, int maximumCards,
        SkillProgramNumberExpression? numberExpression, int? exactTopCount = null)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.TopReorder is not null || _pendingDecision is not null ||
            active.OwnerSeat != _currentSeat || !_players[active.OwnerSeat].IsAlive)
            throw new InvalidOperationException("Top ordering requires one current owner and clean prompt.");
        if (numberExpression is not null and not SkillProgramNumberExpression.LivingPlayerCount)
            throw new InvalidOperationException("Unsupported top-ordering count expression.");
        _ = EnsureDrawPile();
        var count = Math.Min(maximumCards, _cardZones.Count(CardLocation.DrawPile));
        if (numberExpression == SkillProgramNumberExpression.LivingPlayerCount)
            count = Math.Min(count, _players.Count(player => player.IsAlive));
        if (count == 0) return SkillProgramStepOutcome.Continue;
        var viewed = _cardZones.CardsAt(CardLocation.DrawPile).TakeLast(count).Reverse()
            .Select(card => card.Id).ToArray();
        active = active with { TopReorder = new ProgramTopReorder(viewed, [], [], false)
        { RequiredTopCount = exactTopCount is { } exact ? Math.Min(exact, count) : null } };
        ReplaceRuntimeTop(active);
        PublishProgramTopReorderPrompt(active);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishProgramTopReorderPrompt(ProgramSkillFrame frame)
    {
        var order = frame.TopReorder ?? throw new InvalidOperationException("Top ordering lost its state.");
        if (order.RequiredTopCount is not null) ValidateExactTopReorder(frame);
        var used = order.TopCardIds.Concat(order.BottomCardIds).ToHashSet();
        var choices = order.ViewedCardIds.Where(id => !used.Contains(id)).Select(id =>
        {
            var card = _cardZones.CardsAt(CardLocation.DrawPile).Single(item => item.Id == id);
            var action = order.ChoosingBottom ? "bottom" : "top";
            var label = order.RequiredTopCount is null
                ? $"将【{card.DisplayName}】置于牌堆{(order.ChoosingBottom ? "底" : "顶")}。"
                : $"将观看牌 {Array.IndexOf(order.ViewedCardIds.ToArray(), id) + 1}【{card.DisplayName}】{GetSuitDisplayName(card.Suit)}{card.RankText}置于牌堆{(order.ChoosingBottom ? "底" : "顶")}。";
            return new PromptChoice(new ChoiceId($"program-top-order.{frame.Id}.{action}.{id}"),
                label,
                [id], [], new Dictionary<string, string> { ["action"] = action });
        }).ToList();
        if (!order.ChoosingBottom && order.RequiredTopCount is null)
            choices.Add(new PromptChoice(new ChoiceId($"program-top-order.{frame.Id}.finish-top"),
                "结束牌堆顶排序，开始安排剩余牌到牌堆底。", [], [],
                new Dictionary<string, string> { ["action"] = "finish-top" }));
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTopReorder, frame.OwnerSeat,
            "依次排列牌堆顶和牌堆底。", choices.SelectMany(choice => choice.Cards).ToArray(), [])
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices.AsReadOnly(), TargetSeat = frame.OwnerSeat };
        if (order.RequiredTopCount is { } required)
        {
            var skill = _contentRegistry.GetSkill(frame.SkillId);
            var stage = order.ChoosingBottom ? "牌堆底排序" : $"牌堆顶排序 {order.TopCardIds.Count}/{required}";
            _pendingDecision = _pendingDecision with
            {
                Prompt = order.ChoosingBottom ? "依次选择最靠牌堆底的牌。" : $"依次选择牌堆顶的牌（首张下次先摸），需选 {required} 张。",
                SkillPrompt = new(frame.SkillId, skill.Name, $"{skill.Name} · {stage}",
                    order.ChoosingBottom ? "首先选择的牌最靠牌堆底。" : "首先选择的牌下次先摸。")
            };
        }
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private CommandResult SubmitProgramTopReorderAnswer(PromptChoice choice) => Accept(() =>
    {
        ResolveProgramTopReorderChoice(choice);
        AdvanceRulesAndPublishState();
        if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
    });

    private void ResolveProgramTopReorderChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("Top ordering lost its active program frame.");
        var state = frame.TopReorder ?? throw new InvalidOperationException("Top ordering lost its cards.");
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTopReorder, PlayerSeat: var seat } decision ||
            seat != frame.OwnerSeat || !decision.Choices.Any(item => item.Id == choice.Id))
            throw new InvalidOperationException("Top ordering choice is not current.");
        if (state.RequiredTopCount is not null) ValidateExactTopReorder(frame);
        var used = state.TopCardIds.Concat(state.BottomCardIds).ToHashSet();
        if (state.ViewedCardIds.Any(id => !_cardZones.CardsAt(CardLocation.DrawPile).Any(card => card.Id == id)))
            throw new InvalidOperationException("Top ordering cards changed before commitment.");
        var action = choice.Parameters.GetValueOrDefault("action");
        if (action == "finish-top" && state.RequiredTopCount is null && !state.ChoosingBottom && choice.Cards.Count == 0)
            state = state with { ChoosingBottom = true };
        else if (choice.Cards is [var id] && state.ViewedCardIds.Contains(id) && !used.Contains(id))
            state = action switch
            {
                "top" when !state.ChoosingBottom => state with { TopCardIds = [..state.TopCardIds, id] },
                "bottom" when state.ChoosingBottom => state with { BottomCardIds = [..state.BottomCardIds, id] },
                _ => throw new InvalidOperationException("Top ordering stage does not match the choice.")
            };
        else throw new InvalidOperationException("Top ordering choice has no current card.");
        if (state.RequiredTopCount is { } required && state.TopCardIds.Count == required)
            state = state with { ChoosingBottom = true };
        ClearPendingDecision();
        frame = frame with { TopReorder = state };
        ReplaceRuntimeTop(frame);
        if (state.TopCardIds.Count + state.BottomCardIds.Count == state.ViewedCardIds.Count)
        {
            _cardZones.ReorderDrawPileTop(state.ViewedCardIds, state.TopCardIds, state.BottomCardIds);
            ReplaceRuntimeTop(frame with { TopReorder = null });
            AdvanceRuntimeProgram(frame.Id);
        }
        else PublishProgramTopReorderPrompt(frame);
    }

    private bool IsAiProgramTopReorderPending() => _pendingDecision is
        { Kind: DecisionKind.ProgramTopReorder, PlayerSeat: var seat } && !_players[seat].IsHuman;

    private void ResolvePendingAiProgramTopReorder()
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("AI top ordering has no program frame.");
        var state = frame.TopReorder ?? throw new InvalidOperationException("AI top ordering has no cards.");
        var decision = _pendingDecision ?? throw new InvalidOperationException("AI top ordering has no prompt.");
        var owner = _players[frame.OwnerSeat];
        var delayed = GetJudgment(owner).Count(card => IsDelayedCard(GetJudgmentEffectiveCardKind(card)));
        var desiredTop = state.RequiredTopCount ?? Math.Min(state.ViewedCardIds.Count, delayed + Math.Max(0, GetTurnDrawCount(owner)));
        var selected = !state.ChoosingBottom && state.TopCardIds.Count >= desiredTop
            ? decision.Choices.Single(choice => choice.Parameters.GetValueOrDefault("action") == "finish-top")
            : decision.Choices.First(choice => choice.Cards.Count == 1);
        ResolveProgramTopReorderChoice(selected);
        AdvanceRulesAndPublishState();
    }
}
