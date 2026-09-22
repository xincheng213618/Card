using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Content.Standard.Skills;
using CardGame.Core;

internal static class ZongshiModuleChecks
{
    private const int HumanSeat = 0;
    private const string PhaseSkillId = "fixture:zongshi-pindian";

    public static void PlanFollowsWinLossAndAvailability()
    {
        var module = new ZongshiModule();
        var sourceWin = new PindianResult(0, 1, 101, 202, 12, 5);
        var opponentWin = new PindianResult(0, 1, 101, 202, 4, 11);
        var tie = new PindianResult(0, 1, 101, 202, 8, 8);

        Require(module.CreatePlan(new(0, "fixture:pindian", sourceWin, [101, 202])) is
                {
                    CardId: 202,
                    Presentation:
                    {
                        SkillId: "classic:jianyong-zongshi",
                        Name: "纵适",
                        Title: "纵适 · 是否获得拼点牌"
                    }
                },
            "A winning source must be offered the lower opposing Pindian card with generic presentation metadata.");
        Require(module.CreatePlan(new(1, "fixture:pindian", opponentWin, [101, 202]))?.CardId == 101,
            "A winning opponent must be offered the lower source Pindian card.");
        Require(module.CreatePlan(new(0, "fixture:pindian", opponentWin, [101, 202]))?.CardId == 101,
            "A losing source must be offered its own Pindian card.");
        Require(module.CreatePlan(new(0, "fixture:pindian", tie, [101, 202]))?.CardId == 101,
            "A tied source did not win and must be offered its own Pindian card.");
        Require(module.CreatePlan(new(2, "fixture:pindian", sourceWin, [101, 202])) is null,
            "Zongshi must ignore a Pindian that did not involve its owner.");
        Require(module.CreatePlan(new(0, "fixture:pindian", sourceWin, [101])) is null,
            "Zongshi must not claim a card that another result consumer already moved.");
    }

