using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            Require(vm.IsMotionEnabled, "A fresh device must show game effects by default.");
            var window = new MainWindow(vm);
            window.ApplyTemplate();
            var root = (FrameworkElement)window.Content;
            Program.Render(root, 1120, 740, Path.Combine(output, "54-preferences-startup.png"));
            var before = State(vm);
            var slider = (Slider)window.FindName("SetupSoundVolume");
            var toggle = (CheckBox)window.FindName("SetupSoundToggle");
            var motion = (CheckBox)window.FindName("SetupMotionToggle");
            var settingsSlider = (Slider)window.FindName("SettingsSoundVolume");
            var settingsVolume = (TextBlock)window.FindName("SettingsSoundVolumeValue");
            var settingsBack = (Button)window.FindName("CloseSettingsButton");
            vm.OpenSettingsCommand.Execute(null);
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Require(Equals(settingsBack.Content, "返回大厅") && vm.IsNewGameSetupOpen,
                "Settings opened from the lobby should return to the lobby.");
            settingsSlider.Value = .19;
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var expectedVolume = vm.SoundVolume.ToString("P0", settingsVolume.Language.GetSpecificCulture());
            Require(vm.SoundVolume == .19 && slider.Value == .19 && settingsVolume.Text == expectedVolume &&
                    System.Windows.Automation.AutomationProperties.GetName(settingsSlider) == "游戏音量" &&
                    settingsSlider.SmallChange == .05 && settingsSlider.LargeChange == .1,
                "Settings volume needs a readable percentage, keyboard steps and a shared accessible volume control.");
            Program.Render(root, 1120, 740, Path.Combine(output, "54-preferences-settings.png"));
            SettingsScrollKeepsHeader(window, root, output);
            vm.CloseSettingsCommand.Execute(null);
            Require(!vm.IsSettingsOpen && vm.IsNewGameSetupOpen, "Closing settings unexpectedly left the lobby.");
            vm.IsNewGameSetupOpen = false;
            vm.OpenSettingsCommand.Execute(null);
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Require(Equals(settingsBack.Content, "返回牌桌"), "Settings opened from play should return to the table.");
            vm.CloseSettingsCommand.Execute(null);
            vm.IsNewGameSetupOpen = true;
            vm.OpenLobbyCategoryCommand.Execute("identity");
            vm.OpenModeConfigurationCommand.Execute(vm.VisibleModeGroups.Single(group => group.Variants.Contains(vm.SelectedTableMode)));
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            toggle.IsChecked = false;
            motion.IsChecked = false;
            Pump(650);
            Require(store.Read() == new PlayerPreferences(1, false, .19, false) && saves.WriteCount == 0 && State(vm) == before,
                "Setup preferences failed to persist independently of the untouched game.");
            Require(!slider.IsEnabled && !settingsSlider.IsEnabled && settingsVolume.Text == expectedVolume && toggle.IsEnabled && motion.IsEnabled,
                "Muted volume controls should retain their displayed level while the sound toggle remains available.");
            Program.Render(root, 1120, 740, Path.Combine(output, "55-preferences-muted.png"));
            var start = (Button)window.FindName("StartNewGameButton");
            var configuration = (StackPanel)window.FindName("ModeConfigurationPanel");
            var origin = start.TranslatePoint(new Point(), root);
            var end = start.TranslatePoint(new Point(start.ActualWidth, start.ActualHeight), root);
            Require(vm.IsModeConfigurationOpen && configuration.Visibility == Visibility.Visible && start.Visibility == Visibility.Visible &&
                    start.ActualWidth > 0 && start.ActualHeight > 0 &&
                    origin.X >= 0 && origin.Y >= 0 && end.X <= 1120 && end.Y <= 740,
                "Preferences pushed the visible mode-configuration start button outside the small window.");
            window.Content = null;
            window.Close();
        }
        using (var reopened = new MainViewModel(false, 721020, true, saves, preferencesStore: new FilePlayerPreferencesStore(path)))
        {
            Require(!reopened.IsSoundEnabled && reopened.SoundVolume == .19 && !reopened.IsMotionEnabled && saves.WriteCount == 0,
                "Relaunch without loading a match lost device preferences.");
            reopened.StartNewGameCommand.Execute(null);
            reopened.SaveGameCommand.Execute(null);
            reopened.LoadManualGameCommand.Execute(null);
            Require(!reopened.HasSaveError && !reopened.IsSoundEnabled && reopened.SoundVolume == .19 && !reopened.IsMotionEnabled,
                "Loading a match overrode device preferences.");
            reopened.StartTutorialCommand.Execute(null);
            reopened.SoundVolume = .24;
            Require(reopened.IsTutorialActive && reopened.FlushPreferences() && store.Read()!.SoundVolume == .24,
                "Tutorial settings should persist while its game remains transient.");
            reopened.ExitTutorialCommand.Execute(null);
            Require(!reopened.IsSoundEnabled && reopened.SoundVolume == .24, "Returning from tutorial reverted device preferences.");
        }

        var freshPath = Path.Combine(directory, "fresh-device.json");
        using (var firstLoad = new MainViewModel(false, 721019, true, saves, preferencesStore: new FilePlayerPreferencesStore(freshPath)))
        {
            var before = (firstLoad.IsSoundEnabled, firstLoad.SoundVolume, firstLoad.IsMotionEnabled);
            firstLoad.LoadManualGameCommand.Execute(null);
            Require((firstLoad.IsSoundEnabled, firstLoad.SoundVolume, firstLoad.IsMotionEnabled) == before,
                "A match save must not seed or replace device preferences.");
            firstLoad.IsSoundEnabled = false;
            firstLoad.LoadManualGameCommand.Execute(null);
            Require(!firstLoad.IsSoundEnabled, "Repeated loading unmuted the player.");
        }
        Require(new FilePlayerPreferencesStore(freshPath).Read() is { SoundEnabled: false }, "Closing failed to flush the latest setting.");
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
        var retryWindow = new MainWindow(retry);
        var retryRoot = (FrameworkElement)retryWindow.Content;
        try
        {
            var before = State(retry);
            retry.OpenSettingsCommand.Execute(null);
            Program.Render(retryRoot, 1120, 740, Path.Combine(output, "preferences-read-failure-settings.png"));
            var errorPanel = retryWindow.FindName("SettingsPreferencesError") as Border;
            var retryButton = retryWindow.FindName("RetrySettingsPreferencesButton") as Button;
            Require(errorPanel is { Visibility: Visibility.Visible, ActualHeight: > 0 } &&
                    retryButton is { IsEnabled: true, ActualWidth: > 0 } &&
                    ReferenceEquals(retryButton.Command, retry.RetryPreferencesCommand),
                "Settings errors must expose a visible retry action inside the current settings panel.");
            failure.FailRead = false;
            ClickRetry();
            Require(!retry.HasPreferencesError && retry.SoundVolume == .3 && failure.Writes == 0 &&
                    errorPanel!.Visibility == Visibility.Collapsed && retry.IsSettingsOpen && State(retry) == before,
                "The settings retry button must reread a temporary failure without writing or leaving the current panel.");
            failure.FailWrite = true;
            for (var i = 1; i < 20; i++) retry.SoundVolume = i / 100.0;
            Require(failure.Writes == 0, "Slider changes caused immediate synchronous file writes.");
            Pump(650);
            Program.Render(retryRoot, 1120, 740, Path.Combine(output, "preferences-write-failure-settings.png"));
            Require(failure.Writes == 1 && retry.HasPreferencesError && retry.SoundVolume == .19 && State(retry) == before &&
                    errorPanel!.Visibility == Visibility.Visible && retryButton!.IsEnabled,
                "A failed debounced save must retain active settings and expose the current panel's retry action.");
            failure.FailWrite = false;
            ClickRetry();
            Require(failure.Writes == 2 && !retry.HasPreferencesError && failure.Value.SoundVolume == .19 &&
                    retry.SoundVolume == .19 && errorPanel!.Visibility == Visibility.Collapsed &&
                    retry.IsSettingsOpen && State(retry) == before,
                "Retry must save the latest active value and clear the error without changing the match or closing settings.");

            void ClickRetry()
            {
                typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(retryButton, null);
                retryRoot.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            }
        }
        finally { retryWindow.Content = null; retryWindow.Close(); }
        Console.WriteLine("  Device settings: startup-only persistence, one-time save migration, mute retention, tutorial return, corrupt backup and debounced retry verified.");
    }

    private static void SettingsScrollKeepsHeader(MainWindow window, FrameworkElement root, string output)
    {
        var scroll = (ScrollViewer)window.FindName("SettingsContentScroll");
        var content = (StackPanel)window.FindName("SettingsContentHost");
        var privacy = (TextBlock)window.FindName("SettingsPrivacyNotice");
        var back = (Button)window.FindName("CloseSettingsButton");
        var presenter = Program.Find<ScrollContentPresenter>(scroll).Single(item => ReferenceEquals(item.TemplatedParent, scroll));
        var originalTransform = content.LayoutTransform;
        try
        {
            // Exercise enlarged content at the minimum client size without shrinking its text.
            content.LayoutTransform = new ScaleTransform(1.5, 1.5);
            Program.Render(root, 1120, 740, null);
            var fixedHeader = back.TranslatePoint(new Point(), root);
            Require(scroll.ScrollableHeight > 0 && scroll.ViewportHeight > 0,
                "Enlarged settings content must scroll inside the available height.");
            scroll.ScrollToEnd();
            root.UpdateLayout();
            Program.Render(root, 1120, 740, Path.Combine(output, "54-preferences-settings-enlarged.png"));
            var footerBounds = privacy.TransformToAncestor(presenter).TransformBounds(new Rect(privacy.RenderSize));
            var headerEnd = back.TranslatePoint(new Point(back.ActualWidth, back.ActualHeight), root);
            Require(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1 &&
                    footerBounds.Top >= -1 && footerBounds.Left >= -1 &&
                    footerBounds.Bottom <= presenter.ActualHeight + 1 && footerBounds.Right <= presenter.ActualWidth + 1 &&
                    (back.TranslatePoint(new Point(), root) - fixedHeader).Length < 1 &&
                    fixedHeader.X >= 0 && fixedHeader.Y >= 0 && headerEnd.X <= 1120 && headerEnd.Y <= 740,
                "Scrolling enlarged settings must reveal the entire privacy notice while the return button stays fixed and visible.");
        }
        finally
        {
            content.LayoutTransform = originalTransform;
            scroll.ScrollToHome();
            Program.Render(root, 1120, 740, null);
        }
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
