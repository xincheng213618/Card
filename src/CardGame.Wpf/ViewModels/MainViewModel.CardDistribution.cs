using System.Windows.Input;
using CardGame.Core;
using CardGame.Wpf.Controls;

namespace CardGame.Wpf.ViewModels;

public sealed record CardDistributionDestination(string Key, string Label, int? Seat, bool IsSelected);

public sealed partial class MainViewModel
{
    private readonly HashSet<int> _distributionCardIds = [];
    private IReadOnlyList<PromptChoice> _distributionChoices = [];
    private string? _distributionTarget;
    private PromptId? _distributionPrompt;
    private GameEngine? _distributionGame;
    private bool _confirmingDistribution;
    public IReadOnlyList<CardViewModel> DistributionCards { get; private set; } = [];
    public IReadOnlyList<CardDistributionDestination> DistributionDestinations { get; private set; } = [];
    public IReadOnlyList<PromptChoice> DistributionFinishChoices { get; private set; } = [];

    private static bool IsCardDistributionPrompt(GameSnapshot? snapshot) => snapshot?.PendingDecision is { } prompt &&
        prompt.PlayerSeat == snapshot.HumanSeat && prompt.Choices.Any(choice =>
            choice.Cards.Count == 1 && choice.Parameters.ContainsKey("distribution-target"));
    public bool IsCardDistributionPending => IsCardDistributionPrompt(_snapshot);
    public bool HasStandardCenterChoices => HasCenterChoices && !IsCardDistributionPending;
    private int DistributionMaximum => _distributionChoices.Count == 0 ? 0 : Math.Min(DistributionCards.Count,
        int.Parse(_distributionChoices[0].Parameters["distribution-limit"], System.Globalization.CultureInfo.InvariantCulture));
    private bool IsDistributionMaterial(int id) => IsCardDistributionPending && DistributionCards.Any(card => card.Id == id);
    private bool CanConfirmDistribution => !_confirmingDistribution && IsCardDistributionPending &&
        _distributionCardIds.Count > 0 && _distributionCardIds.Count <= DistributionMaximum &&
        _distributionCardIds.All(id => _distributionChoices.Any(choice => choice.Cards[0] == id &&
            choice.Parameters["distribution-target"] == _distributionTarget));
    public bool ShowDistributionCards => DistributionCards.Any(card => !Hand.Any(hand => hand.Id == card.Id));
    public string DistributionSelectionHint => $"已选 {_distributionCardIds.Count}/{DistributionMaximum} 张 · " +
        (_distributionTarget is null ? "选择去向后点击「确定」。" :
            "去向：" + DistributionDestinations.FirstOrDefault(target => target.Key == _distributionTarget)?.Label.Split('·', '\n')[0].Trim() + " · 点击「确定」分配。") +
        " 再次点击取消单张，Esc 清空。";

    private void SyncCardDistribution()
    {
        if (!ReferenceEquals(_distributionGame, _game) || _distributionPrompt != _snapshot.PendingDecision?.PromptId)
        {
            _distributionCardIds.Clear();
            _distributionTarget = null;
        }
        _distributionGame = _game;
        _distributionPrompt = _snapshot.PendingDecision?.PromptId;
        _distributionChoices = IsCardDistributionPending ? _snapshot.PendingDecision!.Choices.Where(choice =>
            choice.Cards.Count == 1 && choice.Parameters.ContainsKey("distribution-target")).ToArray() : [];
        DistributionCards = _distributionChoices.GroupBy(choice => choice.Cards[0]).Select(group =>
            PromptCardChoiceConverter.Present(group.First(), _snapshot).Face).OfType<CardViewModel>().ToArray();
        DistributionFinishChoices = IsCardDistributionPending ? _snapshot.PendingDecision!.Choices.Where(choice =>
            choice.Cards.Count == 0 && choice.Parameters.ContainsKey("distribution-scope")).ToArray() : [];
        RebuildDistributionDestinations();
    }

    private void RebuildDistributionDestinations()
    {
        DistributionDestinations = _distributionChoices.GroupBy(choice => choice.Parameters["distribution-target"])
            .Select(group => new CardDistributionDestination(group.Key, group.First().Parameters["distribution-label"],
                group.First().Targets.Count == 1 ? group.First().Targets[0] : null, group.Key == _distributionTarget)).ToArray();
        RaisePropertyChanged(nameof(IsCardDistributionPending));
        RaisePropertyChanged(nameof(HasStandardCenterChoices));
        RaisePropertyChanged(nameof(DistributionCards));
        RaisePropertyChanged(nameof(ShowDistributionCards));
        RaisePropertyChanged(nameof(DistributionDestinations));
        RaisePropertyChanged(nameof(DistributionFinishChoices));
        RaisePropertyChanged(nameof(DistributionSelectionHint));
    }

