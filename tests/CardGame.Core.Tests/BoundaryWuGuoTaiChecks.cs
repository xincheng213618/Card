using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryWuGuoTaiChecks
{
    private const string Ganlu = "boundary:ganlu-current", Buyi = "boundary:buyi-current";
    private const string Driver = "fixture:wgt-driver", Cost = "fixture:wgt-cost", Hp = "fixture:wgt-hp";
    private const string Mode = "identity:classic-wu-guo-tai-fixture";
    private const string GanluCost = "skill-program." + Ganlu + ".MoveBoundCards";
    private const string BuyiCost = "skill-program." + Buyi + ".MoveBoundCards";

    public static void PaidEquipmentPairFreezesCostAndSwapsAfterChildren()
    {
        var (g, r) = Create(equipment: true, observers: true); Play(g);
        Use(g, "equip", [0]); Play(g); Use(g, "equip", [1]); Play(g); Use(g, "weapon", [1]); Play(g);
        var armor = View(g, 0).Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        var hand = View(g, 0).Hand.First().Id;
        var exchanged = View(g, 1).Equipment.Select(c => c.Id).Order().ToArray();
        Require(exchanged.Length == 2 && View(g, 2).Equipment.Count == 0 && g.State.Players[0].Hp == 4,
            "Real equipment producers establish the two-card difference and full formal Lord HP.");
        Use(g, "hurt", [0]); Play(g); Require(g.State.Players[0].Hp == 3, "A real one-HP loss establishes lost HP one.");
        GanluPair(g, 1, 2); Reach(g, p => Action(p, "select-owned-cards"));
        var root = PairRoot(g); var frozen = Facts<ProgramEquipmentPairPaymentFrozenEvent>(g).Single();
        Require(root.EquipmentPairPayment is { OwnerLostHp: 1, FirstEquipmentCount: 2, SecondEquipmentCount: 0, RequiredPaymentCount: 2 } &&
            root.OwnedCardSelection is { RequiredCount: 2, MinimumCount: 2 } && frozen.FrameId == root.Id &&
            frozen.Source.SkillInstanceId == root.SkillInstanceId && frozen.GameplayHash == root.GameplayHash,
            "Selecting the actual pair freezes the full X=2 cost once; it is not the excess over lost HP.");
        var before = g.CardMovements.Count; Private(g, 0); Cold(g, r); Reject(g);
        Answer(g, c => c.Cards.SequenceEqual([armor]));
        Require(g.CardMovements.Count == before && View(g, 0).Equipment.Any(c => c.Id == armor),
            "The first private choice does not prematurely discard either entity."); Cold(g, r);
        Answer(g, c => c.Cards.SequenceEqual([hand])); Reach(g, p => p.SkillPrompt?.SkillId == Hp && p.PlayerSeat == 0);
        root = PairRoot(g); var paid = CostMoves(g, GanluCost);
        Require(root.Id == frozen.FrameId && root.PendingMovementContinuation is { SubjectSeat: 0, BeforeCount: 0, CoverageResultBind: null } &&
            paid.Length == 2 && paid.Select(m => m.CardId).Order().SequenceEqual(new[] { armor, hand }.Order()) &&
            g.State.Players[0].Hp == 4 && View(g, 1).Equipment.Select(c => c.Id).Order().SequenceEqual(exchanged) &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.ResumeFrameId == root.Id &&
                h.Change.ParentFrameId == root.Id && h.Continuation == PostEventContinuation.AwaitedProgramMovement),
            "Both exact costs commit once, then Silver Lion's real recovery child pauses before the frozen pair exchanges.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Cost && p.PlayerSeat == 0);
        var moved = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(m => m.Batch.ParentFrameId == root.Id);
        Require((moved.Batch.AwaitingProgramFrameId is null || moved.Batch.AwaitingProgramFrameId == root.Id) && moved.ResumeProgramFrameId is null &&
            moved.Batch.Movements.Select(m => m.CardId).Order().SequenceEqual(new[] { armor, hand }.Order()) &&
            View(g, 2).Equipment.Count == 0 && PairRoot(g).EquipmentPairPayment!.RequiredPaymentCount == 2,
            "The exact atomic payment movement child returns to that same issued pair despite the owner's new lost HP zero.");
        g = RestoreAfterCold(g, r); Continue(g); Play(g);
        Require(View(g, 1).Equipment.Count == 0 && View(g, 2).Equipment.Select(c => c.Id).Order().SequenceEqual(exchanged) &&
            CostMoves(g, GanluCost).Length == 2 && Facts<ProgramEquipmentPairExchangeStartedEvent>(g).Count() == 1 &&
            !g.GetHumanLegalActions().Any(a => a.SkillId == Ganlu) && !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.EquipmentPairPayment is not null),
            "Returning all real payment children swaps the original pair exactly once and retains the per-play-phase limit."); Cold(g, r);
    }

    public static void FreeAndChangedEquipmentPairsNeverRepriceCommittedPayment()
    {
        var (free, fr) = Create(); Play(free); Use(free, "weapon", [1]); Play(free); Use(free, "clear-hand"); Play(free);
        Require(View(free, 0).HandCount == 0 && View(free, 0).Equipment.Count == 0 &&
            !free.GetHumanLegalActions().Any(a => a.SkillId == Ganlu), "A full-HP owner without legal HE cannot begin a required one-card payment.");
        Use(free, "hurt", [0]); Play(free); var weapon = View(free, 1).Equipment.Single().Id;
        GanluPair(free, 1, 2); Play(free);
        Require(Facts<ProgramEquipmentPairPaymentFrozenEvent>(free).Single() is { OwnerLostHp: 1, RequiredPaymentCount: 0 } &&
            CostMoves(free, GanluCost).Length == 0 && View(free, 2).Equipment.Any(c => c.Id == weapon),
            "When X equals actual lost HP, the real empty binding costs nothing and the current equipment swaps."); Cold(free, fr);

        var (changed, cr) = Create(observers: true, changedEquipment: true); Play(changed); Use(changed, "weapon", [1]); Play(changed);
        var crossbow = View(changed, 1).Equipment.Single().Id; GanluPair(changed, 0, 1);
        Reach(changed, p => Action(p, "select-owned-cards")); var costId = P(changed)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        Cold(changed, cr); Answer(changed, c => c.Cards.SequenceEqual([costId])); Reach(changed, p => p.SkillPrompt?.SkillId == Cost);
        Require(PairRoot(changed).EquipmentPairPayment is { FirstEquipmentCount: 0, SecondEquipmentCount: 1, RequiredPaymentCount: 1 } &&
            View(changed, 0).Equipment.Count == 0, "The original pair's difference is frozen before the cost child can alter equipment.");
        Cold(changed, cr); Continue(changed); Play(changed);
        var generated = changed.CardMovements.Single(m => m.From == CardLocation.OutsideGame && m.To == CardLocation.Equipment(0) &&
            m.Reason == CardMoveReasons.EquipmentEnter).CardId;
        Require(View(changed, 0).Equipment.Any(c => c.Id == crossbow) && View(changed, 1).Equipment.Any(c => c.Id == generated) &&
            CostMoves(changed, GanluCost).Single().CardId == costId &&
            Facts<ProgramEquipmentPairPaymentFrozenEvent>(changed).Single().RequiredPaymentCount == 1 &&
            Facts<ProgramEquipmentPairExchangeStartedEvent>(changed).Count() == 1,
            "The observer's real named weapon joins the current pair exchange; the already committed X=1 is neither repriced nor paid twice."); Cold(changed, cr);
    }

    public static void DyingOwnedSelectionStaysOpaqueAndBasicHasNoPayment()
    {
        foreach (var victim in new[] { 1, 0 })
        {
            var (g, r) = Create(); Play(g); EnterDying(g, victim); ActivateBuyi(g);
            Reach(g, p => Action(p, "dying-owned-card")); var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == Buyi);
            var dying = g.ResolutionStack.OfType<DyingFrame>().Single(f => f.VictimSeat == victim);
            var entry = g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(e => e.ResumeDyingFrameId == dying.Id);
            Require(root.WindowContext?.ParentFrameId == entry.Id && entry.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry &&
                entry.OwnerSeat == victim && g.State.Players[victim].Hp == 0, "The real choice belongs to the exact self or foreign DyingEntering victim.");
            var choices = P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).ToArray();
            Require(choices.Length > 0 && (victim == 0 ? choices.All(c => c.Cards.Count == 1) :
                choices.All(c => c.Cards.Count == 0 && c.Label.Contains("手牌", StringComparison.Ordinal) && !c.Label.Contains("杀", StringComparison.Ordinal))) &&
                (victim == 0 || P(g)!.ValidCardIds.Count == 0), "Only the victim's own visible hand may publish entity IDs; foreign hand choices remain opaque slots without a Basic filter.");
            Require(victim == 0 || g.CreateSnapshot(0).PrivateRevealedCards is null or { Count: 0 },
                "No private program reveal projection discloses the foreign Basic entity before selection.");
            Private(g, 0); g = RestoreAfterCold(g, r); Reject(g); var moves = g.CardMovements.Count; var recoveries = Facts<RecoveryAppliedEvent>(g).Length;
            Answer(g, c => c.Id == choices[0].Id);
            Require(Facts<ProgramDyingCardSelectionResolvedEvent>(g).Single() is { NonBasic: false } fact && fact.DyingFrameId == dying.Id &&
                fact.VictimSeat == victim && g.CardMovements.Count == moves && Facts<RecoveryAppliedEvent>(g).Length == recoveries &&
                g.State.Players[victim].Hp == 0 && CostMoves(g, BuyiCost).Length == 0 &&
                !g.ResolutionStack.OfType<ProgramSkillFrame>().Where(f => f.SkillId == Buyi).SelectMany(f => f.CardSetBindings).Any(b => b.CardIds.Count > 0) &&
                (victim == 0 || g.CreateSnapshot(0).PrivateRevealedCards is null or { Count: 0 }),
                "A genuine Basic result binds no private entity, reveals no identity, pays nothing and grants no recovery.");
            Cold(g, r);
        }
    }

    public static void NonBasicDiscardOwnsSilverLionAndRecoveryBeforeReturn()
    {
        foreach (var fromEquipment in new[] { false, true })
        {
            var (g, r) = Create(equipment: true, observers: true); Play(g);
            if (fromEquipment) { Use(g, "equip", [1]); Play(g); }
            EnterDying(g, 1); var dying = g.ResolutionStack.OfType<DyingFrame>().Single(f => f.VictimSeat == 1);
            ActivateBuyi(g); Reach(g, p => Action(p, "dying-owned-card")); Private(g, 0); g = RestoreAfterCold(g, r); Reject(g);
            var chosen = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("source-zone") ==
                (fromEquipment ? nameof(CardZoneKind.Equipment) : nameof(CardZoneKind.Hand)));
            Require(fromEquipment ? chosen.Cards.Count == 1 : chosen.Cards.Count == 0,
                "The actual equipment is public while the actual nonbasic foreign hand remains opaque before discard.");
            Answer(g, c => c.Id == chosen.Id);
            Reach(g, p => p.SkillPrompt?.SkillId == (fromEquipment ? Hp : Cost) && p.PlayerSeat == 1);
            var root = DyingRoot(g); var receipt = root.DyingOwnedCard!; var id = receipt.PaidCardId!.Value;
            Require(receipt.DyingFrameId == dying.Id && receipt.VictimSeat == 1 && receipt.SourceLocation ==
                (fromEquipment ? CardLocation.Equipment(1) : CardLocation.Hand(1)) && receipt.NonBasic &&
                CostMoves(g, BuyiCost) is [var paid] && paid.CardId == id && paid.From == receipt.SourceLocation && paid.To == CardLocation.DiscardPile &&
                root.CardSetBindings.Single(b => b.Name == "non-basic").SelectionActorSeat == 1 &&
                Facts<ProgramDyingCardSelectionResolvedEvent>(g).Single() is { NonBasic: true, OwnerSeat: 0, VictimSeat: 1 },
                "The nonbasic identity becomes public only through one real original-zone discard, with its private binding attributed to the victim.");
            if (fromEquipment)
            {
                Require(g.State.Players[1].Hp == 1 && g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h =>
                    h.ResumeFrameId == root.Id && h.Change.ParentFrameId == root.Id && h.Continuation == PostEventContinuation.AwaitedProgramMovement),
                    "Removed Silver Lion recovers the exact victim once before the cost movement and later Buyi recovery.");
                g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Cost && p.PlayerSeat == 1);
            }
            var moved = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(m => m.Batch.ParentFrameId == root.Id);
            Require((moved.Batch.AwaitingProgramFrameId is null || moved.Batch.AwaitingProgramFrameId == root.Id) && moved.ResumeProgramFrameId is null &&
                moved.Batch.Movements is [var record] && record.CardId == id &&
                g.State.Players[1].Hp == (fromEquipment ? 1 : 0), "The paid movement child precedes the exact one-point rescue benefit.");
            g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Hp && p.PlayerSeat == 1);
            Require(g.State.Players[1].Hp == (fromEquipment ? 2 : 1) && g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h =>
                h.ResumeFrameId == root.Id && h.Change.ParentFrameId == root.Id && h.Continuation == PostEventContinuation.Program && h.Change.TargetSeat == 1) &&
                Facts<RecoveryAppliedEvent>(g).Single(e => e.SourceSeat == 0 && e.TargetSeat == 1) is { Amount: 1 },
                "Buyi really recovers one point after payment children and keeps the original Dying victim and typed program return.");
            g = RestoreAfterCold(g, r); Continue(g); Play(g);
            Require(CostMoves(g, BuyiCost).Single().CardId == id && Facts<DyingResolvedEvent>(g).Single(e => e.ResolutionId == dying.Id) is
                { VictimSeat: 1, Survived: true } && !g.ResolutionStack.OfType<DyingFrame>().Any() &&
                !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.DyingOwnedCard is not null) && g.CreateSnapshot(0).ProcessingCardCount == 0,
                "Native victim observers finish through real Advance commands, then the original dying token survives with one payment and no residual owning frame."); Cold(g, r);
        }
    }

    private static (GameEngine, ContentRegistry) Create(bool equipment = false, bool observers = false, bool changedEquipment = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(equipment, observers, changedEquipment));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:wgt-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private static void GanluPair(GameEngine g, int first, int second)
    {
        Accept(g, new UseProgramSkillCommand(0, Ganlu, "exchange-equipment", [], [], g.Revision, P(g)!.PromptId));
        Reach(g, p => Action(p, "equipment-pair-payment")); Answer(g, c => c.Targets.SequenceEqual(new[] { first, second }.Order()));
    }
    private static void EnterDying(GameEngine g, int victim)
    {
        var actualHp = g.State.Players[victim].Hp; Require(actualHp > 0, "The fixed victim is initially alive with actual formal HP.");
        for (var i = 1; i < actualHp; i++) { Use(g, "hurt", [victim]); Play(g); }
        Use(g, "hurt", [victim]); Reach(g, p => p.SkillPrompt?.SkillId == Buyi && Action(p, "activate"));
    }
    private static void ActivateBuyi(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == Buyi);
    private static ProgramSkillFrame PairRoot(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.EquipmentPairPayment is not null);
    private static ProgramSkillFrame DyingRoot(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.DyingOwnedCard is not null);
    private static PlayerSnapshot View(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static T[] Facts<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardMovementRecord[] CostMoves(GameEngine g, string reason) => g.CardMovements.Where(m => m.Reason.Value == reason).ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string id) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == id);
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g)
    {
        var p = P(g)!; Require(p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"), "The real observer has its published continue option.");
        if (p.PlayerSeat == 0) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 160; i++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); }
        throw new InvalidOperationException("Fixed Wu Guo Tai command boundary absent: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack, Events = g.Events.TakeLast(5).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) }));
    }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId is Cost or Hp) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die"))
            Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)),
        "Four prepared private views, exact typed returns, scalar facts, card ledger and real command history cold-restore identically.");
    private static GameEngine RestoreAfterCold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Require(State(g) == State(restored), "The cold-restored paid child retains all four views, owning frames, facts and exact once-payment ledger.");
        return restored;
    }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished answer cannot change private bindings or pay a cost."); }
    private static void Private(GameEngine g, int chooser)
    {
        foreach (var s in Enumerable.Range(0, 4).Where(s => s != chooser)) Require(g.CreateSnapshot(s).PendingDecision is null, "Only the actual chooser sees private entities or opaque slots.");
        var p = g.CreateSnapshot(chooser).PendingDecision!;
        Require(p.Choices is System.Collections.IList { IsReadOnly: true } && p.ValidCardIds is System.Collections.IList { IsReadOnly: true } &&
            p.Choices.All(c => c.Cards is System.Collections.IList { IsReadOnly: true } && c.Targets is System.Collections.IList { IsReadOnly: true }), "Prepared outer and nested choice collections are frozen.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool equipment, bool observers, bool changedEquipment) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-wu-guo-tai", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
                {"schemaVersion":0,"skills":[
                  {"id":"fixture:wgt-driver","revision":1,"activations":[
                    {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"}]},
                    {"id":"weapon","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"placeNamedWeapon","target":"selectedTarget","outputKind":"crossbow"}]},
                    {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                    {"id":"clear-hand","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"clear"},{"op":"moveBoundCards","target":"owner","sourceBind":"clear","destination":"discardPile"},{"op":"awaitBoundCardMovements","target":"owner"}]}]},
                  {"id":"fixture:wgt-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"fixture:wgt-cost","revision":1,"triggers":[{"id":"cost","window":"cardsMoved","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"sourceZones":["hand","equipment"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:ganlu-current.MoveBoundCards","skill-program.boundary:buyi-current.MoveBoundCards"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:wgt-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
                ]}
                """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (changedEquipment) ((JsonArray)rules["skills"]![2]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"placeNamedWeapon","target":"owner","outputKind":"qinggangSword"}"""));
            var presentation = JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实命令", description = "固定区域与真实 HP" }, ["fixture:wgt-quiet"] = new { name = "安静回合", description = "真实跳过出牌" },
                  [Cost] = Pause("真实成本"), [Hp] = Pause("真实回复") } });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), presentation);
            foreach (var id in new[] { Driver, "fixture:wgt-quiet", Cost, Hp }) b.AddSkill(new(id, id, "真实程序夹具") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:wgt-selection", "固定选将", "无运行能力") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            var ownerSkills = new List<string> { Buyi, Driver }; if (observers) { ownerSkills.Add(Cost); ownerSkills.Add(Hp); }
            b.AddGeneral(new("fixture:wgt-owner", "界吴国太机制", "supporter", Ganlu, "wu", 3, ownerSkills, Gender: GeneralGender.Female));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:wgt-target-{i}", "固定目标", "supporter", "fixture:wgt-selection", "qun", 4,
                observers ? ["fixture:wgt-quiet", Cost, Hp] : ["fixture:wgt-quiet"]));
            b.AddDeck(new("fixture:wgt-deck", "固定合法实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64)
                .Select(i => new ContentDeckPhysicalCard(equipment ? "classic:silver-lion" : "standard:slash", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "真实界吴国太", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:wgt-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:wgt-owner", "fixture:wgt-target-1", "fixture:wgt-target-2", "fixture:wgt-target-3"]));
        }
        private static object Pause(string name) => new { name, description = "真实子结算暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
