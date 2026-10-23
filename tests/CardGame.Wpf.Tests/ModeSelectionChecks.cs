using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Presentation;
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
        KeepRequiredRoleSelection(vm, window, root, output);
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
            if (group.Name == "剑阁决战") CheckTeamActivityDifficulties(vm, group, choices, settings, root);
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
        var jiangeGroup = testedGroups.Single(group => group.Name == "剑阁决战");
        vm.OpenModeConfigurationCommand.Execute(jiangeGroup);
        Program.Render(root, 1120, 740, null);
        var teams = (ListBox)window.FindName("TeamChoices");
        var wei = vm.StartingTeams.Single(team => team.TeamId == "team:wei");
        teams.SetCurrentValue(Selector.SelectedItemProperty, wei);
        teams.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
        Program.Assert(vm.SelectedStartingTeam == wei, "Choosing the activity team did not update the configured side.");
        var rememberedGroup = testedGroups.First(group => group.Variants.Count > 1);
        vm.SelectedModeCategory = vm.ModeCategories.Single(category => category.Id == "identity");
        vm.SelectedModeCategory = vm.ModeCategories.Single(category => category.Id == "activity");
        vm.OpenModeConfigurationCommand.Execute(rememberedGroup);
        choices.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Program.Assert(vm.SelectedTableMode == rememberedGroup.Variants[^1] &&
                       Equals(choices.SelectedItem, vm.SelectedTableMode),
            "Returning to an activity after browsing another category lost its chosen difficulty.");
        vm.BackInLobbySetupCommand.Execute(null);
        vm.OpenModeConfigurationCommand.Execute(jiangeGroup);
        teams.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Program.Assert(vm.SelectedStartingTeam == wei && Equals(teams.SelectedItem, wei) &&
                       vm.StartingTeams.Contains(vm.SelectedStartingTeam) && vm.TeamModeSetupText.Contains(wei.Name, StringComparison.Ordinal),
            "Returning to an activity after browsing identity modes lost the configured team or its selected visual state.");
        vm.BackInLobbySetupCommand.Execute(null);
        Program.Assert(Program.Engine(vm).Revision == revision, "Browsing mode and difficulty choices changed the suspended match.");
        shortcut.Invoke(window, [Key.Escape, ModifierKeys.None]);
        Program.Assert(!vm.IsLobbyConfigurationOpen && vm.IsNewGameSetupOpen,
            "Escape from mode selection did not return to the home lobby.");
        vm.OpenLobbyCategoryCommand.Execute("all");
        Program.Assert(vm.IsModeSelectionOpen, "Reopening setup retained the previous difficulty step.");
        EnlargedLobbyContent(vm, root, home, output);
    }

    public static void RequiredChoiceInput(MainViewModel vm, MainWindow window)
    {
        Program.Assert(window.IsLoaded && vm.IsNewGameSetupOpen && !vm.IsLobbyConfigurationOpen,
            "Required setup input must use the loaded native lobby fixture.");
        var game = Program.Engine(vm);
        var revision = game.Revision;
        var views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
            .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        var journal = CommandJson.Serialize(game.AcceptedCommands);
        var category = vm.SelectedModeCategory;
        var mode = vm.SelectedTableMode;
        var role = vm.SelectedStartingRole;
        var team = vm.SelectedStartingTeam;
        var speed = vm.SelectedPlaybackSpeed;
        var focus = Keyboard.FocusedElement;
        var home = (HomeLobby)window.FindName("LobbyHome");
        var homeScroll = (ScrollViewer)home.FindName("LobbyContentScroll");
        var homeOffset = homeScroll.VerticalOffset;
        try
        {
            vm.OpenLobbyCategoryCommand.Execute("identity");
            vm.OpenModeConfigurationCommand.Execute(vm.VisibleModeGroups.Single(group => group.Variants.Contains(vm.SelectedTableMode)));
            Pump();
            Check((ListBox)window.FindName("RoleChoices"), vm.StartingRoles.Single(item => item.Role == Role.Loyalist),
                vm.StartingRoles.Single(item => item.Role == Role.Rebel), () => vm.SelectedStartingRole);
            CheckEnlargedRoleNavigation();
            vm.OpenLobbyCategoryCommand.Execute("team");
            vm.OpenModeConfigurationCommand.Execute(vm.VisibleModeGroups.Single());
            Pump();
            Check((ListBox)window.FindName("TeamChoices"), vm.StartingTeams[1], vm.StartingTeams[0],
                () => vm.SelectedStartingTeam);
            vm.OpenSettingsCommand.Execute(null);
            Pump();
            Check((ListBox)window.FindName("SettingsPlaybackSpeedSelector"), PlaybackSpeed.Fast, PlaybackSpeed.Normal,
                () => vm.SelectedPlaybackSpeed, checkNullRecovery: false);
            vm.CloseSettingsCommand.Execute(null);
            Program.Assert(ReferenceEquals(Program.Engine(vm), game) && game.Revision == revision &&
                           CommandJson.Serialize(game.AcceptedCommands) == journal &&
                           Enumerable.Range(0, views.Length).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == views[seat]),
                "Required setup input must preserve the suspended engine, every player view and accepted journal.");
        }
        finally
        {
            vm.CloseSettingsCommand.Execute(null);
            vm.SelectedPlaybackSpeed = speed;
            vm.SelectedModeCategory = category;
            vm.SelectedTableMode = mode;
            vm.SelectedStartingRole = role;
            vm.SelectedStartingTeam = team;
            vm.BackToLobbyCommand.Execute(null);
            Pump();
            if (focus is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
            homeScroll.ScrollToVerticalOffset(homeOffset);
            window.UpdateLayout();
        }
        Program.Assert(vm.SelectedModeCategory == category && vm.SelectedTableMode == mode &&
                       vm.SelectedStartingRole == role && vm.SelectedStartingTeam == team &&
                       vm.SelectedPlaybackSpeed == speed && !vm.IsSettingsOpen &&
                       !vm.IsLobbyConfigurationOpen && vm.IsModeSelectionOpen && vm.IsNewGameSetupOpen &&
                       ReferenceEquals(Keyboard.FocusedElement, focus),
            "Required setup input must restore the original lobby choices, step and keyboard focus.");

        void Check(ListBox choices, object chosen, object next, Func<object> configured, bool checkNullRecovery = true)
        {
            Select(chosen);
            Pump();
            var item = (ListBoxItem)choices.ItemContainerGenerator.ContainerFromItem(chosen);
            Program.Assert(item.Focus(), "The native required-choice fixture must focus its selected row.");
            var guide = vm.GuideIdentity;
            var summary = vm.TeamModeSetupText;
            Program.Assert(MouseDown(item, ModifierKeys.Control), "Ctrl+click on a required selected row must stop before native deselection.");
            AssertChoice(chosen);
            var keyHandled = KeyDown(Key.Space, ModifierKeys.Control, previewOnly: true);
            Program.Assert(keyHandled, "Ctrl+Space on a required selected row must stop before native deselection.");
            AssertChoice(chosen);
            Program.Assert(vm.GuideIdentity == guide && vm.TeamModeSetupText == summary,
                "Preventing empty setup choices must keep their guide and summary unchanged.");
            var nextItem = (ListBoxItem)choices.ItemContainerGenerator.ContainerFromItem(next);
            Program.Assert(!MouseDown(nextItem, ModifierKeys.Control), "Ctrl+click on another valid row must retain native selection.");
            AssertChoice(next);
            // Role and team additionally promise source-driven recovery after
            // a null binding update; speed shares the required input contract.
            if (checkNullRecovery)
            {
                Select(null);
                choices.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                AssertChoice(next);
                Select(null);
            }
            Select(chosen);
            choices.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            AssertChoice(chosen);

            void Select(object? value)
            {
                choices.SetCurrentValue(Selector.SelectedItemProperty, value);
                choices.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
            }
            void AssertChoice(object expected)
            {
                Pump();
                var selected = (ListBoxItem)choices.ItemContainerGenerator.ContainerFromItem(expected);
                Program.Assert(Equals(configured(), expected) && Equals(choices.SelectedItem, expected) && selected.IsSelected,
                    $"{choices.Name} must keep its configured value, SelectedItem and native row highlight in sync.");
                Console.WriteLine($"Required setup {choices.Name}: configured={configured()}, selected={choices.SelectedItem}, IsSelected={selected.IsSelected}.");
            }
        }

        void CheckEnlargedRoleNavigation()
        {
            var roles = (ListBox)window.FindName("RoleChoices");
            var panel = (FrameworkElement)window.FindName("ModeConfigurationPanel");
            var scroll = (ScrollViewer)window.FindName("ModeSetupScroll");
            var transform = panel.LayoutTransform;
            var selected = vm.SelectedStartingRole;
            var guide = vm.GuideIdentity;
            var width = window.Width;
            var height = window.Height;
            try
            {
                window.Width = 1120;
                window.Height = 780;
                panel.LayoutTransform = new ScaleTransform(1.5, 1.5);
                Pump();
                Program.Assert(vm.SelectedStartingRole == selected && vm.GuideIdentity == guide,
                    "Enlarging identity cards must retain their selected role and guide.");
                var first = (ListBoxItem)roles.ItemContainerGenerator.ContainerFromIndex(0);
                TableShortcutChecks.WithModifiers(ModifierKeys.None, () => first.Focus());
                scroll.ScrollToTop();
                Pump();
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, first) && scroll.ScrollableHeight > 0,
                    "The loaded compact identity fixture must focus its first row in an overflowing setup viewport.");
                var last = (ListBoxItem)roles.ItemContainerGenerator.ContainerFromIndex(roles.Items.Count - 1);
                var presenter = Program.Find<ScrollContentPresenter>(scroll).Single(item => ReferenceEquals(item.TemplatedParent, scroll));
                var before = last.TransformToAncestor(presenter).TransformBounds(new Rect(last.RenderSize));
                Program.Assert(before.Bottom > presenter.ActualHeight + 1,
                    "The final identity must begin below the native viewport before keyboard navigation.");
                Program.Assert(KeyDown(Key.End, ModifierKeys.None), "Native End must navigate the identity ListBox.");
                Pump();
                var bounds = last.TransformToAncestor(presenter).TransformBounds(new Rect(last.RenderSize));
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, last) && last.IsSelected &&
                    Equals(roles.SelectedItem, vm.StartingRoles[^1]) && vm.SelectedStartingRole == vm.StartingRoles[^1] &&
                    vm.GuideIdentity.Contains(vm.StartingRoles[^1].Name, StringComparison.Ordinal) &&
                    bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= presenter.ActualWidth + 1 &&
                    bounds.Bottom <= presenter.ActualHeight + 1 && ReferenceEquals(Program.Engine(vm), game) &&
                    game.Revision == revision && CommandJson.Serialize(game.AcceptedCommands) == journal &&
                    Enumerable.Range(0, views.Length).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == views[seat]),
                    "Native End must select the last enlarged identity, reveal its complete card, update the guide and preserve every player view and accepted journal.");
            }
            finally
            {
                panel.LayoutTransform = transform;
                window.Width = width;
                window.Height = height;
                roles.SetCurrentValue(Selector.SelectedItemProperty, selected);
                roles.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
                Pump();
            }
        }

        bool KeyDown(Key key, ModifierKeys modifiers, bool previewOnly = false)
        {
            var target = (UIElement)Keyboard.FocusedElement;
            return TableShortcutChecks.WithModifiers(modifiers, () =>
            {
                var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                target.RaiseEvent(input);
                var handled = input.Handled;
                if (!handled) { input.RoutedEvent = Keyboard.KeyDownEvent; target.RaiseEvent(input); }
                return previewOnly ? handled : input.Handled;
            });
        }

        static bool MouseDown(ListBoxItem item, ModifierKeys modifiers) => TableShortcutChecks.WithModifiers(modifiers, () =>
        {
            var target = Descendants(item).OfType<TextBlock>().First();
            var input = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent };
            target.RaiseEvent(input);
            var handled = input.Handled;
            if (!handled) { input.RoutedEvent = Mouse.MouseDownEvent; target.RaiseEvent(input); }
            return handled;
        });
        void Pump()
        {
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.UpdateLayout();
        }
    }

    private static void EnlargedLobbyContent(MainViewModel vm, FrameworkElement root, HomeLobby home, string output)
    {
        // Content zoom at the minimum client size, not physical DPI acceptance.
        var scroll = (ScrollViewer)home.FindName("LobbyContentScroll");
        var content = (FrameworkElement)scroll.Content;
        var originalTransform = home.LayoutTransform;
        var category = vm.SelectedModeCategory;
        var revision = Program.Engine(vm).Revision;
        var failures = new List<string>();
        try
        {
            vm.BackToLobbyCommand.Execute(null);
            home.LayoutTransform = new ScaleTransform(1.5, 1.5);
            scroll.ScrollToTop();
            Program.Render(root, 1120, 740, Path.Combine(output, "134-home-lobby-enlarged.png"));
            var presenter = Program.Find<ScrollContentPresenter>(scroll).Single(item => ReferenceEquals(item.TemplatedParent, scroll));
            foreach (var (labelName, ownerName) in new[]
            {
                ("LobbyWelcomeCaption", "LobbyWelcomeBanner"), ("LobbyFeaturedTitle", "EnterBattleButton"),
                ("ActivityBattleTitle", "ActivityBattleButton"), ("ShanheCampaignTitle", "ShanheCampaignButton"),
                ("WushuangBattleTitle", "WushuangBattleButton")
            })
            {
                var label = (TextBlock)home.FindName(labelName);
                var owner = (FrameworkElement)home.FindName(ownerName);
                var position = label.TransformToAncestor(content).Transform(new Point());
                scroll.ScrollToVerticalOffset(position.Y);
                root.UpdateLayout();
                Program.Render(root, 1120, 740, null);
                var fullText = new TextBlock
                {
                    Text = label.Text, FontFamily = label.FontFamily, FontSize = label.FontSize,
                    FontStyle = label.FontStyle, FontWeight = label.FontWeight, FontStretch = label.FontStretch,
                    TextWrapping = label.TextWrapping, LineHeight = label.LineHeight, Language = label.Language
                };
                fullText.Measure(new Size(label.TextWrapping == TextWrapping.NoWrap ? double.PositiveInfinity : label.ActualWidth,
                    double.PositiveInfinity));
                if (!Fits(label, owner) || !Fits(label, presenter) || fullText.DesiredSize.Width > label.ActualWidth + 1 ||
                    fullText.DesiredSize.Height > label.ActualHeight + 1)
                    failures.Add($"Enlarged lobby text '{label.Text}' must fit its owner and remain fully reachable: actual={label.RenderSize}, required={fullText.DesiredSize}.");
            }
            scroll.ScrollToEnd();
            root.UpdateLayout();
            Program.Render(root, 1120, 740, Path.Combine(output, "134-home-lobby-enlarged-bottom.png"));
            var settings = (Button)home.FindName("LobbySettingsButton");
            if (!Fits(settings, root)) failures.Add("The fixed lobby footer must stay reachable while enlarged central content scrolls.");
            Program.Assert(failures.Count == 0 && Program.Engine(vm).Revision == revision,
                string.Join(Environment.NewLine, failures));
        }
        finally
        {
            home.LayoutTransform = originalTransform;
            scroll.ScrollToTop();
            vm.OpenLobbyCategoryCommand.Execute(category.Id);
            Program.Render(root, 1120, 740, null);
        }

        static bool Fits(FrameworkElement element, FrameworkElement viewport)
        {
            var bounds = element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize));
            return element.ActualWidth > 0 && element.ActualHeight > 0 && bounds.Left >= -1 && bounds.Top >= -1 &&
                bounds.Right <= viewport.ActualWidth + 1 && bounds.Bottom <= viewport.ActualHeight + 1;
        }
    }

    private static void CheckTeamActivityDifficulties(MainViewModel vm, TableModeGroupOption group, ListBox choices,
        FrameworkElement settings, FrameworkElement root)
    {
        var game = Program.Engine(vm);
        var revision = game.Revision;
        var team = vm.SelectedStartingTeam;
        var summary = Descendants(settings).OfType<TextBlock>().Single(text =>
            text.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path?.Path == "SelectedTableMode.Description");
        Program.Assert(group.Variants.Select(mode => mode.Description).Distinct().Count() == group.Variants.Count,
            "Activity difficulty cards must explain their different tiers instead of repeating one description.");
        foreach (var mode in group.Variants)
        {
            var rules = game.ContentRegistry.Modes[mode.ModeId].TeamActivity!;
            choices.SetCurrentValue(Selector.SelectedItemProperty, mode);
            choices.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
            Program.Render(root, 1120, 740, null);
            var item = (ListBoxItem)choices.ItemContainerGenerator.ContainerFromItem(mode);
            var description = Descendants(item).OfType<TextBlock>().Single(text =>
                text.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path?.Path == "Description");
            var lines = description.Text.Split('\n');
            var spiritTiers = rules.SpiritTierMinimum == rules.SpiritTierMaximum
                ? $"{rules.SpiritTierMinimum} 阶" : $"{rules.SpiritTierMinimum}–{rules.SpiritTierMaximum} 阶";
            var bounds = description.TransformToAncestor(item).TransformBounds(new Rect(description.RenderSize));
            Program.Assert(vm.SelectedTableMode == mode && item.IsSelected && summary.Text == description.Text &&
                           lines[0].Contains("人机搭档", StringComparison.Ordinal) && lines[0].Contains($"{rules.PlayerTier} 阶", StringComparison.Ordinal) &&
                           lines[1].Contains($"英灵 {spiritTiers}", StringComparison.Ordinal) &&
                           lines[1].Contains($"双方器械 {rules.MachineTier} 阶", StringComparison.Ordinal) &&
                           bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= item.ActualWidth + 1 && bounds.Bottom <= item.ActualHeight + 1 &&
                           vm.SelectedStartingTeam == team && game.Revision == revision,
                "Selecting a difficulty must show its registered player, spirit and machine tiers in a complete card and matching summary without changing the team or match.");
        }
    }

    private static void CheckRoleCardLayout(MainViewModel vm, MainWindow window, FrameworkElement root, string output)
    {
        var roles = (ListBox)window.FindName("RoleChoices");
        var panel = (FrameworkElement)window.FindName("ModeConfigurationPanel");
        var scroll = (ScrollViewer)window.FindName("ModeSetupScroll");
        var transform = panel.LayoutTransform;
        var selected = vm.SelectedStartingRole;
        var guide = vm.GuideIdentity;
        var game = Program.Engine(vm);
        var revision = game.Revision;
        try
        {
            // Content zoom exercises logical width; it is not physical DPI acceptance.
            foreach (var (width, height, scale, columns, render) in new[]
            {
                (1440, 880, 1d, 3, "134-identity-setup.png"),
                (1120, 740, 1d, 3, (string?)null),
                (1120, 740, 1.5, 2, "134-identity-setup-enlarged.png"),
                // Both current configuration shells become narrower than two 220-DIP cards.
                (1120, 740, 2.5, 1, (string?)null)
            })
            {
                panel.LayoutTransform = new ScaleTransform(scale, scale);
                scroll.ScrollToTop();
                Program.Render(root, width, height, render is null ? null : Path.Combine(output, render));
                var grid = Program.Find<UniformGrid>(roles).Single();
                var cards = vm.StartingRoles.Select(role => (ListBoxItem)roles.ItemContainerGenerator.ContainerFromItem(role)).ToArray();
                var rows = cards.Select(card => Math.Round(card.TranslatePoint(new Point(), grid).Y, 2)).Distinct().Count();
                Program.Assert(grid.Columns == columns && rows == (int)Math.Ceiling((double)cards.Length / columns),
                    $"Identity cards at {width}x{height}, content zoom {scale} must use {columns} columns and complete compact rows, actual columns={grid.Columns}, rows={rows}.");
                foreach (var card in cards)
                {
                    var description = Descendants(card).OfType<TextBlock>().Single(text =>
                        text.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path?.Path == "Description");
                    var fullText = new TextBlock { Text = description.Text, FontFamily = description.FontFamily,
                        FontSize = description.FontSize, FontStyle = description.FontStyle, FontWeight = description.FontWeight,
                        FontStretch = description.FontStretch, TextWrapping = description.TextWrapping,
                        LineHeight = description.LineHeight, Language = description.Language };
                    fullText.Measure(new Size(description.ActualWidth, double.PositiveInfinity));
                    var bounds = description.TransformToAncestor(card).TransformBounds(new Rect(description.RenderSize));
                    Program.Assert(description.ActualWidth > 0 && description.ActualHeight >= fullText.DesiredSize.Height - 1 &&
                        fullText.DesiredSize.Width <= description.ActualWidth + 1 &&
                        bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= card.ActualWidth + 1 &&
                        bounds.Bottom <= card.ActualHeight + 1,
                        $"The complete identity description '{description.Text}' must fit its card at content zoom {scale}.");
                }
                Program.Assert(vm.SelectedStartingRole == selected && Equals(roles.SelectedItem, selected) &&
                    ((ListBoxItem)roles.ItemContainerGenerator.ContainerFromItem(selected)).IsSelected &&
                    vm.GuideIdentity == guide && ReferenceEquals(Program.Engine(vm), game) && game.Revision == revision,
                    "Responsive identity layout must retain the selected role, its highlight, guide and suspended game.");
            }
        }
        finally
        {
            panel.LayoutTransform = transform;
            scroll.ScrollToTop();
            Program.Render(root, 1120, 740, null);
        }
    }

    private static void KeepRequiredRoleSelection(MainViewModel vm, MainWindow window, FrameworkElement root, string output)
    {
        var revision = Program.Engine(vm).Revision;
        var originalRole = vm.SelectedStartingRole;
        vm.SelectedModeCategory = vm.ModeCategories.Single(category => category.Id == "identity");
        vm.OpenModeConfigurationCommand.Execute(vm.VisibleModeGroups.Single(group => group.Variants.Contains(vm.SelectedTableMode)));
        Program.Render(root, 1120, 740, null);
        var roles = (ListBox)window.FindName("RoleChoices");
        var chosen = vm.StartingRoles.Single(role => role.Role == Role.Loyalist);
        roles.SetCurrentValue(Selector.SelectedItemProperty, chosen);
        roles.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
        var guide = vm.GuideIdentity;
        roles.SetCurrentValue(Selector.SelectedItemProperty, null);
        roles.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
        roles.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Program.Render(root, 1120, 740, null);
        Program.Assert(vm.SelectedStartingRole == chosen && Equals(roles.SelectedItem, chosen) &&
                       ((ListBoxItem)roles.ItemContainerGenerator.ContainerFromItem(chosen)).IsSelected &&
                       vm.GuideIdentity == guide && guide.Contains(chosen.Name, StringComparison.Ordinal) &&
                       Program.Engine(vm).Revision == revision,
            "Clearing the required identity must retain its last role, selected visual state, guide and suspended match.");
        roles.SetCurrentValue(Selector.SelectedItemProperty, null);
        roles.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
        roles.SetCurrentValue(Selector.SelectedItemProperty, originalRole);
        roles.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
        roles.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Program.Assert(vm.SelectedStartingRole == originalRole && Equals(roles.SelectedItem, originalRole) &&
                       vm.GuideIdentity.Contains(originalRole.Name, StringComparison.Ordinal) && Program.Engine(vm).Revision == revision,
            "Deferred identity selection recovery must preserve a newer valid choice.");
        CheckRoleCardLayout(vm, window, root, output);
        vm.BackInLobbySetupCommand.Execute(null);
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
