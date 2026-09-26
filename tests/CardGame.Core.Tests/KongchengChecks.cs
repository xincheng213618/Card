using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class KongchengChecks
{
    public static void DuelTargeting()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var policy = registry.GetSkill("classic:kongcheng").Program!.CardPolicies.Single(item =>
            item.Kind == SkillProgramCardPolicyKind.ProhibitTarget);
        var empty = new PlayerSkillContext(1, 3, 3, 0, TurnPhase.Play);
        var holdingCards = empty with { HandCount = 1 };
        Require(policy.CardKinds.Contains(CardKind.Slash) &&
                policy.CardKinds.Contains(CardKind.FireSlash) &&
                policy.CardKinds.Contains(CardKind.ThunderSlash) &&
                policy.CardKinds.Contains(CardKind.Duel) &&
                policy.Condition.Evaluate(empty) &&
                !policy.Condition.Evaluate(holdingCards),
            "Kongcheng must prohibit every Slash kind and Duel while the owner has no hand cards.");
        Require(!policy.CardKinds.Contains(CardKind.DrawTwo),
            "Kongcheng must not prohibit Duel with a hand card or unrelated card kinds.");

        var seed = FindSeedWithHumanDuel(registry);
        var current = CreateGame(registry, seed);
        ArrangeEmptyKongchengTarget(current, targetSeat: 1);

        var currentStart = current.Submit(new StartGameCommand());
        Require(currentStart.Accepted,
            "The Kongcheng fixture failed to start.");
        Require(currentStart.Result.Status == EngineStatus.AwaitingHumanPlay,
            "The Kongcheng fixture did not reach the human play boundary.");

        var duelId = current.CreateSnapshot(0, revealAll: true).Players[0].Hand
            .Single(card => card.Kind == CardKind.Duel).Id;

        Require(!current.GetHumanLegalActions().Any(action =>
                action.Kind == LegalActionKind.Duel &&
                action.CardId == duelId &&
                action.TargetSeat == 1),
            "An empty-hand Kongcheng owner must not be a legal Duel target.");
        Require(current.GetHumanLegalActions().Any(action =>
                action.Kind == LegalActionKind.Duel &&
                action.CardId == duelId &&
                action.TargetSeat == 2),
            "Formal Kongcheng must not remove unrelated living Duel targets.");
        Require(current.CreateSnapshot(0, revealAll: true).Players[1].Skills?
                    .Any(skill => skill.Description.Contains("【决斗】", StringComparison.Ordinal)) == true,
            "The player projection must describe formal Kongcheng for current classic rules.");

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
            var current = CreateGame(registry, seed);
            ArrangeEmptyKongchengTarget(current, targetSeat: 1);
            var currentStart = current.Submit(new StartGameCommand());
            var currentDuels = current.CreateSnapshot(0, revealAll: true).Players[0].Hand
                .Where(card => card.Kind == CardKind.Duel)
                .ToArray();
            if (currentStart.Accepted &&
                currentStart.Result.Status == EngineStatus.AwaitingHumanPlay &&
                currentDuels.Length == 1)
            {
                return seed;
            }
        }

        throw new InvalidOperationException("No deterministic classic fixture dealt Duel to the human seat.");
    }

    private static GameEngine CreateGame(ContentRegistry registry, int seed)
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
        return game;
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
            new GeneralDefinition("fixture:kongcheng", "诸葛亮", "zhuge_liang",
                [new GeneralSkillDefinition("空城",
                    "没有手牌时不能成为【杀】或【决斗】的目标。")
                { ContentId = "classic:kongcheng" }]));

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
