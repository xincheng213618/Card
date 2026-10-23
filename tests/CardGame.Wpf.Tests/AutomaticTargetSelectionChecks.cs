using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class AutomaticTargetSelectionChecks
{
    public static void CardAndConversionDraftsSelectOnlyUniqueTargets()
    {
        foreach (var scenario in new[]
        {
            new Scenario("standard:slash"), new Scenario("standard:fire_slash"),
            new Scenario("standard:thunder_slash"), new Scenario("standard:snatch"),
            new Scenario("standard:dismantlement"), new Scenario("standard:fire_attack"),
            new Scenario("standard:duel"), new Scenario("standard:iron_chain", Conversion: "standard:wusheng"),
            new Scenario("standard:peach", Conversion: "classic:huoji"),
            new Scenario("standard:slash", Seats: 4),
            new Scenario("standard:duel", Seats: 4), new Scenario("standard:iron_chain")
        })
        {
            var (model, registry) = Create(scenario);
            using (model)
            {
                var window = new MainWindow(model);
                var root = (FrameworkElement)window.Content;
                try
                {
                    var game = Program.Engine(model);
                    var before = game.CreateCheckpoint();
                    var unchanged = Boundary(game);
                    if (scenario.Conversion is { } skill)
                        model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(item => item.ContentId == skill));
                    var action = game.GetHumanLegalActions().First(item => item.CardId.HasValue &&
                        item.TargetSeats.Count == 1 && item.TargetSeats[0] != 0 &&
                        item.ConversionSource?.SkillId == scenario.Conversion);
                    var card = model.Hand.Single(item => item.Id == action.CardId);
                    Layout(root);
                    Click(Bound(root, model.SelectCardCommand, card));
                    Layout(root);
                    var selected = model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToArray();
                    var ambiguous = scenario.Seats > 2 || scenario.Card == "standard:iron_chain" && scenario.Conversion is null;
                    if (ambiguous)
                    {
                        Program.Assert(selected.Length == 0 && !model.CanConfirmSelected && Boundary(game) == unchanged,
                            "Multiple legal recipients and an optional multi-target/recast card must keep their target choice explicit.");
                        continue;
                    }

                    var concreteCardPick = action.Kind is LegalActionKind.Dismantlement or LegalActionKind.Snatch;
                    Program.Assert(selected.SequenceEqual(action.TargetSeats) && card.IsSelected &&
                        model.CanConfirmSelected == !concreteCardPick && model.SelectionArea.CanConfirm == !concreteCardPick &&
                        ((Button)window.FindName("PlayCardButton")).IsEnabled == !concreteCardPick && Boundary(game) == unchanged,
                        $"{scenario.Card}/{scenario.Conversion ?? "printed"} must draft its sole recipient and retain the required card choice without payment.");
                    if (scenario.Card == "standard:slash" && scenario.Conversion is null)
                    {
                        var portrait = model.Seats.Single(seat => seat.Seat == action.TargetSeats[0]);
                        Click(Bound(root, model.SelectTargetCommand, portrait));
                        Layout(root);
                        Program.Assert(model.Seats.All(seat => !seat.IsSelectedTarget) && !model.CanConfirmSelected &&
                            card.IsSelected && Boundary(game) == unchanged,
                            "Clicking the selected portrait must cancel it without a refresh immediately selecting it again.");
                        Click(Bound(root, model.SelectTargetCommand, portrait));
                        Program.Assert(portrait.IsSelectedTarget && model.CanConfirmSelected && Boundary(game) == unchanged,
                            "The same portrait must remain available for explicit reselection without paying the physical Slash.");
                    }
                    model.ClearSelectionCommand.Execute(null);
                    Program.Assert(!model.HasSelection && model.Seats.All(seat => !seat.IsSelectedTarget) && Boundary(game) == unchanged,
                        "Cancel must remove the automatic target and leave the actual card, prompt and every committed view unchanged.");
                    if (scenario.Conversion is { } conversion)
                        model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(item => item.ContentId == conversion));
                    model.SelectCardCommand.Execute(model.Hand.Single(item => item.Id == action.CardId));
                    Layout(root);
                    var reference = GameReplay.Restore(before, registry);
                    var command = new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision,
                        game.CreateSnapshot(0).PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId)
                    { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources,
                        TargetHandSlot = concreteCardPick ? 0 : null };
                    Accept(reference, command);
                    if (concreteCardPick)
                        model.SelectPublicTargetChoiceCommand.Execute(model.PublicTargetChoices.Single(choice => choice.Parameters.GetValueOrDefault("target-hand-slot") == "0"));
                    else Click((Button)window.FindName("PlayCardButton"));
                    Program.Assert(game.Revision == before.Revision + 1 &&
                        CommandJson.Serialize([game.AcceptedCommands.Last()]) == CommandJson.Serialize([command]) &&
                        Boundary(game) == Boundary(reference),
                        "The real confirm button must submit the exact native auto-targeted action once; selection alone never commits it.");
                }
                finally { window.Content = null; window.Close(); }
            }
        }
    }

    public static void ActiveSkillDraftsFollowMaterialLegality()
    {
        var (model, registry) = Create(new("standard:crossbow", Seats: 4, Placement: true));
        using (model)
        {
            var window = new MainWindow(model);
            var root = (FrameworkElement)window.Content;
            try
            {
                var game = Program.Engine(model);
                var before = game.CreateCheckpoint();
                var unchanged = Boundary(game);
                var action = model.HumanActiveSkillActions.Single(item => item.ProgramSkillId == "boundary:zhijian" &&
                    item.ProgramActivationId == "place-equipment");
                var armor = model.Hand.First(card => card.Kind == CardKind.BaguaFormation);
                var weapon = model.Hand.First(card => card.Kind == CardKind.Crossbow);
                var armorTargets = action.SelectableCardTargets!.Where(pair => pair.CardId == armor.Id)
                    .Select(pair => pair.TargetSeat).Distinct().ToArray();
                Program.Assert(action.SelectableTargetSeats.Count == 3 && armorTargets.Length == 1,
                    "The native placement fixture must publish three recipients before payment selection and exactly one compatible armor recipient.");

                model.SelectActiveSkillCommand.Execute(action);
                Layout(root);
                Program.Assert(model.Seats.All(seat => !seat.IsSelectedTarget) && !model.CanConfirmSelected && Boundary(game) == unchanged,
                    "A three-recipient active skill must keep targets unselected while its physical material is undecided.");
                model.SelectCardCommand.Execute(weapon);
                Program.Assert(model.Seats.All(seat => !seat.IsSelectedTarget) && !model.CanConfirmSelected && Boundary(game) == unchanged,
                    "A weapon payable to several recipients must keep the recipient choice explicit.");
                model.ClearSelectionCommand.Execute(null);
                model.SelectActiveSkillCommand.Execute(model.HumanActiveSkillActions.Single(item => item.ProgramSkillId == "boundary:zhijian"));
                Click(Bound(root, model.SelectCardCommand, armor));
                Layout(root);
                Program.Assert(model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).SequenceEqual(armorTargets) &&
                    model.CanConfirmActiveSkill && model.CanConfirmSelected && Boundary(game) == unchanged,
                    "Selecting armor must recalculate material-dependent legality and draft the sole compatible recipient without moving or paying that equipment.");
                var reference = GameReplay.Restore(before, registry);
                var command = new UseProgramSkillCommand(0, "boundary:zhijian", "place-equipment", [armor.Id], armorTargets,
                    game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId);
                Accept(reference, command);
                Click((Button)window.FindName("PlayCardButton"));
                Program.Assert(game.Revision == before.Revision + 1 &&
                    CommandJson.Serialize([game.AcceptedCommands.Last()]) == CommandJson.Serialize([command]) &&
                    Boundary(game) == Boundary(reference),
                    "Only explicit confirmation may pay the material and place it on its exact automatically drafted recipient.");
            }
            finally { window.Content = null; window.Close(); }
        }
        UniquePendingTargetKeepsExplicitConfirmation();
    }

    private static void UniquePendingTargetKeepsExplicitConfirmation()
    {
        var (model, registry) = Create(new("standard:slash", Pending: true));
        using (model)
        {
            var game = Program.Engine(model);
            var before = game.CreateCheckpoint();
            var unchanged = Boundary(game);
            var prompt = game.CreateSnapshot(0).PendingDecision!;
            var choice = prompt.Choices.Single(item => item.Targets.Count == 1);
            var window = new MainWindow(model);
            try
            {
                Layout((FrameworkElement)window.Content);
                Program.Assert(model.IsSkillTargetSelectionPending &&
                    model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).SequenceEqual(choice.Targets) &&
                    model.CanConfirmSelected && Boundary(game) == unchanged,
                    "A new required single-recipient skill prompt must draft its only native recipient without answering or drawing its promised reward.");
                var command = new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision);
                var reference = GameReplay.Restore(before, registry);
                Accept(reference, command);
                Click((Button)window.FindName("PlayCardButton"));
                Program.Assert(game.Revision == before.Revision + 1 &&
                    CommandJson.Serialize([game.AcceptedCommands.Last()]) == CommandJson.Serialize([command]) &&
                    Boundary(game) == Boundary(reference),
                    "The auto-drafted skill recipient must be answered only by the actual confirmation button with the exact native choice.");
            }
            finally { window.Content = null; window.Close(); }
        }
    }

    private static (MainViewModel, ContentRegistry) Create(Scenario scenario)
    {
        var registry = scenario.Placement
            ? ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(scenario))
            : ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.Mode, UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Program.Assert(game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } general &&
            general.ValidContentIds.Contains(Fixture.Owner), "The small native fixture must expose its real human general choice.");
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId));
        bool ReachedBoundary() => scenario.Pending
            ? game.CreateSnapshot(0).PendingDecision?.SkillPrompt?.SkillId == Fixture.Pending
            : game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
        for (var step = 0; step < 60 && !ReachedBoundary(); step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Program.Assert(ReachedBoundary(), "The fixed native fixture must reach its real human decision boundary.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, 31, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        try
        {
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError && Boundary(Program.Engine(model)) == Boundary(game),
                "Actual manual load must preserve the native fixture's complete committed boundary: " + model.SaveStatus);
            return (model, registry);
        }
        catch { model.Dispose(); throw; }
    }

    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, Commands = CommandJson.Serialize(game.AcceptedCommands), Movements = game.CardMovements,
        Views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray()
    });
    private static Button Bound(FrameworkElement root, System.Windows.Input.ICommand command, object value) =>
        Program.Find<Button>(root).Single(button => ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, value));
    private static void Click(Button button)
    {
        Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible && button.Command!.CanExecute(button.CommandParameter),
            "The actual bound control must be enabled before its click.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Automatic-target native fixture command rejected.");
    }

    private sealed record Scenario(string Card, int Seats = 2, string? Conversion = null, bool Placement = false, bool Pending = false);
    private sealed class Fixture(Scenario scenario) : IGameContentPackage
    {
        internal const string Mode = "identity:classic-auto-target-ui", Owner = "fixture:auto-target-owner";
        internal const string Pending = "fixture:auto-target-pending";
        private const string Initial = "fixture:auto-target-initial", Partial = "fixture:auto-target-partial";
        public PackageManifest Manifest { get; } = new("fixture-auto-target-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var fireAttack = scenario.Card == "standard:fire_attack" || scenario.Conversion == "classic:huoji";
            if (!scenario.Placement && scenario.Seats == 2)
                CharacterFixtureContent.Register(builder, scenario.Conversion == "classic:huoji"
                    ? ["classic:zhuge-liang", "classic:wolong-zhuge-liang"] : ["classic:zhuge-liang"]);
            if (scenario.Pending)
            {
                var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new
                {
                    schemaVersion = SkillProgramCatalog.RulesSchemaVersion,
                    skills = new[] { new { id = Pending, revision = 1,
                        triggers = new[] { new { id = "one-native-recipient", window = "turnStartBeforeNormalFlow", subject = "owner", optional = false,
                            effects = new object[]
                            {
                                new { op = "selectTargets", target = "owner", targetKind = "otherLivingWithHand", minimumTargets = 1, maximumTargets = 1,
                                    targetAiOrder = "stable" },
                                new { op = "draw", target = "owner", amount = 1 }
                            } } } } }
                }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Pending] = new { name = "单一人选", description = "选择唯一其他角色后摸牌" } } }));
                builder.AddSkill(new(Pending, catalog.Presentations[Pending].Name, catalog.Presentations[Pending].Description)
                    { Program = catalog.Programs[Pending], ProgramPresentation = catalog.Presentations[Pending] });
            }
            {
                var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new
                {
                    schemaVersion = SkillProgramCatalog.RulesSchemaVersion,
                    skills = new object[]
                    {
                        new { id = Initial, revision = 1, modifiers = new[] { new { id = "ten-native-cards", query = "initialHandSize",
                            operation = "add", value = scenario.Placement ? 10 : fireAttack ? 1 : 4,
                            priority = 0, condition = new { kind = "always" } } } },
                        new { id = Partial, revision = 1, triggers = new[] { new { id = "native-armor-slot-zero", window = "gameStarting",
                            subject = "owner", optional = false, effects = new[] { new { op = "alterEquipmentSlots", target = "owner",
                                equipmentSlots = new[] { "armor" }, amount = 0 } } } } }
                    }
                }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object>
                    {
                        [Initial] = new { name = "固定实体起手", description = "原生起手数量" },
                        [Partial] = new { name = "原生部分装备槽", description = "护甲槽容量为零" }
                    } }));
                foreach (var (id, program) in catalog.Programs)
                    builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                        { Program = program, ProgramPresentation = catalog.Presentations[id] });
            }
            builder.AddGeneral(new(Owner, "自动目标持有者", "supporter", scenario.Pending ? Pending : scenario.Placement ? "boundary:zhijian" :
                scenario.Conversion ?? "standard:none", "wu", 6, [Initial]));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:auto-target-peer-{seat}").ToArray();
            for (var index = 0; index < peers.Length; index++)
                builder.AddGeneral(new(peers[index], "原生合法目标", "supporter",
                    scenario.Placement ? index < 2 ? Partial : "standard:none" :
                        index == 0 || scenario.Seats == 4 ? Initial : "classic:kongcheng", "wei", 6));
            const string deck = "fixture:auto-target-deck";
            // Fire Attack cannot target its owner after paying their last hand card.
            // This gives its printed and Huoji forms one actual legal recipient.
            // Two native empty-handed Kongcheng peers remain alive but cannot
            // receive Slash/Duel or any of the hand-dependent ordinary tricks.
            builder.AddDeck(new(deck, "固定自动目标实体牌", 0, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, scenario.Placement ? 12 : 80).Select(index => new ContentDeckPhysicalCard(
                    scenario.Placement ? index < 6 ? "standard:crossbow" : "standard:bagua" : scenario.Card,
                    Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "自动目标实际四席模式", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
