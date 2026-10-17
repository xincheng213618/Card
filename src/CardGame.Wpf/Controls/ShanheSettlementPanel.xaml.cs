using System.Windows;
using System.Windows.Controls;
namespace CardGame.Wpf.Controls;
public partial class ShanheSettlementPanel : UserControl
{
    public ShanheSettlementPanel() => InitializeComponent();
    public event EventHandler? OpenCodexRequested;
    private void OpenCodex(object sender, RoutedEventArgs args) => OpenCodexRequested?.Invoke(this, EventArgs.Empty);
    public void FocusSettlement() => SettlementCloseButton.Focus();
}
