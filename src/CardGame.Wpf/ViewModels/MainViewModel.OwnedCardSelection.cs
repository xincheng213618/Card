using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private readonly List<int> _selectedOwnedCardIds = [];
    private GameEngine? _ownedSelectionGame;
    private PromptId? _ownedSelectionPromptId;
    private bool _confirmingOwnedCardSelection;

    public bool IsOwnedCardSelectionPending
    {
        get
        {
            if (_snapshot?.PendingDecision is not { SkillPrompt: not null, RequiredCardCount: > 0, Choices.Count: > 0 } prompt ||
                prompt.PlayerSeat != _snapshot.HumanSeat) return false;
            var human = _snapshot.Players.Single(player => player.Seat == _snapshot.HumanSeat);
            var owned = human.Hand.Concat(human.Equipment).Select(card => card.Id).ToHashSet();
            var cards = prompt.Choices.Where(choice => choice.Cards.Count > 0).ToArray();
            var action = cards.FirstOrDefault()?.Parameters.GetValueOrDefault("program-action");
            if (action is null) return false;
            return cards.Length > 0 && prompt.Choices.All(choice => choice.Targets.Count == 0 && choice.ContentIds.Count == 0 &&
                (choice.Parameters.GetValueOrDefault("program-action") == action &&
                    choice.Cards.Count == 1 && owned.Contains(choice.Cards[0]) ||
                 choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards" && choice.Cards.Count == 0 ||
                 IsPaymentDecline(choice))) &&
                cards.Select(choice => choice.Cards[0]).Distinct().Count() == cards.Length;
        }
    }

    private bool IsOwnedSelectionMaterial(int? id) => IsOwnedCardSelectionPending && id is { } cardId &&
        _snapshot.PendingDecision!.Choices.Any(choice => choice.Cards.Count == 1 && choice.Cards[0] == cardId);

    private bool CanConfirmOwnedCardSelection => !_confirmingOwnedCardSelection && IsOwnedCardSelectionPending &&
        _selectedOwnedCardIds.Count >= (_snapshot.PendingDecision!.MinimumCardCount ?? _snapshot.PendingDecision.RequiredCardCount) &&
        _selectedOwnedCardIds.Count <= _snapshot.PendingDecision.RequiredCardCount;

    private PromptChoice? OwnedCardDeclineChoice => IsOwnedCardSelectionPending
        ? _snapshot.PendingDecision!.Choices.SingleOrDefault(choice => IsPaymentDecline(choice) ||
            choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards" && choice.Parameters.GetValueOrDefault("selection-index") == "0")
        : null;

    private void SyncOwnedCardSelection()
    {
        var promptId = IsOwnedCardSelectionPending ? _snapshot.PendingDecision!.PromptId : (PromptId?)null;
        if (!ReferenceEquals(_ownedSelectionGame, _game) || _ownedSelectionPromptId != promptId)
            _selectedOwnedCardIds.Clear();
        _ownedSelectionGame = _game;
        _ownedSelectionPromptId = promptId;
    }

    private void ToggleOwnedSelectionCard(int id)
    {
        if (!IsOwnedSelectionMaterial(id)) return;
        if (!_selectedOwnedCardIds.Remove(id))
        {
            if (_selectedOwnedCardIds.Count >= _snapshot.PendingDecision!.RequiredCardCount) return;
            _selectedOwnedCardIds.Add(id);
        }
        foreach (var card in Hand) card.IsSelected = _selectedOwnedCardIds.Contains(card.Id);
        SelectedCardText = $"已选择 {_selectedOwnedCardIds.Count}/{_snapshot.PendingDecision!.RequiredCardCount} 张牌";
        RefreshEquipmentMaterialSelection();
        RefreshSelectionHint();
    }

    private string OwnedCardSelectionHint => $"{_snapshot.PendingDecision!.SkillPrompt!.Name} · 选择 " +
        (_snapshot.PendingDecision.MinimumCardCount is { } min && min != _snapshot.PendingDecision.RequiredCardCount
            ? $"{min} 至 {_snapshot.PendingDecision.RequiredCardCount}" : $"{_snapshot.PendingDecision.RequiredCardCount}") + " 张手牌或装备" +
        $"（已选 {_selectedOwnedCardIds.Count}/{_snapshot.PendingDecision.RequiredCardCount}）· 选齐后点击「确定」；Esc 取消选择。";

    private ICommand? _selectOwnedCardEquipmentCommand;
    public ICommand SelectOwnedCardEquipmentCommand => _selectOwnedCardEquipmentCommand ??=
        new RelayCommand<EquipmentSlotViewModel>(slot =>
        {
            if (HumanEquipmentSlots.Any(current => ReferenceEquals(current, slot)) && IsOwnedSelectionMaterial(slot.Card?.Id))
                ToggleOwnedSelectionCard(slot.Card!.Id);
        }, slot => HumanEquipmentSlots.Any(current => ReferenceEquals(current, slot)) && IsOwnedSelectionMaterial(slot.Card?.Id));

    public ICommand SelectEquipmentMaterialCommand => IsOwnedCardSelectionPending
        ? SelectOwnedCardEquipmentCommand : IsCardPaymentSelectionPending ? SelectPaymentEquipmentCommand : SelectActiveEquipmentCommand;

    private void ConfirmOwnedCardSelection()
    {
        if (!CanConfirmOwnedCardSelection) return;
        var initial = _snapshot.PendingDecision!;
        var source = initial.Choices.First(choice => choice.Cards.Count == 1).Parameters;
        var action = source.GetValueOrDefault("program-action");
        var ids = _selectedOwnedCardIds.ToArray();
        ExecuteSafely(() =>
        {
            _confirmingOwnedCardSelection = true;
            try
            {
                // Requery each committed player view. Never carry a previous
                // prompt's choice, revision or private runtime state forward.
                for (var index = 0; index < ids.Length; index++)
                {
                    var view = _game.CreateSnapshot(_snapshot.HumanSeat);
                    var prompt = view.PendingDecision;
                    if (prompt?.PlayerSeat != initial.PlayerSeat || prompt.SkillPrompt?.SkillId != initial.SkillPrompt!.SkillId ||
                        prompt.RequiredCardCount != initial.RequiredCardCount - index) return;
                    var choice = prompt.Choices.SingleOrDefault(choice => choice.Cards.SequenceEqual([ids[index]]) &&
                        choice.Parameters.GetValueOrDefault("program-action") == action &&
                        choice.Parameters.GetValueOrDefault("frame-id") == source.GetValueOrDefault("frame-id") &&
                        choice.Parameters.GetValueOrDefault("result-bind") == source.GetValueOrDefault("result-bind"));
                    if (choice is null || !SubmitCommand(new AnswerPromptCommand(view.HumanSeat,
                            prompt.PromptId, choice.Id, view.Revision)).Accepted) return;
                }
                // A variable-size set completes through the current published Finish;
                // an exact/maximal set may already have advanced to the next effect.
                var current = _game.CreateSnapshot(_snapshot.HumanSeat);
                var finish = current.PendingDecision?.Choices.SingleOrDefault(choice =>
                    choice.Cards.Count == 0 &&
                    (choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards" ||
                     choice.Parameters.GetValueOrDefault("program-action") == action && choice.Parameters.GetValueOrDefault("prep-discard-step") == "finish") &&
                    choice.Parameters.GetValueOrDefault("frame-id") == source.GetValueOrDefault("frame-id") &&
                    choice.Parameters.GetValueOrDefault("result-bind") == source.GetValueOrDefault("result-bind"));
                if (finish is not null && current.PendingDecision!.PlayerSeat == initial.PlayerSeat)
                    SubmitCommand(new AnswerPromptCommand(current.HumanSeat, current.PendingDecision.PromptId, finish.Id, current.Revision));
            }
            finally
            {
                _confirmingOwnedCardSelection = false;
                _selectedOwnedCardIds.Clear();
                RefreshCurrentView();
            }
        });
    }
}
