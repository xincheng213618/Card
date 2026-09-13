using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private static bool SupportsHandResponse(DecisionKind? kind) => kind is
        DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying or
        DecisionKind.Nullification or DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard;

    public bool IsHandResponsePending => SupportsHandResponse(_snapshot?.PendingDecision?.Kind);

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
        : Hand.Any(card => card.IsPlayable) ? "选择一张亮起的手牌，再确认响应；技能、装备和放弃选项在中央。"
        : "当前没有可直接选择的手牌，请在中央选择技能、装备或其他响应。";

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
        if (SelectedHandResponse is not { } choice || _snapshot.PendingDecision is not { } prompt) return;
        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new AnswerPromptCommand(_snapshot.HumanSeat, prompt.PromptId, choice.Id, _snapshot.Revision));
            if (!result.Accepted) { PromptText = $"响应未执行：{result.Error?.Message}"; return; }
            Refresh(result.State);
        });
    }
}
