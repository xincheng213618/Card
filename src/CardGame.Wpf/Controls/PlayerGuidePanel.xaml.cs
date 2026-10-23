using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Controls;

public partial class PlayerGuidePanel : UserControl
{
    private CardKind? _displayedGuideCard;
    private bool _guideScrollQueued;
    private int _searchFocusRequest;

    public PlayerGuidePanel() => InitializeComponent();
    public void FocusGuide() => CloseGuideButton.Focus();

    public void FocusCardSearch()
    {
        if (DataContext is not MainViewModel model) return;
        model.SelectedGuideSection = model.GuideSections.Single(section => section.Key == "cards");
        var request = ++_searchFocusRequest;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (request != _searchFocusRequest || !IsLoaded || !IsVisible || !IsEnabled ||
                !ReferenceEquals(DataContext, model) || !model.IsHelpOpen || model.IsSettingsOpen ||
                model.IsHistoryOpen || model.IsGeneralGalleryOpen || model.IsShanheOpen || model.IsWushuangShopOpen) return;
            GuideSearchBox.Focus();
            GuideSearchBox.SelectAll();
        }));
    }

    private void GuideSearchCleared_Click(object sender, RoutedEventArgs e) => GuideSearchBox.Focus();

    private void GuideCardDetails_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_guideScrollQueued) return;
        _guideScrollQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _guideScrollQueued = false;
            var card = GuideCardDetails.DataContext as GuideCardEntry;
            if (_displayedGuideCard == card?.Kind) return;
            _displayedGuideCard = card?.Kind;
            GuideCardDetailsScroll.ScrollToTop();
            if (card is not null) GuideCardList.ScrollIntoView(card);
        }));
    }
}
