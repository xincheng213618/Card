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

internal static class ResponseConversionSelectionChecks
{
    private const string Longhun = "wushuang:skill-3023";
    private static readonly Lazy<ContentSkillDefinition> Definition = new(() =>
    {
        var assembly = typeof(StandardContentPackage).Assembly;
        string Read(string name) { using var stream = assembly.GetManifestResourceStream(
            $"CardGame.Content.Standard.SkillPrograms.wushuang-skills-b-audit.{name}.json")!;
            using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var catalog = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
        return new(Longhun, catalog.Presentations[Longhun].Name, catalog.Presentations[Longhun].Description)
            { Program = catalog.Programs[Longhun], ProgramPresentation = catalog.Presentations[Longhun] };
    });

    public static void ResponsesRequireSkillAndExactMaterials(string output)
    {
        foreach (var (suit, kind) in new[] { (Suit.Spade, DecisionKind.Nullification), (Suit.Club, DecisionKind.RespondDodge),
                     (Suit.Diamond, DecisionKind.RespondSlash), (Suit.Heart, DecisionKind.RescueDying) })
        {
            var (setup, registry) = Create(suit);
            Equip(setup);
            if (kind == DecisionKind.RescueDying)
            {
                while (setup.CreateSnapshot(0).Players[0].Hp > 0)
                {
                    Reach(setup, DecisionKind.PlayCard);
                    Accept(setup, new UseProgramSkillCommand(0, Fixture.Driver, "hurt", [], [], setup.Revision, setup.PendingDecision!.PromptId));
                }
            }
            else Accept(setup, new EndPlayPhaseCommand(0, setup.Revision, setup.PendingDecision!.PromptId));
            Reach(setup, kind);
            var checkpoint = setup.CreateCheckpoint();
            foreach (var count in new[] { 1, 2 })
            {
                using var model = Restore(checkpoint, registry);
                var game = Program.Engine(model);
                var prompt = game.CreateSnapshot(0).PendingDecision!;
                var revision = game.Revision;
                var commands = game.AcceptedCommands.Count;
                var physical = model.Hand.ToDictionary(card => card.Id, card => (card.Kind, card.Name, card.SuitGlyph, card.Rank));
                var window = new MainWindow(model);
                try
                {
                    var root = (FrameworkElement)window.Content;
                    var skill = model.HumanSkillCards.Single(item => item.ContentId == Longhun);
                    Program.Assert(skill.IsAvailable && skill.TypeText.Contains("主动") && !model.HasSelection &&
                        model.HandResponseExtraChoices.Count == 0 && model.Hand.All(card => !card.IsPlayable) &&
                        !model.HumanEquipmentSlots.Any(slot => slot.IsSelectable) && !model.CanConfirmSelected,
                        $"{kind}: an available conversion must wait on its skill rail without showing or preselecting costs.");
                    Click(root, model.ActivateHumanSkillCommand, skill);
                    Program.Assert(model.HasSelection && model.HandResponseExtraChoices.Count == 2 &&
                        model.HandResponseExtraChoices.All(choice => choice.Cards.Count == 0) &&
                        model.Hand.All(card => !card.IsPlayable) && !model.CanConfirmSelected,
                        "Longhun must choose one/two-card conversion before enabling materials.");
                    var retired = model.HandResponseExtraChoices.Single(choice => choice.Description.StartsWith(count + "张"));
                    Click(root, model.SelectHandResponseChoiceCommand, retired);
                    Program.Assert(model.IsCardPaymentSelectionPending && !model.CanConfirmSelected && model.HandResponseExtraChoices.Count == 0,
                        "The conversion mode must enter a reversible payment draft without a direct-submit alternative.");
                    var equipment = model.HumanEquipmentSlots.Single(slot => slot.Card is not null);
                    Program.Assert(equipment.IsSelectable, "Published equipment must be selectable only after choosing its conversion.");
                    Click(root, model.SelectEquipmentMaterialCommand, equipment);
                    var ids = new List<int> { equipment.Card!.Id };
                    if (count == 2)
                    {
                        Program.Assert(!model.CanConfirmSelected, "One material cannot pay a two-card conversion.");
                        var card = model.Hand.First(item => item.IsPlayable);
                        ids.Add(card.Id);
                        Click(root, model.SelectCardCommand, card);
                    }
                    var choice = prompt.Choices.Single(item => item.Cards.Count == count && item.Cards.ToHashSet().SetEquals(ids) &&
                        item.Parameters.GetValueOrDefault("conversion-skill-id") == Longhun);
                    Program.Assert(model.CanConfirmSelected && game.Revision == revision && game.AcceptedCommands.Count == commands &&
                        model.Hand.All(card => physical[card.Id] == (card.Kind, card.Name, card.SuitGlyph, card.Rank)) &&
                        model.HumanEquipmentSlots.Single(slot => slot.Card is not null).IsSelected,
                        "Drafts must retain physical card faces and pay nothing before confirmation.");
                    model.SelectHandResponseChoiceCommand.Execute(retired);
                    model.SelectHandResponseChoiceCommand.Execute(choice with { });
                    Program.Assert(game.Revision == revision && model.CanConfirmSelected,
                        "Retired modes and copied raw responses cannot submit or replace a draft.");
                    model.ClearSelectionCommand.Execute(null);
                    Program.Assert(!model.HasSelection && !model.CanConfirmSelected && model.HandResponseExtraChoices.Count == 0 &&
                        !model.HumanEquipmentSlots.Any(slot => slot.IsSelectable || slot.IsSelected) && game.Revision == revision,
                        "Esc/cancel must exit the conversion while leaving the original response unanswered.");
                    model.LoadManualGameCommand.Execute(null);
                    game = Program.Engine(model);
                    Program.Assert(!model.HasSaveError && !model.HasSelection && model.HandResponseExtraChoices.Count == 0 &&
                        !model.CanConfirmSelected && game.Revision == revision,
                        "Reloading the pending response must require explicit skill selection again.");
                    Click(root, model.ActivateHumanSkillCommand, model.HumanSkillCards.Single(item => item.ContentId == Longhun));
                    Click(root, model.SelectHandResponseChoiceCommand,
                        model.HandResponseExtraChoices.Single(item => item.Description.StartsWith(count + "张")));
                    Click(root, model.SelectEquipmentMaterialCommand, model.HumanEquipmentSlots.Single(slot => slot.Card is not null));
                    if (count == 2) Click(root, model.SelectCardCommand, model.Hand.Single(card => card.Id == ids[1]));
                    var expected = GameReplay.Restore(checkpoint, registry);
                    Accept(expected, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, expected.Revision));
                    model.ConfirmSelectedCommand.Execute(null);
                    Program.Assert(game.AcceptedCommands.Count == commands + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                        answer.Choice == choice.Id && game.CardMovements.SequenceEqual(expected.CardMovements) &&
                        SnapshotJson.Serialize(game.CreateSnapshot(0)) == SnapshotJson.Serialize(expected.CreateSnapshot(0)),
                        "Confirm must submit exactly the published conversion and pay its native hand/equipment cost once.");
                    var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                    Program.Assert(SnapshotJson.Serialize(cold.CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)),
                        "The accepted conversion and any native follow-up must survive cold replay.");
                }
                finally { window.Content = null; window.Close(); }
            }
        }
        var (rescue, rescueRegistry) = StableHandResponseFixture.Create(DecisionKind.RescueDying, output, jijiu: true);
        using (rescue)
        {
            var game = Program.Engine(rescue);
            var revision = game.Revision;
            var card = rescue.Hand.First(item => item.Kind == CardKind.Dodge &&
                game.PendingDecision!.Choices.Any(choice => choice.Cards.SequenceEqual([item.Id]) &&
                    choice.Parameters.GetValueOrDefault("conversion-skill-id") == "standard:jijiu"));
            Program.Assert(!card.IsPlayable && rescue.Hand.Any(item => item.Kind == CardKind.Peach && item.IsPlayable),
                "Jijiu must preserve native Peach while its red Dodge waits for skill selection.");
            rescue.ActivateHumanSkillCommand.Execute(rescue.HumanSkillCards.Single(item => item.ContentId == "standard:jijiu"));
            Program.Assert(rescue.HandResponseExtraChoices.Count == 0 && rescue.IsCardPaymentSelectionPending &&
                rescue.Hand.Single(item => item.Id == card.Id).IsPlayable && game.Revision == revision,
                "A single-output response conversion must skip an unnecessary mode chooser and remain unpaid.");
            rescue.SelectCardCommand.Execute(rescue.Hand.Single(item => item.Id == card.Id));
            Program.Assert(rescue.CanConfirmSelected && game.Revision == revision, "Other conversions must share the confirmation boundary.");
            rescue.ConfirmSelectedCommand.Execute(null);
            Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand,
                "Jijiu must commit only after explicit confirmation.");
            StableHandResponseFixture.RecordAndReplay(game, rescueRegistry, output, "skill-first-jijiu.after");
        }
    }

    public static void PlayGroupsSingleAndMultipleCardConversions()
    {
        var (setup, registry) = Create(null);
        foreach (var kind in new[] { CardKind.Peach, CardKind.FireSlash })
        foreach (var count in new[] { 1, 2 })
        {
            using var model = Restore(setup.CreateCheckpoint(), registry);
            var game = Program.Engine(model);
            var revision = game.Revision;
            var window = new MainWindow(model);
            try
            {
                var root = (FrameworkElement)window.Content;
                Click(root, model.ActivateHumanSkillCommand, model.HumanSkillCards.Single(skill => skill.ContentId == Longhun));
                Program.Assert(model.EquipmentPlayChoices.Count == 4 && model.Hand.All(card => !card.IsPlayable) &&
                    !model.SupplementalActiveSkillActions.Any(action => action.ProgramSkillId == Longhun),
                    "The rail must group one/two-card Peach and Fire Slash before enabling materials: " +
                    string.Join(" | ", model.EquipmentPlayChoices.Select(choice => choice.Description)) + "; hand=" +
                    string.Join(",", model.Hand.Select(card => card.SuitGlyph + card.Name)) + "; " + model.ActionHint);
                var mode = model.EquipmentPlayChoices.Single(choice => choice.Parameters.GetValueOrDefault("played-kind") == kind.ToString() &&
                    choice.Description.Contains(count + "张牌"));
                Click(root, model.SelectEquipmentPlayChoiceCommand, mode);
                var cards = model.Hand.Where(card => card.IsPlayable).Take(count).ToArray();
                Program.Assert(cards.Length == count && !model.CanConfirmSelected, "A conversion mode must expose its own legal material set.");
                foreach (var card in cards) Click(root, model.SelectCardCommand, card);
                if (kind == CardKind.FireSlash && !model.Seats.Any(seat => seat.IsSelectedTarget))
                    model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 1));
                Program.Assert(model.CanConfirmSelected && game.Revision == revision, "Selecting conversion materials/targets cannot pay early.");
                model.ConfirmSelectedCommand.Execute(null);
                Program.Assert(game.Revision == revision + 1 && (count == 1
                    ? game.AcceptedCommands.Last() is PlayCardCommand played && played.ConversionSource?.SkillId == Longhun && played.PlayedCardKind == kind
                    : game.AcceptedCommands.Last() is UseProgramSkillCommand used && used.SkillId == Longhun && used.CardIds.Count == 2),
                    "Each conversion mode must retain its native single-card or multi-card command identity.");
            }
            finally { window.Content = null; window.Close(); }
        }
    }

    private static (GameEngine, ContentRegistry) Create(Suit? suit)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(suit));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Fixture.Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, DecisionKind.PlayCard);
        return (game, registry);
    }

    private static void Equip(GameEngine game)
    {
        var action = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Equip);
        Accept(game, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, DecisionKind.PlayCard);
    }

    private static MainViewModel Restore(GameCheckpoint checkpoint, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, checkpoint));
        var model = new MainViewModel(false, 31, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }

    private sealed class Fixture(Suit? suit) : IGameContentPackage
    {
        internal const string Mode = "identity:classic-response-conversion-ui", Owner = "fixture:response-conversion-owner", Driver = "fixture:response-conversion-driver";
        public PackageManifest Manifest { get; } = new("fixture-response-conversion-ui", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(Definition.Value);
            var rules = """
                {"schemaVersion":$SCHEMA$,"skills":[
                {"id":"fixture:response-conversion-driver","revision":1,"activations":[
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
                {"id":"fixture:response-conversion-attack","revision":1,"triggers":[{"id":"attack","window":"$WINDOW$","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"anyWounded"},$USE$]}]}]}
                """.Replace("$SCHEMA$", SkillProgramCatalog.RulesSchemaVersion.ToString())
                .Replace("$WINDOW$", suit is Suit.Diamond or Suit.Spade ? "turnStartBeforeNormalFlow" : "afterNormalDraw")
                .Replace("$USE$", suit is Suit.Diamond or Suit.Spade
                    ? """{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"outputKind":"duel"}"""
                    : """{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}""");
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "驱动", description = "原生发动" },
                    ["fixture:response-conversion-attack"] = new { name = "来袭", description = "原生来袭" } } }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, "规则夹具")
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Owner, "龙魂选牌", "supporter", Longhun, "qun", 4, AdditionalSkillIds: [Driver]) { InitialHp = 2 });
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:response-conversion-peer-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "来袭者", "supporter", "fixture:response-conversion-attack", "wei", 6));
            builder.AddDeck(new("fixture:response-conversion-deck", "材料", 8, 0, []) { PhysicalCards = Enumerable.Range(0, 80)
                .Select(index => new ContentDeckPhysicalCard("standard:crossbow", suit ?? (index % 2 == 0 ? Suit.Heart : Suit.Diamond), index % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "龙魂选牌", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:response-conversion-deck", 4, [Owner, .. peers]));
        }
    }

    private static void Reach(GameEngine game, DecisionKind kind)
    {
        for (var step = 0; step < 50; step++)
        {
            if (game.State.Status == EngineStatus.Completed) throw new InvalidOperationException($"Completed before {kind}: " +
                string.Join(" | ", game.Events.TakeLast(8).Select(item => item.Payload.GetType().Name + ":" + JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))));
            if (game.PendingDecision is { PlayerSeat: 0 } prompt && prompt.Kind == kind) return;
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.Nullification } skip)
                Accept(game, new AnswerPromptCommand(0, skip.PromptId, skip.Choices.Single(choice => choice.Parameters.GetValueOrDefault("response") == "pass").Id, game.Revision));
            else if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards } discard)
                Accept(game, new DiscardCardsCommand(0, discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, game.Revision));
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException($"Did not reach {kind}: {game.PendingDecision?.Kind}.");
    }
    private static void Accept(GameEngine game, GameCommand command) { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
    private static void Click(FrameworkElement root, ICommand command, object parameter)
    {
        root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
        var button = Program.Find<Button>(root).Single(item => ReferenceEquals(item.Command, command) && ReferenceEquals(item.CommandParameter, parameter));
        Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible, "The actual conversion control must be enabled and visible.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
}
