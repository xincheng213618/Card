using System.Windows.Controls;
namespace CardGame.Wpf.Controls;
public partial class ShanheShopPanel : UserControl
{
    public ShanheShopPanel()
    {
        InitializeComponent();
        MarketCategories.SelectionChanged += (_, _) => MarketCardsScroll.ScrollToLeftEnd();
        MarketCardsScroll.PreviewMouseWheel += (_, args) =>
        {
            MarketCardsScroll.ScrollToHorizontalOffset(MarketCardsScroll.HorizontalOffset - args.Delta * 1.5);
            args.Handled = true;
        };
    }
}
