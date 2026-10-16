using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryJianYongChecks
{
    private const string Skill = "boundary:qiaoshui-current";
    private const string Driver = "fixture:jy-driver";
    private const string Multi = "fixture:jy-multi";
    private const string Observer = "fixture:jy-committed";
    private const string Mode = "identity:classic-boundary-jian-yong-fixture";

    public static void NextActualUsePaidWinsRefreshSingleTargetTokenAndClaimOriginalCards()
    {
        var (game, registry) = Create(); ReachPlay(game);
        var first = Contest(game, registry, 1, take: true);
        var second = Contest(game, registry, 3, take: true);
        var grants = Grants(game);
        Require(first.SourceWon && second.SourceWon && grants.Length == 2 && grants[0].ProgramFrameId != grants[1].ProgramFrameId,
            "Two real paid wins issue distinct latest receipts without stacking target allowance.");
        var actions = game.GetHumanLegalActions().Where(a => a.Kind == LegalActionKind.Slash).ToArray();
        Require(actions.Any(a => a.TargetSeats.SequenceEqual(new[] { 1, 3 })) && actions.All(a => a.TargetSeats.Count <= 2),
            "Repeated unconsumed successes permit exactly one extra legal target.");
        var hp = game.CreateSnapshot(0).Players.Select(p => p.Hp).ToArray();
        var action = actions.First(a => a.TargetSeats.SequenceEqual(new[] { 1, 3 }));
        var entity = action.CardId!.Value;
        Play(game, action); ReachObserver(game); RejectUnpublished(game); Cold(game, registry); Continue(game); ReachPlay(game);
        var consumed = Consumed(game).Single();
        Require(consumed.GrantProgramFrameId == grants[1].ProgramFrameId && !consumed.IsNullificationUse && consumed.EffectiveKind == CardKind.Slash &&
            game.CreateSnapshot(0).Players[1].Hp == hp[1] - 1 && game.CreateSnapshot(0).Players[3].Hp == hp[3] - 1 &&
            PayCount(game, entity) == 1 && CleanupCount(game, entity) == 1 &&
            !game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "The latest token is consumed by one real use, each target resolves, the original entity pays and cleans once, and Slash quota remains one.");
        Contest(game, registry, 1, take: false);
        EndPlay(game);
        Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard && game.CreateSnapshot(0).TurnNumber > 1);
        Require(!game.GetHumanLegalActions().Any(a => a.ProgramActivationId == "next-actual-use-target-adjustment"),
            "An unused prior-turn receipt cannot create adjusted actions on the owner's next actual turn.");
        Cold(game, registry);
    }

    private static PindianResult Contest(GameEngine game, ContentRegistry registry, int opponent, bool take)
    {
        var card = game.CreateSnapshot(0).Players[0].Hand[0].Id;
        var prior = game.Events.Select(e => e.Payload).OfType<PindianResultDeterminedEvent>().Count();
        Accept(game, new UseProgramSkillCommand(0, Skill, "contest", [card], [opponent], game.Revision, P(game)!.PromptId));
        Require(game.ResolutionStack.OfType<PindianFrame>().Any() && game.CreateSnapshot(2).PendingDecision is null &&
            game.CreateSnapshot(2).PublicRevealedCards.Count == 0,
            "Uncommitted real contest choices stay private until both actual participants commit.");
        Cold(game, registry);
        Reach(game, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-claim"));
        RejectUnpublished(game); Cold(game, registry);
        var result = game.Events.Select(e => e.Payload).OfType<PindianResultDeterminedEvent>().Skip(prior).Single().Result;
        Require(result.SourceCardId == card && game.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(0) &&
            m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal) == 1,
            "The chosen source hand entity is actually paid once before the optional original-card claim.");
        Answer(game, c => c.Parameters.GetValueOrDefault("action") == "pindian-claim" && c.Parameters.GetValueOrDefault("take") == (take ? "true" : "false"));
        ReachPlay(game);
        var expected = result.SourceWon ? result.OpponentCardId : result.SourceCardId;
        if (take) Require(game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == expected) &&
            game.CardMovements.Count(m => m.CardId == expected && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0) && m.Reason.Value == "program.pindian.claim") == 1,
            "Zongshi obtains the original correct contest entity rather than a reconstructed card.");
        Cold(game, registry); return result;
    }
    private static NextActualUseTargetAdjustmentGrantedEvent[] Grants(GameEngine game) => game.Events.Select(e => e.Payload).OfType<NextActualUseTargetAdjustmentGrantedEvent>().ToArray();
    private static NextActualUseTargetAdjustmentConsumedEvent[] Consumed(GameEngine game) => game.Events.Select(e => e.Payload).OfType<NextActualUseTargetAdjustmentConsumedEvent>().ToArray();
    private static int PayCount(GameEngine game, int entity) => game.CardMovements.Count(m => m.CardId == entity && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use);
    private static int CleanupCount(GameEngine game, int entity) => game.CardMovements.Count(m => m.CardId == entity && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile);
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void ReachPlay(GameEngine game) => Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void ReachObserver(GameEngine game) => Reach(game, p => p.SkillPrompt?.SkillId == Observer);
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void EndPlay(GameEngine game) => Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
    private static void Use(GameEngine game, string activation, IReadOnlyList<int>? targets = null) => Accept(game,
        new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], game.Revision, P(game)!.PromptId));
    private static void Play(GameEngine game, LegalAction action) => Accept(game, new PlayCardCommand(0, action.CardId!.Value,
        action.TargetSeats, game.Revision, P(game)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var p = P(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, game.Revision)); }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 160; step++) { var p = P(game); if (p is not null && predicate(p)) return; Advance(game); }
        throw new InvalidOperationException("Fixed Jian Yong fixture did not reach its actual boundary: " + JsonSerializer.Serialize(P(game)));
    }
    private static void Advance(GameEngine game)
    {
        var p = P(game);
        if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId == Observer) Continue(game);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
            Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass"))
            Answer(game, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)),
        "Four private snapshots, owning material/target children, frozen event payloads, actual payments and return cursors cold-restore exactly.");
    private static void RejectUnpublished(GameEngine game)
    { var p = P(game)!; var before = State(game); Require(!game.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), game.Revision)).Accepted && before == State(game), "Unpublished input changes no costs, claims, private view or issued source."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool win = true, bool counterDeck = false, bool native = false, bool spearDeck = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(win, counterDeck, native, spearDeck));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode,
            HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = native ? 2 : 8 }, registry);
        Accept(game, new StartGameCommand());
        if (!native) { Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral); Accept(game, new SelectGeneralCommand(0, "fixture:jy-owner", game.Revision, P(game)!.PromptId)); }
        return (game, registry);
    }

    private sealed class Fixture(bool win, bool counterDeck, bool native, bool spearDeck) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-jian-yong", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = FixtureRules.Replace("$SCHEMA$", SkillProgramCatalog.RulesSchemaVersion.ToString());
            var presentations = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, Multi, Observer, "fixture:jy-quiet" }) presentations[id] = id == Observer
                ? new { name = id, description = "真实付款后的目标子链", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                : (object)new { name = id, description = "固定实体和实际程序夹具" };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = presentations }));
            foreach (var id in catalog.Programs.Keys) builder.AddSkill(new(id, id, "真实夹具能力") { Program = catalog.Programs[id] });
            builder.AddSkill(new("fixture:jy-pick-owner", "固定原生主公", "只用公开身份权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            builder.AddSkill(new("fixture:jy-pick-other", "固定其他", "只用公开身份权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100000d) });
            var owner = new List<string> { "fixture:jy-pick-owner", "classic:zongshi-pindian" };
            if (win) owner.Add("classic:tianbian");
            if (!native) owner.AddRange([Driver, Multi, Observer, "classic:qice"]);
            builder.AddGeneral(new("fixture:jy-owner", "界简雍真实机制", "supporter", Skill, "shu", 8, owner.ToArray()));
            for (var i = 1; i < 4; i++) builder.AddGeneral(new($"fixture:jy-other-{i}", "固定其他角色", "supporter", "fixture:jy-pick-other", "shu", 12, ["fixture:jy-quiet"]));
            builder.AddDeck(new("fixture:jy-deck", "固定真实心牌实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80)
                .Select(_ => new ContentDeckPhysicalCard(spearDeck ? "classic:zhangba-serpent-spear" : counterDeck ? "standard:nullification" : "standard:slash", Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(Mode, "真实简雍付款", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:jy-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:jy-owner", "fixture:jy-other-1", "fixture:jy-other-2", "fixture:jy-other-3"]));
        }
    }
    private const string FixtureRules = """
    {"schemaVersion":$SCHEMA$,"skills":[
     {"id":"fixture:jy-driver","revision":1,"activations":[
      {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
      {"id":"opponent-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
      {"id":"lose-qiaoshui","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["boundary:qiaoshui-current"],"sourceBind":"fixture:jy-pick-owner"}]}]},
     {"id":"fixture:jy-quiet","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
     {"id":"fixture:jy-committed","revision":1,"triggers":[{"id":"committed","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash","arrowBarrage","peach","drawTwo"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]},{"id":"completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["slash","arrowBarrage","peach","drawTwo"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"completed-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:jy-multi","revision":1,"viewAs":[
      {"id":"two-slash","inputKinds":["slash"],"inputSuits":[],"allowSameKind":true,"outputKind":"slash","forPlay":true,"forResponse":false,"inputCount":2,"extendedUse":true,"sourceZones":["hand","equipment"]},
      {"id":"two-arrow","inputKinds":["slash"],"inputSuits":[],"outputKind":"arrowBarrage","forPlay":true,"forResponse":false,"inputCount":2,"sameSuit":true,"extendedUse":true,"sourceZones":["hand"]},
      {"id":"two-peach","inputKinds":["slash"],"inputSuits":[],"outputKind":"peach","forPlay":true,"forResponse":false,"inputCount":2,"extendedUse":true,"sourceZones":["hand","equipment"]}],"activations":[
      {"id":"double-slash","minCards":2,"maxCards":2,"sourceZones":["hand","equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two-slash","outputKind":"slash"}]},
      {"id":"double-arrow","minCards":2,"maxCards":2,"sourceZones":["hand"],"selectedCardsSameSuit":true,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"owner","sourceBind":"two-arrow","outputKind":"arrowBarrage"}]},
      {"id":"double-peach","minCards":2,"maxCards":2,"sourceZones":["hand","equipment"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"owner","sourceBind":"two-peach","outputKind":"peach"}]}]}]}
    """;
}
