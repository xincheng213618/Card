using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class KongchengChecks
{
    public static void DuelTargetingAndLegacy()
    {
        Require(GameCheckpoint.CurrentRulesVersion >= 15,
            "Formal Kongcheng targeting must have an explicit rules version.");

        var rule = SkillRegistry.Get(SkillKind.Kongcheng);
        var empty = new PlayerSkillContext(1, 3, 3, 0, TurnPhase.Play);
        var holdingCards = empty with { HandCount = 1 };
        Require(rule.ProhibitsCardTarget(empty, CardKind.Slash) &&
                rule.ProhibitsCardTarget(empty, CardKind.FireSlash) &&
                rule.ProhibitsCardTarget(empty, CardKind.ThunderSlash) &&
                rule.ProhibitsCardTarget(empty, CardKind.Duel),
            "Kongcheng must prohibit every Slash kind and Duel while the owner has no hand cards.");
        Require(!rule.ProhibitsCardTarget(holdingCards, CardKind.Duel) &&
                !rule.ProhibitsCardTarget(empty, CardKind.DrawTwo),
            "Kongcheng must not prohibit Duel with a hand card or unrelated card kinds.");

        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var seed = FindSeedWithHumanDuel(registry);
        var current = CreateGame(registry, seed, GameCheckpoint.CurrentRulesVersion);
        var legacy = CreateGame(registry, seed, rulesVersion: 14);
        ArrangeEmptyKongchengTarget(current, targetSeat: 1);
        ArrangeEmptyKongchengTarget(legacy, targetSeat: 1);

        var currentStart = current.Submit(new StartGameCommand());
        var legacyStart = legacy.Submit(new StartGameCommand());
        Require(currentStart.Accepted && legacyStart.Accepted,
            "The paired Kongcheng fixtures failed to start.");
        Require(currentStart.Result.Status == EngineStatus.AwaitingHumanPlay &&
                legacyStart.Result.Status == EngineStatus.AwaitingHumanPlay,
            "The paired Kongcheng fixtures did not reach the human play boundary.");

        var duelId = current.CreateSnapshot(0, revealAll: true).Players[0].Hand
            .Single(card => card.Kind == CardKind.Duel).Id;
        var legacyDuelId = legacy.CreateSnapshot(0, revealAll: true).Players[0].Hand
            .Single(card => card.Kind == CardKind.Duel).Id;
        Require(duelId == legacyDuelId,
            "Rules-version fixtures must retain the same physical Duel card.");

        Require(!current.GetHumanLegalActions().Any(action =>
                action.Kind == LegalActionKind.Duel &&
                action.CardId == duelId &&
                action.TargetSeat == 1),
            "Rules v15 must remove an empty-hand Kongcheng owner from Duel targets.");
        Require(current.GetHumanLegalActions().Any(action =>
                action.Kind == LegalActionKind.Duel &&
                action.CardId == duelId &&
                action.TargetSeat == 2),
            "Formal Kongcheng must not remove unrelated living Duel targets.");
        Require(legacy.GetHumanLegalActions().Any(action =>
                action.Kind == LegalActionKind.Duel &&
                action.CardId == legacyDuelId &&
                action.TargetSeat == 1),
            "Rules v14 must retain the historical Slash-only Kongcheng behavior.");
        Require(current.CreateSnapshot(0, revealAll: true).Players[1].SkillDescription
                    .Contains("【决斗】", StringComparison.Ordinal) &&
                !legacy.CreateSnapshot(0, revealAll: true).Players[1].SkillDescription
                    .Contains("【决斗】", StringComparison.Ordinal),
            "The player projection must describe formal Kongcheng only for the current classic rules.");

        var beforeState = current.SerializeState();
        var beforeEvents = current.Events.Count;
        var beforeCommands = current.AcceptedCommands.Count;
        var prompt = current.PendingDecision ??
            throw new InvalidOperationException("The current Kongcheng fixture has no play prompt.");
        var rejected = current.Submit(new PlayCardCommand(
            ActorSeat: 0,
            CardId: duelId,
            TargetSeats: [1],
            ExpectedRevision: current.Revision,
            PromptId: prompt.PromptId));
        Require(!rejected.Accepted && rejected.Error?.Code == CommandErrorCode.InvalidTarget,
            "A forged Duel target against formal Kongcheng must be rejected at the command boundary.");
        Require(current.SerializeState() == beforeState &&
                current.Events.Count == beforeEvents &&
                current.AcceptedCommands.Count == beforeCommands,
            "Rejected Kongcheng targeting must be atomic.");
    }

    private static int FindSeedWithHumanDuel(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateGame(registry, seed, GameCheckpoint.CurrentRulesVersion);
            if (game.CreateSnapshot(0, revealAll: true).Players[0].Hand.Any(card => card.Kind == CardKind.Duel))
            {
                return seed;
            }
        }

        throw new InvalidOperationException("No deterministic classic fixture dealt Duel to the human seat.");
    }

    private static GameEngine CreateGame(ContentRegistry registry, int seed, int rulesVersion)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = "identity:classic-5",
            UseInteractiveSetup = false,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 220
        }, registry);
        return rulesVersion == GameCheckpoint.CurrentRulesVersion
            ? game
            : GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
    }

    private static void ArrangeEmptyKongchengTarget(GameEngine game, int targetSeat)
    {
        var playersField = typeof(GameEngine).GetField(
            "_players",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("Player runtime field not found.");
        var players = ((System.Collections.IEnumerable)playersField.GetValue(game)!)
            .Cast<object>()
            .ToArray();
        var target = players[targetSeat];
        var generalProperty = target.GetType().GetProperty("General") ??
            throw new InvalidOperationException("Player general property not found.");
        generalProperty.SetValue(
            target,
            GeneralCatalog.DemoGenerals.Single(general => general.Skill == SkillKind.Kongcheng));

        var zonesField = typeof(GameEngine).GetField(
            "_cardZones",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("Card-zone field not found.");
        var zones = (CardZoneStore)zonesField.GetValue(game)!;
        foreach (var card in zones.CardsAt(CardLocation.Hand(targetSeat)).ToArray())
        {
            zones.Move(card.Id, CardLocation.Hand(targetSeat), CardLocation.OutsideGame);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
