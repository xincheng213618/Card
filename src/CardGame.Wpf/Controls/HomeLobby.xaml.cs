using System.Windows;
using System.Windows.Controls;

namespace CardGame.Wpf.Controls;

public partial class HomeLobby : UserControl
{
    private Button? _lastModeEntry;

    public HomeLobby()
    {
        InitializeComponent();
        AddHandler(Button.ClickEvent, new RoutedEventHandler(RememberModeEntry));
    }

    public void FocusPrimary() =>
        (_lastModeEntry is { IsVisible: true, IsEnabled: true, Focusable: true } entry ? entry : EnterBattleButton).Focus();

    private void RememberModeEntry(object sender, RoutedEventArgs e)
    {
        // Click is raised before its command opens the covering configuration.
        // Mere Tab traversal and utility actions must not replace this origin.
        if (DataContext is ViewModels.MainViewModel model && e.OriginalSource is Button entry &&
            entry.IsDescendantOf(this) && ReferenceEquals(entry.Command, model.OpenLobbyCategoryCommand))
            _lastModeEntry = entry;
    }

    internal Button WushuangSaveAccessFocusTarget => ContinueWushuangButton.IsVisible && ContinueWushuangButton.IsEnabled
        ? ContinueWushuangButton : WushuangBattleButton;

    private void OpenLanRooms(object sender, System.Windows.RoutedEventArgs e)
    {
        var owner = System.Windows.Window.GetWindow(this);
        new LanRoomWindow(DataContext as ViewModels.MainViewModel) { Owner = owner }.Show();
    }
}
