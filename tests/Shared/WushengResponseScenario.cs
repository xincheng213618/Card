using CardGame.Content.Standard;
using CardGame.Core;

internal static class WushengResponseScenario
{
    public static GameEngine Find(CardKind incoming)
        => FindResponse(incoming, SkillKind.Wusheng, DecisionKind.RespondSlash,
            skillContentId: "standard:wusheng");

    public static GameEngine FindLongdanDodge(
        ContentRegistry? registry = null,
        string? modeId = null,
        string? skillContentId = null)
        => FindResponse(
            CardKind.Slash,
            SkillKind.Longdan,
            DecisionKind.RespondDodge,
            registry,
            modeId,
            skillContentId ?? "standard:longdan");

    public static GameEngine FindClassicWushengHand(ContentRegistry registry)
        => FindResponse(
            CardKind.Duel,
            SkillKind.Wusheng,
            DecisionKind.RespondSlash,
            registry,
            "identity:classic-8",
            "classic:wusheng");

    public static GameEngine FindQingguoDodge(
        CardKind incoming = CardKind.Slash,
        string? skillContentId = null)
        => FindResponse(
            incoming,
            SkillKind.Qingguo,
            DecisionKind.RespondDodge,
            StandardContentRegistry.CreateWithClassicGenerals(),
            "identity:classic-8",
            skillContentId ?? "classic:qingguo");

    public static GameEngine FindClassicWushengEquipmentResponse()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = "identity:classic-5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 220,
                AiPolicyVersion = 2
            }, registry);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, "Classic Wusheng equipment fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:guan-yu"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:guan-yu",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, "Classic Wusheng equipment fixture could not select Guan Yu.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, "Classic Wusheng equipment fixture did not reach play.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play)
            {
                continue;
            }

            var redEquipment = game.CreateSnapshot(0, revealAll: true).Players[0].Hand.FirstOrDefault(card =>
                EquipmentCatalog.IsEquipment(card.Kind) &&
                card.Suit is Suit.Heart or Suit.Diamond);
            if (redEquipment is null)
            {
                continue;
            }

            var equipped = game.Submit(new PlayCardCommand(
                0,
                redEquipment.Id,
                [],
                game.Revision,
                play.PromptId));
            Require(equipped.Accepted, "Classic Wusheng equipment fixture could not equip the red card.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                var resumed = game.Submit(new AdvanceCommand(game.Revision));
                Require(resumed.Accepted, "Classic Wusheng equipment fixture did not return to play.");
            }
            var nextPlay = game.PendingDecision ??
                throw new InvalidOperationException("Classic Wusheng equipment fixture lost its play prompt.");
            var ended = game.Submit(new EndPlayPhaseCommand(0, game.Revision, nextPlay.PromptId));
            Require(ended.Accepted, "Classic Wusheng equipment fixture could not end play.");

            for (var step = 0; step < 4_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 } response &&
                    response.IncomingCard is CardKind.Duel or CardKind.BarbarianAssault &&
                    response.Choices.Any(choice =>
                        choice.Cards.SequenceEqual([redEquipment.Id]) &&
                        choice.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Slash)))
                {
                    return game;
                }

                var owner = game.CreateSnapshot(0, revealAll: true).Players[0];
                if (!owner.IsAlive || owner.Equipment.All(card => card.Id != redEquipment.Id))
                {
                    break;
                }

                try
                {
                    Step(game);
                }
                catch (InvalidOperationException exception) when (
                    exception.Message.StartsWith("Unknown or unsupported turn phase:", StringComparison.Ordinal))
                {
                    // A bare human phase boundary is not a valid autonomous response fixture.
                    break;
                }
            }
        }

        throw new InvalidOperationException("No bounded classic Wusheng equipment response fixture was found.");
    }

    private static GameEngine FindResponse(
        CardKind incoming,
        SkillKind responderSkill,
        DecisionKind decisionKind,
        ContentRegistry? registry = null,
        string? modeId = null,
        string? skillContentId = null)
    {
        for (var seed = 1; seed <= (skillContentId is null ? 256 : 4_096); seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                PlayerCount = 8,
                ModeId = modeId,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 160,
                AiPolicyVersion = 2
            }, registry ?? StandardContentRegistry.Create());
            var human = game.CreateSnapshot(0).Players[0];
            var matchesSkill = skillContentId is null
                ? human.Skills?.Any(skill => skill.Kind == responderSkill) == true
                : human.Skills?.Any(skill => skill.ContentId == skillContentId) == true;
            if (!matchesSkill)
            {
                continue;
            }
            Require(game.Submit(new StartGameCommand()).Accepted, "Wusheng fixture failed to start.");
            for (var step = 0; step < 2500 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { PlayerSeat: 0 } prompt && prompt.Kind == decisionKind && prompt.IncomingCard == incoming)
                {
                    var hand = game.CreateSnapshot(0).Players[0].Hand;
                    if (prompt.Choices.Any(choice => choice.Cards.Count == 1 && (decisionKind == DecisionKind.RespondDodge
                        ? hand.Single(card => card.Id == choice.Cards[0]).Kind != CardKind.Dodge
                        : hand.Single(card => card.Id == choice.Cards[0]).Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)))) return game;
                }
                try
                {
                    Step(game);
                }
                catch (InvalidOperationException exception) when (
                    exception.Message.StartsWith("Unknown or unsupported turn phase:", StringComparison.Ordinal))
                {
                    // This seed returned to a bare human phase before the requested response.
                    break;
                }
            }
        }
        throw new InvalidOperationException($"No bounded {responderSkill} {incoming} fixture was found.");
    }

    public static void Step(GameEngine game)
    {
        GameCommand command = game.PendingDecision is { PlayerSeat: 0 } prompt
            ? prompt.Kind == DecisionKind.PlayCard ? new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)
                : new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.Last().Id, game.Revision)
            : new AdvanceOneStepCommand(game.Revision);
        var result = game.Submit(command);
        Require(result.Accepted, $"Response fixture could not continue: {result.Error?.Message}");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
