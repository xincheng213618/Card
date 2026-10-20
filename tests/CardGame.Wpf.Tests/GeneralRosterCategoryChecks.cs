using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class GeneralRosterCategoryChecks
{
    public static void SeriesSearchAndModeBoundaries(string output)
    {
        using var model = new MainViewModel(false, 31, true, new MemorySaveStore(), useExpandedContent: true,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore(),
            contentRegistry: StandardContentRegistry.CreateWithClassicGenerals())
        { IsMotionEnabled = false, IsMusicEnabled = false, IsSoundEnabled = false };
        model.SelectedStartingRole = model.StartingRoles.Single(role => role.Role == Role.Lord);
        model.StartNewGameCommand.Execute(null);
        model.ContinueFromIdentityRevealCommand.Execute(null);
        model.ToggleGeneralRosterCommand.Execute(null);
        var all = model.CustomGeneralChoices.ToArray();
        var game = Program.Engine(model);
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0));
        var accepted = game.AcceptedCommands.Count;
        var categories = model.CustomGeneralCategories.ToArray();
        Program.Assert(categories.Single(category => category.Id == "all").Count == all.Length &&
            categories.Where(category => category.Id != "all").Sum(category => category.Count) == all.Length &&
            new[] { "boundary", "mou", "magic", "god", "le", "wumiao" }.All(id => categories.Any(category => category.Id == id)),
            "Every currently eligible edition belongs to one counted category, including separate magic and god categories.");
        Program.Assert(GeneralRosterCatalog.Classify("boundary:ju-shou") == "boundary" &&
            GeneralRosterCatalog.Classify("classic:shen-sima-yi") == "god" &&
            GeneralRosterCatalog.Classify("ol:shen-sima-yi") == "god" &&
            GeneralRosterCatalog.Classify("magic:guan-yu") == "magic" &&
            GeneralRosterCatalog.Classify("le:xiao-qiao") == "le",
            "Broad categories merge both boundary packs and keep formal magic, god and new Le editions distinct.");
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            foreach (var category in categories)
            {
                model.SelectCustomGeneralCategoryCommand.Execute(category.Id);
                Program.Assert(model.CustomGeneralChoices.Select(choice => choice.GeneralId).ToHashSet().SetEquals(
                        all.Where(choice => category.Id == "all" || choice.RosterCategoryId == category.Id).Select(choice => choice.GeneralId)) &&
                    model.CustomGeneralChoices.Count == category.Count,
                    "A category contains exactly its current prompt's editions without broadening the mode roster.");
            }
            model.SelectCustomGeneralCategoryCommand.Execute("boundary");
            var target = model.CustomGeneralChoices.Single(choice => choice.GeneralId == "boundary:sima-yi");
            model.CustomGeneralSearch = target.Name;
            Program.Assert(model.CustomGeneralChoices.Contains(target) &&
                model.CustomGeneralChoices.All(choice => choice.RosterCategoryId == "boundary" && choice.Name.Contains(target.Name)) &&
                model.CustomGeneralCategories.SequenceEqual(categories),
                "Search narrows the selected series while category totals retain the full eligible roster.");
            model.CustomGeneralSearch = "__missing_series_general__";
            Program.Assert(model.HasNoCustomGeneralChoices, "An empty series search exposes its empty state.");
            model.CustomGeneralSearch = string.Empty;
            foreach (var (width, height) in new[] { (1120, 740), (1440, 900), (1920, 1080) })
            {
                Program.Render(root, width, height, Path.Combine(output, $"custom-general-series-{width}.png"));
                var tabs = (ItemsControl)window.FindName("CustomGeneralCategoryTabs");
                var roster = (ListBox)window.FindName("CustomGeneralRoster");
                var confirm = (FrameworkElement)window.FindName("ConfirmGeneralChoiceButton");
                var panel = (FrameworkElement)window.FindName("GeneralSelectionPanel");
                var buttons = Program.Find<Button>(tabs).ToArray();
                var selected = buttons.Single(button => Equals(button.CommandParameter, "boundary"));
                Program.Assert(buttons.Length == categories.Length &&
                    selected.Tag is true && selected.Background.ToString() != buttons.First().Background.ToString() &&
                    buttons.All(button => button.Visibility == Visibility.Visible && button.IsEnabled && button.TransformToAncestor(tabs)
                        .TransformBounds(new Rect(button.RenderSize)).Right <= tabs.ActualWidth + 1) &&
                    roster.ActualHeight > 120 && Program.Find<ListBoxItem>(roster).Count() is > 0 and <= 64 &&
                    new Rect(panel.RenderSize).Contains(confirm.TransformToAncestor(panel).TransformBounds(new Rect(confirm.RenderSize))),
                    "Wrapped category buttons, selected styling, virtualized results and confirmation fit compact and wide layouts.");
            }
            Program.Assert(before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && game.AcceptedCommands.Count == accepted,
                "Browsing categories, searching and previewing must not submit commands or change the published private prompt.");
            model.SelectCustomGeneralCategoryCommand.Execute("__unknown_category__");
            Program.Assert(model.SelectedCustomGeneralCategory == "boundary", "Unknown category input cannot retire current results.");
            model.ToggleGeneralRosterCommand.Execute(null);
            model.ToggleGeneralRosterCommand.Execute(null);
            Program.Assert(model.SelectedCustomGeneralCategory == "boundary" && model.CustomGeneralChoices.All(choice => choice.RosterCategoryId == "boundary"),
                "Returning from candidates preserves the current series for the same mode.");
            model.SelectedTableMode = model.TableModes.Single(mode => mode.ModeId == "identity:classic-5");
            model.StartNewGameCommand.Execute(null);
            model.ContinueFromIdentityRevealCommand.Execute(null);
            model.ToggleGeneralRosterCommand.Execute(null);
            Program.Assert(model.SelectedCustomGeneralCategory == "all" &&
                model.CustomGeneralCategories.All(category => category.Id is "all" or "standard" or "myth") &&
                model.CustomGeneralChoices.All(choice => choice.RosterCategoryId is "standard" or "myth"),
                "Switching to the five-player mode drops unavailable categories and restores its own eligible roster.");
        }
        finally { window.Content = null; window.Close(); }
    }
}
