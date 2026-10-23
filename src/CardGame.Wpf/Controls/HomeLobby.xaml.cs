using System.Windows.Controls;

namespace CardGame.Wpf.Controls;

public partial class HomeLobby : UserControl
{
    public HomeLobby() => InitializeComponent();

    public void FocusPrimary() => EnterBattleButton.Focus();

    internal Button WushuangSaveAccessFocusTarget => ContinueWushuangButton.IsVisible && ContinueWushuangButton.IsEnabled
        ? ContinueWushuangButton : WushuangBattleButton;

    private void OpenLanRooms(object sender, System.Windows.RoutedEventArgs e)
    {
        var owner = System.Windows.Window.GetWindow(this);
        new LanRoomWindow(DataContext as ViewModels.MainViewModel) { Owner = owner }.Show();
    }
}
