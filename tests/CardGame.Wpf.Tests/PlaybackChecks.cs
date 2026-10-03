using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class PlaybackChecks
{
    public static void AutomaticStepsPreserveCommittedBoundaries()
    {
        var saveStore = new MemorySaveStore();
        using var automatic = Create(saveStore);
        using var singleStep = Create(new MemorySaveStore());
        var automaticFeedback = ObserveFeedback(automatic);
        var singleFeedback = ObserveFeedback(singleStep);
        var tick = typeof(MainViewModel).GetMethod("OnAdvanceTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var coalesced = false;
        var checkedPause = false;
        var checkedHuman = false;
        var checkedInquiryDelay = false;
        var checkedMixedFeedbackDelay = false;
        var timer = (DispatcherTimer)typeof(MainViewModel).GetField("_advanceTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(automatic)!;
        var refreshes = 0;
        var tickCommands = 0;
        var tickRefreshes = 0;
        var tickBytes = 0L;
        var tickTime = TimeSpan.Zero;
        var sliceCosts = new List<(int FirstCommand, long Revision, double LogicMs, double LayoutMs)>();
        automatic.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(MainViewModel.TurnHeadline)) refreshes++; };
        var automaticWindow = new MainWindow(automatic);
        var singleWindow = new MainWindow(singleStep);
        var automaticRoot = (FrameworkElement)automaticWindow.Content;
        var singleRoot = (FrameworkElement)singleWindow.Content;
        foreach (var root in new[] { automaticRoot, singleRoot })
        {
            root.Measure(new Size(1120, 740));
            root.Arrange(new Rect(0, 0, 1120, 740));
            root.UpdateLayout();
        }
        try
        {
            for (var iteration = 0; iteration < 140 && !automatic.HasGameOver; iteration++)
            {
                var before = Program.Engine(automatic).AcceptedCommands.Count;
                automatic.SelectedPlaybackSpeed = PlaybackSpeed.All[iteration % PlaybackSpeed.All.Count];
                var lastCueSequence = automatic.BattleCues.LastOrDefault()?.Sequence ?? 0;
                automatic.IsAutoAdvance = true;
                if (automatic.CanStepAi && !checkedPause)
                {
                    automatic.IsHelpOpen = true;
                    tick.Invoke(automatic, [null, EventArgs.Empty]);
                    Program.Assert(Program.Engine(automatic).AcceptedCommands.Count == before,
                        "Automatic batching advanced behind an open guide.");
                    automatic.IsHelpOpen = false;
                    checkedPause = true;
                }
                var wasAi = automatic.CanStepAi;
                var previousRefreshes = refreshes;
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var started = Stopwatch.GetTimestamp();
                tick.Invoke(automatic, [null, EventArgs.Empty]);
                var logicMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var layoutStarted = Stopwatch.GetTimestamp();
                automaticRoot.UpdateLayout();
                sliceCosts.Add((before, Program.Engine(automatic).Revision, logicMs, Stopwatch.GetElapsedTime(layoutStarted).TotalMilliseconds));
                var newCues = automatic.BattleCues.Where(cue => cue.Sequence > lastCueSequence).ToArray();
                var inquiryOnly = newCues.Length > 0 && newCues.All(cue => cue.IsInquiry);
                var expectedDelay = automatic.SelectedPlaybackSpeed.IntervalMilliseconds;
                if (automatic.CanStepAi)
                {
                    if (inquiryOnly) expectedDelay = 80;
                    else if (newCues.Length == 0) expectedDelay = 1;
                    checkedInquiryDelay |= inquiryOnly;
                    checkedMixedFeedbackDelay |= newCues.Any(cue => cue.IsInquiry) && newCues.Any(cue => !cue.IsInquiry);
                }
                Program.Assert(timer.Interval.TotalMilliseconds == expectedDelay,
                    "Automatic pacing delayed a bare inquiry, shortened real feedback or skipped a human boundary.");
                tickTime += Stopwatch.GetElapsedTime(started);
                tickBytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
                tickRefreshes += refreshes - previousRefreshes;
                automatic.IsAutoAdvance = false;
                var after = Program.Engine(automatic).AcceptedCommands.Count;
                tickCommands += after - before;
                if (!wasAi)
                {
                    checkedHuman = true;
                    Program.Assert(after == before && State(automatic) == State(singleStep),
                        "Automatic batching answered or bypassed a human decision.");
                    PersistenceChecks.Step(automatic);
                    PersistenceChecks.Step(singleStep);
                    automaticRoot.UpdateLayout();
                    singleRoot.UpdateLayout();
                }
                else
                {
                    Program.Assert(after > before && after - before <= 32,
                        "An automatic slice did not respect its command bound.");
                    Program.Assert(refreshes - previousRefreshes == 1,
                        "An automatic slice rebuilt the table for intermediate invisible continuations.");
                    coalesced |= after - before > 1;
                    for (var index = before; index < after; index++) singleStep.StepAiCommand.Execute(null);
                    singleRoot.UpdateLayout();
                }
                Program.Assert(State(automatic) == State(singleStep) &&
                        GameCheckpointJson.Serialize(Program.Engine(automatic).CreateCheckpoint()) ==
                        GameCheckpointJson.Serialize(Program.Engine(singleStep).CreateCheckpoint()),
                    "Coalescing internal continuations changed the committed commands or replay state.");
                Program.Assert(View(automatic) == View(singleStep) && automaticFeedback.SequenceEqual(singleFeedback),
                    "Automatic presentation lost or reordered public feedback, sound, hand availability or seat state.");
                Program.Assert(SnapshotJson.Serialize(PresentedSnapshot(automatic)) ==
                               SnapshotJson.Serialize(Program.Engine(automatic).CreateSnapshot(0)),
                    "An automatic slice returned with an outdated player view.");
                // This fixed seed only needs the prefix covering every asserted
                // playback boundary; later turns repeat the same invariants.
                if (coalesced && checkedPause && checkedHuman && checkedInquiryDelay && checkedMixedFeedbackDelay) break;
            }
            Program.Assert(coalesced && checkedPause && checkedHuman && checkedInquiryDelay && checkedMixedFeedbackDelay,
                "The real match did not cover batching, modal pause, human boundaries and inquiry-only/mixed feedback pacing.");
            Console.WriteLine($"Automatic playback with bound layout: {tickCommands} commands, {tickRefreshes} table refreshes; {tickTime.TotalMilliseconds:F1} ms; {tickBytes:N0} bytes.");
            var profileCheckpoint = Program.Engine(automatic).CreateCheckpoint();
            var profileRegistry = (ContentRegistry)typeof(MainViewModel).GetField("_contentRegistry", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(automatic)!;
            var profileEngine = GameReplay.Restore(profileCheckpoint with { Commands = [], Revision = 0 }, profileRegistry);
            var coreCosts = new List<double>();
            foreach (var command in profileCheckpoint.Commands)
            {
                var coreStarted = Stopwatch.GetTimestamp();
                var replayResult = profileEngine.Submit(command);
                coreCosts.Add(Stopwatch.GetElapsedTime(coreStarted).TotalMilliseconds);
                Program.Assert(replayResult.Accepted, "Diagnostic command replay diverged.");
            }
            foreach (var cost in sliceCosts.OrderByDescending(cost => cost.LogicMs + cost.LayoutMs).Take(10))
                Console.WriteLine($"Automatic slice revision {cost.FirstCommand}->{cost.Revision}: logic {cost.LogicMs:F1} ms; layout {cost.LayoutMs:F1} ms; core replay {coreCosts.Skip(cost.FirstCommand).Take((int)cost.Revision - cost.FirstCommand).Sum():F1} ms.");
            Program.Assert(automatic.FlushPendingSave() &&
                           GameCheckpointJson.Serialize(saveStore.Read(GameSaveSlot.Automatic).Checkpoint) ==
                           GameCheckpointJson.Serialize(Program.Engine(automatic).CreateCheckpoint()),
                "Saving after an automatic slice omitted accepted commands.");

            // A fixed opening prefix avoids a timing-dependent end-of-loop prompt.
            foreach (var automaticFailure in new[] { true, false })
            {
                var failedStore = new MemorySaveStore();
                using var failed = Create(failedStore);
                failed.SelectGeneralChoiceCommand.Execute(failed.GeneralChoices[0]);
                Program.AdvanceToDecision(failed);
                Program.Assert(failed.CanEndTurn, "The failure fixture did not reach its opening Play phase.");
                failed.EndTurnCommand.Execute(null);
                for (var step = 0; step < 8 && failed.CanStepAi; step++) failed.StepAiCommand.Execute(null);
                Program.Assert(failed.IsDiscardSelectionPending, "The fixed opening did not request its hand-limit discard.");
                PersistenceChecks.Step(failed);
                Program.Assert(failed.CanStepAi && failed.FlushPendingSave(), "The pre-failure AI boundary was not saved.");
                failed.BattleCues.CollectionChanged += (_, args) =>
                {
                    if (args.NewItems is not null) throw new InvalidOperationException("fixture feedback observer failure");
                };
                failed.IsAutoAdvance = automaticFailure;
                Program.Assert(failed.FlushPendingSave(), "The playback preference was not saved before failure injection.");
                for (var step = 0; step < 32 && !failed.ActionHint.Contains("fixture feedback observer failure"); step++)
                {
                    if (automaticFailure) tick.Invoke(failed, [null, EventArgs.Empty]);
                    else failed.StepAiCommand.Execute(null);
                }
                Console.WriteLine($"Feedback failure: automatic mode={automaticFailure}, view={PresentedSnapshot(failed).Revision}, engine={Program.Engine(failed).Revision}");
                Program.Assert(!failed.IsAutoAdvance && failed.ActionHint.Contains("fixture feedback observer failure") &&
                               SnapshotJson.Serialize(PresentedSnapshot(failed)) ==
                               SnapshotJson.Serialize(Program.Engine(failed).CreateSnapshot(0)),
                    "A failed automatic feedback observer hid the committed view or continued playback.");
                Program.Assert(failed.FlushPendingSave() &&
                               GameCheckpointJson.Serialize(failedStore.Read(GameSaveSlot.Automatic).Checkpoint) ==
                               GameCheckpointJson.Serialize(Program.Engine(failed).CreateCheckpoint()),
                    "A failed feedback observer prevented saving its accepted command.");
            }
        }
        finally
        {
            automaticWindow.Content = null;
            singleWindow.Content = null;
            automaticWindow.Close();
            singleWindow.Close();
        }

        static MainViewModel Create(MemorySaveStore store) => new(false, 17, false, store,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false };
    }

    private static List<string> ObserveFeedback(MainViewModel vm)
    {
        var events = new List<string>();
        vm.BattleCues.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is not null)
                foreach (var cue in args.NewItems) events.Add("cue:" + JsonSerializer.Serialize(cue));
        };
        vm.SoundsRequested += (_, args) => events.Add("sound:" + JsonSerializer.Serialize(args.Sounds));
        vm.VoiceRequested += (_, args) => events.Add("voice:" + JsonSerializer.Serialize(args.Voice));
        return events;
    }

    private static string View(MainViewModel vm) => JsonSerializer.Serialize(new
    {
        vm.CanStepAi, vm.CanEndTurn, vm.HasGameOver, vm.PromptText, vm.PublicRevealTitle,
        Hand = vm.Hand.Select(card => new { card.Id, card.IsPlayable, card.IsSelected, card.AvailabilityText }),
        Seats = vm.Seats.Select(seat => new { seat.Seat, seat.GeneralName, seat.RoleLabel, seat.Hp, seat.HandCount,
            seat.IsCurrent, seat.EquipmentText, seat.JudgmentText, seat.DistanceText }),
        RecentPlays = vm.RecentPlays.Select(play => new { play.Sequence, play.Name, play.ActorName, play.Kind }),
        vm.GameLog
    });

    private static GameSnapshot PresentedSnapshot(MainViewModel vm) => (GameSnapshot)typeof(MainViewModel)
        .GetField("_snapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!;
    private static string State(MainViewModel vm) => SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0, true));
}
