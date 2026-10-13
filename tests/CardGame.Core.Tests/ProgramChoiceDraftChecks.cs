using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramChoiceDraftChecks
{
    private const string Skill = "fixture:owned-choice";

    public static void MultiStepBindingsPrivacyAndReplay()
    {
        foreach (var decline in new[] { false, true })
        {
            var (game, registry) = Create();
            Use(game, "hands");
            Reach(game, "strategic-choice");
            var frameId = Current(game).Id;
            var selected = new List<int>();
            while (selected.Count < (decline ? 1 : 4))
            {
                var frame = Current(game);
                Require(frame.Id == frameId && frame.StrategicDraft is { } draft &&
                    draft.Selected.SequenceEqual(selected), "The owning frame must retain each intermediate selection.");
                Frozen(frame.StrategicDraft!.Candidates); Frozen(frame.StrategicDraft.Selected);
                Require(Enumerable.Range(1, 3).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
                    "Only the selecting owner may see either hand in the suspended private prompt.");
                game = Cold(game, registry);
                RejectUnknown(game);
                var card = Prompt(game).Choices.First(choice => choice.Cards.Count == 1);
                selected.Add(card.Cards.Single()); Answer(game, card);
            }
            if (decline)
            {
                game = Cold(game, registry);
                Answer(game, Prompt(game).Choices.Single(choice => choice.Parameters.GetValueOrDefault("choice") == "decline"));
            }
            Reach(game, "choose-option");
            var bound = Current(game);
            var binding = bound.CardSetBindings.Single(item => item.Name == "hands");
            Require(bound.Id == frameId && bound.StrategicDraft is null &&
                binding.CardIds.SequenceEqual(decline ? [] : selected) &&
                binding.SourceLocations.Count == binding.CardIds.Count &&
                binding.Visibility == SkillProgramCardSetVisibility.Private,
                "Retiring the draft must preserve the newly written card-set binding for the following instruction.");
            var before = game.CardMovements.Count;
            game = Cold(game, registry); Answer(game, Prompt(game).Choices.Single());
            Settle(game);
            Require(game.CardMovements.Skip(before).Count(move => move.Reason.Value == $"skill-program.{Skill}.ApplyHandDiscardShare" &&
                    move.To == CardLocation.DiscardPile) == (decline ? 0 : 4) &&
                game.ResolutionStack.All(frame => frame.Id != frameId),
                "The restored binding must pay exactly the chosen cards once, or no cards after declining.");
            Cold(game, registry);
        }

        var (chained, chainRegistry) = Create();
        Use(chained, "prepare-chain"); Settle(chained); ReachPlay(chained);
        Use(chained, "chain", []); Reach(chained, "strategic-choice");
        var chainFrameId = Current(chained).Id;
        foreach (var seat in new[] { 0, 1 })
        {
            chained = Cold(chained, chainRegistry);
            Answer(chained, Prompt(chained).Choices.Single(choice => choice.Targets.SequenceEqual([seat])));
        }
        var selection = Current(chained).StrategicDraft!;
        Require(selection.Selected.SequenceEqual([0, 1]) && selection.Maximum == 2,
            "The chained target draft must retain ordered selections up to the consumed marker count.");
        Frozen(selection.Candidates); Frozen(selection.Selected);
        chained = Cold(chained, chainRegistry);
        Answer(chained, Prompt(chained).Choices.Single(choice => choice.Parameters.GetValueOrDefault("choice") == "finish"));
        Reach(chained, "choose-option");
        Require(Current(chained).StrategicDraft is null && Current(chained).SelectedTargetSeats.SequenceEqual([0, 1]),
            "Finishing target selection must preserve its target set after the draft is removed.");
        chained = Cold(chained, chainRegistry); Answer(chained, Prompt(chained).Choices.Single());
        Settle(chained);
        Require(chained.CreateSnapshot(0).Players[0].HandCount == 1 &&
            chained.ResolutionStack.All(frame => frame.Id != chainFrameId),
            "The completed chained-target activation must finish its following effect and retire its frame.");
        chained = Cold(chained, chainRegistry); ReachPlay(chained);
        Use(chained, "one", [0, 1]);
        Reach(chained, "strategic-choice");
        var oneFrameId = Current(chained).Id;
        Require(oneFrameId != chainFrameId && Current(chained).StrategicDraft is
            { Op: SkillProgramEffectOp.SelectOneSelectedTarget } one && one.Candidates.SequenceEqual([0, 1]),
            "A separate activation must own a fresh narrowing draft over its declared target set.");
        chained = Cold(chained, chainRegistry);
        Answer(chained, Prompt(chained).Choices.Single(choice => choice.Targets.SequenceEqual([1])));
        Reach(chained, "choose-option");
        Require(Current(chained).Id == oneFrameId && Current(chained).StrategicDraft is null &&
            Current(chained).SelectedTargetSeats.SequenceEqual([1]),
            "The narrowing result must survive draft retirement before the next instruction.");
        chained = Cold(chained, chainRegistry); Answer(chained, Prompt(chained).Choices.Single());
        Settle(chained);
        Require(chained.CreateSnapshot(1).Players[1].HandCount == 1 &&
            chained.ResolutionStack.All(frame => frame.Id != oneFrameId),
            "The chosen target receives the following effect once and the frame retires.");
        Cold(chained, chainRegistry);
    }

    internal static void Frozen(IReadOnlyList<int> values)
    {
        Require(values is not int[], "Frame draft collections must not expose mutable arrays.");
        if (values is IList<int> list)
        {
            try { list.Add(-1); }
            catch (NotSupportedException) { return; }
            throw new InvalidOperationException("Frame draft collections must reject external mutation.");
        }
    }

    private static ProgramSkillFrame Current(GameEngine game) => game.ResolutionStack.OfType<ProgramSkillFrame>().Last();
    private static PendingDecision Prompt(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).First(prompt => prompt is not null)!;
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision,
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Events = game.Events.Select(envelope => new
        {
            envelope.Id, envelope.ParentId, envelope.Sequence, envelope.Revision, envelope.CorrelationId,
            Payload = JsonSerializer.Serialize(envelope.Payload, envelope.Payload.GetType())
        }).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(game) == State(copy), "Cold replay must preserve every view, draft, event, revision, command and card zone.");
        return copy;
    }
    private static void RejectUnknown(GameEngine game)
    {
        var before = State(game); var prompt = Prompt(game);
        var rejected = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, new("not-published"), game.Revision));
        Require(!rejected.Accepted && State(game) == before, "An unpublished answer must preserve the suspended frame and all observable state.");
    }
    private static void Answer(GameEngine game, PromptChoice choice)
    { var prompt = Prompt(game); Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision)); }
    private static void Use(GameEngine game, string activation, IReadOnlyList<int>? targets = null) => Accept(game,
        new UseProgramSkillCommand(0, Skill, activation, [], targets ?? [1], game.Revision, Prompt(game).PromptId));
    private static void Reach(GameEngine game, string action)
    {
        for (var step = 0; step < 30; step++)
        {
            if (Enumerable.Range(0, 4).Any(seat => game.CreateSnapshot(seat).PendingDecision?.Choices
                .Any(choice => choice.Parameters.GetValueOrDefault("program-action") == action) == true)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException($"The fixed choice fixture did not reach {action}.");
    }
    private static void Settle(GameEngine game)
    {
        for (var step = 0; step < 30 && game.ResolutionStack.Count > 0; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.ResolutionStack.Count == 0, "The fixed choice fixture left an unresolved frame.");
    }
    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 30 && game.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(Prompt(game).Kind == DecisionKind.PlayCard, "The fixed choice fixture must reach its play phase.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Choice fixture command rejected."); }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine Game, ContentRegistry Registry) Create()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:owned-choice",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 10
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:choice-owner", game.Revision, Prompt(game).PromptId));
        ReachPlay(game);
        return (game, registry);
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-owned-choices", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"fixture:owned-choice","revision":1,"activations":[
                    {"id":"hands","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[
                      {"op":"draw","target":"owner","amount":4},{"op":"draw","target":"selectedTarget","amount":4},
                      {"op":"selectDistinctSuitHandDiscards","target":"selectedTarget","resultBind":"hands"},
                      {"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]},
                      {"op":"applyHandDiscardShare","target":"owner","sourceBind":"hands"}]},
                    {"id":"prepare-chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[
                      {"op":"setChainedState","target":"owner","chained":true},{"op":"setChainedState","target":"selectedTarget","chained":true},
                      {"op":"setMarkerAmount","target":"owner","marker":"junlue","amount":2}]},
                    {"id":"chain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[
                      {"op":"selectChainedByMarker","target":"owner","marker":"junlue"},
                      {"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]},
                      {"op":"draw","target":"owner","amount":1}]},
                    {"id":"one","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[
                      {"op":"selectOneSelectedTarget","target":"owner"},
                      {"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]},
                      {"op":"draw","target":"selectedTarget","amount":1}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:owned-choice":{"name":"帧内选择","description":"多步选择及后续绑定","optionLabels":{"continue":"继续"}}}}""");
            builder.AddSkill(new(Skill, "帧内选择", "多步选择及后续绑定") { Program = catalog.Programs[Skill] });
            builder.AddGeneral(new("fixture:choice-owner", "选择者", "supporter", Skill, "wei", BaseHp: 9));
            foreach (var seat in Enumerable.Range(1, 3))
                builder.AddGeneral(new($"fixture:choice-{seat}", "选择目标", "supporter", "standard:none", "wei", BaseHp: 9));
            builder.AddDeck(new("fixture:choice-deck", "四花色小牌堆", 0, 0, [])
            {
                PhysicalCards = new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }.SelectMany(suit =>
                    new[] { new ContentDeckPhysicalCard("standard:dodge", suit, 1), new ContentDeckPhysicalCard("standard:dodge", suit, 2) }).ToArray()
            });
            builder.AddMode(new("identity:owned-choice", "帧内选择测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:choice-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:choice-owner", "fixture:choice-1", "fixture:choice-2", "fixture:choice-3"]));
        }
    }
}
