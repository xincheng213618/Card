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
    public static void Aggregation()
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore());
        var active = Program.Engine(vm).CreateSnapshot(0, true);
        var completed = active with { Status = EngineStatus.Completed };
        long sequence = 0;
        EventEnvelope Event(IGameEvent payload) => new(new EventId(++sequence), null, sequence, 1, "result-check", payload);
        var events = new[]
        {
            Event(new CardUseDeclaredEvent(1, 123, CardKind.ArrowBarrage, 0)),
            Event(new GroupCardUsedEvent(1, 123, CardKind.ArrowBarrage, 0, [1, 2])),
            Event(new CardUsedEvent(123, CardKind.ArrowBarrage, 0, 1)),
            Event(new CardRespondedEvent(456, 1, 0, CardKind.Dodge)),
            Event(new DuelResponseEvent(1, 1, true, 456, CardKind.Slash)),
            Event(new DamageAppliedEvent(0, 1, 2, 0)),
            Event(new DamageAppliedEvent(0, 2, 1, 2)),
            Event(new DamageAppliedEvent(-1, 0, 1, 3)),
            Event(new SkillHpLostEvent(2, 0, SkillKind.Kujin, 2, 1)),
            Event(new RecoveryAppliedEvent(0, 0, 1, 2)),
            Event(new RecoveryAppliedEvent(0, 2, 2, 4)),
            Event(new DyingResponseEvent(3, 0, true, 111) { UsedPeachPhysicalCardKind = CardKind.Dodge }),
            Event(new DyingResponseEvent(4, 0, false, null, true, 112)),
            Event(new DyingResponseEvent(5, 0, false, null)),
            Event(new PlayerDiedEvent(1, 0)),
            Event(new PlayerDiedEvent(0, 0)),
            Event(new PlayerDiedEvent(3, null)),
            Event(new GeneralSelectedEvent(2, "private-general-secret")),
            Event(new CardMovedEvent(987654, CardKind.Slash, CardLocation.DrawPile, CardLocation.Hand(2), CardMoveReasons.Draw))
        };
        Program.Assert(MatchSummary.Create(active, events) is null && vm.CompletedMatch is null, "An ongoing game exposed a result table.");
        var summary = MatchSummary.Create(completed, events)!;
        var human = summary.Players.Single(player => player.Seat == 0);
        Program.Assert(human is { CardsUsed: 1, Responses: 0, DamageDealt: 3, DamageTaken: 1, Recovery: 3, RescueCards: 2, Defeats: 1 },
            "Totals double-counted effect notifications, skill HP costs, self-death or declined rescue.");
        Program.Assert(summary.Players.Single(player => player.Seat == 1) is { Responses: 1, DamageTaken: 2 }, "Response and damage totals are not per player.");
        var json = JsonSerializer.Serialize(summary);
        Program.Assert(!json.Contains("987654") && !json.Contains("private-general-secret") && !json.Contains("CardId") && !json.Contains("Hand"),
            "The summary retained private payloads or physical card identifiers.");
        var teamView = completed with { Players = completed.Players.Select(player => player with { TeamId = player.Seat % 2 == 0 ? "team:blue" : "team:red" }).ToArray() };
        Program.Assert(MatchSummary.Create(teamView, events)!.Players.Select(player => player.Camp).Distinct().Order().SequenceEqual(new[] { "赤队", "青队" }.Order()),
            "Team results used hidden identity camps.");
    }

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
