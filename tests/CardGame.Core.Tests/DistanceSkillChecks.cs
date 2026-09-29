using CardGame.Content.Standard;
using CardGame.Core;

internal static class DistanceSkillChecks
{
    public static void MashuDistanceAndLegality()
    {
        var registry = StandardContentRegistry.CreateWithActiveSkills();
        Require(
            registry.Skills.TryGetValue("standard:mashu", out var definition) &&
            definition is { Program.RuntimeVersion: "skill-program-v62" } &&
            definition.Program.Modifiers.Single() is
            {
                Query: SkillRuleQuery.OutgoingDistance,
                Operation: SkillRuleOperation.Add,
                Value: -1
            },
            "The current Mashu content definition must use the formal rule-query program.");

        var game = FindMashuGame(registry);
        Require(game.GetSeatDistance(0, 2) == 2, "The five-seat ring baseline must remain distance two.");
        Require(game.GetCombatDistance(0, 2) == 1, "Mashu must reduce a public combat distance-two edge to one.");

        var snatch = game.GetHumanLegalActions().SingleOrDefault(action =>
            action.Kind == LegalActionKind.Snatch &&
            action.TargetSeats.SequenceEqual([2]));
        Require(
            snatch is not null,
            "AI-facing legal actions must consume Mashu's distance modifier for a distance-one Snatch target.");
        Require(
            game.CreateSnapshot(0).Seed is null &&
            game.CreateSnapshot(0).Players.Single(player => player.Seat == 2).Hand.Count == 0 &&
            game.CreateSnapshot(0).Players.Single(player => player.Seat == 2).HandCount == 4,
            "The public distance query must not require or expose hidden seed or target card ids.");
    }

    public static void QicaiRemovesTrickDistance()
    {
        var registry = StandardContentRegistry.CreateWithActiveSkills();
        Require(
            registry.Skills.TryGetValue("standard:qicai", out var definition) &&
            definition.Program is { RuntimeVersion: "skill-program-v62" },
            "The Qicai content definition must use the current program.");

        var distancePolicy = (definition?.Program ?? throw new InvalidOperationException("Missing Qicai program."))
            .CardPolicies.Single(policy =>
            policy.Kind == SkillProgramCardPolicyKind.IgnoreUseDistance);
        Require(
            distancePolicy.CardKinds.Contains(CardKind.Snatch) &&
            distancePolicy.CardKinds.Contains(CardKind.SupplyShortage) &&
            !distancePolicy.CardKinds.Contains(CardKind.Slash),
            "Qicai must waive distance only for trick cards, not basic Slash cards.");

        var game = FindQicaiGame(registry);
        Require(game.GetCombatDistance(0, 2) == 2, "The fixture must keep a public distance-two target.");
        var snatch = game.GetHumanLegalActions().SingleOrDefault(action =>
            action.Kind == LegalActionKind.Snatch &&
            action.TargetSeats.SequenceEqual([2]));
        Require(
            snatch is not null,
            "AI-facing legal actions must allow Qicai to target a distance-two Snatch target.");
        Require(
            game.CreateSnapshot(0).Seed is null &&
            game.CreateSnapshot(0).Players.Single(player => player.Seat == 2).Hand.Count == 0 &&
            game.CreateSnapshot(0).Players.Single(player => player.Seat == 2).HandCount == 4,
            "Qicai distance legality must not require or expose hidden seed or target card ids.");

        var supplyShortageGame = FindQicaiSupplyShortageGame(registry);
        Require(
            supplyShortageGame.GetCombatDistance(0, 2) == 2 &&
            supplyShortageGame.GetHumanLegalActions().Any(action =>
                action.Kind == LegalActionKind.SupplyShortage &&
                action.TargetSeats.SequenceEqual([2])),
            "Qicai must waive the formal distance-one restriction for Supply Shortage.");
    }

    private static GameEngine FindMashuGame(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 8192; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = "identity:active-skills-5",
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2
            }, registry);
            var initial = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            if (initial.GeneralId is not { } generalId ||
                !registry.Generals[generalId].SkillIds.Contains("standard:mashu", StringComparer.Ordinal) ||
                !initial.Hand.Any(card => card.Kind == CardKind.Snatch))
            {
                continue;
            }

            if (!game.Submit(new StartGameCommand()).Accepted)
            {
                continue;
            }

            if (game.PendingDecision?.Kind == DecisionKind.PlayCard &&
                game.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.Snatch && action.TargetSeats.SequenceEqual([2])))
            {
                return game;
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Mashu distance fixture.");
    }

    private static GameEngine FindQicaiGame(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 8192; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = "identity:active-skills-5",
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2
            }, registry);
            var initial = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            if (initial.Skills?.Any(skill => skill.ContentId == "standard:qicai") != true || !initial.Hand.Any(card => card.Kind == CardKind.Snatch))
            {
                continue;
            }

            if (!game.Submit(new StartGameCommand()).Accepted)
            {
                continue;
            }

            if (game.PendingDecision?.Kind == DecisionKind.PlayCard &&
                game.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.Snatch && action.TargetSeats.SequenceEqual([2])))
            {
                return game;
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Qicai distance fixture.");
    }

    private static GameEngine FindQicaiSupplyShortageGame(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 8192; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = "identity:active-skills-5",
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2
            }, registry);
            var initial = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            if (initial.Skills?.Any(skill => skill.ContentId == "standard:qicai") != true || !initial.Hand.Any(card => card.Kind == CardKind.SupplyShortage))
            {
                continue;
            }

            if (!game.Submit(new StartGameCommand()).Accepted)
            {
                continue;
            }

            if (game.PendingDecision?.Kind == DecisionKind.PlayCard &&
                game.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.SupplyShortage && action.TargetSeats.SequenceEqual([2])))
            {
                return game;
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Qicai Supply Shortage fixture.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
