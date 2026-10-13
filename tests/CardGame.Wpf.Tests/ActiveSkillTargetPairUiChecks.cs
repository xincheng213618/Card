using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ActiveSkillTargetPairUiChecks
{
    private const string Skill = "boundary:dimeng-current", Activation = "swap-first";
    private const string ExchangeReason = "skill-program." + Skill + ".exchange";
    private const string DebtReason = "skill-program." + Skill + ".MoveBoundCards";

    public static void Run()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var original = Create(registry);
        var checkpoint = original.CreateCheckpoint();
        var originalView = original.CreateSnapshot(0);
        var low = originalView.Players.Single(player => player.Seat != 0 && player.HandCount == 1).Seat;
        var bridge = originalView.Players.Single(player => player.Seat != 0 && player.HandCount == 2).Seat;
        var high = originalView.Players.Single(player => player.Seat != 0 && player.HandCount == 3).Seat;
        var union = new[] { low, bridge, high };
        var unchanged = Boundary(original);
        foreach (var targets in new[] { new[] { low, high }, new[] { high, low } })
        {
            var rejected = GameReplay.Restore(checkpoint, registry);
            var result = rejected.Submit(Command(rejected, targets));
            Program.Assert(!result.Accepted && result.Error is { Code: CommandErrorCode.InvalidTarget } &&
                Boundary(rejected) == unchanged,
                "Both orders of the actual one/three-hand pair must be rejected natively before any command, exchange, debt or safe view changes.");
        }

        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, checkpoint));
        using var model = new MainViewModel(false, 31, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            var game = Program.Engine(model);
            Program.Assert(!model.HasSaveError && Boundary(game) == unchanged,
                "The actual WPF manual load must restore the original native one-HE/three-peer boundary.");
            var action = Action();
            Program.Assert(action.MinCardCount == 0 && action.MaxCardCount == 0 && action.MinTargetCount == 2 &&
                action.MaxTargetCount == 2 && action.SelectableTargetSeats.ToHashSet().SetEquals(union),
                "The formal Dimeng action must publish zero activation materials and the union of all three individually payable recipients.");
            var expectedSequences = new[] { new[] { low, bridge }, new[] { bridge, low },
                new[] { bridge, high }, new[] { high, bridge } };
            Program.Assert(action.SelectableTargetSequences is { Count: 4 } sequences &&
                expectedSequences.All(expected => sequences.Any(sequence => sequence.SequenceEqual(expected))) &&
                sequences is ICollection<IReadOnlyList<int>> { IsReadOnly: true } &&
                sequences.All(sequence => sequence is ICollection<int> { IsReadOnly: true }),
                "The public action must freeze every complete native ordered pair, without either one/three-hand combination.");

            Begin();
            AssertUnion();
            Program.Assert(model.ActionHint.Contains("目标须组成合法组合", StringComparison.Ordinal) &&
                model.ActionHint.Contains("点已选目标可取消", StringComparison.Ordinal),
                "The active hint must explain the published pair constraint and its target cancellation path.");
            Select(low);
            AssertSelected(low);
            Console.WriteLine($"Target-pair Dimeng: ownHE=1, low={low}/1, bridge={bridge}/2, high={high}/3, " +
                $"union={string.Join(',', action.SelectableTargetSeats)}, afterLowLegal=" +
                $"{string.Join(',', model.Seats.Where(seat => seat.IsLegalTarget).Select(seat => seat.Seat))}, " +
                $"canConfirm={model.CanConfirmActiveSkill}, buttonEnabled={Confirm().IsEnabled}.");
            Program.Assert(Seat(bridge).IsLegalTarget && !Seat(high).IsLegalTarget &&
                !model.CanConfirmActiveSkill && !Confirm().IsEnabled,
                "Selecting the real one-card recipient must retain its two-card partner and stop highlighting the unpaid three-card partner.");
            var lowDraft = Draft(model);
            Select(high);
            Program.Assert(Draft(model) == lowDraft && Boundary(game) == unchanged &&
                !model.CanConfirmSelected && !Confirm().IsEnabled &&
                !Confirm().Command!.CanExecute(Confirm().CommandParameter),
                "The actual incompatible portrait cannot be added to the current first-target draft or enable the bound confirmation.");
            Program.Assert(!TableShortcutChecks.Input(window, Key.Enter) && Draft(model) == lowDraft && Boundary(game) == unchanged,
                "Enter must preserve the original single-target draft without committing an invalid pair or paying any phase debt.");
            Select(bridge);
            AssertSelected(low, bridge);
            Program.Assert(model.CanConfirmActiveSkill && model.CanConfirmSelected && Confirm().IsEnabled && Boundary(game) == unchanged,
                "The compatible bridge recipient must prepare a legal two-target draft without exchanging either actual hand early.");

            Select(low);
            AssertSelected(bridge);
            Program.Assert(Seat(low).IsLegalTarget && Seat(high).IsLegalTarget && !model.CanConfirmSelected && !Confirm().IsEnabled,
                "Cancelling one target must recompute both real partners of the remaining bridge, including the previously blocked high recipient.");
            Select(high);
            AssertSelected(bridge, high);
            Program.Assert(model.CanConfirmSelected && Confirm().IsEnabled && Boundary(game) == unchanged,
                "The corrected two/three-hand pair must enable confirmation without early cost or exchange.");
            Select(bridge);
            AssertSelected(high);
            Program.Assert(Seat(bridge).IsLegalTarget && !Seat(low).IsLegalTarget && !model.CanConfirmSelected,
                "Removing the bridge must narrow the remaining three-card target back to its actual two-card partner.");
            var highDraft = Draft(model);
            Select(low);
            Program.Assert(Draft(model) == highDraft && Boundary(game) == unchanged &&
                !TableShortcutChecks.Input(window, Key.Enter),
                "The reverse invalid selection order must also preserve its current single target and never submit.");
            Select(high);
            AssertUnion();
            Program.Assert(!model.CanConfirmSelected && !Confirm().IsEnabled && Boundary(game) == unchanged,
                "Cancelling the last target must restore the native union while keeping the empty draft uncommitted.");
            Escape();

            foreach (var targets in new[] { new[] { low, bridge }, new[] { bridge, low },
                         new[] { bridge, high }, new[] { high, bridge } })
            {
                foreach (var route in new[] { "button", "Enter" })
                {
                    model.LoadManualGameCommand.Execute(null);
                    game = Program.Engine(model);
                    Program.Assert(!model.HasSaveError && !model.HasSelection && Boundary(game) == unchanged,
                        "Every ordered confirmation route must reload the same actual checkpoint, without retaining a previous pair or debt.");
                    var movements = game.CardMovements.Count;
                    var events = game.Events.Count;
                    var firstCards = OwnHand(game, targets[0]);
                    var secondCards = OwnHand(game, targets[1]);
                    var ownerCards = OwnHand(game, 0);
                    var untouchedSeat = union.Single(seat => !targets.Contains(seat));
                    var untouchedCards = OwnHand(game, untouchedSeat);
                    var prompt = game.CreateSnapshot(0).PendingDecision!.PromptId;
                    Begin();
                    Select(targets[0]);
                    Program.Assert(Seat(targets[1]).IsLegalTarget,
                        "Each actual first recipient must expose its original payable ordered partner.");
                    Select(targets[1]);
                    AssertSelected(targets);
                    Program.Assert(model.CanConfirmActiveSkill && model.CanConfirmSelected && Confirm().IsEnabled &&
                        Confirm().Visibility == Visibility.Visible && Confirm().Command!.CanExecute(Confirm().CommandParameter) &&
                        Boundary(game) == unchanged && !game.Events.Skip(events).Any(item => item.Payload is PhaseHandExchangeDebtIssuedEvent),
                        "The real compatible pair must enable the actual button before any exchange, debt issuance, or owner payment.");

                    var reference = GameReplay.Restore(checkpoint, registry);
                    Accept(reference, Command(reference, targets));
                    Console.WriteLine($"Target-pair Dimeng/{route}: orderedTargets={string.Join(',', targets)}, " +
                        $"actualCounts={firstCards.Length},{secondCards.Length}, canConfirm={model.CanConfirmActiveSkill}, " +
                        $"buttonEnabled={Confirm().IsEnabled}.");
                    if (route == "button") Click(Confirm());
                    else Program.Assert(TableShortcutChecks.Input(window, Key.Enter),
                        "Enter must commit the exact current published two-target draft.");
                    Program.Assert(game.Revision == checkpoint.Revision + 1 &&
                        game.AcceptedCommands.Count == checkpoint.Commands.Count + 1 &&
                        game.AcceptedCommands.Last() is UseProgramSkillCommand submitted && submitted.ActorSeat == 0 &&
                        submitted.SkillId == Skill && submitted.ActivationId == Activation && submitted.CardIds.Count == 0 &&
                        submitted.TargetSeats.SequenceEqual(targets) && submitted.ExpectedRevision == checkpoint.Revision &&
                        submitted.PromptId == prompt && !model.HasSelection && !model.IsActiveSkillSelectionPending &&
                        Boundary(game) == Boundary(reference),
                        "Button/Enter must commit once, preserve the actual selection order, and match the independent native command in every safe view and record.");
                    AssertCold(game, registry);
                    for (var step = 0; step < 16 && game.CreateSnapshot(0).PendingDecision is not
                             { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
                    {
                        Program.Assert(model.CanStepAi, "The native exchange continuation must be advanceable to its original human Play boundary.");
                        Accept(reference, new AdvanceOneStepCommand(reference.Revision));
                        model.StepAiCommand.Execute(null);
                        Program.Assert(Boundary(game) == Boundary(reference),
                            "Every bounded typed exchange continuation must agree with the independent native execution.");
                    }
                    Program.Assert(game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                        OwnHand(game, targets[0]).SequenceEqual(secondCards) && OwnHand(game, targets[1]).SequenceEqual(firstCards) &&
                        OwnHand(game, 0).SequenceEqual(ownerCards) && OwnHand(game, untouchedSeat).SequenceEqual(untouchedCards),
                        "Only the two original actual hands must exchange; the owner and third peer keep all their entities when human Play returns.");
                    var exchanged = game.CardMovements.Skip(movements).ToArray();
                    Program.Assert(exchanged.Length == 2 * (firstCards.Length + secondCards.Length) &&
                        exchanged.All(move => move.Reason.Value == ExchangeReason) &&
                        firstCards.All(id => Paid(id, targets[0], targets[1])) &&
                        secondCards.All(id => Paid(id, targets[1], targets[0])) &&
                        game.Events.Skip(events).Select(item => item.Payload).OfType<PhaseHandExchangeDebtIssuedEvent>().ToArray() is [var debt] &&
                        debt.Source.OwnerSeat == 0 && debt.Source.SkillId == Skill && debt.FirstSeat == targets[0] &&
                        debt.SecondSeat == targets[1] && debt.FirstCount == firstCards.Length && debt.SecondCount == secondCards.Length &&
                        debt.FrozenDifference == 1 && debt.TurnOwnerSeat == 0 &&
                        !game.Events.Skip(events).Any(item => item.Payload is PhaseHandExchangeDebtPaymentStartedEvent) &&
                        !game.CardMovements.Skip(movements).Any(move => move.Reason.Value == DebtReason) &&
                        game.AcceptedCommands.Skip(checkpoint.Commands.Count).OfType<UseProgramSkillCommand>().Count() == 1,
                        "The two ordered real hands must move exactly once through Processing, issue one frozen difference-one debt, and never pay the owner's Ending cost during Play.");
                    AssertCold(game, registry);

                    bool Paid(int id, int from, int to) => exchanged.Count(move => move.CardId == id &&
                        move.From == CardLocation.Hand(from) && move.To == CardLocation.Processing && move.Reason.Value == ExchangeReason) == 1 &&
                        exchanged.Count(move => move.CardId == id && move.From == CardLocation.Processing &&
                            move.To == CardLocation.Hand(to) && move.Reason.Value == ExchangeReason) == 1;
                }
            }

            LegalAction Action() => model.HumanActiveSkillActions.Single(candidate =>
                candidate.ProgramSkillId == Skill && candidate.ProgramActivationId == Activation);
            SeatViewModel Seat(int seat) => model.Seats.Single(item => item.Seat == seat);
            Button Confirm() => (Button)window.FindName("PlayCardButton");
            void Begin() { model.SelectActiveSkillCommand.Execute(Action()); Layout(root); }
            void Select(int seat) { Click(BoundButton(root, model.SelectTargetCommand, Seat(seat))); Layout(root); }
            void AssertSelected(params int[] targets)
            {
                Program.Assert(model.IsActiveSkillSelectionPending && model.HasSelection &&
                    model.Hand.All(card => !card.IsSelected) &&
                    model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToHashSet().SetEquals(targets) &&
                    Boundary(game) == unchanged,
                    "The bound portraits must preserve exactly the intended target membership with no physical fee, native command or payment.");
            }
            void AssertUnion()
            {
                Program.Assert(model.IsActiveSkillSelectionPending && model.Seats.All(seat => !seat.IsSelectedTarget) &&
                    model.Hand.All(card => !card.IsSelected) &&
                    model.Seats.Where(seat => seat.IsLegalTarget).Select(seat => seat.Seat).ToHashSet().SetEquals(union),
                    "Without a target prefix, all and only the actual native payable target union must be highlighted.");
            }
            void Escape()
            {
                Program.Assert(TableShortcutChecks.Input(window, Key.Escape) && !model.HasSelection &&
                    !model.IsActiveSkillSelectionPending && model.Seats.All(seat => !seat.IsSelectedTarget) &&
                    model.Hand.All(card => !card.IsSelected) && Boundary(game) == unchanged,
                    "Esc must cancel only the pair draft, preserving every original hand, native prompt, event, movement and safe view.");
                Layout(root);
            }
        }
        finally { window.Content = null; window.Close(); }
    }

    private static GameEngine Create(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Fixture.Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(game, new StartGameCommand());
        Program.Assert(game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 },
            "The pair fixture must enter through native general selection.");
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId));
        for (var step = 0; step < 40 && game.CreateSnapshot(0).PendingDecision is not
                 { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        var view = game.CreateSnapshot(0);
        Program.Assert(view.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
            view.Players[0].HandCount == 1 && view.Players[0].Equipment.Count == 0 &&
            view.Players.Skip(1).Select(player => player.HandCount).Order().SequenceEqual([1, 2, 3]) &&
            view.Players.All(player => player.IsAlive) &&
            view.Players.All(player => registry.Generals.ContainsKey(player.GeneralId)) &&
            game.GetHumanLegalActions().Single(action => action.ProgramSkillId == Skill && action.ProgramActivationId == Activation)
                .SelectableTargetSeats.ToHashSet().SetEquals(view.Players.Skip(1).Select(player => player.Seat)),
            "Four native holder-scoped initial-hand modifiers must prepare owner HE=1 and real other hands 1/2/3, leaving all three peers in the action union.");
        foreach (var seat in Enumerable.Range(0, 4))
        {
            var safe = game.CreateSnapshot(seat);
            Program.Assert(safe.Players.Select(player => player.HandCount).SequenceEqual(view.Players.Select(player => player.HandCount)) &&
                safe.Players.Where(player => player.Seat != seat).All(player => player.Hand.Count == 0) &&
                safe.Players[seat].Hand.Count == safe.Players[seat].HandCount,
                "Each player-safe view must expose the same actual counts and only its own physical hand faces.");
        }
        return game;
    }

    private static UseProgramSkillCommand Command(GameEngine game, IReadOnlyList<int> targets) =>
        new(0, Skill, Activation, [], targets, game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId);
    private static int[] OwnHand(GameEngine game, int seat) =>
        game.CreateSnapshot(seat).Players[seat].Hand.Select(card => card.Id).Order().ToArray();
    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, Commands = CommandJson.Serialize(game.AcceptedCommands), Movements = game.CardMovements,
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray()
    });
    private static string Draft(MainViewModel model) => JsonSerializer.Serialize(new
    {
        model.HasSelection, model.IsActiveSkillSelectionPending, model.CanConfirmActiveSkill, model.CanConfirmSelected,
        model.SelectedCardText, model.ActionHint,
        Cards = model.Hand.Where(card => card.IsSelected).Select(card => card.Id).ToArray(),
        Targets = model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToArray(),
        Legal = model.Seats.Where(seat => seat.IsLegalTarget).Select(seat => seat.Seat).ToArray()
    });
    private static void AssertCold(GameEngine game, ContentRegistry registry)
    {
        var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Program.Assert(Boundary(cold) == Boundary(game),
            "Cold restore must retain every safe view, exact ordered command, exchange entity movement and typed phase-debt continuation.");
    }
    private static Button BoundButton(FrameworkElement root, ICommand command, object parameter) =>
        Program.Find<Button>(root).Single(button => ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, parameter));
    private static void Click(Button button) => typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(button, null);
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "A native target-pair fixture command was rejected.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Owner = "fixture:target-pair-owner", Mode = "identity:classic-ui-target-pair";
        private static string Initial(int count) => $"fixture:target-pair-initial-{count}";
        public PackageManifest Manifest { get; } = new("fixture-ui-target-pair", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.RulesSchemaVersion,
                skills = Enumerable.Range(1, 3).Select(count => new
                {
                    id = Initial(count), revision = 1,
                    modifiers = new[] { new { id = "real-holder-initial-hand", query = "initialHandSize", operation = "add",
                        value = count, priority = 0, condition = new { kind = "always" } } }
                }).ToArray()
            }), JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = Enumerable.Range(1, 3).ToDictionary(Initial,
                    count => new { name = $"真实起手{count}张", description = "仅持有者通过原生发牌取得固定数量的实体牌" })
            }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Owner, "缔盟目标关联夹具", "supporter", Skill, "wu", 4, [Initial(1)], GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(count => $"fixture:target-pair-peer-{count}").ToArray();
            for (var index = 0; index < peers.Length; index++)
                builder.AddGeneral(new(peers[index], $"实际手牌{index + 1}张", "supporter", Initial(index + 1), "shu", 4,
                    Gender: GeneralGender.Male));
            const string deck = "fixture:target-pair-deck";
            builder.AddDeck(new(deck, "固定十二张实际响应牌", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 12).Select(index => new ContentDeckPhysicalCard("standard:dodge",
                new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[index % 4], index + 1)).ToArray() });
            builder.AddMode(new(Mode, "原生两目标关联", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
