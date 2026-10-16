using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class HandResponseChecks
{
    public static void Controls(string output)
    {
        foreach (var kind in new[] { DecisionKind.RespondDodge, DecisionKind.RespondSlash,
                     DecisionKind.RescueDying, DecisionKind.Nullification, DecisionKind.FireAttackReveal, DecisionKind.FireAttackDiscard })
        {
            var (model, registry) = StableHandResponseFixture.Create(kind, output);
            using (model)
            {
                VerifyBoundary(model, output, kind.ToString());
                StableHandResponseFixture.RecordAndReplay(Program.Engine(model), registry, output, kind + ".after");
                VerifyCancel(model, output, kind, registry, checkFocusedEnter: kind == DecisionKind.RespondDodge);
                StableHandResponseFixture.RecordAndReplay(Program.Engine(model), registry, output, kind + ".cancelled");
                if (kind == DecisionKind.RespondDodge) VerifyExtraChoices(model);
            }
        }
        var (rescue, rescueRegistry) = StableHandResponseFixture.Create(DecisionKind.RescueDying, output, jijiu: true);
        using (rescue)
        {
            VerifyBoundary(rescue, output, "Jijiu", rescue.DyingChoices.First(choice => choice.Description.Contains("当作【桃】") && choice.Parameters.GetValueOrDefault("physical-card-kind") == nameof(CardKind.Dodge)).Cards[0]);
            StableHandResponseFixture.RecordAndReplay(Program.Engine(rescue), rescueRegistry, output, "Jijiu.after");
        }
        var (arrow, arrowRegistry) = StableHandResponseFixture.Create(DecisionKind.RespondDodge, output, arrowBarrage: true);
        using (arrow)
        {
            VerifyBoundary(arrow, output, "ArrowBarrage");
            StableHandResponseFixture.RecordAndReplay(Program.Engine(arrow), arrowRegistry, output, "ArrowBarrage.after");
            VerifyCancel(arrow, output, DecisionKind.RespondDodge, arrowRegistry);
            StableHandResponseFixture.RecordAndReplay(Program.Engine(arrow), arrowRegistry, output, "ArrowBarrage.cancelled");
        }
        StoredGrainResponseUiChecks.Run();
    }

    internal static void VerifyBoundary(MainViewModel vm, string output, string label, int? chosenId = null, string? expectedButtonText = null)
    {
        var engine = Program.Engine(vm);
        var prompt = engine.PendingDecision!;
        DecisionContextChecks.VerifyLive(vm);
        var card = chosenId is { } id ? vm.Hand.Single(card => card.Id == id) : vm.Hand.Last(card => card.IsPlayable);
        var choice = prompt.Choices.Single(choice => choice.Cards.Count == 1 && choice.Cards[0] == card.Id);
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        var state = SnapshotJson.Serialize(engine.CreateSnapshot(0, true));
        Program.Assert(!vm.CanConfirmSelected && vm.CanActFromHand, $"{label}: response should require selection.");
        var invalid = vm.Hand.FirstOrDefault(card => !card.IsPlayable);
        if (invalid is not null) vm.SelectCardCommand.Execute(invalid);
        foreach (var ambiguous in vm.Hand.Where(card => prompt.Choices.Count(choice => choice.Cards.Count == 1 && choice.Cards[0] == card.Id) > 1))
            Program.Assert(!ambiguous.IsPlayable && ambiguous.AvailabilityText.Contains("多种响应方式"), "Ambiguous card silently picked an effect.");
        Program.Assert(!vm.HasSelection, $"{label}: invalid hand card selected.");
        vm.SelectCardCommand.Execute(card);
        Program.Assert(card.IsSelected && vm.CanConfirmSelected && vm.ActionHint.Contains(choice.Description), $"{label}: missing exact response description.");
        Program.Assert(SnapshotJson.Serialize(engine.CreateSnapshot(0, true)) == state, $"{label}: selecting paid the cost.");
        Shortcut(window, Key.Escape);
        Program.Assert(!vm.CanConfirmSelected && !card.IsSelected && engine.PendingDecision?.PromptId == prompt.PromptId,
            $"{label}: Esc should cancel selection, never decline the response.");
        var index = vm.Hand.IndexOf(card);
        if (index < 9) Program.Assert(Shortcut(window, Key.D1 + index), "Number shortcut did not select response card.");
        else vm.SelectCardCommand.Execute(card);
        Shortcut(window, Key.F1);
        Program.Assert(!Shortcut(window, Key.Enter) && SnapshotJson.Serialize(engine.CreateSnapshot(0, true)) == state,
            $"{label}: guide allowed a hidden confirmation.");
        Program.Assert(vm.CurrentGuideSteps.Any(step => step.Text.Contains(choice.Description)), $"{label}: guide lost the chosen effect.");
        Shortcut(window, Key.Escape);
        vm.StartTutorialCommand.Execute(null);
        vm.ExitTutorialCommand.Execute(null);
        Program.Assert(vm.Hand.Single(item => item.Id == card.Id).IsSelected && vm.CanConfirmSelected &&
            ReferenceEquals(engine, Program.Engine(vm)) && SnapshotJson.Serialize(engine.CreateSnapshot(0, true)) == state,
            $"{label}: tutorial lost response selection or changed the match.");
        Program.Render(root, 1120, 740, Path.Combine(output, $"40-response-{label}.png"));
        if (prompt.Kind == DecisionKind.Nullification)
            Program.Assert(((TextBlock)window.FindName("DecisionDescriptionText")).Visibility == Visibility.Collapsed &&
                ((TextBlock)window.FindName("ActionDockHint")).Visibility == Visibility.Collapsed &&
                ((Border)window.FindName("DecisionPanel")).Visibility == Visibility.Collapsed &&
                ((Border)window.FindName("ResponseStatusBanner")).Visibility == Visibility.Visible && card.IsSelected,
                "Nullification must use its live response status, hand selection and buttons without explanatory text or an empty prompt panel.");
        Program.Assert(((TextBlock)window.FindName("DecisionTitleText")).Text == vm.CurrentDecisionContext!.Title &&
            ((TextBlock)window.FindName("DecisionDescriptionText")).Text == vm.CurrentDecisionContext.Description,
            "Actual response context controls did not bind to the current prompt.");
        var confirm = (Button)window.FindName("PlayCardButton");
        Program.Assert(confirm.Visibility == Visibility.Visible && confirm.IsEnabled, $"{label}: main response control is missing.");
        Program.Assert(((ItemsControl)window.FindName("HandResponseExtraChoices")).Items.Count == vm.HandResponseExtraChoices.Count &&
            vm.HandResponseExtraChoices.All(candidate => candidate.Cards.Count != 1 || candidate.Cards[0] != card.Id),
            $"{label}: selected hand response still has a duplicate central button.");
        Program.Assert(((Button)window.FindName("CancelActionButton")).Visibility == Visibility.Visible &&
            ((Grid)window.FindName("PrimaryActionButtons")).Children.OfType<Button>().Count(button => button.Visibility == Visibility.Visible) == 2,
            $"{label}: response must expose only the bottom confirm and cancel buttons.");
        if (expectedButtonText is not null) Program.Assert(Equals(confirm.Content, expectedButtonText), "Converted response button did not name the effective card.");
        Program.Assert(Shortcut(window, Key.Enter), $"{label}: Enter was not handled.");
        Program.Assert(engine.Revision == prompt.Revision + 1 && engine.AcceptedCommands.Last() is AnswerPromptCommand answer &&
            answer.Choice == choice.Id && answer.PromptId == prompt.PromptId,
            $"{label}: confirmation did not submit the selected exact choice once.");
        Program.Assert(!vm.HasSelection && !vm.CanConfirmSelected, $"{label}: old selection leaked into next prompt.");
        DecisionContextChecks.VerifyLive(vm);
        var after = engine.Revision;
        vm.ConfirmSelectedCommand.Execute(null);
        Program.Assert(engine.Revision == after, $"{label}: repeated confirmation reused the previous choice.");
        Console.WriteLine($"  Hand response verified: {label}, {choice.Description}");
        window.Content = null;
        window.Close();
    }

    private static void VerifyCancel(MainViewModel vm, string output, DecisionKind kind, ContentRegistry registry,
        bool checkFocusedEnter = false)
    {
        // Reload the saved, real pending response so both unselected and selected cancellation
        // exercise the same published prompt without another setup or seed search.
        foreach (var selectCard in new[] { false, true })
        {
            vm.LoadManualGameCommand.Execute(null);
            var engine = Program.Engine(vm);
            var prompt = engine.PendingDecision!;
            Program.Assert(prompt.Kind == kind, "Reload did not restore the original response.");
            if (checkFocusedEnter && !selectCard) VerifyFocusedCancelEnter(engine.CreateCheckpoint(), registry);
            var decline = prompt.Choices.SingleOrDefault(choice => choice.Cards.Count == 0);
            Program.Assert(vm.HandResponseExtraChoices.Count == 0,
                $"{kind}: ordinary hand choices or declining were duplicated in the center.");
            Program.Assert(vm.CanCancelAction == (decline is not null),
                $"{kind}: cancellation availability does not match the real prompt.");
            if (selectCard) vm.SelectCardCommand.Execute(vm.Hand.Last(card => card.IsPlayable));
            var window = new MainWindow(vm);
            var root = (FrameworkElement)window.Content;
            Program.Render(root, 1120, 740, Path.Combine(output, $"response-{kind}-cancel-{selectCard}.png"));
            var cancel = (Button)window.FindName("CancelActionButton");
            Program.Assert(cancel.IsEnabled == (selectCard || decline is not null),
                $"{kind}: the actual cancel control has the wrong enabled state.");
            var movements = engine.CardMovements.Count;
            if (cancel.IsEnabled) cancel.Command!.Execute(cancel.CommandParameter);
            if (decline is null)
                Program.Assert(engine.Revision == prompt.Revision && engine.PendingDecision?.PromptId == prompt.PromptId && !vm.HasSelection,
                    "Mandatory fire-attack revealing must only clear selection, never decline or submit a card.");
            else
                Program.Assert(engine.Revision == prompt.Revision + 1 && engine.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                    answer.PromptId == prompt.PromptId && answer.Choice == decline.Id && !vm.HasSelection &&
                    !engine.CardMovements.Skip(movements).Any(move => move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing),
                    $"{kind}: Cancel must decline once, with no selected card paid as a response cost.");
            window.Content = null;
            window.Close();
        }
    }

    private static void VerifyFocusedCancelEnter(GameCheckpoint checkpoint, ContentRegistry registry)
    {
        // These fixed checkpoint objects retain the same accepted prefixes throughout
        // this method. Cache only the independent expected state, never a live engine.
        var expectedLoadBoundaries = new Dictionary<GameCheckpoint, string>(ReferenceEqualityComparer.Instance);
        var loadCount = 0;
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, checkpoint));
        using var model = new MainViewModel(false, checkpoint.Options.Seed, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var game = Program.Engine(model);
        var boundary = ResponseBoundary(game);
        var prompt = model.ChoiceSnapshot.PendingDecision!;
        var card = model.Hand.Last(candidate => candidate.IsPlayable && candidate.Kind == CardKind.Dodge);
        var response = prompt.Choices.Single(choice => choice.Cards.SequenceEqual([card.Id]));
        var decline = prompt.Choices.Single(choice => choice.Cards.Count == 0);
        Program.Assert(!model.HasSaveError && prompt is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0, IsPrivate: true } &&
            game.CreateSnapshot(0).Players.Count == 4 && decline.Parameters.GetValueOrDefault("response") == "take-damage" &&
            ResponseBoundary(GameReplay.Restore(checkpoint, registry)) == boundary,
            "The fixed native Dodge fixture must restore its real selected-card and decline alternatives with all four safe views intact.");
        var window = new MainWindow(model)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -30000, Top = -30000, Width = 1120, Height = 820
        };
        var root = (FrameworkElement)window.Content;
        var cancel = (Button)window.FindName("CancelActionButton");
        var previewCount = 0;
        var bubbleCount = 0;
        KeyEventHandler observePreview = (_, args) => { if (args.Key == Key.Enter) previewCount++; };
        KeyEventHandler observeBubble = (_, args) => { if (args.Key == Key.Enter) bubbleCount++; };
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            const int extendedStyle = -20;
            const int noActivate = 0x08000000;
            SetWindowLong(handle, extendedStyle, GetWindowLong(handle, extendedStyle) | noActivate);
            var previousParent = SetParent(handle, new nint(-3));
            var parentError = Marshal.GetLastPInvokeError();
            Program.Assert((previousParent != 0 || parentError == 0) && IsMessageOnlyWindow(handle),
                $"The cancel-key fixture's own HWND must be message-only: parent={previousParent}, error={parentError}.");
            window.Show();
            ShowWindow(handle, 0);
            SetFocus(handle);
            Pump();
            model.SelectCardCommand.Execute(card);
            Pump();
            var focusAccepted = cancel.Focus();
            var focusReady = window.IsLoaded && window.IsVisible && IsMessageOnlyWindow(handle) && GetFocus() == handle &&
                !IsWindowVisible(handle) && GetForegroundWindow() != handle &&
                (GetWindowLong(handle, extendedStyle) & noActivate) != 0 &&
                ReferenceEquals(PresentationSource.FromVisual(cancel), PresentationSource.FromVisual(window)) &&
                ReferenceEquals(cancel.Command, model.CancelActionCommand) && cancel.IsVisible && cancel.IsEnabled &&
                cancel.Command!.CanExecute(cancel.CommandParameter) && ReferenceEquals(Keyboard.FocusedElement, cancel) &&
                Keyboard.Modifiers == ModifierKeys.None && card.IsSelected && model.CanConfirmSelected &&
                ResponseBoundary(game) == boundary;
            if (!focusReady)
                Console.WriteLine($"Cancel Enter prerequisites: accepted focus={focusAccepted}, loaded={window.IsLoaded}, WPF visible={window.IsVisible}, " +
                    $"message only={IsMessageOnlyWindow(handle)}, exact native focus={GetFocus() == handle}, native hidden={!IsWindowVisible(handle)}, " +
                    $"owns foreground={GetForegroundWindow() == handle}, no activate={(GetWindowLong(handle, extendedStyle) & noActivate) != 0}, " +
                    $"same source={ReferenceEquals(PresentationSource.FromVisual(cancel), PresentationSource.FromVisual(window))}, " +
                    $"same command={ReferenceEquals(cancel.Command, model.CancelActionCommand)}, visible/enabled={cancel.IsVisible}/{cancel.IsEnabled}, " +
                    $"exact WPF focus={ReferenceEquals(Keyboard.FocusedElement, cancel)}, modifiers={Keyboard.Modifiers}, " +
                    $"selected={card.IsSelected}, confirm={model.CanConfirmSelected}, same boundary={ResponseBoundary(game) == boundary}.");
            Program.Assert(focusReady,
                "The actual loaded cancel button must own exact keyboard focus over a reversible native Dodge draft without taking foreground.");
            var reference = GameReplay.Restore(checkpoint, registry);
            var expected = reference.Submit(new AnswerPromptCommand(0, prompt.PromptId, decline.Id, reference.Revision));
            Program.Assert(expected.Accepted, expected.Error?.Message ?? "The native decline alternative must be accepted independently.");
            var beforeMovements = game.CardMovements.Count;
            window.AddHandler(Keyboard.PreviewKeyDownEvent, observePreview, handledEventsToo: true);
            cancel.AddHandler(Keyboard.KeyDownEvent, observeBubble, handledEventsToo: true);
            // Use the standard tunnel then unhandled bubble route. The actual
            // Window preview handler and Button class handler own the operation.
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(cancel)!, 0, Key.Enter)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            cancel.RaiseEvent(key);
            var previewHandled = key.Handled;
            if (!key.Handled)
            {
                key.RoutedEvent = Keyboard.KeyDownEvent;
                cancel.RaiseEvent(key);
            }
            var actualChoice = (game.AcceptedCommands.LastOrDefault() as AnswerPromptCommand)?.Choice;
            Console.WriteLine($"Focused Cancel Enter: seed=31, prompt={prompt.PromptId}, selected={response.Id}, expected={decline.Id}, " +
                $"actual={actualChoice}, preview={previewCount}, preview handled={previewHandled}, bubble={bubbleCount}, handled={key.Handled}, " +
                $"same native decline={ResponseBoundary(game) == ResponseBoundary(reference)}.");
            Program.Assert(previewCount == 1, "The actual Window PreviewKeyDown must observe the focused button's Enter once.");
            Program.Assert(game.Revision == checkpoint.Revision + 1 && game.AcceptedCommands.Count == checkpoint.Commands.Count + 1 &&
                game.AcceptedCommands.Last() is AnswerPromptCommand submitted && submitted.ActorSeat == 0 &&
                submitted.PromptId == prompt.PromptId && submitted.ExpectedRevision == checkpoint.Revision && submitted.Choice == decline.Id &&
                !game.CardMovements.Skip(beforeMovements).Any(move => move.CardId == card.Id &&
                    move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Respond) &&
                ResponseBoundary(game) == ResponseBoundary(reference),
                "Enter on the focused actual Cancel button must submit native decline once, never pay the selected Dodge as a response.");
            Program.Assert(!previewHandled && bubbleCount == 1 && key.Handled && !model.HasSelection,
                "The ordinary Enter must reach the actual Cancel button's KeyDown handler and clear the completed response draft.");
            var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Program.Assert(ResponseBoundary(cold) == ResponseBoundary(game),
                "The actual keyboard decline must cold-restore every safe view, typed event, accepted command and card movement exactly.");

            foreach (var focus in new[] { "hand", "confirm" })
            {
                var currentGame = Load(checkpoint);
                var currentPrompt = model.ChoiceSnapshot.PendingDecision!;
                var currentCard = model.Hand.Single(candidate => candidate.Id == card.Id);
                var currentChoice = currentPrompt.Choices.Single(candidate => candidate.Cards.SequenceEqual([currentCard.Id]));
                model.SelectCardCommand.Execute(currentCard);
                Pump();
                var control = focus == "hand"
                    ? Program.Find<Button>(root).Single(candidate => ReferenceEquals(candidate.Command, model.SelectCardCommand) &&
                        ReferenceEquals(candidate.CommandParameter, currentCard))
                    : (Button)window.FindName("PlayCardButton");
                var native = GameReplay.Restore(checkpoint, registry);
                Program.Assert(native.Submit(new AnswerPromptCommand(0, currentPrompt.PromptId, currentChoice.Id, native.Revision)).Accepted,
                    "The same actual Dodge must remain an accepted independent native response.");
                var route = EnterOn(control);
                Program.Assert(route.PreviewHandled == (focus == "hand") && route.Handled &&
                    currentGame.Revision == checkpoint.Revision + 1 && currentGame.AcceptedCommands.Count == checkpoint.Commands.Count + 1 &&
                    currentGame.AcceptedCommands.Last() is AnswerPromptCommand answer && answer.Choice == currentChoice.Id &&
                    !model.HasSelection && ResponseBoundary(currentGame) == ResponseBoundary(native),
                    "Focused hand and primary-confirm Enter must each preserve exact native selected-response confirmation once.");
                AssertCold(currentGame);
                Console.WriteLine($"Focused {focus} Enter: choice={currentChoice.Id}, preview handled={route.PreviewHandled}, same native response=True.");
            }

            // Share the immutable Play checkpoint, retaining an independent native engine.
            var numberPlayPrefix = checkpoint.Commands.TakeWhile(command => command is not EndPlayPhaseCommand { ActorSeat: 0 }).ToArray();
            var numberPlayNative = GameReplay.Replay(checkpoint.Options, numberPlayPrefix, registry);
            var numberPlayCheckpoint = numberPlayNative.CreateCheckpoint();
            Program.Assert(numberPlayPrefix.Length > 0 && numberPlayPrefix.Length < checkpoint.Commands.Count &&
                numberPlayNative.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
                "The original native fixture prefix must expose the human's first Play boundary.");
            var numberPlayInitialBoundary = ResponseBoundary(numberPlayNative);

            {
                var numberGame = Load(checkpoint);
                var numberPrompt = model.ChoiceSnapshot.PendingDecision!;
                var numberCard = model.Hand.Last(candidate => candidate.IsPlayable && candidate.Kind == CardKind.Dodge);
                var numberIndex = model.Hand.IndexOf(numberCard);
                var numberChoice = numberPrompt.Choices.Single(candidate => candidate.Cards.SequenceEqual([numberCard.Id]));
                var numberKey = Key.D1 + numberIndex;
                cancel.Focus();
                Program.Assert(numberIndex is >= 0 and < 9 && !model.HasSelection && !model.CanConfirmSelected &&
                    model.CanCancelAction && cancel.IsVisible && cancel.IsEnabled &&
                    ReferenceEquals(cancel.Command, model.CancelActionCommand) && cancel.Command!.CanExecute(cancel.CommandParameter) &&
                    ReferenceEquals(Keyboard.FocusedElement, cancel) && GetFocus() == handle &&
                    IsMessageOnlyWindow(handle) && !IsWindowVisible(handle) && GetForegroundWindow() != handle &&
                    (GetWindowLong(handle, extendedStyle) & noActivate) != 0 && Keyboard.Modifiers == ModifierKeys.None &&
                    ResponseBoundary(numberGame) == boundary,
                    "The native number-key response must begin unselected with exact focus on the actual enabled Cancel button in the owned hidden HWND.");
                var selectedRoute = RouteFocusedKey(cancel, numberKey);
                Program.Assert(selectedRoute.PreviewHandled && selectedRoute.Bubbles == 0 && selectedRoute.Handled &&
                    numberCard.IsSelected && model.CanConfirmSelected && numberGame.Revision == checkpoint.Revision &&
                    numberGame.AcceptedCommands.Count == checkpoint.Commands.Count && ResponseBoundary(numberGame) == boundary,
                    "The actual number key must only select its current native Dodge, preserving every safe view and journal without a command.");
                Pump();
                var numberButton = Program.Find<Button>(root).Single(candidate =>
                    ReferenceEquals(candidate.Command, model.SelectCardCommand) && ReferenceEquals(candidate.CommandParameter, numberCard));
                var exactHandFocus = ReferenceEquals(Keyboard.FocusedElement, numberButton);
                Program.Assert(Keyboard.FocusedElement is Button,
                    "After the number key, the actual current keyboard target must remain an enabled table Button for Enter routing.");
                var actualInputTarget = (Button)Keyboard.FocusedElement!;
                var numberNative = GameReplay.Restore(checkpoint, registry);
                Program.Assert(numberNative.Submit(new AnswerPromptCommand(0, numberPrompt.PromptId, numberChoice.Id, numberNative.Revision)).Accepted,
                    "The number-selected native Dodge choice must be accepted independently.");
                // Deliberately use the actual resulting focus. Focusing the hand
                // here would conceal a number shortcut that left Cancel active.
                var numberEnter = RouteFocusedKey(actualInputTarget, Key.Enter);
                var numberActualChoice = (numberGame.AcceptedCommands.LastOrDefault() as AnswerPromptCommand)?.Choice;
                Console.WriteLine($"Focused response number then Enter: index={numberIndex + 1}, card={numberCard.Id}, " +
                    $"expected={numberChoice.Id}, actual={numberActualChoice}, exact hand focus={exactHandFocus}, " +
                    $"number preview handled={selectedRoute.PreviewHandled}, Enter preview handled={numberEnter.PreviewHandled}, " +
                    $"Enter bubble={numberEnter.Bubbles}, handled={numberEnter.Handled}, " +
                    $"same native response={ResponseBoundary(numberGame) == ResponseBoundary(numberNative)}.");
                Program.Assert(exactHandFocus && numberEnter.PreviewHandled && numberEnter.Bubbles == 0 && numberEnter.Handled &&
                    numberGame.Revision == checkpoint.Revision + 1 && numberGame.AcceptedCommands.Count == checkpoint.Commands.Count + 1 &&
                    numberGame.AcceptedCommands.Last() is AnswerPromptCommand numberSubmitted && numberSubmitted.ActorSeat == 0 &&
                    numberSubmitted.PromptId == numberPrompt.PromptId && numberSubmitted.ExpectedRevision == checkpoint.Revision &&
                    numberSubmitted.Choice == numberChoice.Id && !model.HasSelection &&
                    ResponseBoundary(numberGame) == ResponseBoundary(numberNative),
                    "A number key must focus its actual selected hand Button so immediate Enter submits that exact native Dodge once, never declines from the old Cancel focus.");
                AssertCold(numberGame);

                var ignoredGame = Load(checkpoint);
                var invalidNumberCard = model.Hand.First(candidate => !candidate.IsPlayable);
                var invalidNumberIndex = model.Hand.IndexOf(invalidNumberCard);
                Program.Assert(invalidNumberIndex is >= 0 and < 9 && model.Hand.Count < 9,
                    "The same native response must expose an unplayable numbered card and an out-of-range ninth slot.");
                cancel.Focus();
                foreach (var ignoredKey in new[] { Key.D1 + invalidNumberIndex, Key.D9 })
                {
                    var ignoredRoute = RouteFocusedKey(cancel, ignoredKey);
                    Program.Assert(!ignoredRoute.PreviewHandled && !model.HasSelection &&
                        ReferenceEquals(Keyboard.FocusedElement, cancel) && !invalidNumberCard.IsSelected &&
                        ResponseBoundary(ignoredGame) == boundary,
                        "An unplayable or out-of-range number must preserve the exact current action focus and untouched native response.");
                }
                Console.WriteLine("Focused response ignored numbers: unplayable and out-of-range, same action focus=True, added commands=0, same native boundary=True.");

                var toggledGame = Load(checkpoint);
                var toggledCard = model.Hand.Single(candidate => candidate.Id == numberCard.Id);
                var toggledButton = Program.Find<Button>(root).Single(candidate =>
                    ReferenceEquals(candidate.Command, model.SelectCardCommand) && ReferenceEquals(candidate.CommandParameter, toggledCard));
                cancel.Focus();
                foreach (var selected in new[] { true, false, true })
                {
                    var toggleRoute = RouteFocusedKey((Button)Keyboard.FocusedElement!, numberKey);
                    Pump();
                    Program.Assert(toggleRoute.PreviewHandled && toggleRoute.Bubbles == 0 && toggleRoute.Handled &&
                        toggledCard.IsSelected == selected && model.CanConfirmSelected == selected &&
                        ReferenceEquals(Keyboard.FocusedElement, toggledButton) && ResponseBoundary(toggledGame) == boundary,
                        "Each fresh number press must toggle only the current draft while retaining exact current card focus and every native boundary.");
                }
                var toggleEnter = RouteFocusedKey((Button)Keyboard.FocusedElement!, Key.Enter);
                Program.Assert(toggleEnter.PreviewHandled && toggleEnter.Bubbles == 0 && toggleEnter.Handled &&
                    toggledGame.AcceptedCommands.Count == checkpoint.Commands.Count + 1 &&
                    toggledGame.AcceptedCommands.Last() is AnswerPromptCommand toggledAnswer && toggledAnswer.Choice == numberChoice.Id &&
                    ResponseBoundary(toggledGame) == ResponseBoundary(numberNative),
                    "After reversible fresh number toggles, actual focused Enter must still submit the original native Dodge exactly once.");
                AssertCold(toggledGame);
                Console.WriteLine($"Focused response fresh number toggles: card={toggledCard.Id}, exact hand focus=True, added commands=1, same native response=True, cold boundary=True.");

                var numberPlayGame = Load(numberPlayCheckpoint);
                var numberPlayPrompt = model.ChoiceSnapshot.PendingDecision!;
                var numberPlayAction = numberPlayGame.GetHumanLegalActions().First(candidate =>
                    candidate.Kind == LegalActionKind.Slash && candidate.CardId is not null && candidate.TargetSeats.Count == 1);
                var numberPlayCard = model.Hand.Single(candidate => candidate.Id == numberPlayAction.CardId);
                var numberPlayIndex = model.Hand.IndexOf(numberPlayCard);
                var numberPlayTarget = model.Seats.Single(candidate => candidate.Seat == numberPlayAction.TargetSeats.Single());
                var numberPlayButton = Program.Find<Button>(root).Single(candidate =>
                    ReferenceEquals(candidate.Command, model.SelectCardCommand) && ReferenceEquals(candidate.CommandParameter, numberPlayCard));
                var numberPlayEnd = (Button)window.FindName("EndTurnButton");
                Program.Assert(numberPlayIndex is >= 0 and < 9 && numberPlayPrompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                    !model.HasSelection && model.CanEndTurn && numberPlayEnd.IsVisible && numberPlayEnd.IsEnabled,
                    "The original accepted native prefix must expose an unselected legal numbered Slash and an actual enabled End Turn button.");
                numberPlayEnd.Focus();
                var numberPlayRoute = RouteFocusedKey(numberPlayEnd, Key.D1 + numberPlayIndex);
                Pump();
                Program.Assert(numberPlayRoute.PreviewHandled && numberPlayRoute.Bubbles == 0 && numberPlayRoute.Handled &&
                    numberPlayCard.IsSelected && !model.CanConfirmSelected &&
                    ReferenceEquals(Keyboard.FocusedElement, numberPlayButton) &&
                    ResponseBoundary(numberPlayGame) == ResponseBoundary(numberPlayNative),
                    "A Play number key must move focus from End Turn to its exact Slash without committing an incomplete target draft.");
                model.SelectTargetCommand.Execute(numberPlayTarget);
                Pump();
                Program.Assert(numberPlayTarget.IsSelectedTarget && model.CanConfirmSelected &&
                    ReferenceEquals(Keyboard.FocusedElement, numberPlayButton) &&
                    ResponseBoundary(numberPlayGame) == ResponseBoundary(numberPlayNative),
                    "Adding the current legal target must keep the numbered hand focus and complete only the reversible native Slash draft.");
                Program.Assert(numberPlayNative.Submit(new PlayCardCommand(0, numberPlayCard.Id, numberPlayAction.TargetSeats,
                    numberPlayNative.Revision, numberPlayPrompt.PromptId, numberPlayAction.PlayedCardKind, numberPlayAction.TargetCardId)
                    { ConversionSource = numberPlayAction.ConversionSource, AdditionalConversionSources = numberPlayAction.AdditionalConversionSources }).Accepted,
                    "The number-selected physical Slash and current target must be independently accepted natively.");
                var numberPlayEnter = RouteFocusedKey((Button)Keyboard.FocusedElement!, Key.Enter);
                Program.Assert(numberPlayEnter.PreviewHandled && numberPlayEnter.Bubbles == 0 && numberPlayEnter.Handled &&
                    numberPlayGame.AcceptedCommands.Count == numberPlayCheckpoint.Commands.Count + 1 &&
                    numberPlayGame.AcceptedCommands.Last() is PlayCardCommand numberPlayCommand &&
                    numberPlayCommand.ActorSeat == 0 && numberPlayCommand.CardId == numberPlayCard.Id &&
                    numberPlayCommand.PromptId == numberPlayPrompt.PromptId &&
                    numberPlayCommand.ExpectedRevision == numberPlayCheckpoint.Revision &&
                    numberPlayCommand.TargetSeats.SequenceEqual(numberPlayAction.TargetSeats) &&
                    ResponseBoundary(numberPlayGame) == ResponseBoundary(numberPlayNative),
                    "Enter after numbering a Play card and completing its current target must submit that exact Slash once, never execute the old End Turn action.");
                AssertCold(numberPlayGame);
                Console.WriteLine($"Focused Play number then Enter: card={numberPlayCard.Id}, target={numberPlayTarget.Seat}, exact hand focus=True, added commands=1, same native Slash=True, cold boundary=True.");

                var switchedResponseGame = Load(checkpoint);
                var switchedResponsePrompt = model.ChoiceSnapshot.PendingDecision!;
                var previousResponseCard = model.Hand.Single(candidate => candidate.Id == card.Id);
                var focusedResponseCard = model.Hand.First(candidate => candidate.IsPlayable && candidate.Kind == CardKind.Dodge &&
                    candidate.Id != previousResponseCard.Id);
                var previousResponseChoice = switchedResponsePrompt.Choices.Single(candidate => candidate.Cards.SequenceEqual([previousResponseCard.Id]));
                var focusedResponseChoice = switchedResponsePrompt.Choices.Single(candidate => candidate.Cards.SequenceEqual([focusedResponseCard.Id]));
                var focusedResponseButton = Program.Find<Button>(root).Single(candidate =>
                    ReferenceEquals(candidate.Command, model.SelectCardCommand) && ReferenceEquals(candidate.CommandParameter, focusedResponseCard));
                model.SelectCardCommand.Execute(previousResponseCard);
                Pump();
                focusedResponseButton.Focus();
                Program.Assert(previousResponseCard.IsPlayable && focusedResponseCard.IsPlayable && previousResponseCard.IsSelected &&
                    !focusedResponseCard.IsSelected && model.CanConfirmSelected && previousResponseChoice.Id != focusedResponseChoice.Id &&
                    model.Hand.Contains(previousResponseCard) && model.Hand.Contains(focusedResponseCard) &&
                    ReferenceEquals(Keyboard.FocusedElement, focusedResponseButton) &&
                    ResponseBoundary(switchedResponseGame) == boundary,
                    "The native response must expose two different unique playable Dodge choices, with the completed old draft and exact focus on the unselected current card.");
                var switchedResponseRoute = RouteFocusedKey(focusedResponseButton, Key.Enter);
                var switchedResponseAnswer = switchedResponseGame.AcceptedCommands.LastOrDefault() as AnswerPromptCommand;
                var handPreviewSucceeded = !switchedResponseRoute.PreviewHandled && switchedResponseRoute.Bubbles == 1 &&
                    switchedResponseRoute.Handled && focusedResponseCard.IsSelected && !previousResponseCard.IsSelected &&
                    model.CanConfirmSelected && ResponseBoundary(switchedResponseGame) == boundary;
                Console.WriteLine($"Focused other hand Enter: previous card={previousResponseCard.Id}, focused card={focusedResponseCard.Id}, " +
                    $"previous choice={previousResponseChoice.Id}, expected preview choice={focusedResponseChoice.Id}, actual answer={switchedResponseAnswer?.Choice}, " +
                    $"preview handled={switchedResponseRoute.PreviewHandled}, bubble={switchedResponseRoute.Bubbles}, handled={switchedResponseRoute.Handled}, " +
                    $"previous selected={previousResponseCard.IsSelected}, focused selected={focusedResponseCard.IsSelected}, " +
                    $"added commands={switchedResponseGame.AcceptedCommands.Count - checkpoint.Commands.Count}, " +
                    $"same native boundary={ResponseBoundary(switchedResponseGame) == boundary}, preview succeeded={handPreviewSucceeded}.");

                Program.Assert(handPreviewSucceeded && ReferenceEquals(Keyboard.FocusedElement, focusedResponseButton),
                    "Previewing the other native Dodge must retain its exact live hand focus without submitting the old response.");
                var switchedResponseReference = GameReplay.Restore(checkpoint, registry);
                Program.Assert(switchedResponseReference.Submit(new AnswerPromptCommand(0, switchedResponsePrompt.PromptId,
                    focusedResponseChoice.Id, switchedResponseReference.Revision)).Accepted,
                    "The newly focused current Dodge must be independently accepted at the original native prompt.");
                var switchedResponseConfirm = RouteFocusedKey(focusedResponseButton, Key.Enter);
                Program.Assert(switchedResponseConfirm.PreviewHandled && switchedResponseConfirm.Bubbles == 0 &&
                    switchedResponseConfirm.Handled && switchedResponseGame.Revision == checkpoint.Revision + 1 &&
                    switchedResponseGame.AcceptedCommands.Count == checkpoint.Commands.Count + 1 &&
                    switchedResponseGame.AcceptedCommands.Last() is AnswerPromptCommand switchedResponseSubmitted &&
                    switchedResponseSubmitted.ActorSeat == 0 && switchedResponseSubmitted.PromptId == switchedResponsePrompt.PromptId &&
                    switchedResponseSubmitted.ExpectedRevision == checkpoint.Revision &&
                    switchedResponseSubmitted.Choice == focusedResponseChoice.Id && !model.HasSelection &&
                    ResponseBoundary(switchedResponseGame) == ResponseBoundary(switchedResponseReference),
                    "The second fresh Enter at the retained selected hand focus must submit only the new native Dodge once, preserving all safe views and journals.");
                AssertCold(switchedResponseGame);
                Console.WriteLine($"Focused other hand second Enter: card={focusedResponseCard.Id}, choice={focusedResponseChoice.Id}, " +
                    "retained focus=True, added commands=1, same native response=True, cold boundary=True.");

                // Reload before the independent portrait experiment, so an old
                // hand submission cannot conceal the second ownership defect.
                var switchedTargetGame = Load(numberPlayCheckpoint);
                var switchedTargetPrompt = model.ChoiceSnapshot.PendingDecision!;
                var previousTargetAction = switchedTargetGame.GetHumanLegalActions().First(candidate =>
                    candidate.Kind == LegalActionKind.Slash && candidate.CardId is not null && candidate.TargetSeats.Count == 1);
                var focusedTargetAction = switchedTargetGame.GetHumanLegalActions().First(candidate =>
                    candidate.Kind == previousTargetAction.Kind && candidate.CardId == previousTargetAction.CardId &&
                    candidate.PlayedCardKind == previousTargetAction.PlayedCardKind &&
                    candidate.ConversionSource == previousTargetAction.ConversionSource && candidate.TargetSeats.Count == 1 &&
                    candidate.TargetSeats.Single() != previousTargetAction.TargetSeats.Single());
                var switchedTargetCard = model.Hand.Single(candidate => candidate.Id == previousTargetAction.CardId);
                var previousTarget = model.Seats.Single(candidate => candidate.Seat == previousTargetAction.TargetSeats.Single());
                var focusedTarget = model.Seats.Single(candidate => candidate.Seat == focusedTargetAction.TargetSeats.Single());
                var focusedTargetButton = Program.Find<Button>(root).Single(candidate =>
                    ReferenceEquals(candidate.Command, model.SelectTargetCommand) && ReferenceEquals(candidate.CommandParameter, focusedTarget));
                model.SelectCardCommand.Execute(switchedTargetCard);
                model.SelectTargetCommand.Execute(previousTarget);
                Pump();
                focusedTargetButton.Focus();
                var switchedTargetReference = GameReplay.Restore(numberPlayCheckpoint, registry);
                Program.Assert(switchedTargetPrompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                    switchedTargetCard.IsSelected && previousTarget.IsSelectedTarget && !focusedTarget.IsSelectedTarget &&
                    previousTarget.IsLegalTarget && focusedTarget.IsLegalTarget && model.CanConfirmSelected &&
                    model.Seats.Contains(previousTarget) && model.Seats.Contains(focusedTarget) &&
                    ReferenceEquals(Keyboard.FocusedElement, focusedTargetButton) &&
                    ResponseBoundary(switchedTargetGame) == ResponseBoundary(switchedTargetReference),
                    "The native physical Slash must offer both distinct current legal one-target actions while the old complete target draft remains unchanged under focus on the new portrait.");
                var switchedTargetBoundary = ResponseBoundary(switchedTargetGame);
                var switchedTargetRoute = RouteFocusedKey(focusedTargetButton, Key.Enter);
                var switchedTargetCommand = switchedTargetGame.AcceptedCommands.LastOrDefault() as PlayCardCommand;
                var portraitPreviewSucceeded = !switchedTargetRoute.PreviewHandled && switchedTargetRoute.Bubbles == 1 &&
                    switchedTargetRoute.Handled && focusedTarget.IsSelectedTarget && !previousTarget.IsSelectedTarget &&
                    switchedTargetCard.IsSelected && model.CanConfirmSelected && ResponseBoundary(switchedTargetGame) == switchedTargetBoundary;
                Console.WriteLine($"Focused other portrait Enter: card={switchedTargetCard.Id}, previous target={previousTarget.Seat}, focused target={focusedTarget.Seat}, " +
                    $"actual play card={switchedTargetCommand?.CardId}, actual targets={string.Join(',', switchedTargetCommand?.TargetSeats ?? [])}, " +
                    $"preview handled={switchedTargetRoute.PreviewHandled}, bubble={switchedTargetRoute.Bubbles}, handled={switchedTargetRoute.Handled}, " +
                    $"previous selected={previousTarget.IsSelectedTarget}, focused selected={focusedTarget.IsSelectedTarget}, " +
                    $"added commands={switchedTargetGame.AcceptedCommands.Count - numberPlayCheckpoint.Commands.Count}, " +
                    $"same native boundary={ResponseBoundary(switchedTargetGame) == switchedTargetBoundary}, preview succeeded={portraitPreviewSucceeded}.");
                Program.Assert(handPreviewSucceeded && portraitPreviewSucceeded,
                    "First Enter on a different current hand card or legal portrait must only preview that focused object, never submit the previous completed draft.");

                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, focusedTargetButton),
                    "Previewing the new legal target must retain its actual current portrait focus for the next fresh Enter.");
                Program.Assert(switchedTargetReference.Submit(new PlayCardCommand(0, switchedTargetCard.Id, focusedTargetAction.TargetSeats,
                    switchedTargetReference.Revision, switchedTargetPrompt.PromptId, focusedTargetAction.PlayedCardKind, focusedTargetAction.TargetCardId)
                    { ConversionSource = focusedTargetAction.ConversionSource, AdditionalConversionSources = focusedTargetAction.AdditionalConversionSources }).Accepted,
                    "The newly focused physical Slash target must be independently accepted from the unchanged native Play checkpoint.");
                var switchedTargetConfirm = RouteFocusedKey(focusedTargetButton, Key.Enter);
                Program.Assert(switchedTargetConfirm.PreviewHandled && switchedTargetConfirm.Bubbles == 0 &&
                    switchedTargetConfirm.Handled && switchedTargetGame.Revision == numberPlayCheckpoint.Revision + 1 &&
                    switchedTargetGame.AcceptedCommands.Count == numberPlayCheckpoint.Commands.Count + 1 &&
                    switchedTargetGame.AcceptedCommands.Last() is PlayCardCommand switchedTargetSubmitted &&
                    switchedTargetSubmitted.ActorSeat == 0 && switchedTargetSubmitted.CardId == switchedTargetCard.Id &&
                    switchedTargetSubmitted.PromptId == switchedTargetPrompt.PromptId &&
                    switchedTargetSubmitted.ExpectedRevision == numberPlayCheckpoint.Revision &&
                    switchedTargetSubmitted.TargetSeats.SequenceEqual(focusedTargetAction.TargetSeats) && !model.HasSelection &&
                    ResponseBoundary(switchedTargetGame) == ResponseBoundary(switchedTargetReference),
                    "The second fresh Enter at the retained selected portrait focus must submit the same physical Slash only against the new target once, preserving every native boundary.");
                AssertCold(switchedTargetGame);
                Console.WriteLine($"Focused other portrait second Enter: card={switchedTargetCard.Id}, target={focusedTarget.Seat}, " +
                    "retained focus=True, added commands=1, same native Slash=True, cold boundary=True.");

                var illegalTargetGame = Load(numberPlayCheckpoint);
                var illegalTargetCard = model.Hand.Single(candidate => candidate.Id == previousTargetAction.CardId);
                var retainedTarget = model.Seats.Single(candidate => candidate.Seat == previousTargetAction.TargetSeats.Single());
                model.SelectCardCommand.Execute(illegalTargetCard);
                model.SelectTargetCommand.Execute(retainedTarget);
                Pump();
                var illegalTarget = model.Seats.Single(candidate => candidate.Seat == 0);
                var illegalTargetButton = Program.Find<Button>(root).Single(candidate =>
                    ReferenceEquals(candidate.Command, model.SelectTargetCommand) && ReferenceEquals(candidate.CommandParameter, illegalTarget));
                illegalTargetButton.Focus();
                var illegalTargetBoundary = ResponseBoundary(illegalTargetGame);
                Program.Assert(illegalTargetCard.IsSelected && retainedTarget.IsSelectedTarget && retainedTarget.IsLegalTarget &&
                    !illegalTarget.IsSelectedTarget && !illegalTarget.IsLegalTarget && model.CanConfirmSelected &&
                    !illegalTargetGame.GetHumanLegalActions().Any(candidate => candidate.Kind == previousTargetAction.Kind &&
                        candidate.CardId == illegalTargetCard.Id && candidate.TargetSeats.Contains(illegalTarget.Seat)) &&
                    ReferenceEquals(Keyboard.FocusedElement, illegalTargetButton) &&
                    illegalTargetBoundary == ResponseBoundary(GameReplay.Restore(numberPlayCheckpoint, registry)),
                    "The native Slash draft must be complete while its actual enabled self portrait remains an unselected illegal target.");
                for (var press = 0; press < 2; press++)
                {
                    var illegalTargetRoute = RouteFocusedKey(illegalTargetButton, Key.Enter);
                    Program.Assert(!illegalTargetRoute.PreviewHandled && illegalTargetRoute.Bubbles == 1 && illegalTargetRoute.Handled &&
                        illegalTargetCard.IsSelected && retainedTarget.IsSelectedTarget && !illegalTarget.IsSelectedTarget &&
                        model.CanConfirmSelected && ReferenceEquals(Keyboard.FocusedElement, illegalTargetButton) &&
                        ResponseBoundary(illegalTargetGame) == illegalTargetBoundary,
                        "Each fresh Enter on an unselected illegal portrait must keep the completed old draft and all native boundaries untouched, never attack the old target.");
                }
                AssertCold(illegalTargetGame);
                Console.WriteLine($"Focused illegal portrait Enter: card={illegalTargetCard.Id}, retained target={retainedTarget.Seat}, " +
                    $"focused target={illegalTarget.Seat}, fresh presses=2, added commands=0, same native boundary=True, cold boundary=True.");

                var controlResponsesPreserved = true;
                foreach (var focus in new[] { "confirm", "cancel", "hand" })
                {
                    var controlGame = Load(checkpoint);
                    var controlCard = model.Hand.Single(candidate => candidate.Id == card.Id);
                    model.SelectCardCommand.Execute(controlCard);
                    Pump();
                    var controlButton = focus switch
                    {
                        "confirm" => (Button)window.FindName("PlayCardButton"),
                        "cancel" => (Button)window.FindName("CancelActionButton"),
                        _ => Program.Find<Button>(root).Single(candidate =>
                            ReferenceEquals(candidate.Command, model.SelectCardCommand) && ReferenceEquals(candidate.CommandParameter, controlCard))
                    };
                    controlButton.Focus();
                    Program.Assert(!model.CanEndTurn && model.IsHandResponsePending && controlCard.IsSelected &&
                        model.CanConfirmSelected && ReferenceEquals(Keyboard.FocusedElement, controlButton) &&
                        ResponseBoundary(controlGame) == boundary,
                        "Unavailable Ctrl+Enter must begin on an actual current response control above the unchanged completed native Dodge draft.");
                    var controlRoute = TableShortcutChecks.WithControl(() => RouteFocusedKey(controlButton, Key.Enter, ModifierKeys.Control));
                    var controlPreserved = controlRoute.PreviewHandled && controlRoute.Bubbles == 0 && controlRoute.Handled &&
                        controlCard.IsSelected && model.CanConfirmSelected && model.HasSelection &&
                        ReferenceEquals(Keyboard.FocusedElement, controlButton) && ResponseBoundary(controlGame) == boundary;
                    controlResponsesPreserved &= controlPreserved;
                    Console.WriteLine($"Focused response Ctrl+Enter {focus}: selected card={controlCard.Id}, " +
                        $"actual answer={(controlGame.AcceptedCommands.LastOrDefault() as AnswerPromptCommand)?.Choice}, " +
                        $"preview handled={controlRoute.PreviewHandled}, bubble={controlRoute.Bubbles}, handled={controlRoute.Handled}, " +
                        $"selected={controlCard.IsSelected}, can confirm={model.CanConfirmSelected}, " +
                        $"added commands={controlGame.AcceptedCommands.Count - checkpoint.Commands.Count}, " +
                        $"same native boundary={ResponseBoundary(controlGame) == boundary}, preserved={controlPreserved}, modifiers restored={Keyboard.Modifiers == ModifierKeys.None}.");
                    Program.Assert(controlPreserved,
                        "Unavailable Ctrl+Enter must leave this exact response control, draft and complete native boundary ready for ordinary Enter.");
                    AssertCold(controlGame);
                    var controlChoice = focus == "cancel" ? decline.Id : response.Id;
                    var controlNative = GameReplay.Restore(checkpoint, registry);
                    Program.Assert(controlNative.Submit(new AnswerPromptCommand(0, prompt.PromptId, controlChoice, controlNative.Revision)).Accepted,
                        "The intended ordinary response action after unavailable Ctrl+Enter must be independently accepted natively.");
                    var ordinaryAfterControl = RouteFocusedKey(controlButton, Key.Enter);
                    Program.Assert(ordinaryAfterControl.PreviewHandled == (focus == "hand") &&
                        ordinaryAfterControl.Bubbles == (focus == "hand" ? 0 : 1) && ordinaryAfterControl.Handled &&
                        controlGame.Revision == checkpoint.Revision + 1 && controlGame.AcceptedCommands.Count == checkpoint.Commands.Count + 1 &&
                        controlGame.AcceptedCommands.Last() is AnswerPromptCommand ordinaryControlAnswer &&
                        ordinaryControlAnswer.ActorSeat == 0 && ordinaryControlAnswer.PromptId == prompt.PromptId &&
                        ordinaryControlAnswer.ExpectedRevision == checkpoint.Revision && ordinaryControlAnswer.Choice == controlChoice &&
                        !model.HasSelection && ResponseBoundary(controlGame) == ResponseBoundary(controlNative),
                        "After thread modifiers are restored, ordinary Enter at the retained actual control must perform only its intended native response once.");
                    AssertCold(controlGame);
                    Console.WriteLine($"Focused response after Ctrl+Enter {focus}: choice={controlChoice}, retained focus=True, " +
                        "added commands=1, same native action=True, cold boundary=True.");
                }
                Program.Assert(controlResponsesPreserved,
                    "When End Turn is unavailable, actual Ctrl+Enter on response confirmation, cancel or hand must preserve the completed draft and every native boundary, never pay or decline it.");

                var controlPlayGame = Load(numberPlayCheckpoint);
                var controlPlayPrompt = model.ChoiceSnapshot.PendingDecision!;
                var controlPlayCard = model.Hand.Single(candidate => candidate.Id == previousTargetAction.CardId);
                var controlPlayTarget = model.Seats.Single(candidate => candidate.Seat == previousTargetAction.TargetSeats.Single());
                model.SelectCardCommand.Execute(controlPlayCard);
                model.SelectTargetCommand.Execute(controlPlayTarget);
                Pump();
                var controlPlayButton = (Button)window.FindName("PlayCardButton");
                controlPlayButton.Focus();
                var controlPlayNative = GameReplay.Restore(numberPlayCheckpoint, registry);
                Program.Assert(model.CanEndTurn && controlPlayCard.IsSelected && controlPlayTarget.IsSelectedTarget &&
                    model.CanConfirmSelected && ReferenceEquals(Keyboard.FocusedElement, controlPlayButton) &&
                    ResponseBoundary(controlPlayGame) == ResponseBoundary(controlPlayNative),
                    "Available Ctrl+Enter must begin at the actual confirmation button above the complete native physical Slash draft.");
                Program.Assert(controlPlayNative.Submit(new EndPlayPhaseCommand(0, controlPlayNative.Revision, controlPlayPrompt.PromptId)).Accepted,
                    "The native end of this exact Play boundary must be accepted independently without paying the selected Slash.");
                var controlPlayRoute = TableShortcutChecks.WithControl(() => RouteFocusedKey(controlPlayButton, Key.Enter, ModifierKeys.Control));
                Program.Assert(controlPlayRoute.PreviewHandled && controlPlayRoute.Bubbles == 0 && controlPlayRoute.Handled &&
                    controlPlayGame.Revision == numberPlayCheckpoint.Revision + 1 &&
                    controlPlayGame.AcceptedCommands.Count == numberPlayCheckpoint.Commands.Count + 1 &&
                    controlPlayGame.AcceptedCommands.Last() is EndPlayPhaseCommand controlEnd && controlEnd.ActorSeat == 0 &&
                    controlEnd.PromptId == controlPlayPrompt.PromptId && controlEnd.ExpectedRevision == numberPlayCheckpoint.Revision &&
                    !model.HasSelection && ResponseBoundary(controlPlayGame) == ResponseBoundary(controlPlayNative),
                    "Available actual Ctrl+Enter must end Play exactly once from the focused confirmation button and never submit its selected Slash instead.");
                AssertCold(controlPlayGame);
                Console.WriteLine($"Focused Play Ctrl+Enter: selected card={controlPlayCard.Id}, target={controlPlayTarget.Seat}, " +
                    "actual command=EndPlayPhaseCommand, added commands=1, same native end=True, cold boundary=True, modifiers restored=True.");

                var controlLogGame = Load(checkpoint);
                var controlLogCard = model.Hand.Single(candidate => candidate.Id == card.Id);
                model.SelectCardCommand.Execute(controlLogCard);
                model.IsLogOpen = true;
                Pump();
                var controlLogClose = (Button)window.FindName("CloseBattleLogButton");
                controlLogClose.Focus();
                Program.Assert(model.IsLogOpen && controlLogCard.IsSelected && model.CanConfirmSelected &&
                    ReferenceEquals(Keyboard.FocusedElement, controlLogClose) && ResponseBoundary(controlLogGame) == boundary,
                    "The real log overlay must own its actual Close button above the untouched native response draft.");
                var controlLogRoute = TableShortcutChecks.WithControl(() => RouteFocusedKey(controlLogClose, Key.Enter, ModifierKeys.Control));
                Pump();
                Program.Assert(!controlLogRoute.PreviewHandled && controlLogRoute.Bubbles == 1 && controlLogRoute.Handled &&
                    !model.IsLogOpen && controlLogCard.IsSelected && model.CanConfirmSelected &&
                    ResponseBoundary(controlLogGame) == boundary,
                    "Ctrl+Enter on the log's own Close action must keep native overlay input ownership and leave the covered response entirely unchanged.");
                AssertCold(controlLogGame);
                Console.WriteLine("Focused log Ctrl+Enter: preview handled=False, bubble=1, closed=True, draft preserved=True, added commands=0, same native boundary=True, cold boundary=True, modifiers restored=True.");

                (bool PreviewHandled, int Bubbles, bool Handled) RouteFocusedKey(Button control, Key pressed, ModifierKeys modifiers = ModifierKeys.None)
                {
                    Program.Assert(control.IsVisible && control.IsEnabled && control.Command!.CanExecute(control.CommandParameter) &&
                        control.IsDescendantOf(window) && ReferenceEquals(Keyboard.FocusedElement, control) &&
                        GetFocus() == handle && IsMessageOnlyWindow(handle) && !IsWindowVisible(handle) && GetForegroundWindow() != handle &&
                        (GetWindowLong(handle, extendedStyle) & noActivate) != 0 && Keyboard.Modifiers == modifiers &&
                        ReferenceEquals(PresentationSource.FromVisual(control), PresentationSource.FromVisual(window)),
                        "Each number/Enter input must start at its actual current visible enabled Button without manually changing focus.");
                    var tunnels = 0;
                    var bubbles = 0;
                    KeyEventHandler observeTunnel = (_, args) => { if (args.Key == pressed) tunnels++; };
                    KeyEventHandler observeKeyDown = (_, args) => { if (args.Key == pressed) bubbles++; };
                    window.AddHandler(Keyboard.PreviewKeyDownEvent, observeTunnel, handledEventsToo: true);
                    control.AddHandler(Keyboard.KeyDownEvent, observeKeyDown, handledEventsToo: true);
                    try
                    {
                        var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(control)!, 0, pressed)
                            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                        control.RaiseEvent(input);
                        var handledInPreview = input.Handled;
                        if (!input.Handled)
                        {
                            input.RoutedEvent = Keyboard.KeyDownEvent;
                            control.RaiseEvent(input);
                        }
                        Program.Assert(tunnels == 1, "The real Window must observe each number/Enter PreviewKeyDown exactly once.");
                        return (handledInPreview, bubbles, input.Handled);
                    }
                    finally
                    {
                        window.RemoveHandler(Keyboard.PreviewKeyDownEvent, observeTunnel);
                        control.RemoveHandler(Keyboard.KeyDownEvent, observeKeyDown);
                    }
                }
            }

            // Each route still loads its own engine from the unchanged Play prefix.
            var playCheckpoint = numberPlayCheckpoint;
            foreach (var focus in new[] { "cancel", "end", "portrait" })
            {
                var currentGame = Load(playCheckpoint);
                var currentPrompt = model.ChoiceSnapshot.PendingDecision!;
                var action = currentGame.GetHumanLegalActions().First(candidate =>
                    candidate.Kind == LegalActionKind.Slash && candidate.CardId is not null && candidate.TargetSeats.Count == 1);
                var currentCard = model.Hand.Single(candidate => candidate.Id == action.CardId);
                var target = model.Seats.Single(candidate => candidate.Seat == action.TargetSeats.Single());
                model.SelectCardCommand.Execute(currentCard);
                model.SelectTargetCommand.Execute(target);
                Pump();
                Program.Assert(currentCard.IsSelected && target.IsSelectedTarget && model.CanConfirmSelected &&
                    ResponseBoundary(currentGame) == numberPlayInitialBoundary,
                    "The same native physical Slash and current target must form a complete reversible Play draft.");
                var control = focus switch
                {
                    "cancel" => (Button)window.FindName("CancelActionButton"),
                    "end" => (Button)window.FindName("EndTurnButton"),
                    _ => Program.Find<Button>(root).Single(candidate => ReferenceEquals(candidate.Command, model.SelectTargetCommand) &&
                        ReferenceEquals(candidate.CommandParameter, target))
                };
                var native = GameReplay.Restore(playCheckpoint, registry);
                if (focus == "end")
                    Program.Assert(native.Submit(new EndPlayPhaseCommand(0, native.Revision, currentPrompt.PromptId)).Accepted,
                        "The independent native EndPlayPhase must accept the current Play boundary.");
                else if (focus == "portrait")
                    Program.Assert(native.Submit(new PlayCardCommand(0, currentCard.Id, action.TargetSeats,
                        native.Revision, currentPrompt.PromptId, action.PlayedCardKind, action.TargetCardId)
                        { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources }).Accepted,
                        "The independent native selected Slash must accept its current target.");
                var route = EnterOn(control);
                Program.Assert(route.PreviewHandled == (focus == "portrait") && route.Handled &&
                    ResponseBoundary(currentGame) == ResponseBoundary(native) && !model.HasSelection,
                    "Focused Play Cancel must clear only the draft, End must end the phase, and portrait Enter must confirm the native Slash.");
                Program.Assert(currentGame.AcceptedCommands.Count == playCheckpoint.Commands.Count + (focus == "cancel" ? 0 : 1) &&
                    (focus == "cancel" ? !currentCard.IsSelected && !target.IsSelectedTarget :
                     focus == "end" ? currentGame.AcceptedCommands.Last() is EndPlayPhaseCommand :
                     currentGame.AcceptedCommands.Last() is PlayCardCommand playedCommand && playedCommand.CardId == currentCard.Id &&
                         playedCommand.TargetSeats.SequenceEqual(action.TargetSeats)),
                    "A focused Play action must retain its exact command kind and never substitute another completed draft operation.");
                AssertCold(currentGame);
                Console.WriteLine($"Focused Play {focus} Enter: preview handled={route.PreviewHandled}, " +
                    $"added commands={currentGame.AcceptedCommands.Count - playCheckpoint.Commands.Count}, same native action=True.");
            }
            Console.WriteLine($"Focused Enter baseline reuse: actual loads={loadCount}, independent baselines={expectedLoadBoundaries.Count}.");

            GameEngine Load(GameCheckpoint saved)
            {
                loadCount++;
                store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, saved));
                model.LoadManualGameCommand.Execute(null);
                Pump();
                var current = Program.Engine(model);
                Program.Assert(!model.HasSaveError && ResponseBoundary(current) == ExpectedLoadBoundary(saved),
                    "Each actual keyboard route must load its own unchanged native boundary in the same owned window.");
                return current;
            }
            string ExpectedLoadBoundary(GameCheckpoint saved)
            {
                if (!expectedLoadBoundaries.TryGetValue(saved, out var expected))
                {
                    expected = ResponseBoundary(GameReplay.Restore(saved, registry));
                    expectedLoadBoundaries.Add(saved, expected);
                }
                return expected;
            }
            (bool PreviewHandled, bool Handled) EnterOn(Button control)
            {
                control.Focus();
                Program.Assert(control.IsVisible && control.IsEnabled && control.Command!.CanExecute(control.CommandParameter) &&
                    ReferenceEquals(Keyboard.FocusedElement, control) && GetFocus() == handle &&
                    IsMessageOnlyWindow(handle) && !IsWindowVisible(handle) && GetForegroundWindow() != handle &&
                    (GetWindowLong(handle, extendedStyle) & noActivate) != 0 && Keyboard.Modifiers == ModifierKeys.None &&
                    ReferenceEquals(PresentationSource.FromVisual(control), PresentationSource.FromVisual(window)),
                    "Each live control must own exact keyboard focus in the same hidden native source without taking foreground.");
                var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(control)!, 0, Key.Enter)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                control.RaiseEvent(input);
                var handledInPreview = input.Handled;
                if (!input.Handled)
                {
                    input.RoutedEvent = Keyboard.KeyDownEvent;
                    control.RaiseEvent(input);
                }
                return (handledInPreview, input.Handled);
            }
            void AssertCold(GameEngine current)
            {
                var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(current.CreateCheckpoint())), registry);
                Program.Assert(ResponseBoundary(restored) == ResponseBoundary(current),
                    "Every actual focused Enter route must cold-restore all four safe views and complete typed journals.");
            }

            void Pump()
            {
                root.Measure(new Size(1120, 820)); root.Arrange(new Rect(0, 0, 1120, 820)); root.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                root.UpdateLayout();
            }
        }
        finally
        {
            window.RemoveHandler(Keyboard.PreviewKeyDownEvent, observePreview);
            cancel.RemoveHandler(Keyboard.KeyDownEvent, observeBubble);
            window.Content = null;
            window.Close();
        }
    }

    private static string ResponseBoundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, Commands = CommandJson.Serialize(game.AcceptedCommands), Movements = game.CardMovements,
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        Views = Enumerable.Range(0, 4).Select(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer))).ToArray()
    });

    private static bool IsMessageOnlyWindow(nint handle)
    {
        var cursor = nint.Zero;
        for (var count = 0; count < 4096; count++)
        {
            cursor = FindWindowEx(new nint(-3), cursor, null, null);
            if (cursor == handle) return true;
            if (cursor == 0) return false;
        }
        return false;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern nint GetFocus();
    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint handle);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint handle, int index, int value);

    private static void VerifyExtraChoices(MainViewModel vm)
    {
        vm.LoadManualGameCommand.Execute(null);
        var snapshot = Program.Engine(vm).CreateSnapshot(0);
        var prompt = snapshot.PendingDecision!;
        var native = prompt.Choices.First(choice => choice.Cards.Count == 1);
        var hand = snapshot.Players.Single(player => player.IsHuman).Hand;
        // Presentation-only published variants cover skill/equipment choices and ambiguous
        // one-card effects. Core commands remain untouched throughout this UI projection check.
        var ambiguous = native with { Id = new("fixture:alternate-response"), Description = "另一种响应效果" };
        var equipment = new PromptChoice(new("fixture:equipment-response"), "发动装备响应", [], [],
            new Dictionary<string, string> { ["response"] = "bagua" });
        var pair = new PromptChoice(new("fixture:two-card-response"), "两张手牌响应", hand.Take(2).Select(card => card.Id).ToArray(), [],
            new Dictionary<string, string> { ["response"] = "zhangba-slash" });
        var refresh = typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            refresh.Invoke(vm, [snapshot with { PendingDecision = prompt with { Choices = prompt.Choices.Concat([ambiguous, equipment, pair]).ToArray() } }]);
            Program.Assert(vm.HandResponseExtraChoices.Select(choice => choice.Id).ToHashSet()
                    .SetEquals([native.Id, ambiguous.Id, equipment.Id, pair.Id]) &&
                !vm.Hand.Single(card => card.Id == native.Cards[0]).IsPlayable &&
                vm.HandResponseExtraChoices.All(choice => vm.SelectHandResponseChoiceCommand.CanExecute(choice)),
                "Distinct effects, equipment, and multi-card responses must retain their exact central choices.");
            Program.Assert(vm.HandResponseExtraChoices.All(choice => !vm.SelectHandResponseChoiceCommand.CanExecute(choice with { })),
                "Copied extra response choices must not be enabled merely because their IDs match current choices.");
            var replacementPair = pair with { };
            refresh.Invoke(vm, [snapshot with { PendingDecision = prompt with { Choices = prompt.Choices.Concat([ambiguous, equipment, replacementPair]).ToArray() } }]);
            Program.Assert(!vm.SelectHandResponseChoiceCommand.CanExecute(pair) && vm.SelectHandResponseChoiceCommand.CanExecute(replacementPair),
                "Replacing an extra response choice must disable the retired object and enable the exact current object.");
        }
        finally { refresh.Invoke(vm, [snapshot]); }
    }

    private static bool Shortcut(MainWindow window, Key key) => (bool)typeof(MainWindow)
        .GetMethod("HandleShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [key, ModifierKeys.None])!;
}
