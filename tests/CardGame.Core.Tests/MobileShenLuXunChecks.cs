using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MobileShenLuXunChecks
{
    private const string Hero = "mobile:shen-lu-xun", Cuike = "mobile:cuike";
    private const string Watch = "fixture:mobile-luxun-chain-watch", Quiet = "fixture:mobile-luxun-quiet";
    private const string Mode = "identity:classic-mobile-luxun-fixture";
    private const string Payment = "skill-program.mobile:cuike.SelectAndMoveOwnedCard";

    public static void CuikeChainsBeforeDiscardAndColdReturnsOnce()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Hero, g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == Cuike && p.Choices.Any(c => Action(c) == "activate"));
        Need(V(g).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.Junlue)?.Count is null or 0,
            "The actual first Play phase offers the formal even Cuike branch with zero real Junlue marks.");
        var before = V(g, 1).HandCount;
        Need(before > 0 && !V(g, 1).IsChained, "The selected real target begins unchained with physical hand cards.");
        Answer(g, c => Action(c) == "activate");
        Reach(g, p => p.SkillPrompt?.SkillId == Cuike && p.Choices.Any(c => Action(c) == "select-target"));
        Answer(g, c => c.Targets.SequenceEqual([1]));
        Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Cuike);
        var parentId = parent.Id;
        Need(parent.TriggerId == "even-chain-discard" && V(g, 1).IsChained && V(g, 1).HandCount == before &&
             !g.CardMovements.Any(m => m.Reason.Value == Payment) &&
             E<ProgramChainedStateSetEvent>(g).Count(e => e.FrameId == parentId && e.IsChained && e.TargetSeat == 1) == 1,
            "Mobile Cuike really enters chain and opens its native state child before any foreign discard payment.");
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(g, p => p.SkillPrompt?.SkillId == Cuike && p.Choices.Any(c => Action(c) == "select-and-move-owned-card"));
        Need(V(g, 1).IsChained && V(g, 1).HandCount == before && !g.CardMovements.Any(m => m.Reason.Value == Payment),
            "Returning from the actual chain child publishes the foreign-card choice after chaining and before paying.");
        Answer(g, c => Action(c) == "select-and-move-owned-card");
        Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        var moves = g.CardMovements.Where(m => m.Reason.Value == Payment).ToArray();
        Need(V(g, 1).IsChained && V(g, 1).HandCount == before - 1 &&
             moves is [{ From: var from, To: var to }] && from == CardLocation.Hand(1) && to == CardLocation.DiscardPile &&
             E<ProgramChainedStateSetEvent>(g).Count(e => e.FrameId == parentId) == 1 &&
             E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == parentId && e.Activated && e.Completed) == 1 &&
             g.ResolutionStack.All(f => f.Id != parentId),
            "The restored original Cuike frame discards one real target entity, chains once and completes once.");
    }

    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static string? Action(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Need(result.Accepted, result.Error?.Message ?? "Rejected real mobile Lu Xun command.");
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var p = P(g)!;
        var choice = p.Choices.FirstOrDefault(predicate);
        Need(choice is not null, "Missing published mobile Lu Xun choice: " + JsonSerializer.Serialize(p));
        Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, g.Revision));
    }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var step = 0; step < 160; step++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                throw new InvalidOperationException("Unexpected mobile Lu Xun boundary: " + JsonSerializer.Serialize(p));
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Mobile Lu Xun missed the bounded native boundary.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Cold replay preserves four views, the original Cuike frame, native chain child, physical zones and command prefix.");
        return restored;
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(g.AcceptedCommands)
    });
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mobile-luxun", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b), Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"entered","window":"characterEnteredChain","subject":"any","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"chain-seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object>
            {
                [Watch] = new { name = "实际连环子窗", description = "暂停真实连环状态的原生窗口。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Quiet] = new { name = "安静目标", description = "跳过其他角色的出牌阶段。" }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, id, "Real mobile Cuike fixture") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            for (var n = 1; n < 4; n++) b.AddGeneral(new($"fixture:mobile-luxun-peer-{n}", "真实目标", "supporter", Quiet, "wei", 8));
            b.AddDeck(new("fixture:mobile-luxun-deck", "固定实体牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 40).Select(i => new ContentDeckPhysicalCard("standard:dodge", (Suit)(i % 4), i % 13 + 1)).ToArray()
            });
            b.AddMode(new(Mode, "移动神陆逊实结算", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:mobile-luxun-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [Hero, "fixture:mobile-luxun-peer-1", "fixture:mobile-luxun-peer-2", "fixture:mobile-luxun-peer-3"]));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == Hero ? d with { AdditionalSkillIds = [..d.AdditionalSkillIds!, Watch] } : d);
        public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d);
        public void AddCard(ContentCardDefinition d) => b.AddCard(d);
        public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d);
        public void AddMode(ContentModeDefinition d) => b.AddMode(d);
    }
}
