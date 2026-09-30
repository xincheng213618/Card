using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramCompositionEntryChecks
{
    private const int Human = 0;





    internal static void UseActivation(GameEngine game, string skillId)
    {
        var play = game.PendingDecision ?? throw new InvalidOperationException("Play prompt missing.");
        var action = game.GetHumanLegalActions().Single(item =>
            item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == skillId &&
            item.ProgramActivationId == "active");
        var used = game.Submit(new UseProgramSkillCommand(
            Human, skillId, "active", [], [], game.Revision, play.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "The active composition was rejected.");
    }

    private static void EndPlay(GameEngine game)
    {
        ReachPlay(game);
        var play = game.PendingDecision is { Kind: DecisionKind.PlayCard } prompt
            ? prompt : throw new InvalidOperationException("Play prompt missing after activation.");
        var ended = game.Submit(new EndPlayPhaseCommand(Human, game.Revision, play.PromptId));
        Require(ended.Accepted, ended.Error?.Message ?? "Could not end Play.");
    }

    internal static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: Human }) return;
            Require(game.PendingDecision?.PlayerSeat != Human, $"Unexpected human prompt {game.PendingDecision?.Kind}.");
            Advance(game);
        }
        throw new InvalidOperationException("Fixture did not reach Play.");
    }



    private static void AnswerAction(GameEngine game, string action) => Answer(game,
        game.PendingDecision!.Choices.Single(item =>
            item.Parameters.GetValueOrDefault("program-action") == action));

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Prompt missing.");
        var result = game.Submit(new AnswerPromptCommand(
            prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Program answer rejected.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Fixture advance failed.");
    }

    internal static GameEngine Start(ContentRegistry registry, string skillId)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 922, PlayerCount = 5, HumanSeat = Human, HumanRole = Role.Lord,
            ModeId = FixturePackage.ModeId, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Fixture start failed.");
        var prompt = game.PendingDecision!;
        Require(prompt.ValidContentIds.Contains(FixturePackage.OwnerGeneralId), "Owner general was not offered.");
        var selected = game.Submit(new SelectGeneralCommand(
            Human, FixturePackage.OwnerGeneralId, game.Revision, prompt.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? $"Could not select owner for {skillId}.");
        return game;
    }

    internal static ContentRegistry Registry(string skillId, string rules, string presentation)
    {
        var program = SkillProgramCatalog.Load(rules, presentation).Programs[skillId];
        return ContentRegistry.Build(new StandardContentPackage(), new FixturePackage(skillId, program));
    }

    internal static GameCheckpoint RoundTrip(GameCheckpoint value) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(value));
    internal static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(Human, true));
    internal static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    internal static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class FixturePackage(string skillId, SkillProgram program) : IGameContentPackage
    {
        public const string OwnerGeneralId = "fixture:composition-owner";
        public const string ModeId = "identity:composition-entry-5";
        private const string DeckId = "fixture:composition-deck";
        public PackageManifest Manifest { get; } = new("composition-entry", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new(skillId, skillId, "Composition fixture")
            {
                Program = program,
                ExecutionForms = SkillExecutionForm.Trigger,
                ActionForms = SkillActionForm.Active
            });
            builder.AddGeneral(new(OwnerGeneralId, "组合入口", "supporter", skillId, "wei", BaseHp: 4));
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:composition-target-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new(target, "组合目标", "supporter", "standard:none", "shu", BaseHp: 4));
            builder.AddDeck(new(DeckId, "组合牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard("standard:slash", (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(ModeId, "五人组合入口", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, DeckId, GeneralCandidateCount: 5,
                GeneralPoolIds: [OwnerGeneralId, .. targets]));
        }
    }

    private const string RevealSkillId = "fixture:cross-entry-reveal";
    private const string GiftSkillId = "fixture:cross-entry-gift";
    private const string RevealEffects = """
    [{"op":"revealTopCards","target":"owner","amount":4,"resultBind":"revealed","visibility":"public"},
     {"op":"selectCardSubset","target":"owner","sourceBind":"revealed","resultBind":"selected","minimumCards":0,"maximumCards":2,"maximumRankSum":13,"aiOrder":"mostCardsThenRankSum"},
     {"op":"moveBoundCards","target":"owner","sourceBind":"selected","destination":"ownerHand"},
     {"op":"moveBoundCards","target":"owner","sourceBind":"revealed","exceptBind":"selected","destination":"discardPile"}]
    """;
    private const string GiftEffects = """
    [{"op":"draw","target":"owner","amount":2,"resultBind":"drawn"},
     {"op":"giveBoundCard","target":"owner","sourceBind":"drawn","targetKind":"otherLiving"}]
    """;
    private static readonly string RevealRules = Rules(RevealSkillId, RevealEffects);
    private static readonly string GiftRules = Rules(GiftSkillId, GiftEffects);
    private static string Rules(string skillId, string effects) => $$"""
    {"schemaVersion":62,"skills":[{"id":"{{skillId}}","revision":1,"minimumRulesVersion": 171,
    "modifiers":[],"viewAs":[],
    "activations":[{"id":"active","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":{{effects}}}],
    "triggers":[
      {"id":"play","window":"playEnding","subject":"owner","optional":true,"priority":0,"effects":{{effects}}},
      {"id":"turn","window":"turnEnding","subject":"owner","optional":true,"priority":0,"effects":{{effects}}}],
    "contributions":[],"cardIdentities":[]}]}
    """;
    private const string RevealPresentation = """{"schemaVersion":3,"skills":{"fixture:cross-entry-reveal":{"name":"组合亮牌","description":"测试"}}}""";
    private const string GiftPresentation = """{"schemaVersion":3,"skills":{"fixture:cross-entry-gift":{"name":"组合赠牌","description":"测试"}}}""";
}
