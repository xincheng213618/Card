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

    public ObservableCollection<CardViewModel> OpeningHandCards { get; } = [];
    public bool IsOpeningDealVisible { get => _isOpeningDealVisible; private set => SetProperty(ref _isOpeningDealVisible, value); }
    public string OpeningDealTitle => HumanPlayer is null ? "起 手牌" : $"{HumanPlayer.GeneralName} · 起 手牌";
    public string OpeningDealSummary => $"已获得 {OpeningHandCards.Count} 张起手牌 · 牌面仅对你可见";
    public ICommand DismissOpeningDealCommand { get; private set; } = null!;
    public ICommand RerollOpeningHandCommand { get; private set; } = null!;
    public bool CanRerollOpeningHand => _snapshot?.PendingDecision?.Kind == DecisionKind.OpeningHand;

    private void InitializeOpeningDeal()
    {
        DismissOpeningDealCommand = new RelayCommand(() =>
        {
            if (CanRerollOpeningHand) AnswerOpeningHand("confirm");
            else DismissOpeningDeal();
        });
        RerollOpeningHandCommand = new RelayCommand(() => AnswerOpeningHand("reroll"), () => CanRerollOpeningHand);
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
        _openingDealArmed = false;
        _manualOpeningDealVisible = false;
        DismissOpeningDeal();
    }

    private void RefreshOpeningDeal()
    {
        RaisePropertyChanged(nameof(CanRerollOpeningHand));
        (RerollOpeningHandCommand as RelayCommand)?.NotifyCanExecuteChanged();
        if (CanRerollOpeningHand)
        {
            _openingDealArmed = false;
            _manualOpeningDealVisible = true;
            ShowOpeningDeal(Hand);
            return;
        }
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
}
