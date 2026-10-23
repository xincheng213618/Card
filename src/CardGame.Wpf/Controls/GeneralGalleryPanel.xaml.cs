using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Controls;

public partial class GeneralGalleryPanel : UserControl
{
    private MainViewModel? _observedViewModel;
    private IInputElement? _detailsReturnFocus;
    private int _detailsFocusRequest;

    public GeneralGalleryPanel() => InitializeComponent();

    public void FocusSearch() => GeneralGallerySearchBox.Focus();

    private void GeneralGalleryFiltersCleared_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || !IsVisible || !IsEnabled ||
            sender is not UIElement { IsVisible: true, IsEnabled: true } ||
            DataContext is not MainViewModel
            {
                IsGeneralGalleryOpen: true, HasGeneralGallerySelection: false,
                IsWushuangShopOpen: false, IsShanheOpen: false, IsSettingsOpen: false, IsHistoryOpen: false
            } viewModel || !ReferenceEquals(viewModel, _observedViewModel)) return;
        // Move focus before the bound command collapses its empty-result button.
        _detailsFocusRequest++;
        FocusSearch();
    }

    public void FocusCurrentView()
    {
        if (DataContext is MainViewModel { HasGeneralGallerySelection: true }) CloseGeneralDetailsButton.Focus();
        else FocusSearch();
    }

    private void Panel_Loaded(object sender, RoutedEventArgs e) => Observe(DataContext as MainViewModel);
    private void Panel_Unloaded(object sender, RoutedEventArgs e) => Observe(null);
    private void Panel_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded) Observe(e.NewValue as MainViewModel);
    }

    private void Observe(MainViewModel? viewModel)
    {
        _detailsFocusRequest++;
        _detailsReturnFocus = null;
        if (_observedViewModel is not null) _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _observedViewModel = viewModel;
        if (_observedViewModel is not null) _observedViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedGeneralGalleryEntry))
        {
            GeneralSkillsScroller.ScrollToTop();
            GeneralSkinsScroller.ScrollToTop();
            GeneralVoicesScroller.ScrollToTop();
            if (_observedViewModel is not { } viewModel) return;
            if (viewModel.HasGeneralGallerySelection)
            {
                _detailsReturnFocus ??= Keyboard.FocusedElement;
                QueueDetailsFocus(viewModel, showingDetails: true, null);
            }
            else
            {
                var previous = _detailsReturnFocus;
                _detailsReturnFocus = null;
                // HasGeneralGallerySelection is notified after the selected entry.
                // Wait for its bindings to enable the original browse target again.
                QueueDetailsFocus(viewModel, showingDetails: false, previous);
            }
        }
    }

    private void QueueDetailsFocus(MainViewModel viewModel, bool showingDetails, IInputElement? previous)
    {
        var request = ++_detailsFocusRequest;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (request != _detailsFocusRequest || !ReferenceEquals(viewModel, _observedViewModel) ||
                !ReferenceEquals(DataContext, viewModel) || !IsVisible || !IsEnabled ||
                !viewModel.IsGeneralGalleryOpen || viewModel.IsHistoryOpen || viewModel.IsSettingsOpen ||
                viewModel.HasGeneralGallerySelection != showingDetails) return;
            if (showingDetails)
            {
                // Next/previous entries keep the same live detail controls.
                // Retain their focus so keyboard browsing can continue.
                if (Keyboard.FocusedElement is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } current &&
                    current.IsDescendantOf(GeneralDetailsPanel)) return;
                CloseGeneralDetailsButton.Focus();
            }
            else if (previous is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } target &&
                     target.IsDescendantOf(GalleryBrowseSurface)) target.Focus();
            else FocusSearch();
        }));
    }

    private void SeriesPrevious_Click(object sender, RoutedEventArgs e) => SeriesScroller.ScrollToHorizontalOffset(SeriesScroller.HorizontalOffset - 300);
    private void SeriesNext_Click(object sender, RoutedEventArgs e) => SeriesScroller.ScrollToHorizontalOffset(SeriesScroller.HorizontalOffset + 300);
    private void SeriesScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SeriesScroller.ScrollToHorizontalOffset(SeriesScroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
    private void SeriesTab_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ((FrameworkElement)sender).BringIntoView();
}
