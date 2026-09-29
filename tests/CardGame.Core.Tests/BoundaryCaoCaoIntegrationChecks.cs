using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryCaoCaoIntegrationChecks
{
    private const string General = "boundary:cao-cao";
    private const string Jianxiong = "boundary:jianxiong";
    private const string DuelMode = "identity:classic-boundary-cao-duel-check-5";
    private const string SpearMode = "identity:classic-boundary-cao-spear-check-5";

    public static void DuelDamageClaimsPhysicalCardAndReplays()
    {
        var registry = Registry();
        GameEngine? found = null;
        for (var seed = 1; seed <= 1 && found is null; seed++)
        {
            var game = Start(registry, DuelMode, General, seed);
            for (var step = 0; step < 1100 && game.State.Winner == Winner.None; step++)
            {
                if (game.PendingDecision?.SkillPrompt?.SkillId == Jianxiong &&
                    game.Events.Select(item => item.Payload).OfType<DamageRequestedEvent>()
                        .LastOrDefault() is { TargetSeat: 0, SourceCard: CardKind.Duel })
                {
                    found = game;
                    break;
                }
                if (!Advance(game)) break;
            }
        }
        Require(found is not null, "No bounded real Duel damage to boundary Cao Cao was found.");
        var gameAtDamage = found!;
        var physical = gameAtDamage.CreateCardZoneDiagnostics().Where(item =>
            item.Location == CardLocation.Processing && item.CardKind == CardKind.Duel).ToArray();
        Require(physical.Length == 1, "Exactly this Duel's physical card must await Jianxiong in Processing.");
        var checkpoint = RoundTrip(gameAtDamage.CreateCheckpoint());
        var claimed = GameReplay.Restore(checkpoint, registry);
        Require(State(claimed) == State(gameAtDamage) && Events(claimed).SequenceEqual(Events(gameAtDamage)),
            "The pending Duel Jianxiong offer must restore exactly.");
        Answer(claimed, "program-action", "activate");
        var option = Prompt(claimed);
        Require(option.IsPrivate && option.Choices.Select(item => item.Parameters.GetValueOrDefault("option-id"))
                    .Order(StringComparer.Ordinal).SequenceEqual(["claim", "draw"]),
            "The physical Duel must offer both private draw and claim options.");
        Answer(claimed, "option-id", "claim");
        var award = claimed.Events.Select(item => item.Payload).OfType<ProgramDamageCardsClaimedEvent>()
            .Single(item => item.SkillId == Jianxiong);
        Require(award.CardIds.SequenceEqual([physical[0].CardId]) &&
                claimed.CreateCardZoneDiagnostics().Single(item => item.CardId == physical[0].CardId)
                    .Location == CardLocation.Hand(0),
            "Duel Jianxiong must claim the exact physical Duel card once.");
        AssertReplay(claimed, registry);
    }

    private static (GameEngine Game, int SourceSeat, int CaoSeat, IReadOnlyList<int> CostCardIds)
        FindSpearDamage(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 1; seed++)
        {
            var game = Start(registry, SpearMode, "fixture:zhangba-source", seed);
            for (var step = 0; step < 60 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                if (!Advance(game)) break;
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play) continue;
            var players = game.CreateSnapshot(0, true).Players;
            var source = players[0];
            var cao = players.FirstOrDefault(item => item.GeneralId == General);
            var weapon = source.Hand.FirstOrDefault(card => card.Kind == CardKind.ZhangbaSerpentSpear);
            if (cao is null || weapon is null || source.Hand.Count < 3 || cao.Hp <= 1) continue;
            var equip = game.Submit(new PlayCardCommand(0, weapon.Id, [], game.Revision, play.PromptId));
            if (!equip.Accepted) continue;
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard && !Advance(game)) continue;
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } afterEquip) continue;
            var action = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.UseEquipmentEffect &&
                item.EquipmentKind == CardKind.ZhangbaSerpentSpear &&
                item.SelectableCardIds.Count >= 2 && item.SelectableTargetSeats.Contains(cao.Seat));
            if (action is null) continue;
            var costs = action.SelectableCardIds.Take(2).ToArray();
            var used = game.Submit(new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear,
                costs, [cao.Seat], game.Revision, afterEquip.PromptId));
            if (!used.Accepted) continue;
            for (var step = 0; step < 24 && game.State.Winner == Winner.None; step++)
            {
                if (HostPending(game)?.SkillPrompt?.SkillId == Jianxiong &&
                    game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                        .LastOrDefault() is { TargetSeat: var hit } && hit == cao.Seat)
                    return (game, 0, cao.Seat, costs);
                if (game.PendingDecision is { PlayerSeat: 0 }) break;
                var next = game.Submit(new AdvanceOneStepCommand(game.Revision));
                if (!next.Accepted) break;
            }
        }
        throw new InvalidOperationException("No two-card Zhangba hit on boundary Cao Cao was found at fixed seed 1.");
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario());

    private static GameEngine Start(ContentRegistry registry, string mode, string general, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, ModeId = mode, HumanSeat = 0, HumanRole = Role.Lord,
            UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 12, AiPolicyVersion = 2
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Cao Cao integration fixture failed to start.");
        var setup = Prompt(game);
        Require(setup.ValidContentIds.Contains(general) &&
                game.Submit(new SelectGeneralCommand(0, general, game.Revision, setup.PromptId)).Accepted,
            "Requested integration general was not selectable.");
        return game;
    }

    private static bool Advance(GameEngine game)
    {
        var prompt = game.PendingDecision;
        if (prompt is null || prompt.PlayerSeat != 0)
            return game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted;
        if (prompt.Kind == DecisionKind.PlayCard)
            return game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)).Accepted;
        if (prompt.Kind == DecisionKind.DiscardCards)
            return game.Submit(new DiscardCardsCommand(0,
                prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision)).Accepted;
        var choice = prompt.Choices.FirstOrDefault(item => item.Cards.Count == 0) ?? prompt.Choices.LastOrDefault();
        return choice is not null && game.Submit(new AnswerPromptCommand(0, prompt.PromptId,
            choice.Id, game.Revision)).Accepted;
    }

    private static void Answer(GameEngine game, string key, string value)
    {
        var prompt = Prompt(game);
        var choice = prompt.Choices.Single(item => item.Parameters.GetValueOrDefault(key) == value);
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? $"Cao Cao {value} was rejected.");
    }

    private static PendingDecision Prompt(GameEngine game) => game.PendingDecision ??
        throw new InvalidOperationException("Integration fixture lost its expected prompt.");
    private static PendingDecision? HostPending(GameEngine game) =>
        (PendingDecision?)typeof(GameEngine)
            .GetField("_pendingDecision", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game);
    private static GameCheckpoint RoundTrip(GameCheckpoint value) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(value));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void AssertReplay(GameEngine game, ContentRegistry registry)
    {
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "Cao Cao integration checkpoint/replay state or events diverged.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scenario : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("cao-cao-integration-scenario", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhangba-source", "蛇矛发起者",
                "supporter", "standard:none", "qun", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:duel-deck", "Duel Deck", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard("standard:duel", (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddDeck(new ContentDeckRecipe("fixture:zhangba-deck", "Spear Deck", 5, 2,
                [new ContentDeckCardCount("classic:zhangba-serpent-spear", 40),
                 new ContentDeckCardCount("standard:slash", 120)]));
            var roles = new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
            };
            builder.AddMode(new ContentModeDefinition(DuelMode, "Cao Duel", 5, 5,
                roles, "fixture:duel-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General, "classic:xiahou-dun", "classic:guan-yu",
                    "classic:zhang-fei", "classic:sun-quan"]));
            builder.AddMode(new ContentModeDefinition(SpearMode, "Cao Spear", 5, 5,
                roles, "fixture:zhangba-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: ["fixture:zhangba-source", General, "classic:guan-yu",
                    "classic:zhang-fei", "classic:sun-quan"]));
        }
    }
}
