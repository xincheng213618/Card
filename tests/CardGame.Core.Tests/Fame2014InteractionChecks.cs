using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2014InteractionChecks
{
    public static void EnhancementDrawRetainsOwnerAfterActorReplacement()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 29, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "fixture:enhancement-role", UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 20
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:enhancement-owner", game.Revision, Prompt(game)!.PromptId)));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard && prompt.PlayerSeat == 0);
        var action = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Duel && item.TargetSeats.SequenceEqual(new[] { 1 }));
        Accept(game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, Prompt(game)!.PromptId, action.PlayedCardKind, action.TargetCardId)));
        Reach(game, prompt => prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("enhancement-option") == nameof(CurrentCardEnhancement.DrawAfterDamage)));
        Answer(game, choice => choice.Parameters.GetValueOrDefault("enhancement-option") == nameof(CurrentCardEnhancement.DrawAfterDamage));
        Answer(game, choice => choice.Parameters.GetValueOrDefault("enhancement-option") == "finish");
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:zenhui" &&
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Answer(game, choice => choice.Targets.SequenceEqual(new[] { 2 }));
        Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "become-user");
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single();
        Require(use.EnhancementOwnerSeat == 0 && use.Action?.ProviderSeat == 0,
            "The paused invitation must retain the enhancement owner and physical provider.");
        Replay(game, registry);
        var ownerHand = game.CreateSnapshot(0, true).Players[0].Hand.Count;
        var actorHand = game.CreateSnapshot(2, true).Players[2].Hand.Count;
        var eventsBefore = game.Events.Count;
        Answer(game, choice => choice.Cards.Count == 1);
        for (var step = 0; step < 120 && game.ResolutionStack.Count > 0; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Require(game.ResolutionStack.Count == 0 &&
            game.Events.Skip(eventsBefore).Any(item => item.Payload is ProgramCardUseActorReplacedEvent { ActorSeat: 2, ProviderSeat: 0 }) &&
            game.Events.Skip(eventsBefore).Any(item => item.Payload is DamageAppliedEvent { SourceSeat: 2, TargetSeat: 1, Amount: 1 }),
            "The replaced actor must actually deal the Duel damage.");
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == ownerHand + 2 &&
            game.CreateSnapshot(2, true).Players[2].Hand.Count == actorHand - 1,
            "Damage from the enhanced card must draw for its enhancement owner, even after its user changes.");
        Require(game.CardMovements.Count(move => move.CardId == action.CardId && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1,
            "Actor replacement must not pay the original physical card twice.");
        Replay(game, registry);
    }

    private static PendingDecision? Prompt(GameEngine game) => game.PendingDecision ?? Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat, true).PendingDecision).FirstOrDefault(prompt => prompt is not null);

    private static void Reach(GameEngine game, Func<PendingDecision, bool> condition)
    {
        for (var step = 0; step < 120; step++)
        {
            if (Prompt(game) is { } prompt && condition(prompt)) return;
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        throw new InvalidOperationException("Enhancement/actor interaction did not reach its required prompt.");
    }

    private static void Answer(GameEngine game, Func<PromptChoice, bool> condition)
    {
        var prompt = Prompt(game)!;
        var choice = prompt.Choices.First(condition);
        var command = new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision);
        Accept(game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()));
    }

    private static void Replay(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat, true)) ==
                SnapshotJson.Serialize(restored.CreateSnapshot(seat, true))) &&
            JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(restored.ResolutionStack) &&
            game.CardMovements.SequenceEqual(restored.CardMovements) &&
            game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).SequenceEqual(
                restored.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))),
            "Paused and completed interactions must replay with the same snapshots, frames, physical movements and typed events.");
    }

    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Interaction command failed.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Scenario : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-enhancement-role", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new("fixture:enhancement-owner", "增强与使用者测试", "supporter", "classic:benxi", "shu", BaseHp: 8,
                AdditionalSkillIds: ["classic:zenhui"]));
            foreach (var seat in Enumerable.Range(1, 3))
                builder.AddGeneral(new($"fixture:enhancement-target-{seat}", "目标", "supporter", "standard:none", "wei", BaseHp: 8));
            builder.AddDeck(new("fixture:enhancement-deck", "实体决斗", 8, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 100).Select(index => new ContentDeckPhysicalCard("standard:duel", Suit.Spade, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new("fixture:enhancement-role", "增强与使用者交互", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:enhancement-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:enhancement-owner", "fixture:enhancement-target-1", "fixture:enhancement-target-2", "fixture:enhancement-target-3"]));
        }
    }
}
