using CardGame.Content.Standard;
using CardGame.Core;

internal static class PrivateReserveScenario
{
    public const string Qixing = "ol:qixing";
    public const string Gale = "ol:kuangfeng";
    public const string Mist = "ol:dawu";
    public static ContentRegistry Registry() => ContentRegistry.Build(new StandardContentPackage(), new Fixture());

    public static GameEngine Start(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 19, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "identity:private-reserve-ui", UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 16
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "ol:shen-zhuge-liang", game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, g => IsQixingOffer(g));
        return game;
    }

    public static bool IsQixingOffer(GameEngine game) => game.PendingDecision is { PlayerSeat: 0, SkillPrompt.SkillId: Qixing } prompt &&
        prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");

    public static void Reach(GameEngine game, Func<GameEngine, bool> condition)
    {
        for (var step = 0; step < 200; step++)
        {
            if (condition(game)) return;
            if (game.PendingDecision is { PlayerSeat: 0 } prompt)
            {
                if (prompt.Kind == DecisionKind.PlayCard)
                {
                    Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                    continue;
                }
                var choice = prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip") ?? prompt.Choices.FirstOrDefault();
                if (choice is not null) { Answer(game, choice); continue; }
            }
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        throw new InvalidOperationException("The fixed private-reserve scenario did not reach its requested boundary.");
    }

    public static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(
        new AnswerPromptCommand(game.PendingDecision!.PlayerSeat, game.PendingDecision.PromptId, choice.Id, game.Revision)));

    public static void Accept(CommandResult result)
    {
        if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message ?? "Private-reserve command rejected.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("private-reserve-ui-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:shen-zhuge-liang");
            var others = Enumerable.Range(1, 3).Select(index => "private-reserve:other-" + index).ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "陪测", "cao_cao", "standard:none", "wei", 8));
            builder.AddDeck(new("private-reserve:deck", "固定牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 100).Select(index => new ContentDeckPhysicalCard(
                    "standard:nullification", (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new("identity:private-reserve-ui", "七星交互", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "private-reserve:deck", GeneralCandidateCount: 4, GeneralPoolIds: ["ol:shen-zhuge-liang", .. others]));
        }
    }
}
