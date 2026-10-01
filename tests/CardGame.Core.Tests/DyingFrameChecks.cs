using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DyingFrameChecks
{
    internal static void ThirdPartyPeachResumesOrderedFrameAndReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.ModeId, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
            MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        var setup = game.PendingDecision is { Kind: DecisionKind.SelectGeneral } decision &&
                    decision.ValidContentIds.Contains(Fixture.OwnerGeneralId)
            ? decision
            : throw new InvalidOperationException("The rescue owner was not offered at setup.");
        Accept(game, new SelectGeneralCommand(0, Fixture.OwnerGeneralId,
            game.Revision, setup.PromptId));
        for (var step = 0; step < 100 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
        {
            Require(game.PendingDecision is null, "The rescue fixture reached an unexpected setup prompt.");
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        var play = game.PendingDecision is { Kind: DecisionKind.PlayCard } playPrompt
            ? playPrompt
            : throw new InvalidOperationException("The rescue fixture did not reach Play.");
        Accept(game, new UseProgramSkillCommand(0, Fixture.SkillId, "draw-then-wound",
            [], [3], game.Revision, play.PromptId));
        for (var step = 0; step < 100 && game.PendingDecision?.Kind != DecisionKind.RescueDying; step++)
        {
            Require(game.PendingDecision is null,
                "The rescue fixture reached an unexpected prompt before the second responder.");
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        var prompt = game.PendingDecision is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 } rescue
            ? rescue
            : throw new InvalidOperationException("The second rescue responder was not the human owner.");
        var frame = game.ResolutionStack.OfType<DyingFrame>().Single();
        Require(frame is { VictimSeat: 3, ResponderIndex: 1,
                Continuation: DyingContinuationKind.ProgramSkill } &&
                frame.ResponderSeats.SequenceEqual([3, 0, 1, 2]) &&
                prompt.TargetSeat == 3 && game.CreateSnapshot(1).PendingDecision is null,
            "The frozen rescue order or private responder prompt changed.");
        var peach = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("response") == "peach");
        var peachCardId = peach.Cards.Single();
        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var replay = GameReplay.Restore(checkpoint, registry);
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "The second-responder frame did not replay at its checkpoint.");
        var beforeInvalid = State(game);
        var invalid = game.Submit(new AnswerPromptCommand(1, prompt.PromptId, peach.Id, game.Revision));
        Require(!invalid.Accepted && State(game) == beforeInvalid,
            "Another seat must not spend the human responder's Peach.");
        foreach (var branch in new[] { game, replay })
        {
            var current = branch.PendingDecision!;
            Accept(branch, new AnswerPromptCommand(0, current.PromptId,
                current.Choices.Single(choice => choice.Id == peach.Id).Id, branch.Revision));
            for (var step = 0; step < 100 &&
                 !branch.Events.Any(item => item.Payload is DyingResolvedEvent resolved &&
                     resolved.ResolutionId == frame.Id); step++)
            {
                Require(branch.PendingDecision is null,
                    "The physical Peach use left an unexpected child prompt.");
                Accept(branch, new AdvanceOneStepCommand(branch.Revision));
            }
        }
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "The ordered third-party rescue did not replay exactly.");
        Require(game.Events.Count(item => item.Payload is DyingResponseEvent response &&
                    response.ResolutionId == frame.Id && response.ResponderSeat == 0 &&
                    response.UsedPeach && response.PeachCardId == peachCardId) == 1 &&
                game.Events.Count(item => item.Payload is RecoveryAppliedEvent recovery &&
                    recovery.SourceSeat == 0 && recovery.TargetSeat == 3) == 1 &&
                game.Events.Count(item => item.Payload is DyingResolvedEvent resolved &&
                    resolved.ResolutionId == frame.Id && resolved.Survived) == 1 &&
                game.Events.Count(item => item.Payload is ProgramSkillResolvedEvent resolved &&
                    resolved.SkillId == Fixture.SkillId && resolved.Completed) == 1 &&
                game.ResolutionStack.All(item => item is not DyingFrame),
            "The third-party Peach changed provider, victim, or parent completion ownership.");
        Require(game.CardMovements.Any(move => move.CardId == peachCardId &&
                    move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) &&
                game.CardMovements.Any(move => move.CardId == peachCardId &&
                    move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.UseFinished),
            "The third-party Peach did not complete its physical use and cleanup.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? $"{command.GetType().Name} was rejected.");
    }

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|" +
            JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))
        .ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string ModeId = "dying-frame-test:mode";
        internal const string OwnerGeneralId = "dying-frame-test:owner";
        internal const string SkillId = "dying-frame-test:skill";
        private static readonly string[] OtherGeneralIds =
            Enumerable.Range(1, 3).Select(index => $"dying-frame-test:other-{index}").ToArray();

        public PackageManifest Manifest { get; } = new("dying-frame-test", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                 {"id":"dying-frame-test:skill","revision":1,
                  "minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"activations":[
                  {"id":"draw-then-wound","minCards":0,"maxCards":0,
                   "minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,
                   "effects":[{"op":"draw","target":"owner","amount":1},
                              {"op":"loseHp","target":"selectedTarget","amount":4}]}]}]}
                """;
            const string presentation = """
                {"schemaVersion":3,"skills":{"dying-frame-test:skill":
                {"name":"Rescue fixture","description":"Draw, then wound a target."}}}
                """;
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            var text = catalog.Presentations[SkillId];
            builder.AddSkill(new ContentSkillDefinition(SkillId, text.Name, text.Description)
            { Program = catalog.Programs[SkillId] });
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId, "Owner", "cao_cao", SkillId, "wei", BaseHp: 4));
            foreach (var id in OtherGeneralIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "Target", "cao_cao", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("dying-frame-test:deck", "Peach deck", 0, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 40)
                    .Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 5))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(ModeId, "Ordered rescue", 4, 4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1
                }, DeckId: "dying-frame-test:deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [OwnerGeneralId, .. OtherGeneralIds]));
        }
    }
}
