using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TableInteractionChecks
{
    public static void SelectionCost(string output)
    {
        using var vm = new MainViewModel(false, 1, false, new MemorySaveStore());
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        // Use the stable base catalogue for presentation costs; expanded-pool behavior has separate coverage.
        for (var step = 0; step < 8 && vm.SkillChoices.Count > 0; step++)
        {
            vm.SelectSkillChoiceCommand.Execute(vm.SkillChoices.Last());
            Program.AdvanceToDecision(vm);
        }
        var card = vm.Hand.First(card => card.IsPlayable);
        var engine = Program.Engine(vm);
        var beforeRevision = engine.Revision;
        for (var i = 0; i < 3; i++) { vm.SelectCardCommand.Execute(card); vm.ClearSelectionCommand.Execute(null); }
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 30; i++) { vm.SelectCardCommand.Execute(card); vm.ClearSelectionCommand.Execute(null); }
        timer.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var result = $"30 select/cancel pairs: {timer.Elapsed.TotalMilliseconds:F1} ms; {allocated:N0} bytes";
        Console.WriteLine(result);
        File.WriteAllText(Path.Combine(output, "table-interaction-cost.txt"), result);
        Program.Assert(allocated < 4_000_000 && engine.Revision == beforeRevision,
            "Pure selection rebuilt expensive rules queries or submitted a command.");
        Program.Assert(vm.TopSeats.Select(seat => seat.Seat).SequenceEqual([5, 4, 3]) &&
                       vm.LeftSeats.Select(seat => seat.Seat).SequenceEqual([6, 7]) &&
                       vm.RightSeats.Select(seat => seat.Seat).SequenceEqual([2, 1]),
            "Eight-player seats must follow turn order around the table with no duplicated seats.");
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        foreach (var (width, height) in new[] { (1120, 740), (1440, 860), (1920, 1080) })
        {
            Program.Render(root, width, height, Path.Combine(output, $"table-reference-play-{width}.png"));
            var rings = new[] { "TopSeatRing", "LeftSeatRing", "RightSeatRing" }
                .Select(name => (ItemsControl)window.FindName(name)).ToArray();
            var bounds = rings.SelectMany(ring => Program.Find<Button>(ring))
                .Where(button => button.DataContext is SeatViewModel)
                .Select(button => button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize))).ToArray();
            Program.Assert(bounds.Length == 7 && bounds.All(rect => rect.Width >= 80 && rect.Height >= 105) &&
                           bounds.All(a => bounds.Count(b => a.IntersectsWith(b)) == 1),
                "Ring portraits overlap or become unreadable at a supported viewport.");
        }

        var query = typeof(MainViewModel).GetMethod("GetViewLegalActions", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var cached = query.Invoke(vm, null);
        vm.SelectCardCommand.Execute(card);
        var cancel = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent, Source = window.FindName("HandViewport")
        };
        window.RaiseEvent(cancel);
        Program.Assert(cancel.Handled && !vm.HasSelection && engine.Revision == beforeRevision,
            "Right-clicking the hand surface must cancel without submitting a command.");
        var seats = vm.Seats.ToArray();
        var logReset = false;
        vm.FilteredBattleLog.CollectionChanged += (_, e) => logReset |= e.Action == NotifyCollectionChangedAction.Reset;
        var equip = engine.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Equip);
        var equipment = vm.Hand.Single(item => item.Id == equip.CardId);
        vm.SelectCardCommand.Execute(equipment);
        var quickConfirm = typeof(MainWindow).GetMethod("TryQuickConfirm", BindingFlags.Instance | BindingFlags.NonPublic)!;
        vm.IsHelpOpen = true;
        Program.Assert(quickConfirm.Invoke(window, [equipment]) is false && engine.Revision == beforeRevision,
            "Quick confirmation bypassed an open modal.");
        vm.IsHelpOpen = false;
        Program.Assert(quickConfirm.Invoke(window, [equipment]) is true && engine.Revision == beforeRevision + 1 &&
                       engine.CreateSnapshot(0).Players[0].Equipment.Any(item => item.Id == equipment.Id),
            "Quick confirmation must use the selected legal equipment exactly once.");
        Program.Assert(quickConfirm.Invoke(window, [equipment]) is false && !logReset &&
                       vm.Seats.Zip(seats).All(pair => ReferenceEquals(pair.First, pair.Second)) &&
                       !ReferenceEquals(cached, query.Invoke(vm, null)),
            "A command reused stale actions, rebuilt the public log or replaced the seat containers.");
        vm.IsDeveloperView = true;
        vm.IsDeveloperView = false;
        var permitted = engine.CreateSnapshot(0);
        Program.Assert(vm.Seats.All(seat => permitted.Players[seat.Seat].Role is not null || seat.RoleLabel == "?"),
            "Reusing seat instances retained a hidden role from the developer view.");
        vm.NewGameCommand.Execute(null);
        vm.StartNewGameCommand.Execute(null);
        Program.Assert(!ReferenceEquals(engine, Program.Engine(vm)) &&
                       ((IReadOnlyList<LegalAction>)query.Invoke(vm, null)!).Count == 0,
            "A new match reused the previous engine's cached actions.");
        window.Content = null;
        window.Close();
    }
}
