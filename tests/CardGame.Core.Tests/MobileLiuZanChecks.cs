using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MobileLiuZanChecks
{
    private const string Skill = "mobile:fenyin", Driver = "fixture:fenyin-driver";
    private const string Gain = "fixture:fenyin-gain", Mode = "identity:mobile-liu-zan-fixture";
    private const string DrawReason = "skill-program." + Skill + ".Draw";

    public static void ActualColorPairsDrawOnceThroughGainChildAndResetOnExtraTurn()
    {
        var (game, registry) = Create(); ReachPlay(game);
        Equip(game, Suit.Spade); ReachPlay(game); Equip(game, Suit.Spade); ReachPlay(game);
        Require(Draws(game) == 0 && Records(game).Length == 2 &&
            Records(game)[0].PreviousActionId is null && Records(game)[1].PreviousActionId == Records(game)[0].ActionId,
            "The first actual use and the same-color next use build one frozen predecessor chain without a draw.");
        Require(typeof(AlternateActualUseColorRecordedEvent).GetProperties().All(property => property.Name != "IsRed") &&
            Records(game).All(fact => !JsonSerializer.Serialize(fact).Contains("IsRed", StringComparison.Ordinal) &&
                !fact.ToString().Contains("IsRed", StringComparison.Ordinal) &&
                !fact.ToString().Contains("True", StringComparison.Ordinal) && !fact.ToString().Contains("False", StringComparison.Ordinal)),
            "Trusted color is absent from public reflected members, serialized events and public string descriptions.");
        Equip(game, Suit.Heart); Reach(game, prompt => prompt.SkillPrompt?.SkillId == Gain);
        var root = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == Skill);
        Require(root.InstructionIndex == 2 && Draws(game) == 1 && Grants(game).Length == 1 &&
            Grants(game)[0].ActionId == Records(game)[2].ActionId &&
            Grants(game)[0].PreviousActionId == Records(game)[1].ActionId,
            "An alternate real equipment use grants and pays one ordinary draw before its actual gain observer suspends the owning frame.");
        game = Cold(game, registry); Continue(game); ReachPlay(game);
        Require(Draws(game) == 1 && !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == Skill),
            "Returning from the real gain child cannot grant or draw a second time.");
        var hand = game.CreateSnapshot(0).Players[0].Hand;
        var slash = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash &&
            action.TargetSeat == 1 && action.ConversionSource?.SkillId == Driver && action.CardId is { } id &&
            hand.Single(card => card.Id == id).Suit == Suit.Spade);
        Accept(game, new PlayCardCommand(0, slash.CardId!.Value, [1], game.Revision, P(game)!.PromptId, slash.PlayedCardKind)
        { ConversionSource = slash.ConversionSource });
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == Gain);
        Require(Draws(game) == 2 && Grants(game).Length == 2 && Records(game).Length == 4 &&
            game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(window =>
                window.Action.Type == CardActionType.Use && window.Action.EffectiveKind == CardKind.Slash &&
                window.Action.PhysicalCards.Count == 1 && window.Action.ActorSeat == 0),
            "A real native Slash commitment records its frozen color before candidate qualification, including the path whose accepted-action output follows commitment.");
        game = Cold(game, registry); Continue(game); ReachPlay(game);
        Use(game, "extra"); ReachPlay(game);
        var turn = game.State.TurnNumber;
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
        Reach(game, prompt => prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.State.TurnNumber > turn);
        Use(game, "stock"); ReachPlay(game); Equip(game, Suit.Heart); ReachPlay(game);
        var next = Records(game).Where(fact => fact.ActualTurn == game.State.TurnNumber).ToArray();
        Require(next is [{ PreviousActionId: null }] && Draws(game) == 2 &&
            game.Events.Select(item => item.Payload).OfType<TurnStartedEvent>().TakeLast(2).All(fact => fact.ActorSeat == 0),
            "The immediately following extra actual turn starts a fresh predecessor chain even for the same owner and a different color.");
        Cold(game, registry);
    }

    private static AlternateActualUseColorRecordedEvent[] Records(GameEngine game) =>
        game.Events.Select(item => item.Payload).OfType<AlternateActualUseColorRecordedEvent>()
            .Where(fact => fact.ActorSeat == 0).ToArray();
    private static AlternateActualUseColorDrawGrantedEvent[] Grants(GameEngine game) =>
        game.Events.Select(item => item.Payload).OfType<AlternateActualUseColorDrawGrantedEvent>().ToArray();
    private static int Draws(GameEngine game) => game.CardMovements.Count(move =>
        move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0) && move.Reason.Value == DrawReason);
    private static PendingDecision? P(GameEngine game) => game.State.PendingDecision;
    private static void ReachPlay(GameEngine game) => Reach(game, prompt => prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Equip(GameEngine game, Suit suit)
    {
        var cards = game.CreateSnapshot(0).Players[0].Hand;
        var action = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Equip &&
            item.CardId is { } id && cards.Single(card => card.Id == id).Suit == suit && item.ConversionSource is null);
        Accept(game, new PlayCardCommand(0, action.CardId!.Value, [], game.Revision, P(game)!.PromptId, action.PlayedCardKind));
    }
    private static void Use(GameEngine game, string binding, IReadOnlyList<int>? targets = null) => Accept(game,
        new UseProgramSkillCommand(0, Driver, binding, [], targets ?? [], game.Revision, P(game)!.PromptId));
    private static void Continue(GameEngine game)
    {
        var prompt = P(game)!;
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue").Id, game.Revision));
    }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 180; step++)
        {
            if (P(game) is { } prompt && predicate(prompt)) return;
            if (P(game) is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 } discard)
                Accept(game, new DiscardCardsCommand(0, discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, game.Revision));
            else if (P(game) is { PlayerSeat: 0 } choice && choice.Choices.Any(item => item.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(game);
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The actual Fenyin boundary was not reached: " + JsonSerializer.Serialize(P(game)));
    }
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        static string State(GameEngine value) => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(value.CreateSnapshot(seat))).ToArray(),
            Frames = JsonSerializer.Serialize(value.ResolutionStack),
            Events = value.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
            TrustedColors = Records(value).Select(fact => new { fact.ActionId, fact.PreviousActionId, fact.IsRed }).ToArray(),
            value.CardMovements
        });
        Require(State(game) == State(restored), "Every view, frame, color predecessor fact and paid movement survives cold command replay.");
        return restored;
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "The actual fixture command was rejected.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(string response = "none")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(response));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand()); Reach(game, prompt => prompt.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:fenyin-owner", game.Revision, P(game)!.PromptId));
        return (game, registry);
    }

    private sealed class Fixture(string response) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mobile-liu-zan", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "mobile:liu-zan");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,
                "viewAs":[
                  {"id":"dodge","inputKinds":[],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true},
                  {"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":true}],
                "triggers":[{"id":"stock-first-preparation","window":"turnStartBeforeNormalFlow","subject":"owner","priority":1000,"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"draw","target":"owner","amount":20}]}],
                "activations":[
                  {"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                  {"id":"duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                  {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                  {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]}]},
              {"id":"{{Gain}}","revision":1,"triggers":[{"id":"gain-pause","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{DrawReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:fenyin-attack","revision":1,"triggers":[{"id":"attack-current-turn","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]},
              {"id":"fixture:fenyin-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var names = new[] { Driver, Gain, "fixture:fenyin-attack", "fixture:fenyin-quiet" };
            var presentation = JsonSerializer.Serialize(new
            {
                schemaVersion = 3,
                skills = names.ToDictionary(id => id, id => new { name = id, description = "实际颜色用牌小夹具",
                    optionLabels = id == Gain ? new Dictionary<string, string> { ["continue"] = "继续" } : [] })
            });
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, "实际颜色用牌小夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new("fixture:fenyin-owner", "留赞", "mobile-liu-zan", Skill, "wu", 4, [Driver, Gain]));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new($"fixture:fenyin-other-{seat}", "其他", "supporter", "fixture:fenyin-quiet", "qun", 8,
                    response == "dodge" && seat == 1 ? ["fixture:fenyin-attack"] : []));
            builder.AddDeck(new("fixture:fenyin-deck", "小型颜色牌堆", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 96).Select(index => new ContentDeckPhysicalCard(
                    "standard:crossbow", index % 2 == 0 ? Suit.Spade : Suit.Heart, 5)).ToArray()
            });
            builder.AddMode(new(Mode, "留赞实际颜色用牌", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:fenyin-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:fenyin-owner", "fixture:fenyin-other-1", "fixture:fenyin-other-2", "fixture:fenyin-other-3"]));
        }
    }
}
