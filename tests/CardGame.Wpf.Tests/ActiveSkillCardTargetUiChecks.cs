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
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class ActiveSkillCardTargetUiChecks
{
    private enum Placement { Zhijian, Jieyin }

    public static void Run()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        foreach (var branch in Enum.GetValues<Placement>())
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
                var game = Program.Engine(model);
                var skill = Skill(branch);
                var action = Action();
                var human = game.CreateSnapshot(0).Players[0];
                var limited = game.CreateSnapshot(0).Players.Single(player => Capacity(player, EquipmentSlot.Armor) == 0).Seat;
                var normal = game.CreateSnapshot(0).Players.First(player => player.Seat != 0 && player.Seat != limited).Seat;
                var armorId = human.Hand.First(card => card.Kind == CardKind.BaguaFormation).Id;
                var weaponId = human.Hand.First(card => card.Kind == CardKind.Crossbow).Id;
                var before = game.CreateCheckpoint();
                var unchanged = Boundary(game);
                var adviceView = game.CreateSnapshot(0);
                var advice = PlayAdvisor.Recommend(adviceView, [action], []);
                var adviceCard = human.Hand.Where(card => card.Kind == CardKind.BaguaFormation).MinBy(card => card.Id)!;
                var adviceSuit = adviceCard.Suit switch { Suit.Spade => "♠", Suit.Heart => "♥", Suit.Club => "♣", _ => "♦" };
                Program.Assert(advice is not null && advice.Action == action.Description &&
                    advice.Selection.Contains($"选牌：【{adviceCard.DisplayName}】{adviceSuit}{adviceCard.RankText}", StringComparison.Ordinal) &&
                    advice.Selection.Contains($"目标：{normal + 1:00} {adviceView.Players[normal].GeneralName}", StringComparison.Ordinal) &&
                    !advice.Selection.Contains($"目标：{limited + 1:00} ", StringComparison.Ordinal) && Boundary(game) == unchanged,
                    "Formal equipment-placement advice must pair the cheapest actual armor with the known legal normal recipient and leave the live native game unchanged.");
                var adviceGame = GameReplay.Restore(before, registry);
                Program.Assert(adviceGame.Submit(Command(adviceGame, branch, adviceCard.Id, normal)).Accepted,
                    "The exact fee and recipient described by placement advice must form one native accepted skill activation.");
                Console.WriteLine($"Card-target advice {branch}: armor={adviceCard.Id}, target={normal}, nativeAccepted=True.");
                Program.Assert(action.MinCardCount == 1 && action.MaxCardCount == 1 && action.MinTargetCount == 1 &&
                    action.MaxTargetCount == 1 && action.SelectableCardIds.Contains(armorId) && action.SelectableCardIds.Contains(weaponId) &&
                    action.SelectableTargetSeats.Contains(limited) && action.SelectableTargetSeats.Contains(normal) &&
                    !action.SelectableTargetSeats.Contains(0),
                    "Both real equipment entities and both recipients must independently belong to the native placement union, with no self target.");
                Program.Assert(action.SelectableCardTargets is { } associations &&
                    associations.Contains(new(weaponId, limited)) && associations.Contains(new(armorId, normal)) &&
                    !associations.Contains(new(armorId, limited)) &&
                    associations.All(pair => action.SelectableCardIds.Contains(pair.CardId) &&
                        action.SelectableTargetSeats.Contains(pair.TargetSeat)) &&
                    associations is ICollection<ActiveSkillCardTarget> { IsReadOnly: true },
                    "The published placement must freeze its exact weapon/limited and armor/normal associations while excluding the native rejected pair.");
                var rejected = GameReplay.Restore(before, registry);
                var invalid = rejected.Submit(Command(rejected, branch, armorId, limited));
                Program.Assert(!invalid.Accepted && invalid.Error is { Code: CommandErrorCode.InvalidCard } error &&
                    error.Message.Contains("equipment", StringComparison.OrdinalIgnoreCase) && Boundary(rejected) == unchanged,
                    "The exact native armor/zero-armor-slot pair must fail before commands, movements, events or safe views change.");

                Begin();
                AssertUnion();
                Program.Assert(model.ActionHint.Contains("目标须能接收所选费用牌", StringComparison.Ordinal) &&
                    model.ActionHint.Contains("可取消原目标后改选", StringComparison.Ordinal),
                    "The active hint must explain its published card-target requirement and the retained-target correction path.");
                SelectCard(armorId);
                Layout(root);
                Console.WriteLine($"Card-target {branch}: armor={armorId}, weapon={weaponId}, limited={limited}, normal={normal}, " +
                    $"nativeTargetUnion={string.Join(',', action.SelectableTargetSeats)}, " +
                    $"armorLegal={string.Join(',', model.Seats.Where(seat => seat.IsLegalTarget).Select(seat => seat.Seat))}.");
                Program.Assert(!Seat(limited).IsLegalTarget && Seat(normal).IsLegalTarget &&
                    model.Seats.Where(seat => seat.IsLegalTarget).Select(seat => seat.Seat).ToHashSet()
                        .SetEquals(action.SelectableTargetSeats.Where(seat => seat != limited)),
                    "Selecting actual armor must narrow the portrait highlights to that material's native recipients.");
                var armorDraft = Draft(model);
                SelectTarget(limited);
                Program.Assert(Draft(model) == armorDraft && Boundary(game) == unchanged &&
                    !model.CanConfirmActiveSkill && !Confirm().IsEnabled,
                    "Clicking the real incompatible portrait must preserve the armor draft and keep confirmation disabled.");
                Program.Assert(!TableShortcutChecks.Input(window, Key.Enter) && Draft(model) == armorDraft && Boundary(game) == unchanged,
                    "Enter must not submit or pay an armor draft without a compatible selected recipient.");
                Escape();

                Begin();
                AssertUnion();
                SelectTarget(limited);
                SelectCard(armorId);
                Layout(root);
                AssertSelected(armorId, limited);
                Program.Assert(!model.CanConfirmActiveSkill && !model.CanConfirmSelected && !Confirm().IsEnabled &&
                    !Confirm().Command!.CanExecute(Confirm().CommandParameter),
                    "A recipient selected before its incompatible armor must remain selected and cancellable while the real confirmation stays disabled.");
                var mismatch = Draft(model);
                Program.Assert(!TableShortcutChecks.Input(window, Key.Enter) && Draft(model) == mismatch && Boundary(game) == unchanged,
                    "An incompatible target-first draft must retain its exact cost and old target without native payment.");
                SelectTarget(limited);
                Program.Assert(model.Seats.All(seat => !seat.IsSelectedTarget) && Card(armorId).IsSelected && Boundary(game) == unchanged,
                    "The selected incompatible portrait must still toggle off without losing the prepared armor.");
                SelectTarget(limited);
                Program.Assert(model.Seats.All(seat => !seat.IsSelectedTarget) && Card(armorId).IsSelected,
                    "After cancellation, the incompatible portrait cannot be added again while armor remains selected.");
                SelectTarget(normal);
                AssertSelected(armorId, normal);
                Program.Assert(model.CanConfirmActiveSkill && Confirm().IsEnabled,
                    "Correcting only the recipient must make the same prepared armor usable.");
                Escape();

                Begin();
                SelectCard(weaponId);
                Program.Assert(Seat(limited).IsLegalTarget, "The partial abolition must retain its actual legal weapon placement.");
                SelectTarget(limited);
                AssertSelected(weaponId, limited);
                Program.Assert(model.CanConfirmActiveSkill && Confirm().IsEnabled, "Weapon/limited-target must be a genuine complete draft.");
                SelectCard(weaponId);
                AssertUnion();
                Program.Assert(Seat(limited).IsSelectedTarget && !model.CanConfirmActiveSkill &&
                    model.Hand.All(card => !card.IsSelected),
                    "Removing the sole cost must restore the native target union without silently cancelling the old recipient.");
                SelectCard(armorId);
                AssertSelected(armorId, limited);
                Program.Assert(!model.CanConfirmActiveSkill && !Confirm().IsEnabled && Seat(normal).IsLegalTarget,
                    "Switching the real cost from weapon to armor must immediately update confirmation and retain compatible recipients.");
                SelectTarget(limited);
                Program.Assert(!Seat(limited).IsSelectedTarget && !Seat(limited).IsLegalTarget && Card(armorId).IsSelected,
                    "Removing the retained old recipient after a cost switch must leave it incompatible and the armor intact.");
                SelectTarget(normal);
                AssertSelected(armorId, normal);
                Program.Assert(model.CanConfirmSelected && Confirm().IsEnabled && Boundary(game) == unchanged,
                    "Changing to a compatible recipient after a cost switch must prepare the native action without paying early.");
                Escape();

                foreach (var cost in new[] { (Id: armorId, Target: normal), (Id: weaponId, Target: limited) })
                {
                    if (cost.Id == weaponId)
                    {
                        var equipped = GameReplay.Restore(before, registry);
                        var equip = equipped.GetHumanLegalActions().Single(candidate => candidate.CardId == weaponId &&
                            candidate.Kind == LegalActionKind.Equip);
                        var setupMovements = equipped.CardMovements.Count;
                        var setupCommands = equipped.AcceptedCommands.Count;
                        Accept(equipped, new PlayCardCommand(0, weaponId, equip.TargetSeats, equipped.Revision,
                            equipped.CreateSnapshot(0).PendingDecision!.PromptId, equip.PlayedCardKind, equip.TargetCardId)
                            { ConversionSource = equip.ConversionSource });
                        for (var step = 0; step < 16 && equipped.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                            Accept(equipped, new AdvanceOneStepCommand(equipped.Revision));
                        var setupPayment = equipped.CardMovements.Skip(setupMovements).Where(move => move.CardId == weaponId).ToArray();
                        Program.Assert(equipped.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                            equipped.AcceptedCommands.Skip(setupCommands).OfType<PlayCardCommand>().Count() == 1 &&
                            equipped.CreateSnapshot(0).Players[0].Equipment.Single().Id == weaponId &&
                            setupPayment is [var use, var enter] && use.From == CardLocation.Hand(0) &&
                            use.To == CardLocation.Processing && use.Reason == CardMoveReasons.EquipmentUse &&
                            enter.From == CardLocation.Processing && enter.To == CardLocation.Equipment(0) &&
                            enter.Reason == CardMoveReasons.EquipmentEnter,
                            "The positive weapon route must first genuinely use and equip its exact entity through one native ordinary Equip command.");
                        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow,
                            false, equipped.CreateCheckpoint()));
                    }
                    foreach (var route in new[] { "button", "Enter" })
                    {
                    model.LoadManualGameCommand.Execute(null);
                    game = Program.Engine(model);
                    Program.Assert(!model.HasSaveError && !model.HasSelection && game.CreateSnapshot(0).PendingDecision is
                        { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
                        "Each positive placement route must restore the same actual saved native Play boundary.");
                    var checkpoint = game.CreateCheckpoint();
                    var movements = game.CardMovements.Count;
                    var events = game.Events.Count;
                    var prompt = game.CreateSnapshot(0).PendingDecision!.PromptId;
                    var origin = game.CreateSnapshot(0).Players[0].Equipment.Any(card => card.Id == cost.Id)
                        ? CardLocation.Equipment(0) : CardLocation.Hand(0);
                    Program.Assert(origin == (cost.Id == weaponId ? CardLocation.Equipment(0) : CardLocation.Hand(0)),
                        "Each positive route must restore the intended real hand armor or already-equipped weapon origin.");
                    var frozen = Boundary(game);
                    Begin();
                    SelectCard(cost.Id);
                    SelectTarget(cost.Target);
                    AssertSelected(cost.Id, cost.Target);
                    Program.Assert(model.CanConfirmActiveSkill && model.CanConfirmSelected && Confirm().IsEnabled &&
                        Confirm().Visibility == Visibility.Visible && Confirm().Command!.CanExecute(Confirm().CommandParameter) &&
                        Boundary(game) == frozen,
                        "The real compatible equipment/recipient draft must enable confirmation without changing any native boundary.");
                    var reference = GameReplay.Restore(checkpoint, registry);
                    Accept(reference, Command(reference, branch, cost.Id, cost.Target));
                    Console.WriteLine($"Card-target {branch}/{route}: cost={cost.Id}, recipient={cost.Target}, " +
                        $"canConfirm={model.CanConfirmActiveSkill}, buttonEnabled={Confirm().IsEnabled}.");
                    if (route == "button") Click(Confirm());
                    else Program.Assert(TableShortcutChecks.Input(window, Key.Enter),
                        "Enter must submit the exact current compatible equipment/recipient pair.");
                    Program.Assert(game.Revision == checkpoint.Revision + 1 && game.AcceptedCommands.Count == checkpoint.Commands.Count + 1 &&
                        game.AcceptedCommands.Last() is UseProgramSkillCommand submitted && submitted.ActorSeat == 0 &&
                        submitted.SkillId == skill && submitted.ActivationId == "place-equipment" && submitted.CardIds.SequenceEqual([cost.Id]) &&
                        submitted.TargetSeats.SequenceEqual([cost.Target]) && submitted.ExpectedRevision == checkpoint.Revision &&
                        submitted.PromptId == prompt && !model.HasSelection && !model.IsActiveSkillSelectionPending &&
                        Boundary(game) == Boundary(reference),
                        "Button/Enter must commit exactly one native placement command with the original pair and match all four reference views.");
                    AssertPayment();
                    var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                    Program.Assert(Boundary(cold) == Boundary(game),
                        "The committed placement and its typed continuation must cold restore every safe view, movement, event and command.");
                    for (var step = 0; step < 16 && game.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                    {
                        Program.Assert(model.CanStepAi, "The actual placement continuation must be advanceable until human Play.");
                        Accept(reference, new AdvanceOneStepCommand(reference.Revision));
                        model.StepAiCommand.Execute(null);
                        Program.Assert(Boundary(game) == Boundary(reference),
                            "Each bounded placement continuation must retain the exact independent native result.");
                    }
                    Program.Assert(game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                        model.Hand.All(card => card.Id != cost.Id) &&
                        game.CreateSnapshot(0).Players[0].Equipment.All(card => card.Id != cost.Id) &&
                        game.CreateSnapshot(0).Players[cost.Target].Equipment.Single(card => card.Id == cost.Id).Kind ==
                            (cost.Id == armorId ? CardKind.BaguaFormation : CardKind.Crossbow),
                        "The original physical cost must remain in its exact recipient equipment area after the native return.");
                    AssertPayment();
                    cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                    Program.Assert(Boundary(cold) == Boundary(game),
                        "The returned placement boundary must retain exact costs and all four safe views on cold restore.");

                    void AssertPayment()
                    {
                        var paid = game.CardMovements.Skip(movements).Where(move => move.CardId == cost.Id).ToArray();
                        Program.Assert(paid is [var placement] && placement.From == origin &&
                            placement.To == CardLocation.Equipment(cost.Target) && placement.Reason == CardMoveReasons.EquipmentEnter &&
                            game.Events.Skip(events).Select(item => item.Payload).OfType<EquipmentChangedEvent>()
                                .Count(item => item.CardId == cost.Id && item.PlayerSeat == cost.Target) == 1,
                            "The actual placement must pay exactly once directly from its original Hand/Equipment zone to recipient Equipment, without using or discarding its entity.");
                    }
                    }
                }

                LegalAction Action() => model.HumanActiveSkillActions.Single(candidate =>
                    candidate.ProgramSkillId == skill && candidate.ProgramActivationId == "place-equipment");
                SeatViewModel Seat(int seat) => model.Seats.Single(item => item.Seat == seat);
                CardViewModel Card(int id) => model.Hand.Single(item => item.Id == id);
                Button Confirm() => (Button)window.FindName("PlayCardButton");
                void Begin() { model.SelectActiveSkillCommand.Execute(Action()); Layout(root); }
                void SelectCard(int id)
                {
                    if (model.Hand.SingleOrDefault(card => card.Id == id) is { } card)
                        Click(BoundButton(root, model.SelectCardCommand, card));
                    else
                        Click(BoundButton(root, model.SelectActiveSkillEquipmentChoiceCommand,
                            model.ActiveSkillEquipmentChoices.Single(choice => choice.Cards.SequenceEqual([id]))));
                    Layout(root);
                }
                void SelectTarget(int seat) { Click(BoundButton(root, model.SelectTargetCommand, Seat(seat))); Layout(root); }
                void AssertSelected(int id, int target)
                {
                    Program.Assert(model.IsActiveSkillSelectionPending && model.HasSelection &&
                        model.Hand.Where(card => card.IsSelected).Select(card => card.Id)
                            .Concat(model.ActiveSkillEquipmentChoices.Where(choice => choice.Description.StartsWith("✓", StringComparison.Ordinal))
                                .SelectMany(choice => choice.Cards)).SequenceEqual([id]) &&
                        model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).SequenceEqual([target]),
                        "The bound card and portrait controls must preserve the exact physical cost and selected recipient.");
                }
                void AssertUnion()
                {
                    Program.Assert(model.Hand.All(card => !card.IsSelected) &&
                        model.Seats.Where(seat => seat.IsLegalTarget).Select(seat => seat.Seat).ToHashSet()
                            .SetEquals(Action().SelectableTargetSeats),
                        "Without a selected fee, the visible portrait union must equal the current native published target union.");
                }
                void Escape()
                {
                    Program.Assert(TableShortcutChecks.Input(window, Key.Escape) && !model.HasSelection &&
                        !model.IsActiveSkillSelectionPending && model.Hand.All(card => !card.IsSelected) &&
                        model.Seats.All(seat => !seat.IsSelectedTarget) && Boundary(game) == unchanged,
                        "Esc must clear only the placement draft and retain every physical entity, prompt and player-safe native view.");
                    Layout(root);
                }
            }
            finally { window.Content = null; window.Close(); }
        }
    }

    private static GameEngine Create(ContentRegistry registry, Placement branch)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Fixture.Mode(branch),
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(game, new StartGameCommand());
        Program.Assert(game.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 },
            "The placement fixture must begin through native general selection.");
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner(branch), game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; step < 40 && game.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        var view = game.CreateSnapshot(0);
        var limited = view.Players.Single(player => Capacity(player, EquipmentSlot.Armor) == 0);
        Program.Assert(view.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && view.Players[0].Hand.Count == 10 &&
            view.Players[0].Hand.Count(card => card.Kind == CardKind.BaguaFormation) >= 4 &&
            view.Players[0].Hand.Count(card => card.Kind == CardKind.Crossbow) >= 4 && !limited.IsEquipmentAreaAbolished &&
            Capacity(limited, EquipmentSlot.Weapon) == 1 &&
            view.Players.Where(player => player.Seat != 0 && player.Seat != limited.Seat).All(player => Capacity(player, EquipmentSlot.Armor) == 1) &&
            game.Events.Select(item => item.Payload).OfType<EquipmentSlotCapacityChangedEvent>()
                .Count(item => item.Seat == limited.Seat && item.Slot == EquipmentSlot.Armor && item.Capacity == 0) == 1 &&
            view.Players.Skip(1).All(player => registry.Generals[player.GeneralId].Gender == GeneralGender.Male) &&
            (branch != Placement.Jieyin || registry.Generals[view.Players[0].GeneralId].Gender == GeneralGender.Female &&
                registry.Generals["boundary:sun-shangxiang"].Gender == GeneralGender.Female),
            "The native ten-of-twelve deal and real startup slot instruction must prepare two payable equipment types, exactly one partial recipient and an unchanged female Jieyin owner.");
        foreach (var seat in Enumerable.Range(0, 4))
            Program.Assert(Capacity(game.CreateSnapshot(seat).Players[limited.Seat], EquipmentSlot.Armor) == 0,
                "Every player-safe view must expose the actual partial equipment-slot state.");
        return game;
    }

    private static string Skill(Placement branch) => branch == Placement.Zhijian ? "boundary:zhijian" : "boundary:jieyin-current";
    private static int Capacity(PlayerSnapshot player, EquipmentSlot slot) => player.IsEquipmentAreaAbolished ? 0 :
        player.EquipmentSlotCapacities?.GetValueOrDefault(slot, 1) ?? 1;
    private static UseProgramSkillCommand Command(GameEngine game, Placement branch, int card, int target) =>
        new(0, Skill(branch), "place-equipment", [card], [target], game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId);
    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, Commands = CommandJson.Serialize(game.AcceptedCommands), Movements = game.CardMovements,
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray()
    });
    private static string Draft(MainViewModel model) => JsonSerializer.Serialize(new
    {
        model.HasSelection, model.IsActiveSkillSelectionPending, model.SelectedCardText, model.ActionHint,
        Cards = model.Hand.Where(card => card.IsSelected).Select(card => card.Id).ToArray(),
        Targets = model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToArray()
    });
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
        Program.Assert(result.Accepted, result.Error?.Message ?? "A native card-target fixture command was rejected.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        private const string Initial = "fixture:card-target-initial", Partial = "fixture:card-target-partial";
        internal static string Owner(Placement branch) => $"fixture:card-target-owner-{branch.ToString().ToLowerInvariant()}";
        internal static string Mode(Placement branch) => $"identity:classic-ui-card-target-{branch.ToString().ToLowerInvariant()}";
        public PackageManifest Manifest { get; } = new("fixture-ui-card-target", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.RulesSchemaVersion,
                skills = new object[]
                {
                    new { id = Initial, revision = 1, modifiers = new[] { new { id = "ten-real-owner-cards", query = "initialHandSize",
                        operation = "add", value = 10, priority = 0, condition = new { kind = "always" } } } },
                    new { id = Partial, revision = 1, triggers = new[] { new { id = "actual-armor-slot-zero", window = "gameStarting",
                        subject = "owner", optional = false, effects = new[] { new { op = "alterEquipmentSlots", target = "owner",
                            equipmentSlots = new[] { "armor" }, amount = 0 } } } } }
                }
            }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    [Initial] = new { name = "固定实际起手", description = "仅持有者实际发十张实体牌" },
                    [Partial] = new { name = "实际部分装备槽", description = "原生开局仅令护甲槽容量为零" }
                } }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:card-target-{seat}").ToArray();
            foreach (var peer in peers)
                builder.AddGeneral(new(peer, "实际男性目标", "supporter", peer == peers[0] ? Partial : "standard:none", "wu", 4,
                    Gender: GeneralGender.Male));
            const string deck = "fixture:card-target-deck";
            builder.AddDeck(new(deck, "固定十二张实际装备", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 12).Select(i => new ContentDeckPhysicalCard(
                i < 6 ? "standard:crossbow" : "standard:bagua",
                new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i % 4], i + 1)).ToArray() });
            foreach (var branch in Enum.GetValues<Placement>())
            {
                builder.AddGeneral(new(Owner(branch), branch == Placement.Jieyin ? "孙尚香费用目标" : "界二张费用目标", "supporter",
                    Skill(branch), "wu", 3, [Initial],
                    branch == Placement.Jieyin ? GeneralGender.Female : GeneralGender.Male));
                builder.AddMode(new(Mode(branch), "原生装备费用目标", 4, 4, new Dictionary<string, int>
                    { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, deck,
                    GeneralCandidateCount: 4, GeneralPoolIds: [Owner(branch), .. peers]));
            }
        }
    }
}
