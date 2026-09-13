using System.Windows.Controls;

namespace CardGame.Wpf.Controls;

public partial class MatchHistoryPanel : UserControl
{
    public MatchHistoryPanel() => InitializeComponent();
    public void FocusHistory()
    {
        if (HistoryList.Items.Count > 0) HistoryList.Focus();
        else HistoryCloseButton.Focus();
    }
}
