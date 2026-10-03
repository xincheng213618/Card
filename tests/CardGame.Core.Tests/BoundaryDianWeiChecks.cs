using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryDianWeiChecks
{
    private const string Driver = "fixture:dw-driver";
    private const string Cost = "fixture:dw-cost";
    private const string Gain = "fixture:dw-gain";
    private const string Hp = "fixture:dw-hp";
    private const string Mode = "identity:classic-boundary-dian-wei-fixture";

    public static void RealDamageWeaponCostSharedQuotaAndActualTurnTargets()
    {
        var (g, r) = Create(extra: true); var turn = g.State.TurnNumber;
        var beforeHp = g.State.Players[0].Hp;
        Qiangxi(g, "receive-damage", 2); Play(g);
        Require(Events<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 0 && e.Amount == 1 && e.SourceLess) &&
            g.State.Players[0].Hp == beforeHp - 1 && !Events<ProgramSkillHpLostEvent>(g).Any(e => e.SkillId == "boundary:qiangxi"),
            "Receiving the official cost is actual source-less damage, with its normal damage fact rather than an HP-loss payment.");
        Require(Actions(g).All(a => !a.SelectableTargetSeats.Contains(2)), "Both branches exclude the already designated actual-turn target.");
        var unchanged = State(g);
        Require(!g.Submit(new UseProgramSkillCommand(0, "boundary:qiangxi", "receive-damage", [], [2], g.Revision, P(g)!.PromptId)).Accepted && State(g) == unchanged,
            "A repeated target rejects atomically before another damage cost or ledger commitment.");
        var weapon = g.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.QinggangSword).Id;
        Accept(g, new PlayCardCommand(0, weapon, [], g.Revision, P(g)!.PromptId)); Play(g);
        var targetHp = g.State.Players[3].Hp;
        Qiangxi(g, "discard-weapon", 3, weapon); Reach(g, p => p.SkillPrompt?.SkillId == Cost);
        var paid = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:qiangxi");
        var child = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == paid.Id);
        Require(paid.SelectedCardPayment is { MovementCommitted: true } && child.Batch.OriginSkillInstanceId == paid.SkillInstanceId &&
            child.Batch.Movements is [var actual] && actual.CardId == weapon && actual.From == CardLocation.Equipment(0) &&
            actual.To == CardLocation.DiscardPile && g.State.Players[3].Hp == targetHp,
            "The real equipment payment and its typed movement child finish before any target damage.");
        Private(g); Cold(g, r); Continue(g); Play(g);
        Require(g.State.Players[3].Hp == targetHp - 1 && !Actions(g).Any() &&
            g.CardMovements.Count(m => m.CardId == weapon && m.Reason.Value == "skill-program.boundary:qiangxi.DiscardSelected") == 1,
            "Damage and weapon branches share the same two-use actual Play quota, and pay the weapon exactly once.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Play(g);
        Require(g.State.TurnNumber == turn && Actions(g).Any() && Actions(g).All(a => !a.SelectableTargetSeats.Contains(2) && !a.SelectableTargetSeats.Contains(3)),
            "Returning from the real inserted Play phase refreshes its phase quota while preserving both actual-turn target commitments.");
        Qiangxi(g, "receive-damage", 1); Play(g);
        var committed = Events<ProgramDistinctTurnTargetCommittedEvent>(g).Where(e => e.OwnerSeat == 0 && e.TurnNumber == turn).ToArray();
        Require(committed.Length == 3 && committed.Select(e => e.TargetSeat).Distinct().Count() == 3 &&
            committed.Select(e => e.PhaseInstanceId).Distinct().Count() == 2 && committed.All(e => !string.IsNullOrWhiteSpace(e.SkillInstanceId) && e.GameplayHash.Length > 0),
            "The public history freezes the exact source instance, actual turn and distinct real phase ids.");
        Cold(g, r); EndTurn(g); Play(g);
        Require(g.State.TurnNumber > turn && Actions(g).All(a => a.SelectableTargetSeats.Contains(2)),
            "Only the next actual turn clears the different-target restriction."); Cold(g, r);
    }

    public static void SecondVictimOccurrenceCountsDamageEventsAndActualParticipants()
    {
        var (g, r) = Create();
        Use(g, "two", [1]); Play(g);
        Require(DrawCount(g) == 0, "A single two-point injury is one occurrence and does not trigger Ninge.");
        Use(g, "one", [1]); Play(g);
        Require(DrawCount(g) == 1 && Events<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 1) == 2,
            "The actual victim's second occurrence draws once even though the first dealt two points.");
        Use(g, "one", [1]); Play(g); Require(DrawCount(g) == 1, "The third injury does not repeat the second-occurrence benefit.");
        Use(g, "foreign", [2]); Play(g); Use(g, "foreign", [2]); Play(g);
        Require(DrawCount(g) == 1, "Two injuries between other participants do not treat the skill owner as their actual source.");
        Use(g, "one", [2]); Play(g); Require(DrawCount(g) == 1, "An owner's later injury cannot reuse a different source's already passed second occurrence.");
        Use(g, "incoming", [3]); Play(g); Use(g, "incoming", [3]); Play(g);
        Require(DrawCount(g) == 2 && Events<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 0 && e.SourceSeat == 3 && !e.SourceLess) == 2,
            "The owner being the actual victim qualifies independently of the damage source and without field cards to discard.");
        Cold(g, r); var old = g.State.TurnNumber; EndTurn(g); Play(g);
        Use(g, "one", [1]); Play(g); Require(DrawCount(g) == 2, "The next actual turn starts a new occurrence count.");
        Use(g, "one", [1]); Play(g); Require(g.State.TurnNumber > old && DrawCount(g) == 3,
            "The new actual-turn second occurrence draws exactly once."); Cold(g, r);
        var (nested, nr) = Create(nested: true);
        Use(nested, "one", [1]); Play(nested); Use(nested, "one", [1]); Play(nested);
        Require(Events<DamageAppliedEvent>(nested).Count(e => e.TargetSeat == 1) == 3 &&
            Events<DamageAppliedEvent>(nested).Count(e => e.TargetSeat == 1 && e.SourceSeat == 1) == 1 && DrawCount(nested) == 1,
            "An earlier actual AfterDamage candidate's nested third injury cannot rewrite the original owning window's frozen second-occurrence fact."); Cold(nested, nr);
    }

    public static void BenefitGainFieldRecoveryChildrenAndSourceCancellationColdReplay()
    {
        foreach (var invalidate in new[] { false, true })
        {
            var (g, r) = Create(armor: true, gainObserver: true, invalidate: invalidate);
            var armor = g.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.SilverLion).Id;
            Use(g, "place", [1], [armor]); Play(g);
            Require(g.State.Players[1].Equipment.Any(c => c.Id == armor), "The field card entered through real equipment placement.");
            Use(g, "one", [1]); Play(g); Use(g, "one", [1]); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:ninge");
            var applied = root.AppliedDamageBenefit!;
            var gain = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == root.Id);
            Require(applied.DrawCount == 1 && applied.TargetSeat == 1 && applied.SourceSeat == 0 &&
                applied.DamageWindowId == root.WindowContext!.ParentFrameId &&
                (gain.Batch.AwaitingProgramFrameId is null || gain.Batch.AwaitingProgramFrameId == root.Id) &&
                gain.Batch.Movements is [var drawn] && drawn.To == CardLocation.Hand(0) && drawn.Reason.Value == "program.applied-damage-benefit.draw" &&
                g.State.Players[1].Equipment.Any(c => c.Id == armor),
                "The exact applied-damage producer drains its real gain child before exposing or paying the later field discard.");
            Private(g); Cold(g, r); Continue(g);
            if (invalidate)
            {
                Until(g, () => g.ResolutionStack.OfType<DyingFrame>().Any(d => d.VictimSeat == 0)); Cold(g, r);
                Until(g, () => g.State.Status == EngineStatus.Completed);
                Require(!g.State.Players[0].IsAlive && DrawCount(g) == 1 && g.State.Players[1].Equipment.Any(c => c.Id == armor) &&
                    !g.CardMovements.Any(m => m.CardId == armor && m.Reason.Value == "skill-program.boundary:ninge.SelectAndMoveOwnedCard"),
                    "Real source death after the committed draw preserves that draw and cancels the unpaid field tail.");
                Cold(g, r); continue;
            }
            Reach(g, p => p.SkillPrompt?.SkillId == "boundary:ninge" && p.Choices.Any(c => c.Cards.Contains(armor)));
            Private(g); Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([armor])); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
            root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:ninge");
            var hp = g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single(w => w.Change.ParentFrameId == root.Id);
            Require(hp.Change.TargetSeat == 1 && hp.Change.Kind == HpChangeKind.Recovery && hp.ResumeFrameId == root.Id &&
            root.PendingMovementContinuation is { SubjectSeat: 1 } && root.AppliedDamageBenefit == applied &&
                g.ResolutionStack.OfType<DamageTriggerWindowFrame>().Any(w => w.Id == applied.DamageWindowId) &&
                Events<SilverLionRemovedRecoveryEvent>(g).Count(e => e.PlayerSeat == 1 && e.Reason.Value == "skill-program.boundary:ninge.SelectAndMoveOwnedCard") == 1,
                "Actual Silver Lion recovery pauses inside the same frozen benefit and original damage cursor.");
            Cold(g, r); Continue(g); Play(g);
            Require(DrawCount(g) == 1 && g.CardMovements.Count(m => m.CardId == armor &&
                m.Reason.Value == "skill-program.boundary:ninge.SelectAndMoveOwnedCard") == 1 &&
                Events<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 1) == 2 &&
                !g.ResolutionStack.OfType<DamageTriggerWindowFrame>().Any() && !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(),
                "All gain, equipment, recovery and movement children return once, with the original injury already applied and no repeated benefit or cost.");
            Cold(g, r);
        }
    }

    public static void FatalReceivedDamageAndNativeAiPreserveCommittedTargetHistory()
    {
        var (fatal, fr) = Create(fragile: true); Use(fatal, "lose-one"); Play(fatal);
        Require(fatal.State.Players[0].Hp == 1, "Real setup and HP-loss driver leave the living Lord at one HP.");
        var targetHp = fatal.State.Players[1].Hp;
        Qiangxi(fatal, "receive-damage", 1);
        Until(fatal, () => fatal.ResolutionStack.OfType<DyingFrame>().Any(d => d.VictimSeat == 0));
        Require(Events<ProgramDistinctTurnTargetCommittedEvent>(fatal).Single().TargetSeat == 1 &&
            Events<DamageAppliedEvent>(fatal).Single(e => e.TargetSeat == 0) is { SourceLess: true, Amount: 1 } &&
            fatal.State.Players[1].Hp == targetHp,
            "The target is committed before actual fatal damage and its real dying child; target damage has not run.");
        Cold(fatal, fr); Until(fatal, () => fatal.State.Status == EngineStatus.Completed);
        Require(!fatal.State.Players[0].IsAlive && fatal.State.Players[1].Hp == targetHp &&
            Events<ProgramDistinctTurnTargetCommittedEvent>(fatal).Length == 1 &&
            Events<DamageAppliedEvent>(fatal).Count(e => e.TargetSeat == 0) == 1,
            "A dead source cancels the unpaid target effect and retains the once-paid cost and commitment."); Cold(fatal, fr);
        var (ai, ar) = Create(native: true); var source = ai.State.Players.Single(p => p.GeneralId == "fixture:dw-owner").Seat;
        Require(ai.State.Players[source].Role == Role.Rebel, "Real native general selection gives the skill owner the visible Lord as an enemy; random automatic setup cannot establish this premise.");
        Until(ai, () => Events<ProgramDistinctTurnTargetCommittedEvent>(ai).FirstOrDefault(e => e.OwnerSeat == source) is { } first &&
            Events<TurnEndedEvent>(ai).Any(e => e.ActorSeat == source && e.TurnNumber == first.TurnNumber) || ai.State.Status == EngineStatus.Completed);
        var commitments = Events<ProgramDistinctTurnTargetCommittedEvent>(ai).Where(e => e.OwnerSeat == source).ToArray();
        Require(commitments.Length == 2 && commitments.Select(e => e.TargetSeat).Distinct().Count() == 2 &&
            commitments.Select(e => e.PhaseInstanceId).Distinct().Count() == 1 && commitments.All(e => e.OwnerSeat != e.TargetSeat),
            "Native AI uses the same legal two-branch offer and exact actual-turn target commitments without a character runner or private opponent-hand reads. " +
            JsonSerializer.Serialize(new { Source = source, Player = ai.State.Players[source], Commitments = commitments,
                Thoughts = ai.AiThoughts.Where(thought => thought.ActorSeat == source).TakeLast(6).ToArray() }));
        var nativeTurn = commitments[0].TurnNumber;
        var nativeCosts = ai.CardMovements.Where(m => m.TurnNumber == nativeTurn && m.From.OwnerSeat == source &&
            m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && m.To == CardLocation.DiscardPile &&
            m.Reason.Value == "skill-program.boundary:qiangxi.DiscardSelected").ToArray();
        var nativeDamage = Events<DamageAppliedEvent>(ai).Where(e => e.SourceSeat == source && !e.SourceLess && e.TargetSeat != source).ToArray();
        Require(commitments.All(commit => commit.BindingId == "discard-weapon" && Events<ProgramSkillResolvedEvent>(ai).Any(e =>
                e.FrameId == commit.FrameId && e.OwnerSeat == source && e.SkillId == commit.SkillId && e.ActivationId == commit.BindingId && e.Completed)) &&
            nativeCosts.Length == 2 && nativeCosts.Select(m => m.CardId).Distinct().Count() == 2 && nativeCosts.All(m => m.CardKind == CardKind.QinggangSword) &&
            nativeDamage.Length == 2 && nativeDamage.Select(e => e.TargetSeat).SequenceEqual(commitments.Select(e => e.TargetSeat)) &&
            nativeDamage.All(e => e.Amount == 1) && Events<TurnEndedEvent>(ai).Any(e => e.ActorSeat == source && e.TurnNumber == nativeTurn),
            "The native owner's first actual turn finishes both committed activations: two distinct real weapons pay once, both targets suffer their real damage, and all cost children return before TurnEnded.");
        Cold(ai, ar);
    }

    private static LegalAction[] Actions(GameEngine g) => g.GetHumanLegalActions().Where(a => a.ProgramSkillId == "boundary:qiangxi").ToArray();
    private static int DrawCount(GameEngine g) => g.CardMovements.Count(m => m.Reason.Value == "program.applied-damage-benefit.draw");
    private static T[] Events<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Qiangxi(GameEngine g, string id, int target, int? card = null) =>
        Accept(g, new UseProgramSkillCommand(0, "boundary:qiangxi", id, card is { } c ? [c] : [], [target], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, int[]? targets = null, int[]? cards = null) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void EndTurn(GameEngine g)
    { var turn = g.State.TurnNumber; Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Until(g, () => Events<TurnEndedEvent>(g).Any(e => e.TurnNumber == turn)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    { for (var i = 0; i < 240; i++) { if (P(g) is { } p && predicate(p)) return; Step(g); } throw new InvalidOperationException("Did not reach the exact fixture prompt: " + JsonSerializer.Serialize(P(g))); }
    private static void Until(GameEngine g, Func<bool> predicate)
    { for (var i = 0; i < 300; i++) { if (predicate()) return; Step(g); } throw new InvalidOperationException("Did not reach the exact real event/frame boundary."); }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real fixture command."); }
    private static void Private(GameEngine g)
    { var p = P(g)!; Require(p.IsPrivate && p.PlayerSeat == 0, "Only the actual chooser owns this private prompt."); for (var s = 1; s < 4; s++) Require(g.CreateSnapshot(s).PendingDecision is null, "Another view must not expose private choices."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)),
        "All four player views, exact owning frames, real movement facts and accepted command prefix cold-restore identically.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool extra = false, bool armor = false, bool gainObserver = false, bool invalidate = false, bool fragile = false, bool native = false, bool nested = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture(extra, armor, gainObserver, invalidate, fragile, native, nested));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = native ? 4 : 12 }, r);
        Accept(g, new StartGameCommand());
        if (native) Until(g, () => g.State.Players.All(player => player.GeneralId.StartsWith("fixture:dw-", StringComparison.Ordinal)));
        if (!native) { Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0); Accept(g, new SelectGeneralCommand(0, "fixture:dw-owner", g.Revision, P(g)!.PromptId)); Play(g); }
        return (g, r);
    }

    private sealed class Fixture(bool extra, bool armor, bool gainObserver, bool invalidate, bool fragile, bool native, bool nested) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-dian-wei", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"activations":[
              {"id":"one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
              {"id":"two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
              {"id":"foreign","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
              {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
              {"id":"lose-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]},
             {"id":"{{Cost}}","revision":1,"triggers":[{"id":"actual-payment","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.boundary:qiangxi.DiscardSelected"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
             {"id":"{{Gain}}","revision":1,"triggers":[{"id":"actual-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.applied-damage-benefit.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gained","options":[{"id":"continue"}]}{{(invalidate ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}" : "")}}]}]},
             {"id":"{{Hp}}","revision":1,"triggers":[{"id":"real-equipment-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"healed","options":[{"id":"continue"}]}]}]},
             {"id":"fixture:dw-extra","revision":1,"triggers":[{"id":"actual-extra","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
             {"id":"fixture:dw-nested","revision":1,"triggers":[{"id":"earlier-nested-injury","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","priority":100,"optional":false,"condition":{"kind":"compare","left":{"kind":"eventTargetDamageInstancesTakenThisTurn"},"operator":"equal","right":{"kind":"integerConstant","value":2} },"effects":[{"op":"damage","target":"owner","amount":1}]}]},
             {"id":"fixture:dw-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """;
            var presentations = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, Cost, Gain, Hp, "fixture:dw-extra", "fixture:dw-nested", "fixture:dw-quiet" })
                presentations[id] = id is Cost or Gain or Hp
                    ? new { name = id, description = "真实通用能力边界夹具", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                    : (object)new { name = id, description = "真实通用能力边界夹具" };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = presentations }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实通用边界夹具") { Program = catalog.Programs[id], Tags = id == "fixture:dw-quiet" ? SkillTag.Locked : SkillTag.None });
            b.AddSkill(new("fixture:dw-pick-owner", "固定来源", "只用于公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == (native ? Role.Rebel : Role.Lord) ? 100000d : -100000d) });
            b.AddSkill(new("fixture:dw-pick-other", "固定其他", "只用于公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == (native ? Role.Rebel : Role.Lord) ? -100000d : 100000d) });
            var skills = new List<string> { "boundary:qiangxi", "boundary:ninge" };
            if (!native) skills.AddRange([Driver, Cost]); if (gainObserver) skills.Add(Gain); if (extra) skills.Add("fixture:dw-extra");
            b.AddGeneral(new("fixture:dw-owner", "真实界典韦机制", "supporter", "fixture:dw-pick-owner", "wei", fragile ? 4 : 8, skills.ToArray()) { InitialHp = fragile ? 1 : null });
            for (var i = 1; i < 4; i++)
            {
                var otherSkills = new List<string> { "fixture:dw-quiet" };
                if (gainObserver && i == 1) otherSkills.Add(Hp);
                if (nested && i == 1) otherSkills.Add("fixture:dw-nested");
                b.AddGeneral(new($"fixture:dw-other-{i}", "固定其他角色", "supporter", "fixture:dw-pick-other", "wei", 12, otherSkills.ToArray()));
            }
            b.AddDeck(new("fixture:dw-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard(armor ? "classic:silver-lion" : "standard:qinggang_sword", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "界典韦真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:dw-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:dw-owner", "fixture:dw-other-1", "fixture:dw-other-2", "fixture:dw-other-3"]));
        }
    }
}
