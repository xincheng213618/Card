using System.IO;
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

internal static class ActiveSkillMatchingCostUiChecks
{
    private const string Skill = "ol:jianhe";
    private enum Materials { Mixed, Canonical, Equipment }

    public static void Run()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        foreach (var branch in Enum.GetValues<Materials>())
        {
            var original = Create(registry, branch);
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow,
                false, original.CreateCheckpoint()));
            using var model = new MainViewModel(false, 31, true, store, contentRegistry: registry)
                { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            var window = new MainWindow(model);
            var root = (FrameworkElement)window.Content;
            try
            {
                foreach (var route in new[] { "button", "Enter" })
                {
                    model.LoadManualGameCommand.Execute(null);
                    var game = Program.Engine(model);
                    Program.Assert(!model.HasSaveError && game.CreateSnapshot(0).PendingDecision is
                        { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
                        "Each matching-cost control must start from the same real saved Play boundary.");
                    var action = model.HumanActiveSkillActions.Single(item => item.ProgramSkillId == Skill &&
                        item.ProgramActivationId == "jianhe");
                    var human = game.CreateSnapshot(0).Players[0];
                    var available = human.Hand.Concat(human.Equipment).ToArray();
                    Program.Assert(action.SelectedCardsSameNameOrAllEquipment &&
                        action.ProgramAiHint?.MatchingNameOrEquipmentRecastInput == true &&
                        action.MinCardCount == 2 && action.MaxCardCount >= 3 &&
                        action.SelectableTargetSeats.Contains(0) && !action.SelectedCardsSameSuit &&
                        !action.SelectedCardsDistinctSuits,
                        "The actual Jianhe action must publish its uncapped HE union and a legal self target, without substituting a suit constraint.");
                    var before = game.CreateCheckpoint();
                    var promptId = game.CreateSnapshot(0).PendingDecision!.PromptId;
                    var beforeMovements = game.CardMovements.Count;
                    var unchanged = Boundary(game);
                    var valid = branch switch
                    {
                        Materials.Canonical => new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash }
                            .Select(kind => human.Hand.First(card => card.Kind == kind).Id).Order().ToArray(),
                        Materials.Equipment => new[] { human.Equipment.Single().Id,
                            human.Hand.First(card => card.Kind == CardKind.BaguaFormation).Id }.Order().ToArray(),
                        _ => human.Hand.Where(card => card.Kind == CardKind.Slash).Take(2)
                            .Select(card => card.Id).Order().ToArray()
                    };
                    Program.Assert(valid.Length == (branch == Materials.Canonical ? 3 : 2) &&
                        valid.All(action.SelectableCardIds.Contains),
                        "Every real positive material must belong to the current native published cost union.");

                    if (branch == Materials.Mixed)
                    {
                        var invalid = new[] { human.Hand.First(card => card.Kind == CardKind.Slash).Id,
                            human.Hand.First(card => card.Kind == CardKind.Duel).Id }.Order().ToArray();
                        Program.Assert(new[] { CardKind.Slash, CardKind.Duel }.All(kind =>
                            human.Hand.Count(card => card.Kind == kind && action.SelectableCardIds.Contains(card.Id)) >= 2) &&
                            invalid.All(action.SelectableCardIds.Contains),
                            "Both mixed-name entities must have real same-name partners and be independently selectable in the native union.");
                        var rejected = GameReplay.Restore(before, registry);
                        var rejection = rejected.Submit(Command(rejected, invalid));
                        Program.Assert(!rejection.Accepted && rejection.Error is { Code: CommandErrorCode.InvalidCard } error &&
                            error.Message.Contains("same-name", StringComparison.Ordinal) &&
                            Boundary(rejected) == unchanged,
                            "The native mixed-name boundary must reject the exact group without payment, commands, events or player-view changes.");
                        Select(invalid);
                        Layout(root);
                        var confirm = (Button)window.FindName("PlayCardButton");
                        Console.WriteLine($"Jianhe {branch}/{route}: ids={string.Join(',', invalid)}, " +
                            $"kinds={string.Join(',', invalid.Select(id => available.Single(card => card.Id == id).Kind))}, " +
                            $"published={action.SelectableCardIds.Count}, canConfirm={model.CanConfirmActiveSkill}, buttonEnabled={confirm.IsEnabled}.");
                        AssertDraft(invalid);
                        Program.Assert(!model.CanConfirmActiveSkill && !model.CanConfirmSelected && !confirm.IsEnabled &&
                            !confirm.Command!.CanExecute(confirm.CommandParameter),
                            "A mixed-name Jianhe group must keep the actual primary confirmation disabled despite satisfying counts and individual candidates.");
                        Program.Assert(!TableShortcutChecks.Input(window, Key.Enter) && Boundary(game) == unchanged,
                            "Enter must not submit or pay the invalid mixed-name group.");
                        AssertDraft(invalid);
                        Program.Assert(TableShortcutChecks.Input(window, Key.Escape) && !model.HasSelection &&
                            !model.IsActiveSkillSelectionPending && model.Hand.All(card => !card.IsSelected) &&
                            model.Seats.All(seat => !seat.IsSelectedTarget) && Boundary(game) == unchanged,
                            "Esc must clear only the mixed-name draft while retaining the exact native prompt and physical cards.");
                    }

                    Select(valid);
                    Layout(root);
                    AssertDraft(valid);
                    var button = (Button)window.FindName("PlayCardButton");
                    Console.WriteLine($"Jianhe {branch}/{route}: valid ids={string.Join(',', valid)}, " +
                        $"origins={string.Join(',', valid.Select(id => human.Equipment.Any(card => card.Id == id) ? "Equipment" : "Hand"))}, " +
                        $"canConfirm={model.CanConfirmActiveSkill}, buttonEnabled={button.IsEnabled}.");
                    Program.Assert(model.CanConfirmActiveSkill && model.CanConfirmSelected && button.IsEnabled &&
                        button.Visibility == Visibility.Visible && button.Command!.CanExecute(button.CommandParameter) &&
                        Boundary(game) == unchanged,
                        "Canonical Slash names and differently named equipment must enable the real confirmation without paying early.");
                    var reference = GameReplay.Restore(before, registry);
                    Accept(reference, Command(reference, valid));
                    if (route == "button") Click(button);
                    else Program.Assert(TableShortcutChecks.Input(window, Key.Enter),
                        "Enter must confirm the current legal matching-cost draft.");
                    var submitted = game.AcceptedCommands.Last() as UseProgramSkillCommand;
                    Program.Assert(game.Revision == before.Revision + 1 &&
                        game.AcceptedCommands.Count == before.Commands.Count + 1 && submitted is not null &&
                        submitted.ActorSeat == 0 && submitted.SkillId == Skill && submitted.ActivationId == "jianhe" &&
                        submitted.CardIds.SequenceEqual(valid) && submitted.TargetSeats.SequenceEqual([0]) &&
                        submitted.ExpectedRevision == before.Revision && submitted.PromptId == promptId &&
                        !model.HasSelection && !model.IsActiveSkillSelectionPending && Boundary(game) == Boundary(reference),
                        "The actual button/key must commit exactly one current native Jianhe command and preserve all four reference views.");
                    var paid = game.CardMovements.Skip(beforeMovements).Where(move => valid.Contains(move.CardId) &&
                        move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.RecastDiscard).ToArray();
                    Program.Assert(paid.Length == valid.Length && valid.All(id => paid.Count(move => move.CardId == id &&
                            move.From == (human.Equipment.Any(card => card.Id == id) ? CardLocation.Equipment(0) : CardLocation.Hand(0))) == 1) &&
                        game.Events.Select(item => item.Payload).OfType<MatchingRecastStartedEvent>().Count() == 1 &&
                        game.Events.Select(item => item.Payload).OfType<MatchingRecastPaidEvent>().Count(item =>
                            item.Cursor == 0 && item.ParticipantSeat == 0 && valid.Contains(item.CardId)) == valid.Length,
                        "Each original hand/equipment entity must pay once from its exact real zone through native recast events.");
                    for (var step = 0; step < 16 && !IsPeerPrompt(game); step++)
                    {
                        Program.Assert(model.CanStepAi, "The native recast continuation must remain available until its real peer prompt.");
                        Accept(reference, new AdvanceOneStepCommand(reference.Revision));
                        model.StepAiCommand.Execute(null);
                        Program.Assert(Boundary(game) == Boundary(reference),
                            "Each bounded native continuation must retain identical commands, movements, events and safe player views.");
                    }
                    Program.Assert(IsPeerPrompt(game) && game.Events.Select(item => item.Payload)
                            .OfType<MatchingRecastDrawIssuedEvent>().Count(item => item.Cursor == 0 && item.ActualCount == valid.Length) == 1 &&
                        !game.Events.Select(item => item.Payload).OfType<MatchingRecastCompletedEvent>().Any(),
                        "The owner's exact payment and draw must return to its still-pending native peer choice, without simulating the rest of the match.");
                    var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                    Program.Assert(Boundary(cold) == Boundary(game),
                        "Cold restore must preserve all four safe views, physical payments, exact commands and the original native peer continuation.");

                    void Select(int[] ids)
                    {
                        model.SelectActiveSkillCommand.Execute(model.HumanActiveSkillActions.Single(item =>
                            item.ProgramSkillId == Skill && item.ProgramActivationId == "jianhe"));
                        Layout(root);
                        foreach (var id in ids)
                        {
                            if (model.Hand.SingleOrDefault(card => card.Id == id) is { } card)
                                Click(BoundButton(root, model.SelectCardCommand, card));
                            else
                                Click(BoundButton(root, model.SelectActiveSkillEquipmentChoiceCommand,
                                    model.ActiveSkillEquipmentChoices.Single(choice => choice.Cards.SequenceEqual([id]))));
                            Layout(root);
                        }
                        Click(BoundButton(root, model.SelectTargetCommand, model.Seats.Single(seat => seat.Seat == 0)));
                    }

                    void AssertDraft(int[] ids)
                    {
                        var expectedHand = ids.Where(id => human.Hand.Any(card => card.Id == id)).Order().ToArray();
                        var expectedEquipment = ids.Where(id => human.Equipment.Any(card => card.Id == id)).Order().ToArray();
                        Program.Assert(model.IsActiveSkillSelectionPending && model.HasSelection &&
                            model.ActionHint.Contains("费用牌须同名（杀含火杀、雷杀）或全部为装备", StringComparison.Ordinal) &&
                            model.Hand.Where(card => card.IsSelected).Select(card => card.Id).Order().SequenceEqual(expectedHand) &&
                            model.ActiveSkillEquipmentChoices.Where(choice => choice.Description.StartsWith("✓", StringComparison.Ordinal))
                                .SelectMany(choice => choice.Cards).Order().SequenceEqual(expectedEquipment) &&
                            model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).SequenceEqual([0]),
                            "The actual cost controls and portrait must retain precisely the prepared native physical materials and self target.");
                    }
                }
            }
            finally { window.Content = null; window.Close(); }
        }
    }

    private static GameEngine Create(ContentRegistry registry, Materials branch)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Fixture.Mode(branch),
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(game, new StartGameCommand());
        Program.Assert(game.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 },
            "The small matching-cost fixture must begin through native human selection.");
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId));
        ReachPlay();
        var hand = game.CreateSnapshot(0).Players[0].Hand;
        Program.Assert(hand.Count == 10 && game.CreateSnapshot(0).Players.Skip(1).All(player => player.HandCount == 0),
            "The native owner-only ten-card deal must leave two real deck cards, without fabricated snapshots or prompts.");
        if (branch == Materials.Equipment)
        {
            var card = hand.First(item => item.Kind == CardKind.Crossbow);
            var equip = game.GetHumanLegalActions().Single(action => action.CardId == card.Id && action.Kind == LegalActionKind.Equip);
            var moves = game.CardMovements.Count;
            var commands = game.AcceptedCommands.Count;
            Accept(game, new PlayCardCommand(0, card.Id, equip.TargetSeats, game.Revision, game.PendingDecision!.PromptId,
                equip.PlayedCardKind, equip.TargetCardId) { ConversionSource = equip.ConversionSource });
            ReachPlay();
            var payment = game.CardMovements.Skip(moves).Where(move => move.CardId == card.Id).ToArray();
            Program.Assert(game.AcceptedCommands.Skip(commands).OfType<PlayCardCommand>().Count() == 1 &&
                game.CreateSnapshot(0).Players[0].Equipment.Single().Id == card.Id && payment is [var use, var enter] &&
                use.From == CardLocation.Hand(0) && use.To == CardLocation.Processing && use.Reason == CardMoveReasons.EquipmentUse &&
                enter.From == CardLocation.Processing && enter.To == CardLocation.Equipment(0) && enter.Reason == CardMoveReasons.EquipmentEnter,
                "The equipment-cost precondition must genuinely use and equip one exact Crossbow before saving the UI boundary.");
        }
        return game;

        void ReachPlay()
        {
            for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            Program.Assert(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
                "The fixed small matching-cost fixture must reach its native human Play prompt.");
        }
    }

    private static UseProgramSkillCommand Command(GameEngine game, int[] cards) =>
        new(0, Skill, "jianhe", cards, [0], game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId);
    private static bool IsPeerPrompt(GameEngine game) => game.CreateSnapshot(0).PendingDecision is { PlayerSeat: 0 } prompt &&
        prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "matching-recast" &&
            choice.Parameters.GetValueOrDefault("step") == "damage");
    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, Commands = CommandJson.Serialize(game.AcceptedCommands), Movements = game.CardMovements,
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray()
    });
    private static Button BoundButton(FrameworkElement root, ICommand command, object parameter) =>
        Program.Find<Button>(root).Single(button => ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, parameter));
    private static void Click(Button button) => typeof(ButtonBase).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(button, null);
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "A native matching-cost fixture command was rejected.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Owner = "fixture:matching-cost-owner";
        internal static string Mode(Materials branch) => $"identity:classic-ui-matching-{branch.ToString().ToLowerInvariant()}";
        public PackageManifest Manifest { get; } = new("fixture-ui-matching-cost", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-zhang-hua.rules.json")!);
            using var presentation = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-zhang-hua.presentation.json")!);
            var formal = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
            foreach (var (id, program) in formal.Programs)
                builder.AddSkill(new(id, formal.Presentations[id].Name, formal.Presentations[id].Description)
                    { Program = program, ProgramPresentation = formal.Presentations[id] });
            const string initial = "fixture:matching-cost-initial";
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.RulesSchemaVersion,
                skills = new[] { new { id = initial, revision = 1,
                    modifiers = new[] { new { id = "ten-real-owner-cards", query = "initialHandSize", operation = "add",
                        value = 10, priority = 0, condition = new { kind = "always" } } } } }
            }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [initial] = new { name = "固定实际起手", description = "仅持有者实际发十张实体牌" } } }));
            builder.AddSkill(new(initial, "固定实际起手", "仅持有者实际发十张实体牌")
                { Program = catalog.Programs[initial], ProgramPresentation = catalog.Presentations[initial] });
            builder.AddGeneral(new(Owner, "剑合费用夹具", "supporter", Skill, "jin", 6, [initial], GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:matching-cost-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "其他角色", "supporter", "standard:none", "shu", 6));
            foreach (var branch in Enum.GetValues<Materials>())
            {
                var names = branch switch
                {
                    Materials.Canonical => new[] { "standard:slash", "standard:fire_slash", "standard:thunder_slash" },
                    Materials.Equipment => new[] { "standard:crossbow", "standard:bagua" },
                    _ => new[] { "standard:slash", "standard:duel" }
                };
                var deck = $"fixture:matching-cost-deck-{branch.ToString().ToLowerInvariant()}";
                builder.AddDeck(new(deck, "固定十二张实体费用", 0, 0, [])
                { PhysicalCards = Enumerable.Range(0, 12).Select(i => new ContentDeckPhysicalCard(names[i * names.Length / 12],
                    new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i % 4], i + 1)).ToArray() });
                builder.AddMode(new(Mode(branch), "剑合原生费用", 4, 4, new Dictionary<string, int>
                    { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, deck,
                    GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
            }
        }
    }
}
