using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryLiuBiaoChecks
{
    private const string Draw = "boundary:zishou-current", Faction = "boundary:zongshi-current";
    private const string Driver = "fixture:lb-driver", Gain = "fixture:lb-gain", Hp = "fixture:lb-hp", YieldGain = "fixture:lb-yield-gain";
    private const string Mode = "identity:classic-liu-biao-fixture";
    private const string DrawReason = "program.extra-draw-debt.draw", YieldReason = "skill-program." + Faction + ".SelectAndMoveOwnedCard";

    public static void ExtraDrawOwnsGainChildAndEndingDebtFreezesNewFactionCount()
    {
        var (g, r) = Create(observers: true);
        ActivateExtra(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        var issued = Facts<ProgramExtraDrawIssuedEvent>(g).Single();
        var producer = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ExtraDrawReceipt is not null);
        var moved = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.ResumeProgramFrameId == producer.Id);
        Require(issued.RequestedCount == 4 && issued.ActualCount == 4 && producer.InstructionIndex == 1 &&
            producer.ExtraDrawReceipt is { ActualTurnNumber: 1, TurnOwnerSeat: 0, ActualCount: 4 } cost &&
            moved.Batch.ParentFrameId == producer.Id && moved.Batch.AwaitingProgramFrameId is null &&
            moved.Batch.OriginSkillId == Draw && moved.Batch.OriginSkillInstanceId == producer.SkillInstanceId &&
            moved.Batch.Movements is [var card] && card.From == CardLocation.DrawPile && card.To == CardLocation.Hand(0) &&
            card.Reason.Value == DrawReason && card.Sequence > cost.MovementSequenceBefore && card.Sequence <= cost.MovementSequenceAfter,
            "Four real extra draws are paid once before the first per-card gain child; the exact current draw-start program owns that child.");
        Require(!g.CardMovements.Any(m => m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw),
            "The additional draw precedes the actual ordinary draw.");
        Private(g, 0); Cold(g, r); Reject(g); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        Require(g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.Change.Amount == 1) &&
            Facts<ProgramExtraDrawIssuedEvent>(g).Count() == 1 && DrawMoves(g).Length == 4,
            "The real gain-owned recovery child retains the paid draw while HP observation pauses.");
        Cold(g, r); Continue(g); Play(g);
        Require(g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw) == 2,
            "Gain and HP children finish before the two ordinary draw entities.");
        Use(g, "stock"); Play(g); Use(g, "kill", [3]); Play(g);
        Require(!g.State.Players[3].IsAlive && g.State.Winner == Winner.None,
            "A real nonterminal death removes exactly one of the four public factions.");
        PlaySlash(g, 1); Play(g);
        var actual = Facts<ActualTurnDamageCardUsedEvent>(g).Single();
        Require(actual is { ActualTurnNumber: 1, TurnOwnerSeat: 0, ActorSeat: 0, EffectiveKind: CardKind.Slash } &&
            Facts<CardUseDeclaredEvent>(g).Any(e => e.ResolutionId == actual.CardUseFrameId && e.SourceSeat == 0 && e.CardKind == CardKind.Slash),
            "A genuine owner Slash use, independently of its damage result, arms the actual-turn debt.");
        End(g); Reach(g, p => p.SkillPrompt?.SkillId == Draw && Action(p, "select-owned-cards"));
        var ending = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.TurnDrawDebtPayment is not null);
        Require(ending.TurnDrawDebtPayment is { EndingFactionCount: 3, RequiredPaymentCount: 3 } debt &&
            debt.ExtraDrawProgramFrameId == issued.FrameId && ending.OwnedCardSelection is { RequiredCount: 3 } &&
            Facts<ProgramTurnDrawDebtPaymentStartedEvent>(g).Single().ExtraDrawProgramFrameId == issued.FrameId,
            "Ending X is frozen after the real death, rather than copied from the earlier X=4 draw.");
        var before = g.CardMovements.Count; Private(g, 0); Cold(g, r); Reject(g);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
        Require(g.CardMovements.Count == before && ending.Id == g.ResolutionStack.OfType<ProgramSkillFrame>().Last().Id,
            "The first private selection freezes an identity without prematurely paying any of the three entities.");
        Cold(g, r);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
        Require(g.CardMovements.Count == before, "The second selection also precedes the atomic discard payment.");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
        ReachFact(g, () => Facts<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0 && e.TurnNumber == 1));
        Require(g.CardMovements.Count(m => m.Reason.Value == "skill-program." + Draw + ".MoveBoundCards" && m.To == CardLocation.DiscardPile) == 3 &&
            Facts<ProgramTurnDrawDebtPaymentStartedEvent>(g).Count() == 1 && DrawMoves(g).Length == 4,
            "All three ending entities are discarded once, and returning the cost does not repay the extra draw."); Cold(g, r);
    }

    public static void OrdinaryPaidSlashResponseDoesNotBecomeDamageCardUse()
    {
        var (g, r) = Create(faction: false); ActivateExtra(g); Play(g);
        Use(g, "opponent-duel", [1]); Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash);
        var response = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "slash");
        Cold(g, r); Answer(g, c => c.Id == response.Id);
        Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash); Pass(g); Play(g);
        Require(Facts<CardActionAcceptedEvent>(g).Any(e => e.Action.ActorSeat == 0 && e.Action.Type == CardActionType.Response &&
            e.Action.EffectiveKind == CardKind.Slash && e.Action.PhysicalCards.Select(c => c.CardId).SequenceEqual(response.Cards)) &&
            !Facts<ActualTurnDamageCardUsedEvent>(g).Any(e => e.ActorSeat == 0) && g.State.Players[0].Hp == 1,
            "The actual paid Duel Slash response and real resulting damage do not invent an owner damage-card Use.");
        End(g); ReachFact(g, () => Facts<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0 && e.TurnNumber == 1));
        Require(!Facts<ProgramTurnDrawDebtPaymentStartedEvent>(g).Any(),
            "Ending the turn adds no Zishou discard debt when the owner only responded with Slash."); Cold(g, r);
    }

    public static void SourceFactionQuotaOpaqueHeJAndEquipmentChildrenAreExact()
    {
        // The genuine native chooser gets slots, not the other participant's hand IDs.
        var (hidden, hr) = Create(); Play(hidden); Use(hidden, "opponent-duel", [1]);
        Reach(hidden, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash); Pass(hidden);
        Reach(hidden, p => p.PlayerSeat == 1 && p.SkillPrompt?.SkillId == Faction);
        var pending = P(hidden)!; var frozen = Facts<ProgramSourceFactionPreventionIssuedEvent>(hidden).Single();
        Require(pending.ValidCardIds.Count == 0 && pending.Choices.Count == hidden.State.Players[0].HandCount &&
            pending.Choices.All(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)) &&
            frozen is { SourceSeat: 1, TargetSeat: 0, PreventedAmount: 1 } && frozen.Source.OwnerSeat == 0,
            "Mandatory prevention consumes the actual foreign faction before publishing opaque HEJ choices to the exact source.");
        Private(hidden, 1); Cold(hidden, hr); Reject(hidden); Play(hidden);
        Require(!Facts<DamageAppliedEvent>(hidden).Any(e => e.SourceSeat == 1 && e.TargetSeat == 0) &&
            hidden.CardMovements.Count(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason.Value == YieldReason) == 1 &&
            hidden.CardMovements.Count(m => m.From == CardLocation.Processing && m.To == CardLocation.Hand(1) && m.Reason.Value == YieldReason) == 1,
            "The source's native choice transfers one actual hidden entity and prevents the whole damage once.");
        Use(hidden, "lose-faction"); Play(hidden); Use(hidden, "regain-faction"); Play(hidden);
        var hp = hidden.State.Players[0].Hp; Use(hidden, "opponent-duel", [1]);
        Reach(hidden, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash); Pass(hidden); Play(hidden);
        Require(hidden.State.Players[0].Hp == hp - 1 && Facts<ProgramSourceFactionPreventionIssuedEvent>(hidden).Count() == 1 &&
            hidden.CreateSnapshot(0).Players[0].SkillRuntimeStates!.Single(s => s.SkillId == Faction).Usages
                .Any(u => u.UsageId == frozen.UsageId && u.Scope == SkillUsageScope.Game && u.Count == 1),
            "Losing and regaining the real skill instance does not reset the already consumed source-faction game key."); Cold(hidden, hr);

        var (g, r) = Create(equipment: true, observers: true); Play(g); Use(g, "equip", [0]); Play(g);
        var silver = g.CreateSnapshot(0).Players[0].Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        Use(g, "clear-hand");
        Play(g); Require(g.State.Players[0].HandCount == 0 && g.State.Players[0].Hp == 2,
            "Real equipment placement and a real whole-hand payment leave only the wounded owner's Silver Lion.");
        Use(g, "opponent-duel", [1]); Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash); Pass(g);
        Reach(g, p => p.PlayerSeat == 1 && p.SkillPrompt?.SkillId == Faction);
        Require(P(g)!.Choices is [var only] && only.Cards.SequenceEqual([silver]),
            "The source has one genuine visible equipment entity to select."); Cold(g, r);
        Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SourceFactionPrevention is not null);
        var beforeDamage = g.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Single(f => f.Id == root.SourceFactionPrevention!.BeforeDamageFrameId);
        Require(root.InstructionIndex == 2 && root.PendingMovementContinuation is { SubjectSeat: 0, BeforeCount: 0 } && beforeDamage.Prevented &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f => f.ResumeFrameId == root.Id &&
                f.Continuation == PostEventContinuation.AwaitedProgramMovement && f.Change.ParentFrameId == root.Id) &&
            g.State.Players[0].Hp == 3 && g.CardMovements.Count(m => m.CardId == silver && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing && m.Reason.Value == YieldReason) == 1,
            "The real Silver Lion recovery pauses above the exact already-prevented damage and already-paid transfer.");
        Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == YieldGain);
        Require(g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(f => f.Batch.ParentFrameId == root.Id && f.ResumeProgramFrameId == root.Id) &&
            g.CardMovements.Count(m => m.CardId == silver && m.From == CardLocation.Processing && m.To == CardLocation.Hand(1) && m.Reason.Value == YieldReason) == 1,
            "The recipient's real gain child follows recovery and keeps the same prevention program return.");
        Private(g, 1); Cold(g, r); Play(g);
        Use(g, "another-duel", [2]); Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash); Pass(g); Play(g);
        Require(Facts<ProgramSourceFactionPreventionIssuedEvent>(g).Count() == 2 && g.State.Players[0].Hp == 3 &&
            g.CardMovements.Count(m => m.Reason.Value == YieldReason && m.To.Zone == CardZoneKind.Hand) == 1,
            "A different source faction prevents once even when HEJ is empty, without fabricating another transfer."); Cold(g, r);
    }

    public static void NativeExtraDrawAndEmptyDeckDoNotInventDrawDebt()
    {
        var (native, nr) = Create(native: true);
        ReachFact(native, () => Facts<ProgramExtraDrawIssuedEvent>(native).Any(e => e.TurnOwnerSeat == 0));
        Require(Facts<ProgramExtraDrawIssuedEvent>(native).Single() is { ActualTurnNumber: 1, RequestedCount: 4, ActualCount: 4, Source.OwnerSeat: 0 } &&
            DrawMoves(native).Length == 4, "The native Lord voluntarily issues and pays the actual four-card extra draw."); Cold(native, nr);
        var (empty, er) = Create(faction: false, emptyDeck: true); ActivateExtra(empty); Play(empty);
        Require(Facts<ProgramExtraDrawIssuedEvent>(empty).Single() is { RequestedCount: 4, ActualCount: 0 } && DrawMoves(empty).Length == 0,
            "Accepting the optional instruction with a truly exhausted physical deck records zero actual extra draws.");
        PlaySlash(empty, 1); Play(empty); End(empty);
        ReachFact(empty, () => Facts<TurnEndedEvent>(empty).Any(e => e.ActorSeat == 0 && e.TurnNumber == 1));
        Require(Facts<ActualTurnDamageCardUsedEvent>(empty).Count() == 1 && !Facts<ProgramTurnDrawDebtPaymentStartedEvent>(empty).Any(),
            "A subsequent genuine damage-card Use does not turn zero actual extra draws into an ending payment."); Cold(empty, er);
    }

    private static (GameEngine, ContentRegistry) Create(bool faction = true, bool observers = false, bool equipment = false,
        bool native = false, bool emptyDeck = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(faction, observers, equipment, native, emptyDeck));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = native ? -1 : 0,
            HumanRole = native ? null : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
        Accept(g, new StartGameCommand());
        if (!native) { Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
            Accept(g, new SelectGeneralCommand(0, "fixture:lb-owner", g.Revision, P(g)!.PromptId)); }
        return (g, r);
    }
    private static T[] Facts<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardMovementRecord[] DrawMoves(GameEngine g) => g.CardMovements.Where(m => m.Reason.Value == DrawReason).ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static void ActivateExtra(GameEngine g) { Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Draw && c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(g, c => c.Parameters.GetValueOrDefault("skill-id") == Draw && c.Parameters.GetValueOrDefault("program-action") == "activate"); }
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void PlaySlash(GameEngine g, int target) { var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.Contains(target));
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, [target], g.Revision, P(g)!.PromptId, a.PlayedCardKind)); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Pass(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) { for (var i = 0; i < 180; i++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); }
        throw new InvalidOperationException("Fixed Liu Biao boundary absent: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack, Events = g.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
    private static void ReachFact(GameEngine g, Func<bool> predicate) { for (var i = 0; i < 180; i++) { if (predicate()) return; Advance(g); } throw new InvalidOperationException("The bounded real command prefix did not settle its fact."); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId is Gain or Hp) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die"))
            Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)),
        "All four private views, exact parent returns, scalar issued facts, payment ledger and command history cold-restore identically.");
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished choice changes no receipt, hidden choice or cost."); }
    private static void Private(GameEngine g, int chooser) { foreach (var s in Enumerable.Range(0, 4).Where(s => s != chooser)) Require(g.CreateSnapshot(s).PendingDecision is null, "The private participant's choices have no public projection.");
        var p = g.CreateSnapshot(chooser).PendingDecision!; Require(p.Choices is System.Collections.IList { IsReadOnly: true } && p.ValidCardIds is System.Collections.IList { IsReadOnly: true }, "Prepared decision collections are frozen."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool faction, bool observers, bool equipment, bool native, bool emptyDeck) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-liu-biao", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
                {"schemaVersion":0,"skills":[
                {"id":"fixture:lb-driver","revision":1,"activations":[
                  {"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20},{"op":"draw","target":"owner","amount":12}]},
                  {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},
                  {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]},
                  {"id":"clear-hand","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"hand"},{"op":"moveBoundCards","target":"owner","sourceBind":"hand","destination":"discardPile"},{"op":"awaitBoundCardMovements","target":"owner"}]},
                  {"id":"opponent-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                  {"id":"another-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                  {"id":"lose-faction","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["boundary:zongshi-current"],"sourceBind":"fixture:lb-selection"}]},
                  {"id":"regain-faction","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["boundary:zongshi-current"]}]}]},
                {"id":"fixture:lb-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                {"id":"fixture:lb-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"usageScope":"turn","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.extra-draw-debt.draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":1}]}]},
                {"id":"fixture:lb-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"turn","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                {"id":"fixture:lb-yield-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:zongshi-current.SelectAndMoveOwnedCard"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}]}
                """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var presentation = JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实命令", description = "固定实体与技能实例调整" }, ["fixture:lb-quiet"] = new { name = "安静回合", description = "原生跳过出牌" },
                  [Gain] = Pause("额外得牌"), [Hp] = Pause("真实回复"), [YieldGain] = Pause("实际转交") } });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), presentation);
            foreach (var id in new[] { Driver, "fixture:lb-quiet", Gain, Hp, YieldGain }) b.AddSkill(new(id, id, "真实程序夹具") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:lb-selection", "固定选将", "无运行能力") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddSkill(new("fixture:lb-native", "原生主公", "固定真实来源") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? 1000d : -1000d) });
            var owner = new List<string>(); if (faction) owner.Add(Faction); if (!native) owner.Add(Driver); else owner.Add("fixture:lb-native");
            if (observers) { owner.Add(Hp); if (!equipment) owner.Add(Gain); }
            b.AddGeneral(new("fixture:lb-owner", "界刘表机制", "supporter", Draw, "qun", 3, owner) { InitialHp = 1 });
            var factions = new[] { "wei", "shu", "wu" };
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:lb-target-{i}", "固定目标", "supporter", "fixture:lb-selection", factions[i - 1], 8,
                observers && equipment ? ["fixture:lb-quiet", YieldGain] : ["fixture:lb-quiet"]));
            b.AddDeck(new("fixture:lb-deck", "固定合法实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, emptyDeck ? 16 : 64)
                .Select(i => new ContentDeckPhysicalCard(equipment ? "classic:silver-lion" : "standard:slash", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "真实界刘表", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:lb-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:lb-owner", "fixture:lb-target-1", "fixture:lb-target-2", "fixture:lb-target-3"]));
        }
        private static object Pause(string name) => new { name, description = "真实子结算暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