    public static void SharedPindianClaimIsPrivateReplayableAndResumes()
    {
        var registry = Registry();
        var game = ReachZongshiPrompt(registry);
        var prompt = RequireZongshiPrompt(game);
        var result = game.Events.Select(item => item.Payload).OfType<PindianResultDeterminedEvent>().Single().Result;
        var expectedCardId = result.WonBy(HumanSeat)
            ? result.SourceRank < result.OpponentRank ? result.SourceCardId : result.OpponentCardId
            : result.CardOf(HumanSeat);
        Require(game.CreateSnapshot(1).PendingDecision is null,
            "The exact Zongshi claim choice must remain private to its owner.");

        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var claimedReplay = GameReplay.Restore(checkpoint, registry);
        var skipped = GameReplay.Restore(checkpoint, registry);
        var beforeForged = State(game);
        Require(!game.Submit(new AnswerPromptCommand(
                    HumanSeat, prompt.PromptId, new ChoiceId("forged-zongshi"), game.Revision)).Accepted &&
                State(game) == beforeForged,
            "A forged Zongshi answer must not consume the result window or mutate state.");

        var skipPrompt = RequireZongshiPrompt(skipped);
        Accept(skipped.Submit(new AnswerPromptCommand(
            HumanSeat,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "pindian-skip").Id,
            skipped.Revision)));
        Require(skipped.Events.Select(item => item.Payload).OfType<PindianCardClaimedEvent>().Count() == 0 &&
                skipped.Events.Select(item => item.Payload).OfType<SkillModuleResolvedEvent>().Any(item =>
                    item.SkillId == "classic:jianyong-zongshi" && item.OwnerSeat == HumanSeat && !item.Used) &&
                skipped.PendingDecision is null && skipped.State.Phase == TurnPhase.Play &&
                !skipped.ResolutionStack.OfType<PindianFrame>().Any() &&
                !skipped.ResolutionStack.OfType<PhaseSkillFrame>().Any(),
            "Skipping Zongshi must discard the unclaimed Pindian cards and restore the suspended Play phase.");
        Accept(skipped.Submit(new AdvanceOneStepCommand(skipped.Revision)));
        Require(skipped.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat },
            "Normal engine progression must publish the restored Play prompt after skipping Zongshi.");

        Claim(game);
        Claim(claimedReplay);
        var ownerHand = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand;
        Require(ownerHand.Any(card => card.Id == expectedCardId) &&
                game.Events.Select(item => item.Payload).OfType<PindianCardClaimedEvent>().Single() is
                { SkillId: "classic:jianyong-zongshi", OwnerSeat: HumanSeat } claimed &&
                claimed.CardId == expectedCardId &&
                game.Events.Select(item => item.Payload).OfType<SkillModuleResolvedEvent>().Count(item =>
                    item.SkillId == "classic:jianyong-zongshi" && item.OwnerSeat == HumanSeat && item.Used) == 1 &&
                game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat } &&
                !game.ResolutionStack.OfType<PindianFrame>().Any() &&
                !game.ResolutionStack.OfType<PhaseSkillFrame>().Any(),
            "Using Zongshi must move exactly the rule-selected revealed card into hand and resume Play once.");
        Require(State(game) == State(claimedReplay) && Events(game).SequenceEqual(Events(claimedReplay)),
            "A paused Zongshi result choice must replay with identical state and public event order.");
    }

    private static void Claim(GameEngine game)
    {
        var prompt = RequireZongshiPrompt(game);
        Accept(game.Submit(new AnswerPromptCommand(
            HumanSeat,
            prompt.PromptId,
            prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "pindian-claim").Id,
            game.Revision)));
        Require(game.PendingDecision is null && game.State.Phase == TurnPhase.Play,
            "Claiming a Pindian card must restore Play before the next normal engine step.");
        Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    }

    private static GameEngine ReachZongshiPrompt(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 700021,
            PlayerCount = 5,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        var selection = game.PendingDecision ?? throw new InvalidOperationException("Expected general selection.");
        Require(selection.ValidContentIds.Contains(ScenarioPackage.GeneralId),
            "The fixed Zongshi fixture did not publish its human general.");
        Accept(game.Submit(new SelectGeneralCommand(
            HumanSeat, ScenarioPackage.GeneralId, game.Revision, selection.PromptId)));

        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { SkillPrompt.SkillId: PhaseSkillId } activation &&
                activation.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "fixture-pindian-use"))
            {
                Accept(game.Submit(new AnswerPromptCommand(
                    HumanSeat,
                    activation.PromptId,
                    activation.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("action") == "fixture-pindian-use").Id,
                    game.Revision)));
                break;
            }
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }

        var start = game.PendingDecision is { SkillPrompt.SkillId: PhaseSkillId } pending &&
                    pending.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("action") == "pindian-start")
            ? pending
            : throw new InvalidOperationException("The fixed fixture did not reach the shared Pindian selection.");
        var hand = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand;
        var selected = start.Choices
            .OrderByDescending(choice => hand.Single(card => card.Id == choice.Cards.Single()).Rank)
            .ThenBy(choice => choice.Targets.Single())
            .ThenBy(choice => choice.Cards.Single())
            .First();
        Accept(game.Submit(new AnswerPromptCommand(
            HumanSeat, start.PromptId, selected.Id, game.Revision)));
        Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        _ = RequireZongshiPrompt(game);
        return game;
    }

    private static PendingDecision RequireZongshiPrompt(GameEngine game) =>
        game.PendingDecision is
        {
            Kind: DecisionKind.SkillModule,
            PlayerSeat: HumanSeat,
            IsPrivate: true,
            SkillPrompt:
            {
                SkillId: "classic:jianyong-zongshi",
                Name: "纵适",
                Title: "纵适 · 是否获得拼点牌"
            }
        } prompt &&
        prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
            .Order(StringComparer.Ordinal).SequenceEqual(["pindian-claim", "pindian-skip"])
            ? prompt
            : throw new InvalidOperationException("Expected the generic private Zongshi result prompt.");

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new ScenarioPackage());

    private sealed class PindianPhaseModule : IPhaseSkillModule
    {
        public string SkillId => PhaseSkillId;
        public int Revision => 1;
        public PhaseSkillWindow Window => PhaseSkillWindow.PlayStarting;

        public SkillActivationPlan CreatePlan(PhaseSkillContext context)
        {
            PromptChoice Choice(bool use) => new(
                new ChoiceId($"fixture.zongshi-pindian.{(use ? "use" : "skip")}"),
                use ? "发动测试拼点。" : "跳过测试拼点。", [], [],
                new Dictionary<string, string>
                {
                    ["action"] = use ? "fixture-pindian-use" : "fixture-pindian-skip"
                });
            return new SkillActivationPlan(
                new(PhaseSkillId, "测试巧说", "测试巧说 · 是否拼点", "出牌阶段开始时，可以与一名角色拼点。"),
                "是否发动测试拼点？", Choice(true), Choice(false), [new BeginSkillPindian()]);
        }
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-zongshi-module-test-5";
        public const string GeneralId = "fixture:zongshi-owner";
        private const string DeckId = "fixture:zongshi-module-deck";
        private static readonly string[] TargetIds =
        [
            "fixture:zongshi-target-1", "fixture:zongshi-target-2",
            "fixture:zongshi-target-3", "fixture:zongshi-target-4"
        ];

        public PackageManifest Manifest { get; } = new("zongshi-module-test", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new(PhaseSkillId, "测试巧说", "出牌阶段开始时，可以发起共享拼点。")
            {
                PhaseSkill = new PindianPhaseModule()
            });
            builder.AddSkill(new("classic:jianyong-zongshi", "纵适", "拼点后，可以获得规则指定的拼点牌。")
            {
                PindianResultSkill = new ZongshiModule()
            });
            builder.AddGeneral(new(
                GeneralId, "纵适测试武将", "supporter", PhaseSkillId, "shu", BaseHp: 3,
                AdditionalSkillIds: ["classic:jianyong-zongshi"]));
            foreach (var id in TargetIds)
                builder.AddGeneral(new(id, "纵适测试目标", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(DeckId, "纵适固定拼点牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 80)
                    .Select(index => new ContentDeckPhysicalCard(
                        "standard:crossbow", (Suit)(index % 4), index % 13 + 1))
                    .ToArray()
            });
            builder.AddMode(new(
                ModeId, "五人纵适模块场景", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, DeckId, 5, [GeneralId, .. TargetIds]));
        }
    }

    private static void Accept(CommandResult result) =>
        Require(result.Accepted, result.Error?.Message ?? "Command rejected.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
