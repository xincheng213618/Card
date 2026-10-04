using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryZhangBaoChecks
{
    private const string Owner = "fixture:source-curse-owner", Driver = "fixture:source-curse-driver";
    private const string Observer = "fixture:source-curse-gain", Mode = "identity:classic-source-curse-fixture";

    public static void CurseIsThePhysicalInitialJudgmentBeforeGuidao()
    {
        var (game, registry) = Start(replaceJudgment: true);
        var curse = Deposit(game);
        Require(Enumerable.Range(0, 4).All(s => game.CreateSnapshot(s).Players[1].SourceCurses is
                [{ SourceSeat: 0, ActualDrawCount: 0, Card.Id: var id }] && id == curse.CardId),
            "Every viewer sees the same public curse card, source seat and draw count.");
        RequireFrozen(game.CreateSnapshot(1).Players[1].SourceCurses!);
        Require(!JsonSerializer.Serialize(game.CreateSnapshot(1).Players[1].SourceCurses).Contains("BenefitSkillInstanceId", StringComparison.Ordinal),
            "Public curse views do not disclose the trusted paired grant identity.");
        Require(!game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "ol:zhoufu"),
            "The actual Play-phase quota is paid once by the real transfer.");
        var before = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        var forged = game.Submit(new UseProgramSkillCommand(0, "ol:zhoufu", "place-source-curse",
            [game.CreateSnapshot(0).Players[0].Hand[0].Id], [1], game.Revision, game.PendingDecision!.PromptId));
        Require(!forged.Accepted && before == GameCheckpointJson.Serialize(game.CreateCheckpoint()),
            "A second paid activation rejects atomically.");

        var firstMovement = game.CardMovements.Last().Sequence;
        Activate(game, "judge", 1);
        Reach(game, () => game.PendingDecision?.Kind == DecisionKind.ProgramJudgmentReplacement);
        var judgment = game.ResolutionStack.OfType<JudgmentFrame>().Single();
        Require(judgment is { TargetSeat: 1, SourceSeat: 0, Reason: "fixture.source-curse-judgment" } &&
                judgment.CardId == curse.CardId && judgment.SourceCurseOrigin == curse,
            "The original judgment token retains its source/reason and names the actual issued curse.");
        var reveal = game.CardMovements.Where(m => m.Sequence > firstMovement && m.CardId == curse.CardId).ToArray();
        Require(reveal.Length == 2 && reveal[0].From == curse.Location && reveal[0].To == CardLocation.Processing &&
                reveal[1].From == CardLocation.Processing && reveal[1].To == CardLocation.Judgment(1) &&
                !game.CardMovements.Any(m => m.Sequence > firstMovement && m.From == CardLocation.DrawPile && m.To == CardLocation.Judgment(1)),
            "The physical curse passes through Processing and the initial reveal consumes no draw-pile card.");
        Cold(game, registry);
        var replacement = game.PendingDecision!.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "program-judgment-replace");
        Answer(game, replacement);
        ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == curse.CardId) &&
                Facts<SourceCurseLostEvent>(game).Count(e => e.Loss.Deposit.DepositFrameId == curse.DepositFrameId) == 1 &&
                game.CreateSnapshot(0).Players[1].SourceCurses is null,
            "Guidao claims the real old card through its normal replacement; the curse loss is recorded once.");
        var hp = game.CreateSnapshot(0).Players[1].Hp;
        EndPlay(game);
        Reach(game, () => Facts<SourceCurseHpLossIssuedEvent>(game).Any());
        Reach(game, () => !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SourceCurseReceipt is not null));
        Require(game.CreateSnapshot(0).Players[1].Hp == hp - 1 &&
                Facts<SourceCurseLossRosterIssuedEvent>(game).Single().Losses.Count == 1 &&
                Facts<ProgramSkillHpLostEvent>(game).Count(e => e.SkillId == "ol:zhoufu" && e.TargetSeat == 1) == 1,
            "The actual ended turn issues one distinct-target roster and one genuine HP loss.");
        RequireFrozen(Facts<SourceCurseLossRosterIssuedEvent>(game).Single().Losses);
        Cold(game, registry);
    }

    public static void LockedOriginalRewardSurvivesZhoufuLossAndPaidYingbingLoss()
    {
        var (game, registry) = Start(replaceJudgment: false);
        var curse = Deposit(game);
        Activate(game, "lose-zhoufu"); ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[1].SourceCurses is [{ Card.Id: var id }] && id == curse.CardId,
            "Removing the issuer's nonlocked grant does not clear its foreign issued card.");
        for (var draw = 1; draw <= 2; draw++)
        {
            Activate(game, "equip-user", 1);
            Reach(game, () => game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } &&
                game.ResolutionStack.LastOrDefault() is ProgramSkillFrame { SkillId: Observer });
            var root = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SourceCurseReceipt is
                { Stage: SourceCurseStage.DrawChildren });
            Require(root.SkillId == "ol:yingbing" && root.SourceCurseReceipt!.DrawCount == 1 &&
                    root.SourceCurseReceipt.Deposit!.ActualDrawCount == draw &&
                    root.PendingMovementContinuation is not null &&
                    game.CreateSnapshot(1).Players[1].SourceCurses is [{ ActualDrawCount: var count }] && count == draw,
                "A genuine forced equipment Use issues one real draw and freezes its paid movement child.");
            var moved = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == root.Id);
            Require(moved.ResumeProgramFrameId is null && moved.Batch.AwaitingProgramFrameId == root.Id &&
                    moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId &&
                    moved.Batch.OriginSkillInstanceId == root.SkillInstanceId && moved.Batch.Movements.All(m =>
                        m.Sequence > root.SourceCurseReceipt!.Before && m.Sequence <= root.SourceCurseReceipt.After),
                "The actual paid movement uses its unique awaited return, original producer and exact real invoice.");
            Cold(game, registry);
            Answer(game, game.PendingDecision!.Choices.Single());
            ReachPlay(game);
        }
        var rewards = Facts<SourceCurseDrawIssuedEvent>(game).ToArray();
        Require(rewards.Length == 2 && rewards.All(e => e.Deposit.DepositFrameId == curse.DepositFrameId && e.ActualDrawCount == 1) &&
                rewards.Select(e => e.ActionId).Distinct().Count() == 2 &&
                Facts<ProgramOwnerSkillsReplacedEvent>(game).Any(e => e.LostSkillIds.Contains("ol:yingbing")) &&
                game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == curse.CardId) &&
                game.CreateSnapshot(0).Players[1].SourceCurses is null &&
                Facts<SourceCurseObtainedEvent>(game).Count() == 1 &&
                game.CardMovements.Count(m => m.CardId == curse.CardId && m.From == curse.Location &&
                    m.To == CardLocation.Hand(0) && m.Reason.Value == "program.source-curse.obtain") == 1,
            "The second draw's child removes Yingbing, then the paid return still obtains the original curse exactly once.");
        Cold(game, registry);
        EndPlay(game);
        Reach(game, () => Facts<TurnEndedEvent>(game).Any());
        Require(!Facts<SourceCurseLossRosterIssuedEvent>(game).Any(),
            "A removed Zhoufu source cannot issue a new turn-end HP roster for the already obtained curse.");
    }

    public static void ActualHpDyingAndPhysicalDeathCleanupRetainTypedParents()
    {
        var (game, registry) = Start(replaceJudgment: false);
        var curse = Deposit(game);
        Activate(game, "weaken", 1); ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[1].Hp == 1, "The fixed target's real HP loss prepares an exact one-HP target.");
        Activate(game, "judge", 1); ReachPlay(game);
        EndPlay(game);
        Reach(game, () => game.ResolutionStack.OfType<DyingFrame>().Any(d => d.VictimSeat == 1 &&
            d.Continuation == DyingContinuationKind.ProgramSkill));
        var dying = game.ResolutionStack.OfType<DyingFrame>().Single(d => d.VictimSeat == 1);
        var owner = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == dying.ParentFrameId);
        Require(owner.SkillId == "ol:zhoufu" && owner.SourceCurseReceipt is
                { Stage: SourceCurseStage.LossHpChildren, LossIndex: 1, CurrentHpTarget: 1 } receipt &&
                receipt.Losses.Single().Deposit.DepositFrameId == curse.DepositFrameId &&
                game.ResolutionStack.OfType<DeferredTurnEndFrame>().Any(f => f.Id == receipt.OriginalParentId) &&
                Facts<ProgramSkillHpLostEvent>(game).Count(e => e.FrameId == owner.Id && e.TargetSeat == 1 && e.RemainingHp == 0) == 1,
            "The actual zero-HP child retains its original program parent and paid cursor under the true ended-turn frame.");
        Cold(game, registry);
        Reach(game, () => !game.CreateSnapshot(0).Players[1].IsAlive &&
            !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SourceCurseReceipt is not null));
        Require(Facts<SourceCurseHpLossIssuedEvent>(game).Count() == 1,
            "The real Dying/death return cannot issue the completed HP loss twice.");
        Cold(game, registry);

        var (targetDeath, targetRegistry) = Start(replaceJudgment: false);
        var held = Deposit(targetDeath);
        Activate(targetDeath, "kill-target", 1);
        Reach(targetDeath, () => !targetDeath.CreateSnapshot(0).Players[1].IsAlive &&
            targetDeath.CreateSnapshot(0).Players[1].SourceCurses is null &&
            !targetDeath.ResolutionStack.OfType<DeathFrame>().Any(d => d.VictimSeat == 1));
        Require(targetDeath.CreateSnapshot(0).Players[1].SourceCurses is null &&
                targetDeath.CardMovements.Count(m => m.CardId == held.CardId && m.From == held.Location &&
                    m.To == CardLocation.DiscardPile) == 1 &&
                Facts<SourceCurseLostEvent>(targetDeath).Count(e => e.Loss.Deposit.DepositFrameId == held.DepositFrameId) == 1,
            "Physical target death cleans its real foreign zone and records exactly that true card loss.");
        Cold(targetDeath, targetRegistry);

        var (issuerDeath, issuerRegistry) = Start(replaceJudgment: false);
        var retained = Deposit(issuerDeath);
        Activate(issuerDeath, "kill-owner");
        Reach(issuerDeath, () => !issuerDeath.CreateSnapshot(0).Players[0].IsAlive &&
            !issuerDeath.ResolutionStack.OfType<DeathFrame>().Any(d => d.VictimSeat == 0));
        Require(issuerDeath.CreateSnapshot(1).Players[1].SourceCurses is [{ Card.Id: var retainedId }] &&
                retainedId == retained.CardId && !Facts<SourceCurseLostEvent>(issuerDeath).Any(),
            "The issuer's actual death leaves its target's issued foreign curse in place without fabricating loss.");
        Cold(issuerDeath, issuerRegistry);
    }

    private static (GameEngine Game, ContentRegistry Registry) Start(bool replaceJudgment)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardClassicGeneralPackage(), new Fixture(replaceJudgment));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, game.PendingDecision!.PromptId));
        ReachPlay(game); return (game, registry);
    }
    private static SourceCurseDeposit Deposit(GameEngine game)
    {
        var card = game.CreateSnapshot(0).Players[0].Hand[0];
        Accept(game, new UseProgramSkillCommand(0, "ol:zhoufu", "place-source-curse", [card.Id], [1],
            game.Revision, game.PendingDecision!.PromptId));
        ReachPlay(game);
        var curse = Facts<SourceCurseDepositedEvent>(game).Single().Deposit;
        Require(curse.CardId == card.Id && curse.Source.OwnerSeat == 0 && curse.TargetSeat == 1 &&
                game.CardMovements.Count(m => m.CardId == card.Id && m.From == CardLocation.Hand(0) && m.To == curse.Location) == 1,
            "The selected physical HE card transfers once to the actual foreign target zone.");
        return curse;
    }
    private static void Activate(GameEngine game, string activation, params int[] targets) =>
        Accept(game, new UseProgramSkillCommand(0, Driver, activation, [], targets, game.Revision, game.PendingDecision!.PromptId));
    private static void EndPlay(GameEngine game) =>
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
    private static void ReachPlay(GameEngine game) => Reach(game,
        () => game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine game, Func<bool> reached)
    {
        for (var step = 0; step < 128 && !reached(); step++)
        {
            if (game.PendingDecision is { } p)
            {
                Require(p.Kind != DecisionKind.PlayCard, "The bounded driver must not play an unrelated full match.");
                var choice = p.Choices.FirstOrDefault(c => c.Cards.Count == 0 && c.Targets.Count == 0 &&
                    (c.Id.Value.Contains("skip", StringComparison.Ordinal) || c.Id.Value.Contains("pass", StringComparison.Ordinal))) ?? p.Choices.First();
                Answer(game, choice);
            }
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(reached(), "The small fixed fixture must reach its exact real command boundary.");
    }
    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var p = game.PendingDecision!;
        Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice.Id, game.Revision));
    }
    private static IEnumerable<T> Facts<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>();
    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(game.CreateSnapshot(s)) == SnapshotJson.Serialize(copy.CreateSnapshot(s))) &&
                game.CardMovements.SequenceEqual(copy.CardMovements) &&
                game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).SequenceEqual(
                    copy.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))),
            "Accepted-command cold restoration reproduces each viewer, real movements and canonical facts.");
    }
    private static void RequireFrozen<T>(IReadOnlyList<T> values)
    {
        if (values is IList<T> list && list.Count > 0)
        {
            try { list[0] = list[0]; }
            catch (NotSupportedException) { return; }
        }
        throw new InvalidOperationException("An exposed curse collection must reject index mutation.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command); Require(result.Accepted, result.Error?.Message ?? "A real fixture command must be accepted.");
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool replacement) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:source-curse", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load(Rules.Replace("$SCHEMA$", SkillProgramCatalog.RulesSchemaVersion.ToString()),
                """{"schemaVersion":3,"skills":{"fixture:source-curse-driver":{"name":"咒实体驱动","description":"真实移动与判定"},"fixture:source-curse-gain":{"name":"已付款摸牌观察","description":"第二次摸牌子流程中移除原影兵","optionLabels":{"continue":"继续"}}}}""");
            foreach (var p in catalog.Programs) b.AddSkill(new(p.Key, p.Key, p.Key) { Program = p.Value });
            b.AddGeneral(new(Owner, "咒机制拥有者", "supporter", "ol:zhoufu", "qun", 8,
                replacement ? ["ol:yingbing", Driver, Observer, "classic:guidao"] : ["ol:yingbing", Driver, Observer]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:source-curse-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "固定目标", "supporter", "standard:none", "wei", 8));
            // Every card has the same usable identity. Setup's actual Fisher-Yates
            // shuffle cannot invalidate the recipe; no seed search or copied snapshot.
            b.AddDeck(new("fixture:source-curse-deck", "固定黑色装备", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "实体咒共享机制", 4, 4, new Dictionary<string, int>
            { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:source-curse-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
    private const string Rules = """
    {"schemaVersion":$SCHEMA$,"skills":[
     {"id":"fixture:source-curse-driver","revision":1,"activations":[
      {"id":"judge","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"startJudgment","target":"selectedTarget","judgmentReason":"fixture.source-curse-judgment","resultBind":"judged","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"judged","destination":"discardPile"}]},
      {"id":"equip-user","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetHand","targetRef":{"kind":"selectedTarget"},"resultBind":"equipment","revealBeforeMove":true,"awaitMovementTriggers":true},{"op":"useBoundCardByTarget","target":"selectedTarget","sourceBind":"equipment"}]},
      {"id":"lose-zhoufu","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:zhoufu"],"sourceBind":"standard:none"}]},
      {"id":"weaken","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":7}]},
      {"id":"kill-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},
      {"id":"kill-owner","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]}
     ]},
     {"id":"fixture:source-curse-gain","revision":1,"states":[{"id":"first-draw-returned","initialValue":false,"visibility":"public","resetScope":"game","reacquirePolicy":"preserveUntilGameEnd"}],"triggers":[{"id":"paid-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.source-curse.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:yingbing"],"sourceBind":"standard:none","condition":{"kind":"booleanState","stateId":"first-draw-returned","expectedValue":true}},{"op":"setBooleanState","target":"owner","stateId":"first-draw-returned","value":true}]}]}
    ]}
    """;
}
