using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CardGame.Wpf;
using CardGame.Wpf.ViewModels;

internal static class GeneralSelectionChecks
{
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
            vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "national:lite-4");
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
}
