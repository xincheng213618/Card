using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class Fame2017CaoJieChecks
{
    public static void NamedDefenseActorPaymentAndPrivateTake()
    {
        var (game, registry) = Create(); var hpBefore = game.CreateSnapshot(0).Players[0].Hp; RequestSlash(game);
        Reach(game, p => p.SkillPrompt?.SkillId == "classic:shouxi"); Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Answer(game, c => c.Parameters.GetValueOrDefault("card-name") == nameof(CardKind.Slash));
        var payer = Prompt(game)!.PlayerSeat; var cost = Prompt(game)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        Require(payer != 0 && Prompt(game)!.IsPrivate && game.CreateSnapshot(0).PendingDecision is null, "The actual Slash actor privately chooses its own real named cost.");
        Reject(game, 0); Replay(game, registry);
        Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.CardMovements.Count(m => m.CardId == cost && m.From == CardLocation.Hand(payer) && m.To == CardLocation.DiscardPile) == 1,
            "Normal AI advancement pays one actual named card.");
        Reach(game, p => p.SkillPrompt?.SkillId == "fixture:caojie-observe");
        Require(!game.CardMovements.Any(m => m.Reason.Value == "skill-program.classic:shouxi.named-take"), "Taking a card waits for the paid discard's nested observer.");
        Replay(game, registry); Accept(game, new AdvanceOneStepCommand(game.Revision));
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "named-defense-take-hand"));
        Require(Prompt(game)!.Choices.Where(c => c.Parameters.GetValueOrDefault("program-action") == "named-defense-take-hand").All(c => c.Cards.Count == 0), "Private target hand slots reveal no entity or face to the attacker.");
        Reject(game, 0); Replay(game, registry); Accept(game, new AdvanceOneStepCommand(game.Revision));
        Reach(game, p => p.Kind == DecisionKind.PlayCard);
        Require(game.CreateSnapshot(0).Players[0].Hp == hpBefore - 1, "Paid defense leaves the actual Slash effective.");
        Require(game.CardMovements.Count(m => m.Reason.Value == "skill-program.classic:shouxi.named-take" && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(payer)) == 1,
            "After payment and nested continuation, the actor takes exactly one real Cao Jie card and the Slash still proceeds.");
        Replay(game, registry);
    }

    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static void RequestSlash(GameEngine game)
    {
        var target = Enumerable.Range(1, 3).First(seat => game.GetCombatDistance(0, seat) == 1 && game.CreateSnapshot(seat, true).Players[seat].Hand.Any(c => c.Kind == CardKind.Slash));
        Accept(game, new UseProgramSkillCommand(0, "classic:tiaoxin", "taunt", [], [target], game.Revision, Prompt(game)!.PromptId));
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "request-slash");
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> test)
    { var p = Prompt(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(test).Id, game.Revision)); }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> test)
    {
        for (var i = 0; i < 180; i++)
        {
            if (Prompt(game) is { } p && test(p)) return;
            if (Prompt(game) is { PlayerSeat: 0 } own)
            {
                if (own.Kind == DecisionKind.PlayCard) Accept(game, new EndPlayPhaseCommand(0, game.Revision, own.PromptId));
                else Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip" || c.Parameters.GetValueOrDefault("response") == "pass");
            }
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Cao Jie fixture missed its actual prompt: " + JsonSerializer.Serialize(game.ResolutionStack));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack, Moves = g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static void Replay(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "Every viewer, movement and typed cursor survives checkpoint/command JSON replay.");
    private static void Reject(GameEngine game, int? seat = null)
    { var before = State(game); var p = Prompt(game)!; var result = game.Submit(new AnswerPromptCommand(seat ?? p.PlayerSeat, p.PromptId, seat is null ? new("unpublished") : p.Choices[0].Id, game.Revision)); Require(!result.Accepted && before == State(game), "Invalid input rejects atomically without moving an entity or cursor."); }
    private static void Accept(GameEngine game, GameCommand command) { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Expected accepted command."); }
    private static void Require(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
    private static (GameEngine, ContentRegistry) Create(string interrupt = "none")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(interrupt));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:classic-caojie", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, registry);
        Accept(game, new StartGameCommand()); Accept(game, new SelectGeneralCommand(0, "fixture:caojie-owner", game.Revision, Prompt(game)!.PromptId)); Reach(game, p => p.Kind == DecisionKind.PlayCard); return (game, registry);
    }
    private sealed class Fixture(string interrupt) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-caojie", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:caojie-observe","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.classic:shouxi.named-cost"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"observed","options":[{"id":"continue"}]}]}]}]}""";
            if (interrupt is "move" or "recipient-death" or "owner-death")
            {
                var doc = System.Text.Json.Nodes.JsonNode.Parse(rules)!;
                var selection = interrupt == "move" ? """{"op":"selectTargets","target":"owner","targetKind":"anyLiving","minimumTargets":2,"maximumTargets":2,"targetAiOrder":"stable"}""" : """{"op":"selectTarget","target":"owner","targetKind":"otherLiving"}""";
                var op = interrupt == "move" ? """{"op":"exchangeSelectedTargetHands","target":"owner"}""" : """{"op":"loseHp","target":"selectedTarget","amount":20}""";
                doc["skills"]![0]!["triggers"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"gained","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.classic:huimin.public-draft"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"observed-gain","options":[{"id":"continue"}]},""" + selection + "," + op + "]}"));
                rules = doc.ToJsonString();
            }
            if (interrupt == "spread")
            {
                var doc = System.Text.Json.Nodes.JsonNode.Parse(rules)!;
                doc["skills"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"fixture:caojie-spread","revision":1,"triggers":[{"id":"spread","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"priority":100,"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"addCurrentCardUseTarget","target":"selectedTarget"}]}]}"""));
                rules = doc.ToJsonString();
            }
            var presentation = System.Text.Json.Nodes.JsonNode.Parse("""{"schemaVersion":3,"skills":{"fixture:caojie-observe":{"name":"付款观察","description":"暂停真实弃牌","optionLabels":{"continue":"继续"}},"fixture:caojie-spread":{"name":"多目标杀","description":"添加一个真实杀目标"}}}""");
            if (interrupt != "spread") presentation!["skills"]!.AsObject().Remove("fixture:caojie-spread");
            var catalog = SkillProgramCatalog.Load(rules, presentation!.ToJsonString());
            builder.AddSkill(new("fixture:caojie-observe", "付款观察", "暂停真实弃牌") { Program = catalog.Programs["fixture:caojie-observe"] });
            if (interrupt == "spread") builder.AddSkill(new("fixture:caojie-spread", "多目标杀", "添加一个真实杀目标") { Program = catalog.Programs["fixture:caojie-spread"] });
            builder.AddGeneral(new("fixture:caojie-owner", "曹节测试", "supporter", "classic:shouxi", "qun", 6, ["classic:huimin", "classic:tiaoxin"], GeneralGender.Female));
            foreach (var n in Enumerable.Range(1, 3)) builder.AddGeneral(new($"fixture:caojie-{n}", "目标", "supporter", "fixture:caojie-observe", "wei", n == 1 ? 4 : 8, interrupt == "spread" ? ["fixture:caojie-spread"] : []));
            builder.AddDeck(new("fixture:caojie-deck", "真实杀牌", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 140).Select(i => new ContentDeckPhysicalCard("standard:slash", i % 2 == 0 ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-caojie", "曹节测试", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:caojie-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:caojie-owner", "fixture:caojie-1", "fixture:caojie-2", "fixture:caojie-3"]));
        }
    }
}
