using System.Windows.Controls;

namespace CardGame.Wpf.Controls;

public partial class HomeLobby : UserControl
{
    public HomeLobby() => InitializeComponent();

    public void FocusPrimary() => EnterBattleButton.Focus();
}
