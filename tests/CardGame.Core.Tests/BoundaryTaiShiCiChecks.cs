using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryTaiShiCiChecks
{
    private const string Driver = "fixture:tsc-contest";
    private const string Rank = "fixture:tsc-rank";

    public static void BothRandomStrategiesFreezeReceiptsAndClaimTiedSlashes()
    {
        var (game, registry) = Start("both");
        var selected = UseTianyi(game);
        Require(P(game) is { PlayerSeat: 0, SkillPrompt.SkillId: "boundary:hanzhan" }, "The source policy acts before the opponent policy.");
        Replay(game, registry);
        Answer(game, "pindian-random", true);
        var first = (PindianFrame)game.ResolutionStack[^1];
        var receipt = first.RandomSelection!.Receipts.Single();
        Require(receipt is { OwnerSeat: 0, TargetSeat: 1 } &&
            P(game) is { PlayerSeat: 1 } && game.CreateSnapshot(0).PendingDecision is null &&
            game.CreateSnapshot(-1).PendingDecision is null && game.CreateSnapshot(-1).PublicRevealedCards.Count == 0 &&
            game.CreateCardZoneDiagnostics().Single(c => c.CardId == receipt.CardId).Location == CardLocation.Hand(1),
            "The first identity is frozen in the original frame while the second policy remains private and neither card has been paid.");
        Require(first.RandomSelection.Receipts is System.Collections.ObjectModel.ReadOnlyCollection<PindianRandomReceipt>,
            "Trusted diagnostics expose a frozen receipt collection.");
        var restored = Replay(game, registry);
        Advance(game); Advance(restored); Equivalent(game, restored);
        var frame = (PindianFrame)game.ResolutionStack[^1];
        var receipts = frame.RandomSelection!.Receipts;
        Require(receipts.Count == 2 && receipts[0] == receipt && receipts[1] is { OwnerSeat: 1, TargetSeat: 0 } &&
            receipts[1].RandomBefore == receipt.RandomAfter && frame.SourceCardId == receipts[1].CardId &&
            frame.Result is { SourceRank: 7, OpponentRank: 7, SourceWon: false } &&
            P(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("take") == "true").Cards.Count == 2,
            "Both strategies consume RNG once in source/opponent order, and tied maximum elemental Slashes are offered together.");
        Require(game.CardMovements.Count(m => m.Reason == CardMoveReasons.PindianReveal) == 2 &&
            game.CardMovements.Where(m => m.Reason == CardMoveReasons.PindianReveal).Select(m => m.CardId)
                .Order().SequenceEqual(receipts.Select(r => r.CardId).Order()) &&
            (selected == frame.SourceCardId || game.CreateCardZoneDiagnostics().Single(c => c.CardId == selected).Location == CardLocation.Hand(0)),
            "The original selected source card stays in hand when overridden; exactly the two frozen random identities are paid once.");
        Replay(game, registry);
        var ids = P(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("take") == "true").Cards.ToArray();
        Answer(game, "pindian-claim", true);
        Require(game.ResolutionStack.Count == 0 && ids.All(id => game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == id)) &&
            game.CardMovements.Count(m => m.Reason.Value == "program.pindian.claim") == 2 &&
            !game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "Taking both tied Slashes resumes Tianyi once, including its not-won Slash prohibition.");
        Replay(game, registry);
        var (forcedTop, forcedTopRegistry) = Start("both-top");
        Accept(forcedTop, new UseProgramSkillCommand(0, Driver, "contest", [], [1], forcedTop.Revision, P(forcedTop)!.PromptId));
        Answer(forcedTop, "pindian-random", true); Advance(forcedTop);
        var forcedFrame = (PindianFrame)forcedTop.ResolutionStack[^1];
        Require(forcedFrame.RandomSelection!.Receipts.Count == 2 && !forcedFrame.SourceUsesDrawPileTop &&
            forcedFrame.Result is not null && !forcedTop.Events.Any(e => e.Payload is PindianTopSourceChosenEvent) &&
            !forcedTop.CardMovements.Any(m => m.Reason.Value == "pindian.reserve-top"),
            "A forced random source reaches reveal without offering or reserving its otherwise eligible top source.");
        Replay(forcedTop, forcedTopRegistry); Answer(forcedTop, "pindian-claim", true); Replay(forcedTop, forcedTopRegistry);
    }

    public static void DeclinedRandomPolicyDoesNotConsumeRngAndOpponentCanForceSource()
    {
        var (accepted, acceptedRegistry) = Start("both");
        UseTianyi(accepted); Answer(accepted, "pindian-random", true);
        var reference = ((PindianFrame)accepted.ResolutionStack[^1]).RandomSelection!.Receipts.Single();
        Replay(accepted, acceptedRegistry);
        var (game, registry) = Start("both");
        UseTianyi(game); Answer(game, "pindian-random", false);
        Require(((PindianFrame)game.ResolutionStack[^1]).RandomSelection!.Receipts.Count == 0 &&
            !game.Events.Any(e => e.Payload is PindianRandomHandCommittedEvent), "Declining records no sampled identity or activation.");
        Replay(game, registry);
        Advance(game);
        var frame = (PindianFrame)game.ResolutionStack[^1];
        var only = frame.RandomSelection!.Receipts.Single();
        Require(only is { OwnerSeat: 1, TargetSeat: 0 } && only.RandomBefore == reference.RandomBefore &&
            only.RandomAfter == reference.RandomAfter && frame.SourceCardId == only.CardId &&
            P(game) is { PlayerSeat: 1 } && P(game)!.Choices.All(c => c.Parameters.GetValueOrDefault("action") is "pindian-card"),
            "The opponent's strategy is reachable, overwrites the source card, and receives exactly the first RNG step after the source declines.");
        Replay(game, registry); Advance(game);
        Answer(game, "pindian-claim", false); AdvanceToPlay(game);
        Require(game.CardMovements.Count(m => m.Reason == CardMoveReasons.PindianReveal) == 2 &&
            game.CardMovements.Count(m => m.Reason.Value == "program.pindian.claim") == 2 &&
            game.Events.Count(e => e.Payload is PindianRandomHandCommittedEvent) == 1,
            "Declining the source post-claim lets the opponent obtain both tied cards without paying either identity again.");
        Replay(game, registry);
        CheckHighNonSlashAndPriorLegacyClaim();
    }

    private static void CheckHighNonSlashAndPriorLegacyClaim()
    {
        var (high, highRegistry) = Start("high-non-slash");
        EnsureHandKind(high, 0, CardKind.FireSlash); EnsureHandKind(high, 1, CardKind.Crossbow);
        var slash = high.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.FireSlash);
        Accept(high, new UseProgramSkillCommand(0, "boundary:tianyi", "contest", [slash.Id], [1], high.Revision, P(high)!.PromptId));
        Answer(high, "pindian-random", false); Advance(high);
        var highFrame = (PindianFrame)high.ResolutionStack[^1];
        Require(highFrame.Result is { SourceRank: 7, OpponentRank: 13 } &&
            high.CreateCardZoneDiagnostics().Single(c => c.CardId == highFrame.Result.OpponentCardId).CardKind == CardKind.Crossbow &&
            P(high)!.Choices.Single(c => c.Parameters.GetValueOrDefault("take") == "true").Cards.SequenceEqual([slash.Id]),
            "A higher non-Slash does not block obtaining the lower card which is the maximum within the Slash subset.");
        Replay(high, highRegistry); Answer(high, "pindian-claim", true); Replay(high, highRegistry);

        var (legacy, legacyRegistry) = Start("legacy-first");
        var selected = UseTianyi(legacy); Answer(legacy, "pindian-random", false); Advance(legacy);
        Require(P(legacy) is { SkillPrompt.SkillId: "classic:zongshi-pindian" }, "Existing participant claim precedes its new maximum-Slash claim.");
        var original = (PindianFrame)legacy.ResolutionStack[^1];
        var frozen = original.PolicyClaims!.Claims.Single(c => c.Kind == SkillProgramCardPolicyKind.PindianMaximumSlashClaim).CardIds.ToArray();
        Require(frozen.Length == 2, "Both tied maximum Slashes are frozen before any participant claim.");
        Replay(legacy, legacyRegistry); Answer(legacy, "pindian-claim", true);
        var remaining = P(legacy)!.Choices.Single(c => c.Parameters.GetValueOrDefault("take") == "true").Cards;
        Require(P(legacy) is { SkillPrompt.SkillId: "boundary:hanzhan" } && remaining.Count == 1 &&
            remaining[0] != selected && frozen.Contains(remaining[0]) &&
            ((PindianFrame)legacy.ResolutionStack[^1]).PolicyClaims!.Claims.Single(c => c.Kind == SkillProgramCardPolicyKind.PindianMaximumSlashClaim).CardIds.SequenceEqual(frozen),
            "A preceding legacy claim removes its own tied maximum from the offered cards without replacing or recomputing frozen maximum identities.");
        Replay(legacy, legacyRegistry); Answer(legacy, "pindian-claim", true);
        Require(legacy.CardMovements.Count(m => m.Reason.Value == "program.pindian.claim") == 2,
            "Legacy and maximum-Slash claims each obtain only their remaining physical identity once.");
        Replay(legacy, legacyRegistry);
    }

    private static void EnsureHandKind(GameEngine game, int seat, CardKind kind)
    {
        for (var i = 0; i < 6; i++)
        {
            if (game.CreateSnapshot(seat).Players[seat].Hand.Any(c => c.Kind == kind)) return;
            Accept(game, new UseProgramSkillCommand(0, Driver, "prepare", [], [seat], game.Revision, P(game)!.PromptId));
            AdvanceToPlay(game);
        }
        throw new InvalidOperationException("The fixed mixed deck lacks its required test card after bounded actual draws.");
    }

    public static void MaximumSlashClaimUsesEffectiveRanksAndPreservesDeferredTop()
    {
        var (game, registry) = Start("rank-top");
        var play = P(game)!;
        Accept(game, new UseProgramSkillCommand(0, Driver, "contest", [], [1], game.Revision, play.PromptId));
        Require(P(game) is { PlayerSeat: 0, SkillPrompt.SkillId: "boundary:hanzhan" }, "The source strategy precedes its Qin Mi top selection.");
        Answer(game, "pindian-random", false);
        Require(P(game)!.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-top") &&
            !game.Events.Any(e => e.Payload is PindianTopSourceChosenEvent), "Declining leaves the original top option and never reserves or reveals it early.");
        var top = P(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("action") == "pindian-top");
        Accept(game, new AnswerPromptCommand(0, P(game)!.PromptId, top.Id, game.Revision));
        Require(game.Events.Count(e => e.Payload is PindianTopSourceChosenEvent) == 1 &&
            ((PindianFrame)game.ResolutionStack[^1]).SourceUsesDrawPileTop,
            "The ordinary top choice is paid only after random policy decisions complete.");
        var restored = Replay(game, registry); Advance(game); Advance(restored); Equivalent(game, restored);
        var frame = (PindianFrame)game.ResolutionStack[^1];
        Require(frame.Result is { SourceRank: 13, OpponentRank: 7, SourceWon: true } &&
            P(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("take") == "true").Cards.SequenceEqual([frame.Result.SourceCardId]),
            "Maximum Slash comparison uses frozen effective Pindian ranks, including a suit-rank policy on a reserved top card.");
        Answer(game, "pindian-claim", true); AdvanceToPlay(game);
        Require(game.Events.Count(e => e.Payload is PindianTopSourceChosenEvent) == 1 &&
            game.CardMovements.Count(m => m.Reason.Value == "pindian.reserve-top") == 1 &&
            game.CardMovements.Count(m => m.Reason.Value == "program.pindian.claim") == 1 &&
            game.CreateCardZoneDiagnostics().All(c => c.Location != CardLocation.Processing),
            "The winning single maximum Slash is obtained once and the other revealed card is cleaned up.");
        Replay(game, registry);
    }

    private static int UseTianyi(GameEngine game)
    {
        var card = game.CreateSnapshot(0).Players[0].Hand[0];
        Accept(game, new UseProgramSkillCommand(0, "boundary:tianyi", "contest", [card.Id], [1], game.Revision, P(game)!.PromptId));
        return card.Id;
    }
    private static void Answer(GameEngine game, string action, bool take)
    {
        var p = P(game)!;
        var choice = p.Choices.Single(c => c.Parameters.GetValueOrDefault("action") == action && c.Parameters.GetValueOrDefault("take") == (take ? "true" : "false"));
        Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice.Id, game.Revision));
    }
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Advance(GameEngine game) => Accept(game, new AdvanceOneStepCommand(game.Revision));
    private static void AdvanceToPlay(GameEngine game)
    {
        for (var i = 0; i < 24; i++) { if (P(game) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) return; Advance(game); }
        throw new InvalidOperationException("The fixed Pindian fixture did not return to human play: " + JsonSerializer.Serialize(P(game)));
    }
    private static (GameEngine, ContentRegistry) Start(string mode)
    {
        var definitions = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(definitions, mode));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7001, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = "identity:classic-taishi-ci-check", UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:tsc-owner", game.Revision, P(game)!.PromptId));
        AdvanceToPlay(game); return (game, registry);
    }
    private static GameEngine Replay(GameEngine game, ContentRegistry registry)
    {
        var frames = JsonSerializer.Serialize(game.ResolutionStack);
        Require(JsonSerializer.Serialize(JsonSerializer.Deserialize<ResolutionFrame[]>(frames)) == frames, "Pindian receipts and claim drafts round trip as owned frame data.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Equivalent(game, restored);
        Require(Enumerable.Range(0, 4).All(s => game.CreateSnapshot(s).Players.Where(p => p.Seat != s).All(p => p.Hand.Count == 0)) &&
            game.CreateCardZoneDiagnostics().Select(c => c.CardId).Distinct().Count() == 80,
            "All viewers retain hand privacy and all physical entities are conserved.");
        return restored;
    }
    private static void Equivalent(GameEngine a, GameEngine b) => Require(State(a) == State(b), "Cold replay preserves all player views, exact receipts, event history and physical movements.");
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(-1, 5).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), game.CardMovements,
        Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray()
    });
    private static void Accept(GameEngine game, GameCommand command) { var result = game.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Command rejected."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(ContentRegistry definitions, string mode) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-taishi-ci", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var id in new[] { "boundary:tianyi", "boundary:hanzhan", "classic:zongshi-pindian" }) b.AddSkill(definitions.GetSkill(id));
            var rules = """
                {"schemaVersion":0,"skills":[{"id":"fixture:tsc-contest","revision":1,"activations":[
                {"id":"contest","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand",
                "usesPerTurn":null,"effects":[{"op":"startPindian","target":"owner","opponentRef":{"kind":"selectedTarget"},"resultBind":"contest","visibility":"public"}]},
                {"id":"prepare","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,
                "effects":[{"op":"draw","target":"selectedTarget","amount":4}]}]},
                {"id":"fixture:tsc-rank","revision":1,"cardPolicies":[{"id":"effective-thirteen","kind":"pindianRankBySuit","inputSuit":"club","value":13},
                {"id":"top-source","kind":"pindianTopCardChoice"}]}]}
                """;
            var presentation = """{"schemaVersion":3,"skills":{"fixture:tsc-contest":{"name":"真实拼点驱动","description":"以真实命令发起拼点。"},"fixture:tsc-rank":{"name":"既有顶牌与点数能力","description":"复用正式秦宓拼点能力。"}}}""";
            var rulesNode = System.Text.Json.Nodes.JsonNode.Parse(rules)!;
            rulesNode["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var programs = SkillProgramCatalog.Load(rulesNode.ToJsonString(), presentation).Programs;
            foreach (var id in new[] { Driver, Rank }) b.AddSkill(new(id, id, "正式能力拼点") { Program = programs[id] });
            b.AddSkill(new("fixture:tsc-select", "固定其他选将", "保留人工机制角色")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddGeneral(new("fixture:tsc-owner", "界太史慈机制", "taishi_ci", Driver, "wu", 4,
                mode.Contains("top", StringComparison.Ordinal) ? ["boundary:tianyi", "boundary:hanzhan", Rank] :
                mode == "legacy-first" ? ["boundary:tianyi", "boundary:hanzhan", "classic:zongshi-pindian"] : ["boundary:tianyi", "boundary:hanzhan"]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:tsc-other-{i}", "其他" + i, "supporter", "fixture:tsc-select", "qun", 4,
                mode.StartsWith("both", StringComparison.Ordinal) ? ["boundary:hanzhan"] : []));
            b.AddCard(new("fixture:tsc-fire-slash", "火杀", "基本牌", "同点杀实体", CardKind.FireSlash));
            b.AddCard(new("fixture:tsc-crossbow", "诸葛连弩", "装备牌", "高点非杀实体", CardKind.Crossbow));
            b.AddDeck(new("fixture:tsc-deck", "固定同点杀", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(
                mode == "high-non-slash" && i % 2 == 0 ? "fixture:tsc-crossbow" : "fixture:tsc-fire-slash", Suit.Club,
                mode == "high-non-slash" && i % 2 == 0 ? 13 : 7)).ToArray() });
            b.AddMode(new("identity:classic-taishi-ci-check", "界太史慈机制", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:tsc-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:tsc-owner", "fixture:tsc-other-1", "fixture:tsc-other-2", "fixture:tsc-other-3"]));
        }
    }
}
