using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramCompositionEntryChecks
{
    private const int Human = 0;

    public static void CrossEntryRevealSubsetReplays()
    {
        var registry = Registry(RevealSkillId, RevealRules, RevealPresentation);
        var game = Start(registry, RevealSkillId);
        ReachPlay(game);
        UseActivation(game, RevealSkillId);
        ResolveSubsetWithReplay(game, registry, "active");

        EndPlay(game);
        ReachBinding(game, RevealSkillId, "play");
        AnswerAction(game, "activate");
        ResolveSubsetWithReplay(game, registry, "play");
        ReachBinding(game, RevealSkillId, "turn");
        AnswerAction(game, "activate");
        ResolveSubsetWithReplay(game, registry, "turn");

        var reveals = game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
            .Where(item => item.SkillId == RevealSkillId).ToArray();
        var subsets = game.Events.Select(item => item.Payload).OfType<ProgramCardSubsetSelectedEvent>()
            .Where(item => item.SkillId == RevealSkillId).ToArray();
        Require(reveals.Select(item => item.BindingId).SequenceEqual(new[] { "active", "play", "turn" }) &&
                subsets.Select(item => item.BindingId).SequenceEqual(new[] { "active", "play", "turn" }),
            "The same reveal/subset instructions did not retain their activation and trigger binding ids.");
        Require(game.ResolutionStack.All(frame => frame is not ProgramSkillFrame),
            "The cross-entry reveal composition left a program frame behind.");
    }

    public static void CrossEntryDrawGiftReplays()
    {
        var registry = Registry(GiftSkillId, GiftRules, GiftPresentation);
        var game = Start(registry, GiftSkillId);
        ReachPlay(game);
        UseActivation(game, GiftSkillId);
        ResolveGiftWithReplay(game, registry, "active");

        EndPlay(game);
        ReachBinding(game, GiftSkillId, "play");
        AnswerAction(game, "activate");
        ResolveGiftWithReplay(game, registry, "play");
        ReachBinding(game, GiftSkillId, "turn");
        AnswerAction(game, "activate");
        ResolveGiftWithReplay(game, registry, "turn");

        var gifts = game.Events.Select(item => item.Payload).OfType<ProgramBoundCardGivenEvent>()
            .Where(item => item.SkillId == GiftSkillId).ToArray();
        Require(gifts.Select(item => item.BindingId).SequenceEqual(new[] { "active", "play", "turn" }),
            "The same draw/gift instructions did not give one bound card under each binding identity.");
        Require(game.ResolutionStack.All(frame => frame is not ProgramSkillFrame),
            "The cross-entry gift composition left a program frame behind.");
    }

    private static void ResolveSubsetWithReplay(GameEngine game, ContentRegistry registry, string bindingId)
    {
        var prompt = RequireInstructionPrompt(game, "select-subset");
        Require(prompt.IsPrivate && game.CreateSnapshot(1).PendingDecision is null &&
                game.CreateSnapshot(Human).PublicRevealedCards.Count == 4,
            $"Binding {bindingId} did not expose a private subset prompt over four public cards.");
        var choice = prompt.Choices.OrderByDescending(item => item.Cards.Count)
            .ThenBy(item => item.Id.Value, StringComparer.Ordinal).First();
        var revealed = game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
            .Last(item => item.SkillId == RevealSkillId && item.BindingId == bindingId)
            .Cards.Select(card => card.Id).ToHashSet();
        var movementStart = game.CardMovements.Count;
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Answer(game, choice);
        Answer(replay, replay.PendingDecision!.Choices.Single(item => item.Id == choice.Id));
        var settled = game.CardMovements.Skip(movementStart).Where(move => revealed.Contains(move.CardId) &&
            move.From == CardLocation.Processing &&
            (move.To == CardLocation.Hand(Human) || move.To == CardLocation.DiscardPile)).ToArray();
        Require(revealed.Count == 4 && settled.Length == 4 &&
                settled.Select(move => move.CardId).Distinct().Count() == 4,
            $"Binding {bindingId} did not settle each revealed card once in this answer interval.");
        Require(State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)) &&
                game.CreateSnapshot(Human).PublicRevealedCards.Count == 0,
            $"Binding {bindingId} did not resume its subset checkpoint exactly once.");
    }

    private static void ResolveGiftWithReplay(GameEngine game, ContentRegistry registry, string bindingId)
    {
        var prompt = RequireInstructionPrompt(game, "give-bound-card");
        Require(prompt.IsPrivate && game.CreateSnapshot(1).PendingDecision is null &&
                prompt.Choices.Any(item => item.Parameters.GetValueOrDefault("program-action") == "keep-bound-cards"),
            $"Binding {bindingId} did not expose a private gift/keep prompt.");
        var gift = prompt.Choices.First(item =>
            item.Parameters.GetValueOrDefault("program-action") == "give-bound-card");
        Require(gift.Cards.Count == 1 && gift.Targets.Count == 1,
            "A bound-card gift must commit exactly one physical card and one target.");
        var movementStart = game.CardMovements.Count;
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Answer(game, gift);
        Answer(replay, replay.PendingDecision!.Choices.Single(item => item.Id == gift.Id));
        Require(game.CardMovements.Skip(movementStart).Count(move =>
                move.CardId == gift.Cards[0] && move.From == CardLocation.Hand(Human) &&
                move.To == CardLocation.Hand(gift.Targets[0])) == 1,
            "The bound card was not given exactly once in this answer interval.");
        Require(State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
            $"Binding {bindingId} did not resume its gift checkpoint exactly once.");
    }

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

    private static void ReachBinding(GameEngine game, string skillId, string bindingId)
    {
        for (var step = 0; step < 128; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: var actual } prompt &&
                actual == skillId && prompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("binding-id") == bindingId)) return;
            Require(game.PendingDecision?.PlayerSeat != Human, $"Unexpected human prompt {game.PendingDecision?.Kind}.");
            Advance(game);
        }
        throw new InvalidOperationException($"Fixture did not reach binding {bindingId}.");
    }

    private static PendingDecision RequireInstructionPrompt(GameEngine game, string action) =>
        game.PendingDecision is { Kind: DecisionKind.ProgramTrigger } prompt && prompt.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == action)
            ? prompt : throw new InvalidOperationException($"Expected program instruction {action}.");

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
    {"schemaVersion":60,"skills":[{"id":"{{skillId}}","revision":1,"minimumRulesVersion":170,
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
