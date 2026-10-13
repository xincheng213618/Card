using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
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
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class StoredGrainResponseUiChecks
{
    public static void Run()
    {
        foreach (var branch in new[] { "Slash", "Dodge", "Taoluan" })
        {
            var (model, registry, grainId) = Create(branch);
            using (model)
            {
                var window = new MainWindow(model);
                var root = (FrameworkElement)window.Content;
                try
                {
                    Layout(root);
                    var retiredCard = model.Hand.Single(card => card.Id == grainId);
                    var retiredButton = CardButton(root, model, retiredCard);
                    var retiredChoice = Choice(Program.Engine(model), grainId);
                    Console.WriteLine($"Stored {branch}: card={grainId}, native choice={retiredChoice.Id}, " +
                        $"grain={retiredCard.IsStoredGrain}, playable={retiredCard.IsPlayable}, button={retiredButton.IsEnabled}, " +
                        $"extra={model.HandResponseExtraChoices.Count}, commands={Program.Engine(model).AcceptedCommands.Count}.");
                    Program.Assert(retiredCard.IsStoredGrain && retiredCard.IsPlayable && retiredButton.IsEnabled &&
                        !model.HandResponseExtraChoices.Any(choice => choice.Cards.SequenceEqual([grainId])),
                        $"{branch}: the unique native grain response must be selectable in its actual bottom card control, without a duplicate center choice.");
                    if (branch == "Taoluan") VerifyNativeExtraFocusAndSubmission(window, root, model, registry);

                    foreach (var route in new[] { "button", "Enter" })
                    {
                        model.LoadManualGameCommand.Execute(null);
                        var game = Program.Engine(model);
                        var prompt = game.CreateSnapshot(0).PendingDecision!;
                        var choice = Choice(game, grainId);
                        var card = model.Hand.Single(item => item.Id == grainId);
                        Program.Assert(!model.HasSaveError && !ReferenceEquals(card, retiredCard) && !ReferenceEquals(choice, retiredChoice),
                            "Manual reload must replace both the visible grain card and its exact published response choice.");
                        var before = game.CreateCheckpoint();
                        var movements = game.CardMovements.Count;
                        var events = game.Events.Count;
                        var unchanged = Boundary(game);
                        Layout(root);
                        Click(CardButton(root, model, card));
                        AssertSelected(model, root, grainId, choice, branch);
                        var draft = Draft(model);
                        retiredButton.Command!.Execute(retiredButton.CommandParameter);
                        model.SelectCardCommand.Execute(new CardViewModel
                            { Id = card.Id, Kind = card.Kind, Name = card.Name, IsPlayable = true, KindLabel = card.KindLabel,
                                SuitGlyph = card.SuitGlyph, Rank = card.Rank, Description = card.Description });
                        model.SelectHandResponseChoiceCommand.Execute(retiredChoice);
                        model.SelectHandResponseChoiceCommand.Execute(choice with { });
                        Program.Assert(Boundary(game) == unchanged && Draft(model) == draft,
                            "Retired bottom-card/choice parameters and equal copies must neither consume grain nor change its prepared response.");
                        Program.Assert(TableShortcutChecks.Input(window, Key.Escape) && !model.HasSelection &&
                            !card.IsSelected && !model.CanConfirmSelected && Boundary(game) == unchanged,
                            "Esc must clear only the grain draft, retaining its exact prompt, stored entity and all four safe views.");
                        var index = model.Hand.IndexOf(card);
                        Program.Assert(index is >= 0 and < 9 && TableShortcutChecks.Input(window, Key.D1 + index),
                            "The visible grain must remain selectable through its real hand-number shortcut.");
                        AssertSelected(model, root, grainId, choice, branch);
                        Program.Assert(Boundary(game) == unchanged,
                            "Both bottom-card and number selection must prepare the current grain response without a native command or payment.");
                        var reference = GameReplay.Restore(before, registry);
                        Accept(reference, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, reference.Revision));
                        var confirm = (Button)window.FindName("PlayCardButton");
                        if (route == "button") Click(confirm);
                        else Program.Assert(TableShortcutChecks.Input(window, Key.Enter),
                            "Enter must submit the currently selected stored-grain response.");
                        VerifyAnswer(game, reference, registry, before.Revision, before.Commands.Count, prompt.PromptId, choice.Id);
                        var addedMoves = game.CardMovements.Skip(movements).ToArray();
                        Program.Assert(addedMoves.Count(move => move.CardId == grainId &&
                                move.From == CardLocation.WoodenOxGrain(0) && move.To == CardLocation.Processing &&
                                move.Reason == CardMoveReasons.Respond) == 1 &&
                            addedMoves.Count(move => move.CardId == grainId && move.From == CardLocation.Processing &&
                                move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.ResponseFinished) == 1 &&
                            game.Events.Skip(events).Select(item => item.Payload).OfType<CardRespondedEvent>()
                                .Count(item => item.CardId == grainId && item.ResponderSeat == 0 &&
                                    item.EffectiveCardKind == (branch == "Slash" ? CardKind.Slash : CardKind.Dodge)) == 1 &&
                            model.Hand.All(item => item.Id != grainId) && !model.HasSelection,
                            $"{branch}/{route}: the exact native response must pay only its real stored entity once and remove its bottom control.");
                        var accepted = Boundary(game);
                        model.ConfirmSelectedCommand.Execute(null);
                        model.SelectHandResponseChoiceCommand.Execute(choice);
                        Program.Assert(Boundary(game) == accepted,
                            "A completed stored-grain response must not be reused by its old confirmation or choice.");
                    }

                    model.LoadManualGameCommand.Execute(null);
                    var cancelling = Program.Engine(model);
                    var pending = cancelling.CreateSnapshot(0).PendingDecision!;
                    var decline = pending.Choices.Single(choice => choice.Cards.Count == 0 &&
                        choice.Parameters.GetValueOrDefault("response") == "take-damage");
                    var cancelBefore = cancelling.CreateCheckpoint();
                    var cancelMoves = cancelling.CardMovements.Count;
                    var cancelReference = GameReplay.Restore(cancelBefore, registry);
                    Accept(cancelReference, new AnswerPromptCommand(0, pending.PromptId, decline.Id, cancelReference.Revision));
                    Layout(root);
                    Click(CardButton(root, model, model.Hand.Single(card => card.Id == grainId)));
                    Layout(root);
                    Click((Button)window.FindName("CancelActionButton"));
                    VerifyAnswer(cancelling, cancelReference, registry, cancelBefore.Revision, cancelBefore.Commands.Count, pending.PromptId, decline.Id);
                    Program.Assert(cancelling.CreateSnapshot(0).Players[0].WoodenOxGrain!.Single().Id == grainId &&
                        !cancelling.CardMovements.Skip(cancelMoves).Any(move => move.CardId == grainId) &&
                        model.Hand.Single(card => card.Id == grainId).IsStoredGrain && !model.HasSelection,
                        "The actual bottom Cancel button must decline the native response while preserving the selected grain entity and its visible zone marker.");
                }
                finally { window.Content = null; window.Close(); }
            }
        }
    }

    private static void VerifyNativeExtraFocusAndSubmission(MainWindow window, FrameworkElement root,
        MainViewModel model, ContentRegistry registry)
    {
        var game = Program.Engine(model);
        var choices = model.HandResponseExtraChoices.ToArray();
        var choice = choices.Single(candidate => candidate.Cards.SequenceEqual([45]) &&
            candidate.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:taoluan");
        Program.Assert(game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.RespondDodge, IncomingCard: CardKind.Slash } &&
            model.ChoiceSnapshot.Players[0].Equipment.Any(card => card.Id == 45 && card.Kind == CardKind.WoodenOx) &&
            model.ChoiceSnapshot.PendingDecision!.Choices.Any(candidate => ReferenceEquals(candidate, choice)),
            "The focus fixture must use the current native Taoluan equipment response, rather than a synthetic extra choice.");
        var unchanged = Boundary(game);
        var draft = Draft(model);
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        window.Top = -30000;
        window.Width = 1120;
        window.Height = 820;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        const int extendedStyle = -20;
        const int noActivate = 0x08000000;
        SetWindowLong(handle, extendedStyle, GetWindowLong(handle, extendedStyle) | noActivate);
        var previousParent = SetParent(handle, new nint(-3));
        var parentError = Marshal.GetLastPInvokeError();
        Program.Assert((previousParent != 0 || parentError == 0) && IsMessageOnlyWindow(handle),
            $"The extra-response fixture must own a message-only HWND: parent={previousParent}, error={parentError}.");
        window.Show();
        ShowWindow(handle, 0);
        SetFocus(handle);
        Pump();
        Program.Assert(window.IsLoaded && window.IsVisible && !IsWindowVisible(handle) && GetForegroundWindow() != handle,
            $"The extra-response fixture must load its own hidden Window: loaded={window.IsLoaded}, WPF visible={window.IsVisible}, " +
            $"native visible={IsWindowVisible(handle)}, owns foreground={GetForegroundWindow() == handle}.");
        AssertHost();
        Program.Assert(Boundary(game) == unchanged && Draft(model) == draft,
            "Loading the actual extra-response surface must preserve the native prompt and prepared UI boundary.");
        var button = ExtraButton(choice);
        button.Focus();
        Program.Assert(button.IsVisible && button.IsEnabled && ReferenceEquals(Keyboard.FocusedElement, button),
            "The fixture must focus the actual enabled native equipment-response button before changing speed.");
        AssertHost();
        foreach (var speed in new[] { PlaybackSpeed.Fast, PlaybackSpeed.Find("slow")!, PlaybackSpeed.Normal })
        {
            model.SelectedPlaybackSpeed = speed;
            Pump();
            var speedButton = ExtraButton(choice);
            Console.WriteLine($"Taoluan extra speed={speed.Id}: choice45={choice.Id}, extras={model.HandResponseExtraChoices.Count}, " +
                $"same button={ReferenceEquals(button, speedButton)}, exact WPF focus={ReferenceEquals(Keyboard.FocusedElement, button)}, " +
                $"native focus={GetFocus()}, native hidden={!IsWindowVisible(handle)}, owns foreground={GetForegroundWindow() == handle}.");
            AssertHost();
            Program.Assert(ReferenceEquals(Program.Engine(model), game) && Boundary(game) == unchanged && Draft(model) == draft &&
                model.HandResponseExtraChoices.SequenceEqual(choices, ReferenceEqualityComparer.Instance),
                "Changing playback speed must preserve every exact native extra choice, draft and player-safe command boundary.");
            Program.Assert(ReferenceEquals(button, speedButton) && ReferenceEquals(Keyboard.FocusedElement, button) &&
                button.IsVisible && button.IsEnabled,
                $"Changing playback speed to {speed.Id} must retain the actual focused native equipment-response button.");
        }

        model.OpenSettingsCommand.Execute(null);
        Pump();
        Program.Assert(model.IsSettingsOpen && Keyboard.FocusedElement is UIElement settingsFocus &&
            settingsFocus.IsDescendantOf((DependencyObject)window.FindName("SettingsOverlay")),
            "The actual settings overlay must receive focus before changing its bound speed selector.");
        var speedSelector = (ListBox)window.FindName("SettingsPlaybackSpeedSelector");
        speedSelector.SelectedItem = PlaybackSpeed.Fast;
        Pump();
        Program.Assert(model.SelectedPlaybackSpeed == PlaybackSpeed.Fast &&
            ReferenceEquals(ExtraButton(choice), button) && Boundary(game) == unchanged && Draft(model) == draft,
            "The real settings speed selector must retain the underlying native response button and prompt.");
        Click((Button)window.FindName("CloseSettingsButton"));
        Pump();
        Program.Assert(!model.IsSettingsOpen && ReferenceEquals(Keyboard.FocusedElement, button),
            "Closing settings after a speed change must restore the exact still-live equipment-response button.");
        model.OpenContextGuideCommand.Execute(null);
        Pump();
        Program.Assert(model.IsHelpOpen && Keyboard.FocusedElement is UIElement guideFocus &&
            guideFocus.IsDescendantOf((DependencyObject)window.FindName("PlayerGuideOverlay")),
            "The actual contextual guide must receive focus above the pending native response.");
        Program.Assert(TableShortcutChecks.Input(window, Key.Escape), "Esc must close the current guide overlay.");
        Pump();
        AssertHost();
        Program.Assert(!model.IsHelpOpen && ReferenceEquals(ExtraButton(choice), button) &&
            ReferenceEquals(Keyboard.FocusedElement, button) && Boundary(game) == unchanged && Draft(model) == draft,
            "Returning from the guide must preserve the exact extra-response button, its focus and every native boundary.");

        var before = game.CreateCheckpoint();
        var moves = game.CardMovements.Count;
        var eventCount = game.Events.Count;
        var prompt = model.ChoiceSnapshot.PendingDecision!;
        var reference = GameReplay.Restore(before, registry);
        Accept(reference, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, reference.Revision));
        var retiredCommand = button.Command!;
        var retiredParameter = button.CommandParameter;
        Click(button);
        VerifyAnswer(game, reference, registry, before.Revision, before.Commands.Count, prompt.PromptId, choice.Id);
        Program.Assert(game.CardMovements.Skip(moves).Count(move => move.CardId == 45 &&
                move.From == CardLocation.Equipment(0) && move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Respond) == 1 &&
            game.CardMovements.Skip(moves).Count(move => move.CardId == 45 && move.From == CardLocation.Processing &&
                move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.ResponseFinished) == 1 &&
            game.Events.Skip(eventCount).Select(item => item.Payload).OfType<CardRespondedEvent>()
                .Count(item => item.CardId == 45 && item.ResponderSeat == 0 && item.EffectiveCardKind == CardKind.Dodge) == 1,
            "The focused equipment extra must submit its exact native converted response and pay the equipped entity once.");
        model.LoadManualGameCommand.Execute(null);
        Pump();
        var reloaded = Program.Engine(model);
        var currentChoice = model.HandResponseExtraChoices.Single(candidate => candidate.Cards.SequenceEqual([45]));
        var currentButton = ExtraButton(currentChoice);
        var restored = Boundary(reloaded);
        var restoredDraft = Draft(model);
        Program.Assert(!model.HasSaveError && !ReferenceEquals(reloaded, game) && !ReferenceEquals(currentChoice, choice) &&
            restored == unchanged,
            "Manual load must restore the unconsumed native prompt with a newly published equipment choice.");
        Program.Assert(currentButton.IsEnabled && !ReferenceEquals(currentButton, button) &&
            !Program.Find<Button>((ItemsControl)window.FindName("HandResponseExtraChoices")).Contains(button),
            "The restored center must bind a fresh live button and retire the old equipment-response control.");
        retiredCommand.Execute(retiredParameter);
        model.SelectHandResponseChoiceCommand.Execute(currentChoice with { });
        Program.Assert(Boundary(reloaded) == restored && Draft(model) == restoredDraft,
            "The old actual equipment button parameter and an equal copy must not consume or change the restored response.");
        var liveRetiredGame = reloaded;
        var liveRetiredChoice = currentChoice;
        var liveRetiredButton = currentButton;
        var liveRetiredCommand = currentButton.Command!;
        var liveRetiredParameter = currentButton.CommandParameter;
        model.LoadManualGameCommand.Execute(null);
        Pump();
        reloaded = Program.Engine(model);
        currentChoice = model.HandResponseExtraChoices.Single(candidate => candidate.Cards.SequenceEqual([45]));
        currentButton = ExtraButton(currentChoice);
        Program.Assert(!model.HasSaveError && !ReferenceEquals(reloaded, liveRetiredGame) &&
            currentChoice.Id == liveRetiredChoice.Id && !ReferenceEquals(currentChoice, liveRetiredChoice) &&
            !ReferenceEquals(currentButton, liveRetiredButton) && currentButton.IsEnabled &&
            !Program.Find<Button>((ItemsControl)window.FindName("HandResponseExtraChoices")).Contains(liveRetiredButton) &&
            Boundary(reloaded) == restored && Draft(model) == restoredDraft,
            "Loading the same checkpoint must retire its still-live center button even when the native choice ID is unchanged.");
        liveRetiredCommand.Execute(liveRetiredParameter);
        model.SelectHandResponseChoiceCommand.Execute(currentChoice with { });
        Program.Assert(Boundary(reloaded) == restored && Draft(model) == restoredDraft,
            "The retired live response button and an equal fresh-choice copy must not change the reloaded native boundary.");
        var reloadBefore = reloaded.CreateCheckpoint();
        var reloadPrompt = model.ChoiceSnapshot.PendingDecision!;
        var reloadReference = GameReplay.Restore(reloadBefore, registry);
        Accept(reloadReference, new AnswerPromptCommand(0, reloadPrompt.PromptId, currentChoice.Id, reloadReference.Revision));
        Click(currentButton);
        VerifyAnswer(reloaded, reloadReference, registry, reloadBefore.Revision, reloadBefore.Commands.Count,
            reloadPrompt.PromptId, currentChoice.Id);

        Button ExtraButton(PromptChoice parameter) => Program.Find<Button>((ItemsControl)window.FindName("HandResponseExtraChoices"))
            .Single(candidate => ReferenceEquals(candidate.Command, model.SelectHandResponseChoiceCommand) &&
                ReferenceEquals(candidate.CommandParameter, parameter));
        void Pump()
        {
            Layout(root);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            root.UpdateLayout();
        }
        void AssertHost() => Program.Assert(IsMessageOnlyWindow(handle) && GetFocus() == handle &&
            !IsWindowVisible(handle) && GetForegroundWindow() != handle && (GetWindowLong(handle, extendedStyle) & noActivate) != 0,
            "Actual response focus must belong to this native-hidden message-only source without taking desktop foreground.");
    }

    private static bool IsMessageOnlyWindow(nint handle)
    {
        nint previous = 0;
        for (var count = 0; count < 4096; count++)
        {
            previous = FindWindowEx(new nint(-3), previous, null, null);
            if (previous == handle) return true;
            if (previous == 0) return false;
        }
        return false;
    }

    private static void AssertSelected(MainViewModel model, FrameworkElement root, int id, PromptChoice choice, string branch)
    {
        Layout(root);
        var confirm = Program.Find<Button>(root).Single(button => button.Name == "PlayCardButton");
        var expected = branch == "Taoluan" ? "当作闪打出" : branch == "Dodge" ? "打出闪" : "打出杀";
        Program.Assert(model.Hand.Where(card => card.IsSelected).Select(card => card.Id).SequenceEqual([id]) &&
            model.CanConfirmSelected && confirm.IsEnabled && confirm.Visibility == Visibility.Visible &&
            Equals(confirm.Content, expected) && model.ActionHint.Contains(choice.Description, StringComparison.Ordinal) &&
            !model.HandResponseExtraChoices.Any(candidate => candidate.Cards.SequenceEqual([id])),
            $"{branch}: the actual confirmation must name the effective native response and keep one exact visible grain draft.");
        var human = model.ChoiceSnapshot.Players.Single(player => player.IsHuman);
        var visibleIds = human.Hand.Concat(human.WoodenOxGrain ?? []).Select(card => card.Id).ToHashSet();
        var choices = model.ChoiceSnapshot.PendingDecision!.Choices;
        var ambiguousIds = choices.Where(candidate => candidate.Cards.Count == 1).GroupBy(candidate => candidate.Cards[0])
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        var alternatives = choices.Where(candidate => candidate.Cards.Count > 1 || candidate.Cards.Count == 1 &&
            (!visibleIds.Contains(candidate.Cards[0]) || ambiguousIds.Contains(candidate.Cards[0]))).ToArray();
        Program.Assert(alternatives.All(candidate => model.HandResponseExtraChoices.Any(current => ReferenceEquals(current, candidate))) &&
            (branch != "Taoluan" || alternatives.Any(candidate => candidate.Cards.SequenceEqual([45]))),
            "Migrating a unique grain choice must retain every current equipment, multi-card and ambiguous native alternative in the center.");
        foreach (var alternative in alternatives)
            Program.Assert(Program.Find<Button>(root).Single(button =>
                    ReferenceEquals(button.Command, model.SelectHandResponseChoiceCommand) && ReferenceEquals(button.CommandParameter, alternative)).IsEnabled,
                "Each remaining native alternative must retain its actual enabled center button.");
    }

    private static void VerifyAnswer(GameEngine game, GameEngine reference, ContentRegistry registry,
        long revision, int commands, PromptId prompt, ChoiceId choice)
    {
        Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Count == commands + 1 &&
            game.AcceptedCommands.Last() is AnswerPromptCommand submitted && submitted.ActorSeat == 0 &&
            submitted.ExpectedRevision == revision && submitted.PromptId == prompt && submitted.Choice == choice &&
            Views(game).SequenceEqual(Views(reference)) && game.CardMovements.SequenceEqual(reference.CardMovements) &&
            CommandJson.Serialize(game.AcceptedCommands) == CommandJson.Serialize(reference.AcceptedCommands),
            "The actual response button/key must submit one exact current AnswerPrompt, matching independent native execution and all four player-safe views.");
        var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Program.Assert(Views(game).SequenceEqual(Views(cold)) && game.CardMovements.SequenceEqual(cold.CardMovements) &&
            CommandJson.Serialize(game.AcceptedCommands) == CommandJson.Serialize(cold.AcceptedCommands),
            "Stored-grain response and decline boundaries must cold-restore all four safe views, physical movements and exact native commands.");
    }

    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, Commands = CommandJson.Serialize(game.AcceptedCommands),
        Events = game.Events.Count, Movements = game.CardMovements, Views = Views(game)
    });

    private static string Draft(MainViewModel model) => JsonSerializer.Serialize(new
    {
        Selected = model.Hand.Where(card => card.IsSelected).Select(card => card.Id).ToArray(),
        model.SelectedCardText, model.PromptText, model.ActionHint, model.HasSelection, model.CanConfirmSelected
    });

    private static string[] Views(GameEngine game) => Enumerable.Range(0, 4)
        .Select(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer))).ToArray();

    private static PromptChoice Choice(GameEngine game, int grainId) => game.CreateSnapshot(0).PendingDecision!.Choices
        .Single(choice => choice.Cards.SequenceEqual([grainId]));

    private static Button CardButton(FrameworkElement root, MainViewModel model, CardViewModel card) =>
        Program.Find<Button>(root).Single(button => button.Name == "CardButton" &&
            ReferenceEquals(button.Command, model.SelectCardCommand) && ReferenceEquals(button.CommandParameter, card));

    private static void Click(Button button)
    {
        Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible && button.Command!.CanExecute(button.CommandParameter),
            "Stored-grain input must use its actual enabled current bound button.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }

    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1120, 740));
        root.Arrange(new Rect(0, 0, 1120, 740));
        root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        root.UpdateLayout();
    }

    private static (MainViewModel, ContentRegistry, int) Create(string branch)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(branch));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Fixture.Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId));
        Reach(game, DecisionKind.PlayCard);
        Program.Assert(game.CreateSnapshot(0).Players[0].Hand.Single(card => card.Id == 45).Kind == CardKind.WoodenOx,
            "The verified seed31/Card45 slot must deal the native Wooden Ox to Human0 before real equipment use.");
        var equipment = game.GetHumanLegalActions().Single(action => action.CardId == 45 && action.ConversionSource is null);
        var equipMoves = game.CardMovements.Count;
        Accept(game, new PlayCardCommand(0, 45, equipment.TargetSeats, game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId,
            equipment.PlayedCardKind, equipment.TargetCardId));
        Reach(game, DecisionKind.PlayCard);
        Program.Assert(game.CreateSnapshot(0).Players[0].Equipment.Single(card => card.Kind == CardKind.WoodenOx).Id == 45 &&
            game.CardMovements.Skip(equipMoves).Count(move => move.CardId == 45 && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 &&
            game.CardMovements.Skip(equipMoves).Count(move => move.CardId == 45 && move.From == CardLocation.Processing && move.To == CardLocation.Equipment(0)) == 1,
            "Real Wooden Ox equipment must pay the exact dealt entity through its two native movement stages.");
        var storage = game.GetHumanLegalActions().Single(action => action.EquipmentKind == CardKind.WoodenOx);
        var physicalKind = branch == "Dodge" ? CardKind.Dodge : CardKind.Slash;
        var grain = game.CreateSnapshot(0).Players[0].Hand.First(card => card.Kind == physicalKind && storage.SelectableCardIds.Contains(card.Id));
        var storeMoves = game.CardMovements.Count;
        Accept(game, new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain.Id], [], game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId));
        Reach(game, DecisionKind.PlayCard);
        Program.Assert(game.CardMovements.Skip(storeMoves).Count(move => move.CardId == grain.Id &&
            move.From == CardLocation.Hand(0) && move.To == CardLocation.WoodenOxGrain(0) && move.Reason == CardMoveReasons.WoodenOxStore) == 1,
            "The actual native storage action must deposit one existing physical hand material without transferring its Wooden Ox.");
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId));
        var kind = branch == "Slash" ? DecisionKind.RespondSlash : DecisionKind.RespondDodge;
        Reach(game, kind);
        var pending = game.CreateSnapshot(0).PendingDecision!;
        var nativeChoice = Choice(game, grain.Id);
        var incomingKind = branch == "Slash" ? CardKind.BarbarianAssault : branch == "Dodge" ? CardKind.ArrowBarrage : CardKind.Slash;
        var incoming = game.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.SourceSeat == 1 && frame.CardKind == incomingKind);
        Program.Assert(pending.PlayerSeat == 0 && pending.Kind == kind && pending.IncomingCard == incomingKind &&
            pending.ValidCardIds.Contains(grain.Id) && game.CreateSnapshot(0).Players[0].Hand.All(card => card.Id != grain.Id) &&
            game.CreateSnapshot(0).Players[0].WoodenOxGrain!.Single().Id == grain.Id &&
            incoming.Action is { ActorSeat: 1, ProviderSeat: 1, PhysicalCards.Count: 1 } action && action.PhysicalCards[0].CardId == incoming.CardId &&
            game.CardMovements.Any(move => move.CardId == incoming.CardId && move.From == CardLocation.Hand(1) && move.To == CardLocation.Processing) &&
            game.CreateCardZoneDiagnostics().Single(card => card.CardId == grain.Id).Location == CardLocation.WoodenOxGrain(0) &&
            (branch == "Taoluan" ? nativeChoice.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:taoluan" &&
                nativeChoice.Parameters.GetValueOrDefault("conversion-binding-id") == "use-dodge"
                : !nativeChoice.Parameters.ContainsKey("conversion-skill-id")) &&
            (branch != "Taoluan" || pending.Choices.Any(choice => choice.Cards.SequenceEqual([45]) &&
                choice.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:taoluan")) &&
            Enumerable.Range(1, 3).All(viewer => (game.CreateSnapshot(viewer).Players[0].WoodenOxGrain ?? []).Count == 0),
            "Each branch must reach its actual native AI1 incoming use with one published grain response of the exact current physical/conversion origin, keeping grain private.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, 31, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        try
        {
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError && Views(Program.Engine(model)).SequenceEqual(Views(game)),
                "Actual manual load must restore the prepared native grain response and every player-safe view.");
            return (model, registry, grain.Id);
        }
        catch { model.Dispose(); throw; }
    }

    private static void Reach(GameEngine game, DecisionKind kind)
    {
        for (var step = 0; step < 64; step++)
        {
            var prompt = game.CreateSnapshot(0).PendingDecision;
            if (prompt is { PlayerSeat: 0 } && prompt.Kind == kind) return;
            if (prompt is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards })
                Accept(game, new DiscardCardsCommand(0, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision));
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException($"Stored-grain {kind} fixture did not reach its native human boundary; actual={game.CreateSnapshot(0).PendingDecision?.Kind}.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "A native stored-grain fixture command was rejected.");
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
    private static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint handle, int index, int value);

    private sealed class Fixture(string branch) : IGameContentPackage
    {
        internal const string Mode = "identity:classic-stored-grain-response";
        internal const string Owner = "fixture:stored-response-owner";
        public PackageManifest Manifest { get; } = new("fixture-stored-grain-response", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new(Owner, "固定粮牌响应者", "supporter", branch == "Taoluan" ? "classic:taoluan" : "standard:none", "qun", 4));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:stored-response-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "固定原生行动者", "supporter", "standard:none", "wei", 4));
            builder.AddDeck(new("fixture:stored-response-deck", "已验证存粮响应牌库", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard(index + 1 == 45 ? "classic:wooden-ox" :
                    new[] { 21, 37, 61, 80 }.Contains(index + 1) ? branch == "Dodge" ? "standard:arrow_barrage" :
                        branch == "Slash" ? "standard:barbarian_assault" : "standard:slash" :
                    branch == "Dodge" ? "standard:dodge" : "standard:slash", Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "原生粮牌响应", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:stored-response-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
