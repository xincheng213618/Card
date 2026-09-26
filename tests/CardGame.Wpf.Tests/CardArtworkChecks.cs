using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class CardArtworkChecks
{
    public static void FacesAndInteractions(string output)
    {
        var definitions = CardCatalog.ImplementedCards.ToArray();
        foreach (var definition in definitions)
            Program.Assert(CardArt.Get(definition.Kind) is BitmapSource { PixelWidth: 186, PixelHeight: 260, IsFrozen: true },
                $"Missing or invalid original face for {definition.DisplayName}.");
        Program.Assert(CardArt.Get(null) is null && CardArt.Get((CardKind)int.MaxValue) is null,
            "An unspecified/unknown card must not reveal an arbitrary face.");
        Program.Assert(ReferenceEquals(CardArt.Get(CardKind.Slash), CardArt.Get(CardKind.Slash)) &&
            !ReferenceEquals(CardArt.Get(CardKind.Slash), CardArt.Get(CardKind.ThunderSlash)),
            "Card faces must be cached while Slash variants remain distinct.");

        // Render the real template with every original face and runtime rank/suit overlays.
        var sheet = new WrapPanel { Width = 990, Background = System.Windows.Media.Brushes.DarkSlateGray };
        var index = 0;
        foreach (var definition in definitions)
        {
            var card = new CardViewModel
            {
                Id = index++, Kind = definition.Kind, Name = definition.DisplayName,
                KindLabel = definition.CategoryName, SuitGlyph = index % 2 == 0 ? "♦" : "♠", Rank = "Q",
                Description = definition.Description, IsPlayable = true
            };
            sheet.Children.Add(new ContentControl
            {
                Width = 110, Height = 170, Content = card,
                ContentTemplate = (DataTemplate)Application.Current.FindResource("CardTemplate")
            });
        }
        // A Window provides the same command ancestor as the live hand.
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore()) { IsMotionEnabled = false };
        var preview = new Window { Content = sheet, DataContext = vm };
        Program.Render(sheet, 990, 850, Path.Combine(output, "card-artwork-catalog.png"));
        preview.Content = null;
        preview.Close();

        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        var revision = Program.Engine(vm).Revision;
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "card-artwork-hand.png"));
        var hand = (ItemsControl)window.FindName("HandCards");
        foreach (var card in vm.Hand)
        {
            var container = (FrameworkElement)hand.ItemContainerGenerator.ContainerFromItem(card);
            var art = Program.Find<Image>(container).Single(image => image.Name == "CardArtwork");
            Program.Assert(card.Kind is not null && ReferenceEquals(art.Source, CardArt.Get(card.Kind)),
                $"Hand card {card.Name} lost its stable kind/art mapping.");
            Program.Assert(Program.Find<TextBlock>(container).Single(text => text.Name == "CardRank").Text == card.Rank &&
                Program.Find<TextBlock>(container).Single(text => text.Name == "CardSuit").Text == card.SuitGlyph,
                "The face baked in or lost the dealt rank/suit.");
            var button = Program.Find<Button>(container).Single(button => button.Name == "CardButton");
            Program.Assert(button.IsEnabled == card.IsPlayable && ReferenceEquals(button.CommandParameter, card),
                "Artwork changed enabled state or the physical card passed to selection.");
        }
        var slash = vm.Hand.First(card => card.Kind == CardKind.Slash && card.IsPlayable);
        var slashContainer = (FrameworkElement)hand.ItemContainerGenerator.ContainerFromItem(slash);
        var slashButton = Program.Find<Button>(slashContainer).Single(button => button.Name == "CardButton");
        slashButton.Command!.Execute(slashButton.CommandParameter);
        Program.Render(root, 1120, 740, Path.Combine(output, "card-artwork-selected.png"));
        Program.Assert(slash.IsSelected && Program.Find<Border>(slashContainer).Single(border => border.Name == "SelectedCheck").Visibility == Visibility.Visible,
            "The illustrated card did not retain its selection indicator.");
        vm.OpenContextGuideCommand.Execute(null);
        vm.OpenCardGuideCommand.Execute(new GuideHandEntry("铁索连环", string.Empty));
        Program.Render(root, 1120, 740, Path.Combine(output, "card-artwork-guide.png"));
        Program.Assert(vm.IsHelpOpen && vm.SelectedGuideCard?.Kind == CardKind.IronChain &&
            ((Border)window.FindName("PlayerGuideOverlay")).Visibility == Visibility.Visible &&
            Program.Find<Image>(root).Single(image => image.Name == "GuideCardArtwork").ActualHeight > 0 &&
            ReferenceEquals(Program.Find<Image>(root).Single(image => image.Name == "GuideCardArtwork").Source, CardArt.Get(CardKind.IronChain)),
            "Card guide did not bind the selected card's original face.");
        Program.Assert(Program.Engine(vm).Revision == revision && slash.IsSelected,
            "Rendering/selecting artwork or browsing the guide changed the match.");
        window.Content = null;
        window.Close();
    }
}
