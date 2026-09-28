using CardGame.Content.Standard;
using CardGame.Core;

internal static class GuidaoChecks
{
    public static void BlackHandAndEquipmentReplacement()
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
                Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
                ModeId = ScenarioPackage.ModeId, UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral } setup ||
                !game.Submit(new SelectGeneralCommand(0, ScenarioPackage.GeneralId, game.Revision, setup.PromptId)).Accepted ||
                !game.Submit(new AdvanceCommand(game.Revision)).Accepted ||
                game.PendingDecision?.Kind != DecisionKind.PlayCard)
                continue;

            var hand = game.CreateSnapshot(0, revealAll: true).Players[0].Hand.ToDictionary(card => card.Id);
            var equipment = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Equip && action.CardId is { } id &&
                hand[id].Suit is Suit.Spade or Suit.Club);
            if (equipment?.CardId is not { } equipmentId ||
                !game.Submit(new PlayCardCommand(0, equipmentId, [], game.Revision, game.PendingDecision.PromptId)).Accepted)
                continue;

            for (var step = 0; step < 2400 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.ProgramJudgmentReplacement, PlayerSeat: 0 } prompt &&
                    prompt.ValidCardIds.Contains(equipmentId))
                {
                    Require(prompt.ValidCardIds.All(id =>
                        game.CreateSnapshot(0, revealAll: true).Players[0].Hand
                            .Concat(game.CreateSnapshot(0, revealAll: true).Players[0].Equipment)
                            .Single(card => card.Id == id).Suit is Suit.Spade or Suit.Club),
                        "Guidao must publish only black owned hand or equipment cards.");
                    var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
                    Require(restored.PendingDecision is { Kind: DecisionKind.ProgramJudgmentReplacement } restoredPrompt &&
                            restoredPrompt.ValidCardIds.Contains(equipmentId),
                        "A paused Guidao equipment choice must restore exactly.");
                    var choice = prompt.Choices.Single(item => item.Cards.SequenceEqual([equipmentId]));
                    var accepted = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
                    Require(accepted.Accepted && game.CardMovements.Any(move =>
                            move.CardId == equipmentId && move.From == CardLocation.Equipment(0) &&
                            move.To == CardLocation.Processing && move.Reason == CardMoveReasons.ProgramJudgmentReplace) &&
                            game.Events.Any(item => item.Payload is ProgramJudgmentReplacementResolvedEvent
                            {
                                OwnerSeat: 0, Activated: true
                            }),
                        accepted.Error?.Message ?? "Guidao did not replace the judgment with its exact equipped black card.");
                    return;
                }

                GameCommand command = game.PendingDecision is { PlayerSeat: 0 } pending
                    ? pending.Kind == DecisionKind.PlayCard
                        ? new EndPlayPhaseCommand(0, game.Revision, pending.PromptId)
                        : new AnswerPromptCommand(0, pending.PromptId, pending.Choices.Last().Id, game.Revision)
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted) break;
            }
        }
        throw new InvalidOperationException("No bounded Guidao fixture reached a judgment with a black equipped replacement.");
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:guidao-scenario-5";
        public const string GeneralId = "scenario:zhang-jiao-guidao";
        public PackageManifest Manifest { get; } = new("guidao-scenario", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 56, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition(GeneralId, "张角（鬼道测试）", "zhang_jiao",
                "classic:guidao", "qun", BaseHp: 3));
            builder.AddMode(new ContentModeDefinition(ModeId, "鬼道判定场景", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "classic:standard-deck", 5,
                [GeneralId, "classic:xiahou-dun", "classic:zhen-ji", "classic:ma-chao", "classic:guo-jia"]));
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
