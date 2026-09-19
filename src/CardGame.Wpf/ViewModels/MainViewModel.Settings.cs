using System.Windows.Input;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private bool _isSettingsOpen;

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

    public ICommand OpenSettingsCommand { get; private set; } = null!;
    public ICommand CloseSettingsCommand { get; private set; } = null!;

    private void InitializeSettings()
    {
        OpenSettingsCommand = new RelayCommand(() =>
        {
            IsLogOpen = false;
            IsHelpOpen = false;
            IsHistoryOpen = false;
            IsGeneralGalleryOpen = false;
            IsSettingsOpen = true;
        });
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
    }
}
