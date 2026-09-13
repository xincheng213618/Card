using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PreferencesChecks
{
    private static void Require(bool condition, string message) => Program.Assert(condition, message);
    private static string State(MainViewModel vm) => SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0, true));

    public static void StartupAndMigration(string output)
    {
        var directory = Path.Combine(output, "preference-fixtures", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "preferences.json");
        var store = new FilePlayerPreferencesStore(path);
        var saves = new MemorySaveStore();
        using (var vm = new MainViewModel(false, 721019, true, saves, preferencesStore: store))
        {
            Require(!File.Exists(path), "Merely opening setup must not write preferences.");
            var window = new MainWindow(vm);
            window.ApplyTemplate();
            var root = (FrameworkElement)window.Content;
            Program.Render(root, 1120, 740, Path.Combine(output, "54-preferences-startup.png"));
            var before = State(vm);
            var slider = (Slider)window.FindName("SetupSoundVolume");
            var toggle = (CheckBox)window.FindName("SetupSoundToggle");
            var motion = (CheckBox)window.FindName("SetupMotionToggle");
            slider.Value = .19;
            toggle.IsChecked = false;
            motion.IsChecked = false;
            Pump(650);
            Require(store.Read() == new PlayerPreferences(1, false, .19, false) && saves.WriteCount == 0 && State(vm) == before,
                "Setup preferences failed to persist independently of the untouched game.");
            Require(!slider.IsEnabled && toggle.IsEnabled && motion.IsEnabled, "Setup sound controls are not correctly bound.");
            Program.Render(root, 1120, 740, Path.Combine(output, "55-preferences-muted.png"));
            var start = (Button)window.FindName("StartNewGameButton");
            var end = start.TranslatePoint(new Point(start.ActualWidth, start.ActualHeight), root);
            Require(start.ActualHeight > 0 && end.X <= 1120 && end.Y <= 740, "Preferences pushed the start button outside the small window.");
            window.Content = null;
            window.Close();
        }
        using (var reopened = new MainViewModel(false, 721020, true, saves, preferencesStore: new FilePlayerPreferencesStore(path)))
        {
            Require(!reopened.IsSoundEnabled && reopened.SoundVolume == .19 && !reopened.IsMotionEnabled && saves.WriteCount == 0,
                "Relaunch without loading a match lost device preferences.");
            reopened.StartNewGameCommand.Execute(null);
            reopened.SaveGameCommand.Execute(null);
            var checkpoint = saves.Read(GameSaveSlot.Manual);
            saves.Write(GameSaveSlot.Manual, checkpoint with { SoundEnabled = true, SoundVolume = .9, MotionEnabled = true });
            reopened.LoadManualGameCommand.Execute(null);
            Require(!reopened.HasSaveError && !reopened.IsSoundEnabled && reopened.SoundVolume == .19 && !reopened.IsMotionEnabled,
                "Loading a louder old save overrode current preferences.");
            reopened.StartTutorialCommand.Execute(null);
            reopened.SoundVolume = .24;
            Require(reopened.IsTutorialActive && reopened.FlushPreferences() && store.Read()!.SoundVolume == .24,
                "Tutorial settings should persist while its game remains transient.");
            reopened.ExitTutorialCommand.Execute(null);
            Require(!reopened.IsSoundEnabled && reopened.SoundVolume == .24, "Returning from tutorial reverted device preferences.");
        }

        var migrationPath = Path.Combine(directory, "migrated.json");
        using (var firstLoad = new MainViewModel(false, 721019, true, saves, preferencesStore: new FilePlayerPreferencesStore(migrationPath)))
        {
            firstLoad.LoadManualGameCommand.Execute(null);
            Require(firstLoad.IsSoundEnabled && firstLoad.SoundVolume == .9 && firstLoad.IsMotionEnabled && firstLoad.FlushPreferences(),
                "First legacy save did not seed missing device preferences.");
            firstLoad.IsSoundEnabled = false;
            firstLoad.LoadManualGameCommand.Execute(null);
            Require(!firstLoad.IsSoundEnabled, "Repeated legacy import unmuted the player.");
        }
        Require(new FilePlayerPreferencesStore(migrationPath).Read() is { SoundEnabled: false }, "Closing failed to flush the latest setting.");
    }

    public static void FailuresAndDebounce(string output)
    {
        var path = Path.Combine(output, "preference-fixtures", Guid.NewGuid().ToString("N"), "preferences.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ broken preferences");
        var store = new FilePlayerPreferencesStore(path);
        using (var vm = new MainViewModel(false, 721019, true, new MemorySaveStore(), preferencesStore: store))
        {
            Require(vm.HasPreferencesError && !vm.IsSoundEnabled && !vm.IsMotionEnabled && File.ReadAllText(path) == "{ broken preferences",
                "Unreadable preferences were silently overwritten or enabled sound.");
            var window = new MainWindow(vm);
            var root = (FrameworkElement)window.Content;
            Program.Render(root, 1120, 740, Path.Combine(output, "56-preferences-recovery.png"));
            vm.SoundVolume = .35;
            Require(vm.FlushPreferences() && !vm.HasPreferencesError && File.ReadAllText(path + ".bak") == "{ broken preferences",
                "An explicit preference change did not preserve the unreadable original as a backup.");
            window.Content = null;
            window.Close();
        }

        foreach (var invalid in new[] { "{}", "{\"FormatVersion\":99,\"SoundEnabled\":true,\"SoundVolume\":0.2,\"MotionEnabled\":true}",
            "{\"FormatVersion\":1,\"SoundEnabled\":true,\"SoundVolume\":2,\"MotionEnabled\":true}" })
        {
            File.WriteAllText(path, invalid);
            using var invalidVm = new MainViewModel(false, 721019, true, new MemorySaveStore(), preferencesStore: store);
            Require(invalidVm.HasPreferencesError && File.ReadAllText(path) == invalid, "Malformed or future settings were accepted or rewritten.");
        }

        var failure = new FailingPreferencesStore { FailRead = true };
        using var retry = new MainViewModel(false, 721019, true, new MemorySaveStore(), preferencesStore: failure);
        Require(retry.HasPreferencesError, "Read failure was not surfaced.");
        failure.FailRead = false;
        retry.RetryPreferencesCommand.Execute(null);
        Require(!retry.HasPreferencesError && retry.SoundVolume == .3, "Retry did not reread a temporarily inaccessible settings file.");
        var before = State(retry);
        failure.FailWrite = true;
        for (var i = 1; i < 20; i++) retry.SoundVolume = i / 100.0;
        Require(failure.Writes == 0, "Slider changes caused immediate synchronous file writes.");
        Pump(650);
        Require(failure.Writes == 1 && retry.HasPreferencesError && retry.SoundVolume == .19 && State(retry) == before,
            "Debounced failure interrupted the game or lost the active setting.");
        failure.FailWrite = false;
        retry.RetryPreferencesCommand.Execute(null);
        Require(failure.Writes == 2 && !retry.HasPreferencesError && failure.Value.SoundVolume == .19, "Retry did not save the latest pending value.");
        Console.WriteLine("  Device settings: startup-only persistence, one-time save migration, mute retention, tutorial return, corrupt backup and debounced retry verified.");
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private sealed class FailingPreferencesStore : IPlayerPreferencesStore
    {
        public bool FailRead, FailWrite;
        public int Writes;
        public PlayerPreferences Value = new(1, false, .3, false);
        public PlayerPreferences? Read() => FailRead ? throw new IOException("Fixture read failure.") : Value;
        public void Write(PlayerPreferences preferences)
        {
            Writes++;
            if (FailWrite) throw new IOException("Fixture write failure.");
            Value = preferences;
        }
    }
}