    private void ToggleDistributionCard(int id)
    {
        if (!IsDistributionMaterial(id)) return;
        if (!_distributionCardIds.Remove(id) && _distributionCardIds.Count < DistributionMaximum)
            _distributionCardIds.Add(id);
        RefreshDistributionSelection();
    }

    private void RefreshDistributionSelection()
    {
        foreach (var card in DistributionCards.Concat(Hand)) card.IsSelected = _distributionCardIds.Contains(card.Id);
        RebuildDistributionDestinations();
        foreach (var seat in Seats)
        {
            seat.IsLegalTarget = DistributionDestinations.Any(target => target.Seat == seat.Seat);
            seat.IsSelectedTarget = DistributionDestinations.Any(target => target.Seat == seat.Seat && target.IsSelected);
        }
        CanPlaySelected = false;
        CanPlaySelectedAsSlash = false;
        RefreshSelectionHint();
    }

    private ICommand? _selectDistributionCardCommand, _selectDistributionTargetCommand, _selectAllDistributionCardsCommand, _finishDistributionCommand;
    public ICommand SelectDistributionCardCommand => _selectDistributionCardCommand ??= new RelayCommand<CardViewModel>(card =>
    {
        if (DistributionCards.Any(current => ReferenceEquals(current, card))) ToggleDistributionCard(card.Id);
    });
    public ICommand SelectDistributionTargetCommand => _selectDistributionTargetCommand ??= new RelayCommand<CardDistributionDestination>(target =>
    {
        if (!DistributionDestinations.Any(current => ReferenceEquals(current, target))) return;
        _distributionTarget = _distributionTarget == target.Key ? null : target.Key;
        RefreshDistributionSelection();
    });
    public ICommand SelectAllDistributionCardsCommand => _selectAllDistributionCardsCommand ??= new RelayCommand(() =>
    {
        if (!IsCardDistributionPending) return;
        _distributionCardIds.Clear();
        foreach (var card in DistributionCards.Where(card => _distributionTarget is null || _distributionChoices.Any(choice =>
                     choice.Cards[0] == card.Id && choice.Parameters["distribution-target"] == _distributionTarget)).Take(DistributionMaximum))
            _distributionCardIds.Add(card.Id);
        RefreshDistributionSelection();
    });
    public ICommand FinishDistributionCommand => _finishDistributionCommand ??= new RelayCommand<PromptChoice>(choice =>
    {
        if (DistributionFinishChoices.Any(current => ReferenceEquals(current, choice)))
            SubmitPublishedPromptChoice(choice, IsCardDistributionPending, "分配未完成");
    });

    private void ConfirmCardDistribution()
    {
        if (!CanConfirmDistribution) return;
        var initialGame = _game;
        var initial = _snapshot.PendingDecision!;
        var scope = _distributionChoices[0].Parameters["distribution-scope"];
        var target = _distributionTarget;
        var cards = DistributionCards.Where(card => _distributionCardIds.Contains(card.Id)).Select(card => card.Id).ToArray();
        ExecuteSafely(() =>
        {
            _confirmingDistribution = true;
            var completed = 0;
            string? rejection = null;
            try
            {
                foreach (var id in cards)
                {
                    // Requery the committed player view after every native command. A child
                    // prompt, different occurrence, or removed recipient stops this draft.
                    var view = _snapshot;
                    var prompt = view.PendingDecision;
                    if (!ReferenceEquals(_game, initialGame) || prompt?.PlayerSeat != initial.PlayerSeat ||
                        prompt.SkillPrompt?.SkillId != initial.SkillPrompt?.SkillId) break;
                    var choice = prompt.Choices.SingleOrDefault(choice => choice.Cards.SequenceEqual([id]) &&
                        choice.Parameters.GetValueOrDefault("distribution-scope") == scope &&
                        choice.Parameters.GetValueOrDefault("distribution-target") == target);
                    if (choice is null) break;
                    var result = SubmitCommand(new AnswerPromptCommand(view.HumanSeat, prompt.PromptId, choice.Id, view.Revision));
                    RefreshCommandResult(result.State);
                    if (!result.Accepted) { rejection = "分配未执行：" + result.Error?.Message; break; }
                    completed++;
                }
            }
            finally
            {
                _confirmingDistribution = false;
                _distributionCardIds.Clear();
                _distributionTarget = null;
                RefreshCurrentView();
                if (rejection is not null) PromptText = rejection;
                else if (completed > 0 && completed < cards.Length)
                    PromptText = $"已分配 {completed} 张；结算已进入新的选择，请完成当前响应后继续分配。";
            }
        });
    }
}
