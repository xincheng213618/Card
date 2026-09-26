using CardGame.Content.Standard;
using CardGame.Core;

internal static class HuaXiongChecks
{
    public static void RedSlashBenefitAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Rebel, PlayerCount = 5,
                ModeId = "identity:classic-5", UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } setup ||
                !setup.ValidContentIds.Contains("classic:hua-xiong") ||
                !game.Submit(new SelectGeneralCommand(0, "classic:hua-xiong", game.Revision, setup.PromptId)).Accepted)
                continue;

            for (var step = 0; step < 768; step++)
            {
                var yaowu = game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .LastOrDefault(item => item.SkillId == "classic:yaowu" && item.Completed);
                if (yaowu is not null)
                {
                    var sourceSeat = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                        .Last(item => item.TargetSeat == yaowu.OwnerSeat).SourceSeat;
                    Require(yaowu.OwnerSeat == 0 && yaowu.Activated &&
                            (game.Events.Select(item => item.Payload).OfType<RecoveryAppliedEvent>()
                                 .Any(item => item.TargetSeat == sourceSeat && item.Amount == 1) ||
                             game.CardMovements.Any(move =>
                                 move.Reason.Value == "skill-program.classic:yaowu.Draw" &&
                                 move.To == CardLocation.Hand(sourceSeat))),
                        "A red-Slash source must receive exactly its chosen Yaowu benefit.");
                    var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
                    Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                            SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                            restored.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                                .Count(item => item.SkillId == "classic:yaowu") ==
                            game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                                .Count(item => item.SkillId == "classic:yaowu"),
                        "A resolved Yaowu benefit must replay exactly.");
                    return;
                }

                GameCommand command = game.PendingDecision is { PlayerSeat: 0 } prompt
                    ? new AnswerPromptCommand(0, prompt.PromptId, ChooseDecline(prompt).Id, game.Revision)
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted) break;
            }
        }
        throw new InvalidOperationException("No bounded Hua Xiong fixture received red Slash damage.");
    }

    private static PromptChoice ChooseDecline(PendingDecision prompt) =>
        prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.Values.Any(value =>
                value.Contains("decline", StringComparison.Ordinal) ||
                value.Contains("skip", StringComparison.Ordinal))) ?? prompt.Choices.Last();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
