using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Controls;

public partial class GeneralGalleryPanel : UserControl
{
    private MainViewModel? _observedViewModel;
    private IInputElement? _detailsReturnFocus;

    public GeneralGalleryPanel() => InitializeComponent();

    public void FocusSearch() => GeneralGallerySearchBox.Focus();

    private void Panel_Loaded(object sender, RoutedEventArgs e) => Observe(DataContext as MainViewModel);
    private void Panel_Unloaded(object sender, RoutedEventArgs e) => Observe(null);
    private void Panel_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded) Observe(e.NewValue as MainViewModel);
    }

    private void Observe(MainViewModel? viewModel)
    {
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
            if (_observedViewModel?.HasGeneralGallerySelection == true)
            {
                _detailsReturnFocus ??= Keyboard.FocusedElement;
                Dispatcher.BeginInvoke(() => CloseGeneralDetailsButton.Focus());
            }
            else
            {
                if (_detailsReturnFocus is not null) Keyboard.Focus(_detailsReturnFocus);
                _detailsReturnFocus = null;
            }
        }
        if (e.PropertyName is nameof(MainViewModel.SelectedGeneralGallerySeries) or
            nameof(MainViewModel.SelectedGeneralGalleryGroup) or nameof(MainViewModel.SelectedGeneralGalleryFaction) or
            nameof(MainViewModel.GeneralGallerySearchText)) GalleryScroller.ScrollToTop();
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
