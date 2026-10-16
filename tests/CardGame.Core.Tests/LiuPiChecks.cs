using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LiuPiChecks
{
    private const string General = "ol:liu-pi", Skill = "ol:yicheng", Mode = "identity:classic-liu-pi-fixture";
    private const string Driver = "fixture:liupi-driver", Quiet = "fixture:liupi-quiet", Watch = "fixture:liupi-watch", Stock = "fixture:liupi-stock";
    private const string EqualReason = "skill-program.ol:yicheng.equal-exchange", FullReason = "skill-program.ol:yicheng.full-exchange";
    private const string ReturnReason = "skill-program.ol:yicheng.return-top";

    public static void CancelledEqualAndLowerExchangesPreserveDeckSlotsAndCold()
    {
        var (g, registry) = Start();
        var original = Top(g, V(g, 0).MaxHp);
        Use(g); At(g, LiuPiExchangeStage.HandSelection);
        Need(Root(g).LiuPiExchange!.Table.Select(e => e.CardId).SequenceEqual(original),
            "The actual Yicheng receipt preserves exactly maximum-HP top cards in their original top-to-bottom order.");
        Need(E<ProgramCardsRevealedEvent>(g).Last(e => e.SkillId == Skill).Cards.Select(c => c.Id).SequenceEqual(original),
            "The native public reveal fact preserves the same true top-to-bottom card order.");
        // GetProgramPublicCards aggregates every public binding by sorted ID;
        // rule order belongs to the owning receipt and native reveal fact above.
        Need(Enumerable.Range(0, 4).All(seat => g.CreateSnapshot(seat).PublicRevealedCards.Select(c => c.Id).SequenceEqual(original.Order())),
            "Every public viewer sees exactly the displayed entities in the shared aggregation's stable ID order.");
        Need(P(g) is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(seat => g.CreateSnapshot(seat).PendingDecision is null),
            "The public top row never exposes the owner's unpaid hand selection to another viewer.");
        AuditFrozenReceipt(g); g = Cold(g, registry); Option(g, "select-table"); Play(g);
        Need(Top(g, original.Length).SequenceEqual(original) && !E<LiuPiExchangeCommittedEvent>(g).Any() &&
            original.All(id => Location(g, id) == CardLocation.DrawPile) &&
            g.CardMovements.All(m => m.Reason.Value != "skill-program.ol:yicheng.reveal-cleanup"),
            "Declining the exchange returns the displayed row to its exact original top positions without discarding or exchanging it.");
        Reject(g, new UseProgramSkillCommand(0, Skill, "exchange", [], [], g.Revision, P(g)!.PromptId));
        var previousTurn = g.State.TurnNumber;
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && g.State.TurnNumber > previousTurn);
        var hand = V(g, 0).Hand.Take(2).Select(c => c.Id).ToArray();
        Use(g); At(g, LiuPiExchangeStage.HandSelection);
        var displayed = Root(g).LiuPiExchange!.Table.Select(e => e.CardId).ToArray();
        Card(g, "hand", hand[1]); Card(g, "hand", hand[0]); Option(g, "select-table");
        At(g, LiuPiExchangeStage.TableSelection); Card(g, "table", displayed[3]);
        Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, new ChoiceId("unpublished-unequal-exchange"), g.Revision));
        Card(g, "table", displayed[1]); g = Cold(g, registry); Option(g, "equal-exchange");
        Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var receipt = Root(g).LiuPiExchange!;
        var batch = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last().Batch;
        Need(receipt.Stage == LiuPiExchangeStage.EqualExchangeChildren && receipt.EqualRankAfter == receipt.EqualRankBefore &&
            batch.Movements.Count == 4 && batch.Movements.Count(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 2 &&
            batch.Movements.Count(m => m.From == CardLocation.Processing && m.To == CardLocation.Hand(0)) == 2 &&
            batch.SourceCounts.Single(c => c.Location == CardLocation.Hand(0)) is { CountBefore: var before, CountAfter: var after } && before == after,
            "The real equal exchange commits both directions in one native batch and never invents a transient empty hand.");
        var expected = displayed.ToArray(); expected[1] = hand[0]; expected[3] = hand[1];
        Need(receipt.Table.Select(e => e.CardId).SequenceEqual(expected),
            "First-exchange hand arrival order replaces the selected displayed positions, independent of toggle order.");
        AuditFrozenReceipt(g); g = Cold(g, registry); Continue(g); Play(g);
        Need(Top(g, expected.Length).SequenceEqual(expected) && E<LiuPiExchangeCommittedEvent>(g).Single() is { FullExchange: false, HandCardsGiven: 2, TableCardsTaken: 2 } &&
            hand.All(id => g.CardMovements.Count(m => m.CardId == id && m.Reason.Value == EqualReason) == 1),
            "Equal rank totals do not offer a full exchange, and native child recovery returns the substituted row exactly once.");
        _ = Cold(g, registry);

        (g, registry) = Start(mixed: true);
        var low = V(g, 0).Hand.First(c => c.Rank == 1).Id;
        var high = V(g, 0).Hand.First(c => c.Rank == 13).Id;
        DriverTop(g, high); Play(g); Use(g); At(g, LiuPiExchangeStage.HandSelection);
        Card(g, "hand", low); Option(g, "select-table"); Card(g, "table", high); Option(g, "equal-exchange");
        Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        Need(Root(g).LiuPiExchange!.EqualRankAfter == Root(g).LiuPiExchange!.EqualRankBefore - 12,
            "The strict comparison uses the displayed set's actual changed rank sum, including a real decrease.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(Top(g, 1).Single() == low && E<LiuPiExchangeCommittedEvent>(g).Single() is { FullExchange: false },
            "A lower resulting rank sum returns the row without opening the second exchange.");
        _ = Cold(g, registry);
    }

    public static void StrictIncreaseSwapsUnequalWholeHandAndPrivateTopOrderThroughChildrenCold()
    {
        var (g, registry) = Start(mixed: true, nested: true);
        var low = V(g, 0).Hand.First(c => c.Rank == 1).Id;
        var high = V(g, 0).Hand.First(c => c.Rank == 13).Id;
        DriverTop(g, low); Play(g); var hp = V(g, 0).Hp;
        Use(g); At(g, LiuPiExchangeStage.HandSelection);
        var shown = Root(g).LiuPiExchange!.Table.Select(e => e.CardId).ToArray();
        Need(shown[0] == low && shown.Length == V(g, 0).MaxHp,
            "The selected real low card is genuinely placed on top before maximum-HP display.");
        Card(g, "hand", high); g = Cold(g, registry); Option(g, "select-table"); Card(g, "table", low); Option(g, "equal-exchange");
        Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var first = E<LiuPiExchangeCommittedEvent>(g).Single();
        var handBeforeChild = V(g, 0).Hand.Select(c => c.Id).ToArray();
        Need(first.TableRankAfter == first.TableRankBefore + 12 && Root(g).LiuPiExchange!.EqualRankAfter == first.TableRankAfter,
            "A strict increase is frozen from the completed physical exchange before its gain observer can draw or deal damage.");
        g = Cold(g, registry); Continue(g); At(g, LiuPiExchangeStage.FullOffer);
        var currentHand = V(g, 0).Hand.Select(c => c.Id).ToArray();
        var currentTable = Root(g).LiuPiExchange!.Table.Select(e => e.CardId).ToArray();
        Need(currentHand.Length == handBeforeChild.Length + 1 && V(g, 0).Hp == hp - 1 &&
            Root(g).LiuPiExchange!.EqualRankBefore == first.TableRankBefore && Root(g).LiuPiExchange!.EqualRankAfter == first.TableRankAfter,
            "A genuine nested native draw and damage return to the issued rank receipt without replacing its comparison or old hand snapshot.");
        Need(currentHand.Length != currentTable.Length, "The real fixed fixture deliberately gives the second exchange unequal set sizes.");
        Option(g, "full-order"); At(g, LiuPiExchangeStage.FullOrder);
        Need(Enumerable.Range(1, 3).All(seat => g.CreateSnapshot(seat).PendingDecision is null),
            "Only the owner sees and arranges the unpaid complete outgoing hand order.");
        var order = currentHand.Reverse().ToArray();
        for (var index = 0; index < order.Length; index++)
        {
            Card(g, "order", order[index]);
            if (index == order.Length / 2) g = Cold(g, registry);
        }
        g = Cold(g, registry); Option(g, "full-exchange"); Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var second = E<LiuPiExchangeCommittedEvent>(g).Last();
        var paid = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last().Batch;
        Need(second.FullExchange && second.HandCardsGiven == currentHand.Length && second.TableCardsTaken == currentTable.Length &&
            currentHand.All(id => Location(g, id) == CardLocation.Processing) && currentTable.All(id => Location(g, id) == CardLocation.Hand(0)) &&
            Root(g).LiuPiExchange!.Table.Select(e => e.CardId).SequenceEqual(order) &&
            paid.SourceCounts.Single(c => c.Location == CardLocation.Hand(0)).CountAfter == currentTable.Length,
            "The second native atomic exchange pays the entire post-child hand, obtains the whole displayed set and freezes the chosen top order despite unequal counts.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(Top(g, order.Length).SequenceEqual(order) && V(g, 0).Hand.Count == currentTable.Length + 1 && V(g, 0).Hp == hp - 2 &&
            E<LiuPiExchangeCommittedEvent>(g).Count() == 2 && currentHand.All(id =>
                g.CardMovements.Count(m => m.CardId == id && m.Reason.Value == FullReason) == 1 &&
                g.CardMovements.Count(m => m.CardId == id && m.Reason.Value == ReturnReason) == 1),
            "Full exchange and its actual gain/damage child cold-resume once, then return every outgoing entity to the exact privately selected top order.");
        _ = Cold(g, registry);
    }

    private static void AuditFrozenReceipt(GameEngine g)
    {
        var r = Root(g).LiuPiExchange!; var entry = r.Table.First();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var matches = typeof(GameEngine).GetMethod("LiuPiEntryAvailable", flags)!;
        Need(matches.Invoke(g, [entry]) is true && matches.Invoke(g, [entry with { MovementSequence = entry.MovementSequence - 1 }]) is false &&
            matches.Invoke(g, [entry with { Rank = entry.Rank == 13 ? 1 : 13 }]) is false,
            "An actual current table entity rejects an older movement invoice or forged rank even at the same current source.");
        var immutable = false;
        try { ((IList<LiuPiTableEntry>)r.Table)[0] = entry with { Rank = 2 }; }
        catch (NotSupportedException) { immutable = true; }
        Need(immutable, "The owning receipt freezes its table collection rather than exposing an array setter through diagnostics.");
    }
    private static ProgramSkillFrame Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.LiuPiExchange is not null);
    private static void At(GameEngine g, LiuPiExchangeStage stage) => Reach(g, p => p.SkillPrompt?.SkillId == Skill &&
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "liu-pi") && Root(g).LiuPiExchange!.Stage == stage);
    private static string? Option(PromptChoice c) => c.Parameters.GetValueOrDefault("option");
    private static void Card(GameEngine g, string action, int id) => Answer(g, c => Option(c) == action && c.Cards.SequenceEqual([id]));
    private static void Option(GameEngine g, string action) => Answer(g, c => Option(c) == action);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, Skill, "exchange", [], [], g.Revision, P(g)!.PromptId));
    private static void DriverTop(GameEngine g, int id) => Accept(g, new UseProgramSkillCommand(0, Driver, "top", [id], [], g.Revision, P(g)!.PromptId));
    private static int[] Top(GameEngine g, int count) => g.CreateCardZoneDiagnostics().Where(c => c.Location == CardLocation.DrawPile)
        .OrderByDescending(c => c.ZoneIndex).Take(count).Select(c => c.CardId).ToArray();
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static CardLocation Location(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> expected)
    {
        for (var step = 0; step < 200; step++)
        {
            if (P(g) is { } p && expected(p)) return;
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        var p0 = P(g);
        throw new InvalidOperationException("Liu Pi missed its real boundary: " + JsonSerializer.Serialize(new {
            p0?.Kind, Skill = p0?.SkillPrompt?.SkillId, Stage = g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.LiuPiExchange is not null)?.LiuPiExchange?.Stage,
            Facts = g.Events.TakeLast(5).Select(e => e.Payload.GetType().Name).ToArray() }));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real Liu Pi command."); }
    private static void Reject(GameEngine g, GameCommand command)
    { var state = State(g); var result = g.Submit(command); Need(!result.Accepted && State(g) == state, "An unpublished unequal exchange or repeated phase activation atomically changes no viewer, frame, fact, card or accepted command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(g.CreateSnapshot(seat))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "The exact public row, every private selection/order, native owning frames, movement receipts and accepted commands restore cold."); return restored;
    }
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine Game, ContentRegistry Registry) Start(bool mixed = false, bool nested = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(mixed, nested));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, General, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(bool mixed, bool nested) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-liu-pi", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b, mixed), General);
            var child = nested ? ",{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}" : "";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[{"id":"top","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"top"},{"op":"moveBoundCards","target":"owner","sourceBind":"top","destination":"drawPileTop","awaitMovementTriggers":true}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Stock}}","revision":1,"triggers":[{"id":"stock","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"draw","target":"owner","amount":12}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-exchange-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{EqualReason}}","{{FullReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}{{child}}]}]}
            ]}
            """;
            var ids = new[] { Driver, Quiet, Stock, Watch };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "Small actual Liu Pi exchange fixture.",
                    optionLabels = id == Watch ? new Dictionary<string, string> { ["continue"] = "Continue the actual paid gain child" } : [] }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "Liu Pi native exchange fixture") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new($"fixture:liupi-peer-{seat}", "Quiet native participant", "supporter", Quiet, "wei", 8));
            b.AddDeck(new("fixture:liupi-deck", "Fixed actual exchange entities", 4, 2, []) {
                PhysicalCards = Enumerable.Range(0, 128).Select(index => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, mixed ? index % 2 == 0 ? 1 : 13 : 7)).ToArray() });
            b.AddMode(new(Mode, "Actual Liu Pi", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:liupi-deck", GeneralCandidateCount: 4, GeneralPoolIds: [General, "fixture:liupi-peer-1", "fixture:liupi-peer-2", "fixture:liupi-peer-3"]));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b, bool mixed) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == General
            ? d with { AdditionalSkillIds = new[] { Driver, Watch }.Concat(mixed ? new[] { Stock } : []).ToArray() } : d);
        public void AddCard(ContentCardDefinition d) => b.AddCard(d);
        public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d);
        public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d);
        public void AddMode(ContentModeDefinition d) => b.AddMode(d);
    }
}
