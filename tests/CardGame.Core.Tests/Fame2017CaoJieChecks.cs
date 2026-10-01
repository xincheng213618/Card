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

    public static void NamedDefenseRefusalAndGameLedger()
    {
        var (game, registry) = Create(); var hpBefore = game.CreateSnapshot(0).Players[0].Hp; RequestSlash(game);
        Reach(game, p => p.SkillPrompt?.SkillId == "classic:shouxi"); Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        var hp = game.CreateSnapshot(0).Players[0].Hp;
        Answer(game, c => c.Parameters.GetValueOrDefault("card-name") == nameof(CardKind.Lightning));
        Require(Prompt(game)!.Choices.Count == 1 && Prompt(game)!.Choices[0].Parameters["program-action"] == "named-defense-decline", "No same-name real card leaves only the refusal branch.");
        Replay(game, registry); Accept(game, new AdvanceOneStepCommand(game.Revision));
        Reach(game, p => p.Kind == DecisionKind.PlayCard);
        Require(game.CreateSnapshot(0).Players[0].Hp == hp && game.Events.Select(e => e.Payload).OfType<CardEffectSkippedEvent>().Any(e => e.TargetSeat == 0), "Refusal only skips this Slash's target effect.");
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0); RequestSlash(game);
        Reach(game, p => p.SkillPrompt?.SkillId == "classic:shouxi"); Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Require(Prompt(game)!.Choices.All(c => c.Parameters.GetValueOrDefault("card-name") != nameof(CardKind.Lightning)) &&
            Prompt(game)!.Choices.Count(c => c.Parameters.GetValueOrDefault("card-name") is nameof(CardKind.Slash) or nameof(CardKind.FireSlash) or nameof(CardKind.ThunderSlash)) == 1,
            "Declared names persist across turns, and all three Slash natures share one canonical declaration.");
        Reject(game); Replay(game, registry);
    }

    public static void NamedDefenseOnlyNullifiesOneRealSlashTarget()
    {
        var (game, registry) = Create("spread"); var hp = game.CreateSnapshot(0).Players.Select(p => p.Hp).ToArray(); RequestSlash(game);
        Reach(game, p => p.SkillPrompt?.SkillId == "fixture:caojie-spread");
        var actor = Prompt(game)!.PlayerSeat;
        Answer(game, c => c.Targets.Any(seat => seat != 0 && seat != actor && game.GetCombatDistance(actor, seat) == 1));
        Reach(game, p => p.SkillPrompt?.SkillId == "classic:shouxi"); Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        var use = game.ResolutionStack.OfType<CardUseFrame>().Last();
        Require(use.TargetSeats.Count == 2 && use.TargetSeats.Contains(0), "The defense fixture uses one actual Slash with two real targets.");
        var other = use.TargetSeats.Single(seat => seat != 0); Replay(game, registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("card-name") == nameof(CardKind.Lightning)); Accept(game, new AdvanceOneStepCommand(game.Revision));
        Reach(game, p => p.Kind == DecisionKind.PlayCard);
        Require(game.CreateSnapshot(0).Players[0].Hp == hp[0] && game.CreateSnapshot(other).Players[other].Hp == hp[other] - 1,
            "Refusal nullifies only Cao Jie's effect; the same Slash still hits its other actual target.");
        Replay(game, registry);
    }

    public static void FrozenPopulationPublicDraftAndAiReplay()
    {
        var (game, registry) = Create();
        var before = game.CreateSnapshot(0, true);
        var expected = before.Players.Where(p => p.IsAlive && p.Hand.Count < p.Hp).Select(p => p.Seat).ToArray();
        Require(expected.Contains(0), "The fixture includes Cao Jie in the frozen low-hand population.");
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => p.SkillPrompt?.SkillId == "classic:huimin"); Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "public-draft-show"));
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Last();
        Require(frame.PublicHandDraft!.RecipientSeats.SequenceEqual(expected) && game.CreateSnapshot(0, true).Players[0].Hand.Count >= game.CreateSnapshot(0).Players[0].Hp,
            "The recipients are frozen before the draw, including an owner whose post-draw hand is no longer below HP.");
        var selected = new List<int>();
        for (var i = 0; i < expected.Length; i++)
        {
            Require(game.CreateSnapshot(1).PublicRevealedCards.Count == 0 && game.CreateSnapshot(1).PendingDecision is null, "Partial selection does not reveal unrelated owner hand cards.");
            Reject(game, 1); Replay(game, registry); var id = Prompt(game)!.Choices.First().Cards.Single(); selected.Add(id); Answer(game, c => c.Cards.Contains(id));
        }
        Require(Enumerable.Range(0, 4).All(seat => game.CreateSnapshot(seat).PublicRevealedCards.Select(c => c.Id).Order().SequenceEqual(selected.Order())) &&
            selected.All(id => game.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location == CardLocation.Hand(0)), "The public pool shows exactly the selected real hand entities to all viewers.");
        var start = Enumerable.Range(0, 4).FirstOrDefault(seat => !expected.Contains(seat));
        Require(Prompt(game)!.Choices.Any(c => c.Targets.Contains(start)), "The starting character may lie outside the frozen recipient set.");
        Replay(game, registry); Answer(game, c => c.Targets.Contains(start));
        var order = expected.OrderBy(seat => (seat - start + 4) % 4).ToArray();
        foreach (var recipient in order)
        {
            Require(Prompt(game)!.PlayerSeat == recipient && Prompt(game)!.Choices.All(c => c.Cards.Count == 1 && selected.Contains(c.Cards[0])), "The next frozen recipient chooses one remaining public entity in clockwise order.");
            Replay(game, registry); Reject(game, (recipient + 1) % 4);
            if (recipient == 0) Answer(game, _ => true); else Accept(game, new AdvanceOneStepCommand(game.Revision));
            Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "public-draft-gain") || !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.PublicHandDraft is not null));
        }
        var obtained = game.Events.Select(e => e.Payload).OfType<ProgramHandDraftCardObtainedEvent>().ToArray();
        Require(obtained.Select(e => e.RecipientSeat).SequenceEqual(order) && obtained.Select(e => e.CardId).Distinct().Count() == expected.Length &&
            Enumerable.Range(0, 4).All(seat => game.CreateSnapshot(seat).PublicRevealedCards.Count == 0), "All public entities are consumed once, including a same-hand owner pick, with no residual reveal.");
        Replay(game, registry);
    }

    public static void LoaderRejectsWrongDefenseAndDraftContexts()
    {
        var assembly = typeof(StandardClassicGeneralPackage).Assembly;
        string Read(string kind) { using var s = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.classic-cao-jie." + kind + ".json")!; using var r = new StreamReader(s); return r.ReadToEnd(); }
        var rules = Read("rules"); var presentation = Read("presentation");
        void RejectRules(string changed) { try { SkillProgramCatalog.Load(changed, presentation); } catch (InvalidOperationException) { return; } throw new InvalidOperationException("Invalid shared operation context was accepted."); }
        RejectRules(rules.Replace("\"ownerRelation\":\"target\"", "\"ownerRelation\":\"actor\""));
        RejectRules(rules.Replace("\"slash\",\"fireSlash\",\"thunderSlash\"", "\"duel\""));
        RejectRules(rules.Replace("\"turnEnding\"", "\"turnStartBeforeNormalFlow\""));
        RejectRules(rules.Replace("\"stateId\":\"declared-names\"", "\"stateId\":\"declared-names\",\"amount\":1"));
    }

    public static void DraftNestedGainMovedPoolAndDeath()
    {
        foreach (var mode in new[] { "move", "recipient-death", "owner-death" })
        {
            var (game, registry) = Create(mode);
            var allIds = game.CreateCardZoneDiagnostics().Select(c => c.CardId).Order().ToArray();
            Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
            Reach(game, p => p.SkillPrompt?.SkillId == "classic:huimin"); Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "public-draft-show"));
            var frozen = game.ResolutionStack.OfType<ProgramSkillFrame>().Last().PublicHandDraft!.RecipientSeats;
            foreach (var _ in frozen) Answer(game, _ => true);
            var first = frozen.First(seat => seat != 0);
            Answer(game, c => c.Targets.Contains(first));
            Accept(game, new AdvanceOneStepCommand(game.Revision));
            Require(Prompt(game)!.SkillPrompt?.SkillId == "fixture:caojie-observe" &&
                game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.PublicHandDraft is { Cursor: 1 }),
                "A recipient's real gain pauses the draft before the next recipient decision.");
            Replay(game, registry); Reject(game);
            Answer(game, _ => true);
            var victim = mode == "recipient-death" ? frozen.Single(seat => seat != 0 && seat != first) : 0;
            Answer(game, c => c.Targets.Contains(victim) && (mode != "move" || c.Targets.Contains(first)));
            if (mode == "owner-death")
            {
                for (var i = 0; i < 80 && game.CreateSnapshot(0).Winner == Winner.None; i++)
                {
                    Replay(game, registry);
                    if (Prompt(game) is { PlayerSeat: 0 }) Answer(game, c => c.Parameters.GetValueOrDefault("response") == "pass");
                    else Accept(game, new AdvanceOneStepCommand(game.Revision));
                }
                Require(!game.CreateSnapshot(0).Players[0].IsAlive && game.CreateSnapshot(0).Winner != Winner.None,
                    "Nested owner death legally finishes the game and removes the public draft.");
            }
            else
            {
                Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "public-draft-gain") ||
                    !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.PublicHandDraft is not null));
                if (mode == "recipient-death")
                {
                    Require(!game.CreateSnapshot(0).Players[victim].IsAlive && Prompt(game)!.PlayerSeat == 0,
                        "The dead future recipient is skipped without changing the frozen set or replaying a previous pick.");
                    Answer(game, _ => true);
                }
                else Require(!game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.PublicHandDraft is not null) &&
                    game.Events.Select(e => e.Payload).OfType<ProgramHandDraftCardObtainedEvent>().Count() == 1,
                    "An intervening real hand exchange removes the displayed entities from the pool; later recipients cannot take them again.");
            }
            Require(Enumerable.Range(0, 4).All(seat => game.CreateSnapshot(seat).PublicRevealedCards.Count == 0) &&
                game.CreateCardZoneDiagnostics().Select(c => c.CardId).Order().SequenceEqual(allIds),
                "Nested gain, movement and death preserve every real entity and leave no public-hand leak.");
            Replay(game, registry);
        }
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
