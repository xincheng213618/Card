using CardGame.Content.Standard;
using CardGame.Core;

internal sealed record SilverLionBoundary(
    ContentRegistry Registry,
    GameEngine Game,
    GameCheckpoint Checkpoint,
    int SourceSeat,
    int TargetSeat,
    LegalAction Action,
    int? SlashCardId = null);

internal static class SilverLionScenario
{
    private const string ModeId = "identity:classic-silver-lion-5";

    public static SilverLionBoundary FindAlcoholSlash() => Find(requireRemoval: false);
    public static SilverLionBoundary FindRemoval() => Find(requireRemoval: true);

    private static SilverLionBoundary Find(bool requireRemoval)
    {
        const int sourceSeat = 0;
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 8192; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = sourceSeat,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = ModeId,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 100,
                AiPolicyVersion = 2
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted, "Silver Lion fixture failed to start.");
            for (var step = 0; step < 5000 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: sourceSeat } play)
                {
                    var full = game.CreateSnapshot(sourceSeat, revealAll: true);
                    var actions = game.GetHumanLegalActions();
                    if (requireRemoval)
                    {
                        var removal = actions.FirstOrDefault(candidate =>
                            candidate.CardId is { } cardId &&
                            full.Players[sourceSeat].Hand.Single(card => card.Id == cardId).Kind == CardKind.Dismantlement &&
                            candidate.TargetSeat is { } targetSeat &&
                            full.Players[targetSeat].Hp < full.Players[targetSeat].MaxHp &&
                            full.Players[targetSeat].Equipment.Any(card => card.Kind == CardKind.SilverLion));
                        if (removal is not null)
                        {
                            return new(registry, game, game.CreateCheckpoint(), sourceSeat,
                                removal.TargetSeat!.Value, removal);
                        }
                    }
                    else
                    {
                        var alcohol = actions.FirstOrDefault(candidate => candidate.CardId is { } cardId &&
                            full.Players[sourceSeat].Hand.Single(card => card.Id == cardId).Kind == CardKind.Alcohol);
                        var slash = full.Players[sourceSeat].Hand.FirstOrDefault(card => card.Kind == CardKind.Slash);
                        var target = slash is null ? null : actions.FirstOrDefault(candidate =>
                            candidate.CardId == slash.Id && candidate.TargetSeat is { } targetSeat &&
                            full.Players[targetSeat].Hp > 1 &&
                            full.Players[targetSeat].Hand.All(card => card.Kind != CardKind.Dodge) &&
                            full.Players[targetSeat].Equipment.Any(card => card.Kind == CardKind.SilverLion));
                        if (alcohol is not null && target is not null)
                        {
                            return new(registry, game, game.CreateCheckpoint(), sourceSeat,
                                target.TargetSeat!.Value, alcohol, slash!.Id);
                        }
                    }

                    Require(game.Submit(new EndPlayPhaseCommand(sourceSeat, game.Revision, play.PromptId)).Accepted,
                        "Silver Lion fixture could not wait for a boundary.");
                    continue;
                }

                GameCommand command = game.PendingDecision is { PlayerSeat: sourceSeat } prompt
                    ? new AnswerPromptCommand(sourceSeat, prompt.PromptId,
                        (prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0) ?? prompt.Choices.First()).Id,
                        game.Revision)
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted) break;
            }
        }
        throw new InvalidOperationException($"No bounded Silver Lion {(requireRemoval ? "removal" : "damage")} fixture found.");
    }

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(new Version(1, 63, 0)),
        new ScenarioPackage());

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("silver-lion-scenario", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 34, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe("silver-lion-scenario:deck", "白银狮子测试牌堆", 4, 2,
            [
                new ContentDeckCardCount("classic:silver-lion", 18),
                new ContentDeckCardCount("standard:slash", 18),
                new ContentDeckCardCount("standard:alcohol", 12),
                new ContentDeckCardCount("standard:dismantlement", 12),
                new ContentDeckCardCount("standard:fire_slash", 6),
                new ContentDeckCardCount("standard:peach", 6)
            ]));
            builder.AddMode(new ContentModeDefinition(ModeId, "五人经典身份（白银狮子场景）", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "silver-lion-scenario:deck", 3,
                ["classic:sun-quan", "classic:huang-gai", "classic:gan-ning", "classic:lu-meng", "classic:zhang-fei"]));
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
