using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class GeneralSelectionChecks
{
    public static void RelatedVariants(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new EditionPackage());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = EditionPackage.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The edition fixture must start through a real command.");
        for (var step = 0; step < 16 && game.PendingDecision?.Kind != DecisionKind.SelectGeneral; step++)
            Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "The edition fixture must reach its actual selection prompt.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var vm = new MainViewModel(autoAdvance: false, seed: 31, showSetup: true, saveStore: store,
            useExpandedContent: false, contentRegistry: registry) { IsMotionEnabled = false };
        vm.LoadManualGameCommand.Execute(null);
        var engine = Program.Engine(vm);
        var revision = engine.Revision;
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        try
        {
            var ling = vm.GeneralChoices.Single(choice => EditionPackage.Ling.Contains(choice.GeneralId));
            vm.PreviewGeneralChoiceCommand.Execute(ling);
            Program.Assert(vm.SelectedGeneralVariants.Select(choice => choice.GeneralId).ToHashSet().SetEquals(EditionPackage.Ling),
                "Ling Tong must expose both ordinary and boundary editions.");
            vm.PreviewGeneralChoiceCommand.Execute(vm.SelectedGeneralVariants.Single(choice => choice.GeneralId == "boundary:ling-tong"));
            Program.Assert(vm.GeneralChoices.Count == 3 && vm.GeneralChoices.Any(choice => choice.GeneralId == "boundary:ling-tong") &&
                vm.SelectedGeneralChoice!.SkillDescription.Contains("旋风"), "Switching editions must replace the same candidate slot and its skills.");
            vm.PreviewGeneralChoiceCommand.Execute(vm.GeneralChoices.Single(choice => EditionPackage.Zhao.Contains(choice.GeneralId)));
            Program.Assert(vm.SelectedGeneralVariants.Select(choice => choice.GeneralId).ToHashSet().SetEquals(EditionPackage.Zhao),
                "Zhao Yun must expose the requested ordinary, boundary, SP, God and Gundam editions.");
            foreach (var id in EditionPackage.Zhao)
            {
                Program.Render(root, 1120, 820, Path.Combine(output, "general-variant-before.png"));
                var choices = (ItemsControl)window.FindName("GeneralVariantChoices");
                var button = Program.Find<Button>(choices).Single(button => button.DataContext is GeneralChoiceViewModel choice && choice.GeneralId == id);
                button.Command!.Execute(button.CommandParameter);
                Program.Render(root, 1120, 820, Path.Combine(output, $"general-variant-{id.Replace(':', '_')}.png"));
                Program.Assert(vm.SelectedGeneralChoice!.GeneralId == id && vm.GeneralChoices.Count == 3 &&
                    vm.GeneralChoices.Count(choice => choice.IsPreviewSelected) == 1 && engine.Revision == revision &&
                    vm.SelectedGeneralChoice.HealthText.Contains((registry.Generals[id].BaseHp + 1).ToString()),
                    "An edition click must update portrait, health and skills without submitting or adding candidate slots.");
            }
            vm.SaveGameCommand.Execute(null);
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(vm.GeneralChoices.Count == 3 && vm.SelectedGeneralVariants.Count == 6,
                "Reloading a pending prompt lost its edition choices or changed its candidate count.");
            vm.PreviewGeneralChoiceCommand.Execute(vm.SelectedGeneralVariants.Single(choice => choice.GeneralId == "classic:gao-da-yi-hao"));
            vm.ConfirmGeneralChoiceCommand.Execute(null);
            Program.Assert(Program.Engine(vm).CreateSnapshot(0).Players[0].GeneralId == "classic:gao-da-yi-hao" &&
                Program.Engine(vm).Revision == revision + 1, "Confirm must submit the exact selected Gundam edition once.");
        }
        finally { window.Content = null; window.Close(); }
    }

    public static void IdentityRowsAndSeats(string output)
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore(), useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        try
        {
            foreach (var (role, columns, count) in new[] { (CardGame.Core.Role.Lord, 7, 13), (CardGame.Core.Role.Loyalist, 4, 8), (CardGame.Core.Role.Rebel, 4, 8), (CardGame.Core.Role.Renegade, 7, 13) })
            {
                // Force a role only in this fixture; the actual lobby offers random identity alone.
                vm.SelectedStartingRole = new StartingRoleOption("fixture", role, string.Empty);
                vm.StartNewGameCommand.Execute(null);
                vm.ContinueFromIdentityRevealCommand.Execute(null);
                Program.Assert(vm.GeneralChoices.Count == count && vm.GeneralChoiceColumns == columns,
                    "Identity role did not determine the correct general-choice count.");
                foreach (var width in new[] { 1120, 1440 })
                {
                    Program.Render(root, width, 880, Path.Combine(output, $"identity-choices-{count}-{width}.png"));
                    var cards = (ItemsControl)window.FindName("GeneralCandidateCards");
                    var buttons = Program.Find<Button>(cards).Where(button => button.Command == vm.PreviewGeneralChoiceCommand).ToArray();
                    var positions = buttons.Select(button => button.TranslatePoint(new Point(), root)).ToArray();
                    var rows = positions.GroupBy(point => Math.Round(point.Y, 1)).OrderBy(group => group.Key).ToArray();
                    Program.Assert(buttons.Length == count && rows.Length == 2 && rows[0].Count() == columns &&
                                   rows[1].Count() == count - columns && buttons.All(button => button.ActualWidth > 0),
                        "The general panel must keep exactly the requested two rows at both window sizes.");
                    var panel = (Border)window.FindName("GeneralSelectionPanel");
                    var bounds = new Rect(panel.TranslatePoint(new Point(), root), panel.RenderSize);
                    Program.Assert(buttons.All(button => bounds.Contains(button.TranslatePoint(new Point(button.ActualWidth, button.ActualHeight), root))),
                        "A candidate was clipped or placed outside the selection panel.");
                }
                var lord = vm.Seats.Single(seat => seat.RoleLabel == "主公");
                Program.Assert(lord.DisplaySeatNumber == 1 && vm.Seats.All(seat =>
                    seat.DisplaySeatNumber == (seat.Seat - lord.Seat + vm.Seats.Count) % vm.Seats.Count + 1),
                    "Seat numbering must start at the Lord and follow the right-hand direction.");
                Program.Assert(vm.HumanPlayer!.SeatLabel.Contains($"{vm.HumanPlayer.DisplaySeatNumber:00}", StringComparison.Ordinal),
                    "The human label still assumes the human is seat one.");
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    public static void InputBoundaries(string output)
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore(), useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        try
        {
            Program.Render(root, 1120, 740, Path.Combine(output, "general-input-selection.png"));
            var candidates = (ItemsControl)window.FindName("GeneralCandidateCards");
            Button Card(GeneralChoiceViewModel choice) => Program.Find<Button>(candidates)
                .Single(button => ReferenceEquals(button.DataContext, choice));
            var selected = vm.GeneralChoices[1];
            var engine = Program.Engine(vm);
            var revision = engine.Revision;
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Card(selected), null);
            Program.Assert(ReferenceEquals(vm.SelectedGeneralChoice, selected) && engine.Revision == revision,
                "The first candidate click must remain a reversible preview.");

            foreach (var (name, setOpen) in new (string, Action<bool>)[]
            {
                ("guide", open => vm.IsHelpOpen = open),
                ("log", open => vm.IsLogOpen = open),
                ("lobby", open => vm.IsNewGameSetupOpen = open),
                ("settings", open => vm.IsSettingsOpen = open),
                ("history", open => vm.IsHistoryOpen = open),
                ("gallery", open => vm.IsGeneralGalleryOpen = open)
            })
            {
                setOpen(true);
                Shortcut(window, Key.D1);
                Shortcut(window, Key.Enter);
                Program.Assert(engine.Revision == revision && ReferenceEquals(vm.SelectedGeneralChoice, selected),
                    $"The {name} overlay allowed keyboard input to change or confirm a hidden candidate.");
                var handled = DoubleClick(Card(selected));
                Program.Assert(!handled && engine.Revision == revision && ReferenceEquals(vm.SelectedGeneralChoice, selected),
                    $"The {name} overlay allowed a hidden candidate to be changed or confirmed.");
                setOpen(false);
            }

            Program.Assert(!DoubleClick(Card(selected), MouseButton.Right) &&
                           !DoubleClick(candidates) &&
                           !DoubleClick(Card(vm.GeneralChoices[0])) && engine.Revision == revision,
                "Right clicks, empty space or unselected candidates committed a choice.");
            var selectedButton = Card(selected);
            Program.Assert(DoubleClick(Program.Find<System.Windows.Shapes.Rectangle>(selectedButton).First()) &&
                           engine.Revision == revision + 1 &&
                           !vm.IsGeneralSelectionPending,
                "Double-clicking the previewed candidate must confirm exactly one choice.");
            Program.Assert(!DoubleClick(selectedButton) && engine.Revision == revision + 1,
                "A stale candidate submitted a second command after selection ended.");

            vm.NewGameCommand.Execute(null);
            vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "national:ambitious-6");
            vm.StartNewGameCommand.Execute(null);
            engine = Program.Engine(vm);
            revision = engine.Revision;
            Program.Render(root, 1120, 740, Path.Combine(output, "general-input-identity.png"));
            Program.Assert(vm.IsIdentityRevealOpen && vm.IsGeneralSelectionPending &&
                           !DoubleClick(Card(vm.SelectedGeneralChoice!)) && engine.Revision == revision,
                "A candidate click bypassed the identity reveal.");
            Program.Assert(Shortcut(window, Key.Enter) && !vm.IsIdentityRevealOpen && engine.Revision == revision,
                "Enter must dismiss the identity reveal without also choosing a general.");
            var primary = vm.SelectedGeneralChoice!;
            var primaryButton = Card(primary);
            var primaryPrompt = engine.PendingDecision!.PromptId;
            var primaryHandled = DoubleClick(primaryButton);
            Program.Assert(primaryHandled && engine.Revision == revision + 1,
                "The primary general double-click submitted more than one command.");
            Program.Assert(!DoubleClick(primaryButton) && engine.Revision == revision + 1,
                "A primary general's stale input advanced the selection transition.");
            for (var step = 0; step < 8 && !vm.IsGeneralSelectionPending; step++) vm.StepAiCommand.Execute(null);
            Program.Assert(vm.IsGeneralSelectionPending && engine.PendingDecision!.PromptId != primaryPrompt,
                "The national fixture did not reach its separate secondary general prompt.");
            var secondaryRevision = engine.Revision;
            Program.Assert(!DoubleClick(primaryButton) && engine.Revision == secondaryRevision,
                "A primary general's stale input selected the next slot.");
            Program.Render(root, 1120, 740, Path.Combine(output, "general-input-secondary.png"));
            Program.Assert(vm.GeneralChoices.All(choice => choice.GeneralId != primary.GeneralId),
                "The secondary prompt retained the already chosen primary general.");
            Program.Assert(Shortcut(window, Key.D1) && engine.Revision == secondaryRevision &&
                           ReferenceEquals(vm.SelectedGeneralChoice, vm.GeneralChoices[0]),
                "Number keys did not preview the current prompt without submitting it.");
            Program.Assert(DoubleClick(Card(vm.SelectedGeneralChoice!)) &&
                           engine.Revision == secondaryRevision + 1 && !vm.IsGeneralSelectionPending,
                "The secondary general did not confirm once through its own card.");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static bool Shortcut(MainWindow window, Key key) => (bool)typeof(MainWindow)
        .GetMethod("HandleShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, [key, ModifierKeys.None])!;

    private static bool DoubleClick(UIElement source, MouseButton button = MouseButton.Left)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, button)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        };
        // WPF normally sets this while reading native input. Drive the actual preview
        // route so Control must generate the double-click and consume its second press.
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(args, 2);
        source.RaiseEvent(args);
        return args.Handled;
    }

    private sealed class EditionPackage : IGameContentPackage
    {
        internal const string Mode = "identity:classic-variants-ui-5";
        internal static readonly string[] Ling = ["classic:ling-tong", "boundary:ling-tong"];
        internal static readonly string[] Zhao = ["classic:zhao-yun", "boundary:zhao-yun", "sp:zhao-yun", "classic:shen-zhao-yun", "ol:shen-zhao-yun", "classic:gao-da-yi-hao"];
        public PackageManifest Manifest { get; } = new("general-edition-ui-check", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder) => builder.AddMode(new(Mode, "版本切换检查", 5, 5,
            new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 },
            "classic:standard-deck", GeneralCandidateCount: 3, GeneralPoolIds: Ling.Concat(Zhao).Append("classic:cao-cao").ToArray())
            { GeneralVariantGroups = new Dictionary<string, IReadOnlyList<string>> { ["character:ling-tong"] = Ling, ["character:zhao-yun"] = Zhao } });
    }
}
