using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinarySpPangDeChecks
{
    private const string Skill = "ol:juesi", Binding = "discard-slash-and-challenge", Driver = "fixture:spd-driver";
    private const string Observer = "fixture:spd-observer", Entry = "fixture:spd-entry", Loss = "fixture:spd-loss";
    private const string OwnerReason = "skill-program.conditional-discard-duel.owner-cost", TargetReason = "skill-program.conditional-discard-duel.target-cost";
    private const string Mode = "identity:classic-sp-pang-de-fixture";

    public static void TwoRealCostsAndIssuedDuelTypedReturn()
    {
        var (g, r) = Create(); Prepare(g); Begin(g); Reach(g, p => Cost(p, "owner"));
        Private(g); Reject(g); g = Cold(g, r); var ownerCard = PaySlash(g);
        Reach(g, p => Cost(p, "target")); Require(P(g)!.PlayerSeat == 1, "The other character chooses its own real HE card.");
        Private(g); Reject(g); g = Cold(g, r); var targetCard = PayEquipment(g);
        Reach(g, p => p.Kind == DecisionKind.RespondSlash); g = Cold(g, r); Advance(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Entry); g = Cold(g, r); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Entry); g = Cold(g, r); Continue(g); Play(g);
        var issued = E<ConditionalDiscardDuelIssuedEvent>(g).Single().Origin;
        Require(issued.InitialActorSeat == 0 && issued.InitialTargetSeat == 1 && issued.TargetHpAtIssue >= issued.OwnerHpAtIssue &&
            E<CardActionAcceptedEvent>(g).Any(e => e.Action.Type == CardActionType.Use && e.Action.ActorSeat == 0 &&
                e.Action.ProviderSeat == 0 && e.Action.EffectiveKind == CardKind.Duel && e.Action.PhysicalCards.Count == 0) &&
            E<CardUsedEvent>(g).Any(e => e.CardId == 0 && e.CardKind == CardKind.Duel) && E<ConditionalDiscardDuelFinishedEvent>(g).Single().Issued,
            "Both real costs issue one real canonical zero-entity Duel and return once through Damage and Completed children.");
        PaidOnce(g, ownerCard, targetCard); Require(!g.CardMovements.Any(m => m.CardId == 0), "No fabricated Card0 payment or cleanup is emitted.");
        g = Cold(g, r);

        var (cancel, cr) = Create(); Prepare(cancel); Begin(cancel); Reach(cancel, p => Cost(p, "owner"));
        var before = cancel.CardMovements.Count; Answer(cancel, c => c.Parameters.GetValueOrDefault("cost-side") == "cancel"); Play(cancel);
        Require(cancel.CardMovements.Count == before && E<ConditionalDiscardDuelPaidEvent>(cancel).Length == 0 && E<ConditionalDiscardDuelIssuedEvent>(cancel).Length == 0,
            "Declining the unpaid own cost changes no real card and issues no Duel."); cancel = Cold(cancel, cr);
    }

    public static void PrintedSlashIdentityAndHpAfterPaymentChildren()
    {
        var (g, r) = Create(); Prepare(g); Begin(g); Reach(g, p => Cost(p, "owner")); var own = PaySlash(g);
        Reach(g, p => Cost(p, "target")); var theirs = PaySlash(g); Play(g);
        Require(E<ConditionalDiscardDuelIssuedEvent>(g).Length == 0 && E<ConditionalDiscardDuelFinishedEvent>(g).Single().Issued == false,
            "A frozen printed Slash/FireSlash/ThunderSlash payment prevents the conditional Duel even at equal or greater HP."); PaidOnce(g, own, theirs); g = Cold(g, r);

        var (hp, hr) = Create(targetLoss: true); Prepare(hp); Begin(hp); Reach(hp, p => Cost(p, "owner")); var ownerCard = PaySlash(hp);
        Reach(hp, p => Cost(p, "target")); var targetCard = PayEquipment(hp);
        Reach(hp, p => p.SkillPrompt?.SkillId == Observer); Require(hp.State.Players[1].Hp < hp.State.Players[0].Hp &&
            hp.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.ConditionalDiscardDuel is { Stage: ConditionalDiscardDuelStage.TargetChildren }),
            "Actual target discard children run before the HP gate, while the real payment remains on its original activation.");
        hp = Cold(hp, hr); Continue(hp); Play(hp);
        Require(E<ConditionalDiscardDuelIssuedEvent>(hp).Length == 0 && hp.CardMovements.Count(m => m.CardId == targetCard && m.Reason.Value == TargetReason) == 1,
            "A restored real HP observer completes before comparing current HP; an earlier HP comparison cannot issue this Duel."); PaidOnce(hp, ownerCard, targetCard);
    }

    public static void PaidDyingSourceLossAndIssuedSourceLoss()
    {
        var (g, r) = Create(ownerDying: true); Prepare(g); Begin(g); Reach(g, p => Cost(p, "owner")); var own = PaySlash(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Entry); Require(g.ResolutionStack.OfType<DyingFrame>().Any(d => d.VictimSeat == 0) &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.ConditionalDiscardDuel is { Stage: ConditionalDiscardDuelStage.OwnerChildren }),
            "The actual paid discard observer creates a real Dying child beneath the original owning receipt.");
        g = Cold(g, r); Continue(g); Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "peach"));
        var peach = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "peach").Cards.Single();
        Answer(g, c => c.Parameters.GetValueOrDefault("response") == "peach"); Reach(g, p => p.SkillPrompt?.SkillId == Entry); g = Cold(g, r); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Observer); g = Cold(g, r); Continue(g);
        Reach(g, p => Cost(p, "target")); PaySlash(g); Play(g);
        Require(g.CardMovements.Count(m => m.CardId == own && m.Reason.Value == OwnerReason) == 1 &&
            E<CardActionAcceptedEvent>(g).Any(e => e.Action.EffectiveKind == CardKind.Peach && e.Action.PhysicalCards.Any(c => c.CardId == peach)) &&
            E<ConditionalDiscardDuelIssuedEvent>(g).Length == 0, "Restored engine instances actually continue Peach/Completed and cost observers without repeating the own payment.");

        var (damage, dr) = Create(ownerDamageDying: true); Prepare(damage);
        Require(damage.State.Players[0].Hp == 2, "The fixed Lord starts at two HP from the fixture's explicit initial HP; no state is edited.");
        Begin(damage); Reach(damage, p => Cost(p, "owner")); var damageOwnerCard = PaySlash(damage);
        Reach(damage, p => p.SkillPrompt?.SkillId == Entry);
        var paidRoot = damage.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ConditionalDiscardDuel is { Stage: ConditionalDiscardDuelStage.OwnerChildren });
        var dying = damage.ResolutionStack.OfType<DyingFrame>().Single(d => d.VictimSeat == 0);
        var lossProgram = damage.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == dying.ParentFrameId);
        var damageWindow = damage.ResolutionStack.OfType<DamageTriggerWindowFrame>().Single(w => w.Id == lossProgram.WindowContext!.ParentFrameId);
        var damageFrame = damage.ResolutionStack.OfType<DamageFrame>().Single(f => f.Id == damageWindow.ParentFrameId);
        var damageProducer = damage.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == damageFrame.ParentFrameId);
        var entering = damage.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(w => w.Window == SkillProgramTriggerWindow.DyingEntering && w.ResumeDyingFrameId == dying.Id);
        Require(dying.Continuation == DyingContinuationKind.ProgramSkill && lossProgram.SkillId == Entry && lossProgram.TriggerId == "damage" &&
            damageWindow.TriggerWindow == SkillProgramTriggerWindow.AfterDamageApplied && damageWindow.SourceSeat == 0 && damageWindow.TargetSeat == 0 &&
            lossProgram.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied } lossContext &&
            lossContext.ParentFrameId == damageWindow.Id && lossContext.DamageFrameId == damageFrame.Id &&
            lossContext.SourceSeat == 0 && lossContext.TargetSeat == 0 && lossContext.Amount == 1 &&
            damageWindow.CandidateIndex >= 0 && damageWindow.CandidateIndex < damageWindow.Candidates.Count &&
            damageWindow.Candidates[damageWindow.CandidateIndex].ProgramId == Entry &&
            damageProducer.SkillId == Observer && damageProducer.AttackAttempt is not null &&
            damageProducer.WindowContext?.MovementBatch?.ParentFrameId == paidRoot.Id &&
            damage.ResolutionStack.OfType<ProgramSkillFrame>().Last().WindowContext?.ParentFrameId == entering.Id &&
            paidRoot.ConditionalDiscardDuel!.OwnerPayment!.CardId == damageOwnerCard && damage.State.Players[0].Hp == 0 &&
            E<DamageRequestedEvent>(damage).Count(e => e.ResolutionId == damageFrame.Id && e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1 && e.SourceCard is null) == 1 &&
            E<DamageAppliedEvent>(damage).Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1 && e.RemainingHp == 1) == 1 &&
            E<ProgramSkillHpLostEvent>(damage).Count(e => e.FrameId == lossProgram.Id && e.SkillId == Entry && e.TargetSeat == 0 && e.Amount == 1 && e.RemainingHp == 0) == 1 &&
            E<ConditionalDiscardDuelIssuedEvent>(damage).Length == 0,
            "One real owner discard causes native Damage, then a legal mandatory AfterDamageApplied HP loss; its DyingEntering child retains the exact original payment and damage window.");
        damage = Cold(damage, dr); Continue(damage);
        Reach(damage, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "peach"));
        var damagePeach = P(damage)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "peach").Cards.Single();
        Answer(damage, c => c.Parameters.GetValueOrDefault("response") == "peach"); Reach(damage, p => p.SkillPrompt?.SkillId == Entry);
        Require(damage.ResolutionStack.OfType<ProgramSkillFrame>().Last().WindowContext?.Window == SkillProgramTriggerWindow.CardUseCompleted &&
            damage.ResolutionStack.OfType<DamageTriggerWindowFrame>().Any(w => w.Id == damageWindow.Id) &&
            damage.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == paidRoot.Id && f.ConditionalDiscardDuel is { Stage: ConditionalDiscardDuelStage.OwnerChildren }),
            "The actual Peach Completed observer pauses above the original AfterDamageApplied window and paid discard root.");
        damage = Cold(damage, dr); Continue(damage); Reach(damage, p => p.SkillPrompt?.SkillId == Entry);
        Require(damage.ResolutionStack.OfType<ProgramSkillFrame>().Last().Id == lossProgram.Id &&
            damage.ResolutionStack.OfType<DyingFrame>().All(d => d.VictimSeat != 0) && damage.State.Players[0].Hp == 1,
            "The restored rescue returns to the same paid AfterDamageApplied HP-loss program before native damage completion.");
        damage = Cold(damage, dr); Continue(damage); Reach(damage, p => p.SkillPrompt?.SkillId == Observer);
        damage = Cold(damage, dr); Continue(damage); Reach(damage, p => Cost(p, "target")); var damageTargetCard = PaySlash(damage); Play(damage);
        Require(E<ConditionalDiscardDuelIssuedEvent>(damage).Length == 0 && E<ConditionalDiscardDuelFinishedEvent>(damage).Single().Issued == false &&
            E<ProgramBindingResolvedEvent>(damage).Count(e => e.FrameId == damageProducer.Id && e.Completed) == 1 &&
            E<CardActionAcceptedEvent>(damage).Count(e => e.Action.EffectiveKind == CardKind.Peach && e.Action.ActorSeat == 0 && e.Action.PhysicalCards.Any(c => c.CardId == damagePeach)) == 1 &&
            E<ProgramSkillHpLostEvent>(damage).Count(e => e.FrameId == lossProgram.Id) == 1,
            "Real rescue, native damage, both cost children and the same two-cost activation complete once without repeating HP loss, payment or an unissued Duel.");
        PaidOnce(damage, damageOwnerCard, damageTargetCard); damage = Cold(damage, dr);

        foreach (var issuedLoss in new[] { false, true })
        {
            var (loss, lr) = Create(sourceLoss: !issuedLoss, issuedSourceLoss: issuedLoss); Prepare(loss); Begin(loss); Reach(loss, p => Cost(p, "owner")); var ownerCard = PaySlash(loss);
            Reach(loss, p => Cost(p, "target")); var targetCard = PayEquipment(loss);
            Reach(loss, p => p.SkillPrompt?.SkillId == Loss && p.Choices.Any(c => c.Targets.SequenceEqual([0]))); loss = Cold(loss, lr);
            Answer(loss, c => c.Targets.SequenceEqual([0]));
            if (issuedLoss) { Reach(loss, p => p.Kind == DecisionKind.RespondSlash); Advance(loss);
                Reach(loss, p => p.SkillPrompt?.SkillId == Entry); Continue(loss); Reach(loss, p => p.SkillPrompt?.SkillId == Entry); loss = Cold(loss, lr); Continue(loss); }
            Play(loss);
            Require(E<CurrentTurnNonLockedSkillSuppressionIssuedEvent>(loss).Any(e => e.Suppression.TargetSeat == 0) &&
                E<ConditionalDiscardDuelIssuedEvent>(loss).Length == (issuedLoss ? 1 : 0) && E<ConditionalDiscardDuelFinishedEvent>(loss).Single().Issued == issuedLoss,
                "Actual source suppression drains paid children, cancels only the unissued Duel, and preserves an already issued real-use typed return."); PaidOnce(loss, ownerCard, targetCard); loss = Cold(loss, lr);
        }
    }

    public static void NativeChoiceAndStrictStandaloneComposition()
    {
        var (g, r) = Create(native: true);
        for (var i = 0; i < 500 && E<ConditionalDiscardDuelPaidEvent>(g).Length < 2 && g.State.Status != EngineStatus.Completed; i++) Accept(g, new AdvanceOneStepCommand(g.Revision));
        Require(E<ConditionalDiscardDuelPaidEvent>(g).Any(e => e.OwnerCost) && E<ConditionalDiscardDuelPaidEvent>(g).Any(e => !e.OwnerCost) &&
            g.AcceptedCommands.All(c => c is not AnswerPromptCommand), "Fixed native play uses the new public scoring and each exact chooser pays a real entity; no AI answer is manually supplied."); g = Cold(g, r);
        var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills =
            new Dictionary<string, object> { ["fixture:spd-invalid"] = new { name = "严格两费决斗", description = "新节点合同" } } });
        foreach (var activation in new[] {
            "\"minCards\":1,\"maxCards\":1,\"targetKind\":\"otherLivingInAttackRange\",\"usesPerTurn\":null",
            "\"minCards\":0,\"maxCards\":0,\"targetKind\":\"otherLiving\",\"usesPerTurn\":null",
            "\"minCards\":0,\"maxCards\":0,\"targetKind\":\"otherLivingInAttackRange\",\"usesPerTurn\":1"
        })
        {
            var rejected = false;
            try { SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:spd-invalid\",\"revision\":1,\"activations\":[{\"id\":\"bad\",\"minTargets\":1,\"maxTargets\":1," + activation + ",\"effects\":[{\"op\":\"discardSlashThenOtherCardAndUseDuel\",\"target\":\"owner\"}]}]}]}", presentation); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "The new internal two-cost capability rejects external costs, wrong target kind and artificial quotas without widening old loader behavior.");
        }
    }

    private static bool Slash(CardKind k) => k is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Cost(PendingDecision p, string side) => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("cost-side") == side);
    private static void Private(GameEngine g)
    { var p = P(g)!; Require(p.IsPrivate && p.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(c => c.Cards.Count == 1), "Only the payer sees its own real hand-card choices.");
      foreach (var s in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat)) Require(g.CreateSnapshot(s).PendingDecision is null, "Unrelated viewers never receive the private payment choice or identity."); }
    private static int PaySlash(GameEngine g)
    { var p = P(g)!; var ids = g.CreateSnapshot(p.PlayerSeat).Players[p.PlayerSeat].Hand.Where(c => Slash(c.Kind)).Select(c => c.Id).ToHashSet();
      var c = p.Choices.First(c => c.Cards is [var id] && ids.Contains(id)); Answer(g, choice => choice.Id == c.Id); return c.Cards.Single(); }
    private static int PayEquipment(GameEngine g)
    { var c = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Equipment)); Answer(g, x => x.Id == c.Id); return c.Cards.Single(); }
    private static void PaidOnce(GameEngine g, int own, int other)
    {
        foreach (var (cardId, reason) in new[] { (own, OwnerReason), (other, TargetReason) })
        {
            var payments = g.CardMovements.Where(m => m.CardId == cardId && m.Reason.Value == reason &&
                m.To == CardLocation.DiscardPile).ToArray();
            Require(payments.Length == 1, "Each original cost is really discarded once and is never reused as Duel material.");
            Require(!g.CardMovements.Any(m => m.Sequence > payments[0].Sequence && m.CardId == cardId &&
                m.To == CardLocation.Processing), "Each original cost is really discarded once and is never reused as Duel material.");
        }
    }
    private static void Begin(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, Skill, Binding, [], [1], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, int[] targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void Prepare(GameEngine g) { Play(g); Use(g, "draw", []); Play(g); Use(g, "equip", [1]); Play(g); }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(match).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> match)
    { for (var i = 0; i < 160; i++) { var p = P(g); if (p is not null && match(p)) return; Advance(g); }
      throw new InvalidOperationException("Fixed SP Pang De fixture did not reach its real boundary: " + JsonSerializer.Serialize(P(g))); }
    private static void Advance(GameEngine g)
    { var p = P(g); if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage")) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
      else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
      Require(State(g) == State(restored), "Four private player views, scalar receipt, ledger and exact child/typed return journal-restore identically."); return restored; }
    private static void Reject(GameEngine g)
    { var p = P(g)!; var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && before == State(g), "Invalid input does not change costs, history or any private view."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(bool targetLoss = false, bool ownerDying = false, bool sourceLoss = false, bool issuedSourceLoss = false, bool native = false, bool ownerDamageDying = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new Fixture(targetLoss, ownerDying, sourceLoss, issuedSourceLoss, native, ownerDamageDying));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
        Accept(g, new StartGameCommand()); if (!native) { Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral); Accept(g, new SelectGeneralCommand(0, "fixture:spd-owner", g.Revision, P(g)!.PromptId)); } return (g, r);
    }
    private sealed class Fixture(bool targetLoss, bool ownerDying, bool sourceLoss, bool issuedSourceLoss, bool native, bool ownerDamageDying) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-sp-pang-de", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Driver}}","revision":1,"activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"draw","target":"owner","amount":20}]},{"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]}]},
            {"id":"{{Observer}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{(ownerDying || ownerDamageDying ? OwnerReason : TargetReason)}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"{{(ownerDamageDying ? "damage" : "loseHp")}}","target":"owner","amount":{{(ownerDamageDying ? 1 : ownerDying ? 7 : 3)}}},{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Entry}}","revision":1,"triggers":[{"id":"entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]},{"id":"damage","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{{(ownerDamageDying ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}," : string.Empty)}}{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]},{"id":"completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["duel","peach"],"includeResponseUses":true,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Loss}}","revision":1,"triggers":[{"id":"loss",{{(issuedSourceLoss ? "\"window\":\"cardUseBeforeTargetEffects\",\"ownerRelation\":\"target\",\"cardKinds\":[\"duel\"]" : "\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\",\"equipment\"],\"movementOccurrence\":\"perOwnerBatch\",\"movementReasons\":[\"" + TargetReason + "\"]")}},"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"issueCurrentTurnNonLockedSkillSuppression","target":"selectedTarget"}]}]}]}
            """;
            var descriptions = new Dictionary<string, object> { [Driver] = new { name = "材料准备", description = "真实摸牌与装备" },
                [Observer] = new { name = "原弃牌孩子", description = "真实HP变化", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Entry] = new { name = "实际伤害救援完成", description = "真实子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Loss] = new { name = "真实来源失效", description = "成熟抑制能力" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = descriptions }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "固定真实命令能力") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id],
                Tags = id == Entry ? SkillTag.Locked : SkillTag.None });
            foreach (var lord in new[] { true, false }) b.AddSkill(new(lord ? "fixture:spd-pick-owner" : "fixture:spd-pick-other", "正式选将", "角色评分")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == lord ? 10000d : -10000d) });
            var owner = new List<string> { "fixture:spd-pick-owner", "classic:mashu" }; if (!native) owner.AddRange([Driver, Entry]); if (ownerDying || ownerDamageDying) owner.Add(Observer);
            b.AddGeneral(new("fixture:spd-owner", "真实决死来源", "supporter", Skill, "wei", 6, owner.ToArray()) { InitialHp = ownerDamageDying ? 1 : null });
            for (var i = 1; i < 4; i++) { var other = new List<string>(); if (!native) other.Add(Entry); if (i == 1 && targetLoss) other.Add(Observer); if (i == 1 && (sourceLoss || issuedSourceLoss)) other.Add(Loss);
                b.AddGeneral(new($"fixture:spd-other-{i}", "其他角色", "supporter", "fixture:spd-pick-other", "qun", 8, other.ToArray())); }
            b.AddDeck(new("fixture:spd-deck", "固定混合真实材料", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(i =>
                // Native scoring needs a real payable Slash after the ordinary once-per-turn Slash.
                new ContentDeckPhysicalCard(native ? "standard:slash" : i % 5 == 0 ? "standard:slash" : i % 5 == 1 ? "classic:silver-lion" : "standard:peach", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "决死真实两费", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:spd-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:spd-owner", "fixture:spd-other-1", "fixture:spd-other-2", "fixture:spd-other-3"]));
        }
    }
}
