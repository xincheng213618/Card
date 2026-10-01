using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ResponseUseCompletionChecks
{
    private const string Skill = "fixture:response-use-completion";

    public static void DodgeCompletesAfterCostAndResumesSlashOnce() => VerifyUse("slash");
    public static void CounterspellCompletesAfterCostAndResumesChainOnce() => VerifyUse("nullification");

    private static void VerifyUse(string kind)
    {
        var (game, registry) = ReachResponse(kind);
        var prompt = Prompt(game)!;
        var choice = prompt.Choices.First(item => item.Parameters.GetValueOrDefault("conversion-skill-id") == Skill);
        var cost = choice.Cards.Single();
        var hp = game.CreateSnapshot(0).Players[0].Hp;
        Answer(game, choice);
        Reach(game, pending => pending.Choices.Any(item => item.Parameters.GetValueOrDefault("result-bind") == "accepted"));
        var acceptedWindow = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last();
        var actionId = acceptedWindow.Action.ActionId;
        var acceptedCostZone = kind == "slash" ? CardZoneKind.Processing : CardZoneKind.DiscardPile;
        Require(game.CreateCardZoneDiagnostics().Single(item => item.CardId == cost).Location.Zone == acceptedCostZone,
            "Accepted-response observers must retain their existing cost timing.");
        Replay(game, registry);
        Answer(game, Prompt(game)!.Choices.Single());
        Reach(game, pending => pending.Choices.Any(item => item.Parameters.GetValueOrDefault("result-bind") == "completed"));
        var completedWindow = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last();
        Require(completedWindow.Continuation is null &&
                completedWindow.CompletedResponseReturn?.Kind == (kind == "slash" ? ProgramCompletedResponseKind.Dodge : ProgramCompletedResponseKind.Nullification) &&
                completedWindow.Action.ActionId == actionId && completedWindow.Action.Type == CardActionType.Response &&
                game.CreateCardZoneDiagnostics().Single(item => item.CardId == cost).Location == CardLocation.DiscardPile &&
                game.CreateSnapshot(0).Players[0].Hp == hp,
            "Opt-in use completion must freeze the same accepted provenance after physical cost cleanup and before parent continuation.");
        var frameJson = JsonSerializer.Serialize<ResolutionFrame>(completedWindow);
        var restoredWindow = JsonSerializer.Deserialize<ResolutionFrame>(frameJson) as ProgramCardTriggerWindowFrame;
        Require(restoredWindow?.Continuation is null &&
                restoredWindow?.CompletedResponseReturn == completedWindow.CompletedResponseReturn &&
                restoredWindow?.Action.ActionId == actionId &&
                JsonSerializer.Serialize<ResolutionFrame>(restoredWindow) == frameJson,
            "A paused response completion must serialize its typed return and original action identity.");
        var before = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        var moves = game.CardMovements.Count;
        Require(!game.Submit(new AnswerPromptCommand(0, Prompt(game)!.PromptId, new("not-published"), game.Revision)).Accepted &&
                before == GameCheckpointJson.Serialize(game.CreateCheckpoint()) && moves == game.CardMovements.Count,
            "An unpublished completion answer must reject without a cost, journal or parent mutation.");
        Require(game.CreateSnapshot(1).PendingDecision is null, "Completion choices must remain private to their owner.");
        Replay(game, registry);
        Answer(game, Prompt(game)!.Choices.Single());
        for (var step = 0; step < 64 && game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>()
                 .Any(window => window.Action.ActionId == actionId); step++) Drive(game);
        var resolved = game.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>().ToArray();
        Require(resolved.Count(item => item.TriggerId == "complete" && item.SkillId == Skill) == 1 &&
                resolved.All(item => item.TriggerId != "legacy-complete") &&
                game.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>().Count(item => item.Action.ActionId == actionId) == 1 &&
                game.CardMovements.Count(move => move.CardId == cost && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile) == 1,
            "The use completion must resolve once and preserve the old response observer contract and single physical cleanup.");
        var acceptedEvent = game.Events.Single(item => item.Payload is CardActionAcceptedEvent accepted && accepted.Action.ActionId == actionId);
        var completedEvent = game.Events.Single(item => item.Payload is ProgramCardTriggerResolvedEvent completion &&
            completion.SkillId == Skill && completion.TriggerId == "complete");
        Require(game.Events.Any(item => item.Payload is CardRespondedEvent response && response.CardId == cost &&
                    item.Sequence < acceptedEvent.Sequence) && acceptedEvent.Sequence < completedEvent.Sequence,
            "The response, accepted action, and use-completion result must retain their event order.");
        Replay(game, registry);
    }

    public static void GroupDodgeStaysResponseOnlyAndLoaderRejectsWrongWindow()
    {
        var (game, registry) = ReachResponse("arrow");
        Answer(game, Prompt(game)!.Choices.First(item => item.Parameters.GetValueOrDefault("conversion-skill-id") == Skill));
        Reach(game, pending => pending.Choices.Any(item => item.Parameters.GetValueOrDefault("result-bind") == "accepted"));
        Answer(game, Prompt(game)!.Choices.Single());
        for (var step = 0; step < 30 && game.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == Skill); step++) Drive(game);
        Require(!game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(window => window.CompletedResponseReturn is not null) &&
                !game.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>().Any(item => item.TriggerId == "complete" && item.SkillId == Skill),
            "Dodge supplied to Arrow Barrage is a response, so it must not enter an opt-in card-use completion window.");
        Replay(game, registry);
        var rules = Fixture.Rules("dodge");
        var malformed = rules.Replace("\"window\":\"cardUseCompleted\",\"includeResponseUses\":true", "\"window\":\"cardResponseAccepted\",\"includeResponseUses\":true");
        var rejected = false;
        try { SkillProgramCatalog.Load(malformed, Fixture.Presentation); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "The opt-in response-use completion flag must reject all other windows at the shared loader.");
    }

    public static void CounterspellCompletionDyingRescueRetainsExactParent() => VerifyCompletionDyingRescue("nullification");
    public static void DodgeCompletionDyingRescueRetainsExactParent() => VerifyCompletionDyingRescue("slash");
    private static void VerifyCompletionDyingRescue(string kind)
    {
        var (game, registry) = ReachResponse(kind, true);
        Require(game.CreateSnapshot(0).Players[0].Hp == 1, "The actual responder must start at one HP.");
        Answer(game, Prompt(game)!.Choices.First(item => item.Parameters.GetValueOrDefault("conversion-skill-id") == Skill));
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == "accepted"));
        Answer(game, Prompt(game)!.Choices.Single());
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == "completed"));
        var parent = game.ResolutionStack.OfType<NullificationWindowFrame>().SingleOrDefault();
        var completed = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last();
        Answer(game, Prompt(game)!.Choices.Single());
        Reach(game, p => p.Kind == DecisionKind.RescueDying && p.PlayerSeat == 0);
        Require(game.CreateSnapshot(0).Players[0].Hp == 0 && game.ResolutionStack.OfType<DyingFrame>().Single().ParentFrameId ==
            game.ResolutionStack.OfType<ProgramSkillFrame>().Last().Id, "HP loss must create a direct skill dying child.");
        Replay(game, registry);
        var before = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        Require(!game.Submit(new AnswerPromptCommand(1, Prompt(game)!.PromptId, Prompt(game)!.Choices.First().Id, game.Revision)).Accepted &&
            before == GameCheckpointJson.Serialize(game.CreateCheckpoint()), "A wrong-seat rescue answer must reject atomically.");
        Answer(game, Prompt(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("conversion-binding-id") == "rescue"));
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == "rescue-cost") && game.ResolutionStack.OfType<CardUseFrame>().Last().DyingResponse is not null);
        var rescue = game.ResolutionStack.OfType<CardUseFrame>().Last();
        var cost = rescue.PhysicalCardIds!.Single();
        Require(rescue.DyingResponse is not null && game.CreateCardZoneDiagnostics().Single(c => c.CardId == cost).Location == CardLocation.Processing &&
            game.ResolutionStack.OfType<NullificationWindowFrame>().SingleOrDefault() == parent, "The rescue must retain its real Processing cost and original counterspell cursor.");
        Replay(game, registry);
        before = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        Require(!game.Submit(new AnswerPromptCommand(0, Prompt(game)!.PromptId, new("unpublished-rescue"), game.Revision)).Accepted &&
            before == GameCheckpointJson.Serialize(game.CreateCheckpoint()), "An unpublished rescue-cost answer must reject without a mutation.");
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(before), registry);
        Answer(copy, Prompt(copy)!.Choices.Single());
        Answer(game, Prompt(game)!.Choices.Single());
        Require(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(copy.CreateSnapshot(seat))) &&
            game.CardMovements.SequenceEqual(copy.CardMovements), "The restored rescue-cost pause must continue identically after command JSON replay.");
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == "resumed"));
        Require(game.CreateSnapshot(0).Players[0].Hp == 1 && !game.ResolutionStack.OfType<DyingFrame>().Any() &&
            game.CardMovements.Count(m => m.CardId == cost && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "One rescue must finish dying and resume the suspended completion with exactly one cleanup.");
        Replay(game, registry);
        Answer(game, Prompt(game)!.Choices.Single());
        Require(!game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w => w.Id == completed.Id) &&
            game.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Count(e => e.Action.ActionId == completed.Action.ActionId) == 1 &&
            game.Events.Select(e => e.Payload).OfType<ProgramCardTriggerResolvedEvent>().Count(e => e.TriggerId == "complete" && e.SkillId == Skill && e.OwnerSeat == 0) == 1,
            "The original counterspell completion must exit once after its rescued child.");
        Replay(game, registry);
    }

    private static (GameEngine Game, ContentRegistry Registry) ReachResponse(string kind, bool dying = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(kind, dying));
        for (var seed = 1; seed <= (dying ? 32 : 4); seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, PlayerCount = 4, HumanSeat = 0, HumanRole = dying ? Role.Loyalist : Role.Lord,
                ModeId = dying ? "identity:classic-response-use-completion" : "identity:response-use-completion", UseInteractiveSetup = true, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
            Accept(game.Submit(new StartGameCommand()));

            Accept(game.Submit(new SelectGeneralCommand(0, dying ? game.PendingDecision!.Choices[0].Parameters["general-id"] : "fixture:completion-owner", game.Revision, game.PendingDecision!.PromptId)));
            for (var step = 0; step < 100; step++)
            {
                if (Prompt(game) is { PlayerSeat: 0 } prompt && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("conversion-skill-id") == Skill))
                    return (game, registry);
                Drive(game);
            }
        }
        throw new InvalidOperationException("The bounded actual-response fixture did not reach " + kind);
    }

    private static PendingDecision? Prompt(GameEngine game) => game.PendingDecision ??
        Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    { for (var step = 0; step < 64; step++) { if (Prompt(game) is { } prompt && predicate(prompt)) return; Drive(game); } throw new InvalidOperationException("The expected nested response boundary was not reached."); }
    private static void Drive(GameEngine game)
    {
        if (Prompt(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play)
            Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId)));
        else if (Prompt(game) is { Choices.Count: > 0 } prompt && prompt.Kind != DecisionKind.PlayCard)
            Answer(game, prompt.Choices.FirstOrDefault(choice => choice.Targets.Contains(0)) ?? prompt.Choices.Last());
        else Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    }
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(CommandJson.Deserialize(CommandJson.Serialize([
        new AnswerPromptCommand(Prompt(game)!.PlayerSeat, Prompt(game)!.PromptId, choice.Id, game.Revision)])).Single()));
    private static void Replay(GameEngine game, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(copy.CreateSnapshot(seat))) &&
                game.CardMovements.SequenceEqual(copy.CardMovements) &&
                game.Events.Select(item => $"{item.Sequence}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
                    .SequenceEqual(copy.Events.Select(item =>
                        $"{item.Sequence}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")),
            "Every viewer, ordered event and physical movement must survive paused response-use checkpoint replay.");
    }
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Expected an accepted fixture command.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(string kind, bool dying = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-response-use-completion", new Version(1, 0, 0), []);
        internal const string Presentation = """{"schemaVersion":3,"skills":{"fixture:response-use-completion":{"name":"使用完成观察","description":"共享转换及完成窗口","optionLabels":{"continue":"继续"}},"fixture:response-attack":{"name":"攻击驱动","description":"实际虚拟杀"}}}""";
        internal static string Rules(string output) => $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:response-use-completion","revision":1,
            "viewAs":[{"id":"convert","inputKinds":[],"inputSuits":[],"outputKind":"{{output}}","forPlay":false,"forResponse":true}],
            "triggers":[{"id":"accept","window":"cardResponseAccepted","ownerRelation":"conversionSource","sourceSkillId":"fixture:response-use-completion","allowNoEventTarget":true,
            "optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"accepted","options":[{"id":"continue"}]}]},
            {"id":"complete","window":"cardUseCompleted","includeResponseUses":true,"ownerRelation":"conversionSource","sourceSkillId":"fixture:response-use-completion","allowNoEventTarget":true,
            "optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"completed","options":[{"id":"continue"}]}]},
            {"id":"legacy-complete","window":"cardUseCompleted","ownerRelation":"actor","optional":false,
            "effects":[{"op":"chooseOption","target":"owner","resultBind":"legacy","options":[{"id":"continue"}]}]}]},
            {"id":"fixture:response-attack","revision":1,"triggers":[{"id":"attack","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,
            "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]}]}
            """;
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = Rules(kind == "nullification" ? "nullification" : "dodge");
            if (dying)
            {
                var doc = System.Text.Json.Nodes.JsonNode.Parse(rules)!;
                var skill = doc["skills"]![0]!;
                if (kind == "slash") skill["triggers"]!.AsArray().Add(doc["skills"]![1]!["triggers"]![0]!.DeepClone());
                skill["viewAs"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"rescue","inputKinds":[],"inputSuits":[],"outputKind":"peach","forPlay":false,"forResponse":true}"""));
                skill["triggers"]![1]!["sourceViewAsId"] = "convert";
                var effects = skill["triggers"]![1]!["effects"]!.AsArray();
                effects.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":1}"""));
                effects.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"op":"chooseOption","target":"owner","resultBind":"resumed","options":[{"id":"continue"}]}"""));
                skill["triggers"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"rescue-cost","window":"cardUseCommitted","ownerRelation":"actor","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"rescue-cost","options":[{"id":"continue"}]}]}"""));
                rules = doc.ToJsonString();
            }
            var catalog = SkillProgramCatalog.Load(rules, Presentation);
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new("fixture:completion-owner", "响应者", "supporter", Skill, "wei", dying ? 1 : 20));
            var others = Enumerable.Range(1, 3).Select(index => $"fixture:completion-other-{index}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "攻击者", "supporter", dying ? Skill : kind == "slash" ? "fixture:response-attack" : "standard:none", "shu", dying ? 1 : 20));
            var card = kind switch { "slash" => "standard:bagua", "nullification" => "standard:draw_two", _ => "standard:arrow_barrage" };
            builder.AddDeck(new("fixture:completion-deck", "实际响应牌堆", 4, 2, [new(card, 100)]));
            builder.AddMode(new(dying ? "identity:classic-response-use-completion" : "identity:response-use-completion", "响应使用完成测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:completion-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:completion-owner", .. others]));
        }
    }
}
