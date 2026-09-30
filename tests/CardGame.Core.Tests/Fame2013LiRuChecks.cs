using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2013LiRuChecks
{
    private const string General = "classic:li-ru";
    private const string Mode = "identity:classic-li-ru-check";
    public static void TopdeckMovementTriggersPrecedeChallenge()
    {
        var registry = Registry("standard:duel", 1, observer: true);
        var game = Start(registry, general: "fixture:li-ru-observer");
        PlayBoundary(game);
        var action = game.GetHumanLegalActions().Single(a => a.ProgramSkillId == "classic:mieji");
        var card = action.SelectableCardIds.First();
        var target = action.SelectableTargetSeats.First();
        Accept(game.Submit(new UseProgramSkillCommand(0, "classic:mieji", action.ProgramActivationId!, [card], [target], game.Revision, game.PendingDecision!.PromptId)));
        Settle(game);
        var cost = game.CardMovements.Single(m => m.CardId == card && m.To == CardLocation.DrawPile);
        var draw = game.CardMovements.Single(m => m.CardId == card && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Sequence > cost.Sequence);
        var discard = Discards(game, target).Single();
        Require(cost.Sequence < draw.Sequence && draw.Sequence < discard.Sequence,
            "The topdeck cost's movement trigger must draw that physical top card before the target discards.");
        EqualReplay(game, registry);
    }
    public static void FireChainAndDyingResumeReplay()
    {
        var registry = Registry("standard:iron_chain", 3);
        var game = Start(registry);
        PlayBoundary(game);
        var chain = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.IronChain && a.CardId is not null);
        Accept(game.Submit(new PlayCardCommand(0, chain.CardId!.Value, [1, 4], game.Revision, game.PendingDecision!.PromptId)));
        Settle(game);
        PlayBoundary(game);
        var action = game.GetHumanLegalActions().Single(a => a.ProgramSkillId == "classic:fencheng");
        Accept(game.Submit(new UseProgramSkillCommand(0, "classic:fencheng", action.ProgramActivationId!, [], [], game.Revision, game.PendingDecision!.PromptId)));
        Settle(game);
        var damage = game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().ToArray();
        Require(damage.Select(d => d.TargetSeat).Order().SequenceEqual([1, 4]) && damage.All(d => d.Nature == DamageNature.Fire && d.SourceSeat == 0 && d.Amount == 2),
            "Fencheng fire damage must use the normal chain propagation path.");
        Require(!game.CreateSnapshot(0, true).Players[1].IsChained && !game.CreateSnapshot(0, true).Players[4].IsChained,
            "Chained damage must clear both chain states before completing the walk.");
        EqualReplay(game, registry);

        var dyingRegistry = Registry("standard:slash", 0, 2);
        var dying = Start(dyingRegistry);
        PlayBoundary(dying);
        var fire = dying.GetHumanLegalActions().Single(a => a.ProgramSkillId == "classic:fencheng");
        Accept(dying.Submit(new UseProgramSkillCommand(0, "classic:fencheng", fire.ProgramActivationId!, [], [], dying.Revision, dying.PendingDecision!.PromptId)));
        Settle(dying);
        Require(dying.Events.Select(e => e.Payload).OfType<PlayerDyingEvent>().Any(), "Fencheng must enter the ordinary dying/rescue boundary.");
        EqualReplay(dying, dyingRegistry);
    }
    public static void SequentialNonTricksResumeAndReplay()
    {
        var registry = Registry("mixed", 4);
        GameEngine? selected = null;
        LegalAction? action = null;
        var target = -1;
        for (var seed = 0; seed < 40 && selected is null; seed++)
        {
            var candidate = Start(registry, seed);
            PlayBoundary(candidate);
            var available = candidate.GetHumanLegalActions().FirstOrDefault(a => a.ProgramSkillId == "classic:mieji");
            if (available is null) continue;
            var eligible = available.SelectableTargetSeats.FirstOrDefault(seat =>
                candidate.CreateSnapshot(seat, true).Players[seat].Hand.All(c => c.Kind == CardKind.Slash), -1);
            if (eligible < 0) continue;
            selected = candidate; action = available; target = eligible;
        }
        var game = selected ?? throw new InvalidOperationException("No deterministic non-trick fixture found.");
        var before = game.CreateSnapshot(target, true).Players[target].Hand.Count;
        var wrongCost = game.CreateSnapshot(0, true).Players[0].Hand.FirstOrDefault(c => c.Kind == CardKind.Slash);
        if (wrongCost is not null)
        {
            var unchanged = State(game);
            var revision = game.Revision;
            var result = game.Submit(new UseProgramSkillCommand(0, "classic:mieji", action!.ProgramActivationId!, [wrongCost.Id], [target], game.Revision, game.PendingDecision!.PromptId));
            Require(!result.Accepted && game.Revision == revision && State(game) == unchanged,
                "A non-trick cost must be rejected before any usage or physical movement changes.");
        }
        Accept(game.Submit(new UseProgramSkillCommand(0, "classic:mieji", action!.ProgramActivationId!, [action.SelectableCardIds.First()], [target], game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 50 && Discards(game, target).Length == 0; step++) Advance(game);
        Require(Discards(game, target).Length == 1, "Non-trick branch must commit the first discard independently.");
        Require(game.CreateSnapshot(target).PendingDecision is { IsPrivate: true } && game.CreateSnapshot(0).PendingDecision is null,
            "The opponent's second discard must retain its private chooser boundary.");
        var middle = game.CreateCheckpoint();
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(middle)), registry);
        Settle(game);
        foreach (var command in game.AcceptedCommands.Skip(middle.Commands.Count)) Accept(restored.Submit(command));
        Require(Discards(game, target).Length == 2 && game.CreateSnapshot(target, true).Players[target].Hand.Count == before - 2,
            "The complementary branch must discard two non-tricks in separate moves.");
        Require(State(game) == State(restored) && JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(restored.CardMovements),
            "A checkpoint between sequential discards must resume the second discard exactly once.");
        EqualReplay(game, registry);

        var shortageRegistry = Registry("mixed", 1);
        for (var seed = 0; seed < 40; seed++)
        {
            var shortGame = Start(shortageRegistry, seed);
            PlayBoundary(shortGame);
            var shortAction = shortGame.GetHumanLegalActions().FirstOrDefault(a => a.ProgramSkillId == "classic:mieji");
            if (shortAction is null) continue;
            var shortTarget = shortAction.SelectableTargetSeats.FirstOrDefault(seat =>
                shortGame.CreateSnapshot(seat).Players[seat].Hand.Single().Kind == CardKind.Slash, -1);
            if (shortTarget < 0) continue;
            Accept(shortGame.Submit(new UseProgramSkillCommand(0, "classic:mieji", shortAction.ProgramActivationId!,
                [shortAction.SelectableCardIds.First()], [shortTarget], shortGame.Revision, shortGame.PendingDecision!.PromptId)));
            Settle(shortGame);
            Require(shortGame.ResolutionStack.Count == 0 && Discards(shortGame, shortTarget).Length == 1 &&
                shortGame.CreateSnapshot(shortTarget).Players[shortTarget].HandCount == 0,
                "With only one non-trick, the target must discard it and the second unavailable payment must finish.");
            EqualReplay(shortGame, shortageRegistry);
            return;
        }
        throw new InvalidOperationException("No deterministic one-card shortage fixture found.");
    }
    private static CardMovementRecord[] Discards(GameEngine game, int seat) => game.CardMovements.Where(m =>
        m.From.OwnerSeat == seat && m.To == CardLocation.DiscardPile && m.Reason.Value.Contains("ChooseCategoryAlternativeDiscard", StringComparison.Ordinal)).ToArray();
    public static void CategoryDiscardAndPhysicalCostReplay()
    {
        var registry = Registry("standard:duel", 4);
        var game = Start(registry);
        PlayBoundary(game);
        var action = game.GetHumanLegalActions().Single(a => a.ProgramSkillId == "classic:mieji");
        var card = action.SelectableCardIds.First();
        var target = action.SelectableTargetSeats.First();
        var before = game.CreateCheckpoint();
        var priorCount = game.CreateSnapshot(target, true).Players[target].Hand.Count;
        Accept(game.Submit(new UseProgramSkillCommand(0, "classic:mieji", action.ProgramActivationId!, [card], [target], game.Revision, game.PendingDecision!.PromptId)));
        Settle(game);
        Require(game.CreateSnapshot(target, true).Players[target].Hand.Count == priorCount - 1,
            "A target with only tricks must discard one trick.");
        Require(game.CreateCardZoneDiagnostics().Where(zone => zone.Location == CardLocation.DrawPile).OrderBy(zone => zone.ZoneIndex).Last().CardId == card,
            "Mieji must put the exact physical black trick at the draw-pile top.");
        Require(!game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "classic:mieji"),
            "Mieji must be unavailable after its phase use.");
        EqualReplay(game, registry);
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(before)), registry);
        foreach (var command in game.AcceptedCommands.Skip(before.Commands.Count)) Accept(restored.Submit(command));
        Require(State(game) == State(restored), "The paid category discard must restore and resume identically.");
    }

    public static void EscalatingDiscardDamageResetAndReplay()
    {
        var registry = Registry("standard:slash", 2);
        var game = Start(registry);
        PlayBoundary(game);
        var action = game.GetHumanLegalActions().Single(a => a.ProgramSkillId == "classic:fencheng");
        var revision = game.Revision;
        var invalid = game.Submit(new UseProgramSkillCommand(0, "classic:fencheng", action.ProgramActivationId!, [], [1], revision, game.PendingDecision!.PromptId));
        Require(!invalid.Accepted && revision == game.Revision, "Invalid limited-skill targets must not pay the limited use.");
        Accept(game.Submit(new UseProgramSkillCommand(0, "classic:fencheng", action.ProgramActivationId!, [], [], game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 200 && game.ResolutionStack.Count != 0; step++) Advance(game);
        Require(game.ResolutionStack.Count == 0, "Fencheng must finish its participant walk.");
        Require(!game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "classic:fencheng"), "Fencheng must remain spent.");
        var damage = game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().ToArray();
        Require(damage.All(d => d.SourceSeat == 0 && d.Amount == 2 && d.Nature == DamageNature.Fire),
            "Every declined Fencheng payment must cause two fire damage attributed to the owner.");
        Require(damage.Select(d => d.TargetSeat).SequenceEqual([3]), "Two-card opponents must pay 1, then 2, take damage, then reset to 1.");
        var payments = game.CardMovements.Where(m => m.Reason.Value.Contains("EscalatingDiscardOrDamage", StringComparison.Ordinal)).ToArray();
        Require(payments.Select(m => m.From.OwnerSeat).SequenceEqual([1, 2, 2, 4]), "Payment order and reset must follow increasing turn seats.");
        EqualReplay(game, registry);
    }

    public static void EmptyHandOptionalDamageAndReplay()
    {
        var registry = Registry("standard:slash", 0);
        var game = Start(registry);
        PlayBoundary(game);
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 100; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } && prompt.SkillPrompt?.SkillId == "classic:juece")
            {
                var declined = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                var skip = declined.PendingDecision!.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                Accept(declined.Submit(new AnswerPromptCommand(0, declined.PendingDecision.PromptId, skip.Id, declined.Revision)));
                Settle(declined);
                Require(!declined.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(), "Declining Juece must cause no damage.");
                EqualReplay(declined, registry);
                var choice = prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") != "skip") ?? prompt.Choices.First();
                Accept(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision)));
                if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } select)
                    Accept(game.Submit(new AnswerPromptCommand(0, select.PromptId, select.Choices.First().Id, game.Revision)));
                Settle(game);
                Require(game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(d => d.SourceSeat == 0 && d.TargetSeat != 0 && d.Amount == 1),
                    "Juece must cause one damage to an empty-hand other character.");
                EqualReplay(game, registry);
                return;
            }
            Advance(game);
        }
        throw new InvalidOperationException("Juece optional end-phase prompt was not reached.");
    }

    private static ContentRegistry Registry(string card, int initial, int bankHp = 8, bool observer = false) => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario(card, initial, bankHp, observer));
    private static GameEngine Start(ContentRegistry registry, int seed = 7, string general = General)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, PlayerCount = 5, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, general, game.Revision, game.PendingDecision!.PromptId)));
        return game;
    }
    private static void PlayBoundary(GameEngine game)
    {
        for (var step = 0; step < 100; step++)
        { if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return; Advance(game); }
        throw new InvalidOperationException("Li Ru play phase not reached.");
    }
    private static void Settle(GameEngine game)
    { for (var step = 0; step < 200 && game.ResolutionStack.Count != 0; step++) Advance(game); }
    private static void Advance(GameEngine game) => Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Li Ru command rejected.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(game.CreateSnapshot(0, true));
    private static void EqualReplay(GameEngine game, ContentRegistry registry)
    {
        var checkpoint = game.CreateCheckpoint();
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint)), registry);
        Require(State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)) &&
            JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(replay.CardMovements) &&
            GameCheckpointJson.Serialize(checkpoint) == GameCheckpointJson.Serialize(replay.CreateCheckpoint()),
            "Li Ru command, event, physical movement and checkpoint replay must be exact.");
    }
    private static string[] Events(GameEngine game) => game.Events.Select(e =>
        $"{e.Id}|{e.ParentId}|{e.Sequence}|{e.Revision}|{e.CorrelationId}|{e.Payload.GetType().FullName}|{JsonSerializer.Serialize(e.Payload, e.Payload.GetType())}").ToArray();
    private sealed class Scenario(string cardId, int initial, int bankHp, bool observer) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("li-ru-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            var pool = new List<string> { observer ? "fixture:li-ru-observer" : General };
            if (observer)
            {
                var rules = $$"""
                  {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:cost-observer","revision":1,
                  "minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"triggers":[{"id":"after-cost","window":"cardsMoved",
                  "subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","optional":false,
                  "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
                  """;
                const string presentation = """{"schemaVersion":3,"skills":{"fixture:cost-observer":{"name":"移动观察","description":"测试"}}}""";
                var program = SkillProgramCatalog.Load(rules, presentation).Programs["fixture:cost-observer"];
                builder.AddSkill(new ContentSkillDefinition("fixture:cost-observer", "移动观察", "测试") { Program = program });
                builder.AddGeneral(new("fixture:li-ru-observer", "李儒测试", "supporter", "classic:mieji", "qun", BaseHp: 3,
                    AdditionalSkillIds: ["classic:juece", "classic:fencheng", "fixture:cost-observer"]));
            }
            for (var index = 0; index < 4; index++)
            { var id = "fixture:li-ru-bank-" + index; pool.Add(id); builder.AddGeneral(new(id, "测试对手", "supporter", "standard:none", "qun", BaseHp: bankHp)); }
            builder.AddDeck(new("fixture:li-ru-deck", "测试牌堆", initial, 0, [])
                { PhysicalCards = Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(cardId == "mixed" ? i % 4 == 0 ? "standard:duel" : "standard:slash" : cardId, Suit.Spade, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "李儒测试", 5, 5, new Dictionary<string, int> { [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }, "fixture:li-ru-deck", GeneralCandidateCount: 5, GeneralPoolIds: pool));
        }
    }
}
