using System.Windows.Controls;

namespace CardGame.Wpf.Controls;

public partial class PlayerGuidePanel : UserControl
{
    public PlayerGuidePanel() => InitializeComponent();
    public void FocusGuide() => CloseGuideButton.Focus();
}
