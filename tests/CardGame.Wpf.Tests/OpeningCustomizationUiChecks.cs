using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class OpeningCustomizationUiChecks
{
    public static void RolesRosterAndHandConfirmation(string output)
    {
        using var vm = new MainViewModel(autoAdvance: false, seed: 31, showSetup: true,
            saveStore: new MemorySaveStore(), confirmOpeningHand: true, useExpandedContent: true,
            contentRegistry: StandardContentRegistry.CreateWithClassicGenerals(), wushuangSaveStore: new MemorySaveStore()) { IsMotionEnabled = false };
        vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "identity:classic-5");
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
            var modeDefinition = game.ContentRegistry.Modes[game.ModeId];
            var lordFamily = modeDefinition.GeneralVariantGroups?.Values.FirstOrDefault(ids =>
                ids.Contains(lord.GeneralId, StringComparer.Ordinal)) ?? [lord.GeneralId];
            var expectedRoster = (modeDefinition.GeneralSelectionPoolIds ?? modeDefinition.GeneralPoolIds!)
                .Except(lordFamily, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            Program.Assert(vm.IsCustomGeneralSelectionOpen &&
                vm.CustomGeneralChoices.Select(choice => choice.GeneralId).ToHashSet(StringComparer.Ordinal).SetEquals(expectedRoster) &&
                Enumerable.Range(1, humanView.Players.Count - 1).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
                "The non-lord's private full roster excludes the public lord's whole character family without deductions from other AI selections.");
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
            TableShortcutChecks.WithControl(() =>
            {
                Press(roleButtons.Single(button => ((StartingRoleOption)button.DataContext).Role == Role.Rebel), Key.Enter);
                Program.Assert(Keyboard.Modifiers == ModifierKeys.Control,
                    "An unmodified setup route must restore its inherited calling-thread modifier after activating the real button.");
                return true;
            });
            Program.Assert(vm.IsIdentityRevealOpen && Program.Engine(vm).CreateSnapshot(0).Players[0].Role == Role.Rebel,
                "Enter on the actual identity button changes the role without leaving its reveal panel.");
            AssertCurrentIdentityVisual(window, vm, (FrameworkElement)window.FindName("IdentityOverrideChoices"),
                (UIElement)window.FindName("ContinueFromIdentityRevealButton"));
            var previousIdentityGame = Program.Engine(vm);
            Press(roleButtons.Single(button => ((StartingRoleOption)button.DataContext).Role == Role.Loyalist), Key.Enter,
                modifiers: ModifierKeys.Shift);
            Program.Assert(vm.IsIdentityRevealOpen && vm.IsGeneralSelectionPending &&
                !ReferenceEquals(Program.Engine(vm), previousIdentityGame) &&
                Program.Engine(vm).CreateSnapshot(0).Players[0].Role == Role.Loyalist,
                "Shift+Enter on the actual identity button must select its role through the native button without continuing identity reveal.");
            AssertCurrentIdentityVisual(window, vm, (FrameworkElement)window.FindName("IdentityOverrideChoices"),
                (UIElement)window.FindName("ContinueFromIdentityRevealButton"));
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
            VerifyFilteredEditionRefresh(search);
            VerifySearchResultDown(search);
            var searchPreview = vm.SelectedGeneralChoice;
            var searchText = vm.CustomGeneralSearch;
            var searchViews = Enumerable.Range(0, Program.Engine(vm).CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(seat))).ToArray();
            var searchCommands = CommandJson.Serialize(Program.Engine(vm).AcceptedCommands);
            Program.Assert(Press((Button)window.FindName("ToggleGeneralRosterButton"), Key.F, allowShortcut: true, modifiers: ModifierKeys.Control) &&
                ReferenceEquals(Keyboard.FocusedElement, search) && search.SelectedText == searchText &&
                vm.CustomGeneralSearch == searchText && ReferenceEquals(vm.SelectedGeneralChoice, searchPreview),
                "Ctrl+F on the active full roster must focus and select the existing search text without changing its preview.");
            search.Select(search.Text.Length, 0);
            Program.Assert(Press(search, Key.F, allowShortcut: true, modifiers: ModifierKeys.Control, repeat: true) && search.SelectionLength == 0,
                "Held Ctrl+F must not repeatedly select or replace the current roster search draft.");
            vm.IsSettingsOpen = true;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var coveringToggle = (CheckBox)window.FindName("SettingsMotionToggle");
            Program.Assert(!Press(coveringToggle, Key.F, allowShortcut: true, modifiers: ModifierKeys.Control) &&
                ReferenceEquals(Keyboard.FocusedElement, coveringToggle) && search.SelectionLength == 0 &&
                vm.CustomGeneralSearch == searchText && ReferenceEquals(vm.SelectedGeneralChoice, searchPreview) &&
                CommandJson.Serialize(Program.Engine(vm).AcceptedCommands) == searchCommands &&
                Enumerable.Range(0, searchViews.Length)
                    .Select(seat => SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(seat))).SequenceEqual(searchViews),
                "Ctrl+F in covering settings must leave the hidden roster, its draft and every native player boundary untouched.");
            vm.IsSettingsOpen = false;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

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
            var rosterToggle = (Button)window.FindName("ToggleGeneralRosterButton");
            var accepted = Program.Engine(vm).AcceptedCommands.Count;
            Program.Assert(vm.CustomGeneralChoices.Count == 1 && ReferenceEquals(vm.CustomGeneralChoices[0], numbered) &&
                !ReferenceEquals(vm.SelectedGeneralChoice, numbered),
                "The keyboard fixture must expose one searched result different from the hidden first candidate.");
            Program.Assert(Press(rosterToggle, Key.D1, allowShortcut: true) && ReferenceEquals(vm.SelectedGeneralChoice, numbered),
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
            var beforeEmptyViews = Enumerable.Range(0, Program.Engine(vm).CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(seat))).ToArray();
            var beforeEmptyCommands = CommandJson.Serialize(Program.Engine(vm).AcceptedCommands);
            vm.CustomGeneralSearch = "__no_opening_general_match__";
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Assert(vm.CustomGeneralChoices.Count == 0 && vm.SelectedGeneralChoice is null && !numbered.IsPreviewSelected &&
                !vm.CanConfirmGeneralChoice && !confirm.IsEnabled && !vm.ConfirmGeneralChoiceCommand.CanExecute(null),
                "An empty search must clear the hidden preview and disable the actual confirmation button and command.");
            Program.Assert(Press(search, Key.Down, allowShortcut: true) && ReferenceEquals(Keyboard.FocusedElement, search) &&
                vm.SelectedGeneralChoice is null && !vm.CanConfirmGeneralChoice,
                "Down with no visible results must keep focus in search without reviving or confirming a hidden general.");
            Press(rosterToggle, Key.D1, allowShortcut: true);
            Press(roster, Key.Enter, allowShortcut: true);
            Program.Assert(vm.SelectedGeneralChoice is null && !numbered.IsPreviewSelected && !vm.CanConfirmGeneralChoice &&
                !confirm.IsEnabled && !vm.ConfirmGeneralChoiceCommand.CanExecute(null) &&
                before == SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) && Program.Engine(vm).AcceptedCommands.Count == accepted &&
                CommandJson.Serialize(Program.Engine(vm).AcceptedCommands) == beforeEmptyCommands &&
                Enumerable.Range(0, beforeEmptyViews.Length)
                    .Select(seat => SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(seat))).SequenceEqual(beforeEmptyViews),
                "A number or Enter routed from real enabled controls with no search results must leave confirmation disabled and preserve every player's view and accepted command.");
            var emptyCategory = vm.SelectedCustomGeneralCategory;
            Press((Button)window.FindName("ClearCustomGeneralSearchButton"), Key.Enter);
            Program.Assert(vm.CustomGeneralSearch == string.Empty && vm.SelectedCustomGeneralCategory == emptyCategory &&
                vm.CustomGeneralChoices.Count > 0 && vm.CanConfirmGeneralChoice && ReferenceEquals(Keyboard.FocusedElement, search) &&
                CommandJson.Serialize(Program.Engine(vm).AcceptedCommands) == beforeEmptyCommands &&
                Enumerable.Range(0, beforeEmptyViews.Length)
                    .Select(seat => SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(seat))).SequenceEqual(beforeEmptyViews),
                "Keyboard recovery must restore results and focus search without changing the series or committing a general.");
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
            Program.Assert(!vm.IsOpeningDealVisible && vm.CanRerollOpeningHand && vm.OpeningHandCards.Count == vm.Hand.Count &&
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
            Program.Assert(!vm.IsOpeningDealVisible && vm.CanRerollOpeningHand && openingGame.State.TurnNumber == 0 &&
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
            Program.Assert(repeatHandled && !vm.IsOpeningDealVisible && vm.CanRerollOpeningHand &&
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
                ReferenceEquals(Keyboard.FocusedElement, window.FindName("ChangeOpeningHandButton")),
                "Tab moves from random to the inline replacement action without starting the match.");
            Press((Button)window.FindName("ChangeOpeningHandButton"), Key.Enter);
            var replaceTarget = vm.Hand.First();
            Program.Assert(replaceTarget.IsPlayable, "Opening cards remain selectable in the actual hand area.");
            var handButton = Program.Find<Button>((ItemsControl)window.FindName("HandCards")).Single(button =>
                ReferenceEquals(button.Command, vm.SelectCardCommand) && ReferenceEquals(button.CommandParameter, replaceTarget));
            Press(handButton, Key.Enter);
            var replacement = vm.OpeningCardChoices
                .OrderByDescending(choice => EquipmentCatalog.IsEquipment(choice.Kind))
                .First(choice => vm.OpeningCardChoices.Count(candidate =>
                    candidate.Name.Contains(CardCatalog.Get(choice.Kind).DisplayName, StringComparison.Ordinal)) == 1);
            var replacementName = CardCatalog.Get(replacement.Kind).DisplayName;
            vm.OpeningCardSearch = "__missing_card__";
            Program.Assert(vm.OpeningCardChoices.Count == 0 && !vm.ReplaceOpeningCardCommand.CanExecute(null),
                "An empty card search cannot submit a replacement.");
            vm.OpeningCardSearch = replacementName;
            Program.Assert(vm.IsOpeningHandEditorOpen && vm.OpeningCardChoices.Single().Kind == replacement.Kind &&
                replaceTarget.IsSelected && !vm.IsOpeningDealVisible,
                "The inline editor filters by card name and highlights the selected existing hand card without an opening overlay.");
            var openingSearch = (TextBox)window.FindName("OpeningCardSearchBox");
            openingSearch.Focus();
            var beforeSearchKey = openingGame.AcceptedCommands.Count;
            Program.Assert(!Input(Key.Space) && !Input(Key.D1) && !Input(Key.Enter) &&
                beforeSearchKey == openingGame.AcceptedCommands.Count && openingGame.State.TurnNumber == 0,
                "Search typing and Enter belong to the search box and cannot confirm the opening hand.");
            var openingSelector = (ComboBox)window.FindName("OpeningCardSelector");
            var beforeEditorEscape = Enumerable.Range(0, openingGame.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(openingGame.CreateSnapshot(seat))).ToArray();
            var escapeChoice = vm.SelectedOpeningCardChoice;
            openingSelector.Focus();
            openingSelector.IsDropDownOpen = true;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Assert(openingSelector.IsDropDownOpen && vm.IsOpeningHandEditorOpen,
                "The opening selector Escape fixture must expand its actual card list.");
            Press(openingSelector, Key.Escape);
            Program.Assert(!openingSelector.IsDropDownOpen && vm.IsOpeningHandEditorOpen &&
                ReferenceEquals(vm.SelectedOpeningCardChoice, escapeChoice) && replaceTarget.IsSelected,
                "The first Escape must close only the native opening-card dropdown and retain its draft.");
            Program.Assert(Press(openingSelector, Key.Escape, allowShortcut: true, repeat: true) && vm.IsOpeningHandEditorOpen,
                "Holding Escape after the opening dropdown closes must not dismiss its editor.");
            Program.Assert(Press(openingSelector, Key.Escape, allowShortcut: true) && !vm.IsOpeningHandEditorOpen &&
                ReferenceEquals(Keyboard.FocusedElement, window.FindName("ChangeOpeningHandButton")),
                "A fresh Escape must close the opening editor and return focus to its replacement action.");
            Press((Button)window.FindName("ChangeOpeningHandButton"), Key.Enter);
            Program.Assert(Press(openingSearch, Key.Escape, allowShortcut: true) && !vm.IsOpeningHandEditorOpen &&
                ReferenceEquals(Keyboard.FocusedElement, window.FindName("ChangeOpeningHandButton")) &&
                openingGame.AcceptedCommands.Count == beforeSearchKey && openingGame.State.TurnNumber == 0 &&
                Enumerable.Range(0, beforeEditorEscape.Length)
                    .Select(seat => SnapshotJson.Serialize(openingGame.CreateSnapshot(seat))).SequenceEqual(beforeEditorEscape),
                "Escape from the opening search must return to the replacement action without changing any player's view or accepting a command.");
            Press((Button)window.FindName("ChangeOpeningHandButton"), Key.Enter);
            Program.Render(root, 1120, 820, System.IO.Path.Combine(output, "opening-customization-replace.png"));
            Press((Button)window.FindName("ReplaceOpeningCardButton"), Key.Enter);
            Program.Assert(vm.Hand.Single(card => !repeatedOpeningCards.Contains(card.Id)).Kind == replacement.Kind &&
                vm.Hand.All(card => card.Id != replaceTarget.Id) && vm.Hand.Count == oldCards.Length &&
                openingGame.AcceptedCommands.Count == beforeSearchKey + 1 && openingGame.State.TurnNumber == 0 &&
                vm.CanRerollOpeningHand && !vm.IsOpeningDealVisible,
                "The actual inline replacement button swaps one real hand card for its published replacement and keeps the match before its first turn.");
            var saved = SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0));
            vm.SaveGameCommand.Execute(null);
            vm.LoadManualGameCommand.Execute(null);
            vm.IsMotionEnabled = false;
            Program.Assert(saved == SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) &&
                !vm.IsOpeningDealVisible && vm.CanRerollOpeningHand && !vm.IsOpeningHandEditorOpen,
                "Loading the pre-game save restores both the actual hand and its manual confirmation controls.");
            Program.Render(root, 1120, 820, System.IO.Path.Combine(output, "opening-customization-hand.png"));
            Program.Assert(((Button)window.FindName("RerollOpeningHandButton")).Visibility == Visibility.Visible &&
                ((Button)window.FindName("ConfirmOpeningHandButton")).ActualWidth > 0 &&
                ((Button)window.FindName("ChangeOpeningHandButton")).Visibility == Visibility.Visible,
                "Random, replace and confirm are visible immediately above the existing hand area.");
            Press((Button)window.FindName("ConfirmOpeningHandButton"), Key.Enter);
            before = SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0));
            vm.RerollOpeningHandCommand.Execute(null);
            vm.ChangeStartingIdentityCommand.Execute(vm.StartingRoles[2]);
            Program.Assert(!vm.IsOpeningDealVisible && !vm.CanRerollOpeningHand &&
                before == SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)),
                "Retired setup commands cannot change cards or identities after entering the match.");
        }
        finally { Keyboard.ClearFocus(); }

        bool Input(Key key, bool repeat = false, ModifierKeys modifiers = ModifierKeys.None) => (bool)typeof(MainWindow)
            .GetMethod("HandleKeyInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [key, modifiers, repeat])!;

        void AssertFocusedRosterResult(GeneralChoiceViewModel result)
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var roster = (ListBox)window.FindName("CustomGeneralRoster");
            var focused = Keyboard.FocusedElement as Button;
            Program.Assert(focused is not null && focused.IsDescendantOf(roster) &&
                ReferenceEquals(focused.Command, vm.PreviewGeneralChoiceCommand) && ReferenceEquals(focused.CommandParameter, result),
                "Search Down must focus the actual current visible result's preview button.");
            var presenter = Program.Find<ScrollContentPresenter>(roster).Single();
            var bounds = focused!.TransformToAncestor(presenter).TransformBounds(new Rect(focused.RenderSize));
            Program.Assert(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1 &&
                bounds.Right <= presenter.ActualWidth + 1 && bounds.Bottom <= presenter.ActualHeight + 1,
                $"The search result focused by Down must be fully inside the native roster viewport (result={bounds}, viewport={presenter.RenderSize}).");
        }

        void VerifySearchResultDown(TextBox search)
        {
            var originalSearch = vm.CustomGeneralSearch;
            var originalPreviewId = vm.SelectedGeneralChoice!.GeneralId;
            vm.CustomGeneralSearch = string.Empty;
            var selected = vm.CustomGeneralChoices.Last();
            vm.PreviewGeneralChoiceCommand.Execute(selected);
            var roster = (ListBox)window.FindName("CustomGeneralRoster");
            var scroll = Program.Find<ScrollViewer>(roster).Single();
            scroll.ScrollToTop();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Assert(scroll.ScrollableHeight > scroll.ViewportHeight && scroll.VerticalOffset == 0 &&
                roster.Items.IndexOf(selected) > roster.Items.Count / 2,
                "The search navigation fixture must start with its real preview well below the top of a scrollable full roster.");
            var game = Program.Engine(vm);
            var views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            var commands = CommandJson.Serialize(game.AcceptedCommands);
            var promptId = game.PendingDecision!.PromptId;
            foreach (var modifiers in new[] { ModifierKeys.Control, ModifierKeys.Shift })
                Program.Assert(!Press(search, Key.Down, modifiers: modifiers) && ReferenceEquals(Keyboard.FocusedElement, search) &&
                    ReferenceEquals(vm.SelectedGeneralChoice, selected) && scroll.VerticalOffset == 0,
                    "Modified Down must stay with the search box and leave the roster preview and scroll untouched.");
            // The native modifier fixture supports Ctrl and Shift; exercise the
            // same ownership boundary with explicit Alt input instead.
            Program.Assert(!Input(Key.Down, modifiers: ModifierKeys.Alt) && ReferenceEquals(Keyboard.FocusedElement, search) &&
                ReferenceEquals(vm.SelectedGeneralChoice, selected) && scroll.VerticalOffset == 0,
                "Alt+Down must not enter or scroll roster results, change the preview or move focus out of search.");
            vm.IsSettingsOpen = true;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var coveringToggle = (CheckBox)window.FindName("SettingsMotionToggle");
            var settingsHandled = Press(coveringToggle, Key.Down);
            var settings = (UIElement)window.FindName("SettingsOverlay");
            var settingsFocus = Keyboard.FocusedElement;
            var focusWithinSettings = settingsFocus is UIElement focused && focused.IsDescendantOf(settings);
            var samePreview = ReferenceEquals(vm.SelectedGeneralChoice, selected);
            var sameJournal = CommandJson.Serialize(game.AcceptedCommands) == commands;
            Console.WriteLine($"Roster search Down under settings: handled={settingsHandled}, " +
                $"focused={settingsFocus?.GetType().Name}/{(settingsFocus as FrameworkElement)?.Name}, inSettings={focusWithinSettings}, " +
                $"rosterOffset={scroll.VerticalOffset}, selected={vm.SelectedGeneralChoice?.GeneralId}, expected={selected.GeneralId}, " +
                $"samePreview={samePreview}, sameJournal={sameJournal}.");
            Program.Assert(!settingsHandled && vm.IsSettingsOpen && focusWithinSettings && samePreview &&
                scroll.VerticalOffset == 0 && sameJournal,
                "Down may navigate within covering settings but must not enter, scroll or change the underlying roster or command journal.");
            vm.IsSettingsOpen = false;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Assert(Press(search, Key.Down, allowShortcut: true),
                "Unmodified Down in the active roster search must enter its visible preview result.");
            AssertFocusedRosterResult(selected);
            Program.Assert(scroll.VerticalOffset > 0 && ReferenceEquals(vm.SelectedGeneralChoice, selected) &&
                vm.CustomGeneralSearch == string.Empty && vm.CanConfirmGeneralChoice && game.PendingDecision?.PromptId == promptId &&
                CommandJson.Serialize(game.AcceptedCommands) == commands &&
                Enumerable.Range(0, views.Length).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).SequenceEqual(views),
                "Entering an offscreen search result must only scroll and focus it, preserving the preview, draft, real prompt, every safe player view and command journal.");
            vm.CustomGeneralSearch = originalSearch;
            vm.PreviewGeneralChoiceCommand.Execute(vm.CustomGeneralChoices.Single(choice => choice.GeneralId == originalPreviewId));
            search.Focus();
        }

        void VerifyFilteredEditionRefresh(TextBox search)
        {
            var originalSearch = vm.CustomGeneralSearch;
            var originalCategory = vm.SelectedCustomGeneralCategory;
            var originalPreviewId = vm.SelectedGeneralChoice!.GeneralId;
            vm.CustomGeneralSearch = string.Empty;
            var family = vm.CustomGeneralChoices.GroupBy(choice => choice.CandidateGeneralId)
                .First(group => group.Select(choice => choice.RosterCategoryId).Distinct().Count() > 1).ToArray();
            var searched = family.OrderByDescending(choice => choice.GeneralId.Length).First();
            var alternateId = family.First(choice => choice.RosterCategoryId != searched.RosterCategoryId).GeneralId;
            var searchedId = searched.GeneralId;
            var searchedCategory = searched.RosterCategoryId;
            vm.CustomGeneralSearch = searchedId;
            Program.Assert(vm.CustomGeneralChoices.Count == 1 && vm.SelectedGeneralChoice?.GeneralId == searchedId,
                "The edition refresh fixture must filter one real version while exposing another series in its candidate family.");
            var game = Program.Engine(vm);
            var views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            var commands = CommandJson.Serialize(game.AcceptedCommands);
            var promptId = game.PendingDecision!.PromptId;
            var alternate = PreviewAlternate();
            search.Focus();
            Refresh();
            var refreshed = vm.SelectedGeneralChoice;
            Program.Assert(refreshed is not null && refreshed.GeneralId == alternateId &&
                !ReferenceEquals(refreshed, alternate) && vm.GeneralChoices.Contains(refreshed) &&
                vm.SelectedGeneralVariants.Contains(refreshed) && refreshed.IsPreviewSelected &&
                vm.CustomGeneralChoices.Count == 1 && vm.CustomGeneralChoices[0].GeneralId == searchedId &&
                vm.CustomGeneralSearch == searchedId && vm.SelectedCustomGeneralCategory == originalCategory &&
                vm.CanConfirmGeneralChoice && vm.ConfirmGeneralChoiceCommand.CanExecute(null) &&
                ((Button)window.FindName("ConfirmGeneralChoiceButton")).IsEnabled && ReferenceEquals(Keyboard.FocusedElement, search),
                "Refreshing an unchanged filtered prompt must retain the explicitly chosen legal family edition, search draft and live search focus.");
            vm.PreviewGeneralChoiceCommand.Execute(alternate);
            Program.Assert(ReferenceEquals(vm.SelectedGeneralChoice, refreshed),
                "The retired edition control must not replace the freshly rebound current preview.");
            var visibleResult = vm.CustomGeneralChoices[0];
            Program.Assert(Press(search, Key.Down, allowShortcut: true),
                "Search Down must enter a visible result when the previewed family edition itself is filtered out.");
            AssertFocusedRosterResult(visibleResult);
            Program.Assert(ReferenceEquals(vm.SelectedGeneralChoice, refreshed) && vm.CustomGeneralSearch == searchedId,
                "Focusing a visible sibling must not replace the explicitly previewed filtered-out edition or edit the search draft.");

            vm.CustomGeneralSearch = searchedId + " ";
            Program.Assert(vm.SelectedGeneralChoice?.GeneralId == searchedId && vm.CustomGeneralChoices.Count == 1,
                "Editing the query must return to its exact visible result even after a family edition was retained on refresh.");
            PreviewAlternate();
            vm.SelectCustomGeneralCategoryCommand.Execute(searchedCategory);
            Program.Assert(vm.SelectedCustomGeneralCategory == searchedCategory && vm.SelectedGeneralChoice?.GeneralId == searchedId &&
                vm.CustomGeneralChoices.Count == 1,
                "Changing the series must reselect an exact visible result rather than retain an edition outside the newly chosen series.");
            PreviewAlternate();
            vm.CustomGeneralSearch = "__no_filtered_edition_match__";
            Refresh();
            Program.Assert(vm.CustomGeneralChoices.Count == 0 && vm.SelectedGeneralChoice is null &&
                !vm.CanConfirmGeneralChoice && !vm.ConfirmGeneralChoiceCommand.CanExecute(null) &&
                !((Button)window.FindName("ConfirmGeneralChoiceButton")).IsEnabled,
                "An empty filtered prompt must stay empty and unconfirmable after refresh, without reviving a previous family edition or fallback.");
            Program.Assert(game.PendingDecision?.PromptId == promptId && CommandJson.Serialize(game.AcceptedCommands) == commands &&
                Enumerable.Range(0, views.Length).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).SequenceEqual(views),
                "Edition preview, refresh and filter changes must preserve the real prompt, every safe player view and the native command journal.");
            vm.SelectCustomGeneralCategoryCommand.Execute(originalCategory);
            vm.CustomGeneralSearch = originalSearch;
            vm.PreviewGeneralChoiceCommand.Execute(vm.CustomGeneralChoices.Single(choice => choice.GeneralId == originalPreviewId));
            search.Focus();

            GeneralChoiceViewModel PreviewAlternate()
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var button = Program.Find<Button>((ItemsControl)window.FindName("GeneralVariantChoices")).Single(button =>
                    button.CommandParameter is GeneralChoiceViewModel edition && edition.GeneralId == alternateId);
                var choice = (GeneralChoiceViewModel)button.CommandParameter;
                Press(button, Key.Enter);
                Program.Assert(ReferenceEquals(vm.SelectedGeneralChoice, choice) && !vm.CustomGeneralChoices.Contains(choice) &&
                    vm.CustomGeneralChoices.Any(result => result.CandidateGeneralId == choice.CandidateGeneralId),
                    "Enter on the actual related edition button must preview that current legal family version even when only its sibling matches the filter.");
                return choice;
            }

            void Refresh()
            {
                vm.IsDeveloperView = true;
                vm.IsDeveloperView = false;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }
        }

        bool Press(UIElement element, Key key, bool allowShortcut = false, ModifierKeys modifiers = ModifierKeys.None, bool repeat = false)
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            element.Focus();
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, element),
                "The hidden native host must focus the real setup control. " + Diagnose(null));
            return TableShortcutChecks.WithModifiers(modifiers, Route);

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

    internal static void AssertCurrentIdentityVisual(MainWindow window, MainViewModel model, FrameworkElement scope, UIElement focusTarget)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var game = Program.Engine(model);
        var snapshot = game.CreateSnapshot(0);
        var role = snapshot.Players.Single(player => player.Seat == snapshot.HumanSeat).Role;
        var views = Enumerable.Range(0, snapshot.Players.Count).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        var commands = CommandJson.Serialize(game.AcceptedCommands);
        var buttons = Program.Find<Button>(scope).Where(button => button.IsVisible &&
            ReferenceEquals(button.Command, model.ChangeStartingIdentityCommand)).ToArray();
        var selected = buttons.Single(button => ((StartingRoleOption)button.CommandParameter).Role == role);
        Program.Assert(focusTarget.Focus(), "Identity feedback must remain visible after moving actual keyboard focus to another live action.");
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var chrome = (Border)selected.Template.FindName("Chrome", selected);
        var focusRing = (Border)selected.Template.FindName("FocusRing", selected);
        Program.Assert(role is not null && buttons.Length == 5 &&
            buttons.All(button => (button.Tag is true) == (((StartingRoleOption)button.CommandParameter).Role == role)) &&
            !selected.IsKeyboardFocused && focusRing.Visibility != Visibility.Visible && chrome.Background.ToString() == selected.Background.ToString() &&
            buttons.Where(button => !ReferenceEquals(button, selected)).All(button =>
                ((Border)button.Template.FindName("Chrome", button)).Background.ToString() != chrome.Background.ToString()) &&
            CommandJson.Serialize(game.AcceptedCommands) == commands &&
            Enumerable.Range(0, views.Length).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).SequenceEqual(views),
            "Only the safe snapshot's actual human identity must retain a distinct rendered selection background after focus leaves it, without a focus-ring substitute or changes to any player view or accepted command.");
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
