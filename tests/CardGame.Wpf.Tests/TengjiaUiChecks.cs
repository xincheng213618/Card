using System.IO;
using System.Windows;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class TengjiaUiChecks
{
    public static void FireDamageFeedback(string output)
    {
        var boundary = TengjiaScenario.FindFireSlash();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, boundary.BeforeSlash));
        using var viewModel = new MainViewModel(
            autoAdvance: false,
            seed: boundary.Game.Seed,
            showSetup: true,
            saveStore: store,
            useExpandedContent: true,
            contentRegistry: boundary.Registry)
        {
            IsMotionEnabled = true
        };
        viewModel.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback");
        var clock = new FrameClock();
        layer.Clock = clock;
        var screenshot = Path.Combine(output, "123-tengjia-fire-damage.png");
        Program.Render(root, 1120, 740, screenshot);
        var slash = viewModel.Hand.Single(card => card.Id == boundary.SlashAction.CardId);
        var target = viewModel.Seats.Single(seat => seat.Seat == boundary.TargetSeat);
        var engine = Program.Engine(viewModel);
        var before = engine.CreateSnapshot(boundary.SourceSeat, revealAll: true).Players[boundary.TargetSeat].Hp;
        viewModel.SelectCardCommand.Execute(slash);
        viewModel.SelectTargetCommand.Execute(target);
        viewModel.PlaySelectedCardCommand.Execute(null);
        Program.Assert(engine.CreateSnapshot(boundary.SourceSeat, revealAll: true).Players[boundary.TargetSeat].Hp == before - 2 &&
                       viewModel.BattleCues.Any(cue => cue.Label.Contains("藤甲", StringComparison.Ordinal)) &&
                       viewModel.BattleCues.Any(cue => cue.Kind == BattleCueKind.Damage && cue.Label == "−2"),
            "The WPF must show Tengjia's locked fire increase and the resulting two-point damage.");
        clock.Advance(.2);
        layer.InvalidateVisual();
        Program.Render(root, 1120, 740, screenshot);
        Program.Assert(layer.ActiveEffectCount >= 1, "Tengjia fire feedback must remain visible.");
        window.Content = null;
        window.Close();
    }

    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
