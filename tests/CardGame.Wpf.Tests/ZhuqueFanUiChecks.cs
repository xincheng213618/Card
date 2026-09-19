using System.IO;
using System.Windows;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class ZhuqueFanUiChecks
{
    public static void FireSlashConversionFeedback(string output)
    {
        var boundary = ZhuqueFanScenario.FindHumanChainedSlash();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(
            1,
            DateTimeOffset.UtcNow,
            false,
            boundary.BeforeSlash));
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
        var screenshot = Path.Combine(output, "122-zhuque-fan-fire-slash.png");
        Program.Render(root, 1120, 740, screenshot);

        var slash = viewModel.Hand.Single(card => card.Id == boundary.NormalSlashAction.CardId);
        var target = viewModel.Seats.Single(seat => seat.Seat == boundary.PrimaryTargetSeat);
        var engine = Program.Engine(viewModel);
        var before = engine.CreateSnapshot(boundary.SourceSeat, revealAll: true);
        viewModel.SelectCardCommand.Execute(slash);
        viewModel.SelectTargetCommand.Execute(target);
        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.CanPlaySelected &&
                       viewModel.CanPlaySelectedAsSlash &&
                       viewModel.HasAlternateSlash &&
                       viewModel.AlternatePlayText.Contains("火杀", StringComparison.Ordinal),
            "The WPF must expose normal Slash and Zhuque Fire Slash as two explicit actions.");

        viewModel.PlaySelectedAsSlashCommand.Execute(null);
        var converted = engine.Events.Select(item => item.Payload)
            .OfType<ZhuqueFanConvertedEvent>()
            .LastOrDefault(item => item.TargetSeats.SequenceEqual([boundary.PrimaryTargetSeat]));
        var after = engine.CreateSnapshot(boundary.SourceSeat, revealAll: true);
        Program.Assert(converted is not null &&
                       after.Players[boundary.PrimaryTargetSeat].Hp ==
                           before.Players[boundary.PrimaryTargetSeat].Hp - 1 &&
                       after.Players[boundary.ChainedTargetSeat].Hp ==
                           before.Players[boundary.ChainedTargetSeat].Hp - 1 &&
                       viewModel.BattleCues.Any(cue =>
                           cue.Kind == BattleCueKind.Response &&
                           cue.Label.Contains("朱雀羽扇", StringComparison.Ordinal) &&
                           cue.TargetSeats.SequenceEqual([boundary.PrimaryTargetSeat])) &&
                       viewModel.BattleCues.Count(cue => cue.Kind == BattleCueKind.Damage) >= 2 &&
                       !viewModel.HasSelection,
            "The WPF must show Zhuque Fan conversion and both direct and chained Fire damage results.");

        clock.Advance(.2);
        layer.InvalidateVisual();
        Program.Render(root, 1120, 740, screenshot);
        Program.Assert(layer.ActiveEffectCount >= 3,
            "The rendered battle feedback must retain Zhuque Fan and two Fire damage cues.");

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
