using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheBattleChecks
{
    public static void DynamicRostersPrivateViewsAndTeamAiColdReplay()
    {
        string[] enemies = ["classic:ma-chao", "classic:lu-meng", "classic:guan-yu"];
        foreach (var count in new[] { 1, 2, 3 })
        {
            var setup = Recipe(enemies.Take(count).Select(id => new ShanheCombatantDefinition(id)).ToArray());
            var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup);
            var game = StandardShanheCampaignCatalog.CreateBattle(setup);
            var players = game.CreateSnapshot(0).Players;
            Require(players.Count == count + 1 && players[0].GeneralId == setup.PlayerGeneralId &&
                players.Skip(1).Select(p => p.GeneralId).SequenceEqual(enemies.Take(count)),
                "One, two and three real enemies must use the encounter roster rather than a random activity draft.");
            Require(players[0].IsHuman && players.Skip(1).All(p => !p.IsHuman) &&
                players[0].TeamId == ShanheBattleSetup.PlayerTeamId &&
                players.Skip(1).All(p => p.TeamId == ShanheBattleSetup.EnemyTeamId),
                "Each variable-size encounter has exactly one human and public opposing teams.");
            Require(players.All(p => p.HandCount == 4),
                "The campaign must deal four opening cards to every seat without the 2v2 initiative penalty.");
            PrivateHands(game);
            Require(ComposedSkillContentRegistry.CreateShowcaseForCheckpoint(game.CreateCheckpoint()).ContentHash == registry.ContentHash,
                "Diagnostic checkpoint recovery must reconstruct the same optional campaign package and fixed roster.");
            game = Cold(game, registry);
            Accept(game, new StartGameCommand());
            ReachPlay(game);
            Cold(game, registry);
        }

        var cooperative = Recipe([new("classic:ma-chao", MaxHpBonus: 8)]) with
        {
            AllyRoster = [new("classic:guan-yu")],
            StartingCards = [new(CardKind.Slash, Suit.Spade, 7)],
            StartingEquipment = [new(CardKind.QinggangSword, Suit.Spade, 6)]
        };
        var cooperativeRegistry = StandardShanheCampaignCatalog.CreateBattleRegistry(cooperative);
        var allies = StandardShanheCampaignCatalog.CreateBattle(cooperative);
        Require(allies.CreateSnapshot(0).Players[1] is { IsHuman: false, TeamId: ShanheBattleSetup.PlayerTeamId } &&
            allies.CreateSnapshot(0).Players[2].TeamId == ShanheBattleSetup.EnemyTeamId,
            "An explicit ally occupies its own friendly AI seat before the enemy roster.");
        PrivateHands(allies);
        Accept(allies, new StartGameCommand());
        ReachPlay(allies);
        var actions = allies.GetHumanLegalActions().Where(a =>
            a.Kind is LegalActionKind.Slash or LegalActionKind.EndPlay).ToArray();
        Require(actions.Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.Contains(2)),
            "The real opening weapon and Slash must publish an enemy attack for the team scorer.");
        var (chosen, thought) = new SimpleAiBrain(0, cooperative.BattleSeed, AiDecisionPolicy.CurrentVersion)
            .ChoosePlay(allies.CreateSnapshot(0), actions, 1);
        Require(chosen.Kind == LegalActionKind.Slash && chosen.TargetSeats.SequenceEqual([2]) &&
            thought.Candidates.All(c => double.IsFinite(c.Score)),
            "The AI must attack the public enemy rather than its teammate using the same private player view.");
        Accept(allies, new EndPlayPhaseCommand(0, allies.Revision, allies.PendingDecision!.PromptId));
        for (var step = 0; step < 180 && !allies.AiThoughts.Any(t => t.ActorSeat == 1); step++) Step(allies);
        Require(allies.AiThoughts.Any(t => t.ActorSeat == 1),
            "The actual teammate must take an AI turn through the command session, not remain a decorative roster entry.");
        PrivateHands(allies);
        Cold(allies, cooperativeRegistry);
        var duplicateRoster = Recipe([new("classic:cao-cao"), new("classic:cao-cao")]) with
        { AllyRoster = [new("classic:cao-cao")] };
        var duplicate = StandardShanheCampaignCatalog.CreateBattle(duplicateRoster);
        Require(duplicate.CreateSnapshot(0).Players.Count == 4 && duplicate.CreateSnapshot(0).Players.All(p => p.GeneralId == "classic:cao-cao"),
            "Fixed NPC and companion rosters may repeat a general without requiring phantom unique drafts.");
    }

    public static void BonusesPhysicalOpeningCardsAndGrantedSkillColdReplay()
    {
        var setup = Recipe([new("classic:ma-chao", MaxHpBonus: 20)]) with
        {
            Hp = 3, MaxHpBonus = 2, InitialHandBonus = 2, DrawBonus = 2, SlashBonus = 2,
            ExtraSkillIds = ["classic:wusheng"],
            StartingCards = [new(CardKind.Dodge, Suit.Diamond, 4), new(CardKind.Slash, Suit.Spade, 1),
                new(CardKind.Slash, Suit.Club, 2), new(CardKind.Slash, Suit.Heart, 3)],
            StartingEquipment = [new(CardKind.QinggangSword, Suit.Spade, 6)]
        };
        var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup);
        var game = StandardShanheCampaignCatalog.CreateBattle(setup);
        var opening = game.CreateSnapshot(0).Players[0];
        Require(opening.Hp == 3 && opening.MaxHp == registry.Generals[setup.PlayerGeneralId].BaseHp + 2 &&
            opening.HandCount == 4 + 2 + setup.StartingCards.Count &&
            opening.Skills!.Any(s => s.ContentId == "classic:wusheng") &&
            opening.Skills!.Any(s => s.ContentId == "classic:jianxiong"),
            "Encounter HP, maximum HP, opening bonus and permanent extra skill compose with the real general's own skills.");
        var generated = game.CardMovements.Where(m => m.From == CardLocation.OutsideGame &&
            m.To == CardLocation.Hand(0)).Select(m => m.CardId).ToArray();
        var initialCards = generated.Select(id => opening.Hand.Single(c => c.Id == id)).ToArray();
        Require(initialCards.Select(c => new ShanheStartingCard(c.Kind, c.Suit, c.Rank)).SequenceEqual(setup.StartingCards) &&
            opening.Equipment.Single() is { Kind: CardKind.QinggangSword, Suit: Suit.Spade, Rank: 6 } &&
            game.CreateCardZoneDiagnostics().Select(c => c.CardId).Distinct().Count() == game.CreateCardZoneDiagnostics().Count,
            "Starting cards preserve their exact physical faces and the weapon occupies one genuine equipment zone without duplicate entities.");
        game = Cold(game, registry);
        Accept(game, new StartGameCommand());
        ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0].HandCount == opening.HandCount + 2 + setup.DrawBonus,
            "The first native draw phase must apply the additive campaign draw bonus exactly once.");
        var converted = initialCards[0];
        var conversion = game.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.Slash &&
            a.CardId == converted.Id && a.TargetSeats.SequenceEqual([1]));
        Require(conversion is not null && conversion.ConversionSource is not null,
            "The genuinely granted Wusheng must expose a legal red-Dodge-to-Slash action.");
        Play(game, conversion!);
        game = Cold(game, registry);
        ReachPlay(game);
        foreach (var card in initialCards.Skip(1).Take(2))
        {
            var slash = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash &&
                a.CardId == card.Id && a.TargetSeats.SequenceEqual([1]));
            Play(game, slash);
            ReachPlay(game);
        }
        Require(!game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash) &&
            game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == initialCards[3].Id),
            "Base one Slash plus the two campaign uses must stop at three even with another physical Slash still in hand.");
        Require(initialCards.Take(3).All(c => game.CardMovements.Any(m =>
                m.CardId == c.Id && m.To == CardLocation.DiscardPile)) &&
            game.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Any(e =>
                e.CardId == converted.Id && e.CardKind == CardKind.Slash),
            "Local starting cards use the ordinary physical discard lifecycle, and the granted conversion records its actual effective Slash.");
        Cold(game, registry);
    }

    public static void RejectInvalidRecipesAndFreezeExternalCollections()
    {
        var skills = new List<string> { "classic:wusheng" };
        var nestedSkills = new List<string> { "classic:mashu" };
        var cards = new List<ShanheStartingCard> { new(CardKind.Peach, Suit.Heart, 3) };
        var equipment = new List<ShanheStartingCard> { new(CardKind.QinggangSword, Suit.Spade, 6) };
        var enemies = new List<ShanheCombatantDefinition> { new("classic:lu-meng", ExtraSkillIds: nestedSkills) };
        var friends = new List<ShanheCombatantDefinition> { new("classic:guan-yu") };
        var setup = Recipe(enemies) with
        {
            ExtraSkillIds = skills, StartingCards = cards, StartingEquipment = equipment, AllyRoster = friends
        };
        var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup);
        var game = StandardShanheCampaignCatalog.CreateBattle(setup);
        var checkpoint = game.CreateCheckpoint();
        var before = State(game);
        skills.Clear(); nestedSkills.Clear(); cards.Clear(); equipment.Clear(); enemies.Clear(); friends.Clear();
        Require(State(game) == before && ShanheBattleSetup.ValueEquals(
            checkpoint.Options.ShanheBattle, game.CreateCheckpoint().Options.ShanheBattle),
            "Caller-owned outer lists and nested enemy skills cannot mutate a constructed battle or its saved opening recipe.");
        game = Cold(game, registry);
        var frozen = game.CreateCheckpoint().Options.ShanheBattle!;
        var options = game.CreateCheckpoint().Options;
        foreach (var invalid in new[]
        {
            frozen with { Hp = 0 }, frozen with { MaxHpBonus = -1 }, frozen with { SlashBonus = 11 },
            frozen with { EnemyRoster = [] }, frozen with { EncounterOrdinal = 0 },
            frozen with { ExtraSkillIds = ["fixture:missing-shanhe-skill"] },
            frozen with { StartingCards = [new(CardKind.Slash, Suit.None, 1)] },
            frozen with { StartingEquipment = [new(CardKind.QinggangSword), new(CardKind.Crossbow)] }
        })
            Reject(() => GameEngine.CreateStandard(options with { ShanheBattle = invalid }, registry));
        Reject(() => GameEngine.CreateStandard(options with { Seed = unchecked(options.Seed + 1) }, registry));
        Reject(() => GameEngine.CreateStandard(options with { HumanSeat = 1 }, registry));
        Reject(() => GameEngine.CreateStandard(options with { ShanheBattle = null }, registry));
        Require(State(game) == before, "Rejected external recipes must leave the existing battle and replay journal untouched.");
    }

    private static ShanheBattleSetup Recipe(IReadOnlyList<ShanheCombatantDefinition> enemies) =>
        new(73, 1, "fixture:shanhe-encounter", "classic:cao-cao", 4, 0, 0, 0, 0,
            [], [], [], enemies, [], new string('a', 64));

    private static void PrivateHands(GameEngine game)
    {
        var count = game.CreateSnapshot(0).Players.Count;
        for (var viewer = 0; viewer < count; viewer++)
            Require(game.CreateSnapshot(viewer).Players.All(p => p.Seat == viewer
                ? p.Hand.Count == p.HandCount : p.Hand.Count == 0),
                "Every ordinary snapshot exposes only its viewer's own hand; friendly AI hands are equally private.");
    }

    private static void Play(GameEngine game, LegalAction action) => Accept(game,
        new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision,
            game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId)
        { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 160; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            Step(game);
        }
        throw new InvalidOperationException("The real Shanhe battle did not reach its human play boundary.");
    }

    private static void Step(GameEngine game)
    {
        if (game.PendingDecision is { Kind: DecisionKind.DiscardCards } discard)
            Accept(game, new DiscardCardsCommand(discard.PlayerSeat,
                discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, game.Revision));
        else if (game.PendingDecision is { Choices.Count: > 0 } choice)
            Accept(game, new AnswerPromptCommand(choice.PlayerSeat, choice.PromptId, choice.Choices[0].Id, game.Revision));
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }

    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))),
        Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        Facts = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
        game.CardMovements, Zones = game.CreateCardZoneDiagnostics(),
        Checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint())
    });

    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(game) == State(restored),
            "Cold JSON replay must preserve the frozen recipe, all private viewers, real card zones, commands, facts and pending frames.");
        return restored;
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("An invalid Shanhe battle opening recipe was accepted.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "A real Shanhe battle command was rejected.");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
