using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PlayerGuideChecks
{
    public static HashSet<string> ObservedPrompts { get; } = [];
    private static string State(MainViewModel vm) => SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0));
    private static void Require(bool condition, string message) => Program.Assert(condition, message);

    public static void ModalLifecycle()
    {
        using var vm = new MainViewModel(false, 721019, showSetup: false, saveStore: new MemorySaveStore());
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Require(vm.CanStepAi, "Timer fixture needs an actual engine continuation.");
        vm.IsAutoAdvance = true;
        vm.OpenContextGuideCommand.Execute(null);
        var paused = State(vm);
        Pump(TimeSpan.FromMilliseconds(1400));
        Require(State(vm) == paused && vm.IsAutoAdvance, "Guide changed state while its original auto timer was enabled.");
        vm.ToggleHelpCommand.Execute(null);
        Pump(TimeSpan.FromMilliseconds(750));
        Require(State(vm) != paused && vm.IsAutoAdvance, "Closing guide failed to resume the original auto policy.");
        vm.IsAutoAdvance = false;
        var manuallyPaused = State(vm);
        vm.OpenContextGuideCommand.Execute(null);
        vm.ToggleHelpCommand.Execute(null);
        Pump(TimeSpan.FromMilliseconds(750));
        Require(State(vm) == manuallyPaused && !vm.IsAutoAdvance, "Guide silently enabled an intentionally paused timer.");
    }
    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
