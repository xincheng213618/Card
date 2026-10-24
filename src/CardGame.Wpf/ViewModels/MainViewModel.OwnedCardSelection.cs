using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private readonly List<int> _selectedOwnedCardIds = [];
    private GameEngine? _ownedSelectionGame;
    private PromptId? _ownedSelectionPromptId;
    private (string? Frame, string? Stage)? _ownedSelectionScope;
    private bool _confirmingOwnedCardSelection;

    public bool IsForeignCardTakeSelectionPending => _snapshot?.PendingDecision is { SkillPrompt: not null, RequiredCardCount: > 1 } prompt &&
        prompt.PlayerSeat == _snapshot.HumanSeat && prompt.Choices.Count > 0 && prompt.Choices.All(choice =>
            choice.Cards.Count == 1 && choice.Parameters.GetValueOrDefault("program-action") == "private-hand-take" &&
            (_snapshot.PrivateRevealedCards ?? []).Any(card => card.Id == choice.Cards[0]));
    public IReadOnlyList<int> SelectedTargetCardIds => _selectedOwnedCardIds.AsReadOnly();

    public bool IsOwnedCardSelectionPending
    {
        get
        {
            if (IsCardDistributionPending) return false;
            if (IsForeignCardTakeSelectionPending) return true;
            if (_snapshot?.PendingDecision is not { SkillPrompt: not null, Choices.Count: > 0 } prompt ||
                prompt.PlayerSeat != _snapshot.HumanSeat) return false;
            var human = _snapshot.Players.Single(player => player.Seat == _snapshot.HumanSeat);
            var owned = human.Hand.Concat(human.Equipment).Select(card => card.Id).ToHashSet();
            if (HasProjectedOwnedSelection(prompt))
            {
                var picks = prompt.Choices.Where(IsProjectedOwnedPick).ToArray();
                return prompt.RequiredCardCount >= 0 &&
                    (picks.Length > 0 || prompt.Choices.Any(choice => IsOwnedSelectionFinish(choice) ||
                        choice.Parameters.GetValueOrDefault("owned-selection-role") == "unpick")) &&
                    picks.All(choice => choice.Cards.Count == 1 && owned.Contains(choice.Cards[0])) &&
                    picks.Select(choice => choice.Cards[0]).Distinct().Count() == picks.Length;
            }
            var cards = prompt.Choices.Where(choice => choice.Cards.Count > 0).ToArray();
            var action = (cards.FirstOrDefault() ?? prompt.Choices.FirstOrDefault(IsOwnedSelectionFinish))?
                .Parameters.GetValueOrDefault("program-action");
            if (action is null) return false;
            return (prompt.RequiredCardCount > 0 || prompt.MinimumCardCount == 0 && prompt.Choices.Any(IsOwnedSelectionFinish)) &&
                prompt.Choices.All(choice => choice.Targets.Count == 0 && choice.ContentIds.Count == 0 &&
                (choice.Parameters.GetValueOrDefault("program-action") == action &&
                    choice.Cards.Count == 1 && owned.Contains(choice.Cards[0]) ||
                 IsOwnedSelectionFinish(choice) ||
                 IsPaymentDecline(choice))) &&
                cards.Select(choice => choice.Cards[0]).Distinct().Count() == cards.Length;
        }
    }

    private static bool HasProjectedOwnedSelection(PendingDecision prompt) => prompt.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("owned-selection-role") is "pick" or "unpick" or "finish" or "reset");

    private static bool IsProjectedOwnedPick(PromptChoice choice) =>
        choice.Parameters.GetValueOrDefault("owned-selection-role") == "pick";

    private static bool IsOwnedSelectionFinish(PromptChoice choice) =>
        choice.Parameters.GetValueOrDefault("owned-selection-role") == "finish" ||
        choice.Cards.Count == 0 && choice.Targets.Count == 0 && choice.ContentIds.Count == 0 &&
        (choice.Parameters.GetValueOrDefault("program-action") switch
        {
            "finish-owned-cards" => true,
            "prep-discard-ending" => choice.Parameters.GetValueOrDefault("prep-discard-step") == "finish",
            "public-pile-preparation" => choice.Parameters.GetValueOrDefault("pile-action") == "finish",
            "game-domain" => choice.Parameters.GetValueOrDefault("domain-action") == "store-finish",
            _ => false
        });

    private bool IsOwnedSelectionMaterial(int? id) => IsOwnedCardSelectionPending && id is { } cardId &&
        _snapshot.PendingDecision!.Choices.Any(choice => choice.Cards.Count == 1 && choice.Cards[0] == cardId &&
            (!HasProjectedOwnedSelection(_snapshot.PendingDecision) ||
             choice.Parameters.GetValueOrDefault("owned-selection-role") is "pick" or "unpick"));

    private static IEnumerable<int> ProjectedOwnedSelectionIds(PendingDecision? prompt) => prompt?.Choices
        .Select(choice => choice.Parameters.GetValueOrDefault("owned-selection-selected"))
        .FirstOrDefault(value => value is not null)?.Split(',')
        .Select(value => int.TryParse(value, out var id) ? (int?)id : null).OfType<int>() ?? [];
    private IEnumerable<int> NativeOwnedSelectionIds => ProjectedOwnedSelectionIds(_snapshot.PendingDecision);
    private bool HasOwnedCardDraftSelection => _selectedOwnedCardIds.Count > 0 || IsOwnedCardSelectionPending &&
        (NativeOwnedSelectionIds.Any() || _snapshot.PendingDecision!.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("owned-selection-role") == "unpick" && choice.Cards.Count == 1));

    // A cold load may contain unpaid native picks as well as the local draft.
    // Clear only through this same collector's currently published undo command.
    private bool TryClearNativeOwnedSelection()
    {
        if (!IsOwnedCardSelectionPending || !HasProjectedOwnedSelection(_snapshot.PendingDecision!) ||
            !NativeOwnedSelectionIds.Any() || !_snapshot.PendingDecision!.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("owned-selection-role") is "reset" or "unpick")) return false;
        var initial = _snapshot.PendingDecision;
        var anchor = initial.Choices.First(choice => choice.Parameters.ContainsKey("owned-selection-role"));
        var frame = anchor.Parameters.GetValueOrDefault("owned-selection-frame");
        var stage = anchor.Parameters.GetValueOrDefault("owned-selection-stage");
        var maximum = NativeOwnedSelectionIds.Distinct().Count() + 1;
        ExecuteSafely(() =>
        {
            _selectedOwnedCardIds.Clear();
            for (var attempt = 0; attempt < maximum; attempt++)
            {
                var view = _game.CreateSnapshot(initial.PlayerSeat);
                var prompt = view.PendingDecision;
                if (prompt?.PlayerSeat != initial.PlayerSeat || prompt.SkillPrompt?.SkillId != initial.SkillPrompt!.SkillId ||
                    !ProjectedOwnedSelectionIds(prompt).Any()) break;
                var current = prompt.Choices.Where(choice =>
                    choice.Parameters.GetValueOrDefault("owned-selection-frame") == frame &&
                    choice.Parameters.GetValueOrDefault("owned-selection-stage") == stage).ToArray();
                var undo = current.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("owned-selection-role") == "reset") ??
                    current.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("owned-selection-role") == "unpick" && choice.Cards.Count == 1);
                if (undo is null) break;
                var result = SubmitCommand(new AnswerPromptCommand(view.HumanSeat, prompt.PromptId, undo.Id, view.Revision));
                if (!result.Accepted)
                { PromptText = $"清空选牌未执行：{result.Error?.Message}"; break; }
            }
            RefreshCurrentView();
        });
        return true;
    }

    private bool IsOwnedSelectionCardSelected(int id) => _selectedOwnedCardIds.Contains(id) ||
        IsOwnedCardSelectionPending && _snapshot.PendingDecision!.Choices.Any(choice =>
            choice.Cards.SequenceEqual([id]) && choice.Parameters.GetValueOrDefault("owned-selection-role") == "unpick") ||
        IsOwnedCardSelectionPending && NativeOwnedSelectionIds.Contains(id);

    private IReadOnlyList<PromptChoice> OwnedSelectionExtraChoices => IsOwnedCardSelectionPending &&
        HasProjectedOwnedSelection(_snapshot.PendingDecision!)
        ? SkillChoices.Where(choice => !IsProjectedOwnedPick(choice) && !IsOwnedSelectionFinish(choice) &&
            choice.Parameters.GetValueOrDefault("owned-selection-role") != "unpick" &&
            choice.Id != OwnedCardDeclineChoice?.Id).ToArray() : [];

    private bool CanConfirmOwnedCardSelection => !_confirmingOwnedCardSelection && IsOwnedCardSelectionPending &&
        _selectedOwnedCardIds.Count >= (_snapshot.PendingDecision!.MinimumCardCount ?? _snapshot.PendingDecision.RequiredCardCount) &&
        _selectedOwnedCardIds.Count <= _snapshot.PendingDecision.RequiredCardCount &&
        (!HasProjectedOwnedSelection(_snapshot.PendingDecision) ||
            (_selectedOwnedCardIds.Count > 0 || _snapshot.PendingDecision.Choices.Any(IsOwnedSelectionFinish)) &&
            (_snapshot.PendingDecision.Choices.Select(choice => choice.Parameters.GetValueOrDefault("owned-selection-counts"))
                .FirstOrDefault(value => value is not null) is not { } counts ||
             counts.Split(',').Any(value => int.TryParse(value, out var count) && count == _selectedOwnedCardIds.Count)));

    private PromptChoice? OwnedCardDeclineChoice => IsOwnedCardSelectionPending
        ? _snapshot.PendingDecision!.Choices.Where(choice => IsPaymentDecline(choice) ||
            IsOwnedSelectionFinish(choice) && (_snapshot.PendingDecision.MinimumCardCount == 0 ||
                choice.Parameters.GetValueOrDefault("selection-index") == "0") &&
                !HasProjectedOwnedSelection(_snapshot.PendingDecision)).Take(2).ToArray() is [var decline] ? decline : null
        : null;

    private void SyncOwnedCardSelection()
    {
        var promptId = IsOwnedCardSelectionPending ? _snapshot.PendingDecision!.PromptId : (PromptId?)null;
        var tagged = IsOwnedCardSelectionPending && HasProjectedOwnedSelection(_snapshot.PendingDecision!)
            ? _snapshot.PendingDecision!.Choices.First(choice => choice.Parameters.ContainsKey("owned-selection-role")) : null;
        (string? Frame, string? Stage)? scope = tagged is null ? null :
            (tagged.Parameters.GetValueOrDefault("owned-selection-frame"), tagged.Parameters.GetValueOrDefault("owned-selection-stage"));
        if (!ReferenceEquals(_ownedSelectionGame, _game) || _ownedSelectionPromptId != promptId &&
            (scope is null || scope != _ownedSelectionScope))
            _selectedOwnedCardIds.Clear();
        if (tagged is not null)
            _selectedOwnedCardIds.RemoveAll(id => !_snapshot.PendingDecision!.Choices.Any(choice =>
                IsProjectedOwnedPick(choice) && choice.Cards.SequenceEqual([id])));
        _ownedSelectionGame = _game;
        _ownedSelectionPromptId = promptId;
        _ownedSelectionScope = scope;
        RaisePropertyChanged(nameof(SelectedTargetCardIds));
    }

    private void ToggleOwnedSelectionCard(int id)
    {
        if (!IsOwnedSelectionMaterial(id)) return;
        if (_snapshot.PendingDecision!.Choices.SingleOrDefault(choice => choice.Cards.SequenceEqual([id]) &&
                choice.Parameters.GetValueOrDefault("owned-selection-role") == "unpick") is { } unpick)
        {
            SubmitPublishedPromptChoice(unpick, true, "选牌已更新");
            return;
        }
        if (!_selectedOwnedCardIds.Remove(id))
        {
            if (_selectedOwnedCardIds.Count >= _snapshot.PendingDecision!.RequiredCardCount) return;
            _selectedOwnedCardIds.Add(id);
        }
        foreach (var card in Hand) card.IsSelected = IsOwnedSelectionCardSelected(card.Id);
        SelectedCardText = $"已选择 {_selectedOwnedCardIds.Count}/{_snapshot.PendingDecision!.RequiredCardCount} 张牌";
        RefreshEquipmentMaterialSelection();
        RaisePropertyChanged(nameof(SelectedTargetCardIds));
        RefreshSelectionHint();
    }

    private string OwnedCardSelectionHint
    {
        get
        {
            var prompt = _snapshot.PendingDecision!;
            if (HasProjectedOwnedSelection(prompt))
            {
                var prior = NativeOwnedSelectionIds.Concat(prompt.Choices.Where(choice =>
                    choice.Parameters.GetValueOrDefault("owned-selection-role") == "unpick").SelectMany(choice => choice.Cards)).Distinct().Count();
                var counts = prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("owned-selection-counts"))
                    .FirstOrDefault(value => value is not null);
                var minimum = prompt.MinimumCardCount ?? prompt.RequiredCardCount;
                var bounds = minimum != prompt.RequiredCardCount
                    ? $"可选 {minimum} 至 {prompt.RequiredCardCount} 张" : $"需选 {prompt.RequiredCardCount} 张";
                if (counts is not null)
                {
                    var allowed = counts.Split(',').Where(value => int.TryParse(value, out _)).Select(int.Parse).ToHashSet();
                    var forbidden = Enumerable.Range(minimum, Math.Max(0, prompt.RequiredCardCount - minimum + 1)).Except(allowed).ToArray();
                    if (allowed.Count == 0) bounds = "当前没有可确认的张数";
                    else if (forbidden.Length > 0) bounds += $"（不可选 {string.Join('、', forbidden)} 张）";
                }
                return $"{prompt.SkillPrompt!.Name} · " + (prior > 0 ? $"原有已选 {prior} 张；" : "") +
                    $"{bounds}，已选 {_selectedOwnedCardIds.Count} 张 · 确定后统一弃置；再点取消单张，Esc 清空。";
            }
            if (prompt.MinimumCardCount is > 0 && prompt.MinimumCardCount < prompt.RequiredCardCount &&
                !prompt.Choices.Any(IsOwnedSelectionFinish))
                return $"{prompt.SkillPrompt!.Name} · 还需选 {prompt.RequiredCardCount} 张牌（已选 {_selectedOwnedCardIds.Count}）· " +
                    "可逐张或多选确认；选齐后结算，Esc 清空。";
            return $"{prompt.SkillPrompt!.Name} · 选择 " +
                (prompt.MinimumCardCount is { } min && min != prompt.RequiredCardCount
                    ? $"{min} 至 {prompt.RequiredCardCount}" : $"{prompt.RequiredCardCount}") +
                (IsForeignCardTakeSelectionPending ? " 张目标手牌" : " 张手牌或装备") +
                $"（已选 {_selectedOwnedCardIds.Count}/{prompt.RequiredCardCount}）· 选好后点击「确定」；再次点击取消单张，Esc 清空选择。";
        }
    }

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
        var projected = HasProjectedOwnedSelection(initial);
        var source = initial.Choices.First(choice => projected
            ? IsProjectedOwnedPick(choice) || IsOwnedSelectionFinish(choice)
            : choice.Cards.Count == 1 || IsOwnedSelectionFinish(choice)).Parameters;
        var action = source.GetValueOrDefault("program-action");
        bool SameSelection(PendingDecision? prompt, PromptChoice choice) =>
            prompt?.PlayerSeat == initial.PlayerSeat && prompt.SkillPrompt?.SkillId == initial.SkillPrompt!.SkillId &&
            (projected
                ? choice.Parameters.GetValueOrDefault("owned-selection-frame") == source.GetValueOrDefault("owned-selection-frame") &&
                  choice.Parameters.GetValueOrDefault("owned-selection-stage") == source.GetValueOrDefault("owned-selection-stage")
                : choice.Parameters.GetValueOrDefault("frame-id") == source.GetValueOrDefault("frame-id") &&
                  choice.Parameters.GetValueOrDefault("result-bind") == source.GetValueOrDefault("result-bind"));
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
                        SameSelection(prompt, choice) && (projected ? IsProjectedOwnedPick(choice) :
                            choice.Parameters.GetValueOrDefault("program-action") == action));
                    if (choice is null || !SubmitCommand(new AnswerPromptCommand(view.HumanSeat,
                            prompt.PromptId, choice.Id, view.Revision)).Accepted) return;
                }
                // A variable-size set completes through the current published Finish;
                // an exact/maximal set may already have advanced to the next effect.
                var current = _game.CreateSnapshot(_snapshot.HumanSeat);
                var finish = current.PendingDecision?.Choices.SingleOrDefault(choice => IsOwnedSelectionFinish(choice) &&
                    SameSelection(current.PendingDecision, choice));
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
