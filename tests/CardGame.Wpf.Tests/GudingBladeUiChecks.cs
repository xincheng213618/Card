using System.IO;
using System.Windows;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class GudingBladeUiChecks
{
    public static void LockedDamageFeedback(string output)
    {
        var boundary = GudingBladeScenario.FindHumanSlash(requireEmptyTarget: true);
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
        var screenshot = Path.Combine(output, "121-guding-blade-damage.png");
        Program.Render(root, 1120, 740, screenshot);
        var slash = viewModel.Hand.Single(card => card.Id == boundary.SlashAction.CardId);
        var target = viewModel.Seats.Single(seat => seat.Seat == boundary.TargetSeat);
        var engine = Program.Engine(viewModel);
        var hpBefore = engine.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat].Hp;

        Program.Assert(!viewModel.HasSaveError && slash.IsPlayable && target.HandCount == 0,
            "The WPF must restore an exact Guding Blade Slash against a publicly empty hand.");
        viewModel.SelectCardCommand.Execute(slash);
        viewModel.SelectTargetCommand.Execute(target);
        viewModel.PlaySelectedCardCommand.Execute(null);

        var increased = engine.Events.Select(item => item.Payload)
            .OfType<GudingBladeDamageIncreasedEvent>()
            .LastOrDefault(item => item.TargetSeat == boundary.TargetSeat);
        var hpAfter = engine.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat].Hp;
        Program.Assert(increased is { BaseAmount: 1, ModifiedAmount: 2 } &&
                       hpAfter == hpBefore - 2 &&
                       viewModel.BattleCues.Any(cue =>
                           cue.Kind == BattleCueKind.Response &&
                           cue.Label.Contains("古锭刀", StringComparison.Ordinal) &&
                           cue.TargetSeats.SequenceEqual([boundary.TargetSeat])) &&
                       viewModel.BattleCues.Any(cue =>
                           cue.Kind == BattleCueKind.Damage && cue.Label == "−2") &&
                       !viewModel.HasSelection,
            "Guding Blade must show its locked +1 cue before the public two-point damage result.");

        clock.Advance(.2);
        layer.InvalidateVisual();
        Program.Render(root, 1120, 740, screenshot);
        Program.Assert(layer.ActiveEffectCount >= 2,
            "The rendered battle feedback must keep the Guding Blade and two-point damage cues active.");

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
