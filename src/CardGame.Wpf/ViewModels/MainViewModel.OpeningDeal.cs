using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private DispatcherTimer? _openingDealTimer;
    private bool _openingDealArmed;
    private bool _isOpeningDealVisible;

    public ObservableCollection<CardViewModel> OpeningHandCards { get; } = [];
    public bool IsOpeningDealVisible { get => _isOpeningDealVisible; private set => SetProperty(ref _isOpeningDealVisible, value); }
    public string OpeningDealTitle => HumanPlayer is null ? "起 手牌" : $"{HumanPlayer.GeneralName} · 起 手牌";
    public string OpeningDealSummary => $"已获得 {OpeningHandCards.Count} 张起手牌 · 牌面仅对你可见";
    public ICommand DismissOpeningDealCommand { get; private set; } = null!;

    private void InitializeOpeningDeal()
    {
        DismissOpeningDealCommand = new RelayCommand(DismissOpeningDeal);
        _openingDealTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1800)
        };
        _openingDealTimer.Tick += OnOpeningDealTick;
    }

    private void ArmOpeningDeal() => _openingDealArmed = IsMotionEnabled;

    private void RefreshOpeningDeal()
    {
        if (!_openingDealArmed || IsGeneralSelectionPending || Hand.Count == 0 || HasGameOver) return;
        _openingDealArmed = false;
        OpeningHandCards.Clear();
        foreach (var card in Hand) OpeningHandCards.Add(card);
        RaisePropertyChanged(nameof(OpeningDealTitle));
        RaisePropertyChanged(nameof(OpeningDealSummary));
        IsOpeningDealVisible = true;
        _openingDealTimer?.Stop();
        _openingDealTimer?.Start();
    }

    private void DismissOpeningDeal()
    {
        _openingDealTimer?.Stop();
        IsOpeningDealVisible = false;
        OpeningHandCards.Clear();
        RaisePropertyChanged(nameof(OpeningDealSummary));
    }

    private void OnOpeningDealTick(object? sender, EventArgs args) => DismissOpeningDeal();
}
