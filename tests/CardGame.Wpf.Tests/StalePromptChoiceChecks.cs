using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Content.Standard;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class StalePromptChoiceChecks
{
    public static void ReloadedChoices(string output)
    {
        ReloadedPlayChoice(output);
        PublicTargetChoice(output);
        foreach (var kind in new[] { DecisionKind.RespondDodge, DecisionKind.RespondSlash,
                     DecisionKind.RescueDying, DecisionKind.Nullification, DecisionKind.FireAttackReveal,
                     DecisionKind.FireAttackDiscard })
        {
            var (model, _) = StableHandResponseFixture.Create(kind, output);
            using (model)
            {
                var stale = Choices(model, kind).First(choice => choice.Cards.Count == 1);
                var retiredCard = model.Hand.First(card => card.IsPlayable);
                model.LoadManualGameCommand.Execute(null);
                var current = Choices(model, kind).Single(choice => choice.Id == stale.Id);
                Program.Assert(!ReferenceEquals(stale, current), "Reload must replace the published choice objects.");
                var engine = Program.Engine(model);
                var revision = engine.Revision;
                var promptId = engine.PendingDecision!.PromptId;
                var movements = engine.CardMovements.Count;
                var state = SnapshotJson.Serialize(engine.CreateSnapshot(0, true));
                var playerState = SnapshotJson.Serialize(engine.CreateSnapshot(0));
                var currentCard = model.Hand.Single(card => card.Id == retiredCard.Id);
                Program.Assert(!ReferenceEquals(retiredCard, currentCard), "Reload must replace hand view models.");
                model.SelectCardCommand.Execute(retiredCard);
                Program.Assert(!model.HasSelection && !currentCard.IsSelected && engine.Revision == revision &&
                               SnapshotJson.Serialize(engine.CreateSnapshot(0)) == playerState,
                    $"{kind}: a retired hand parameter selected the current response card by its ID.");
                model.SelectCardCommand.Execute(currentCard);
                Program.Assert(currentCard.IsSelected && model.CanConfirmSelected && engine.Revision == revision,
                    $"{kind}: the current hand parameter must still prepare its exact response.");
                var promptText = model.PromptText;
                var commandCount = engine.AcceptedCommands.Count;
                foreach (var (route, active) in new[]
                {
                    (model.SelectDyingChoiceCommand, model.IsDyingSelectionPending),
                    (model.SelectHarvestChoiceCommand, model.IsHarvestSelectionPending),
                    (model.SelectTargetCardChoiceCommand, model.IsTargetCardSelectionPending),
                    (model.SelectFireAttackChoiceCommand, model.IsFireAttackSelectionPending),
                    (model.SelectNullificationChoiceCommand, model.IsNullificationSelectionPending),
                    (model.SelectResponseChoiceCommand, model.IsResponseSelectionPending),
                    (model.SelectSkillChoiceCommand, model.IsSkillSelectionPending)
                })
                {
                    if (!active) route.Execute(current);
                }
                Program.Assert(currentCard.IsSelected && model.CanConfirmSelected && model.PromptText == promptText &&
                    engine.Revision == revision && engine.AcceptedCommands.Count == commandCount &&
                    engine.CardMovements.Count == movements && SnapshotJson.Serialize(engine.CreateSnapshot(0, true)) == state,
                    $"{kind}: an inactive choice route must not consume a current choice or clear the prepared response.");
                model.ClearSelectionCommand.Execute(null);
                var command = Command(model, kind);
                command.Execute(stale);
                command.Execute(current with { });
                model.SelectHandResponseChoiceCommand.Execute(stale);
                model.SelectHandResponseChoiceCommand.Execute(current with { });
                Program.Assert(engine.Revision == revision && engine.CardMovements.Count == movements &&
                               SnapshotJson.Serialize(engine.CreateSnapshot(0, true)) == state,
                    $"{kind}: a stale or copied button choice was rebound to the current prompt and paid its cost.");
                command.Execute(current);
                Program.Assert(engine.Revision == revision + 1 &&
                               engine.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                               answer.PromptId == promptId && answer.Choice == current.Id,
                    $"{kind}: the current published choice no longer submits exactly once.");
                command.Execute(current);
                Program.Assert(engine.Revision == revision + 1,
                    $"{kind}: a retired button choice answered the next prompt.");
            }
        }
    }

    private static void ReloadedPlayChoice(string output)
    {
        var game = BorrowedSwordScenario.FindHumanSourcePlay();
        var registry = BorrowedSwordScenario.CreateSourcePlayFixtureRegistry();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var engine = Program.Engine(model);
        var action = engine.GetHumanLegalActions().First(candidate => candidate.Kind == LegalActionKind.BorrowedSword);
        Console.WriteLine($"Play-choice fixture: seed {game.Seed}, {game.AcceptedCommands.Count} setup commands, native Borrowed Sword.");
        model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == action.CardId));
        var retired = model.TargetCombinationChoices.First(choice => choice.Cards.SequenceEqual([action.CardId!.Value]));
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            Program.Render(root, 1120, 740, Path.Combine(output, "retired-borrowed-choice.png"));
            var button = Program.Find<Button>(root).Single(candidate => candidate.Command == model.SelectTargetCombinationChoiceCommand &&
                ReferenceEquals(candidate.CommandParameter, retired));
            Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible && button.ActualHeight > 0,
                "The fixed Borrowed Sword choice must be captured from a real enabled rendered button.");
            var capturedCommand = button.Command!;
            var capturedParameter = button.CommandParameter;
            model.LoadManualGameCommand.Execute(null);
            engine = Program.Engine(model);
            model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == action.CardId));
            var current = model.TargetCombinationChoices.Single(choice => choice.Id == retired.Id);
            Program.Assert(!ReferenceEquals(retired, current), "A real load must replace the published Borrowed Sword choice.");
            var revision = engine.Revision;
            var movements = engine.CardMovements.Count;
            var commands = engine.AcceptedCommands.Count;
            var state = SnapshotJson.Serialize(engine.CreateSnapshot(0));
            var prompt = engine.PendingDecision!.PromptId;
            capturedCommand.Execute(capturedParameter);
            Program.Assert(engine.Revision == revision && engine.CardMovements.Count == movements &&
                engine.AcceptedCommands.Count == commands && SnapshotJson.Serialize(engine.CreateSnapshot(0)) == state && model.HasSelection,
                "A retired Borrowed Sword button rebound to the loaded Play prompt and paid its physical card.");
            capturedCommand.Execute(current with { });
            Program.Assert(engine.Revision == revision && SnapshotJson.Serialize(engine.CreateSnapshot(0)) == state,
                "A copied Borrowed Sword choice must not substitute for the current published button parameter.");
            capturedCommand.Execute(current);
            Program.Assert(engine.Revision == revision + 1 && engine.AcceptedCommands.Count == commands + 1 &&
                engine.AcceptedCommands.Last() is PlayCardCommand command && command.CardId == action.CardId &&
                command.TargetSeats.SequenceEqual(current.Targets) && command.PromptId == prompt &&
                command.ExpectedRevision == revision && !model.HasSelection &&
                model.Hand.All(card => !card.IsSelected) && model.Seats.All(seat => !seat.IsSelectedTarget) &&
                engine.CardMovements.Skip(movements).Count(move => move.CardId == action.CardId &&
                    move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Use) == 1,
                "The current Borrowed Sword choice must submit its exact ordered targets and physical cost once.");
            capturedCommand.Execute(current);
            Program.Assert(engine.Revision == revision + 1 && engine.AcceptedCommands.Count == commands + 1,
                "The committed Borrowed Sword parameter must not submit a second action.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void VerifyOpaqueSlotFocus(GameCheckpoint initialPlay, ContentRegistry registry)
    {
        var prepared = CreateOpaqueSlotGame(initialPlay, registry, 2);
        var pending = prepared.CreateSnapshot(0).PendingDecision;
        Program.Assert(pending is { Kind: DecisionKind.SelectTargetCard, PlayerSeat: 0, IsPrivate: true } &&
            pending.Choices.Count == prepared.CreateSnapshot(0).Players[2].HandCount && pending.Choices.Count > 0 &&
            pending.Choices.All(choice => choice.Cards.Count == 0 && choice.Targets.SequenceEqual([2]) &&
                choice.Parameters.GetValueOrDefault("action") == "target-card-slot" &&
                choice.Parameters.ContainsKey("slot-index") && !choice.Parameters.ContainsKey("card-id")) &&
            Enumerable.Range(1, 3).All(viewer => prepared.CreateSnapshot(viewer).PendingDecision is null),
            "The existing seed31 fixture must publish only Human0's actual private opaque peer2 slots.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, prepared.CreateCheckpoint()));
        using var model = new MainViewModel(false, prepared.Seed, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var game = Program.Engine(model);
        var nativeBoundary = OpaqueBoundary(game);
        Program.Assert(!model.HasSaveError && nativeBoundary == OpaqueBoundary(prepared) && model.IsTargetCardSelectionPending,
            "Actual manual load must retain the native opaque prompt, four safe views and every command, event and movement.");
        var window = new MainWindow(model)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -30000, Top = -30000, Width = 1120, Height = 820
        };
        var root = (FrameworkElement)window.Content;
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            const int extendedStyle = -20;
            const int noActivate = 0x08000000;
            SetWindowLong(handle, extendedStyle, GetWindowLong(handle, extendedStyle) | noActivate);
            var previousParent = SetParent(handle, new nint(-3));
            var parentError = Marshal.GetLastPInvokeError();
            Program.Assert((previousParent != 0 || parentError == 0) && IsMessageOnlyWindow(handle),
                $"The opaque fixture's own HWND must be message-only: parent={previousParent}, error={parentError}.");
            window.Show();
            ShowWindow(handle, 0);
            SetFocus(handle);
            Pump();
            AssertHost();
            Program.Assert(window.IsLoaded && window.IsVisible && OpaqueBoundary(game) == nativeBoundary,
                "Loading the actual hidden source must leave the native opaque prompt and privacy boundary intact.");
            var choice = model.TargetCardChoices.Last();
            var button = SlotButton(choice);
            button.Focus();
            Program.Assert(button.IsEnabled && button.IsVisible && ReferenceEquals(Keyboard.FocusedElement, button),
                "The real current opaque slot button must acquire keyboard focus before a same-prompt refresh.");
            foreach (var developer in new[] { true, false })
            {
                var originalChoices = model.TargetCardChoices.ToArray();
                var originalContract = OpaqueChoiceContract(originalChoices);
                var retiredCommand = button.Command!;
                var retiredParameter = button.CommandParameter;
                model.IsDeveloperView = developer;
                Pump();
                var current = model.TargetCardChoices.Single(candidate => candidate.Id == choice.Id);
                var currentButton = SlotButton(current);
                var sameChoices = model.TargetCardChoices.SequenceEqual(originalChoices, ReferenceEqualityComparer.Instance);
                var sameButton = ReferenceEquals(button, currentButton);
                Console.WriteLine($"Opaque refresh developer={developer}: seed={prepared.Seed}, slot={current.Parameters["slot-index"]}, " +
                    $"choices={model.TargetCardChoices.Count}, same choice refs={sameChoices}, same button={sameButton}, " +
                    $"current exact focus={ReferenceEquals(Keyboard.FocusedElement, currentButton)}, " +
                    $"focused={(Keyboard.FocusedElement as FrameworkElement)?.Name ?? Keyboard.FocusedElement?.GetType().Name ?? "null"}.");
                AssertHost();
                Program.Assert(ReferenceEquals(Program.Engine(model), game) && OpaqueBoundary(game) == nativeBoundary &&
                    model.ChoiceSnapshot.PendingDecision!.PromptId == pending!.PromptId &&
                    OpaqueChoiceContract(model.TargetCardChoices) == originalContract,
                    "A diagnostic view refresh must preserve the complete native slot contract and all safe journals.");
                Program.Assert(model.TargetCardChoices.All(candidate =>
                        PromptCardChoiceConverter.Present(candidate, model.ChoiceSnapshot) is { IsHidden: true, Face: null }) &&
                    Program.Find<ContentControl>(currentButton).Any(content =>
                        content.Content is PromptCardChoicePresentation { IsCard: true, IsHidden: true, Face: null }),
                    "The actual refreshed opaque button must keep its card back even in a developer snapshot.");
                if (!sameChoices)
                {
                    retiredCommand.Execute(retiredParameter);
                    model.SelectTargetCardChoiceCommand.Execute(current with { });
                    Program.Assert(OpaqueBoundary(game) == nativeBoundary,
                        "Fresh snapshot objects legitimately retire the old slot parameter; old or equal-copy input must not submit.");
                }
                Program.Assert((!sameChoices || sameButton) && currentButton.IsVisible && currentButton.IsEnabled &&
                    ReferenceEquals(Keyboard.FocusedElement, currentButton),
                    "Refreshing the same native opaque prompt must retain keyboard focus on its current corresponding slot button.");
                choice = current;
                button = currentButton;
            }

            model.IsDeveloperView = true;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var sort = (Button)window.FindName("SortHandButton");
            sort.Focus();
            Program.Assert(sort.IsVisible && sort.IsEnabled && ReferenceEquals(Keyboard.FocusedElement, sort),
                "The competing-focus fixture must reach the actual enabled hand-sort button before deferred input.");
            Pump();
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, sort) && OpaqueBoundary(game) == nativeBoundary,
                "Deferred opaque-slot restoration must preserve the live table control the user already focused.");
            choice = model.TargetCardChoices.Single(candidate => candidate.Id == choice.Id);
            button = SlotButton(choice);
            button.Focus();
            Program.Assert(button.IsVisible && button.IsEnabled && ReferenceEquals(Keyboard.FocusedElement, button),
                "The settings competition must begin with exact focus on the current opaque slot before queuing its refresh.");
            model.IsDeveloperView = false;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            model.OpenSettingsCommand.Execute(null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            // Arrange the newly visible scroll viewport without running the queued Input callback.
            root.UpdateLayout();
            var motion = (CheckBox)window.FindName("SettingsMotionToggle");
            motion.Focus();
            Program.Assert(motion.IsVisible && motion.IsEnabled && ReferenceEquals(Keyboard.FocusedElement, motion),
                "The overlay competition must focus its actual enabled settings control before deferred input.");
            Pump();
            Program.Assert(model.IsSettingsOpen && ReferenceEquals(Keyboard.FocusedElement, motion) &&
                OpaqueBoundary(game) == nativeBoundary,
                "A queued slot callback must preserve the user's focused settings control and untouched native boundary.");
            model.CloseSettingsCommand.Execute(null);
            Pump();
            choice = model.TargetCardChoices.Single(candidate => candidate.Id == choice.Id);
            button = SlotButton(choice);
            button.Focus();
            Pump();
            Program.Assert(button.IsVisible && button.IsEnabled && ReferenceEquals(Keyboard.FocusedElement, button),
                "The changed-contract boundary must begin with exact focus on the current opaque slot before queuing its refresh.");

            var changed = CreateOpaqueSlotGame(initialPlay, registry, 1);
            var changedPrompt = changed.CreateSnapshot(0).PendingDecision!;
            Program.Assert(changedPrompt.Kind == DecisionKind.SelectTargetCard && changedPrompt.PromptId == pending!.PromptId &&
                changedPrompt.Choices.Select(candidate => candidate.Id).SequenceEqual(model.TargetCardChoices.Select(candidate => candidate.Id)) &&
                changedPrompt.Choices.All(candidate => candidate.Targets.SequenceEqual([1])) &&
                OpaqueChoiceContract(changedPrompt.Choices) != OpaqueChoiceContract(model.TargetCardChoices),
                "The alternate native hand prompt must reuse the numeric prompt/slot IDs while changing the complete target contract.");
            var changedRetiredCommand = button.Command!;
            var changedRetiredParameter = button.CommandParameter;
            model.IsDeveloperView = true;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, changed.CreateCheckpoint()));
            model.LoadManualGameCommand.Execute(null);
            Pump();
            var changedGame = Program.Engine(model);
            var changedBoundary = OpaqueBoundary(changedGame);
            Program.Assert(!model.HasSaveError && changedBoundary == OpaqueBoundary(changed) &&
                !model.TargetCardChoices.Any(candidate => ReferenceEquals(Keyboard.FocusedElement, SlotButton(candidate))),
                "A changed native contract must not inherit the retired target's slot focus merely because prompt/choice IDs match.");
            changedRetiredCommand.Execute(changedRetiredParameter);
            Program.Assert(OpaqueBoundary(changedGame) == changedBoundary,
                "The retired target's actual button parameter must not answer the alternate native private prompt.");
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, prepared.CreateCheckpoint()));
            model.LoadManualGameCommand.Execute(null);
            Pump();
            game = Program.Engine(model);
            choice = model.TargetCardChoices.Last();
            button = SlotButton(choice);
            button.Focus();
            Pump();
            Program.Assert(OpaqueBoundary(game) == nativeBoundary && ReferenceEquals(Keyboard.FocusedElement, button),
                "The final live-load submission must restart from the exact original unconsumed native hand prompt.");

            var liveCommand = button.Command!;
            var liveParameter = button.CommandParameter;
            var liveChoice = choice;
            var liveButton = button;
            model.LoadManualGameCommand.Execute(null);
            Pump();
            var reloaded = Program.Engine(model);
            choice = model.TargetCardChoices.Single(candidate => candidate.Id == liveChoice.Id);
            button = SlotButton(choice);
            Program.Assert(!model.HasSaveError && !ReferenceEquals(game, reloaded) && !ReferenceEquals(choice, liveChoice) &&
                !ReferenceEquals(button, liveButton) && button.IsVisible && button.IsEnabled &&
                !Program.Find<Button>(root).Contains(liveButton) && OpaqueBoundary(reloaded) == nativeBoundary,
                "Loading the unconsumed opaque prompt must publish a fresh current slot and retire its old actual button.");
            liveCommand.Execute(liveParameter);
            model.SelectTargetCardChoiceCommand.Execute(choice with { });
            Program.Assert(OpaqueBoundary(reloaded) == nativeBoundary,
                "A retired actual slot button and an equal fresh-choice copy must not answer the loaded private prompt.");
            var before = reloaded.CreateCheckpoint();
            var currentPrompt = model.ChoiceSnapshot.PendingDecision!;
            var reference = GameReplay.Restore(before, registry);
            var answer = reference.Submit(new AnswerPromptCommand(0, currentPrompt.PromptId, choice.Id, reference.Revision));
            Program.Assert(answer.Accepted, answer.Error?.Message ?? "The actual native slot must be accepted independently.");
            typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
            Program.Assert(reloaded.Revision == before.Revision + 1 && reloaded.AcceptedCommands.Count == before.Commands.Count + 1 &&
                reloaded.AcceptedCommands.Last() is AnswerPromptCommand submitted && submitted.ActorSeat == 0 &&
                submitted.ExpectedRevision == before.Revision && submitted.PromptId == currentPrompt.PromptId && submitted.Choice == choice.Id &&
                OpaqueBoundary(reloaded) == OpaqueBoundary(reference),
                "The fresh actual slot must answer once and match independent native execution in every safe view and journal.");
            var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(reloaded.CreateCheckpoint())), registry);
            Program.Assert(OpaqueBoundary(cold) == OpaqueBoundary(reloaded) &&
                reloaded.Events.Select(item => item.Payload).OfType<TargetCardDiscardedEvent>().Last() is
                    { TargetSeat: 2, PublicCardId: null, PublicCardKind: null },
                "The real hidden-hand outcome must cold-restore exactly without publishing the selected entity or face.");

            Button SlotButton(PromptChoice parameter) => Program.Find<Button>(root).Single(candidate =>
                ReferenceEquals(candidate.Command, model.SelectTargetCardChoiceCommand) && ReferenceEquals(candidate.CommandParameter, parameter));
            void Pump()
            {
                root.Measure(new Size(1120, 820));
                root.Arrange(new Rect(0, 0, 1120, 820));
                root.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                root.UpdateLayout();
            }
            void AssertHost() => Program.Assert(IsMessageOnlyWindow(handle) && GetFocus() == handle &&
                !IsWindowVisible(handle) && GetForegroundWindow() != handle && (GetWindowLong(handle, extendedStyle) & noActivate) != 0,
                "The real opaque slot focus must belong to the owned native-hidden message-only source without taking foreground.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static GameEngine CreateOpaqueSlotGame(GameCheckpoint initialPlay, ContentRegistry registry, int targetSeat)
    {
        var game = GameReplay.Restore(initialPlay, registry);
        var action = game.GetHumanLegalActions().First(candidate => candidate.Kind == LegalActionKind.Dismantlement &&
            candidate.TargetSeat == targetSeat && candidate.TargetCardId is null && candidate.ConversionSource is null);
        var play = game.CreateSnapshot(0).PendingDecision!;
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, play.PromptId, action.PlayedCardKind, action.TargetCardId));
        Program.Assert(result.Accepted, result.Error?.Message ?? "The fixed native opaque-slot use must be accepted.");
        for (var step = 0; step < 40 && game.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.SelectTargetCard; step++)
        {
            result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Program.Assert(result.Accepted, result.Error?.Message ?? "The real dismantlement must reach its opaque-card continuation.");
        }
        Program.Assert(game.CreateSnapshot(0).PendingDecision?.Kind == DecisionKind.SelectTargetCard,
            "The bounded native use must reach its real hand-slot prompt.");
        return game;
    }

    private static string OpaqueChoiceContract(IEnumerable<PromptChoice> choices) => JsonSerializer.Serialize(choices);

    private static string OpaqueBoundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, Commands = CommandJson.Serialize(game.AcceptedCommands), Movements = game.CardMovements,
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        Views = Enumerable.Range(0, 4).Select(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer))).ToArray()
    });

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

    private static void PublicTargetChoice(string output)
    {
        var (game, registry) = CreatePublicTargetFixture();
        var weapon = game.CreateSnapshot(0).Players[1].Equipment.Single(card => card.Kind == CardKind.Crossbow);
        Program.Assert(game.CardMovements.Any(move => move.CardId == weapon.Id && move.To == CardLocation.Equipment(1)),
            "The public target must be an actual equipped physical weapon, not a fabricated snapshot.");
        var action = game.GetHumanLegalActions().First(candidate => candidate.Kind == LegalActionKind.Dismantlement &&
            candidate.TargetSeat == 1 && candidate.TargetCardId == weapon.Id);
        PublicTargetChoiceControls(output, game, registry, weapon, action);
    }

    internal static (GameEngine Game, ContentRegistry Registry) CreatePublicTargetFixture(
        string effectCardId = "standard:dismantlement", string equipmentCardId = "standard:crossbow",
        bool useClassicRules = false, params IGameContentPackage[] additionalPackages) =>
        CreatePublicTargetFixture([effectCardId], equipmentCardId, useClassicRules, additionalPackages);

    internal static (GameEngine Game, ContentRegistry Registry) CreatePublicTargetFixture(
        IReadOnlyList<string> effectCardIds, string equipmentCardId,
        bool useClassicRules = false, params IGameContentPackage[] additionalPackages)
    {
        var modeId = useClassicRules ? "identity:classic-published-play-choice" : PublicTargetFixture.Mode;
        var registry = ContentRegistry.Build([new StandardContentPackage(), .. additionalPackages,
            new PublicTargetFixture(effectCardIds, equipmentCardId, modeId)]);
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = modeId, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false
        }, registry);
        void Accept(GameCommand command)
        {
            var result = game.Submit(command);
            Program.Assert(result.Accepted, result.Error?.Message ?? "Expected a real public-target fixture command.");
        }
        void ReachPlay()
        {
            for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                Accept(new AdvanceOneStepCommand(game.Revision));
            Program.Assert(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
                "The public-target fixture must reach a real human Play boundary.");
        }
        Accept(new StartGameCommand());
        Accept(new SelectGeneralCommand(0, PublicTargetFixture.Owner, game.Revision, game.PendingDecision!.PromptId));
        ReachPlay();
        Accept(new UseProgramSkillCommand(0, PublicTargetFixture.Driver, "equip", [], [1], game.Revision, game.PendingDecision!.PromptId));
        ReachPlay();
        return (game, registry);
    }

    private static void PublicTargetChoiceControls(string output, GameEngine game, ContentRegistry registry,
        CardSnapshot weapon, LegalAction action)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == action.CardId));
        model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 1));
        var retired = model.PublicTargetChoices.Single(choice => choice.Cards.SequenceEqual([action.CardId!.Value]) &&
            choice.Parameters.GetValueOrDefault("target-card-id") == weapon.Id.ToString());
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            Program.Render(root, 1120, 740, Path.Combine(output, "retired-public-target-choice.png"));
            var button = Program.Find<Button>(root).Single(candidate => candidate.Command == model.SelectPublicTargetChoiceCommand &&
                ReferenceEquals(candidate.CommandParameter, retired));
            Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible && button.ActualHeight > 0,
                "The public weapon option must be captured from a real enabled rendered button.");
            var capturedCommand = button.Command!;
            var capturedParameter = button.CommandParameter;
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 2));
            var engine = Program.Engine(model);
            var published = (GameSnapshot)typeof(MainViewModel).GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model)!;
            Program.Assert(model.Seats.Single(seat => seat.Seat == 2).IsSelectedTarget &&
                model.PublicTargetChoices.Count == published.Players.Single(player => player.Seat == 2).HandCount &&
                model.PublicTargetChoices.All(choice => !ReferenceEquals(choice, retired) &&
                    choice.Targets.SequenceEqual([2]) && choice.Parameters.ContainsKey("target-hand-slot")) &&
                published.PendingDecision!.Choices.Any(choice => ReferenceEquals(choice, retired)),
                "Selecting another legal target must replace the weapon button with that target's opaque hand slots while retaining the original published choice in the same prompt.");
            Console.WriteLine($"Public-target fixture: seed 31, {game.AcceptedCommands.Count} setup commands, " +
                $"physical weapon {weapon.Id}, same-prompt target 1 to 2.");
            var revision = engine.Revision;
            var movements = engine.CardMovements.Count;
            var commands = engine.AcceptedCommands.Count;
            var state = SnapshotJson.Serialize(engine.CreateSnapshot(0));
            capturedCommand.Execute(capturedParameter);
            Program.Assert(engine.Revision == revision && engine.CardMovements.Count == movements &&
                engine.AcceptedCommands.Count == commands && SnapshotJson.Serialize(engine.CreateSnapshot(0)) == state &&
                model.Seats.Single(seat => seat.Seat == 2).IsSelectedTarget,
                "A withdrawn public weapon button committed against the previous target in the same Play prompt.");

            model.LoadManualGameCommand.Execute(null);
            model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == action.CardId));
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 1));
            engine = Program.Engine(model);
            var current = model.PublicTargetChoices.Single(choice => choice.Id == retired.Id);
            Program.Assert(!ReferenceEquals(retired, current), "Reload must replace the public weapon choice object.");
            state = SnapshotJson.Serialize(engine.CreateSnapshot(0));
            capturedCommand.Execute(retired);
            capturedCommand.Execute(current with { });
            Program.Assert(engine.Revision == revision && engine.CardMovements.Count == movements && engine.AcceptedCommands.Count == commands &&
                SnapshotJson.Serialize(engine.CreateSnapshot(0)) == state && model.Seats.Single(seat => seat.Seat == 1).IsSelectedTarget,
                "A retired or copied public weapon choice committed against the loaded prompt.");
            var prompt = engine.PendingDecision!.PromptId;
            capturedCommand.Execute(current);
            Program.Assert(engine.Revision == revision + 1 && engine.AcceptedCommands.Count == commands + 1 &&
                engine.AcceptedCommands.Last() is PlayCardCommand command && command.CardId == action.CardId &&
                command.TargetSeats.SequenceEqual([1]) && command.TargetCardId == weapon.Id && command.PromptId == prompt &&
                command.ExpectedRevision == revision && !model.HasSelection &&
                model.Hand.All(card => !card.IsSelected) && model.Seats.All(seat => !seat.IsSelectedTarget) &&
                engine.CardMovements.Skip(movements).Count(move => move.CardId == action.CardId &&
                    move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Use) == 1,
                "The current public target option must submit its exact card, prompt, seat and physical weapon once.");
            capturedCommand.Execute(current);
            Program.Assert(engine.Revision == revision + 1 && engine.AcceptedCommands.Count == commands + 1,
                "The committed public target parameter must not submit a second action.");
            for (var step = 0; step < 40 && model.CanStepAi; step++) model.StepAiCommand.Execute(null);
            Console.WriteLine($"Public-target settlement: status={engine.CreateSnapshot(0).Status}, prompt={engine.PendingDecision?.Kind}, " +
                $"moves={string.Join("; ", engine.CardMovements.Skip(movements).Where(move => move.CardId == weapon.Id || move.CardId == action.CardId).Select(move => $"{move.CardId}:{move.From}->{move.To}:{move.Reason.Value}"))}.");
            Program.Assert(engine.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                engine.CardMovements.Skip(movements).Count(move => move.CardId == weapon.Id &&
                    move.From == CardLocation.Equipment(1) && move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Dismantlement) == 1 &&
                engine.CardMovements.Skip(movements).Count(move => move.CardId == weapon.Id &&
                    move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.DismantlementFinished) == 1 &&
                engine.CardMovements.Skip(movements).Count(move => move.CardId == action.CardId &&
                    move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.UseFinished) == 1,
                "The current public target use must finish its native effect, discard the exact weapon and complete the paid card once.");
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(engine.CreateCheckpoint())), registry);
            Program.Assert(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(engine.CreateSnapshot(seat)) ==
                SnapshotJson.Serialize(restored.CreateSnapshot(seat))), "The public target UI command must replay every player view exactly.");
        }
        finally { window.Content = null; window.Close(); }
        VerifyOpaqueSlotFocus(game.CreateCheckpoint(), registry);
    }

    private sealed class PublicTargetFixture(IReadOnlyList<string> effectCardIds, string equipmentCardId, string modeId) : IGameContentPackage
    {
        internal const string Mode = "fixture:published-play-choice";
        internal const string Owner = "fixture:published-play-choice-owner";
        internal const string Driver = "fixture:published-play-choice-driver";
        public PackageManifest Manifest { get; } = new("fixture-published-play-choice", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                    {"id":"{{Driver}}","revision":1,"activations":[{"id":"equip","usesPerTurn":1,"minCards":0,"maxCards":0,
                        "minTargets":1,"maxTargets":1,"targetKind":"anyLiving","effects":[
                            {"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                        {"id":"strip","usesPerTurn":null,"minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","effects":[
                            {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand","equipment","judgment"],"count":1,"destination":"discardPile"}]},
                        {"id":"take","usesPerTurn":null,"minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","effects":[
                            {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand","equipment","judgment"],"count":1,"destination":"ownerHand"}]}]}]}
                """, JsonSerializer.Serialize(new
                {
                    schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Driver] = new { name = "真实装备准备", description = "公开目标按钮夹具" } }
                }));
            builder.AddSkill(new(Driver, "真实装备准备", "公开目标按钮夹具") { Program = catalog.Programs[Driver] });
            builder.AddGeneral(new(Owner, "出牌者", "supporter", Driver, "wei", 4));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:published-play-choice-peer-{seat}").ToArray();
            foreach (var id in peers) builder.AddGeneral(new(id, id, "supporter", "standard:none", "wei", 4));
            builder.AddDeck(new("fixture:published-play-choice-deck", "公开装备目标", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 56).Select(index => new ContentDeckPhysicalCard(
                    index < 20 * effectCardIds.Count ? effectCardIds[index / 20] : equipmentCardId, Suit.Spade, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(modeId, "公开装备目标", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:published-play-choice-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }

    private static IEnumerable<PromptChoice> Choices(MainViewModel model, DecisionKind kind) => kind switch
    {
        DecisionKind.RescueDying => model.DyingChoices,
        DecisionKind.Nullification => model.NullificationChoices,
        DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard => model.FireAttackChoices,
        _ => model.ResponseChoices
    };

    private static ICommand Command(MainViewModel model, DecisionKind kind) => kind switch
    {
        DecisionKind.RescueDying => model.SelectDyingChoiceCommand,
        DecisionKind.Nullification => model.SelectNullificationChoiceCommand,
        DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard => model.SelectFireAttackChoiceCommand,
        _ => model.SelectResponseChoiceCommand
    };
}
