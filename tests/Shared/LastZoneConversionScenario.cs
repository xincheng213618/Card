using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class LastZoneConversionScenario
{
    internal const string Conversion = "fixture:last-zone-conversion";
    internal const string Driver = "fixture:last-zone-driver";
    internal const string Accepted = "fixture:last-zone-accepted";
    internal const string Completed = "fixture:last-zone-completed";
    internal const string Health = "fixture:last-zone-health";
    internal const string Cost = "fixture:last-zone-cost";
    private const string IncomingPeer = "fixture:last-zone-incoming-peer";
    private const string Owner = "fixture:last-zone-owner";
    private const string Mode = "identity:classic-last-zone-fixture";

    internal static (GameEngine Game, ContentRegistry Registry) Create(string scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(scenario));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 5
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        return (game, registry);
    }

    internal static int PlaceJudgment(GameEngine game)
    {
        var action = game.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.Lightning)
            ?? throw new InvalidOperationException("The fixed Lightning fixture has no real self-placement action: " + JsonSerializer.Serialize(game.GetHumanLegalActions()));
        var id = action.CardId!.Value;
        Play(game, action);
        ReachPlay(game);
        Require(game.CreateCardZoneDiagnostics().Single(d => d.CardId == id).Location == CardLocation.Judgment(0),
            "A real native Lightning use places its physical entity in the owner's Judgment region.");
        return id;
    }

    internal static PendingDecision? Prompt(GameEngine game) => game.CreateSnapshot(0).PendingDecision;
    internal static void Play(GameEngine game, LegalAction action)
    {
        var targets = action.TargetSeats.Count > 0 ? action.TargetSeats : action.TargetSeat is { } seat ? [seat] : Array.Empty<int>();
        Accept(game, new PlayCardCommand(0, action.CardId!.Value, targets, game.Revision, Prompt(game)!.PromptId,
            action.PlayedCardKind, action.TargetCardId)
            { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
    }
    internal static void Activate(GameEngine game, string id, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null)
    {
        Accept(game, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], game.Revision, Prompt(game)!.PromptId));
        if (id == "incoming")
        {
            Reach(game, p => p is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } && p.Choices.Any(c =>
                c.Parameters.GetValueOrDefault("program-action") == "assisted-physical-slash" &&
                c.Parameters.GetValueOrDefault("request-option") == "target" && c.Targets.SequenceEqual([0])));
            Answer(game, c => c.Parameters.GetValueOrDefault("request-option") == "target" && c.Targets.SequenceEqual([0]));
        }
    }
    internal static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var prompt = Prompt(game)!;
        var choice = prompt.Choices.FirstOrDefault(predicate) ?? throw new InvalidOperationException("A real published last-region choice is missing: " + JsonSerializer.Serialize(prompt));
        Accept(game, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
    }
    internal static void ReachPlay(GameEngine game) => Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    internal static void Reach(GameEngine game, Func<PendingDecision?, bool> ready)
    {
        for (var i = 0; i < 700; i++)
        {
            var p = Prompt(game);
            if (ready(p)) return;
            if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard })
                throw new InvalidOperationException("The real operation returned to Play before the required last-region boundary. Recent facts: " +
                    JsonSerializer.Serialize(game.Events.TakeLast(10).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))));
            Step(game);
        }
        throw new InvalidOperationException("The bounded last-region fixture did not reach its native boundary: " + JsonSerializer.Serialize(Prompt(game)));
    }
    internal static void Step(GameEngine game)
    {
        var p = Prompt(game);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"))
            Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification })
            Answer(game, c => c.Cards.Count == 0);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards })
            Accept(game, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, game.Revision));
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    internal static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "The real last-region command was rejected.");
    }
    internal static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), game.CardMovements,
        Facts = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    internal static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(cold) == State(game), "Cold replay retains every real source region, native material claim, private prompt, payment and exact child return.");
        return cold;
    }
    internal static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    internal static string Rules()
    {
        var rules = JsonNode.Parse("""
        {"skills":[
          {"id":"fixture:last-zone-conversion","revision":1,"viewAs":[
            {"id":"last-hand-dodge","inputKinds":[],"inputSuits":[],"inputCount":1,"sourceZones":["hand"],"outputKind":"dodge","forPlay":false,"forResponse":true,"allowSameKind":true,"lastInSourceZone":true},
            {"id":"last-equipment-nullification","inputKinds":[],"inputSuits":[],"inputCount":1,"sourceZones":["equipment"],"outputKind":"nullification","forPlay":false,"forResponse":true,"useOnly":true,"allowSameKind":true,"lastInSourceZone":true},
            {"id":"last-judgment-slash","inputKinds":[],"inputSuits":[],"inputCount":1,"sourceZones":["judgment"],"outputKind":"slash","forPlay":true,"forResponse":true,"allowSameKind":true,"lastInSourceZone":true}]},
          {"id":"fixture:last-zone-driver","revision":1,"activations":[
            {"id":"drop","minCards":1,"maxCards":64,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"drop-payment"},{"op":"moveBoundCards","target":"owner","sourceBind":"drop-payment","destination":"discardPile","awaitMovementTriggers":true}]},
            {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]},
            {"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
            {"id":"duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
            {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashAgainstChosenTarget","target":"selectedTarget","resultBind":"incoming-request"}]},
            {"id":"remove-equipment","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","zones":["equipment"],"minimumCards":1,"maximumCards":1,"resultBind":"equipment-cost"},{"op":"moveBoundCards","target":"owner","sourceBind":"equipment-cost","destination":"discardPile","awaitMovementTriggers":true}]}]},
          {"id":"fixture:last-zone-accepted","revision":1,"triggers":[{"id":"accepted","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["slash","dodge","nullification"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
          {"id":"fixture:last-zone-completed","revision":1,"triggers":[{"id":"completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["slash"],"singleActionInstance":true,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
          {"id":"fixture:last-zone-health","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
          {"id":"fixture:last-zone-cost","revision":1,"triggers":[{"id":"cost","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perBatch","movementReasons":[],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
        ]}
        """)!;
        rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
        rules["skills"]![5]!["triggers"]![0]!["movementReasons"] = new JsonArray(CardMoveReasons.Nullification.Value);
        rules["skills"]!.AsArray().Add(JsonNode.Parse("""
          {"id":"fixture:last-zone-incoming-peer","revision":1,"viewAs":[{"id":"native-incoming-slash","inputKinds":[],"inputSuits":[],"inputCount":1,"sourceZones":["hand"],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true}]}
        """)!);
        return rules.ToJsonString();
    }
    internal static string Presentation() => JsonSerializer.Serialize(new
    {
        schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
        skills = new[] { Conversion, Driver, Accepted, Completed, Health, Cost, IncomingPeer }.ToDictionary(id => id, id =>
            {
                var entry = new Dictionary<string, object> { ["name"] = id, ["description"] = "真实末区域实体转换与原生返回子窗" };
                if (id is not (Conversion or Driver or IncomingPeer)) entry["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return entry;
            })
    });
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:last-zone", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load(Rules(), Presentation());
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "真实末区域转换")
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:last-zone-idle", "固定其他角色", "无转换能力"));
            b.AddGeneral(new(Owner, "三区域实体拥有者", "supporter", Conversion, "jin", 10, [Driver, Accepted, Completed, Health, Cost]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:last-zone-peer-{i}").ToArray();
            foreach (var peer in peers)
                b.AddGeneral(new(peer, "原生AI", "supporter", "fixture:last-zone-idle", "wei", 10,
                    scenario is "hand" or "grain" ? [IncomingPeer] : []));
            var cards = scenario switch
            {
                "judgment" => new[] { "standard:lightning" },
                "equipment" => new[] { "classic:silver-lion", "standard:crossbow", "standard:draw_two" },
                "grain" => new[] { "classic:wooden-ox" },
                "arrow" => new[] { "standard:arrow_barrage" },
                _ => new[] { "standard:crossbow" }
            };
            b.AddDeck(new("fixture:last-zone-deck", "小型固定区域实体", scenario == "equipment" ? 8 : 4, 0, [])
                { PhysicalCards = Enumerable.Range(0, 72).Select(i => new ContentDeckPhysicalCard(cards[i % cards.Length], Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "真实区域末牌", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Loyalist)] = 1 },
                "fixture:last-zone-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
