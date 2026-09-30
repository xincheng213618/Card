using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class AssistedFactionSlashChecks
{
    public static void ProviderPaymentAndExhaustionResumeAndReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        foreach (var success in new[] { true, false })
        {
            var (game, actor) = Prepare(registry);
            var handBefore = game.CreateSnapshot(0, true).Players[0].HandCount;
            Activate(game, "request", actor);
            var targetPrompt = game.PendingDecision!;
            Answer(game, targetPrompt.Choices.First(c => c.Targets.Single() != 0));
            for (var step = 0; step < 100 && game.PendingDecision?.Kind != DecisionKind.RespondSlash; step++) Step(game);
            Require(game.PendingDecision is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 }, "The actual Shu Lord must pause to ask the human provider for Slash.");
            Require(game.Events.Any(e => e.Payload is FactionSlashRequestedEvent request && request.OwnerSeat == actor && request.SkillId == "classic:jijiang"), "The request must use the registered Lord's actual Jijiang policy.");
            var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
            var providerCard = game.PendingDecision!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "faction-slash-slash").Cards.Single();
            foreach (var branch in new[] { game, replay })
            {
                Answer(branch, branch.PendingDecision!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == (success ? "faction-slash-slash" : "faction-slash-decline")));
                for (var step = 0; step < 100 && branch.ResolutionStack.Count > 0; step++) Step(branch);
                Require(branch.ResolutionStack.Count == 0, "The provider continuation must finish its original program.");
                var result = branch.Events.Select(e => e.Payload).OfType<ProgramOptionChosenEvent>().Last(e => e.SkillId == "fixture:assisted-driver");
                Require(result.OptionId == (success ? "used-slash" : "declined"), "Provider success or exhaustion must commit the 701 result exactly.");
                Require(branch.CreateSnapshot(0, true).Players[0].HandCount == handBefore + (success ? -1 : 2), "Success pays the provider's card; exhaustion gains the actor's two cards.");
                if (success)
                {
                    Require(branch.Events.Any(e => e.Payload is CardUsedEvent used && used.SourceSeat == actor && used.CardId == providerCard && used.CardKind == CardKind.Slash), "The Lord must actually use the provider-owned physical Slash.");
                    Require(branch.CardMovements.Any(m => m.CardId == providerCard && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) && branch.CardMovements.Any(m => m.CardId == providerCard && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile), "The real provider payment must enter processing and finish in discard.");
                }
                else Require(branch.CreateSnapshot(0, true).Players[actor].HandCount == 0 && branch.CardMovements.Count(m => m.From == CardLocation.Hand(actor) && m.To == CardLocation.Hand(0)) == 2, "All-provider refusal must resume the conditional two-card transfer.");
            }
            Equal(game, replay);
            Equal(game, GameReplay.Replay(game.CreateCheckpoint().Options, CommandJson.Deserialize(CommandJson.Serialize(game.AcceptedCommands)), registry));
        }
        FireProviderConversion(1);
        FireProviderConversion(2);
    }
    private static void FireProviderConversion(int count)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(count));
        var (game, actor) = Prepare(registry);
        Require(game.CreateSnapshot(0, true).Players[0].Skills!.Any(s => s.ContentId == "fixture:provider-fire") && game.CreateSnapshot(0, true).Players.Skip(1).All(p => p.Skills!.All(s => s.ContentId != "fixture:provider-fire")), "Only the human provider must have the typed fire conversion.");
        Activate(game, "request", actor);
        Answer(game, game.PendingDecision!.Choices.First(c => c.Targets.Single() != 0));
        for (var i = 0; i < 100 && game.PendingDecision?.Kind != DecisionKind.RespondSlash; i++) Step(game);
        Require(game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }, "Typed fire provider must pause at its real provider prompt.");
        var choice = game.PendingDecision!.Choices.First(c => c.Cards.Count == count && c.Parameters.GetValueOrDefault("response-card-kind") == "FireSlash");
        var ids = choice.Cards.ToArray();
        var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        foreach (var branch in new[] { game, replay })
        {
            Answer(branch, branch.PendingDecision!.Choices.Single(c => c.Id == choice.Id));
            for (var i = 0; i < 100 && branch.ResolutionStack.Count > 0; i++) Step(branch);
            Require(branch.ResolutionStack.Count == 0, "Typed fire provider must finish the original program.");
            var accepted = branch.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Last(e => e.Action.ActorSeat == actor && e.Action.EffectiveKind == CardKind.FireSlash);
            Require(accepted.Action.PhysicalCards.Select(c => c.CardId).Order().SequenceEqual(ids.Order()) && accepted.Action.PhysicalCards.All(c => c.From == CardLocation.Hand(0)) && accepted.Action.ConversionChain.Any(c => c.SkillId == "fixture:provider-fire" && c.OwnerSeat == 0 && c.BindingId == "fire"), "The FireSlash must retain its exact provider payment and typed conversion source.");
            foreach (var id in ids) Require(branch.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) && branch.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile), "Every typed fire payment must move through processing to discard.");
            Require(branch.Events.Select(e => e.Payload).OfType<ProgramOptionChosenEvent>().Last(e => e.SkillId == "fixture:assisted-driver").OptionId == "used-slash", "Typed fire provider must commit the used-slash result.");
        }
        Equal(game, replay);
        Equal(game, GameReplay.Replay(game.CreateCheckpoint().Options, CommandJson.Deserialize(CommandJson.Serialize(game.AcceptedCommands)), registry));
    }
    private static (GameEngine, int) Prepare(ContentRegistry registry)
    {
        // Seed 3 gives the human a Slash and the actual Shu Lord two Peaches after clearing hands.
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 3, HumanSeat = 0, HumanRole = Role.Loyalist, PlayerCount = 4, ModeId = "identity:classic-assisted", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Accept(game.Submit(new StartGameCommand()));
        var setup = game.PendingDecision!;
        Accept(game.Submit(new SelectGeneralCommand(0, setup.ValidContentIds.Contains("fixture:assisted-2") ? "fixture:assisted-2" : setup.ValidContentIds[0], game.Revision, setup.PromptId)));
        for (var step = 0; step < 200 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++) Step(game);
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.CreateSnapshot(0, true).Players[0].Hand.Any(c => c.Kind == CardKind.Slash), "The fixed human provider must retain a physical Slash.");
        var actor = game.CreateSnapshot(0, true).Players.Single(p => p.Role == Role.Lord).Seat;
        Require(actor == 3 && actor != 0, "The actual Lord must be the distinct requested actor.");
        foreach (var peer in Enumerable.Range(1, 3)) { Activate(game, "clear", peer); Drain(game); }
        Require(game.CreateSnapshot(0, true).Players.Skip(1).All(p => p.HandCount == 0), "Actual card movement must clear all alternative provider hands.");
        Activate(game, "draw", actor); Drain(game);
        Require(game.CreateSnapshot(0, true).Players[actor].HandCount == 2 && game.CreateSnapshot(0, true).Players[actor].Hand.All(c => c.Kind == CardKind.Peach), "The fixed actor must retain exactly two real non-Slash cards for the refusal branch.");
        return (game, actor);
    }
    private static void Activate(GameEngine game, string activation, int target) => Accept(game.Submit(new UseProgramSkillCommand(0, "fixture:assisted-driver", activation, [], [target], game.Revision, game.PendingDecision!.PromptId)));
    private static void Drain(GameEngine game) { for (var i = 0; i < 100 && game.ResolutionStack.Count > 0; i++) Step(game); Require(game.ResolutionStack.Count == 0, "Fixture movement must finish."); for (var i = 0; i < 100 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Step(game); }
    private static void Step(GameEngine game)
    {
        var prompt = game.PendingDecision;
        if (prompt is { PlayerSeat: 0 } && prompt.Kind != DecisionKind.PlayCard)
            Answer(game, prompt.Choices.FirstOrDefault(c => c.Parameters.Values.Any(v => v is "skip" or "decline" or "faction-slash-decline" or "take-damage")) ?? prompt.Choices.First());
        else Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    }
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(new AnswerPromptCommand(game.PendingDecision!.PlayerSeat, game.PendingDecision.PromptId, choice.Id, game.Revision)));
    private static void Equal(GameEngine game, GameEngine replay)
    {
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(replay.CreateSnapshot(0, true)), "Provider snapshots must replay.");
        Require(game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).SequenceEqual(replay.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))), "Provider typed events must replay.");
        Require(JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(replay.CardMovements), "Provider physical movements must replay.");
    }
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Rejected");
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private sealed class Fixture(int conversionCount = 0) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("assisted-faction-fixture", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var clearEffects = string.Join(",", Enumerable.Repeat("""{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","skipIfNoCards":true}""", 20));
            var rules = $$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:assisted-driver","revision":1,"minimumRulesVersion":{{{GameCheckpoint.CurrentRulesVersion}}},"activations":[
                {"id":"clear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":3,"effects":[{{{clearEffects}}}]},
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"draw","target":"selectedTarget","amount":2}]},
                {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"requestSlashAgainstChosenTarget","target":"selectedTarget","resultBind":"answer"},{"op":"takeSelectedTargetCards","target":"selectedTarget","amount":2,"condition":{"kind":"choiceIs","sourceBind":"answer","optionId":"declined"}}]}]}]}
                """;
            var catalog = SkillProgramCatalog.Load(rules, """{"schemaVersion":3,"skills":{"fixture:assisted-driver":{"name":"Driver","description":"Fixture"}}}""");
            builder.AddSkill(new ContentSkillDefinition("fixture:assisted-driver", "Driver", "Fixture") { Program = catalog.Programs["fixture:assisted-driver"] });
            if (conversionCount > 0)
            {
                var conversion = SkillProgramCatalog.Load($$$"""
                    {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:provider-fire","revision":1,"minimumRulesVersion":{{{GameCheckpoint.CurrentRulesVersion}}},"viewAs":[{"id":"fire","inputKinds":["slash","peach"],"inputSuits":[],"outputKind":"fireSlash","forPlay":true,"forResponse":false,"sourceZones":["hand"],"inputCount":{{{conversionCount}}},"extendedUse":true}]}]}
                    """, """{"schemaVersion":3,"skills":{"fixture:provider-fire":{"name":"Fire","description":"Typed conversion"}}}""");
                builder.AddSkill(new ContentSkillDefinition("fixture:provider-fire", "Fire", "Typed conversion") { Program = conversion.Programs["fixture:provider-fire"] });
            }
            var ids = Enumerable.Range(0, 4).Select(i => $"fixture:assisted-{i}").ToArray();
            foreach (var id in ids) builder.AddGeneral(new ContentGeneralDefinition(id, "Fixture", "supporter", conversionCount > 0 && id == "fixture:assisted-2" ? "fixture:assisted-driver" : "classic:jijiang", FactionId: "shu", BaseHp: 20, AdditionalSkillIds: conversionCount > 0 && id == "fixture:assisted-2" ? ["fixture:provider-fire"] : ["fixture:assisted-driver"]));
            builder.AddDeck(new ContentDeckRecipe("fixture:assisted-deck", "Fixture", 6, 0, [new ContentDeckCardCount("standard:slash", 30), new ContentDeckCardCount("standard:peach", 100)]));
            builder.AddMode(new ContentModeDefinition("identity:classic-assisted", "Fixture", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, "fixture:assisted-deck", GeneralCandidateCount: 4, GeneralPoolIds: ids));
        }
    }
}
