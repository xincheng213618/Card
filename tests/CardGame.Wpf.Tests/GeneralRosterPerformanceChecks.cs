using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class GeneralRosterPerformanceChecks
{
    public static void OpenSearchAndPreview(string output)
    {
        using var model = new MainViewModel(false, 31, true, new MemorySaveStore(), useExpandedContent: true,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore(),
            contentRegistry: StandardContentRegistry.CreateWithClassicGenerals())
        { IsMotionEnabled = false, IsMusicEnabled = false, IsSoundEnabled = false };
        model.SelectedStartingRole = model.StartingRoles.Single(role => role.Role == Role.Rebel);
        model.StartNewGameCommand.Execute(null);
        model.ContinueFromIdentityRevealCommand.Execute(null);
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            Program.Render(root, 1440, 900, null);
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            model.ToggleGeneralRosterCommand.Execute(null);
            var commandMs = timer.Elapsed.TotalMilliseconds;
            Program.Render(root, 1440, 900, null);
            timer.Stop();
            var openMs = timer.Elapsed.TotalMilliseconds;
            bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            var roster = (ListBox)window.FindName("CustomGeneralRoster");
            var candidates = (ItemsControl)window.FindName("GeneralCandidateCards");
            var rows = Program.Find<ListBoxItem>(roster).Count();
            var hiddenCards = Program.Find<Button>(candidates).Count();
            var total = model.CustomGeneralChoices.Count;
            var searchTarget = model.CustomGeneralChoices.Last();
            Console.WriteLine($"Custom roster: command {commandMs:F1} ms, open/render {openMs:F1} ms, {rows}/{model.CustomGeneralChoices.Count} rows, {hiddenCards} hidden cards, {bytes:N0} bytes.");
            Program.Assert(total > 300 && rows is > 0 and <= 64 && hiddenCards == 0 && bytes < 12_000_000,
                "A tiled roster must realize only nearby cards and must not generate hidden candidates or the whole roster.");
            var tiles = Program.Find<ListBoxItem>(roster).Take(2).ToArray();
            var firstPosition = tiles[0].TransformToAncestor(roster).Transform(new Point());
            var secondPosition = tiles[1].TransformToAncestor(roster).Transform(new Point());
            Program.Assert(Program.Find<VirtualizingTilePanel>(roster).Count() == 1 &&
                Math.Abs(firstPosition.Y - secondPosition.Y) < 1 && secondPosition.X > firstPosition.X + 100,
                "The actual roster must place portrait cards beside one another in a virtualized tile panel.");
            Program.Render(root, 1440, 900, Path.Combine(output, "custom-general-roster.png"));

            var target = model.CustomGeneralChoices.First();
            var revision = Program.Engine(model).Revision;
            var searchNotifications = 0;
            model.CustomGeneralChoices.CollectionChanged += (_, _) => searchNotifications++;
            timer.Restart();
            model.CustomGeneralSearch = target.Name;
            Program.Render(root, 1440, 900, null);
            timer.Stop();
            var searchMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            model.PreviewGeneralChoiceCommand.Execute(target);
            Program.Render(root, 1440, 900, null);
            timer.Stop();
            var previewMs = timer.Elapsed.TotalMilliseconds;
            Console.WriteLine($"Custom roster search {searchMs:F1} ms ({searchNotifications} collection events), preview {previewMs:F1} ms.");
            Program.Assert(searchNotifications == 1, "Search must publish one complete result instead of one event per row.");
            Program.Assert(model.IsCustomGeneralSelectionOpen && model.SelectedGeneralChoice == target &&
                Program.Engine(model).Revision == revision, "Search and preview must preserve the published private selection.");
            model.CustomGeneralSearch = target.SkillName.Split('/')[0].Trim();
            Program.Assert(model.CustomGeneralChoices.Contains(target), "The roster must search registered skill names.");
            var accepted = Program.Engine(model).AcceptedCommands.Count;
            model.CustomGeneralSearch = searchTarget.GeneralId;
            Program.Render(root, 1120, 740, Path.Combine(output, "custom-general-search-result.png"));
            Program.Assert(model.CustomGeneralChoices.Single() == searchTarget && model.SelectedGeneralChoice == searchTarget &&
                model.CanConfirmGeneralChoice && model.ConfirmGeneralChoiceCommand.CanExecute(null),
                "A search must preview its visible result instead of retaining a hidden general for confirmation.");
            model.CustomGeneralSearch = $" {searchTarget.GeneralId} ";
            Program.Assert(model.SelectedGeneralChoice == searchTarget,
                "An equivalent search must preserve the visible general preview.");
            RefreshView();
            Program.Assert(model.SelectedGeneralChoice?.GeneralId == searchTarget.GeneralId &&
                ReferenceEquals(model.SelectedGeneralChoice, model.CustomGeneralChoices.Single()),
                "A refreshed private prompt must retain the visible search result for confirmation.");
            model.CustomGeneralSearch = "__missing_general_roster__";
            RefreshView();
            Program.Render(root, 1120, 740, Path.Combine(output, "custom-general-search-empty.png"));
            var confirmation = (Button)window.FindName("ConfirmGeneralChoiceButton");
            Program.Assert(model.HasNoCustomGeneralChoices && roster.Items.Count == 0 && model.SelectedGeneralChoice is null &&
                !model.CanConfirmGeneralChoice && !model.ConfirmGeneralChoiceCommand.CanExecute(null) && !confirmation.IsEnabled,
                "An empty search must clear the hidden preview and disable confirmation.");
            model.ConfirmGeneralChoiceCommand.Execute(null);
            Program.Assert(Program.Engine(model).Revision == revision && Program.Engine(model).AcceptedCommands.Count == accepted,
                "Searching and a disabled confirmation must preserve the private prompt without selecting a hidden general.");
            model.CustomGeneralSearch = string.Empty;
            Program.Assert(model.CustomGeneralChoices.Contains(model.SelectedGeneralChoice!) && model.CanConfirmGeneralChoice,
                "Clearing an empty search must restore a visible preview and its confirmation.");
            foreach (var (width, height) in new[] { (1120, 740), (1920, 1080), (1440, 900) })
            {
                Program.Render(root, width, height, Path.Combine(output, $"custom-general-roster-{width}.png"));
                var panel = (FrameworkElement)window.FindName("GeneralSelectionPanel");
                var preview = (FrameworkElement)window.FindName("GeneralSelectionPreviewPortrait");
                var skills = (ScrollViewer)window.FindName("GeneralSelectionSkillsScroller");
                var confirm = (FrameworkElement)window.FindName("ConfirmGeneralChoiceButton");
                Rect Bounds(FrameworkElement control) => control.TransformToAncestor(panel).TransformBounds(new Rect(control.RenderSize));
                Program.Assert(roster.ActualWidth > 350 && skills.ActualWidth > 265 && skills.ActualHeight > 50 &&
                    new Rect(panel.RenderSize).Contains(Bounds(preview)) && new Rect(panel.RenderSize).Contains(Bounds(confirm)) &&
                    Program.Find<ListBoxItem>(roster).Count() is > 0 and <= 64,
                    "The real compact and wide layouts must retain usable skills, portrait and confirmation without expanding the full roster.");
            }
            roster.ScrollIntoView(model.CustomGeneralChoices.Last());
            Program.Render(root, 1440, 900, null);
            var tilePanel = Program.Find<VirtualizingTilePanel>(roster).Single();
            Console.WriteLine($"Tile tail: offset={tilePanel.VerticalOffset:F1}, extent={tilePanel.ExtentHeight:F1}, viewport={tilePanel.ViewportHeight:F1}, realized={Program.Find<ListBoxItem>(roster).Count()}, last={roster.ItemContainerGenerator.ContainerFromItem(model.CustomGeneralChoices.Last()) is not null}.");
            Program.Assert(Program.Find<ListBoxItem>(roster).Any(row => ReferenceEquals(row.DataContext, model.CustomGeneralChoices.Last())) &&
                Program.Find<ListBoxItem>(roster).Count() <= 64, "The final general must remain reachable through virtualized scrolling.");
            var lastTile = (ListBoxItem)roster.ItemContainerGenerator.ContainerFromItem(model.CustomGeneralChoices.Last());
            var lastBounds = lastTile.TransformToAncestor(roster).TransformBounds(new Rect(lastTile.RenderSize));
            Program.Assert(lastBounds.Top >= -1 && lastBounds.Bottom <= roster.ActualHeight + 1,
                "Scrolling to the final tile must bring the entire card into the viewport after responsive reflow.");
            roster.ScrollIntoView(model.CustomGeneralChoices.First());
            Program.Render(root, 1440, 900, null);
            Program.Assert(Program.Find<ListBoxItem>(roster).Any(tile => ReferenceEquals(tile.DataContext, model.CustomGeneralChoices.First())) &&
                Program.Find<Button>(roster).Where(button => ReferenceEquals(button.Command, model.PreviewGeneralChoiceCommand))
                    .All(button => ReferenceEquals(button.DataContext, button.CommandParameter)),
                "Scrolling back must reattach recycled tiles and bind their preview commands to the current general.");
            var retired = model.CustomGeneralChoices.First();
            model.ToggleGeneralRosterCommand.Execute(null);
            Program.Render(root, 1440, 900, null);
            var previewBeforeStaleInput = model.SelectedGeneralChoice;
            model.PreviewGeneralChoiceCommand.Execute(retired);
            Program.Assert(ReferenceEquals(model.SelectedGeneralChoice, previewBeforeStaleInput) &&
                candidates.Items.Count is > 0 and < 20, "Returning to candidates must retire the roster's old input objects.");
            timer.Restart();
            model.ToggleGeneralRosterCommand.Execute(null);
            Program.Render(root, 1440, 900, null);
            timer.Stop();
            var reopenMs = timer.Elapsed.TotalMilliseconds;
            Console.WriteLine($"Custom roster reopen {reopenMs:F1} ms.");
            File.WriteAllText(Path.Combine(output, "custom-general-performance.json"),
                System.Text.Json.JsonSerializer.Serialize(new { commandMs, openMs, searchMs, previewMs, reopenMs, rows,
                    total, hiddenCards, managedBytes = bytes }));

            void RefreshView() => typeof(MainViewModel).GetMethod("Refresh",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(model, [Program.Engine(model).CreateSnapshot(0)]);
        }
        finally { window.Content = null; window.Close(); }
    }
}
