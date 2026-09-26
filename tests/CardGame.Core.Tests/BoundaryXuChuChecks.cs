using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryXuChuChecks
{
    private const string GeneralId = "boundary:xu-chu";
    private const string SkillId = "boundary:luoyi";

    public static void DefinitionAndGeneralFilterContract()
    {
        var current = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        Require(current.Generals[GeneralId] is { BaseHp: 4, FactionId: "wei" } general &&
                general.SkillIds.SequenceEqual([SkillId]) &&
                current.Skills[SkillId].Program?.MinimumRulesVersion == 171 &&
                current.Modes["identity:classic-5"].GeneralPoolIds?.Contains(GeneralId) == true &&
                current.Modes["identity:classic-8"].GeneralPoolIds?.Contains(GeneralId) == true,
            "2014 Xu Chu must remain an independent Wei four-HP general in both current formal pools.");
        var valid = GenericRules("""
            {"op":"filterBoundCards","target":"owner","sourceBind":"cards","resultBind":"selected",
             "suits":["spade"],"categories":["trick"],"cardKinds":["peach"]}
            """);
        var catalog = SkillProgramCatalog.Load(valid, GenericPresentation);
        Require(catalog.Programs.ContainsKey("fixture:filter"),
            "A non-Xu-Chu union of trick or Peach intersected with a suit must be legal.");
        Reject(GenericRules("""
            {"op":"filterBoundCards","target":"owner","sourceBind":"cards","resultBind":"selected",
             "categories":["basic"],"effectiveSuitForRef":{"kind":"owner"}}
            """), "effectiveSuitForRef");
        Reject(GenericRules("""
            {"op":"filterBoundCards","target":"owner","sourceBind":"cards","resultBind":"selected",
             "cardKinds":[]}
            """), "nonempty");
        Reject(GenericRules("""
            {"op":"filterBoundCards","target":"owner","sourceBind":"cards","resultBind":"selected",
             "categories":["basic"]}
            """, cleanUp: false), "fully consumed");

        // This schema-54 graph was valid with four suit atoms. It must not inherit the
        // larger typed-card partition limit merely because schema 55 exists.
        var nested = new List<string>
        {
            """{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"root","visibility":"public"}"""
        };
        for (var index = 0; index < 21; index++)
            nested.Add($$"""{"op":"selectCardSubset","target":"owner","sourceBind":"{{(index == 0 ? "root" : $"s{index - 1}")}}","resultBind":"s{{index}}","minimumCards":0,"maximumCards":1,"maximumRankSum":13,"aiOrder":"mostCardsThenRankSum"}""");
        nested.Add("""{"op":"moveBoundCards","target":"owner","sourceBind":"s20","destination":"ownerHand"}""");
        for (var index = 19; index >= 0; index--)
            nested.Add($$"""{"op":"moveBoundCards","target":"owner","sourceBind":"s{{index}}","exceptBind":"s{{index + 1}}","destination":"discardPile"}""");
        nested.Add("""{"op":"moveBoundCards","target":"owner","sourceBind":"root","exceptBind":"s0","destination":"discardPile"}""");
        var legacyNested = $$"""
            {"schemaVersion":61,"skills":[{"id":"fixture:filter","revision":1,"minimumRulesVersion":170,
            "triggers":[{"id":"check","window":"drawPhaseStarting","subject":"owner","optional":true,
            "drawPhaseMode":"replacement","effects":[{{string.Join(',', nested)}}]}]}]}
            """;
        Require(SkillProgramCatalog.Load(legacyNested, GenericPresentation).Programs.ContainsKey("fixture:filter"),
            "A legacy repeated-subset resource graph must keep its four-suit partition acceptance.");
    }

    public static void RevealedCardsPartitionAndReplay()
    {
        var registry = Registry();
        var results = new HashSet<int>();
        for (var seed = 1; seed <= 256 && results.Count < 4; seed++)
        {
            var game = Start(registry, seed);
            ReachDrawPrompt(game);
            var beforeHand = game.CreateSnapshot(0, true).Players[0].HandCount;
            var beforeMoves = game.CardMovements.Count;
            var checkpoint = RoundTrip(game.CreateCheckpoint());
            var resumed = GameReplay.Restore(checkpoint, registry);
            Answer(game, "activate");
            Answer(resumed, "activate");
            var reveal = game.Events.Select(e => e.Payload).OfType<ProgramCardsRevealedEvent>()
                .Single(e => e.SkillId == SkillId);
            var gained = reveal.Cards.Where(card => IsLuoyiCard(card.Kind)).Select(card => card.Id).ToHashSet();
            var discarded = reveal.Cards.Where(card => !IsLuoyiCard(card.Kind)).Select(card => card.Id).ToHashSet();
            var moves = game.CardMovements.Skip(beforeMoves).ToArray();
            var grant = game.Events.Select(e => e.Payload).OfType<CardDamageModifierGrantedEvent>()
                .Single(e => e.Modifier.Source.SkillId == SkillId).Modifier;
            Require(reveal.Cards.Count == 3 &&
                    gained.All(id => moves.Count(move => move.CardId == id &&
                        move.To == CardLocation.Hand(0)) == 1) &&
                    discarded.All(id => moves.Count(move => move.CardId == id &&
                        move.To == CardLocation.DiscardPile) == 1) &&
                    reveal.Cards.All(card => game.CreateCardZoneDiagnostics().All(zone =>
                        zone.CardId != card.Id || zone.Location != CardLocation.Processing)) &&
                    game.CreateSnapshot(0, true).Players[0].HandCount == beforeHand + gained.Count &&
                    grant is { Amount: 1,
                        Expiration: SkillProgramDamageModifierExpiration.NextOwnerTurnStart,
                        SourceScope: SkillProgramDamageModifierSourceScope.DamageSource } &&
                    State(game) == State(resumed) && Events(game).SequenceEqual(Events(resumed)),
                $"Luoyi reveal/partition/grant/replay failed for seed={seed}, gained={gained.Count}.");
            results.Add(gained.Count);
        }
        Require(results.SetEquals([0, 1, 2, 3]),
            $"A bounded mixed deck must exercise 0, 1, 2 and 3 Luoyi hits, found {string.Join(',', results.Order())}.");

        var decline = Start(registry, 1);
        ReachDrawPrompt(decline);
        var hand = decline.CreateSnapshot(0, true).Players[0].HandCount;
        Answer(decline, "skip");
        Require(decline.CreateSnapshot(0, true).Players[0].HandCount == hand + 2 &&
                decline.Events.All(e => e.Payload is not ProgramCardsRevealedEvent) &&
                decline.Events.All(e => e.Payload is not CardDamageModifierGrantedEvent),
            "Declining Luoyi must preserve the ordinary draw and make no damage grant.");

        for (var available = 0; available <= 3; available++)
        {
            var shortRegistry = ContentRegistry.Build(new StandardContentPackage(),
                new StandardActiveSkillExpansionPackage(includeJijiu: true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
                new Scenario(25 + available));
            var shortGame = Start(shortRegistry, 1);
            ReachDrawPrompt(shortGame);
            var before = shortGame.CreateSnapshot(0, true).Players[0].HandCount;
            var beforePile = shortGame.CreateSnapshot(0, true).DrawPileCount;
            Answer(shortGame, "activate");
            var revealed = shortGame.Events.Select(item => item.Payload)
                .OfType<ProgramCardsRevealedEvent>().Single(item => item.SkillId == SkillId);
            Require(revealed.Cards.Count == available &&
                    shortGame.CreateSnapshot(0, true).Players[0].HandCount ==
                    before + revealed.Cards.Count(card => IsLuoyiCard(card.Kind)) &&
                    shortGame.Events.Select(item => item.Payload)
                        .OfType<CardDamageModifierGrantedEvent>()
                        .Any(item => item.Modifier.Source.SkillId == SkillId),
                $"An exhausted pile must reveal and partition exactly {available} available cards " +
                $"(revealed={revealed.Cards.Count}, pileBefore={beforePile}, handBefore={before}, " +
                $"handAfter={shortGame.CreateSnapshot(0, true).Players[0].HandCount}, " +
                $"hands={string.Join(',', shortGame.CreateSnapshot(0, true).Players.Select(player => player.HandCount))}, " +
                $"grant={shortGame.Events.Select(item => item.Payload).OfType<CardDamageModifierGrantedEvent>().Count()}).");
        }
    }

    public static void DamageScopeAndDuration()
    {
        var store = new TurnCardUseEffectStore();
        var source = new CardUseEffectSource(SkillId, "test:luoyi", 0, "seat-0:test:luoyi");
        var grant = store.GrantDamageModifier(1, 0, 10, 0, source,
            [CardKind.Slash, CardKind.Duel], 1,
            SkillProgramDamageModifierExpiration.NextOwnerTurnStart,
            SkillProgramDamageModifierSourceScope.DamageSource);
        var old = store.GrantDamageModifier(1, 0, 11, 0, source,
            [CardKind.Slash, CardKind.Duel], 1);
        Require(store.GetDamageModifiers(2, 1, 0, 1, CardKind.Duel, false)
                    .SequenceEqual([grant]) &&
                store.GetDamageModifiers(2, 1, 0, 1, CardKind.Duel, true).Count == 0 &&
                store.GetDamageModifiers(2, 1, 1, 1, CardKind.Duel, false).Count == 0 &&
                store.GetDamageModifiers(1, 0, 0, 1, CardKind.Duel, false)
                    .SequenceEqual([grant]) &&
                store.GetDamageModifiers(1, 0, 0, 0, CardKind.Duel, false)
                    .SequenceEqual([grant, old]),
            "The new modifier must match the actual damage source even in another player's Duel, " +
            "while old owner-used grants and chain damage keep their old limits.");
        Require(store.ExpireDamageModifiersOnDeath(0).SequenceEqual([grant.GrantSequence]) &&
                store.DamageModifiers.SequenceEqual([old]),
            "Death must clean the future grant without changing the old grant's expiry event timing.");
        var future = store.GrantDamageModifier(1, 0, 12, 0, source,
            [CardKind.Slash], 1,
            SkillProgramDamageModifierExpiration.NextOwnerTurnStart,
            SkillProgramDamageModifierSourceScope.DamageSource);
        Require(store.ExpireDamageModifiersAtOwnerTurnStart(0, 1).Count == 0 &&
                store.ExpireDamageModifiersAtOwnerTurnStart(0, 2).SequenceEqual([future.GrantSequence]) &&
                store.DamageModifiers.SequenceEqual([old]),
            "The grant must survive other turns and expire before the owner's next turn.");

        var registry = Registry();
        var found = false;
        for (var seed = 1; seed <= 256 && !found; seed++)
        {
            var game = Start(registry, seed);
            ReachDrawPrompt(game);
            Answer(game, "activate");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Could not reach Xu Chu's play phase.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                continue;
            var action = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.Slash && item.TargetSeat is not null);
            if (action is null) continue;
            var target = action.TargetSeat!.Value;
            var before = game.CreateSnapshot(0, true).Players[target].Hp;
            var checkpoint = RoundTrip(game.CreateCheckpoint());
            var restored = GameReplay.Restore(checkpoint, registry);
            var played = game.Submit(new PlayCardCommand(0, action.CardId!.Value,
                action.TargetSeats, game.Revision, game.PendingDecision.PromptId, action.PlayedCardKind));
            var replayed = restored.Submit(new PlayCardCommand(0, action.CardId!.Value,
                action.TargetSeats, restored.Revision, restored.PendingDecision!.PromptId, action.PlayedCardKind));
            Require(played.Accepted && replayed.Accepted &&
                    game.CreateSnapshot(0, true).Players[target].Hp == before - 2 &&
                    game.Events.Select(item => item.Payload).OfType<ProgramCardDamageModifiedEvent>()
                        .Any(item => item.Source.SkillId == SkillId && item.TargetSeat == target &&
                                     item.BaseAmount == 1 && item.ModifiedAmount == 2) &&
                    State(game) == State(restored) && Events(game).SequenceEqual(Events(restored)),
                played.Error?.Message ?? replayed.Error?.Message ??
                $"Natural Slash damage or replay failed at seed {seed}.");
            var grantSequence = game.Events.Select(item => item.Payload)
                .OfType<CardDamageModifierGrantedEvent>()
                .Single(item => item.Modifier.Source.SkillId == SkillId).Modifier.GrantSequence;
            for (var step = 0; step < 120 &&
                 !(game.CreateSnapshot(0, true) is { TurnNumber: >= 6, CurrentSeat: 0 }); step++)
            {
                var prompt = game.PendingDecision;
                GameCommand command = prompt?.Kind switch
                {
                    null => new AdvanceCommand(game.Revision),
                    DecisionKind.PlayCard => new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId),
                    DecisionKind.DiscardCards => new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision),
                    _ => new AnswerPromptCommand(0, prompt.PromptId,
                        prompt.Choices.First(choice =>
                            choice.Parameters.GetValueOrDefault("program-action") == "skip" ||
                            choice.Parameters.GetValueOrDefault("response") == "take-damage" ||
                            choice.Parameters.GetValueOrDefault("action") == "skip").Id,
                        game.Revision)
                };
                var moved = game.Submit(command);
                Require(moved.Accepted, moved.Error?.Message ??
                    $"Could not reach Xu Chu's next turn ({prompt?.Kind.ToString() ?? "advance"}).");
            }
            var timeline = game.Events.Select(item => item.Payload).ToArray();
            var expirationIndex = Array.FindIndex(timeline, item =>
                item is TurnCardUseEffectsExpiredEvent expired &&
                expired.GrantSequences.Contains(grantSequence));
            var nextTurnIndex = Array.FindIndex(timeline, item =>
                item is TurnStartedEvent { TurnNumber: >= 6, ActorSeat: 0 });
            Require(nextTurnIndex >= 0 && expirationIndex >= 0 && expirationIndex < nextTurnIndex,
                "Luoyi's grant must expire before Xu Chu's next turn-start event.");
            found = true;
        }
        Require(found, "No bounded natural Slash fixture reached the new damage grant.");
    }

    public static void NaturalReverseDuelAndReplay()
    {
        var registry = Registry();
        var slashes = 0;
        var incomingDuels = 0;
        // Seed 1 was found with the mixed public fixture; keep the regression fixed and cheap.
        for (var seed = 1; seed <= 1; seed++)
        {
            var game = Start(registry, seed);
            ReachDrawPrompt(game);
            Answer(game, "activate");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Could not reach play for reverse Duel.");
            var action = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.Slash && item.TargetSeat is not null);
            if (action is null || game.PendingDecision is not { Kind: DecisionKind.PlayCard }) continue;
            var played = game.Submit(new PlayCardCommand(0, action.CardId!.Value,
                action.TargetSeats, game.Revision, game.PendingDecision.PromptId, action.PlayedCardKind));
            if (!played.Accepted || !game.Events.Select(item => item.Payload)
                    .OfType<ProgramCardDamageModifiedEvent>()
                    .Any(item => item.Source.SkillId == SkillId && item.ModifiedAmount == 2))
                continue;
            slashes++;
            for (var step = 0; step < 100 && game.CreateSnapshot(0, true).TurnNumber < 6; step++)
            {
                var prompt = game.PendingDecision;
                if (prompt is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0,
                    IncomingCard: CardKind.Duel })
                {
                    incomingDuels++;
                    var duelUse = game.Events.Select(item => item.Payload)
                        .OfType<CardUseDeclaredEvent>().Last(item => item.CardKind == CardKind.Duel);
                    Require(game.CreateSnapshot(0, true).TurnNumber is > 1 and < 6 &&
                            duelUse.SourceSeat != 0 &&
                            game.Events.Select(item => item.Payload)
                                .OfType<TurnCardUseEffectsExpiredEvent>()
                                .All(item => !item.GrantSequences.Contains(game.Events
                                    .Select(entry => entry.Payload).OfType<CardDamageModifierGrantedEvent>()
                                    .Single(entry => entry.Modifier.Source.SkillId == SkillId)
                                    .Modifier.GrantSequence)),
                        "The Duel must be used by another player after Xu Chu's Slash, " +
                        "while the Luoyi grant remains active across turns.");
                    var slash = prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "slash");
                    if (slash is null) break;
                    var checkpoint = RoundTrip(game.CreateCheckpoint());
                    var beforeResponseSequence = game.Events[^1].Sequence;
                    var replay = GameReplay.Restore(checkpoint, registry);
                    if (!ResolveReverseDuel(game, duelUse.SourceSeat, beforeResponseSequence) ||
                        !ResolveReverseDuel(replay, duelUse.SourceSeat, beforeResponseSequence)) break;
                    Require(State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
                        $"Reverse Duel continuation must replay from its human Slash prompt (seed {seed}).");
                    return;
                }
                GameCommand command = prompt?.Kind switch
                {
                    null => new AdvanceCommand(game.Revision),
                    DecisionKind.PlayCard => new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId),
                    DecisionKind.DiscardCards => new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision),
                    _ => new AnswerPromptCommand(0, prompt!.PromptId,
                        prompt.Choices.First().Id, game.Revision)
                };
                var moved = game.Submit(command);
                Require(moved.Accepted, moved.Error?.Message ?? "Could not advance reverse Duel search.");
            }
        }
        throw new InvalidOperationException(
            $"No natural reverse Duel after Luoyi Slash was found (slash seeds={slashes}, incoming Duel={incomingDuels}).");
    }

    private static bool ResolveReverseDuel(GameEngine game, int duelUserSeat,
        long beforeResponseSequence)
    {
        for (var step = 0; step < 25; step++)
        {
            var damageEvent = game.Events.LastOrDefault(item =>
                item.Payload is DamageRequestedEvent { SourceCard: CardKind.Duel,
                    SourceSeat: 0 } damage && damage.TargetSeat == duelUserSeat);
            if (damageEvent?.Payload is DamageRequestedEvent damage &&
                damageEvent.Sequence > beforeResponseSequence)
            {
                var duelUse = game.Events.Last(item => item.Payload is CardUseDeclaredEvent
                    { CardKind: CardKind.Duel } declared && declared.SourceSeat == duelUserSeat);
                var modified = game.Events.LastOrDefault(item =>
                    item.Payload is ProgramCardDamageModifiedEvent { ModifiedAmount: 2 } value &&
                    value.Source.SkillId == SkillId && value.TargetSeat == duelUserSeat);
                return damage.Amount == 2 && modified is not null &&
                       duelUse.Sequence < modified.Sequence &&
                       modified.Sequence < damageEvent.Sequence;
            }
            var prompt = game.PendingDecision;
            if (prompt is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0,
                IncomingCard: CardKind.Duel })
            {
                var slash = prompt.Choices.FirstOrDefault(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "slash");
                if (slash is null) return false;
                var answered = game.Submit(new AnswerPromptCommand(0, prompt.PromptId,
                    slash.Id, game.Revision));
                if (!answered.Accepted) return false;
            }
            else
            {
                var moved = game.Submit(new AdvanceCommand(game.Revision));
                if (!moved.Accepted) return false;
            }
        }
        return false;
    }

    private static bool IsLuoyiCard(CardKind kind) =>
        CardCatalog.Get(kind).CategoryName == "基本牌" ||
        EquipmentCatalog.IsEquipment(kind) && EquipmentCatalog.Get(kind).Slot == EquipmentSlot.Weapon ||
        kind == CardKind.Duel;

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario());

    private static GameEngine Start(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Scenario.ModeId, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Xu Chu fixture did not start.");
        var setup = game.PendingDecision!;
        Require(setup.ValidContentIds.Contains(GeneralId) &&
                game.Submit(new SelectGeneralCommand(0, GeneralId, game.Revision, setup.PromptId)).Accepted,
            "Xu Chu was not selectable in the fixture.");
        return game;
    }

    private static void ReachDrawPrompt(GameEngine game)
    {
        var result = game.Submit(new AdvanceCommand(game.Revision));
        Require(result.Accepted && game.PendingDecision is
            { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: SkillId, PlayerSeat: 0 },
            result.Error?.Message ?? "Xu Chu draw-phase choice was not reached.");
    }

    private static void Answer(GameEngine game, string action)
    {
        var prompt = game.PendingDecision!;
        var choice = prompt.Choices.Single(item => item.Parameters.GetValueOrDefault("program-action") == action);
        var result = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? $"Xu Chu {action} failed.");
    }

    private static string GenericRules(string filter, bool cleanUp = true) => $$"""
        {"schemaVersion":61,"skills":[{"id":"fixture:filter","revision":1,"minimumRulesVersion":170,
        "triggers":[{"id":"check","window":"drawPhaseStarting","subject":"owner","optional":true,
        "drawPhaseMode":"replacement","effects":[
        {"op":"revealTopCards","target":"owner","amount":3,"resultBind":"cards","visibility":"public"},
        {{filter}},
        {"op":"moveBoundCards","target":"owner","sourceBind":"selected","destination":"ownerHand"}
        {{(cleanUp ? ",{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"cards\",\"exceptBind\":\"selected\",\"destination\":\"discardPile\"}" : "")}}
        ]}]}]}
        """;
    private const string GenericPresentation =
        """{"schemaVersion":3,"skills":{"fixture:filter":{"name":"通用筛牌","description":"测试"}}}""";
    private static void Reject(string rules, string message)
    {
        try { _ = SkillProgramCatalog.Load(rules, GenericPresentation); }
        catch (InvalidOperationException error) when (error.Message.Contains(message, StringComparison.OrdinalIgnoreCase))
        { return; }
        throw new InvalidOperationException($"Expected a filter definition rejection containing '{message}'.");
    }
    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(int cardCount = 180) : IGameContentPackage
    {
        internal const string ModeId = "identity:boundary-xu-chu-check-5";
        public PackageManifest Manifest { get; } = new("boundary-xu-chu-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 140, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            var ids = Enumerable.Range(1, 4).Select(i => $"fixture:xu-chu-target-{i}").ToArray();
            foreach (var id in ids)
                builder.AddGeneral(new ContentGeneralDefinition(id, "测试目标", "supporter",
                    "standard:none", "qun", BaseHp: 12));
            var cardIds = new[] { "standard:slash", "standard:duel", "standard:crossbow",
                "standard:bagua", "standard:dismantlement", "standard:offensive_horse" };
            builder.AddDeck(new ContentDeckRecipe("fixture:xu-chu-mixed-deck", "裸衣测试牌堆", 5, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, cardCount).Select(index =>
                    new ContentDeckPhysicalCard(cardIds[index % cardIds.Length],
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(ModeId, "界许褚测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:xu-chu-mixed-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [GeneralId, .. ids]));
        }
    }
}
