using System.Reflection;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TableOrderUiChecks
{
    internal static void MovedPositionsUpdatePanelsAndNumbers(string output)
    {
        using var model = new MainViewModel(false, 31, true, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsDeveloperView = false, IsNewGameSetupOpen = false };
        model.SelectGeneralChoiceCommand.Execute(model.GeneralChoices[0]);
        model.ConfirmGeneralChoiceCommand.Execute(null);
        var game = Program.Engine(model);
        var before = game.CreateSnapshot(0);
        var order = Enumerable.Range(0, before.Players.Count).ToList();
        order.Remove(1);
        order.Insert(order.IndexOf(3) + 1, 1);
        var moved = before with { TableOrder = Array.AsReadOnly(order.ToArray()) };
        typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, [moved]);
        var origin = model.HumanPlayer?.Seat ?? 0;
        var originIndex = order.IndexOf(origin);
        var clockwise = Enumerable.Range(1, order.Count - 1).Select(offset => order[(originIndex + offset) % order.Count]).ToArray();
        var shown = model.RightSeats.Reverse().Concat(model.TopSeats.Reverse()).Concat(model.LeftSeats).Select(seat => seat.Seat);
        Program.Assert(shown.SequenceEqual(clockwise), "Table panels must follow the published moved order while preserving participant identities.");
        var lord = before.Players.FirstOrDefault(player => player.Role == CardGame.Core.Role.Lord)?.Seat ?? 0;
        Program.Assert(model.Seats.All(seat => seat.DisplaySeatNumber ==
            (order.IndexOf(seat.Seat) - order.IndexOf(lord) + order.Count) % order.Count + 1),
            "Displayed seat numbers must follow the public ring relative to the lord.");
        Program.Assert(game.Revision == before.Revision && game.CreateSnapshot(0).TableOrder!.SequenceEqual(Enumerable.Range(0, order.Count)),
            "Applying a presentation view must not mutate gameplay or manufacture a seat-movement fact.");
        var panel = new CardGame.Wpf.MainWindow(model);
        try { Program.Render((System.Windows.FrameworkElement)panel.Content, 1120, 740, System.IO.Path.Combine(output, "moved-table-order.png")); }
        finally { panel.Content = null; panel.Close(); }
    }
}
