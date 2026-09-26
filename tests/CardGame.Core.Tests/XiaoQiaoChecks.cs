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
        var ownerHp = before.Players[0].Hp;
        var use = prompt.Choices.First(choice => choice.Parameters.GetValueOrDefault("action") == "tianxiang-use");
        var card = before.Players[0].Hand.Single(item => item.Id == use.Cards.Single());
        Require(card.Suit is Suit.Heart or Suit.Spade,
            "Tianxiang must publish physical Hearts and Hongyan Spades, but no other hand cards.");

        var paused = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var answered = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, use.Id, game.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "Tianxiang transfer choice was rejected.");
        for (var step = 0; step < 32 && !game.Events.Any(item => item.Payload is ProgramDamageTransferCardsDrawnEvent); step++)
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Tianxiang transfer did not finish its damage continuation.");
        var transfer = game.Events.Select(item => item.Payload).OfType<ProgramDamageTransferredEvent>().Last();
        var draw = game.Events.Select(item => item.Payload).OfType<ProgramDamageTransferCardsDrawnEvent>().Last();
        var after = game.CreateSnapshot(0, revealAll: true);
        Require(after.Players[0].Hp == ownerHp && transfer.SkillId == "classic:tianxiang" &&
                transfer.TargetSeat == use.Targets.Single() &&
                transfer.DamageAmount > 0 && draw.TargetSeat == transfer.TargetSeat &&
                game.CardMovements.Any(move => move.CardId == card.Id && move.Reason == CardMoveReasons.TianxiangDiscard),
            "Tianxiang must prevent the owner's full damage, discard the exact private card and transfer that damage.");

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
                if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } tianxiang &&
                    tianxiang.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action") == "tianxiang-use"))
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
