using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Wpf.ViewModels;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Persistence;

namespace CardGame.Wpf;

public partial class MainWindow : Window
{
    private readonly GameAudioController _audio;
    private IInputElement? _focusBeforeGuide;
    private IInputElement? _focusBeforeHistory;
    private IInputElement? _focusBeforeGeneralGallery;
    public MainWindow() : this(new MainViewModel(useExpandedContent: true, historyStore: new FileMatchHistoryStore(),
        preferencesStore: new FilePlayerPreferencesStore()))
    { }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _audio = new GameAudioController(viewModel, () => new MediaPlayerAudioOutput());
        Activated += (_, _) => _audio.SetActive(true);
        Deactivated += (_, _) => _audio.SetActive(false);
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _audio.Dispose();
        (DataContext as MainViewModel)?.Dispose();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.FlushPendingSave()) return;
        viewModel.IsAutoAdvance = false;
        e.Cancel = MessageBox.Show(this, $"{viewModel.SaveStatus}\n\n仍然退出吗？选择“否”可返回牌桌重试保存。",
            "退出前保存失败", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes;
    }

    private void NewGameSetupPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (NewGameSetupPanel.IsVisible)
            {
                if (TableModeChoices.ItemContainerGenerator.ContainerFromItem(TableModeChoices.SelectedItem) is UIElement choice)
                    choice.Focus();
                else
                    TableModeChoices.Focus();
            }
            else
            {
                TableSurface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }
        }));
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        NewGameSetupPanel_IsVisibleChanged(NewGameSetupPanel, default);

    private void PlayerGuideOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (PlayerGuideOverlay.IsVisible) _focusBeforeGuide = Keyboard.FocusedElement;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (PlayerGuideOverlay.IsVisible) PlayerGuide.FocusGuide();
            else if (_focusBeforeGuide is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
            else if (NewGameSetupPanel.IsVisible) TableModeChoices.Focus();
            else TableSurface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }));
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleShortcut(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    private bool HandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (DataContext is not MainViewModel viewModel) return false;
        if (viewModel.IsGeneralGalleryOpen)
        {
            if (key != Key.Escape) return false;
            viewModel.IsGeneralGalleryOpen = false;
            return true;
        }
        if (viewModel.IsHistoryOpen && !(key == Key.M && modifiers == ModifierKeys.Control))
        {
            if (key != Key.Escape) return false;
            viewModel.IsHistoryOpen = false;
            return true;
        }
        if (key == Key.F1 && modifiers == ModifierKeys.None)
        {
            viewModel.ToggleHelpCommand.Execute(null);
            return true;
        }
        if (key == Key.M && modifiers == ModifierKeys.Control)
        {
            viewModel.IsSoundEnabled = !viewModel.IsSoundEnabled;
            return true;
        }
        if (key == Key.Escape)
        {
            if (viewModel.IsHelpOpen) viewModel.IsHelpOpen = false;
            else if (viewModel.IsNewGameSetupOpen) viewModel.IsNewGameSetupOpen = false;
            else if (viewModel.IsLogOpen) viewModel.IsLogOpen = false;
            else viewModel.ClearSelectionCommand.Execute(null);
            return true;
        }
        if (key == Key.F5 && modifiers == ModifierKeys.None && !viewModel.IsNewGameSetupOpen)
        {
            if (viewModel.SaveGameCommand.CanExecute(null))
            {
                viewModel.SaveGameCommand.Execute(null);
                return true;
            }
            return false;
        }
        if (viewModel.IsHelpOpen || viewModel.IsLogOpen || viewModel.IsNewGameSetupOpen || viewModel.IsGeneralGalleryOpen || viewModel.HasGameOver || viewModel.IsGeneralSelectionPending) return false;
        if (key == Key.Enter && modifiers == ModifierKeys.Control && viewModel.CanEndTurn)
        {
            viewModel.EndTurnCommand.Execute(null);
            return true;
        }
        if (key == Key.Enter && modifiers == ModifierKeys.None && viewModel.CanConfirmSelected)
        {
            viewModel.ConfirmSelectedCommand.Execute(null);
            return true;
        }
        if (key is >= Key.D1 and <= Key.D9 && modifiers == ModifierKeys.None)
        {
            var index = key - Key.D1;
            if (index < viewModel.Hand.Count && viewModel.Hand[index].IsPlayable)
            {
                viewModel.SelectCardCommand.Execute(viewModel.Hand[index]);
                return true;
            }
        }
        return false;
    }

    private void HistoryOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (HistoryOverlay.IsVisible) _focusBeforeHistory = Keyboard.FocusedElement;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (HistoryOverlay.IsVisible) HistoryPanel.FocusHistory();
            else if (_focusBeforeHistory is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
            else TableSurface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }));
    }

    private void GeneralGalleryOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (GeneralGalleryOverlay.IsVisible) _focusBeforeGeneralGallery = Keyboard.FocusedElement;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (GeneralGalleryOverlay.IsVisible) GeneralGallerySearchBox.Focus();
            else if (_focusBeforeGeneralGallery is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
            else TableSurface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }));
    }
}
