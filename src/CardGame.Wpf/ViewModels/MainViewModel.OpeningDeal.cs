using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private DispatcherTimer? _openingDealTimer;
    private bool _openingDealArmed;
    private bool _isOpeningDealVisible;
    private bool _manualOpeningDealVisible;
    private bool _isOpeningHandEditorOpen;
    private int? _selectedOpeningCardId;
    private string _openingCardSearch = string.Empty;
    private OpeningCardChoice? _selectedOpeningCardChoice;

    public ObservableCollection<CardViewModel> OpeningHandCards { get; } = [];
    public bool IsOpeningDealVisible { get => _isOpeningDealVisible; private set => SetProperty(ref _isOpeningDealVisible, value); }
    public string OpeningDealTitle => HumanPlayer is null ? "起 手牌" : $"{HumanPlayer.GeneralName} · 起 手牌";
    public string OpeningDealSummary => $"已获得 {OpeningHandCards.Count} 张起手牌 · 牌面仅对你可见";
    public ICommand DismissOpeningDealCommand { get; private set; } = null!;
    public ICommand RerollOpeningHandCommand { get; private set; } = null!;
    public bool CanRerollOpeningHand => _snapshot?.PendingDecision?.Kind == DecisionKind.OpeningHand;
    public ICommand ToggleOpeningHandEditorCommand { get; private set; } = null!;
    public ICommand ReplaceOpeningCardCommand { get; private set; } = null!;
    public ObservableCollection<OpeningCardChoice> OpeningCardChoices { get; } = [];
    public bool IsOpeningHandEditorOpen
    {
        get => _isOpeningHandEditorOpen;
        private set => SetProperty(ref _isOpeningHandEditorOpen, value);
    }
    public string OpeningCardSearch
    {
        get => _openingCardSearch;
        set { if (SetProperty(ref _openingCardSearch, value)) RefreshOpeningCardChoices(); }
    }
    public OpeningCardChoice? SelectedOpeningCardChoice
    {
        get => _selectedOpeningCardChoice;
        set
        {
            if (SetProperty(ref _selectedOpeningCardChoice, value))
                (ReplaceOpeningCardCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }
    public string OpeningHandHint => !IsOpeningHandEditorOpen ? "起手牌 · 可随机或换牌，确定后开始对局" :
        Hand.FirstOrDefault(card => card.Id == _selectedOpeningCardId) is { } selected
            ? $"替换【{selected.Name}】· 点击手牌可改选" : "请先点选一张要替换的手牌";

    private void InitializeOpeningDeal()
    {
        DismissOpeningDealCommand = new RelayCommand(() =>
        {
            if (CanRerollOpeningHand) AnswerOpeningHand("confirm");
            else DismissOpeningDeal();
        });
        RerollOpeningHandCommand = new RelayCommand(() => AnswerOpeningHand("reroll"), () => CanRerollOpeningHand);
        ToggleOpeningHandEditorCommand = new RelayCommand(() =>
        {
            if (!CanRerollOpeningHand) return;
            IsOpeningHandEditorOpen = !IsOpeningHandEditorOpen;
            RefreshOpeningCardChoices();
        }, () => CanRerollOpeningHand);
        ReplaceOpeningCardCommand = new RelayCommand(ReplaceSelectedOpeningCard, () =>
            CanRerollOpeningHand && IsOpeningHandEditorOpen && SelectedOpeningCardChoice is not null);
        _openingDealTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1800)
        };
        _openingDealTimer.Tick += OnOpeningDealTick;
        PropertyChanged += OnOpeningPresentationChanged;
    }

    private void ArmOpeningDeal()
    {
        _openingDealArmed = IsMotionEnabled;
        RefreshOpeningDeal();
    }

    private void ResetStartupPresentation()
    {
        IsIdentityRevealOpen = false;
        CancelOpeningDeal();
    }

    private void CancelOpeningDeal()
    {
        ResetOpeningHandEditor();
        _openingDealArmed = false;
        _manualOpeningDealVisible = false;
        DismissOpeningDeal();
    }

    private void RefreshOpeningDeal()
    {
        RaisePropertyChanged(nameof(CanRerollOpeningHand));
        (RerollOpeningHandCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ToggleOpeningHandEditorCommand as RelayCommand)?.NotifyCanExecuteChanged();
        if (CanRerollOpeningHand)
        {
            _openingDealArmed = false;
            _manualOpeningDealVisible = true;
            _openingDealTimer?.Stop();
            IsOpeningDealVisible = false;
            OpeningHandCards.Clear();
            foreach (var card in Hand) OpeningHandCards.Add(card);
            RaisePropertyChanged(nameof(OpeningDealSummary));
            RefreshOpeningCardChoices();
            return;
        }
        ResetOpeningHandEditor();
        if (_manualOpeningDealVisible)
        {
            _manualOpeningDealVisible = false;
            DismissOpeningDeal();
        }
        if (!_openingDealArmed || IsGeneralSelectionPending || Hand.Count == 0 || HasGameOver) return;
        _openingDealArmed = false;
        ShowOpeningDeal(Hand);
    }

    private void ShowOpeningDeal(IEnumerable<CardViewModel> cards)
    {
        OpeningHandCards.Clear();
        foreach (var card in cards) OpeningHandCards.Add(card);
        RaisePropertyChanged(nameof(OpeningDealTitle));
        RaisePropertyChanged(nameof(OpeningDealSummary));
        IsOpeningDealVisible = true;
        RefreshOpeningDealTimer();
    }

    private void DismissOpeningDeal()
    {
        _openingDealTimer?.Stop();
        IsOpeningDealVisible = false;
        OpeningHandCards.Clear();
        RaisePropertyChanged(nameof(OpeningDealSummary));
    }

    private bool IsOpeningDealCovered => IsSettingsOpen || IsHistoryOpen || IsGeneralGalleryOpen ||
        IsHelpOpen || IsNewGameSetupOpen || IsIdentityRevealOpen || IsLogOpen ||
        IsGeneralSelectionPending || HasGameOver || IsReserveDialogOpen;

    private void RefreshOpeningDealTimer()
    {
        _openingDealTimer?.Stop();
        if (IsOpeningDealVisible && !IsOpeningDealCovered && !CanRerollOpeningHand) _openingDealTimer?.Start();
    }

    private void OnOpeningPresentationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IsMotionEnabled))
        {
            if (!IsMotionEnabled && !CanRerollOpeningHand) CancelOpeningDeal();
        }
        else if (args.PropertyName is nameof(IsSettingsOpen) or nameof(IsHistoryOpen) or
                 nameof(IsGeneralGalleryOpen) or nameof(IsHelpOpen) or nameof(IsNewGameSetupOpen) or
                 nameof(IsIdentityRevealOpen) or nameof(IsLogOpen) or nameof(IsGeneralSelectionPending) or nameof(HasGameOver) or nameof(IsReserveDialogOpen))
        {
            // A covering panel pauses the countdown. Give the player the full
            // reading interval when the opening cards are exposed again.
            RefreshOpeningDealTimer();
        }
    }

    private void OnOpeningDealTick(object? sender, EventArgs args)
    {
        if (IsOpeningDealCovered) RefreshOpeningDealTimer();
        else DismissOpeningDeal();
    }

    private void AnswerOpeningHand(string action)
    {
        if (_snapshot?.PendingDecision is not { Kind: DecisionKind.OpeningHand } prompt) return;
        var choice = prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("action") == action);
        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new AnswerPromptCommand(_snapshot.HumanSeat, prompt.PromptId, choice.Id, _snapshot.Revision));
            RefreshCommandResult(result.State);
        });
    }

    private void ResetOpeningHandEditor()
    {
        IsOpeningHandEditorOpen = false;
        _selectedOpeningCardId = null;
        OpeningCardSearch = string.Empty;
        OpeningCardChoices.Clear();
        SelectedOpeningCardChoice = null;
    }

    private void SelectOpeningHandCard(CardViewModel card)
    {
        _selectedOpeningCardId = card.Id;
        RefreshOpeningCardChoices();
    }

    private void RefreshOpeningCardChoices()
    {
        var previousKind = SelectedOpeningCardChoice?.Kind;
        OpeningCardChoices.Clear();
        if (CanRerollOpeningHand)
        {
            if (!Hand.Any(card => card.Id == _selectedOpeningCardId)) _selectedOpeningCardId = Hand.FirstOrDefault()?.Id;
            foreach (var card in Hand) card.IsSelected = card.Id == _selectedOpeningCardId;
            foreach (var choice in _snapshot.PendingDecision!.Choices.Where(choice =>
                         choice.Parameters.GetValueOrDefault("action") == "replace" &&
                         choice.Cards.Contains(_selectedOpeningCardId ?? -1) &&
                         choice.Description.Contains(OpeningCardSearch.Trim(), StringComparison.CurrentCultureIgnoreCase)))
                OpeningCardChoices.Add(new(_snapshot.PendingDecision.PromptId, choice.Id,
                    Enum.Parse<CardKind>(choice.Parameters["kind"]), choice.Description));
        }
        SelectedOpeningCardChoice = OpeningCardChoices.FirstOrDefault(choice => choice.Kind == previousKind) ?? OpeningCardChoices.FirstOrDefault();
        RaisePropertyChanged(nameof(OpeningHandHint));
        (ReplaceOpeningCardCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void ReplaceSelectedOpeningCard()
    {
        if (!CanRerollOpeningHand || !IsOpeningHandEditorOpen || SelectedOpeningCardChoice is not { } selected ||
            _snapshot.PendingDecision!.PromptId != selected.PromptId ||
            !_snapshot.PendingDecision.Choices.Any(choice => choice.Id == selected.ChoiceId && choice.Cards.Contains(_selectedOpeningCardId ?? -1))) return;
        var oldIds = Hand.Select(card => card.Id).ToHashSet();
        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new AnswerPromptCommand(_snapshot.HumanSeat, selected.PromptId, selected.ChoiceId, _snapshot.Revision));
            if (result.Accepted) _selectedOpeningCardId = result.State.Players.Single(player => player.IsHuman).Hand.First(card => !oldIds.Contains(card.Id)).Id;
            RefreshCommandResult(result.State);
        });
    }
}

public sealed record OpeningCardChoice(PromptId PromptId, ChoiceId ChoiceId, CardKind Kind, string Name);
