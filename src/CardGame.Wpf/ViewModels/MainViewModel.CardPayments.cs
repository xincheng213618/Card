using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private readonly HashSet<int> _paymentCardIds = [];
    private ChoiceId? _paymentChoiceId;
    private GameSnapshot? _paymentSnapshot;
    private bool _hasCardPaymentPrompt;
    private IReadOnlyList<PromptChoice> _cardPaymentChoices = [];
    private HashSet<int> _paymentMaterialIds = [];

    // A published card set is the authority for costs and their restrictions.
    // Never infer payment cards from labels, or expose another player's cards.
    private static bool IsCardPaymentPrompt(GameSnapshot? snapshot)
    {
        if (snapshot?.PendingDecision is not { } prompt || prompt.PlayerSeat != snapshot.HumanSeat ||
            prompt.RequiredCardCount > 0 ||
            HasIncrementalCardSelectionChoices(prompt) ||
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") is "select-owned-cards" or "finish-owned-cards"))
            return false;
        if (prompt.SkillPrompt is null && prompt.Kind is not (DecisionKind.StoneAxe or DecisionKind.CixiongDoubleSwords or DecisionKind.QinglongCrescentBlade) &&
            !prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") is "faction-request-cost" or "friend-duel-discard")) return false;
        var human = snapshot.Players.Single(player => player.Seat == snapshot.HumanSeat);
        var owned = human.Hand.Concat(human.Equipment).Select(card => card.Id).ToHashSet();
        var cards = prompt.Choices.Where(choice => choice.Cards.Count > 0).ToArray();
        if (cards.Length == 0)
            return prompt.Choices.Count == 1 && prompt.Choices[0] is { Cards.Count: 0, Targets.Count: 0, ContentIds.Count: 0 } decline &&
                decline.Parameters.GetValueOrDefault("program-action") == "different-action-category-decline";
        return cards.All(choice => choice.ContentIds.Count == 0 &&
                choice.Cards.Distinct().Count() == choice.Cards.Count && choice.Cards.All(owned.Contains) &&
                choice.Targets.SequenceEqual(cards[0].Targets));
    }

    public bool IsCardPaymentSelectionPending
    {
        get
        {
            if (!ReferenceEquals(_paymentSnapshot, _snapshot))
            {
                _paymentSnapshot = _snapshot;
                _hasCardPaymentPrompt = IsCardPaymentPrompt(_snapshot);
                _cardPaymentChoices = _hasCardPaymentPrompt
                    ? Array.AsReadOnly(_snapshot.PendingDecision!.Choices.Where(choice => choice.Cards.Count > 0).ToArray()) : [];
                _paymentMaterialIds = _cardPaymentChoices.SelectMany(choice => choice.Cards).ToHashSet();
            }
            return _hasCardPaymentPrompt;
        }
    }
    private IReadOnlyList<PromptChoice> CardPaymentChoices => IsCardPaymentSelectionPending ? _cardPaymentChoices : [];
    private bool IsCardPaymentMaterial(int? id) => IsCardPaymentSelectionPending && id is { } cardId && _paymentMaterialIds.Contains(cardId);
    private PromptChoice? SelectedCardPaymentChoice
    {
        get
        {
            var matches = CardPaymentChoices.Where(choice => _paymentCardIds.SetEquals(choice.Cards)).ToArray();
            return matches.Length == 1 ? matches[0] : matches.SingleOrDefault(choice => choice.Id == _paymentChoiceId);
        }
    }

    private bool IsPaymentVariant(PromptChoice choice) => IsCardPaymentSelectionPending &&
        choice.Cards.Count > 0 && _paymentCardIds.SetEquals(choice.Cards);

    private void SelectResponseAlternative(PromptChoice choice)
    {
        if (!HandResponseExtraChoices.Any(current => ReferenceEquals(current, choice))) return;
        if (IsPaymentVariant(choice))
        {
            _paymentChoiceId = choice.Id;
            SelectedCardText = choice.Description;
            RefreshSelectionHint();
        }
        else SubmitHandResponse(choice);
    }

    private PromptChoice? CardPaymentDeclineChoice
    {
        get
        {
            if (!IsCardPaymentSelectionPending) return null;
            var choices = _snapshot.PendingDecision!.Choices.Where(IsPaymentDecline).Take(2).ToArray();
            return choices.Length == 1 ? choices[0] : null;
        }
    }

    private static bool IsPaymentDecline(PromptChoice choice) => choice.Cards.Count == 0 &&
        choice.Targets.Count == 0 && choice.ContentIds.Count == 0 && choice.Parameters.Any(parameter =>
            parameter.Key is "program-action" or "action" or "option" or "branch" or "cost-side" or "payment-kind" &&
            (parameter.Value is "skip" or "decline" or "cancel" or "different-action-category-decline" or "slash-suit-discard" ||
             parameter.Value.EndsWith("-skip", StringComparison.Ordinal) || parameter.Value.StartsWith("decline-", StringComparison.Ordinal)));

    private void TogglePaymentCard(int id)
    {
        if (!IsCardPaymentMaterial(id)) return;
        if (!_paymentCardIds.Remove(id))
        {
            if (!CardPaymentChoices.Any(choice => choice.Cards.Contains(id) && _paymentCardIds.All(choice.Cards.Contains))) return;
            _paymentCardIds.Add(id);
        }
        foreach (var card in Hand) card.IsSelected = _paymentCardIds.Contains(card.Id);
        _paymentChoiceId = null;
        SelectedCardText = $"已选择 {_paymentCardIds.Count} 张牌";
        RefreshEquipmentMaterialSelection();
        RefreshSelectionHint();
    }

    private string CardPaymentSelectionHint
    {
        get
        {
            var counts = CardPaymentChoices.Select(choice => choice.Cards.Count).Distinct().Order().ToArray();
            if (counts.Length == 0) return "当前没有可交出的合法牌。";
            var required = string.Join(" / ", counts);
            return SelectedCardPaymentChoice is { } choice
                ? $"{choice.Description} · 点击「确定」确认；Esc 取消选牌。"
                : CardPaymentChoices.Count(choice => _paymentCardIds.SetEquals(choice.Cards)) > 1
                    ? "所选牌有多种支付效果，请先选择中央的具体方式，再点击「确定」。"
                : $"选择 {required} 张合法手牌或装备（已选 {_paymentCardIds.Count} 张）· 选齐后点击「确定」；Esc 取消选牌。";
        }
    }

    private ICommand? _selectPaymentEquipmentCommand;
    public ICommand SelectPaymentEquipmentCommand => _selectPaymentEquipmentCommand ??=
        new RelayCommand<EquipmentSlotViewModel>(slot =>
        {
            if (HumanEquipmentSlots.Any(current => ReferenceEquals(current, slot)) && IsCardPaymentMaterial(slot.Card?.Id))
                TogglePaymentCard(slot.Card!.Id);
        }, slot => HumanEquipmentSlots.Any(current => ReferenceEquals(current, slot)) && IsCardPaymentMaterial(slot.Card?.Id));

    private void RefreshEquipmentMaterialSelection()
    {
        _equipmentPlayer = null;
        RaisePropertyChanged(nameof(HumanEquipmentSlots));
        (_selectPaymentEquipmentCommand as RelayCommand<EquipmentSlotViewModel>)?.NotifyCanExecuteChanged();
        (_selectOwnedCardEquipmentCommand as RelayCommand<EquipmentSlotViewModel>)?.NotifyCanExecuteChanged();
        (_selectActiveEquipmentCommand as RelayCommand<EquipmentSlotViewModel>)?.NotifyCanExecuteChanged();
    }
}
