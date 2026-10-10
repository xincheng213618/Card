using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        const int previewColumns = 9;
        const int previewCellWidth = 110;
        const int previewCellHeight = 170;
        const int previewCellPadding = 4;
        var sheet = new WrapPanel { Width = previewColumns * previewCellWidth, Background = System.Windows.Media.Brushes.DarkSlateGray };
        var faces = new Dictionary<CardKind, ContentControl>();
        var cells = new Dictionary<CardKind, Border>();
        var index = 0;
        foreach (var definition in definitions)
        {
            var card = new CardViewModel
            {
                Id = index++, Kind = definition.Kind, Name = definition.DisplayName,
                KindLabel = definition.CategoryName, SuitGlyph = index % 2 == 0 ? "♦" : "♠", Rank = "Q",
                Description = definition.Description, IsPlayable = true
            };
            var face = new ContentControl
            {
                Content = card,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                ContentTemplate = (DataTemplate)Application.Current.FindResource("CardTemplate")
            };
            // Measure the real template at its natural size, then fit it into a padded cell.
            // Its fixed card border and top selection margin must not overflow adjacent cells.
            var cell = new Border
            {
                Width = previewCellWidth, Height = previewCellHeight,
                Padding = new Thickness(previewCellPadding),
                Child = new Viewbox { Stretch = Stretch.Uniform, Child = face }
            };
            faces.Add(definition.Kind, face);
            cells.Add(definition.Kind, cell);
            sheet.Children.Add(cell);
        }
        // A Window provides the same command ancestor as the live hand.
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore()) { IsMotionEnabled = false };
        var preview = new Window { Content = sheet, DataContext = vm };
        Program.Render(sheet, previewColumns * previewCellWidth,
            ((definitions.Length + previewColumns - 1) / previewColumns) * previewCellHeight,
            Path.Combine(output, "card-artwork-catalog.png"));
        foreach (var (kind, face) in faces)
        {
            var cell = cells[kind];
            var cellBounds = cell.TransformToAncestor(sheet).TransformBounds(new Rect(cell.RenderSize));
            var contentBounds = cell.TransformToAncestor(sheet).TransformBounds(new Rect(
                previewCellPadding, previewCellPadding,
                cell.ActualWidth - 2 * previewCellPadding, cell.ActualHeight - 2 * previewCellPadding));
            var artwork = Program.Find<Image>(face).Single(image => image.Name == "CardArtwork");
            var artworkBounds = artwork.TransformToAncestor(sheet).TransformBounds(new Rect(artwork.RenderSize));
            const double tolerance = 0.1;
            bool Fits(Rect bounds) => bounds.Width > 0 && bounds.Height > 0 &&
                bounds.Left >= contentBounds.Left - tolerance && bounds.Right <= contentBounds.Right + tolerance &&
                bounds.Top >= contentBounds.Top - tolerance && bounds.Bottom <= contentBounds.Bottom + tolerance;
            Program.Assert(cellBounds.Left >= -tolerance && cellBounds.Right <= sheet.ActualWidth + tolerance &&
                cellBounds.Top >= -tolerance && cellBounds.Bottom <= sheet.ActualHeight + tolerance && Fits(artworkBounds),
                $"Gallery face {kind} must fit its padded cell and the complete output sheet.");
            foreach (var name in Program.Find<TextBlock>(face).Where(text => text.Name == "RuntimeCardName" && text.Visibility == Visibility.Visible))
                Program.Assert(Fits(name.TransformToAncestor(sheet).TransformBounds(new Rect(name.RenderSize))),
                    $"Gallery runtime name for {kind} must remain inside its padded cell, including the final row.");
        }
        var scarletFace = faces[CardKind.ScarletBloodSword];
        var scarletName = Program.Find<TextBlock>(scarletFace).Single(text => text.Name == "RuntimeCardName");
        var scarletCanvas = (FrameworkElement)Program.Find<Image>(scarletFace).Single(image => image.Name == "CardArtwork").Parent;
        var scarletNameBounds = scarletName.TransformToAncestor(scarletCanvas).TransformBounds(new Rect(scarletName.RenderSize));
        Program.Assert(((CardViewModel)scarletFace.Content).HasDynamicWeaponName &&
            scarletName.Text == CardCatalog.Get(CardKind.ScarletBloodSword).DisplayName &&
            scarletName.Visibility == Visibility.Visible && scarletName.ActualWidth > 0 && scarletName.ActualHeight > 0 &&
            scarletNameBounds.Top >= 223 && scarletNameBounds.Bottom <= 260,
            "The official ScarletBloodSword illustration must retain its visible runtime card name.");
        var xingtianName = Program.Find<TextBlock>(faces[CardKind.XingtianAxe]).Single(text => text.Name == "RuntimeCardName");
        Program.Assert(!((CardViewModel)faces[CardKind.XingtianAxe].Content).HasDynamicWeaponName &&
            xingtianName.Visibility == Visibility.Collapsed,
            "The official Xingtian complete face must not receive a duplicate runtime name.");
        preview.Content = null;
        preview.Close();

        Program.StartLordFixture(vm);
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
