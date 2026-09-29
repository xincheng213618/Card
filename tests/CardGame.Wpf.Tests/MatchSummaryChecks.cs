using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class MatchSummaryChecks
{
    public static string VerifyCompleted(MainViewModel vm)
    {
        var engine = Program.Engine(vm);
        var before = SnapshotJson.Serialize(engine.CreateSnapshot(0, true));
        var summary = vm.CompletedMatch ?? throw new InvalidOperationException("Completed game has no result summary.");
        var recorded = vm.MatchHistory.FirstOrDefault(entry => entry.Summary.Players.SequenceEqual(summary.Players));
        var publicState = engine.CreateSnapshot(0, true);
        var expectedOutcome = CardGame.Wpf.Audio.GameSoundRules.Outcome(publicState) switch
        {
            CardGame.Wpf.Audio.GameSound.Victory => MatchOutcome.Win,
            CardGame.Wpf.Audio.GameSound.Defeat => MatchOutcome.Loss,
            _ => MatchOutcome.Draw
        };
        Program.Assert(recorded is not null && recorded.Outcome == expectedOutcome && recorded.Mode == vm.TableModeText,
            "Actual identity/team outcome or mode was not retained in history.");
        Program.Assert(summary.Players.Count == vm.Seats.Count && summary.Players.Count(player => player.IsHuman) == 1,
            "The result omitted players or highlighted the wrong human seat.");
        foreach (var row in summary.Players)
        {
            var seat = vm.Seats.Single(seat => seat.Seat == row.Seat);
            Program.Assert(row.Camp == seat.RoleLabel && row.IsAlive == seat.IsAlive && row.GeneralName == seat.GeneralName,
                "Result rows do not match the final public identity and survival state.");
        }
        var result = JsonSerializer.Serialize(summary);
        vm.NewGameCommand.Execute(null);
        vm.CancelNewGameSetupCommand.Execute(null);
        Program.Assert(result == JsonSerializer.Serialize(vm.CompletedMatch) && before == SnapshotJson.Serialize(engine.CreateSnapshot(0, true)),
            "Canceling rematch changed the completed report or game.");
        return result;
    }

    public static void VerifyControls(MainWindow window)
    {
        var vm = (MainViewModel)window.DataContext;
        var rows = (ItemsControl)window.FindName("MatchResultRows");
        var rematch = (Button)window.FindName("ResultRematchButton");
        var panel = (Border)window.FindName("MatchResultPanel");
        Program.Assert(rows.Items.Count == vm.Seats.Count && rematch.IsEnabled && panel.ActualHeight <= 620,
            "Small-window report or rematch control failed to lay out.");
        var root = (FrameworkElement)window.Content;
        var origin = rematch.TranslatePoint(new Point(0, 0), root);
        Program.Assert(origin.Y >= 0 && origin.Y + rematch.ActualHeight <= root.ActualHeight,
            "The rematch action is outside the visible result panel.");
    }
}
