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
    public static void SettingsAndSpectating(string output)
    {
        var startupStore = new MemorySaveStore();
        using (var startup = new MainViewModel(false, 1, true, startupStore)) startup.SelectedPlaybackSpeed = PlaybackSpeed.Fast;
        Program.Assert(startupStore.WriteCount == 0, "Changing speed at startup overwrote the old match.");
        var store = new FileGameSaveStore(Path.Combine(output, "save-fixtures", $"playback-{Guid.NewGuid():N}"));
        using (var vm = new MainViewModel(false, 721019, false, store) { IsMotionEnabled = false })
        {
            vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
            Program.AdvanceToDecision(vm);
            var window = new MainWindow(vm);
            var root = (FrameworkElement)window.Content;
            Program.Render(root, 1120, 740, Path.Combine(output, "41-playback-settings.png"));
            var selector = (ListBox)window.FindName("PlaybackSpeedSelector");
            var before = State(vm);
            foreach (var speed in PlaybackSpeed.All)
            {
                selector.SelectedItem = speed;
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                Program.Assert(vm.SelectedPlaybackSpeed == speed && Timer(vm).Interval.TotalMilliseconds == speed.IntervalMilliseconds && State(vm) == before,
                    "Speed control failed to bind or changed the rule state.");
            }
            vm.IsAutoAdvance = true;
            Pump(650);
            Program.Assert(State(vm) == before, "Fast playback chose a human action.");
            vm.IsAutoAdvance = false;
            Program.Assert(!vm.FastSpectateCommand.CanExecute(null), "A living player can activate spectator control.");
            vm.SaveGameCommand.Execute(null);
            Program.Assert(store.Read(GameSaveSlot.Manual).PlaybackSpeedId == "fast", "Speed did not reach the saved file.");
            vm.SelectedPlaybackSpeed = PlaybackSpeed.Normal;
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(vm.SelectedPlaybackSpeed == PlaybackSpeed.Fast && State(vm) == before, "Restoring speed changed the saved position.");
            vm.StartTutorialCommand.Execute(null);
            vm.ExitTutorialCommand.Execute(null);
            Program.Assert(vm.SelectedPlaybackSpeed == PlaybackSpeed.Fast && State(vm) == before, "Tutorial return lost speed or position.");
            var json = JsonNode.Parse(File.ReadAllText(store.GetPath(GameSaveSlot.Manual)))!.AsObject();
            json.Remove("PlaybackSpeedId");
            File.WriteAllText(store.GetPath(GameSaveSlot.Manual), json.ToJsonString());
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && vm.SelectedPlaybackSpeed == PlaybackSpeed.Normal, "An older save did not use standard pacing.");
            json["PlaybackSpeedId"] = "invalid-speed";
            File.WriteAllText(store.GetPath(GameSaveSlot.Manual), json.ToJsonString());
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(vm.HasSaveError && vm.SelectedPlaybackSpeed == PlaybackSpeed.Normal && State(vm) == before, "An invalid saved speed replaced the active match.");
            var end = selector.TranslatePoint(new Point(selector.ActualWidth, selector.ActualHeight), root);
            Program.Assert(end.X <= 1120 && end.Y <= 740 && selector.ActualHeight > 0, "Speed control overflowed the small window.");
            window.Content = null;
            window.Close();
        }

        using var spectator = FindSpectator();
        var spectatorWindow = new MainWindow(spectator);
        var spectatorRoot = (FrameworkElement)spectatorWindow.Content;
        Program.Render(spectatorRoot, 1120, 740, Path.Combine(output, "42-spectator-paused.png"));
        var fast = (Button)spectatorWindow.FindName("FastSpectateButton");
        var paused = State(spectator);
        Program.Assert(fast.Visibility == Visibility.Visible && fast.IsEnabled && spectator.ActionHint.Contains("观战已暂停"), "A dead player has no clear continuation.");
        fast.Command.Execute(null);
        Program.Assert(spectator.IsAutoAdvance && spectator.SelectedPlaybackSpeed == PlaybackSpeed.Fast && State(spectator) == paused,
            "Starting observation simulated the whole match synchronously.");
        spectator.SaveGameCommand.Execute(null);
        spectator.IsAutoAdvance = false;
        spectator.SelectedPlaybackSpeed = PlaybackSpeed.Normal;
        spectator.LoadManualGameCommand.Execute(null);
        Program.Assert(spectator.IsSpectating && spectator.IsAutoAdvance && spectator.SelectedPlaybackSpeed == PlaybackSpeed.Fast && State(spectator) == paused,
            "Restoring a spectator position lost its pacing or advanced the match.");
        spectator.OpenContextGuideCommand.Execute(null);
        Pump(600);
        Program.Assert(State(spectator) == paused, "Fast spectating ran behind the guide.");
        spectator.ToggleHelpCommand.Execute(null);
        Pump(650);
        Program.Assert(State(spectator) != paused, "The actual fast timer did not advance the match after closing the guide.");
        spectator.IsAutoAdvance = false;
        paused = State(spectator);
        Pump(450);
        Program.Assert(State(spectator) == paused, "Pausing left a fast timer running.");
        spectator.FastSpectateCommand.Execute(null);
        spectator.NewGameCommand.Execute(null);
        paused = State(spectator);
        Pump(450);
        Program.Assert(State(spectator) == paused, "Opening setup did not pause fast observation.");
        spectator.CancelNewGameSetupCommand.Execute(null);
        Program.Render(spectatorRoot, 1120, 740, Path.Combine(output, "43-spectator-fast.png"));
        spectator.IsAutoAdvance = false;
        for (var step = 0; step < 18000 && !spectator.HasGameOver; step++) PersistenceChecks.Step(spectator);
        Program.Assert(spectator.HasGameOver && !spectator.IsSpectating && !spectator.FastSpectateCommand.CanExecute(null) && spectator.CompletedMatch is not null,
            "Observation failed to reach the real result or retained the spectator action.");
        spectator.NewGameCommand.Execute(null);
        spectator.StartNewGameCommand.Execute(null);
        Program.Assert(!spectator.IsSpectating && spectator.SelectedPlaybackSpeed == PlaybackSpeed.Fast, "Rematch retained a dead-player banner or lost speed preference.");
        spectatorWindow.Content = null;
        spectatorWindow.Close();
    }

    private static MainViewModel FindSpectator()
    {
        for (var seed = 1; seed <= 24; seed++)
        {
            var vm = new MainViewModel(false, seed, false, new MemorySaveStore()) { IsMotionEnabled = false };
            vm.SelectedStartingRole = vm.StartingRoles.Single(role => role.Role == Role.Renegade);
            vm.StartNewGameCommand.Execute(null);
            for (var step = 0; step < 4000 && !vm.HasGameOver; step++)
            {
                if (vm.IsSpectating) return vm;
                PersistenceChecks.Step(vm);
            }
            vm.Dispose();
        }
        throw new InvalidOperationException("No real dead-player observation position was reached.");
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
