using CardGame.Content.Standard;
using CardGame.Core;

internal sealed record TengjiaBoundary(
    ContentRegistry Registry,
    GameEngine Game,
    GameCheckpoint BeforeSlash,
    LegalAction SlashAction,
    int SourceSeat,
    int TargetSeat);

internal static class TengjiaScenario
{
    private const string ModeId = "identity:classic-tengjia-5";

    public static TengjiaBoundary FindOrdinarySlash() => FindSlash(CardKind.Slash);

    public static TengjiaBoundary FindFireSlash() => FindSlash(CardKind.FireSlash);

    private static TengjiaBoundary FindSlash(CardKind slashKind)
    {
        const int sourceSeat = 0;
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new ScenarioPackage());
        for (var seed = 1; seed <= 4096; seed++)
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
                MaxTurns = 80,
                AiPolicyVersion = 2
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted, "Tengjia fixture failed to start.");
            for (var step = 0; step < 4000 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: sourceSeat } play)
                {
                    var full = game.CreateSnapshot(sourceSeat, revealAll: true);
                    var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
                        candidate.Kind == LegalActionKind.Slash &&
                        candidate.CardId is { } cardId &&
                        full.Players[sourceSeat].Hand.Single(card => card.Id == cardId).Kind == slashKind &&
                        candidate.TargetSeat is { } targetSeat &&
                        full.Players[targetSeat].Hp > 1 &&
                        full.Players[targetSeat].Hand.All(card => card.Kind != CardKind.Dodge) &&
                        full.Players[targetSeat].Skills?.All(skill =>
                            skill.ContentId is not ("classic:qingguo" or "classic:longdan" or "classic:hujia")) != false &&
                        full.Players[targetSeat].Equipment.Any(card => card.Kind == CardKind.Tengjia));
                    if (action is not null)
                    {
                        return new(registry, game, game.CreateCheckpoint(), action, sourceSeat, action.TargetSeat!.Value);
                    }

                    Require(game.Submit(new EndPlayPhaseCommand(sourceSeat, game.Revision, play.PromptId)).Accepted,
                        "Tengjia fixture could not wait for an equipped target.");
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

        throw new InvalidOperationException($"No bounded Tengjia fixture found an equipped {slashKind} target.");
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("tengjia-scenario", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 33, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe("tengjia-scenario:deck", "藤甲测试牌堆", 4, 1,
            [
                new ContentDeckCardCount("classic:tengjia", 18),
                new ContentDeckCardCount("standard:slash", 24),
                new ContentDeckCardCount("standard:fire_slash", 12),
                new ContentDeckCardCount("standard:dodge", 12),
                new ContentDeckCardCount("standard:peach", 6)
            ]));
            builder.AddMode(new ContentModeDefinition(ModeId, "五人经典身份（藤甲场景）", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "tengjia-scenario:deck", 3,
                ["classic:sun-quan", "classic:huang-gai", "classic:gan-ning", "classic:lu-meng", "classic:zhang-fei"]));
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
