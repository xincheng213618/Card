using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
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
        using var automatic = Create();
        using var singleStep = Create();
        var tick = typeof(MainViewModel).GetMethod("OnAdvanceTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var coalesced = false;
        var checkedPause = false;
        var checkedHuman = false;
        for (var iteration = 0; iteration < 140 && !automatic.HasGameOver; iteration++)
        {
            var before = Program.Engine(automatic).CreateCheckpoint().Commands.Count;
            automatic.IsAutoAdvance = true;
            if (automatic.CanStepAi && !checkedPause)
            {
                automatic.IsHelpOpen = true;
                tick.Invoke(automatic, [null, EventArgs.Empty]);
                Program.Assert(Program.Engine(automatic).CreateCheckpoint().Commands.Count == before,
                    "Automatic batching advanced behind an open guide.");
                automatic.IsHelpOpen = false;
                checkedPause = true;
            }
            var wasAi = automatic.CanStepAi;
            tick.Invoke(automatic, [null, EventArgs.Empty]);
            automatic.IsAutoAdvance = false;
            var after = Program.Engine(automatic).CreateCheckpoint().Commands.Count;
            if (!wasAi)
            {
                checkedHuman = true;
                Program.Assert(after == before && State(automatic) == State(singleStep),
                    "Automatic batching answered or bypassed a human decision.");
                PersistenceChecks.Step(automatic);
                PersistenceChecks.Step(singleStep);
            }
            else
            {
                Program.Assert(after > before && after - before <= 32,
                    "An automatic slice did not respect its command bound.");
                coalesced |= after - before > 1;
                for (var index = before; index < after; index++) singleStep.StepAiCommand.Execute(null);
            }
            Program.Assert(State(automatic) == State(singleStep) &&
                    GameCheckpointJson.Serialize(Program.Engine(automatic).CreateCheckpoint()) ==
                    GameCheckpointJson.Serialize(Program.Engine(singleStep).CreateCheckpoint()),
                "Coalescing internal continuations changed the committed commands or replay state.");
        }
        Program.Assert(coalesced && checkedPause && checkedHuman,
            "The real match did not cover batching, modal pause and human boundaries.");

        static MainViewModel Create() => new(false, 17, false, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false };
    }



    private static DispatcherTimer Timer(MainViewModel vm) => (DispatcherTimer)typeof(MainViewModel)
        .GetField("_advanceTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!;
    private static string State(MainViewModel vm) => SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0, true));
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
