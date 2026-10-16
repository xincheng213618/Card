using CardGame.Content.Standard;
using CardGame.Core;

internal static class ActiveEffectScenario
{
    public static (GameEngine Game, ContentRegistry Registry) Luoyi()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = Fixture.Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:effect-owner", game.Revision, Prompt(game)!.PromptId));
        Reach(game, () => Prompt(game)?.SkillPrompt?.SkillId == "boundary:luoyi");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, () => Prompt(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        return (game, registry);
    }

    public static void Reach(GameEngine game, Func<bool> ready)
    {
        for (var step = 0; step < 160; step++)
        {
            if (ready()) return;
            if (game.State.Status == EngineStatus.Completed)
                throw new InvalidOperationException("Active-effect fixture completed before its boundary: " + System.Text.Json.JsonSerializer.Serialize(new
                {
                    game.State.Winner, game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase,
                    Players = game.State.Players.Select(player => new { player.Seat, player.Hp, player.IsAlive }),
                    Turns = game.Events.Select(e => e.Payload).OfType<TurnStartedEvent>(),
                    Effects = game.CreateSnapshot(0).Players[0].ActiveEffects
                }));
            if (Prompt(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play)
                Accept(game, new EndPlayPhaseCommand(0, game.Revision, play.PromptId));
            else if (Prompt(game) is { PlayerSeat: 0, Choices.Count: > 0 })
                Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "skip" ||
                    choice.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" || choice.Cards.Count == 0);
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed active-effect fixture did not reach its requested boundary.");
    }

    public static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var prompt = Prompt(game)!;
        var choice = prompt.Choices.FirstOrDefault(predicate) ?? prompt.Choices[0];
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
    }
    public static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message ?? "Fixture command was rejected.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Mode = "identity:classic-active-effect-4";
        public PackageManifest Manifest { get; } = new("fixture:active-effects", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "boundary:xu-chu");
            builder.AddGeneral(new("fixture:effect-owner", "许褚状态测试", "boundary-xu-chu", "boundary:luoyi", "wei", 20));
            var others = Enumerable.Range(1, 3).Select(seat => $"fixture:effect-other-{seat}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "状态测试对手", "supporter", "standard:none", "wu", 20));
            builder.AddDeck(new("fixture:effect-deck", "状态生命周期牌堆", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 96).Select(index => new ContentDeckPhysicalCard(
                    index % 3 == 0 ? "standard:duel" : "standard:slash", (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "状态生命周期测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 },
                "fixture:effect-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:effect-owner", .. others]));
        }
    }
}
