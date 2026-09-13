using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using CardGame.Wpf.Audio;
using System.Text.Json;
using static Program;

internal static class FeedbackChecks
{
    public static void PublicProjection()
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore());
        var view = Engine(vm).CreateSnapshot(0);
        var sequence = 0L;
        EventEnvelope Envelope(IGameEvent payload) => new(new EventId(++sequence), null, sequence, 1, "feedback-check", payload);
        var privateEvents = new[]
        {
            Envelope(new GeneralSelectionRequestedEvent(1, ["private-general"])),
            Envelope(new GeneralSelectedEvent(1, "private-general")),
            Envelope(new CardMovedEvent(9876, CardKind.Peach, CardLocation.DrawPile,
                new CardLocation(CardZoneKind.Hand, 1), CardMoveReasons.Draw))
        };
        Assert(BattleCueProjector.Project(privateEvents, view).Count == 0, "Hidden setup or hand movements leaked into feedback.");
        int[] targets = [1, 1, -1, 99];
        var publicEvents = new[]
        {
            Envelope(new CardUseDeclaredEvent(7, 2468, CardKind.Slash, 0)),
            Envelope(new TargetsConfirmedEvent(7, targets)),
            Envelope(new CardRespondedEvent(1357, 1, 0, CardKind.Slash)),
            Envelope(new DuelResponseEvent(7, 1, true, 1357, CardKind.Slash)),
            Envelope(new DamageAppliedEvent(0, 1, 2, 1, DamageNature.Fire)),
            Envelope(new RecoveryAppliedEvent(2, 1, 1, 2)),
            Envelope(new DamageAppliedEvent(0, 1, 0, 2))
        };
        var cues = BattleCueProjector.Project(publicEvents, view);
        targets[0] = 4;
        Assert(cues.Count == 4 && cues.Select(cue => cue.Sequence).Distinct().Count() == 4, "Public response duplicated or zero damage rendered.");
        Assert(cues[0].Label == "杀" && cues[0].TargetSeats.SequenceEqual([1]), "Declared card or detached public targets differ.");
        Assert(cues[1].Label == "打出杀" && cues[2].Label == "−2" && cues[2].Nature == DamageNature.Fire && cues[3].Label == "+1", "Response, damage, or recovery feedback differs from committed events.");
    }

    public static void HandAndPreferences(string output)
    {
        var store = new MemorySaveStore();
        using (var startup = new MainViewModel(false, 721019, true, store))
        {
            startup.IsMotionEnabled = !startup.IsMotionEnabled;
            startup.IsAutoAdvance = true;
        }
        Assert(store.WriteCount == 0, "Changing startup preferences overwrote the previous match.");
        using var vm = new MainViewModel(false, 721019, false, store) { IsMotionEnabled = false };
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        AdvanceToDecision(vm);
        vm.SortHandCommand.Execute(null);
        Render(root, 1120, 740, Path.Combine(output, "13-retained-hand.png"));
        var hand = (ItemsControl)window.FindName("HandCards");
        var cards = vm.Hand.ToArray();
        var containers = cards.ToDictionary(card => card.Id, card => hand.ItemContainerGenerator.ContainerFromItem(card));
        Assert(containers.Values.All(container => container is not null), "Hand controls did not materialize.");
        vm.IsDeveloperView = true;
        vm.IsDeveloperView = false;
        Assert(vm.Hand.SequenceEqual(cards), "Refreshing reset the arranged hand.");
        vm.EndTurnCommand.Execute(null);
        AdvanceToDecision(vm);
        Assert(vm.IsDiscardSelectionPending, "Fixture did not enter discard.");
        Render(root, 1120, 740, Path.Combine(output, "13-retained-hand.png"));
        Assert(vm.Hand.SequenceEqual(cards) && cards.All(card => ReferenceEquals(containers[card.Id], hand.ItemContainerGenerator.ContainerFromItem(card))), "Entering discard recreated hand controls or reset their order.");
        ResolveDiscard(vm);
        var retainedIds = vm.Hand.Select(card => card.Id).ToHashSet();
        Assert(vm.Hand.SequenceEqual(cards.Where(card => retainedIds.Contains(card.Id))) && vm.Hand.All(card => !card.IsSelected), "Discard changed surviving cards or left stale selection.");
        Assert(vm.FlushPendingSave(), vm.SaveStatus);
        var revision = Engine(vm).Revision;
        var writes = store.WriteCount;
        vm.IsMotionEnabled = true;
        Assert(vm.FlushPendingSave() && store.WriteCount == writes + 1 && store.Read(GameSaveSlot.Automatic).MotionEnabled == true, "Changing motion after a settled save was not persisted.");
        vm.IsAutoAdvance = true;
        Assert(vm.FlushPendingSave() && store.Read(GameSaveSlot.Automatic).AutoAdvance, "Changing auto advance was not persisted.");
        vm.IsAutoAdvance = false;
        vm.SaveGameCommand.Execute(null);
        vm.IsMotionEnabled = false;
        vm.LoadManualGameCommand.Execute(null);
        Assert(!vm.IsMotionEnabled && !vm.IsAutoAdvance && Engine(vm).Revision == revision && vm.BattleCues.Count == 0, "Restore changed current motion preferences, the game, or replayed old animation cues.");
        var savedSequence = Engine(vm).Events.Last().Sequence;
        for (var step = 0; step < 30 && vm.BattleCues.Count == 0; step++) Step(vm);
        Assert(vm.BattleCues.Count > 0 && vm.BattleCues.All(cue => cue.Sequence > savedSequence), "Restored feedback replayed historical events.");
        window.Content = null;
        window.Close();
    }

    public static void RenderAndLifecycle(string output, bool recordMotion)
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore()) { IsMotionEnabled = true };
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback");
        var clock = new FrameClock();
        var segmentSounds = new List<GameSound>();
        var audioEvents = new List<object>();
        vm.SoundsRequested += (_, args) => segmentSounds.AddRange(args.Sounds);
        layer.Clock = clock;
        Render(root, 1440, 860, Path.Combine(output, "14-motion-start.png"));
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices.Single(choice => choice.Name == "关羽"));
        AdvanceToDecision(vm);
        vm.BattleCues.Clear();
        segmentSounds.Clear();
        var action = Engine(vm).GetHumanLegalActions().First(candidate => candidate.CardId is { } id && candidate.TargetSeats.Count == 1 &&
            candidate.TargetCardId is null && vm.Hand.Single(card => card.Id == id).Name == "杀");
        Play(vm, action);
        Assert(vm.BattleCues.Any(cue => cue.Kind == BattleCueKind.Card && cue.Label == "杀" && cue.TargetSeats.SequenceEqual(action.TargetSeats)), "An actual Slash did not generate its target feedback.");
        Assert(!layer.IsHitTestVisible && !layer.Focusable && layer.ActiveEffectCount > 0, "Effect layer is interactive or failed to observe commands.");
        var frameDirectory = Path.Combine(AppContext.BaseDirectory, "preview-frames");
        if (recordMotion) Directory.CreateDirectory(frameDirectory);
        var frameCount = 0;
        CaptureSegment("14-slash-flight", testMovement: true);

        var seen = new HashSet<BattleCueKind>();
        var capturedPrompt = false;
        for (var step = 0; step < 2000 && !vm.HasGameOver &&
            (!seen.Contains(BattleCueKind.Damage) || !seen.Contains(BattleCueKind.Recovery) || !seen.Contains(BattleCueKind.Turn) || !capturedPrompt); step++)
        {
            vm.BattleCues.Clear();
            segmentSounds.Clear();
            Step(vm);
            var cue = vm.BattleCues.FirstOrDefault(cue => cue.Kind is BattleCueKind.Damage or BattleCueKind.Recovery or BattleCueKind.Turn && !seen.Contains(cue.Kind));
            if (cue is not null && seen.Add(cue.Kind)) CaptureSegment($"15-{cue.Kind.ToString().ToLowerInvariant()}");
            if (vm.HasChoicePrompt && !capturedPrompt)
            {
                capturedPrompt = true;
                clock.Advance(.3);
                layer.InvalidateVisual();
                Render(root, 1120, 740, Path.Combine(output, "16-protected-response.png"));
                Assert(layer.IsPromptVisible, "Choice panel is not protected during feedback.");
                Assert(((ScrollViewer)window.FindName("ResponseViewport")).ViewportHeight >= 138, "Small-window response panel clips a complete public card row.");
                var choiceButton = Find<Button>(root).First(button => button.IsEnabled && button.Visibility == Visibility.Visible && button.ActualWidth > 0 &&
                    button.Command is { } command && new[] { vm.SelectRevealedCardCommand, vm.SelectResponseChoiceCommand, vm.SelectDyingChoiceCommand, vm.SelectHarvestChoiceCommand, vm.SelectTargetCardChoiceCommand, vm.SelectNullificationChoiceCommand, vm.SelectSkillChoiceCommand, vm.SelectFireAttackChoiceCommand }.Contains(command));
                var point = choiceButton.TranslatePoint(new Point(choiceButton.ActualWidth / 2, choiceButton.ActualHeight / 2), root);
                DependencyObject? hit = null;
                // Geometry hit testing works without showing a desktop window; native input is a separate acceptance step.
                VisualTreeHelper.HitTest(root,
                    element => element is UIElement ui && (ui.Visibility != Visibility.Visible || !ui.IsHitTestVisible)
                        ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
                    result => { hit = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
                Assert(hit is not null && (ReferenceEquals(hit, choiceButton) || choiceButton.IsAncestorOf(hit)), $"Response control is obstructed at {point}: {hit?.GetType().Name ?? "no visual"}.");
            }
        }
        Assert(seen.Contains(BattleCueKind.Damage) && seen.Contains(BattleCueKind.Recovery) && seen.Contains(BattleCueKind.Turn) && capturedPrompt,
            $"Actual effects coverage missing: {string.Join(", ", seen)}, prompt={capturedPrompt}.");
        vm.BattleCues.Clear();
        for (var step = 0; step < 500 && vm.BattleCues.Count == 0 && !vm.HasGameOver; step++) Step(vm);
        Assert(layer.ActiveEffectCount > 0, "Lifecycle fixture has no live effect.");
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert(layer.IsFrameTimerRunning, "Loaded active feedback did not start rendering.");
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert(!layer.IsFrameTimerRunning && layer.ActiveEffectCount == 0, "Unloading left the frame timer or active cues alive.");
        vm.BattleCues.Add(vm.BattleCues[0]);
        Assert(layer.ActiveEffectCount == 0, "Unloaded layer is still observing the collection.");
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert(layer.ActiveEffectCount == 0, "Reloading replayed history.");
        vm.BattleCues.Add(vm.BattleCues[0]);
        Assert(layer.IsFrameTimerRunning, "Reloading did not observe new cues.");
        vm.IsMotionEnabled = false;
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning, "Disabling motion left active animation.");
        vm.BattleCues.Add(vm.BattleCues[0]);
        vm.IsMotionEnabled = true;
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert(layer.ActiveEffectCount == 0, "Enabling motion replayed old cues.");
        vm.BattleCues.Add(vm.BattleCues[0]);
        clock.Advance(2);
        layer.InvalidateVisual();
        Render(root, 1120, 740, Path.Combine(output, "17-motion-idle.png"));
        Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning, "Expired effects kept the renderer busy.");
        vm.StartNewGameCommand.Execute(null);
        Assert(vm.BattleCues.Count == 0 && layer.ActiveEffectCount == 0, "New game retained feedback from the previous match.");
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        window.Content = null;
        window.Close();
        if (recordMotion)
        {
            File.WriteAllText(Path.Combine(output, "record-frame-count.txt"), frameCount.ToString());
            File.WriteAllText(Path.Combine(output, "record-frame-directory.txt"), frameDirectory);
            File.WriteAllText(Path.Combine(output, "audio-preview-events.json"), JsonSerializer.Serialize(new { Duration = frameCount / 24.0, Events = audioEvents }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"  Actual Slash, damage, recovery, turn and protected response rendered; {frameCount} preview frames.");

        void CaptureSegment(string name, bool testMovement = false)
        {
            if (recordMotion)
                foreach (var sound in GameSoundRules.SelectBatch(segmentSounds))
                    audioEvents.Add(new { Seconds = frameCount / 24.0, Sound = sound.ToString() });
            var declared = Engine(vm).Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Last();
            Assert(vm.RecentPlays[0].Name == CardCatalog.Get(declared.CardKind).DisplayName, "Public table card does not match the last committed declaration.");
            var state = SnapshotJson.Serialize(Engine(vm).CreateSnapshot(0, true));
            var journalCount = Engine(vm).CreateCheckpoint().Commands.Count;
            byte[]? earlierPixels = null;
            for (var index = 0; index < 32; index++)
            {
                layer.InvalidateVisual();
                if (recordMotion) Render(root, 1440, 860, Path.Combine(frameDirectory, $"frame-{frameCount++:D4}.png"));
                if (index is 3 or 9 or 18)
                {
                    Render(root, 1440, 860, Path.Combine(output, $"{name}-{index:D2}.png"));
                    if (testMovement)
                    {
                        var bitmap = new RenderTargetBitmap((int)layer.ActualWidth, (int)layer.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(layer);
                        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                        Assert(pixels.Any(value => value != 0), "Actual animation frame is empty.");
                        Assert(earlierPixels is null || !pixels.SequenceEqual(earlierPixels), "Flight frames did not move or fade.");
                        if (index == 9)
                        {
                            var bar = (FrameworkElement)window.FindName("ActionBar");
                            var barTop = (int)Math.Ceiling(bar.TranslatePoint(new Point(), layer).Y);
                            var barHeight = (int)Math.Floor(bar.ActualHeight) - 1;
                            var barPixels = new byte[bitmap.PixelWidth * barHeight * 4];
                            bitmap.CopyPixels(new Int32Rect(0, barTop, bitmap.PixelWidth, barHeight), barPixels, bitmap.PixelWidth * 4, 0);
                            Assert(barPixels.All(value => value == 0), "Target line paints over the action instructions or buttons.");
                        }
                        earlierPixels = pixels;
                    }
                }
                clock.Advance(1.0 / 24);
            }
            Assert(SnapshotJson.Serialize(Engine(vm).CreateSnapshot(0, true)) == state && Engine(vm).CreateCheckpoint().Commands.Count == journalCount,
                "Rendering advanced the rules or submitted a command.");
        }
    }

    private static void Play(MainViewModel vm, LegalAction action)
    {
        vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == action.CardId));
        if (action.TargetSeat is { } seat && !vm.Seats.Single(player => player.Seat == seat).IsSelectedTarget)
            vm.SelectTargetCommand.Execute(vm.Seats.Single(player => player.Seat == seat));
        Assert(vm.CanConfirmSelected, "Feedback fixture selected an unusable card.");
        vm.ConfirmSelectedCommand.Execute(null);
    }

    private static void Step(MainViewModel vm)
    {
        if (vm.IsGeneralSelectionPending) vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        else if (vm.IsDiscardSelectionPending) ResolveDiscard(vm);
        else if (vm.CanStepAi) vm.StepAiCommand.Execute(null);
        else if (vm.CanEndTurn)
        {
            var peach = Engine(vm).GetHumanLegalActions().FirstOrDefault(action => action.CardId is { } id &&
                vm.Hand.Single(card => card.Id == id).Name == "桃" && action.TargetSeat == 0);
            if (peach is not null) Play(vm, peach); else vm.EndTurnCommand.Execute(null);
        }
        else if (vm.CanDeclineResponse) vm.DeclineResponseCommand.Execute(null);
        else
        {
            var choices = new (IEnumerable<PromptChoice>, ICommand)[]
            {
                (vm.ResponseChoices, vm.SelectResponseChoiceCommand), (vm.DyingChoices, vm.SelectDyingChoiceCommand),
                (vm.HarvestChoices, vm.SelectHarvestChoiceCommand), (vm.TargetCardChoices, vm.SelectTargetCardChoiceCommand),
                (vm.FireAttackChoices, vm.SelectFireAttackChoiceCommand),
                (vm.NullificationChoices, vm.SelectNullificationChoiceCommand), (vm.SkillChoices, vm.SelectSkillChoiceCommand)
            };
            var available = choices.FirstOrDefault(pair => pair.Item1.Any());
            Assert(available.Item1 is not null, $"Feedback fixture has no continuation at {Engine(vm).State.Status}.");
            available.Item2.Execute(available.Item1!.First());
        }
        Assert(!vm.PromptText.Contains("未执行"), vm.PromptText);
    }

    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
