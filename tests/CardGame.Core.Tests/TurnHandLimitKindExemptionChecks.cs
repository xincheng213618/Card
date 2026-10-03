using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using static BoundaryLiDianChecks;

internal static class TurnHandLimitKindExemptionChecks
{
    private const string Source = "fixture:turn-kind-source";
    private const string Driver = "fixture:turn-kind-driver";
    private const string Mode = "identity:classic-turn-kind-fixture";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Grant = """{"op":"grantTurnHandLimitCardKindExemption","target":"owner","cardKinds":["slash","fireSlash","thunderSlash"]}""";

    public static void IssuedKindPolicySurvivesSourceSuppressionUntilActualTurnEnd()
    {
        var (g, registry) = Start();
        Require(g.State.Players[0].Hp == 3 && Eligible(g, 0).Count == g.State.Players[0].HandCount,
            "The small formal fixture starts with three HP and no discard exemption.");
        var observedEvents = 0;
        var observedViews = 0;
        string? firstDelivery = null;
        string? secondDelivery = null;
        g.EventCommitted += envelope =>
        {
            if (envelope.Payload is not TurnHandLimitCardKindExemptionGrantedEvent issued) return;
            observedEvents++;
            firstDelivery = JsonSerializer.Serialize(issued);
            AssertFrozen(issued.Policy.CardKinds);
        };
        g.EventCommitted += envelope =>
        {
            if (envelope.Payload is TurnHandLimitCardKindExemptionGrantedEvent issued)
                secondDelivery = JsonSerializer.Serialize(issued);
        };
        g.StateChanged += snapshot =>
        {
            if (snapshot.Players[0].TurnHandLimitCardKindExemptions is not { Count: > 0 } policies) return;
            observedViews++;
            AssertFrozen(policies);
            AssertFrozen(policies[0].CardKinds);
        };
        Use(g, Source, "grant"); Settle(g);
        var policy = g.CreateSnapshot(0).Players[0].TurnHandLimitCardKindExemptions!.Single();
        var sourceGrant = Players(g)[0].SkillGrants.Grants.Single(grant => grant.SkillId == Source);
        Require(policy.Source.SkillId == Source && policy.Source.BindingId == "grant" &&
            policy.Source.SkillInstanceId == sourceGrant.SkillInstanceId && policy.Source.OwnerSeat == 0 &&
            Eligible(g, 0).Count == 0 && Eligible(g, 1).Count == g.CreateSnapshot(1).Players[1].HandCount,
            "An exact issuing instance exempts only its owner's matching hand kinds.");
        Require(observedEvents == 1 && observedViews > 0 && firstDelivery == secondDelivery && g.ObserverFailures.Count == 0,
            "Prepared event and view collections reject mutation before later observers receive them.");
        for (var viewer = 0; viewer < 4; viewer++)
            Require(g.CreateSnapshot(viewer).Players[0].TurnHandLimitCardKindExemptions is [var shown] &&
                shown.Source == policy.Source && shown.CardKinds.SequenceEqual(policy.CardKinds) &&
                (viewer == 0 || g.CreateSnapshot(viewer).Players[0].Hand.Count == 0),
                "The public kind policy exposes no private physical hand identities to other viewers.");
        ReplayFourViews(g, registry);

        Use(g, Driver, "suppress"); Settle(g);
        Require(g.State.Players[0].Hp == 1 &&
            !HasSource(g, sourceGrant) && Eligible(g, 0).Count == 0 &&
            g.State.Players[0].TurnHandLimitCardKindExemptions?.Single().GrantSequence == policy.GrantSequence,
            "Real HP loss suppresses the issuing instance without erasing its already issued actual-turn policy.");
        ReplayFourViews(g, registry);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, g.PendingDecision!.PromptId));
        for (var step = 0; step < 40 && !Expired(g, policy.GrantSequence); step++)
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        Require(Expired(g, policy.GrantSequence) && g.State.Players[0].TurnHandLimitCardKindExemptions is null &&
            Eligible(g, 0).Count == g.State.Players[0].HandCount &&
            !g.CardMovements.Any(move => move.From == CardLocation.Hand(0) && move.Reason == CardMoveReasons.HandLimitDiscard),
            "The actual turn end expires the issued exemption once; its matching hand never paid an ordinary discard.");
        ReplayFourViews(g, registry);
    }

    public static void SignedSlashParserAndPreparedCollectionBoundaries()
    {
        var negative = Load(Source, [Activation("negative", Rule(-1))]).Activations.Single().Effects.Single();
        var positive = Load(Source, [Activation("positive", Rule(1))]).Activations.Single().Effects.Single();
        var context = new PlayerSkillContext(0, 3, 3, 4, TurnPhase.Play, IsOwnTurn: true);
        Require(ProgramCompositionAi.Estimate([negative], context).Score < 0 &&
            ProgramCompositionAi.Estimate([positive], context).Score > 0,
            "A negative additive Slash limit is a public AI cost; the existing positive benefit keeps its sign.");
        foreach (var effects in new[] { Rule(0), Rule(-21), Rule(-1).Replace("\"add\"", "\"set\""),
            Grant.Replace("\"owner\"", "\"selectedTarget\""), Grant.Replace("[\"slash\",\"fireSlash\",\"thunderSlash\"]", "[]"),
            Grant.Replace("\"fireSlash\"", "\"slash\""), Grant.Replace("\"cardKinds\"", "\"unknownKinds\"") })
        {
            var rejected = false;
            try { Load(Source, [Activation("invalid", effects)]); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Only nonzero additive signed Slash limits and owner/distinct-kind policies parse.");
        }
        var (g, registry) = Start();
        Require(g.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash),
            "The real fixture starts with an ordinary legal Slash.");
        Use(g, Driver, "negative"); Settle(g);
        Require(SlashLimit(g) == 0 && !g.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash),
            "A real -1 grant reduces the base Slash allowance to zero.");
        ReplayFourViews(g, registry);
        Use(g, Driver, "positive"); Settle(g);
        Require(SlashLimit(g) == 2 && g.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash) &&
            g.Events.Select(envelope => envelope.Payload).OfType<TurnRuleModifierGrantedEvent>()
                .Select(envelope => envelope.Modifier.Amount).SequenceEqual([-1, 2]),
            "The existing positive additive grant composes with the issued negative grant exactly once.");
        ReplayFourViews(g, registry);

        // A detached construction audit exercises commit preparation independently of the store's own freeze.
        var suppliedKinds = new List<CardKind> { CardKind.Slash };
        var input = new TurnHandLimitCardKindExemptionGrantedEvent(new(1, 1, 0, 1, 0,
            new(Source, "grant", 0, "fixture:instance"), suppliedKinds));
        var frozen = (TurnHandLimitCardKindExemptionGrantedEvent)CommittedEventProjection.Freeze(input);
        suppliedKinds[0] = CardKind.Dodge;
        Require(frozen.Policy.CardKinds.SequenceEqual([CardKind.Slash]),
            "Commit preparation detaches an externally mutable nested policy collection.");
        AssertFrozen(frozen.Policy.CardKinds);
    }

    private static string Rule(int amount) => JsonSerializer.Serialize(new
        { op = "grantTurnRuleModifier", target = "owner", ruleQuery = "slashLimit", ruleOperation = "add", amount });
    private static string Activation(string id, string effects) =>
        "{\"id\":\"" + id + "\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[" + effects + "]}";
    private static SkillProgram Load(string id, string[] activations) => SkillProgramCatalog.Load(
        "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + id + "\",\"revision\":1,\"activations\":[" + string.Join(',', activations) + "]}]}",
        JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
            { [id] = new { name = "共享回合策略", description = "真实命令夹具" } } })).Programs[id];
    private static void Use(GameEngine g, string skill, string activation) =>
        Accept(g, new UseProgramSkillCommand(0, skill, activation, [], [], g.Revision, g.PendingDecision!.PromptId));
    private static IReadOnlyList<CharacterState> Players(GameEngine g) =>
        (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", Flags)!.GetValue(g)!;
    private static IReadOnlyList<Card> Eligible(GameEngine g, int seat) =>
        (IReadOnlyList<Card>)typeof(GameEngine).GetMethod("GetDiscardEligibleHand", Flags)!.Invoke(g, [Players(g)[seat]])!;
    private static bool HasSource(GameEngine g, SkillGrant grant) =>
        (bool)typeof(GameEngine).GetMethod("HasRuntimeSkillInstance", Flags)!.Invoke(g, [Players(g)[0], grant.SkillId, grant.SkillInstanceId])!;
    private static int SlashLimit(GameEngine g) =>
        (((RuleQueryEvaluation)typeof(GameEngine).GetMethod("EvaluateSlashUseLimit", Flags)!.Invoke(g, [Players(g)[0]])!).Value as FiniteRuleQueryValue)!.Value;
    private static bool Expired(GameEngine g, long sequence) => g.Events.Select(envelope => envelope.Payload)
        .OfType<TurnCardUseEffectsExpiredEvent>().Any(envelope => envelope.GrantSequences.Contains(sequence));
    private static void AssertFrozen<T>(IReadOnlyList<T> values)
    {
        Require(values is IList<T> { IsReadOnly: true }, "The exposed collection must be read-only.");
        var rejected = false;
        try { ((IList<T>)values)[0] = values[0]; }
        catch (NotSupportedException) { rejected = true; }
        Require(rejected, "An observer cannot replace even an equal-valued nested element.");
    }
    private static (GameEngine, ContentRegistry) Start()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, pending => pending.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:turn-kind-owner", game.Revision, game.PendingDecision!.PromptId));
        Settle(game); return (game, registry);
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-turn-kind", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new(Source, "策略来源", "策略来源") { Program = Load(Source, [Activation("grant", Grant)]) });
            builder.AddSkill(new(Driver, "真实驱动", "真实驱动") { Program = Load(Driver,
                [Activation("negative", Rule(-1)), Activation("positive", Rule(2)),
                 Activation("suppress", """{"op":"loseHp","target":"owner","amount":2}""")]) });
            builder.AddSkill(new("fixture:turn-kind-suppress", "来源抑制", "HP1抑制其他技能") { SuppressionRule = new(1) });
            builder.AddSkill(new("fixture:turn-kind-selection", "固定选将", "无运行技能")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new("fixture:turn-kind-owner", "策略源", "supporter", Source, "wei", 2,
                [Driver, "fixture:turn-kind-suppress"]));
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new($"fixture:turn-kind-{seat}", "其他", "supporter",
                "fixture:turn-kind-selection", "shu", 8));
            builder.AddDeck(new("fixture:turn-kind-deck", "固定", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 48)
                .Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Heart, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "固定", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "fixture:turn-kind-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:turn-kind-owner", "fixture:turn-kind-1", "fixture:turn-kind-2", "fixture:turn-kind-3"]));
        }
    }
}
