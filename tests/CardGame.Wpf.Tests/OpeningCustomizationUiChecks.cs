using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class OpeningCustomizationUiChecks
{
    public static void RolesRosterAndHandConfirmation(string output)
    {
        using var vm = new MainViewModel(autoAdvance: false, seed: 31, showSetup: true,
            saveStore: new MemorySaveStore(), confirmOpeningHand: true) { IsMotionEnabled = false };
        vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "identity:standard-5");
        vm.StartNewGameFromLobbyCommand.Execute(null);
        GeneralChoiceViewModel? retiredIdentityChoice = null;
        foreach (var role in new[] { Role.Loyalist, Role.Rebel, Role.Renegade, Role.Lord })
        {
            var option = vm.StartingRoles.Single(option => option.Role == role);
            vm.ChangeStartingIdentityCommand.Execute(option);
            var snapshot = Program.Engine(vm).CreateSnapshot(0, revealAll: true);
            Program.Assert(vm.IsIdentityRevealOpen && vm.CanCustomizeStartingIdentity &&
                snapshot.Players[0].Role == role && snapshot.Players.Count(p => p.Role == Role.Lord) == 1 &&
                snapshot.Players.Count(p => p.Role == Role.Loyalist) == 1 &&
                snapshot.Players.Count(p => p.Role == Role.Rebel) == 2 &&
                snapshot.Players.Count(p => p.Role == Role.Renegade) == 1,
                "Changing an assigned identity rebuilds a valid five-seat role distribution before general confirmation.");
            var game = Program.Engine(vm);
            if (retiredIdentityChoice is not null)
            {
                var unchanged = SnapshotJson.Serialize(game.CreateSnapshot(0));
                var acceptedBeforeRetiredChoice = game.AcceptedCommands.Count;
                vm.SelectGeneralChoiceCommand.Execute(retiredIdentityChoice);
                Program.Assert(SnapshotJson.Serialize(game.CreateSnapshot(0)) == unchanged &&
                    game.AcceptedCommands.Count == acceptedBeforeRetiredChoice,
                    "Changing identity must retire the previous identity's roster controls without selecting a general.");
            }
            if (role == Role.Lord)
            {
                Program.Assert(!vm.GeneralSelectionSubtitle.Contains(snapshot.Players[0].GeneralName, StringComparison.Ordinal),
                    "The unselected human lord must not expose a placeholder general in the selection subtitle.");
                continue;
            }
            var humanView = game.CreateSnapshot(0);
            var lord = humanView.Players.Single(player => player.Role == Role.Lord);
            var lordSeat = vm.Seats.Single(seat => seat.Seat == lord.Seat);
            Program.Assert(lord.IsGeneralPublic && lord.GeneralId.Length > 0 && lord.Skills is not null &&
                lord.Skills.Select(skill => (skill.ContentId, skill.Name, skill.Description))
                    .SequenceEqual(snapshot.Players[lord.Seat].Skills!.Select(skill => (skill.ContentId, skill.Name, skill.Description))) &&
                lordSeat.GeneralId == lord.GeneralId && lordSeat.GeneralName == lord.GeneralName && lordSeat.DisplaySeatNumber == 1 &&
                vm.GeneralSelectionSubtitle.Contains(lord.GeneralName, StringComparison.Ordinal) &&
                humanView.Players.Where(player => !player.IsHuman && player.Seat != lord.Seat).All(player =>
                    !player.IsGeneralPublic && player.GeneralId.Length == 0 && player.Skills is null) &&
                vm.Seats.Where(seat => !seat.IsHuman && seat.Seat != lord.Seat).All(seat => seat.GeneralId.Length == 0) &&
                game.Events.Select(item => item.Payload).OfType<GeneralSelectedEvent>().Single().ActorSeat == lord.Seat,
                "A non-lord human must see the already selected public lord while every remaining AI general is still unselected and hidden.");
            vm.ToggleGeneralRosterCommand.Execute(null);
            var expectedRoster = game.ContentRegistry.Modes[game.ModeId].GeneralPoolIds!
                .Where(id => id != lord.GeneralId).ToHashSet(StringComparer.Ordinal);
            Program.Assert(vm.IsCustomGeneralSelectionOpen &&
                vm.CustomGeneralChoices.Select(choice => choice.GeneralId).ToHashSet(StringComparer.Ordinal).SetEquals(expectedRoster) &&
                Enumerable.Range(1, humanView.Players.Count - 1).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
                "The non-lord's private full roster must contain every mode general except the public lord, without deductions from other AI selections.");
            retiredIdentityChoice = vm.CustomGeneralChoices[0];
        }
        using var host = new NativeResponseStatusChecks.NativeHost(vm);
        var window = host.Window;
        var root = host.Root;
        SetFocus(new WindowInteropHelper(window).Handle);
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        try
        {
            Program.Render(root, 1120, 820, System.IO.Path.Combine(output, "opening-customization-identity.png"));
            Program.Assert(((ItemsControl)window.FindName("IdentityOverrideChoices")).Items.Count == 5,
                "The identity reveal panel exposes random and all four explicit identities.");
            var roleButtons = Program.Find<Button>((ItemsControl)window.FindName("IdentityOverrideChoices")).ToArray();
            Press(roleButtons.Single(button => ((StartingRoleOption)button.DataContext).Role == Role.Rebel), Key.Enter);
            Program.Assert(vm.IsIdentityRevealOpen && Program.Engine(vm).CreateSnapshot(0).Players[0].Role == Role.Rebel,
                "Enter on the actual identity button changes the role without leaving its reveal panel.");
            var previousIdentityGame = Program.Engine(vm);
            Press(roleButtons.Single(button => ((StartingRoleOption)button.DataContext).Role == Role.Loyalist), Key.Enter,
                modifiers: ModifierKeys.Shift);
            Program.Assert(vm.IsIdentityRevealOpen && vm.IsGeneralSelectionPending &&
                !ReferenceEquals(Program.Engine(vm), previousIdentityGame) &&
                Program.Engine(vm).CreateSnapshot(0).Players[0].Role == Role.Loyalist,
                "Shift+Enter on the actual identity button must select its role through the native button without continuing identity reveal.");
            vm.ContinueFromIdentityRevealCommand.Execute(null);
            vm.ToggleGeneralRosterCommand.Execute(null);
            Program.Render(root, 1120, 820, System.IO.Path.Combine(output, "opening-customization-public-lord.png"));
            var selectionView = Program.Engine(vm).CreateSnapshot(0);
            var selectionHuman = selectionView.Players.Single(player => player.Seat == selectionView.HumanSeat);
            var publicLord = selectionView.Players.Single(player => player.Role == Role.Lord);
            var contextTitle = (TextBlock)window.FindName("GeneralSelectionContextTitleText");
            var contextDetail = (TextBlock)window.FindName("GeneralSelectionContextDetailText");
            var selectionPanel = (Border)window.FindName("GeneralSelectionPanel");
            var panelBounds = selectionPanel.TransformToAncestor(root).TransformBounds(new Rect(selectionPanel.RenderSize));
            var titleBounds = contextTitle.TransformToAncestor(root).TransformBounds(new Rect(contextTitle.RenderSize));
            var detailBounds = contextDetail.TransformToAncestor(root).TransformBounds(new Rect(contextDetail.RenderSize));
            Program.Assert(!vm.IsIdentityRevealOpen && vm.IsCustomGeneralSelectionOpen &&
                selectionHuman.Role == Role.Loyalist &&
                selectionView.PendingDecision is { Kind: DecisionKind.SelectGeneral } selectionPrompt &&
                selectionPrompt.PlayerSeat == selectionHuman.Seat &&
                publicLord.IsRoleRevealed && publicLord.IsGeneralPublic && !publicLord.IdentityConcealed && publicLord.GeneralId.Length > 0 &&
                contextTitle.IsVisible && contextDetail.IsVisible &&
                contextTitle.IsDescendantOf(selectionPanel) && contextDetail.IsDescendantOf(selectionPanel) &&
                titleBounds.Width > 0 && titleBounds.Height > 0 && detailBounds.Width > 0 && detailBounds.Height > 0 &&
                panelBounds.Contains(titleBounds) && panelBounds.Contains(detailBounds) && titleBounds.Right <= detailBounds.Left &&
                contextTitle.Text == "你的身份 · 忠臣" && contextTitle.Text == vm.GeneralSelectionContextTitle &&
                contextDetail.Text == $"主公 · {publicLord.GeneralName} · 1 号位" && contextDetail.Text == vm.GeneralSelectionContextDetail &&
                selectionView.Players.Where(player => player.Seat != selectionHuman.Seat && player.Seat != publicLord.Seat)
                    .All(player => player.Role is null && !player.IsGeneralPublic && player.GeneralId.Length == 0),
                "The actual roster context must visibly show this viewer's Loyalist identity and the snapshot's public Lord without clipping, overlap or exposing other hidden identities or generals.");
            Press(Program.Find<Button>(root).Single(button => button.IsVisible &&
                ReferenceEquals(button.Command, vm.ChangeStartingIdentityCommand) &&
                button.CommandParameter is StartingRoleOption { Role: Role.Lord }), Key.Enter);
            vm.ContinueFromIdentityRevealCommand.Execute(null);
            var candidates = vm.GeneralChoices.Select(choice => choice.GeneralId).ToHashSet();
            var retired = vm.GeneralChoices[0];
            vm.ToggleGeneralRosterCommand.Execute(null);
            Program.Assert(vm.IsCustomGeneralSelectionOpen && vm.CustomGeneralChoices.Count > candidates.Count,
                "Self selection opens the full currently available mode roster.");
            var custom = vm.CustomGeneralChoices.First(choice => !candidates.Contains(choice.GeneralId));
            vm.CustomGeneralSearch = custom.Name;
            Program.Assert(vm.CustomGeneralChoices.Contains(custom) && vm.CustomGeneralChoices.All(choice =>
                choice.Name.Contains(custom.Name, StringComparison.OrdinalIgnoreCase)), "The roster searches real general names.");
            vm.PreviewGeneralChoiceCommand.Execute(custom);
            var before = SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0));
            vm.SelectGeneralChoiceCommand.Execute(retired);
            Program.Assert(before == SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)),
                "A retired candidate control cannot confirm against the expanded roster.");
            Program.Render(root, 1120, 820, System.IO.Path.Combine(output, "opening-customization-roster.png"));
            Program.Assert(((Button)window.FindName("ToggleGeneralRosterButton")).Visibility == Visibility.Visible &&
                ((TextBox)window.FindName("CustomGeneralSearchBox")).Visibility == Visibility.Visible &&
                ((TextBox)window.FindName("CustomGeneralSearchBox")).ActualWidth > 0 &&
                ((ListBox)window.FindName("CustomGeneralRoster")).Items.Count > 0,
                "The self-selection search and matching roster are visible on the actual selection panel.");
            var search = (TextBox)window.FindName("CustomGeneralSearchBox");
            search.Focus();
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, search) &&
                !Input(Key.D1) && !Input(Key.D1, repeat: true), "Search digits are not candidate shortcuts, even when held.");
            before = SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0));
            Press(search, Key.Enter);
            Program.Assert(before == SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) && vm.IsCustomGeneralSelectionOpen,
                "Enter in the search box cannot submit the previewed general.");

            vm.CustomGeneralSearch = string.Empty;
            var rosterChoices = vm.CustomGeneralChoices.ToArray();
            var numbered = rosterChoices.First(choice => choice.CandidateGeneralId != vm.GeneralChoices[0].CandidateGeneralId &&
                rosterChoices.Count(candidate => candidate.Name.Contains(choice.Name, StringComparison.OrdinalIgnoreCase) ||
                    candidate.GeneralId.Contains(choice.Name, StringComparison.OrdinalIgnoreCase) ||
                    candidate.Kingdom.Contains(choice.Name, StringComparison.OrdinalIgnoreCase)) == 1);
            vm.CustomGeneralSearch = numbered.Name;
            vm.PreviewGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
            var roster = (ListBox)window.FindName("CustomGeneralRoster");
            var confirm = (Button)window.FindName("ConfirmGeneralChoiceButton");
            var accepted = Program.Engine(vm).AcceptedCommands.Count;
            Program.Assert(vm.CustomGeneralChoices.Count == 1 && ReferenceEquals(vm.CustomGeneralChoices[0], numbered) &&
                !ReferenceEquals(vm.SelectedGeneralChoice, numbered),
                "The keyboard fixture must expose one searched result different from the hidden first candidate.");
            Program.Assert(Press(confirm, Key.D1, allowShortcut: true) && ReferenceEquals(vm.SelectedGeneralChoice, numbered),
                "A number key outside the search box must preview the corresponding visible search result, never a hidden candidate.");
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var numberedButton = Program.Find<Button>(roster).Single(button =>
                ReferenceEquals(button.Command, vm.PreviewGeneralChoiceCommand) && ReferenceEquals(button.CommandParameter, numbered));
            Console.WriteLine($"Roster number preview: focused={Keyboard.FocusedElement?.GetType().Name}, " +
                $"exactButton={ReferenceEquals(Keyboard.FocusedElement, numberedButton)}, visible={numberedButton.IsVisible}, " +
                $"sameSnapshot={before == SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0))}, " +
                $"addedCommands={Program.Engine(vm).AcceptedCommands.Count - accepted}.");
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, numberedButton) && numberedButton.IsVisible &&
                before == SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) && Program.Engine(vm).AcceptedCommands.Count == accepted,
                "Number preview must focus its actual visible roster row without submitting any game command.");
            Press(numberedButton, Key.D9, allowShortcut: true);
            Program.Assert(ReferenceEquals(vm.SelectedGeneralChoice, numbered) && ReferenceEquals(Keyboard.FocusedElement, numberedButton) &&
                before == SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) && Program.Engine(vm).AcceptedCommands.Count == accepted,
                "A number beyond the filtered result count must retain the current preview and row focus without submitting a command.");
            vm.CustomGeneralSearch = "__no_opening_general_match__";
            Program.Assert(vm.CustomGeneralChoices.Count == 0, "The empty-result keyboard fixture must hide every roster row.");
            Press(confirm, Key.D1, allowShortcut: true);
            Program.Assert(ReferenceEquals(vm.SelectedGeneralChoice, numbered) &&
                before == SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) && Program.Engine(vm).AcceptedCommands.Count == accepted,
                "A number with no search results must not select an invisible candidate or submit a command.");
            vm.CustomGeneralSearch = numbered.Name;
            vm.PreviewGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
            roster.ScrollIntoView(numbered);
            roster.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var numberedRow = (ListBoxItem)roster.ItemContainerGenerator.ContainerFromItem(numbered);
            Program.Assert(numberedRow is not null && !ReferenceEquals(vm.SelectedGeneralChoice, numbered),
                "The container Enter fixture must focus the visible result while another candidate remains previewed.");
            Program.Assert(Press(numberedRow!, Key.Enter, allowShortcut: true) && ReferenceEquals(vm.SelectedGeneralChoice, numbered),
                "First Enter on a roster row container must preview that row instead of confirming the old hidden candidate.");
            numberedButton = Program.Find<Button>(roster).Single(button =>
                ReferenceEquals(button.Command, vm.PreviewGeneralChoiceCommand) && ReferenceEquals(button.CommandParameter, numbered));
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, numberedButton) &&
                before == SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) && Program.Engine(vm).AcceptedCommands.Count == accepted,
                "Container Enter preview must focus its actual row button while preserving the prompt and accepted command boundary.");
            Program.Render(root, 1120, 820, System.IO.Path.Combine(output, "opening-customization-keyboard.png"));
            Program.Assert(Press(numberedButton, Key.Enter, allowShortcut: true) &&
                Program.Engine(vm).AcceptedCommands.Count == accepted + 1 &&
                Program.Engine(vm).AcceptedCommands.Last() is SelectGeneralCommand submitted && submitted.GeneralId == numbered.GeneralId &&
                Program.Engine(vm).CreateSnapshot(0).Players.Single(player => player.IsHuman).GeneralId == numbered.GeneralId &&
                !vm.IsGeneralSelectionPending,
                "Second Enter must confirm the newly previewed roster row exactly once and select that actual general.");
            for (var step = 0; step < 30 && !vm.CanRerollOpeningHand; step++) vm.StepAiCommand.Execute(null);
            Program.Assert(vm.IsOpeningDealVisible && vm.CanRerollOpeningHand && vm.OpeningHandCards.Count == vm.Hand.Count &&
                Program.Engine(vm).State.TurnNumber == 0 && !vm.CanStepAi,
                "Even with motion disabled, initial cards wait for explicit confirmation before any turn.");
            var oldCards = vm.Hand.Select(card => card.Id).ToArray();
            Program.Render(root, 1120, 820, System.IO.Path.Combine(output, "opening-customization-hand-before.png"));
            var rerollButton = (Button)window.FindName("RerollOpeningHandButton");
            Press(rerollButton, Key.Enter);
            Program.Assert(vm.Hand.Count == oldCards.Length && !vm.Hand.Select(card => card.Id).SequenceEqual(oldCards) &&
                vm.OpeningHandCards.Select(card => card.Id).SequenceEqual(vm.Hand.Select(card => card.Id)),
                "The reroll button changes the real private hand and refreshes its visible card faces.");
            oldCards = vm.Hand.Select(card => card.Id).ToArray();
            var openingGame = Program.Engine(vm);
            var openingPrompt = openingGame.PendingDecision!;
            var acceptedBeforeShiftReroll = openingGame.AcceptedCommands.Count;
            Press(rerollButton, Key.Enter, modifiers: ModifierKeys.Shift);
            Program.Assert(vm.IsOpeningDealVisible && vm.CanRerollOpeningHand && openingGame.State.TurnNumber == 0 &&
                ReferenceEquals(Program.Engine(vm), openingGame) && vm.Hand.Count == oldCards.Length &&
                !vm.Hand.Select(card => card.Id).SequenceEqual(oldCards) &&
                vm.OpeningHandCards.Select(card => card.Id).SequenceEqual(vm.Hand.Select(card => card.Id)) &&
                openingGame.PendingDecision is { Kind: DecisionKind.OpeningHand } refreshedOpening &&
                refreshedOpening.PromptId != openingPrompt.PromptId &&
                openingGame.AcceptedCommands.Count == acceptedBeforeShiftReroll + 1 &&
                openingGame.AcceptedCommands.Last() is AnswerPromptCommand shiftReroll &&
                shiftReroll.PromptId == openingPrompt.PromptId && shiftReroll.Choice == new ChoiceId("setup.opening.reroll"),
                "Shift+Enter on the actual reroll button must reroll exactly once and retain a fresh opening-hand confirmation instead of entering the match.");
            var repeatedOpeningPrompt = openingGame.PendingDecision!.PromptId;
            var repeatedOpeningRevision = openingGame.Revision;
            var repeatedOpeningCommands = openingGame.AcceptedCommands.ToArray();
            var repeatedOpeningCards = vm.Hand.Select(card => card.Id).ToArray();
            var repeatedOpeningViews = Enumerable.Range(0, openingGame.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(openingGame.CreateSnapshot(seat))).ToArray();
            var repeatHandled = Press(rerollButton, Key.Enter, allowShortcut: true, modifiers: ModifierKeys.Shift, repeat: true);
            Program.Assert(repeatHandled && vm.IsOpeningDealVisible && vm.CanRerollOpeningHand &&
                ReferenceEquals(Program.Engine(vm), openingGame) && openingGame.PendingDecision?.PromptId == repeatedOpeningPrompt &&
                openingGame.Revision == repeatedOpeningRevision && openingGame.AcceptedCommands.SequenceEqual(repeatedOpeningCommands) &&
                vm.Hand.Select(card => card.Id).SequenceEqual(repeatedOpeningCards) &&
                vm.OpeningHandCards.Select(card => card.Id).SequenceEqual(repeatedOpeningCards) &&
                Enumerable.Range(0, repeatedOpeningViews.Length)
                    .Select(seat => SnapshotJson.Serialize(openingGame.CreateSnapshot(seat))).SequenceEqual(repeatedOpeningViews),
                $"Held Shift+Enter must be consumed before the actual reroll button can answer the new opening-hand prompt. " +
                $"Preview handled={repeatHandled}, revision={repeatedOpeningRevision}->{openingGame.Revision}, " +
                $"prompt={repeatedOpeningPrompt}->{openingGame.PendingDecision?.PromptId}, " +
                $"accepted={repeatedOpeningCommands.Length}->{openingGame.AcceptedCommands.Count}.");
            rerollButton.Focus();
            Program.Assert(!Input(Key.Tab) && rerollButton.MoveFocus(new(FocusNavigationDirection.Next)) &&
                ReferenceEquals(Keyboard.FocusedElement, window.FindName("DismissOpeningDealButton")),
                "Tab can move from reroll to the actual enter-match button without starting the match.");
            var saved = SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0));
            vm.SaveGameCommand.Execute(null);
            vm.LoadManualGameCommand.Execute(null);
            vm.IsMotionEnabled = false;
            Program.Assert(saved == SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) &&
                vm.IsOpeningDealVisible && vm.CanRerollOpeningHand,
                "Loading the pre-game save restores both the actual hand and its manual confirmation controls.");
            Program.Render(root, 1120, 820, System.IO.Path.Combine(output, "opening-customization-hand.png"));
            Program.Assert(((Button)window.FindName("RerollOpeningHandButton")).Visibility == Visibility.Visible &&
                ((Button)window.FindName("DismissOpeningDealButton")).ActualWidth > 0,
                "Reroll and enter-match actions are both visible on the opening hand panel.");
            vm.DismissOpeningDealCommand.Execute(null);
            before = SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0));
            vm.RerollOpeningHandCommand.Execute(null);
            vm.ChangeStartingIdentityCommand.Execute(vm.StartingRoles[2]);
            Program.Assert(!vm.IsOpeningDealVisible && !vm.CanRerollOpeningHand &&
                before == SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)),
                "Retired setup commands cannot change cards or identities after entering the match.");
        }
        finally { Keyboard.ClearFocus(); }

        bool Input(Key key, bool repeat = false) => (bool)typeof(MainWindow)
            .GetMethod("HandleKeyInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [key, ModifierKeys.None, repeat])!;

        bool Press(UIElement element, Key key, bool allowShortcut = false, ModifierKeys modifiers = ModifierKeys.None, bool repeat = false)
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            element.Focus();
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, element),
                "The hidden native host must focus the real setup control. " + Diagnose(null));
            return modifiers == ModifierKeys.None ? Route() : TableShortcutChecks.WithModifiers(modifiers, Route);

            bool Route()
            {
                var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element)!, 0, key)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                // Use WPF's actual repeat flag so the Window receives the same
                // routed event shape as an automatically repeated key press.
                SetKeyRepeat(input, repeat);
                var beforeRoute = Diagnose(input);
                element.RaiseEvent(input);
                var previewHandled = input.Handled;
                if (!allowShortcut) Program.Assert(!previewHandled,
                    "The window must leave setup control activation to the focused control. Before: " + beforeRoute +
                    " After: " + Diagnose(input));
                if (!previewHandled)
                {
                    input.RoutedEvent = Keyboard.KeyDownEvent;
                    element.RaiseEvent(input);
                }
                return previewHandled;
            }

            string Diagnose(KeyEventArgs? input)
            {
                var focused = Keyboard.FocusedElement;
                var game = Program.Engine(vm);
                return $"key={key}, modifiers={Keyboard.Modifiers}, repeat={input?.IsRepeat}, " +
                    $"target={element.GetType().Name}/{(element as FrameworkElement)?.Name}, " +
                    $"focus={focused?.GetType().Name}/{(focused as FrameworkElement)?.Name}, exactFocus={ReferenceEquals(focused, element)}, " +
                    $"visible={element.IsVisible}, enabled={element.IsEnabled}, nativeFocus={GetFocus()}, " +
                    $"ownsNativeFocus={GetFocus() == new WindowInteropHelper(window).Handle}, " +
                    $"sameSource={ReferenceEquals(PresentationSource.FromVisual(element), PresentationSource.FromVisual(window))}, " +
                    $"identity={vm.IsIdentityRevealOpen}, opening={vm.IsOpeningDealVisible}, generalSelection={vm.IsGeneralSelectionPending}, " +
                    $"prompt={game.PendingDecision?.Kind}/{game.PendingDecision?.PromptId}, revision={game.Revision}.";
            }
        }
    }

    internal static void SetKeyRepeat(KeyEventArgs input, bool repeat)
    {
        typeof(KeyEventArgs).GetMethod("SetRepeat", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(input, [repeat]);
        Program.Assert(input.IsRepeat == repeat, "The routed key fixture must carry the requested WPF repeat flag.");
    }

    [DllImport("user32.dll")] private static extern nint SetFocus(nint handle);
    [DllImport("user32.dll")] private static extern nint GetFocus();
}
