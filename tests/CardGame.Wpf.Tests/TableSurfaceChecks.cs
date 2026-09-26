using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TableSurfaceChecks
{
    public static void EquipmentAndSkillControls(string output)
    {
        foreach (var kind in CardCatalog.ImplementedCards.Select(card => card.Kind).Where(EquipmentCatalog.IsEquipment))
            Program.Assert(CardArt.GetEquipment(kind) is BitmapSource { PixelWidth: 284, PixelHeight: 50 }, $"Missing equipment strip: {kind}.");
        foreach (var suit in new[] { "♥", "♦", "♠", "♣" })
        foreach (var rank in new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" })
            Program.Assert(CardArt.GetRank(rank, suit) is BitmapSource { PixelWidth: 38 } && CardArt.GetSuit(suit) is BitmapSource { PixelHeight: 34 },
                $"Missing original rank or suit sprite: {suit}{rank}.");

        using var vm = FindEquipmentHand();
        var engine = Program.Engine(vm);
        var action = engine.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Equip);
        var card = vm.Hand.Single(card => card.Id == action.CardId);
        vm.SelectCardCommand.Execute(card);
        vm.PlaySelectedCardCommand.Execute(null);
        Program.AdvanceToDecision(vm);
        var actual = engine.CreateSnapshot(0, false).Players[0].Equipment.Single(equipment => equipment.Id == card.Id);
        var slot = vm.HumanEquipmentSlots.Single(slot => slot.Card?.Id == actual.Id);
        Program.Assert(slot.Tooltip.Contains(actual.DisplayName) && ReferenceEquals(slot.Artwork, CardArt.GetEquipment(actual.Kind)) &&
            ReferenceEquals(slot.RankArtwork, card.RankArtwork) && ReferenceEquals(slot.SuitArtwork, card.SuitArtwork),
            "An equipped physical card lost its identity or original rank/suit mapping.");
        Program.Assert(vm.HumanEquipmentSlots.Count == 5 && vm.HumanEquipmentSlots.Count(slot => slot.IsOccupied) == 1,
            "Empty slots must not show abolished or occupied equipment.");
        var revision = engine.Revision;
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        foreach (var size in new[] { (1120, 740), (1440, 860), (1920, 1080) })
        {
            Program.Render(root, size.Item1, size.Item2, Path.Combine(output, $"table-equipment-{size.Item1}.png"));
            Rect Bounds(string name)
            {
                var element = (FrameworkElement)window.FindName(name);
                return element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
            }
            var equip = Bounds("EquipmentRail");
            var hand = Bounds("HandViewport");
            var skill = Bounds("HumanSkillPanel");
            var portrait = Bounds("HumanPortrait");
            var sidebar = Bounds("TableSidebar");
            Program.Assert(equip.Right <= hand.Left + 1 && hand.Right <= skill.Left + 1 && skill.Right <= portrait.Left + 1,
                "Equipment, hand, skill buttons and portrait overlap.");
            Program.Assert(portrait.Right <= size.Item1 && hand.Bottom <= size.Item2 && sidebar.Bottom <= Bounds("HandDock").Top,
                "The new table layout clips persistent controls.");
        }
        Program.Assert(engine.Revision == revision, "Resizing or inspecting equipment changed the game.");
        window.Content = null;
        window.Close();

        using var active = ActiveSkillChecks.CreateShowcase(721019);
        active.SelectGeneralChoiceCommand.Execute(active.GeneralChoices.Single(choice => choice.SkillName == "苦肉"));
        Program.AdvanceToDecision(active);
        var activeWindow = new MainWindow(active);
        var activeRoot = (FrameworkElement)activeWindow.Content;
        Program.Render(activeRoot, 1120, 740, Path.Combine(output, "table-skill-available.png"));
        var button = Program.Find<Button>(activeRoot).Single(button => button.DataContext is HumanSkillViewModel { Name: "苦肉" });
        var before = Program.Engine(active).CreateSnapshot(0, false).Players[0];
        var beforeRevision = Program.Engine(active).Revision;
        Program.Assert(button.IsEnabled && button.Command is not null, "The skill chip is not an actionable control.");
        button.Command!.Execute(button.CommandParameter);
        var after = Program.Engine(active).CreateSnapshot(0, false).Players[0];
        Program.Assert(Program.Engine(active).Revision == beforeRevision + 1 && after.Hp == before.Hp - 1 && after.Hand.Count == before.Hand.Count + 2,
            "Clicking the skill chip did not use the existing legal action exactly once.");
        activeWindow.Content = null;
        activeWindow.Close();
    }

    private static MainViewModel FindEquipmentHand()
    {
        for (var seed = 1; seed <= 50; seed++)
        {
            var vm = new MainViewModel(false, seed, false, new MemorySaveStore(), useExpandedContent: true);
            vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
            Program.AdvanceToDecision(vm);
            if (Program.Engine(vm).GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Equip)) return vm;
            vm.Dispose();
        }
        throw new InvalidOperationException("No real equipment opening hand found.");
    }
}
