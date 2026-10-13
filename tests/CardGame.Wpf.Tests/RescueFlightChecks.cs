using System.IO;
using System.Windows;
using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class RescueFlightChecks
{
    public static void NativeRecoveryCompletion(string output)
    {
        VerifyNativeRecovery(output, false);
        VerifyNativeRecovery(output, true);
    }

    private static void VerifyNativeRecovery(string output, bool loadAtChild)
    {
        var fixture = StableHandResponseFixture.Create(DecisionKind.RescueDying, output, recoveryChild: true);
        using var vm = fixture.Model;
        vm.IsMotionEnabled = true; vm.IsSoundEnabled = false;
        var window = new MainWindow(vm); var root = (FrameworkElement)window.Content;
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback"); var clock = new FrameClock(); layer.Clock = clock;
        var label = loadAtChild ? "rescue-flight-restored" : "rescue-flight";
        var delivered = new List<BattleCue>();
        var completionSounds = new List<GameSound>();
        vm.SoundsRequested += (_, args) => completionSounds.AddRange(args.Sounds);
        vm.BattleCues.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is not null) delivered.AddRange(args.NewItems.OfType<BattleCue>());
        };
        try
        {
            var game = Engine(vm);
            var peach = vm.Hand.First(card => card.Kind == CardKind.Peach && card.IsPlayable);
            var accepted = game.AcceptedCommands.Count;
            Render(root, 1120, 740, Path.Combine(output, label + "-before.png"));
            vm.SelectCardCommand.Execute(peach); vm.ConfirmSelectedCommand.Execute(null);
            Assert(game.AcceptedCommands.Count == accepted + 1 &&
                game.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Count(e => e.CardId == peach.Id) == 1 &&
                !game.Events.Select(e => e.Payload).OfType<DyingResponseEvent>().Any(e => e.PeachCardId == peach.Id),
                "The actual Peach declaration must suspend on its recovery child before the native dying completion.");
            var first = delivered.Single(cue => cue.PublicCardId == peach.Id && cue.Kind == BattleCueKind.Card);
            clock.Advance(2); layer.InvalidateVisual();
            Render(root, 1120, 740, Path.Combine(output, label + "-declared-before-completion.png"));
            Assert(layer.ActiveEffectCount == 0, "The completed first rescue cue must expire before the later child returns.");
            if (loadAtChild)
            {
                vm.SaveGameCommand.Execute(null); vm.LoadManualGameCommand.Execute(null); game = Engine(vm);
                Assert(!vm.HasSaveError && layer.ActiveEffectCount == 0,
                    "Restoring the actual recovery child must preserve its native parent without replaying a historical card.");
            }
            var child = vm.SkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
            completionSounds.Clear();
            vm.SelectSkillChoiceCommand.Execute(child);
            var views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            clock.Advance(.1); layer.InvalidateVisual();
            Render(root, 1120, 740, Path.Combine(output, label + "-after-completion.png"));
            var completed = game.Events.Select(e => e.Payload).OfType<DyingResponseEvent>().Single(e => e.PeachCardId == peach.Id);
            Assert(game.AcceptedCommands.Count == accepted + 2 && completed.UsedPeach &&
                vm.RecentPlays.Count(play => play.PublicCardId == peach.Id) == 1 && vm.RecentPlays.Single().Sequence == first.Sequence &&
                vm.RecentPlays.Single().PublicCard == first.PublicCard,
                "The actual recovery child must finish once and retain the first committed public face.");
            Console.WriteLine($"Native rescue cue: card={peach.Id}, commands={accepted}->{game.AcceptedCommands.Count}, declaration={first.Sequence}, delivered={string.Join(",", delivered.Where(cue => cue.PublicCardId == peach.Id).Select(cue => $"{cue.Sequence}/{cue.IsUseCompletion}"))}.");
            Assert(delivered.Count(cue => cue.PublicCardId == peach.Id && cue.Kind == BattleCueKind.Card) == 1,
                "A later recovery-child command replayed the already declared rescue card as a second public flight.");
            Assert(!completionSounds.Contains(GameSound.Card) && delivered.Count(cue => cue.Kind == BattleCueKind.Recovery) == 1,
                "Deduplicating the rescue replay must also retire its card sound while preserving the real HP recovery cue.");
            Assert(!Images(VisualTreeHelper.GetDrawing(layer)).Any(image => ReferenceEquals(image.ImageSource, CardArt.Get(CardKind.Peach))) &&
                Enumerable.Range(0, 4).All(seat => views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
                "Rendering a finished rescue redrew the expired card or changed a player-safe snapshot.");
            StableHandResponseFixture.RecordAndReplay(game, fixture.Registry, output, label + "-completed");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static IEnumerable<ImageDrawing> Images(Drawing? drawing)
    {
        if (drawing is ImageDrawing image) yield return image;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children) foreach (var nested in Images(child)) yield return nested;
    }
    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
