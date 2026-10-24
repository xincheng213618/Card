using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private IReadOnlyList<PromptChoice> _handResponseExtraChoices = [];

    private static bool SupportsHandResponse(DecisionKind? kind) => kind is
        DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying or
        DecisionKind.Nullification or DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard or DecisionKind.StoneAxe;

    private static bool IsHandResponsePrompt(GameSnapshot? snapshot)
    {
        if (snapshot?.PendingDecision is not { } prompt || prompt.PlayerSeat != snapshot.HumanSeat)
            return false;
        if (HasIncrementalCardSelectionChoices(prompt)) return false;
        if (SupportsHandResponse(prompt.Kind)) return true;
        if (IsCardPaymentPrompt(snapshot)) return true;
        if (prompt.SkillPrompt is null || prompt.Choices.Any(choice => choice.Targets.Count > 0 || choice.ContentIds.Count > 0))
            return false;
        var handIds = snapshot.Players.Single(player => player.Seat == snapshot.HumanSeat).Hand
            .Select(card => card.Id).ToHashSet();
        // Only unambiguous, published choices may be answered from the player's own hand.
        return prompt.Choices.Where(choice => choice.Cards.Count == 1 && handIds.Contains(choice.Cards[0]))
            .GroupBy(choice => choice.Cards[0]).Any(group => group.Count() == 1);
    }

    // These commands edit a native selection; a later choice commits its payment.
    private static bool HasIncrementalCardSelectionChoices(PendingDecision prompt) => prompt.Choices.Any(choice =>
        choice.Cards.Count > 0 && choice.Parameters.GetValueOrDefault("branch") is "pick" or "unpick");

    public bool IsHandResponsePending => IsHandResponsePrompt(_snapshot);

    private bool IsBaguaActivationPending => IsHandResponsePending && _snapshot.PendingDecision!.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("response") == "bagua-skip");
    private PromptChoice? BaguaActivationChoice => IsBaguaActivationPending
        ? _snapshot.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("response") is
            "bagua" or "faction-defense-bagua") : null;

    private IEnumerable<CardSnapshot> HandResponseMaterials => _snapshot.Players
        .Where(player => player.IsHuman)
        .SelectMany(player => player.Hand.Concat(player.WoodenOxGrain ?? []));

    private PromptChoice? HandResponseDeclineChoice
    {
        get
        {
            if (!IsHandResponsePending) return null;
            if (IsBaguaActivationPending) return _snapshot.PendingDecision!.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("response") == "bagua-skip");
            if (IsCardPaymentSelectionPending && CardPaymentDeclineChoice is { } paymentDecline) return paymentDecline;
            var choices = _snapshot.PendingDecision!.Choices.Where(choice => choice.Cards.Count == 0 &&
                (_snapshot.PendingDecision.Kind switch
                {
                    DecisionKind.RespondDodge => choice.Parameters.GetValueOrDefault("response") is "take-damage" or "faction-defense-decline",
                    DecisionKind.RespondSlash => choice.Parameters.GetValueOrDefault("response") is "take-damage" or "faction-slash-decline" or "borrowed-sword-give-weapon",
                    DecisionKind.RescueDying => choice.Parameters.GetValueOrDefault("response") == "let-die",
                    DecisionKind.Nullification => choice.Parameters.GetValueOrDefault("response") == "pass",
                    DecisionKind.FireAttackDiscard => choice.Parameters.GetValueOrDefault("response") == "fire-attack-skip",
                    DecisionKind.StoneAxe => choice.Parameters.GetValueOrDefault("action") == "stone-axe-skip",
                    _ => false
                })).Take(2).ToArray();
            return choices.Length == 1 ? choices[0] : null;
        }
    }

    // Keep the full published choices for command routing; the center shows only alternatives
    // that cannot be answered by selecting one hand card or using the bottom decline button.
    public IReadOnlyList<PromptChoice> HandResponseExtraChoices
    {
        get
        {
            if (!IsHandResponsePending) return _handResponseExtraChoices = [];
            if (IsBaguaActivationPending) return _handResponseExtraChoices = [];
            if (IsStoneAxeSelectionPending || IsOwnedCardSelectionPending) return _handResponseExtraChoices = [];
            var choices = _snapshot.PendingDecision!.Choices;
            if (IsCardPaymentSelectionPending)
            {
                var payments = CardPaymentChoices.Select(choice => choice.Id).ToHashSet();
                var declineId = HandResponseDeclineChoice?.Id;
                var variants = CardPaymentChoices.Where(IsPaymentVariant).ToArray();
                var extras = choices.Where(choice => !payments.Contains(choice.Id) && choice.Id != declineId ||
                    variants.Length > 1 && variants.Any(variant => ReferenceEquals(variant, choice))).ToArray();
                if (!_handResponseExtraChoices.SequenceEqual(extras, ReferenceEqualityComparer.Instance))
                    _handResponseExtraChoices = Array.AsReadOnly(extras);
                return _handResponseExtraChoices;
            }
            var handIds = HandResponseMaterials.Select(card => card.Id).ToHashSet();
            var handChoices = choices.Where(choice => choice.Cards.Count == 1 && handIds.Contains(choice.Cards[0]))
                .GroupBy(choice => choice.Cards[0]).Where(group => group.Count() == 1)
                .Select(group => group.Single().Id).ToHashSet();
            var decline = HandResponseDeclineChoice?.Id;
            var extraChoices = choices.Where(choice => choice.Id != decline && !handChoices.Contains(choice.Id)).ToArray();
            // Keep live response controls and their focus across hint updates;
            // a newly published choice object must still retire the old button.
            if (!_handResponseExtraChoices.SequenceEqual(extraChoices, ReferenceEqualityComparer.Instance))
                _handResponseExtraChoices = Array.AsReadOnly(extraChoices);
            return _handResponseExtraChoices;
        }
    }

    public bool CanCancelAction => HasSelection || OwnedCardDeclineChoice is not null || HandResponseDeclineChoice is not null || SkillTargetDeclineChoice is not null;
    public string CancelActionHint => OwnedCardDeclineChoice is not null ? "放弃本次选牌；Esc 仅取消当前选牌。" : HandResponseDeclineChoice is { } choice
        ? $"{choice.Description} · Esc 仅取消选牌。"
        : SkillTargetDeclineChoice is { } decline ? $"{decline.Description} · Esc 仅取消选择。"
        : "取消当前选牌和目标 · Esc";

    // A card is selectable only if it identifies one complete, currently published choice.
    // Ambiguous effects continue to use the explicit central candidates.
    private PromptChoice? HandResponseChoice(int? cardId)
    {
        if (!IsHandResponsePending || IsBaguaActivationPending || cardId is null ||
            !HandResponseMaterials.Any(card => card.Id == cardId)) return null;
        var choices = _snapshot.PendingDecision!.Choices
            .Where(choice => choice.Cards.Count == 1 && choice.Cards[0] == cardId).Take(2).ToArray();
        return choices.Length == 1 ? choices[0] : null;
    }

    private PromptChoice? SelectedHandResponse => IsCardPaymentSelectionPending ? SelectedCardPaymentChoice : HandResponseChoice(_selectedCardId);
    private string HandResponseButtonText => IsBaguaActivationPending ? "发动八卦" : IsStoneAxeSelectionPending ? "是" : IsCardPaymentSelectionPending ? "确 定" : _snapshot.PendingDecision?.Kind switch
    {
        DecisionKind.RespondDodge => IsSelectedResponseConversion(CardKind.Dodge) ? "当作闪打出" : "打出闪",
        DecisionKind.RespondSlash => IsSelectedResponseConversion(CardKind.Slash) ? "当作杀打出" : "打出杀",
        DecisionKind.RescueDying => "确认救援",
        DecisionKind.Nullification => "确认无懈",
        DecisionKind.FireAttackReveal => "展示此牌",
        DecisionKind.FireAttackDiscard => "弃牌火攻",
        DecisionKind.StoneAxe => "是",
        _ when _snapshot.PendingDecision?.SkillPrompt is not null => "确 定",
        _ => "确认响应"
    };

    private bool IsSelectedResponseConversion(CardKind required) => SelectedHandResponse is { Cards.Count: 1 } choice &&
        HandResponseMaterials.Single(card => card.Id == choice.Cards[0]).Kind is { } physical &&
        (required == CardKind.Slash ? physical is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash) : physical != required);

    private string HandResponseHint => IsBaguaActivationPending
        ? "是否发动【八卦阵】？红色判定视为打出【闪】；不发动或判定失败后，可以继续选择手牌出闪。"
        : IsStoneAxeSelectionPending
        ? $"贯石斧 · 选择两张手牌或装备（已选 {_paymentCardIds.Count}/2）· 点击「是」弃牌发动，点击「否」放弃；Esc 取消选牌。"
        : IsCardPaymentSelectionPending ? CardPaymentSelectionHint +
            (CardPaymentDeclineChoice is not null ? $"点击「{ActionDockCancelText}」放弃本次支付。" : string.Empty)
        : (SelectedHandResponse is { } choice
        ? $"{choice.Description} · 点击「{HandResponseButtonText}」或按 Enter 确认；Esc 取消选择。"
        : (Hand.Any(card => card.IsPlayable)
            ? $"选择一张亮起的手牌，再点击「{HandResponseButtonText}」。"
            : "当前没有可直接选择的手牌。") +
          (HandResponseExtraChoices.Count > 0 ? HasDockSkillChoices || HasExpandedSelection ? "其他响应方式在下方选择区。" : "其他技能、装备或响应方式在中央。" : string.Empty)) +
          (HandResponseDeclineChoice is not null ? $"点击「{ActionDockCancelText}」放弃本次响应。" : string.Empty);

    private string HandResponseUnavailableHint(int cardId) => IsBaguaActivationPending
        ? "请先选择是否发动【八卦阵】。"
        : _snapshot.PendingDecision!.Choices.Count(choice =>
        choice.Cards.Count == 1 && choice.Cards[0] == cardId) > 1
        ? "这张牌有多种响应方式，请在中央选择具体效果。"
        : "这张牌不能用于本次响应；请使用亮起的手牌或中央选项。";

    private void SelectHandResponse(CardViewModel card)
    {
        if (IsCardPaymentSelectionPending)
        {
            if (card.IsPlayable) TogglePaymentCard(card.Id);
            return;
        }
        if (!card.IsPlayable || HandResponseChoice(card.Id) is null) return;
        _selectedCardId = _selectedCardId == card.Id ? null : card.Id;
        _selectedTargetSeat = null;
        foreach (var item in Hand) item.IsSelected = item.Id == _selectedCardId;
        SelectedCardText = SelectedHandResponse?.Description ?? "未选择响应牌";
        RefreshSelectionHint();
    }

    private void ConfirmHandResponse()
    {
        if ((BaguaActivationChoice ?? SelectedHandResponse) is { } choice) SubmitHandResponse(choice);
    }

    private void CancelAction()
    {
        if (OwnedCardDeclineChoice is { } finish) SubmitPublishedPromptChoice(finish, IsOwnedCardSelectionPending, "选牌未执行");
        else if (HandResponseDeclineChoice is { } choice) SubmitHandResponse(choice);
        else if (SkillTargetDeclineChoice is { } decline) SelectSkillChoice(decline);
        else ClearSelection();
    }

    private void SubmitHandResponse(PromptChoice choice) =>
        SubmitPublishedPromptChoice(choice, IsHandResponsePending, "响应未执行");
}
