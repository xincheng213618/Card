using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ActualUseTargetPauseChecks
{
    private const string Mode = "identity:classic-actual-target-pause";
    private const string Source = "fixture:actual-target-pause-source";

    public static void PacedAiTargetPromptCommitsColdAndReturnsOnce()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        Require(game.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 },
            "The fixed actual-target fixture must begin at the real general choice.");
        Accept(game, new SelectGeneralCommand(0, Source, game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; step < 24 && game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "The fixed actual-target fixture must reach its first human Play phase.");
        var play = game.PendingDecision!;
        Require(game.CreateSnapshot(0).Players.Select(player => player.Hp).Distinct().Count() == 1,
            "The fixed identity setup must begin with tied current HP for the target-peer rule.");
        var slash = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash &&
            action.CardId is not null && action.TargetSeat == 1);
        var hpBefore = game.CreateSnapshot(1).Players[1].Hp;
        Accept(game, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, game.Revision,
            play.PromptId, slash.PlayedCardKind));

        var prompt = game.CreateSnapshot(1).PendingDecision!;
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single();
        var window = game.ResolutionStack.OfType<ActualUseTargetWindowFrame>().Single();
        Require(prompt is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 1, IsPrivate: true } &&
                game.State.Status == EngineStatus.Running && game.ResolutionStack.Count == 2 &&
                window.ParentFrameId == use.Id && window.Candidates[window.CandidateIndex].OwnerSeat == 1 &&
                window.Candidates[window.CandidateIndex].SkillId == "ol:tianming" &&
                window.Contexts[window.CandidateIndex].ActualUseTarget is { } identity &&
                identity.CardUseFrameId == use.Id && identity.ActorSeat == 0 && identity.TargetSeat == 1 &&
                game.CreateSnapshot(0).PendingDecision is null && game.CreateSnapshot(2).PendingDecision is null &&
                game.CreateSnapshot(1).PendingDecision?.PromptId == prompt.PromptId,
            "A paced human Slash must commit its exact AI-owned target prompt without exposing it to other viewers.");

        var frozen = Prefix(game);
        var rejected = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, game.Revision));
        Require(!rejected.Accepted && Prefix(game) == frozen,
            "A foreign actor cannot consume the AI-owned target prompt or mutate its committed prefix.");
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Prefix(game) == frozen, "Cold replay must retain the exact accepted AI-target pause and private player views.");

        for (var step = 0; step < 96 && game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
        {
            Require(game.PendingDecision is not { PlayerSeat: 0 },
                "Tied current HP must avoid an unrelated human unique-peer prompt.");
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                game.ResolutionStack.All(frame => frame.Id != use.Id && frame.Id != window.Id) &&
                game.CreateSnapshot(1).Players[1].Hp == hpBefore - 1 &&
                game.Events.Select(item => item.Payload).OfType<CardUsedEvent>().Count(item => item.CardId == slash.CardId) == 1 &&
                game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Count(item => item.SourceSeat == 0 && item.TargetSeat == 1) == 1,
            "The AI continuation must return once to the original Slash, apply its one damage and restore human Play.");
        var settled = Prefix(game);
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Prefix(restored) == settled, "The completed original attack must cold-replay without repeating its target window or damage.");
    }

    private static string Prefix(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Facts = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "The real actual-target command was rejected.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:actual-target-pause", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            const string neutral = "fixture:actual-target-pause-neutral";
            builder.AddSkill(new(neutral, "Neutral source", "No runtime program"));
            builder.AddGeneral(new(Source, "Human source", "supporter", neutral, "wei", 3));
            var targets = Enumerable.Range(1, 3).Select(index => $"fixture:actual-target-pause-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new(target, "AI target", "supporter", "ol:tianming", "qun", 4));
            const string deck = "fixture:actual-target-pause-deck";
            builder.AddDeck(new(deck, "Small physical Slash deck", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7)).ToArray() });
            builder.AddMode(new(Mode, "Actual AI target pause", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Source, .. targets]));
        }
    }
}
