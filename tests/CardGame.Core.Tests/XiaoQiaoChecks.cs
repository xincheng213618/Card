using CardGame.Content.Standard;
using CardGame.Core;

internal static class XiaoQiaoChecks
{
    public static void HongyanTianxiangTransferAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = FindTianxiangPrompt(registry);
        var prompt = game.PendingDecision!;
        var before = game.CreateSnapshot(0, revealAll: true);
        var beforeEventCount = game.Events.Count;
        var paused = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var activate = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Require(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, activate.Id, game.Revision)).Accepted,
            "Tianxiang activation was rejected.");
        var targetPrompt = game.PendingDecision!;
        var target = targetPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-target");
        Require(game.Submit(new AnswerPromptCommand(0, targetPrompt.PromptId, target.Id, game.Revision)).Accepted,
            "Tianxiang target choice was rejected.");
        var cardPrompt = game.PendingDecision!;
        var use = cardPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
        var card = before.Players[0].Hand.Single(item => item.Id == use.Cards.Single());
        Require(card.Suit is Suit.Heart or Suit.Spade,
            "Tianxiang must publish physical Hearts and Hongyan Spades, but no other hand cards.");

        var restored = GameReplay.Restore(paused, registry);
        Require(restored.PendingDecision?.Kind == DecisionKind.ProgramTrigger,
            "The pending Tianxiang activation must restore exactly.");
        var answered = game.Submit(new AnswerPromptCommand(0, cardPrompt.PromptId, use.Id, game.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "Tianxiang transfer choice was rejected.");
        for (var step = 0; step < 32 && !game.Events.Any(item => item.Payload is ProgramDamageTransferCardsDrawnEvent); step++)
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Tianxiang transfer did not finish its damage continuation.");
        var transfer = game.Events.Select(item => item.Payload).OfType<ProgramDamageTransferredEvent>().Last();
        var draw = game.Events.Select(item => item.Payload).OfType<ProgramDamageTransferCardsDrawnEvent>().Last();
        var transferredDamage = game.Events.Skip(beforeEventCount)
            .Select(item => item.Payload)
            .TakeWhile(item => item is not ProgramDamageTransferCardsDrawnEvent)
            .OfType<DamageAppliedEvent>()
            .ToArray();
        Require(transferredDamage.Length == 1 &&
                transferredDamage[0].TargetSeat == transfer.TargetSeat &&
                transfer.SkillId == "classic:tianxiang" &&
                transfer.TargetSeat == target.Targets.Single() &&
                transfer.DamageAmount > 0 && draw.TargetSeat == transfer.TargetSeat &&
                game.CardMovements.Any(move => move.CardId == card.Id && move.To == CardLocation.DiscardPile),
            "Tianxiang must prevent the owner's direct damage, discard the exact private card and transfer that damage.");

        var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "A completed Tianxiang transfer must replay exactly.");
    }

    private static GameEngine FindTianxiangPrompt(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
                ModeId = "identity:classic-5", UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 100
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } setup ||
                !setup.ValidContentIds.Contains("classic:xiao-qiao") ||
                !game.Submit(new SelectGeneralCommand(0, "classic:xiao-qiao", game.Revision, setup.PromptId)).Accepted)
                continue;

            for (var step = 0; step < 1400; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0,
                    SkillPrompt.SkillId: "classic:tianxiang" } tianxiang &&
                    tianxiang.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"))
                    return game;
                if (game.PendingDecision is { PlayerSeat: 0 } prompt)
                {
                    CommandResult result;
                    if (prompt.Kind == DecisionKind.PlayCard)
                        result = game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId));
                    else
                    {
                        var choice = prompt.Choices.FirstOrDefault(candidate => candidate.Cards.Count == 0) ??
                                     prompt.Choices.FirstOrDefault();
                        if (choice is null) break;
                        result = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
                    }
                    if (!result.Accepted) break;
                }
                else if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            }
        }
        throw new InvalidOperationException("No bounded Xiao Qiao Tianxiang fixture was found.");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
