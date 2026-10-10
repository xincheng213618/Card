using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private static bool SupportsHandResponse(DecisionKind? kind) => kind is
        DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying or
        DecisionKind.Nullification or DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard;

    public bool IsHandResponsePending => SupportsHandResponse(_snapshot?.PendingDecision?.Kind);

    private PromptChoice? HandResponseDeclineChoice
    {
        get
        {
            if (!IsHandResponsePending) return null;
            var choices = _snapshot.PendingDecision!.Choices.Where(choice => choice.Cards.Count == 0 &&
                (_snapshot.PendingDecision.Kind switch
                {
                    DecisionKind.RespondDodge => choice.Parameters.GetValueOrDefault("response") is "take-damage" or "faction-defense-decline",
                    DecisionKind.RespondSlash => choice.Parameters.GetValueOrDefault("response") is "take-damage" or "faction-slash-decline" or "borrowed-sword-give-weapon",
                    DecisionKind.RescueDying => choice.Parameters.GetValueOrDefault("response") == "let-die",
                    DecisionKind.Nullification => choice.Parameters.GetValueOrDefault("response") == "pass",
                    DecisionKind.FireAttackDiscard => choice.Parameters.GetValueOrDefault("response") == "fire-attack-skip",
                    _ => false
                })).Take(2).ToArray();
            return choices.Length == 1 ? choices[0] : null;
        }
    }

    // Keep the full published choices for command routing; the center shows only alternatives
    // that cannot be answered by selecting one hand card or using the bottom Cancel button.
    public IReadOnlyList<PromptChoice> HandResponseExtraChoices
    {
        get
        {
            if (!IsHandResponsePending) return [];
            var choices = _snapshot.PendingDecision!.Choices;
            var handIds = _snapshot.Players.Single(player => player.IsHuman).Hand.Select(card => card.Id).ToHashSet();
            var handChoices = choices.Where(choice => choice.Cards.Count == 1 && handIds.Contains(choice.Cards[0]))
                .GroupBy(choice => choice.Cards[0]).Where(group => group.Count() == 1)
                .Select(group => group.Single().Id).ToHashSet();
            var decline = HandResponseDeclineChoice?.Id;
            return choices.Where(choice => choice.Id != decline && !handChoices.Contains(choice.Id)).ToArray();
        }
    }

    public bool CanCancelAction => HasSelection || HandResponseDeclineChoice is not null || SkillTargetDeclineChoice is not null;
    public string CancelActionHint => HandResponseDeclineChoice is { } choice
        ? $"{choice.Description} · Esc 仅取消选牌。"
        : SkillTargetDeclineChoice is { } decline ? $"{decline.Description} · Esc 仅取消选择。"
        : "取消当前选牌和目标 · Esc";

    // A card is selectable only if it identifies one complete, currently published choice.
    // Ambiguous effects and multi-card choices continue to use the explicit central candidates.
    private PromptChoice? HandResponseChoice(int? cardId)
    {
        if (!IsHandResponsePending || cardId is null ||
            !_snapshot.Players.Any(player => player.IsHuman && player.Hand.Any(card => card.Id == cardId))) return null;
        var choices = _snapshot.PendingDecision!.Choices
            .Where(choice => choice.Cards.Count == 1 && choice.Cards[0] == cardId).Take(2).ToArray();
        return choices.Length == 1 ? choices[0] : null;
    }

    private PromptChoice? SelectedHandResponse => HandResponseChoice(_selectedCardId);
    private string HandResponseButtonText => _snapshot.PendingDecision?.Kind switch
    {
        DecisionKind.RespondDodge => IsSelectedResponseConversion(CardKind.Dodge) ? "当作闪打出" : "打出闪",
        DecisionKind.RespondSlash => IsSelectedResponseConversion(CardKind.Slash) ? "当作杀打出" : "打出杀",
        DecisionKind.RescueDying => "确认救援",
        DecisionKind.Nullification => "确认无懈",
        DecisionKind.FireAttackReveal => "展示此牌",
        DecisionKind.FireAttackDiscard => "弃牌火攻",
        _ => "确认响应"
    };

    private bool IsSelectedResponseConversion(CardKind required) => SelectedHandResponse is { Cards.Count: 1 } choice &&
        _snapshot.Players.Single(player => player.IsHuman).Hand.Single(card => card.Id == choice.Cards[0]).Kind is { } physical &&
        (required == CardKind.Slash ? physical is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) : physical != required);

    private string HandResponseHint => SelectedHandResponse is { } choice
        ? $"{choice.Description} · 点击「{HandResponseButtonText}」或按 Enter 确认；Esc 取消选择。"
        : (Hand.Any(card => card.IsPlayable)
            ? $"选择一张亮起的手牌，再点击「{HandResponseButtonText}」。"
            : "当前没有可直接选择的手牌。") +
          (HandResponseExtraChoices.Count > 0 ? "其他技能、装备或响应方式在中央。" : string.Empty) +
          (HandResponseDeclineChoice is not null ? "点击「取消」放弃本次响应。" : string.Empty);

    private string HandResponseUnavailableHint(int cardId) => _snapshot.PendingDecision!.Choices.Count(choice =>
        choice.Cards.Count == 1 && choice.Cards[0] == cardId) > 1
        ? "这张牌有多种响应方式，请在中央选择具体效果。"
        : "这张牌不能用于本次响应；请使用亮起的手牌或中央选项。";

    private void SelectHandResponse(CardViewModel card)
    {
        if (!card.IsPlayable || HandResponseChoice(card.Id) is null) return;
        _selectedCardId = _selectedCardId == card.Id ? null : card.Id;
        _selectedTargetSeat = null;
        foreach (var item in Hand) item.IsSelected = item.Id == _selectedCardId;
        SelectedCardText = SelectedHandResponse?.Description ?? "未选择响应牌";
        RefreshSelectionHint();
    }

    private void ConfirmHandResponse()
    {
        if (SelectedHandResponse is { } choice) SubmitHandResponse(choice);
    }

    private void CancelAction()
    {
        if (HandResponseDeclineChoice is { } choice) SubmitHandResponse(choice);
        else if (SkillTargetDeclineChoice is { } decline) SelectSkillChoice(decline);
        else ClearSelection();
    }

    private void SubmitHandResponse(PromptChoice choice)
    {
        if (!IsHandResponsePending || _snapshot.PendingDecision is not { } prompt) return;
        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new AnswerPromptCommand(_snapshot.HumanSeat, prompt.PromptId, choice.Id, _snapshot.Revision));
            if (!result.Accepted) { PromptText = $"响应未执行：{result.Error?.Message}"; return; }
            RefreshCommandResult(result.State);
        });
    }
}
