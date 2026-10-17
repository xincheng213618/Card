using System.ComponentModel;
using System.Windows;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf;

public partial class MainWindow
{
    private MainViewModel? _shanheWindowViewModel;
    private ShanheCampaignWindow? _shanheWindow;
    private Visibility _embeddedShanheVisibility;

    private void InitializeShanheWindowHost(MainViewModel viewModel)
    {
        _shanheWindowViewModel = viewModel;
        viewModel.PropertyChanged += ShanheWindowViewModel_PropertyChanged;
    }

    public void OpenShanheWindow()
    {
        if (_shanheWindowViewModel is not { } viewModel) return;
        if (!viewModel.IsShanheOpen)
        {
            if (!viewModel.OpenShanheCommand.CanExecute(null)) return;
            viewModel.OpenShanheCommand.Execute(null);
            if (!viewModel.IsShanheOpen) return;
        }
        if (FocusDetachedShanheWindow()) return;

        var window = new ShanheCampaignWindow(viewModel) { Owner = this };
        _shanheWindow = window;
        _embeddedShanheVisibility = ShanhePanel.Visibility;
        ShanhePanel.SetCurrentValue(VisibilityProperty, Visibility.Collapsed);
        window.Closed += ShanheWindow_Closed;
        window.Show();
    }

    private bool FocusDetachedShanheWindow()
    {
        if (_shanheWindow is not { } window) return false;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        window.FocusCampaign();
        return true;
    }

    private void ShanheWindowViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsShanheOpen) &&
            _shanheWindowViewModel is { IsShanheOpen: false }) _shanheWindow?.Close();
    }

    private void ShanheWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is not ShanheCampaignWindow window || !ReferenceEquals(window, _shanheWindow)) return;
        window.Closed -= ShanheWindow_Closed;
        _shanheWindow = null;
        ShanhePanel.SetCurrentValue(VisibilityProperty, _embeddedShanheVisibility);
    }

    private void DisposeShanheWindowHost()
    {
        if (_shanheWindowViewModel is { } viewModel)
            viewModel.PropertyChanged -= ShanheWindowViewModel_PropertyChanged;
        _shanheWindowViewModel = null;
        if (_shanheWindow is not { } window) return;
        window.Closed -= ShanheWindow_Closed;
        _shanheWindow = null;
        window.Close();
        ShanhePanel.SetCurrentValue(VisibilityProperty, _embeddedShanheVisibility);
    }
}
