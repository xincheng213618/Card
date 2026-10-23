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
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class ProgramPaidChoiceUiChecks
{
    public static void SharedChoiceSurfaces(string output)
    {
        ParticipantPayments(output);
        DonorControlsLordReward(output);
    }

    private static void DonorControlsLordReward(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new RewardPackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Renegade, PlayerCount = 4,
            ModeId = RewardPackage.ModeId, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 20
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, game.PendingDecision!.Choices[0].ContentIds[0],
            game.Revision, game.PendingDecision.PromptId)));
        Reach(game, DecisionKind.RespondSlash);
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Cards.Count == 1));
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        Program.Assert(prompt.SkillPrompt?.SkillId == "boundary:jijiang" && prompt.PlayerSeat == 0 &&
                       prompt.TargetSeat == 0 && prompt.SourceSeat is > 0,
            "The actual outside-turn Shu response must produce a donor-controlled Lord reward.");
        var lordSeat = prompt.SourceSeat!.Value;
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(autoAdvance: false, seed: game.Seed, showSetup: true,
            saveStore: store, useExpandedContent: true, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        Program.Assert(model.CurrentDecisionContext is { TargetSeat: 0, TargetLabel: "正在选择" } context &&
                       context.SourceSeat == lordSeat && context.Title.Contains("激将", StringComparison.Ordinal) &&
                       model.IsSkillSelectionPending && model.SkillChoices.Count == 2 &&
                       Enumerable.Range(1, 3).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
            "Restoring the reward must identify the human donor as chooser and keep the AI Lord as beneficiary.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "267-program-donor-lord-reward.png"));
        var choicePanel = (ItemsControl)window.FindName(model.HasDockSkillChoices ? "DockSkillChoices" : "ScrollingSkillChoices");
        var buttons = Program.Find<Button>(choicePanel).Where(button =>
            button.Command == model.SelectSkillChoiceCommand && button.Visibility == Visibility.Visible &&
            button.IsEnabled && button.ActualWidth > 0 && button.ActualHeight > 0).ToArray();
        Program.Assert(!model.HasPinnedPublicModuleChoices && choicePanel.ActualHeight > 0 && buttons.Length == 2,
            "The human donor must have two visible, enabled reward choices through the shared WPF surface.");
        Program.Assert(!model.HasDockSkillChoices || model.UngroupedCenterSkillChoices.Count == 0,
            "A donor reward shown in the lower dock must not retain duplicate central choices.");
        var before = game.CreateSnapshot(0).Players[lordSeat].HandCount;
        var revision = game.Revision;
        model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Program.Assert(game.Revision == revision + 1 &&
                       game.CreateSnapshot(0).Players[lordSeat].HandCount == before + 1 &&
                       game.CreateSnapshot(0).PendingDecision?.SkillPrompt?.SkillId != "boundary:jijiang" &&
                       model.SkillChoices.Count == 0 && !model.IsSkillSelectionPending,
            "The donor's real WPF command must award the Lord once and retire the old private choice.");
        window.Content = null;
        window.Close();
    }

    private static void ParticipantPayments(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new ParticipantPackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = ParticipantPackage.ModeId, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Reach(game, DecisionKind.SelectGeneral);
        Accept(game.Submit(new SelectGeneralCommand(0, ParticipantPackage.OwnerId, game.Revision,
            game.PendingDecision!.PromptId)));
        Reach(game, DecisionKind.PlayCard);
        Accept(game.Submit(new UseProgramSkillCommand(0, "boundary:chuli", "discard-participants", [], [],
            game.Revision, game.PendingDecision!.PromptId)));
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Targets.SequenceEqual([1])));
        VerifyCenterChoiceFocus(game.CreateCheckpoint(), registry);
        Answer(game, game.PendingDecision!.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "distinct-faction-finish"));

        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(autoAdvance: false, seed: game.Seed, showSetup: true,
            saveStore: store, useExpandedContent: true, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var ownChoice = model.SkillChoices.First(choice => choice.Cards.Count == 1);
        var ownCardId = ownChoice.Cards[0];
        Program.Assert(model.IsSkillSelectionPending && model.CurrentDecisionContext is { TargetSeat: 0 } &&
                       model.Hand.Any(card => card.Id == ownCardId) &&
                       model.SkillChoices.All(choice => choice.Cards.Count == 1),
            "Restoring a participant plan must show the actual owner's own payment choices.");

        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Assert(model.IsHandResponsePending && model.CanActFromHand && !model.HasCenterChoices &&
                       model.Hand.Where(card => model.SkillChoices.Any(choice => choice.Cards.Contains(card.Id))).All(card => card.IsPlayable),
            "Own payment choices must light the actual hand and omit duplicate central cards.");
        var ownCard = model.Hand.Single(card => card.Id == ownCardId);
        var paymentRevision = game.Revision;
        var paymentMoves = game.CardMovements.Count;
        model.SelectCardCommand.Execute(ownCard);
        typeof(MainViewModel).GetMethod("RefreshCommandResult", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(model, [game.CreateSnapshot(0)]);
        Program.Assert(model.CanConfirmSelected && ownCard.IsSelected && game.Revision == paymentRevision &&
                       game.CardMovements.Count == paymentMoves,
            "Selecting an own payment card must survive command reconciliation and wait for confirmation without paying it.");
        model.ClearSelectionCommand.Execute(null);
        Program.Assert(!model.CanConfirmSelected && !ownCard.IsSelected && game.Revision == paymentRevision,
            "Cancelling a mandatory payment draft must clear selection without declining or paying.");
        model.SelectCardCommand.Execute(ownCard);
        Program.Render(root, 1120, 740, Path.Combine(output, "264-program-own-hand-payment.png"));
        Program.Assert(((Border)window.FindName("DecisionPanel")).Visibility == Visibility.Collapsed &&
                       ((Button)window.FindName("PlayCardButton")).IsEnabled,
            "Own payment must expose the actual bottom confirm control with no central duplicate panel.");
        model.ConfirmSelectedCommand.Execute(null);
        Program.Assert(game.Revision == paymentRevision + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                       answer.Choice == ownChoice.Id && !model.HasSelection,
            "The bottom control must submit the exact published payment once and retire the draft.");
        Program.AdvanceToDecision(model);
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        Program.Assert(prompt.SkillPrompt?.SkillId == "boundary:chuli" && model.IsSkillSelectionPending &&
                       model.SkillChoices.Count == 4 && model.SkillChoices.All(choice =>
                           choice.Cards.Count == 0 &&
                           choice.Parameters.GetValueOrDefault("card-owner-seat") == "1" &&
                           choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand) &&
                           !choice.Parameters.ContainsKey("card-id")) &&
                       Enumerable.Range(1, 3).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
            "Other hand costs must use the shared choice surface with opaque slots and private chooser ownership.");
        Program.Render(root, 1120, 740, Path.Combine(output, "265-program-participant-private-cost.png"));
        var picker = Program.Find<TargetCardPicker>(root).Single();
        var buttons = Program.Find<Button>(picker).Where(button =>
            button.Command == model.SelectSkillChoiceCommand && button.Visibility == Visibility.Visible &&
            button.IsEnabled && button.ActualWidth > 0 && button.ActualHeight > 0).ToArray();
        Program.Assert(!model.HasPinnedPublicModuleChoices && model.TargetCardRegions is [{ Zone: CardZoneKind.Hand } region] &&
                       region.Choices.SequenceEqual(model.SkillChoices, ReferenceEqualityComparer.Instance) &&
                       model.UngroupedCenterSkillChoices.Count == 0 && picker.ActualHeight > 0 && buttons.Length == model.SkillChoices.Count,
            "Every opaque payment slot must be reachable in the hand region, without empty equipment or judgment rows.");

        model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Last());
        Program.AdvanceToDecision(model);
        var costs = game.CardMovements.Where(move =>
            move.Reason.Value == "skill-program.boundary:chuli.participant-discard").ToArray();
        var rewards = game.CardMovements.Where(move =>
            move.Reason.Value == "skill-program.boundary:chuli.participant-reward").ToArray();
        Program.Assert(costs.Length == 2 && costs[0].CardId == ownCardId &&
                       costs.Select(move => move.From.OwnerSeat).SequenceEqual(new int?[] { 0, 1 }) &&
                       rewards.Length == 2 && rewards.All(move => move.Sequence > costs[^1].Sequence) &&
                       game.CreateSnapshot(0).PendingDecision?.Kind == DecisionKind.PlayCard &&
                       model.SkillChoices.Count == 0 && !model.IsSkillSelectionPending,
            "WPF payment commands must commit each physical cost once, finish both rewards, and clear the old choices.");
        Program.Render(root, 1120, 740, Path.Combine(output, "266-program-participant-paid.png"));
        window.Content = null;
        window.Close();
    }

    private static void VerifyCenterChoiceFocus(GameCheckpoint checkpoint, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, checkpoint));
        using var model = new MainViewModel(false, checkpoint.Options.Seed, true, store,
            useExpandedContent: true, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var game = Program.Engine(model);
        var boundary = ChoiceBoundary(game);
        var native = GameReplay.Restore(checkpoint, registry);
        Program.Assert(!model.HasSaveError && boundary == ChoiceBoundary(native) &&
            game.CreateSnapshot(0).Players.Count == 4 && model.IsSkillTargetSelectionPending &&
            model.CenterSkillChoices is [{ Cards.Count: 0, Targets.Count: 0 } finish] &&
            finish.Parameters.GetValueOrDefault("program-action") == "distinct-faction-finish",
            "The existing seed31 native participant checkpoint must retain one Finish choice beside legal portrait targets.");
        Program.Assert(model.HasDockSkillChoices && model.ShowPrimaryActionButtons && !model.HasCenterChoices,
            "A small Finish alternative must share the lower dock with portrait confirmation, without a central chooser.");
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
                $"The center-choice fixture's own HWND must be message-only: parent={previousParent}, error={parentError}.");
            window.Show();
            ShowWindow(handle, 0);
            SetFocus(handle);
            Pump();
            AssertHost();
            Program.Assert(window.IsLoaded && window.IsVisible && ChoiceBoundary(game) == boundary,
                "The actual loaded hidden source must preserve the complete native participant checkpoint.");
            var choice = model.CenterSkillChoices.Single();
            var button = ChoiceButton(choice);
            button.Focus();
            Program.Assert(button.IsVisible && button.IsEnabled && ReferenceEquals(Keyboard.FocusedElement, button),
                "The actual current Finish button must acquire exact keyboard focus before a hint-only update.");
            foreach (var speed in new[] { PlaybackSpeed.Fast, PlaybackSpeed.Normal })
            {
                var published = model.SkillChoices.ToArray();
                model.SelectedPlaybackSpeed = speed;
                Pump();
                var current = model.CenterSkillChoices.Single();
                var currentButton = ChoiceButton(current);
                Console.WriteLine($"Center choice speed={speed.Id}: seed=31, prompt={model.ChoiceSnapshot.PendingDecision!.PromptId}, " +
                    $"choice={current.Id}, same choice={ReferenceEquals(choice, current)}, same button={ReferenceEquals(button, currentButton)}, " +
                    $"exact focus={ReferenceEquals(Keyboard.FocusedElement, currentButton)}.");
                AssertHost();
                Program.Assert(ReferenceEquals(Program.Engine(model), game) && ChoiceBoundary(game) == boundary &&
                    model.SkillChoices.SequenceEqual(published, ReferenceEqualityComparer.Instance) && ReferenceEquals(choice, current),
                    "A playback-speed presentation update must preserve the current published native choices, prompt and every safe view and journal.");
                Program.Assert(ReferenceEquals(button, currentButton) && currentButton.IsVisible && currentButton.IsEnabled &&
                    ReferenceEquals(Keyboard.FocusedElement, currentButton),
                    "A hint-only update must preserve the same actual Finish button and its exact keyboard focus.");
                choice = current;
                button = currentButton;
            }

            var retiredChoice = choice;
            var retiredButton = button;
            var retiredCommand = button.Command!;
            var retiredParameter = button.CommandParameter;
            model.LoadManualGameCommand.Execute(null);
            Pump();
            var reloaded = Program.Engine(model);
            choice = model.CenterSkillChoices.Single();
            button = ChoiceButton(choice);
            Program.Assert(!model.HasSaveError && !ReferenceEquals(game, reloaded) && !ReferenceEquals(retiredChoice, choice) &&
                !ReferenceEquals(retiredButton, button) && button.IsVisible && button.IsEnabled &&
                !Program.Find<Button>(panel()).Contains(retiredButton) && ChoiceBoundary(reloaded) == boundary,
                "Reloading the unconsumed native selection must publish a fresh enabled Finish button and retire its old parameter and container.");
            retiredCommand.Execute(retiredParameter);
            model.SelectSkillChoiceCommand.Execute(choice with { });
            Program.Assert(ChoiceBoundary(reloaded) == boundary,
                "An actual retired Finish parameter and an equal copied choice must not complete the loaded participant selection.");
            var before = reloaded.CreateCheckpoint();
            var prompt = model.ChoiceSnapshot.PendingDecision!;
            var reference = GameReplay.Restore(before, registry);
            Accept(reference.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, reference.Revision)));
            Program.Assert(button.Command!.CanExecute(button.CommandParameter), "The fresh actual Finish button must remain executable.");
            typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
            Program.Assert(reloaded.Revision == before.Revision + 1 && reloaded.AcceptedCommands.Count == before.Commands.Count + 1 &&
                reloaded.AcceptedCommands.Last() is AnswerPromptCommand submitted && submitted.ActorSeat == 0 &&
                submitted.ExpectedRevision == before.Revision && submitted.PromptId == prompt.PromptId && submitted.Choice == choice.Id &&
                ChoiceBoundary(reloaded) == ChoiceBoundary(reference),
                "The fresh actual Finish button must submit exactly once and match independent native continuation in all safe views and journals.");
            Pump();
            var paymentChoices = model.CenterSkillChoices;
            var paymentButtons = Program.Find<Button>(root).Where(candidate =>
                ReferenceEquals(candidate.Command, model.SelectCardCommand) && candidate.CommandParameter is CardViewModel card &&
                paymentChoices.Any(current => current.Cards.Contains(card.Id))).ToArray();
            Program.Assert(!model.IsSkillTargetSelectionPending && model.IsSkillSelectionPending && paymentChoices.Count > 0 &&
                paymentChoices.All(current => current.Cards.Count == 1) &&
                paymentChoices.SequenceEqual(model.SkillChoices, ReferenceEqualityComparer.Instance) &&
                model.IsHandResponsePending && !model.HasCenterChoices && panel().Visibility == Visibility.Collapsed &&
                !Program.Find<Button>(panel()).Contains(button) && paymentButtons.Length == paymentChoices.Count &&
                paymentButtons.All(candidate => candidate.IsVisible && candidate.IsEnabled && candidate.ActualHeight > 0 &&
                    candidate.Command!.CanExecute(candidate.CommandParameter)),
                "The same loaded window must retire Finish and expose the owner's actual hand controls without duplicate central payment cards.");
            model.SelectSkillChoiceCommand.Execute(choice);
            Program.Assert(ChoiceBoundary(reloaded) == ChoiceBoundary(reference),
                "The completed Finish parameter must not answer or pay the newly published owner-cost prompt a second time.");
            Console.WriteLine($"Center choice payment: target draft={model.IsSkillTargetSelectionPending}, " +
                $"choices={paymentChoices.Count}, enabled buttons={paymentButtons.Length}, retired Finish removed=True, same native continuation=True.");
            var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(reloaded.CreateCheckpoint())), registry);
            Program.Assert(ChoiceBoundary(cold) == ChoiceBoundary(reloaded),
                "The real participant selection must cold-restore its native pending payment, commands, events, movements and four safe views exactly.");

            ItemsControl panel() => (ItemsControl)window.FindName(model.HasDockSkillChoices ? "DockSkillChoices" : "ScrollingSkillChoices");
            Button ChoiceButton(PromptChoice parameter) => Program.Find<Button>(panel()).Single(candidate =>
                ReferenceEquals(candidate.Command, model.SelectSkillChoiceCommand) && ReferenceEquals(candidate.CommandParameter, parameter));
            void Pump()
            {
                root.Measure(new Size(1120, 820)); root.Arrange(new Rect(0, 0, 1120, 820)); root.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                root.UpdateLayout();
            }
            void AssertHost() => Program.Assert(IsMessageOnlyWindow(handle) && GetFocus() == handle &&
                !IsWindowVisible(handle) && GetForegroundWindow() != handle && (GetWindowLong(handle, extendedStyle) & noActivate) != 0,
                "The real center-choice focus must belong to the owned hidden message-only source without taking foreground.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static string ChoiceBoundary(GameEngine game) => JsonSerializer.Serialize(new
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

    private static void Reach(GameEngine game, DecisionKind kind)
    {
        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } prompt && prompt.Kind == kind) return;
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        throw new InvalidOperationException($"The fixed UI fixture did not reach {kind}.");
    }

    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(
        new AnswerPromptCommand(0, game.PendingDecision!.PromptId, choice.Id, game.Revision)));

    private static void Accept(CommandResult result) => Program.Assert(result.Accepted,
        result.Error?.Message ?? "The fixed UI command was rejected.");

    private sealed class ParticipantPackage : IGameContentPackage
    {
        internal const string OwnerId = "fixture:paid-choice-owner";
        internal const string ModeId = "identity:program-paid-choice-ui";
        public PackageManifest Manifest { get; } = new("fixture:program-paid-choice-ui", new(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new(OwnerId, "支付选择夹具", "supporter", "boundary:chuli", "qun", 3, []));
            var factions = new[] { "shu", "wu", "wei" };
            for (var index = 0; index < factions.Length; index++)
                builder.AddGeneral(new($"fixture:paid-choice-{index}", $"参与者 {index + 1}", "supporter",
                    "standard:none", factions[index], 3, []));
            builder.AddDeck(new("fixture:paid-choice-deck", "固定支付牌库", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(index =>
                    new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(ModeId, "实际支付选择界面", 4, 4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2
                }, "fixture:paid-choice-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [OwnerId, "fixture:paid-choice-0", "fixture:paid-choice-1", "fixture:paid-choice-2"]));
        }
    }

    private sealed class RewardPackage : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-program-actor-choice-ui";
        public PackageManifest Manifest { get; } = new("fixture:program-actor-choice-ui", new(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            var driver = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"fixture:lb-driver","revision":1,"activations":[
                    {"id":"hurt","usesPerTurn":null,"minCards":0,"maxCards":0,"minTargets":0,
                     "maxTargets":0,"targetKind":"anyLiving","effects":[
                       {"op":"loseHp","target":"owner","amount":1}]}]}]}
                """, """
                {"schemaVersion":3,"skills":{"fixture:lb-driver":{"name":"驱动","description":"真实损失"}}}
                """);
            builder.AddSkill(new("fixture:lb-driver", "驱动", "真实损失")
            {
                Program = driver.Programs["fixture:lb-driver"]
            });
            builder.AddGeneral(new("fixture:lb-owner", "固定主公", "supporter", "standard:none", "shu", 20,
                ["boundary:jijiang", "fixture:lb-driver"]));
            for (var index = 1; index < 4; index++)
                builder.AddGeneral(new($"fixture:lb-target-{index}", "固定角色", "supporter", "standard:none",
                    "shu", 20, ["boundary:jijiang"]));
            builder.AddDeck(new("fixture:lb-deck", "固定实际响应牌库", 6, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard(
                    index % 2 == 0 ? "standard:duel" : "standard:slash", Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(ModeId, "实际响应选择界面", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:lb-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:lb-owner", "fixture:lb-target-1", "fixture:lb-target-2", "fixture:lb-target-3"]));
        }
    }
}
