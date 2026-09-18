using CardGame.Content.Standard;
using CardGame.Core;

internal static class WushengResponseScenario
{
    public static GameEngine Find(CardKind incoming)
        => FindResponse(incoming, SkillKind.Wusheng, DecisionKind.RespondSlash);

    public static GameEngine FindLongdanDodge()
        => FindResponse(CardKind.Slash, SkillKind.Longdan, DecisionKind.RespondDodge);

    public static GameEngine FindQingguoDodge(CardKind incoming = CardKind.Slash)
        => FindResponse(
            incoming,
            SkillKind.Qingguo,
            DecisionKind.RespondDodge,
            StandardContentRegistry.CreateWithClassicGenerals(),
            "identity:classic-8");

    private static GameEngine FindResponse(
        CardKind incoming,
        SkillKind responderSkill,
        DecisionKind decisionKind,
        ContentRegistry? registry = null,
        string? modeId = null)
    {
        for (var seed = 1; seed <= 256; seed++)
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
            if (human.Skill != responderSkill &&
                human.Skills?.Any(skill => skill.Kind == responderSkill) != true)
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
                Step(game);
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
