using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class InitialHandModifierChecks
{
    private const string Modifier = "fixture:initial-hand-modifier";
    private const string Driver = "fixture:initial-hand-driver";
    private const string Observer = "fixture:initial-hand-observer";
    private const string Owner = "fixture:initial-hand-owner";
    private const string Mode = "identity:classic-initial-hand-fixture";
    private const string TeamMode = "team:initial-hand-fixture";
    private const int BaseInitial = 2;

    public static void ActualDealAndDynamicLimitPreserveRoleAndReplay()
    {
        RejectInvalidInitialModifiers();
        foreach (var team in new[] { false, true })
        {
            var (baseline, baselineRegistry) = Create(enabled: false, interactive: false, team: team);
            AssertInitialDeal(baseline, enabled: false, team: team);
            _ = Cold(baseline, baselineRegistry);
            Accept(baseline, new StartGameCommand());
            Require(!Facts<ProgramBindingStartedEvent>(baseline).Any(fact => fact.SkillId == Observer),
                "An unchanged initial deal cannot become a later hand-gain program opportunity.");

            var (automatic, automaticRegistry) = Create(enabled: true, interactive: false, team: team);
            AssertInitialDeal(automatic, enabled: true, team: team);
            _ = Cold(automatic, automaticRegistry);
        }
        var (fixedBonus, fixedRegistry) = Create(enabled: true, interactive: false, fixedInitial: 2);
        AssertInitialDeal(fixedBonus, enabled: true, team: false, fixedInitial: 2);
        _ = Cold(fixedBonus, fixedRegistry);

        var (game, registry) = Create(enabled: true, interactive: true);
        Require(game.CardMovements.Count == 0 && game.CreateSnapshot(0).Players.All(player => player.HandCount == 0),
            "Interactive setup has no initial hand before the real general selection completes.");
        Accept(game, new StartGameCommand());
        Reach(game, prompt => prompt is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        game = Cold(game, registry);
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, Prompt(game)!.PromptId));
        ReachPlay(game);
        AssertInitialDeal(game, enabled: true, team: false);
        Require(game.CreateSnapshot(0).Players[0] is { MaxHp: 4, Hp: 4, HandCount: 6 } &&
                game.CreateSnapshot(0).Players.Skip(1).All(player => player.MaxHp == 3 && player.HandCount == 5) &&
                !Facts<ProgramBindingStartedEvent>(game).Any(fact => fact.SkillId == Observer) &&
                !Facts<ProgramSkillStartedEvent>(game).Any() &&
                game.CardMovements.All(movement => movement.Reason == CardMoveReasons.InitialDeal),
            "The actual Lord gains four initial entities and ordinary roles gain three during the deal; no GameStarting draw or gain child substitutes for it.");
        game = Cold(game, registry);

        Use(game, "shrink");
        ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0] is { MaxHp: 3, Hp: 3, HandCount: 6 } &&
                Facts<MaximumHpChangedEvent>(game).Count(fact => fact.SkillId == Driver && fact.Delta == -1 && fact.MaximumHp == 3) == 1,
            "A real command changes the owner's public maximum HP after the already-completed initial deal.");
        game = Cold(game, registry);
        Use(game, "hurt");
        ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0] is { MaxHp: 3, Hp: 2, HandCount: 6 } &&
                Facts<ProgramSkillHpLostEvent>(game).Count(fact => fact.SkillId == Driver && fact.Amount == 1 && fact.RemainingHp == 2) == 1,
            "The second real command loses exactly one HP without reissuing any initial entities.");
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        Reach(game, prompt => prompt is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 });
        var discard = Prompt(game)!;
        Require(discard.RequiredCardCount == 1 && game.CreateSnapshot(0).Players[0].HandCount == 6,
            "The current hand limit is current HP two plus current maximum HP three; the initial four-HP bonus is not frozen for later discard queries.");
        AssertPrivacy(game);
        game = Cold(game, registry);
        discard = Prompt(game)!;
        var cardId = game.CreateSnapshot(0).Players[0].Hand[0].Id;
        var beforeRejected = State(game);
        var rejected = game.Submit(new DiscardCardsCommand(1, [cardId], discard.PromptId, game.Revision));
        Require(rejected.Error is not null && State(game) == beforeRejected,
            "A foreign actor cannot spend the owner's entity or mutate the hand-limit command prefix.");
        Accept(game, new DiscardCardsCommand(0, [cardId], discard.PromptId, game.Revision));
        Require(game.CardMovements.Count(movement => movement.CardId == cardId &&
                    movement.From == CardLocation.Hand(0) && movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.HandLimitDiscard) == 1 &&
                Facts<HandLimitDiscardedEvent>(game).Count(fact => fact.ActorSeat == 0 && fact.CardIds.SequenceEqual([cardId])) == 1 &&
                game.CreateSnapshot(0).Players[0].HandCount == 5,
            "The dynamic query produces one real physical hand-limit payment, with no repeated deal or discard after recovery.");
        _ = Cold(game, registry);
    }

    private static void AssertInitialDeal(GameEngine game, bool enabled, bool team, int? fixedInitial = null)
    {
        var snapshot = game.CreateSnapshot(0);
        var requested = snapshot.Players.ToDictionary(player => player.Seat,
            player => BaseInitial + (enabled ? fixedInitial ?? player.MaxHp : 0));
        var expectedSeats = Enumerable.Range(0, requested.Values.Max()).SelectMany(round =>
            snapshot.Players.Where(player => round < requested[player.Seat] &&
                !(team && round == 0 && player.Seat == snapshot.CurrentSeat)).Select(player => player.Seat)).ToArray();
        var movements = game.CardMovements.Where(movement => movement.Reason == CardMoveReasons.InitialDeal).ToArray();
        Require(movements.Select(movement => movement.To.OwnerSeat!.Value).SequenceEqual(expectedSeats) &&
                movements.Select(movement => movement.Sequence).SequenceEqual(Enumerable.Range(1, expectedSeats.Length)) &&
                movements.All(movement => movement.From == CardLocation.DrawPile && movement.To.Zone == CardZoneKind.Hand && movement.CardId > 0) &&
                movements.Select(movement => movement.CardId).Distinct().Count() == expectedSeats.Length &&
                snapshot.Players.All(player => player.HandCount == expectedSeats.Count(seat => seat == player.Seat)) &&
                snapshot.DrawPileCount == 48 - expectedSeats.Length && snapshot.DiscardPileCount == 0 && snapshot.ProcessingCardCount == 0 &&
                game.CreateCardZoneDiagnostics().Count == 48 && game.CreateCardZoneDiagnostics().Select(card => card.CardId).Distinct().Count() == 48,
            "Initial counts are frozen before dealing, preserve the ordinary per-round seat order and the 2v2 starting-seat first-round deduction, and conserve all physical entities.");
        AssertPrivacy(game);
    }

    private static void AssertPrivacy(GameEngine game)
    {
        foreach (var viewer in Enumerable.Range(0, 4))
            Require(game.CreateSnapshot(viewer).Players.All(player => player.Seat == viewer || player.Hand.Count == 0),
                "An initial-hand modifier changes public hand counts without exposing other participants' private entity identities.");
    }

    private static IEnumerable<T> Facts<T>(GameEngine game) where T : IGameEvent => game.Events.Select(item => item.Payload).OfType<T>();
    private static PendingDecision? Prompt(GameEngine game) => game.PendingDecision;
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(viewer => game.CreateSnapshot(viewer)).ToArray(),
        Frames = game.ResolutionStack, Movements = game.CardMovements,
        Zones = game.CreateCardZoneDiagnostics(), Checkpoint = game.CreateCheckpoint(), Events = game.Events
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(game) == State(restored), "Accepted-prefix cold replay preserves all viewer-safe snapshots, exact entities, frames and query results.");
        return restored;
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        if (result.Error is not null) throw new InvalidOperationException(result.Error.ToString());
    }
    private static void Use(GameEngine game, string activation) => Accept(game,
        new UseProgramSkillCommand(0, Driver, activation, [], [], game.Revision, Prompt(game)!.PromptId));
    private static void ReachPlay(GameEngine game) => Reach(game, prompt => prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine game, Func<PendingDecision, bool> expected)
    {
        for (var step = 0; step < 80; step++)
        {
            if (Prompt(game) is { } prompt)
            {
                if (expected(prompt)) return;
                if (prompt.PlayerSeat == 0) throw new InvalidOperationException($"Unexpected initial-hand boundary {prompt.Kind}: {prompt.Prompt}");
            }
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The initial-hand fixture did not reach its bounded real command boundary.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RejectInvalidInitialModifiers()
    {
        var valid = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"{{{Modifier}}}","revision":1,"modifiers":[
              {"id":"initial","query":"initialHandSize","operation":"add","valueExpression":"ownerMaxHp","priority":0}]}]}
            """)!;
        var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new Dictionary<string, object> { [Modifier] = new { name = "初始手牌修正", description = "当前最大体力" } } });
        _ = SkillProgramCatalog.Load(valid.ToJsonString(), presentation);
        foreach (var change in new Action<JsonObject>[]
        {
            node => node["operation"] = "set",
            node => node["operation"] = "unlimited",
            node => node["condition"] = new JsonObject { ["kind"] = "wounded" },
            node => node["valueExpression"] = "ownerLostHp",
            node => node["query"] = "drawCount",
            node => { node.Remove("valueExpression"); node["value"] = -1; }
        })
        {
            var invalid = JsonNode.Parse(valid.ToJsonString())!;
            change(invalid["skills"]![0]!["modifiers"]![0]!.AsObject());
            try { _ = SkillProgramCatalog.Load(invalid.ToJsonString(), presentation); }
            catch (InvalidOperationException error) when (error.Message.Contains("modifiers[0]", StringComparison.Ordinal)) { continue; }
            throw new InvalidOperationException("An unsupported initial-count operation or expression must be rejected at its modifier definition.");
        }
    }

    private static (GameEngine Game, ContentRegistry Registry) Create(bool enabled, bool interactive,
        bool team = false, int? fixedInitial = null)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(enabled, fixedInitial));
        return (GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = team ? null : Role.Lord,
            HumanTeamId = team ? "team:initial-blue" : null, ModeId = team ? TeamMode : Mode,
            UseInteractiveSetup = interactive, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry), registry);
    }

    private sealed class Fixture(bool enabled, int? fixedInitial) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("initial-hand-modifier-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = JsonNode.Parse($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                  {"id":"{{{Driver}}}","revision":1,"activations":[
                    {"id":"shrink","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"changeMaximumHp","target":"owner","amount":-1}]},
                    {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
                  {"id":"{{{Observer}}}","revision":1,"triggers":[{"id":"initial-gain-must-not-run","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{CardMoveReasons.InitialDeal.Value}}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"initial-watch","options":[{"id":"continue"}]}]}]}]}
                """)!;
            if (enabled)
            {
                var modifier = JsonNode.Parse($$$"""
                    {"id":"{{{Modifier}}}","revision":1,"modifiers":[
                      {"id":"initial","query":"initialHandSize","operation":"add","valueExpression":"ownerMaxHp","priority":0},
                      {"id":"limit","query":"handLimit","operation":"add","valueExpression":"ownerMaxHp","priority":0}]}
                    """)!;
                if (fixedInitial is { } amount)
                {
                    var initial = modifier["modifiers"]![0]!.AsObject();
                    initial.Remove("valueExpression"); initial["value"] = amount;
                }
                rules["skills"]!.AsArray().Add(modifier);
            }
            var presentationSkills = new Dictionary<string, object>
            {
                [Driver] = new { name = "真实公开体力变化", description = "降低最大体力并失去体力" },
                [Observer] = new { name = "初始移动观察", description = "初始发牌不能发起获得触发", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            };
            if (enabled) presentationSkills[Modifier] = new { name = "初始手牌修正", description = "按当前最大体力增加初始手牌与手牌上限" };
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new
            { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentationSkills }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, "共享初始手牌规则") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            if (!enabled) builder.AddSkill(new(Modifier, "初始手牌基线", "保持基础初始手牌"));
            builder.AddGeneral(new(Owner, "实际三体力角色", "supporter", Modifier, "shu", 3, [Driver, Observer]));
            var peers = Enumerable.Range(1, 3).Select(index => $"fixture:initial-hand-peer-{index}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "普通三体力角色", "supporter", Modifier, "wei", 3, [Observer]));
            const string deck = "fixture:initial-hand-deck";
            builder.AddDeck(new(deck, "固定小实体牌堆", BaseInitial, 0, [])
            { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            builder.AddMode(new(Mode, "真实身份初始发牌", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
            builder.AddMode(new(TeamMode, "真实2v2初始发牌", 4, 4, new Dictionary<string, int>(), deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { ["team:initial-blue"] = 2, ["team:initial-red"] = 2 }));
        }
    }
}
