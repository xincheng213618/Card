using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.ViewModels;

internal static class ModeSelectionChecks
{
    public static void TwoStepSetup(MainViewModel vm, MainWindow window, FrameworkElement root, string output)
    {
        var revision = Program.Engine(vm).Revision;
        var home = (HomeLobby)window.FindName("LobbyHome");
        var lobbyButtons = Descendants(home).OfType<Button>().Where(button => button.IsTabStop).ToArray();
        Program.Assert(lobbyButtons.All(button => button.TabIndex < int.MaxValue) &&
                       lobbyButtons.Select(button => button.TabIndex).Distinct().Count() == lobbyButtons.Length &&
                       ((Button)home.FindName("EnterBattleButton")).TabIndex == lobbyButtons.Min(button => button.TabIndex),
            "Every lobby action needs an unambiguous keyboard order starting at the main battle entry.");
        vm.SelectedModeCategory = vm.ModeCategories.Single(category => category.Id == "activity");
        Program.Render(root, 1120, 740, Path.Combine(output, "134-activity-modes.png"));
        var groups = (ItemsControl)window.FindName("ModeGroupChoices");
        var choices = (ListBox)window.FindName("TableModeChoices");
        var start = (Button)window.FindName("StartNewGameButton");
        var modePanel = (StackPanel)window.FindName("ModeSelectionPanel");
        var settings = (StackPanel)window.FindName("ModeConfigurationPanel");
        Program.Assert(vm.IsModeSelectionOpen && modePanel.Visibility == Visibility.Visible &&
                       settings.Visibility == Visibility.Collapsed && start.Visibility == Visibility.Collapsed,
            "Mode selection must hide difficulty, setup and the start button until a mode is chosen.");
        Program.Assert(vm.VisibleModeGroups.Select(group => group.Name).Distinct().Count() == vm.VisibleModeGroups.Count &&
                       vm.VisibleModeGroups.Single(group => group.Name == "剑阁决战").Variants.Count == 3 &&
                       vm.VisibleModeGroups.Single(group => group.Name == "十殿阎罗").Variants.Count == 5,
            "Difficulty variants still appear as duplicate mode entries.");

        var shortcut = typeof(MainWindow).GetMethod("HandleShortcut", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var testedGroups = vm.VisibleModeGroups.Where(group => group.Variants.Count > 1 || group.Name == "驱鬼逐邪").ToArray();
        foreach (var group in testedGroups)
        {
            var count = group.Variants.Count;
            var entry = Descendants(groups).OfType<Button>().Single(button => Equals(button.CommandParameter, group));
            Program.Assert(entry.Command == vm.OpenModeConfigurationCommand, "A mode card lost its navigation command binding.");
            entry.Command!.Execute(entry.CommandParameter);
            Program.Render(root, 1120, 740, Path.Combine(output, $"134-{group.Variants[0].ModeId.Split(':')[1]}-setup.png"));
            Program.Assert(vm.IsModeConfigurationOpen && vm.SelectedModeGroup == group &&
                           vm.VisibleModeVariants.Count == count && choices.Items.Count == count &&
                           Equals(choices.SelectedItem, vm.SelectedTableMode) &&
                           modePanel.Visibility == Visibility.Collapsed && settings.Visibility == Visibility.Visible &&
                           start.Visibility == Visibility.Visible &&
                           choices.Visibility == (count > 1 ? Visibility.Visible : Visibility.Collapsed),
                "Choosing a mode did not isolate its own difficulty and setup controls.");
            if (count > 1)
            {
                var selected = (ListBoxItem)choices.ItemContainerGenerator.ContainerFromItem(vm.SelectedTableMode);
                Program.Assert(selected.IsSelected, "The configured difficulty card is missing its selected visual state.");
            }
            var hardest = group.Variants[^1];
            choices.SetCurrentValue(Selector.SelectedItemProperty, hardest);
            choices.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
            Program.Assert(vm.SelectedTableMode == hardest && vm.GuideIdentity.Contains(hardest.Name, StringComparison.Ordinal),
                "Difficulty selection did not update the actual configured mode and guide.");
            shortcut.Invoke(window, [Key.Escape, ModifierKeys.None]);
            Program.Render(root, 1120, 740, null);
            Program.Assert(vm.IsLobbyConfigurationOpen && vm.IsModeSelectionOpen && vm.SelectedTableMode == hardest,
                "Escape from difficulty setup must return one step and retain the selected difficulty.");
            entry.Command.Execute(entry.CommandParameter);
            Program.Assert(vm.SelectedTableMode == hardest, "Reopening the same mode lost its chosen difficulty.");
            vm.BackInLobbySetupCommand.Execute(null);
        }
        var rememberedGroup = testedGroups.First(group => group.Variants.Count > 1);
        vm.SelectedModeCategory = vm.ModeCategories.Single(category => category.Id == "identity");
        vm.SelectedModeCategory = vm.ModeCategories.Single(category => category.Id == "activity");
        vm.OpenModeConfigurationCommand.Execute(rememberedGroup);
        choices.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Program.Assert(vm.SelectedTableMode == rememberedGroup.Variants[^1] &&
                       Equals(choices.SelectedItem, vm.SelectedTableMode),
            "Returning to an activity after browsing another category lost its chosen difficulty.");
        vm.BackInLobbySetupCommand.Execute(null);
        Program.Assert(Program.Engine(vm).Revision == revision, "Browsing mode and difficulty choices changed the suspended match.");
        shortcut.Invoke(window, [Key.Escape, ModifierKeys.None]);
        Program.Assert(!vm.IsLobbyConfigurationOpen && vm.IsNewGameSetupOpen,
            "Escape from mode selection did not return to the home lobby.");
        vm.OpenLobbyCategoryCommand.Execute("all");
        Program.Assert(vm.IsModeSelectionOpen, "Reopening setup retained the previous difficulty step.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    public static void RefreshBoundSelection(MainViewModel vm, ListBox choices)
    {
        var failures = new List<string>();
        void OnException(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is NullReferenceException &&
                args.Exception.StackTrace?.Contains("MainViewModel", StringComparison.Ordinal) == true)
                failures.Add(args.Exception.ToString());
        }

        AppDomain.CurrentDomain.FirstChanceException += OnException;
        try
        {
            var previous = vm.SelectedTableMode;
            // Exercise the real two-way binding, including exceptions WPF may catch.
            choices.SetCurrentValue(Selector.SelectedItemProperty, null);
            choices.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
            Program.Assert(vm.SelectedTableMode == previous,
                "A temporary empty ListBox selection erased the configured mode.");

            vm.SelectedModeCategory = vm.ModeCategories.Single(category => category.Id == "identity");
            var fivePlayer = vm.VisibleTableModes.Single(mode => mode.ModeId == "identity:classic-5");
            choices.SetCurrentValue(Selector.SelectedItemProperty, fivePlayer);
            choices.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
            foreach (var categoryId in new[] { "all", "identity", "all", "team", "national", "activity", "identity", "all" })
            {
                var retained = vm.SelectedTableMode;
                vm.SelectedModeCategory = vm.ModeCategories.Single(category => category.Id == categoryId);
                choices.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                Program.Assert(vm.SelectedTableMode is not null && vm.VisibleModeVariants.Contains(vm.SelectedTableMode) &&
                               Equals(choices.SelectedItem, vm.SelectedTableMode),
                    "Refreshing mode cards lost a valid selection or left the view and setup out of sync.");
                Program.Assert(!vm.VisibleTableModes.Contains(retained) || vm.SelectedTableMode == retained,
                    "Switching to a category containing the selected mode reset the user's choice.");
                Program.Assert(vm.GuideIdentity.Contains(vm.SelectedTableMode!.Name, StringComparison.Ordinal),
                    "The setup guide did not follow the selected mode.");
            }
            Program.Assert(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnException;
        }
    }
}
