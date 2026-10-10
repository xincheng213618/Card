using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryXiaoQiaoChecks
{
    private const string Hongyan = "boundary:hongyan", Tianxiang = "boundary:tianxiang", Piaoling = "boundary:piaoling";
    private const string Driver = "fixture:xq-driver", Hp = "fixture:xq-hp", Gain = "fixture:xq-gain", Pulse = "fixture:xq-pulse";
    private const string Mode = "identity:classic-xiao-qiao-fixture";

    public static void HeartEquipmentSetsExactMaxHpAfterOrdinaryHandLimitAdds()
    {
        foreach (var equip in new[] { true, false })
        {
            var (g, r) = Create(equipment: true, observers: false);
            if (equip) Equip(g);
            Use(g, "grow"); Play(g); Use(g, "limit"); Play(g);
            var expected = equip ? g.State.Players[0].MaxHp : g.State.Players[0].Hp + 4; var turn = g.State.TurnNumber;
            Require(equip ? g.CreateSnapshot(0).Players[0].Equipment.Single().Suit == Suit.Spade : g.State.Players[0].Equipment.Count == 0,
                "The actual equipment entity remains printed Spade; the effective-Heart policy is verified by the exact discard count, and the comparison has no equipment.");
            End(g); Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.DiscardCards);
            Require(P(g)!.RequiredCardCount == g.State.Players[0].HandCount - expected,
                "Heart equipment sets exactly MaxHP after an actual +4 hand-limit grant; without that equipment the original additive calculation remains.");
            g = Cold(g, r); Reject(g);
            Accept(g, new DiscardCardsCommand(0, P(g)!.ValidCardIds.Take(P(g)!.RequiredCardCount).ToArray(), P(g)!.PromptId, g.Revision));
            Until(g, () => Facts<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0 && e.TurnNumber == turn));
            Require(g.State.Players[0].HandCount == expected, "The true discard command pays the exact calculated excess, not a numerical approximation to the policy.");
        }
    }

    public static void TianxiangEquipmentPaymentWaitsForRecoveryBeforeNewDamageAndCappedDraw()
    {
        var (g, r) = Create(equipment: true); var cost = Equip(g);
        var recipient = Peer(g); var source = Peer(g, recipient); var hp = g.State.Players[0].Hp;
        Incoming(g, source); Activate(g, Tianxiang); ReachAction(g, "suit-prevention-benefit");
        Private(g, 0); g = Cold(g, r); Reject(g);
        Answer(g, c => c.Cards.SequenceEqual([cost]));
        Require(g.CreateCardZoneDiagnostics().Single(c => c.CardId == cost).Location == CardLocation.Equipment(0) &&
            !Facts<ProgramSuitPreventionPaymentEvent>(g).Any(), "The private card choice freezes the exact equipment without prematurely paying it.");
        g = Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([recipient]) && c.Parameters.GetValueOrDefault("branch") == "damage-draw");
        Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var payment = Facts<ProgramSuitPreventionPaymentEvent>(g).Single(); var root = Benefit(g);
        Require(root.SuitPreventionBenefit is { Stage: SuitPreventionBenefitStage.CostPaid, CostEffectiveSuit: Suit.Heart } paid &&
            paid.CostCardId == cost && paid.RecipientSeat == recipient && paid.SourceSeat == source &&
            g.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Single(w => w.Id == paid.BeforeDamageFrameId).Prevented &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(w => w.ResumeFrameId == root.Id && w.Change.TargetSeat == 0) &&
            g.State.Players[0].Hp == hp + 1 && !Facts<ProgramSuitPreventionBenefitIssuedEvent>(g).Any(),
            "Actual SilverLion removal first prevents the original two-point damage and suspends the paid root in its real recovery child; fresh damage has not issued.");
        g = Cold(g, r); Reject(g); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        var draw = Facts<ProgramSuitPreventionDrawEvent>(g).Single(); root = Benefit(g);
        Require(Facts<ProgramSuitPreventionBenefitIssuedEvent>(g).Single() is { SourceSeat: var issuedSource, Amount: 1 } && issuedSource == source &&
            Facts<ProgramSuitPreventionDamageCompletedEvent>(g).Single().Applied && draw is { Requested: 5, Actual: 5 } &&
            root.SuitPreventionBenefit is { Stage: SuitPreventionBenefitStage.Drawing } &&
            Facts<DamageAppliedEvent>(g).Any(e => e.SourceSeat == source && e.TargetSeat == recipient && e.Amount == 1 && e.Nature == DamageNature.Normal) &&
            !Facts<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 0) && g.State.Players[recipient].Hp == 2,
            "The newly owned one-point Normal damage retains the original source, then freezes live lostHP capped at five and drains the real gain child before returning.");
        var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == root.Id);
        Require((movement.Batch.AwaitingProgramFrameId is null || movement.Batch.AwaitingProgramFrameId == root.Id) &&
            movement.Batch.OriginSkillInstanceId == root.SkillInstanceId && movement.Batch.Movements.All(m => m.To == CardLocation.Hand(recipient)),
            "Actual gain batches keep exact owning root and source instance, including the mature first-batch null awaiting token.");
        g = Cold(g, r); Reject(g); Continue(g); Play(g);
        Require(Facts<ProgramSuitPreventionPaymentEvent>(g).Count() == 1 && Facts<ProgramSuitPreventionDrawEvent>(g).Count() == 1 &&
            g.CardMovements.Count(m => m.CardId == cost && m.Sequence == payment.MovementSequence) == 1 &&
            !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SuitPreventionBenefit is not null),
            "Cold child returns neither repay the equipment nor reissue damage/draw, and the original prevented damage finishes normally.");
    }

    public static void TianxiangLossOwnsRealDyingThenGivesTheExactDiscardedEntity()
    {
        var (g, r) = Create(fragilePeers: true); var recipient = Peer(g); var source = Peer(g, recipient);
        Incoming(g, source); Activate(g, Tianxiang); ReachAction(g, "suit-prevention-benefit");
        var card = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        Answer(g, c => c.Cards.SequenceEqual([card])); g = Cold(g, r);
        Answer(g, c => c.Targets.SequenceEqual([recipient]) && c.Parameters.GetValueOrDefault("branch") == "lose-hp-gift");
        Reach(g, p => p.SkillPrompt?.SkillId == Pulse);
        var paid = Benefit(g); var receipt = paid.SuitPreventionBenefit!;
        Require(receipt is { Stage: SuitPreventionBenefitStage.LosingHp, Branch: SuitPreventionBenefitBranch.LoseHpAndGift, HpBefore: 1 } &&
            g.State.Players[recipient].Hp == 0 && g.ResolutionStack.OfType<DyingFrame>().Any(d => d.ParentFrameId == paid.Id && d.VictimSeat == recipient && d.ResumesProgramSkill) &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Pulse && f.WindowContext?.Window == SkillProgramTriggerWindow.SelfDyingResponse) &&
            !Facts<ProgramSuitPreventionGiftEvent>(g).Any() && g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location == CardLocation.DiscardPile,
            "One real HP payment reaches the selected recipient's precise Dying/self-response subtree before any original entity is given.");
        g = Cold(g, r); Reject(g); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        var given = Facts<ProgramSuitPreventionGiftEvent>(g).Single(); var root = Benefit(g);
        Require(given.CostCardId == card && given.RecipientSeat == recipient && !given.Unavailable && g.State.Players[recipient].Hp == 3 &&
            root.SuitPreventionBenefit is { Stage: SuitPreventionBenefitStage.Gifting } &&
            g.CardMovements.Any(m => m.Sequence == given.MovementSequence && m.CardId == card && m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(recipient)),
            "The restored real self-rescue returns, then exactly the paid discarded entity enters the same living recipient's hand before its gain observer.");
        g = Cold(g, r); Reject(g); Continue(g); Play(g);
        Require(Facts<ProgramSuitPreventionPaymentEvent>(g).Count() == 1 && Facts<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == paid.Id) == 1 &&
            Facts<ProgramSuitPreventionGiftEvent>(g).Count() == 1 && !Facts<ProgramSuitPreventionDrawEvent>(g).Any() &&
            !Facts<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 0 || e.TargetSeat == recipient),
            "This branch pays HP once, does not substitute fresh damage or draw, and never repeats the original card gift.");
    }

    public static void PiaolingOriginalHeartJudgmentTopGiftSelfCostAndNativePrevention()
    {
        foreach (var destination in new[] { "top", "other", "self" })
        {
            var (g, r) = Create(); End(g); Activate(g, Piaoling); ReachAction(g, "matched-judgment-placement");
            var root = Placement(g); var original = root.MatchedJudgmentPlacement!; var id = original.CardId;
            Require(original.EffectiveSuit == Suit.Heart && Facts<JudgmentResolvedEvent>(g).Single(e => e.ParentResolutionId == root.Id).Suit == Suit.Heart &&
                g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location == CardLocation.Processing,
                "The owner's real printed Spade judgment becomes Heart and binds its exact original entity.");
            g = Cold(g, r); Reject(g);
            var recipient = destination == "self" ? 0 : Peer(g);
            Answer(g, c => destination == "top" ? c.Parameters.GetValueOrDefault("branch") == "top" : c.Targets.SequenceEqual([recipient]));
            if (destination != "top")
            {
                Reach(g, p => p.SkillPrompt?.SkillId == Gain);
                Require(Placement(g).MatchedJudgmentPlacement is { Stage: MatchedJudgmentPlacementStage.Placed } &&
                    !Facts<ProgramMatchedJudgmentSelfDiscardedEvent>(g).Any(), "The real original-card gain child finishes before any self-cost or parent completion.");
                g = Cold(g, r); Reject(g); Continue(g);
                if (destination == "self")
                {
                    ReachAction(g, "matched-judgment-placement"); Require(P(g)!.Choices.All(c => c.Parameters.GetValueOrDefault("branch") == "self-discard"), "Self acquisition alone owes one real HE discard.");
                    g = Cold(g, r); Reject(g); Answer(g, c => c.Cards.SequenceEqual([id]));
                }
            }
            var fact = Facts<ProgramMatchedJudgmentPlacedEvent>(g).Single();
            Require(fact.CardId == id && fact.OnTop == (destination == "top") &&
                g.CardMovements.Count(m => m.CardId == id && m.Sequence == fact.MovementSequence) == 1 &&
                Facts<ProgramMatchedJudgmentSelfDiscardedEvent>(g).Count() == (destination == "self" ? 1 : 0),
                "Top/other/self each move the one original result exactly once; only self acquisition pays one subsequent real discard.");
            if (destination == "top") Require(g.CreateCardZoneDiagnostics().Where(c => c.Location == CardLocation.DrawPile).MaxBy(c => c.ZoneIndex)!.CardId == id,
                "The original judgment entity is actually the top draw-pile card.");
            if (destination == "self") Require(g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location == CardLocation.DiscardPile,
                "The self-owned result can itself pay the actual post-gain discard.");
            g = Cold(g, r);
        }
        var (native, registry) = Create(nativePeer: true, observers: false);
        var target = native.State.Players.Single(p => p.GeneralId == "fixture:xq-peer-1").Seat;
        Use(native, "range"); Play(native);
        var slash = native.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.PlayedCardKind is null or CardKind.Slash &&
            a.CardId is { } id && native.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == id && c.Kind == CardKind.Slash) && a.TargetSeats.SequenceEqual([target]));
        Accept(native, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, native.Revision, P(native)!.PromptId, slash.PlayedCardKind));
        Until(native, () => Facts<ProgramSuitPreventionPaymentEvent>(native).Any()); native = Cold(native, registry); Play(native);
        Require(Facts<ProgramSuitPreventionPaymentEvent>(native).Single().OwnerSeat == target && !native.State.Players[target].IsHuman &&
            Facts<ProgramSuitPreventionBenefitIssuedEvent>(native).Count() == 1 &&
            Facts<CardUseDeclaredEvent>(native).Any(e => e.ResolutionId == Facts<ProgramSuitPreventionPaymentEvent>(native).Single().OriginalAttackFrameId && e.SourceSeat == 0 && e.CardKind == CardKind.Slash) &&
            native.CardMovements.Count(m => m.CardId == slash.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
            !native.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SuitPreventionBenefit is not null),
            "Native AI pays a real effective-Heart card against a true material Slash, selects one public recipient/branch, and returns the exact CardUse owner with one physical cleanup and no repeated costs.");
        native = Cold(native, registry);
    }

    private static IEnumerable<T> Facts<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static ProgramSkillFrame Benefit(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SuitPreventionBenefit is not null);
    private static ProgramSkillFrame Placement(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MatchedJudgmentPlacement is not null);
    private static int Peer(GameEngine g, int? except = null) => g.State.Players.First(p => p.Seat != 0 && p.Role != Role.Lord && p.Seat != except).Seat;
    private static void Incoming(GameEngine g, int source) => Use(g, "incoming", [source]);
    private static int Equip(GameEngine g)
    {
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip); var id = a.CardId!.Value;
        Accept(g, new PlayCardCommand(0, id, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind)); Play(g); return id;
    }
    private static void Use(GameEngine g, string binding, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, binding, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Activate(GameEngine g, string skill)
    {
        Reach(g, p => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    }
    private static void ReachAction(GameEngine g, string action) => Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> done)
    { for (var i = 0; i < 160; i++) { if (P(g) is { } p && done(p)) return; Step(g); } throw new InvalidOperationException("Xiao Qiao boundary missing: " + JsonSerializer.Serialize(P(g))); }
    private static void Until(GameEngine g, Func<bool> done)
    { for (var i = 0; i < 180; i++) { if (done()) return; Step(g); } throw new InvalidOperationException("Xiao Qiao actual fact boundary missing."); }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is not null && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static void Private(GameEngine g, int owner)
    { Require(P(g) is { IsPrivate: true } p && p.PlayerSeat == owner, "The owning payer alone receives the cost prompt."); for (var s = 0; s < 4; s++) if (s != owner) Require(g.CreateSnapshot(s).PendingDecision is null && g.CreateSnapshot(s).Players[owner].Hand.Count == 0, "Other prepared views reveal neither private hand identities nor the private selection."); }
    private static void Reject(GameEngine g)
    { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("not-published"), g.Revision)).Accepted && State(g) == before, "Unpublished choices are rejected without payment, movement or state changes."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(restored), "Four views, exact owning frames, actual movements, history and real commands restore identically."); return restored; }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static (GameEngine, ContentRegistry) Create(bool equipment = false, bool observers = true, bool fragilePeers = false, bool nativePeer = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(equipment, observers, fragilePeers, nativePeer));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Renegade, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, "fixture:xq-owner", g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(bool equipment, bool observers, bool fragilePeers, bool nativePeer) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-xiao-qiao", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var labels = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, "fixture:xq-quiet", Hp, Gain, Pulse }) labels[id] = id is Driver or "fixture:xq-quiet"
                ? (object)new { name = id, description = "实际拥有帧夹具" } : new { name = id, description = "实际拥有帧夹具", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var programs = SkillProgramCatalog.Load(FixtureRules.Replace("$SCHEMA$", SkillProgramCatalog.RulesSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)), JsonSerializer.Serialize(new { schemaVersion = 3, skills = labels })).Programs;
            foreach (var (id, program) in programs) b.AddSkill(new(id, id, "实际拥有帧夹具") { Program = program, Tags = id == "fixture:xq-quiet" ? SkillTag.Locked : SkillTag.None });
            b.AddSkill(new("fixture:xq-selection", "固定既有角色", "公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100000d) });
            b.AddGeneral(new("fixture:xq-owner", "当前界小乔机制", "supporter", Driver, "wu", 3,
                observers ? [Hongyan, Tianxiang, Piaoling, Hp, Gain] : [Hongyan, Tianxiang, Piaoling]) { InitialHp = 2 });
            for (var i = 1; i < 4; i++)
            {
                var skills = new List<string> { "fixture:xq-quiet" };
                if (observers) skills.Add(Gain);
                if (fragilePeers) skills.Add(Pulse);
                if (nativePeer && i == 1) skills.AddRange([Hongyan, Tianxiang]);
                b.AddGeneral(new($"fixture:xq-peer-{i}", "固定其他角色", "supporter", "fixture:xq-selection", "wu", 8, skills) { InitialHp = fragilePeers ? 1 : 3 });
            }
            b.AddDeck(new("fixture:xq-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 100)
                .Select(i => new ContentDeckPhysicalCard(equipment ? "classic:silver-lion" : "standard:slash", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "界小乔实际命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:xq-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:xq-owner", "fixture:xq-peer-1", "fixture:xq-peer-2", "fixture:xq-peer-3"]));
        }
    }
    private const string FixtureRules = """
    {"schemaVersion":$SCHEMA$,"skills":[
     {"id":"fixture:xq-driver","revision":1,"activations":[
      {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
      {"id":"hurt-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
      {"id":"grow","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]},
      {"id":"range","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashDistanceLimit","ruleOperation":"unlimited"}]},
      {"id":"limit","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"handLimit","ruleOperation":"add","amount":4}]}]},
     {"id":"fixture:xq-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]},{"id":"quiet-discard","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["discard"]}]}]},
     {"id":"fixture:xq-hp","revision":1,"triggers":[{"id":"actual-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:xq-gain","revision":1,"triggers":[{"id":"benefit-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:tianxiang.suit-prevention-draw","skill-program.boundary:tianxiang.suit-prevention-gift","skill-program.boundary:piaoling.matched-judgment-place"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:xq-pulse","revision":1,"triggers":[{"id":"actual-self-dying-return","window":"selfDyingResponse","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"rescue-seen","options":[{"id":"continue"}]},{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true}]}]}
    ]}
    """;
}
