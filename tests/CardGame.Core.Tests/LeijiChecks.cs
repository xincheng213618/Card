using CardGame.Content.Standard;
using CardGame.Core;

internal static class LeijiChecks
{
    public static void DodgeJudgmentDamageAndReplay()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new ScenarioPackage());

        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Rebel, PlayerCount = 5,
                ModeId = ScenarioPackage.ModeId, UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral } setup ||
                !game.Submit(new SelectGeneralCommand(0, ScenarioPackage.GeneralId, game.Revision, setup.PromptId)).Accepted)
                continue;

            for (var step = 0; step < 4000 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.Leiji, PlayerSeat: 0 } prompt)
                {
                    var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
                    Require(restored.PendingDecision is { Kind: DecisionKind.Leiji } restoredPrompt &&
                            restoredPrompt.Choices.Select(choice => choice.Id.Value)
                                .SequenceEqual(prompt.Choices.Select(choice => choice.Id.Value)),
                        "A paused Leiji target choice must restore exactly.");
                    var choice = prompt.Choices.First(item => item.Targets.Count == 1);
                    var accepted = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
                    Require(accepted.Accepted, accepted.Error?.Message ?? "Leiji target choice was rejected.");

                    for (var continuation = 0; continuation < 80 &&
                         !game.Events.Any(item => item.Payload is LeijiResolvedEvent); continuation++)
                    {
                        GameCommand command = game.PendingDecision is { PlayerSeat: 0 } continuationPrompt
                            ? new AnswerPromptCommand(0, continuationPrompt.PromptId, continuationPrompt.Choices.Last().Id, game.Revision)
                            : new AdvanceOneStepCommand(game.Revision);
                        Require(game.Submit(command).Accepted, "Leiji judgment continuation did not advance.");
                    }

                    var resolved = game.Events.Select(item => item.Payload).OfType<LeijiResolvedEvent>().LastOrDefault();
                    Require(resolved is not null && resolved.OwnerSeat == 0 && resolved.TargetSeat == choice.Targets[0],
                        "Leiji must publish its exact owner and selected target.");
                    Require(resolved!.DamageAmount == (resolved.JudgmentSuit switch
                    {
                        Suit.Spade => 2,
                        Suit.Club => 1,
                        _ => 0
                    }), "Leiji damage must follow the final effective judgment suit.");

                    var replayed = GameReplay.Restore(game.CreateCheckpoint(), registry);
                    Require(replayed.Events.Select(item => item.Payload).OfType<LeijiResolvedEvent>().Count() == 1 &&
                            SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)) ==
                            SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
                        "A completed Leiji judgment must replay exactly.");
                    return;
                }

                GameCommand next = game.PendingDecision is { PlayerSeat: 0 } pending
                    ? pending.Kind switch
                    {
                        DecisionKind.PlayCard => new EndPlayPhaseCommand(0, game.Revision, pending.PromptId),
                        DecisionKind.RespondDodge when pending.Choices.Any(choice =>
                            choice.Parameters.GetValueOrDefault("response") == "dodge") =>
                            new AnswerPromptCommand(0, pending.PromptId,
                                pending.Choices.First(choice => choice.Parameters.GetValueOrDefault("response") == "dodge").Id,
                                game.Revision),
                        _ => new AnswerPromptCommand(0, pending.PromptId, pending.Choices.Last().Id, game.Revision)
                    }
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(next).Accepted) break;
            }
        }

        throw new InvalidOperationException("No bounded Leiji fixture reached a successful Dodge trigger.");
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:leiji-scenario-5";
        public const string GeneralId = "scenario:zhang-jiao-leiji";
        public PackageManifest Manifest { get; } = new("leiji-scenario", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 56, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new ContentSkillDefinition("scenario:leiji", "雷击",
                "当你使用或打出闪时，你可以令一名其他角色进行判定。", SkillKind.Leiji));
            builder.AddGeneral(new ContentGeneralDefinition(GeneralId, "张角（雷击测试）", "zhang_jiao",
                "scenario:leiji", "qun", BaseHp: 3));
            builder.AddMode(new ContentModeDefinition(ModeId, "雷击场景", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "classic:standard-deck", 5,
                [GeneralId, "classic:zhang-fei", "classic:huang-zhong", "classic:ma-chao", "classic:lu-bu"]));
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
