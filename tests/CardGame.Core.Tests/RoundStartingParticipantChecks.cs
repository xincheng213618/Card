using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class RoundStartingParticipantChecks
{
    private const string Mode = "identity:round-participants";
    private const string Fengji = "ol:fengji";

    public static void ForeignOwnerReceivesRoundStartAndColdReturns()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new Fixture(ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage())));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 1, HumanRole = Role.Loyalist,
            ModeId = Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, prompt => prompt.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(1, game.PendingDecision!.ValidContentIds.First(), game.Revision, game.PendingDecision.PromptId));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == Fengji &&
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        var round = Facts<RoundStartedEvent>(game).Single();
        Require(round.ActorSeat != 1 && game.State.CurrentSeat == round.ActorSeat,
            "The actual round must begin in a different participant's turn.");
        game = Cold(game, registry);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, prompt => prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "fengji-option"));
        game = Cold(game, registry);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("accept") == "false");
        Require(Facts<ProgramFengjiOptionChosenEvent>(game).Where(choice => choice.OwnerSeat == 1).ToArray() is
            [{ Option: FengjiOptionKind.Draw, Accepted: false }],
            "The foreign owner commits the first round option once.");
        game = Cold(game, registry);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("accept") == "false");
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        var choices = Facts<ProgramFengjiOptionChosenEvent>(game).Where(choice => choice.OwnerSeat == 1).ToArray();
        Require(choices.Length == 2 && choices.All(choice => choice.OwnerSeat == 1 &&
            choice.RoundNumber == round.RoundNumber && !choice.Accepted) &&
            choices.Select(choice => choice.Option).Distinct().Count() == 2 &&
            Facts<ProgramBindingStartedEvent>(game).Count(binding => binding.SkillId == Fengji && binding.OwnerSeat == 1) == 1,
            "Later turns in the same round must not re-open the completed two-option opportunity.");
        _ = Cold(game, registry);
    }

    private static T[] Facts<T>(GameEngine game) => game.Events.Select(item => item.Payload).OfType<T>().ToArray();

    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        string State(GameEngine value) => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, value.PlayerCount).Select(seat => SnapshotJson.Serialize(value.CreateSnapshot(seat))).ToArray(),
            Frames = JsonSerializer.Serialize(value.ResolutionStack),
            Events = value.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
            value.CardMovements
        });
        Require(State(game) == State(restored), "Cold recovery must reproduce every viewer, round choice, owning frame and physical movement.");
        return restored;
    }

    private static void Reach(GameEngine game, Func<PendingDecision, bool> expected)
    {
        for (var step = 0; step < 80; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 1 } prompt)
            {
                if (expected(prompt)) return;
                throw new InvalidOperationException($"The round participant missed its boundary: {prompt.Kind}: {prompt.Prompt}");
            }
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed round fixture exceeded 80 real commands.");
    }

    private static void Answer(GameEngine game, Func<PromptChoice, bool> expected)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("The round owner lost its prompt.");
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.Single(expected).Id, game.Revision));
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.ToString() ?? "The real round command was rejected.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture(ContentRegistry source) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("round-participant-fixture", new(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(source.GetSkill(Fengji));
            builder.AddSkill(source.GetSkill("ol:xuanhui"));
            builder.AddGeneral(source.Generals["ol:chen-deng"]);
            for (var seat = 0; seat < 3; seat++)
                builder.AddGeneral(source.Generals["ol:chen-deng"] with { Id = $"fixture:round-plain-{seat}" });
            builder.AddDeck(new("fixture:round-deck", "轮开始夹具", 1, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 5)).ToArray()
            });
            builder.AddMode(new(Mode, "轮开始夹具", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:round-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["ol:chen-deng", "fixture:round-plain-0", "fixture:round-plain-1", "fixture:round-plain-2"]));
        }
    }
}
