using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PhaseHandSeizureChecks
{
    private const string Lihun = "ol:lihun", Biyue = "ol:biyue", Driver = "fixture:phs-driver";
    private const string Hp = "fixture:phs-hp", Cost = "fixture:phs-cost", Flip = "fixture:phs-flip", Gain = "fixture:phs-gain";
    private const string Earlier = "fixture:phs-earlier", Mode = "identity:classic-phase-hand-seizure-fixture";
    private const string Activation = "discard-flip-take-male-hand", Payment = "skill-program.ol:lihun.DiscardTurnOverAndTakeHand.payment";
    private const string Take = "skill-program.ol:lihun.DiscardTurnOverAndTakeHand.take", Return = "skill-program.ol:lihun.ReturnIssuedPhaseHandDebt";

    public static void LihunRealCostFlipTakeAndFrozenHpReturnColdReplay()
    {
        var (g, r) = Start(); Equip(g); var lion = V(g).Equipment.Single().Id;
        var original = g.CreateSnapshot(1).Players[1].Hand.Select(c => c.Id).ToArray();
        UseLihun(g, lion, 1); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        Require(Root(g).PhaseHandSeizure is { Stage: PhaseHandSeizureStage.PaymentChildren } &&
            g.State.Players[0].Hp == 4 && !g.State.Players[0].IsFaceDown && E<PhaseHandSeizureIssuedEvent>(g).Length == 0 &&
            Moves(g, Payment) is [var paid] && paid.CardId == lion && paid.From == CardLocation.Equipment(0) && paid.To == CardLocation.DiscardPile,
            "One physical armor discard and its real recovery occur before turnover or hand acquisition.");
        Cold(g, r); Reject(g); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Cost);
        Require(!g.State.Players[0].IsFaceDown && E<PhaseHandSeizurePaidEvent>(g).Length == 1,
            "The new payment suffix classifies as a real discard, with one exact paid fact and typed movement return.");
        Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Flip);
        Require(Root(g).PhaseHandSeizure is { Stage: PhaseHandSeizureStage.FlipChildren } && g.State.Players[0].IsFaceDown &&
            E<PhaseHandSeizureIssuedEvent>(g).Length == 0, "The actual turnover child completes before taking the then-current target hand.");
        Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        Require(Root(g).PhaseHandSeizure!.TakenCardIds.SequenceEqual(original) && Moves(g, Take).Length == original.Length &&
            original.All(id => Moves(g, Take).Count(m => m.CardId == id && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0)) == 1),
            "Every original current-hand entity really transfers once, before its gain child returns.");
        Cold(g, r); Continue(g); ReachPlay(g); DriverUse(g, "hurt", [], [1]); ReachPlay(g);
        Require(g.State.Players[1].Hp == 2 && !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Lihun), "The actual phase permits one paid seizure; later target HP can change.");
        EndPlay(g); Reach(g, p => Action(p, "phase-hand-debt-return")); var frozen = Root(g).PhaseHandDebtReturn!;
        Require(frozen is { TargetSeat: 1, FrozenHp: 2, RequiredCount: 2 } && g.CreateSnapshot(1).PendingDecision is null &&
            g.CreateSnapshot(2).PendingDecision is null && g.CreateSnapshot(3).PendingDecision is null,
            "The exact original target's HP freezes at Play ending; only the payer receives legal private choices.");
        Cold(g, r); Reject(g); var ids = P(g)!.ValidCardIds.Take(2).ToArray(); foreach (var id in ids) Answer(g, c => c.Cards.SequenceEqual([id]));
        Reach(g, p => ActivationChoice(p, Biyue)); var end = E<PhaseHandDebtSettledEvent>(g).Single();
        Require(end is { Reason: "returned", RequiredCount: 2, ActualCount: 2 } && Moves(g, Return).Length == 2 &&
            ids.All(id => g.CreateSnapshot(1).Players[1].Hand.Any(c => c.Id == id)) && Moves(g, Payment).Length == 1,
            "The frozen two-card invoice is paid once before actual Ending; resumed children never repeat its cost."); Cold(g, r);
    }

    public static void LihunMaleSelfEquipmentTransferCountsOnlyPhysicalCards()
    {
        var (g, r) = Start(male: true); Equip(g); var lion = V(g).Equipment.Single().Id; var cost = V(g).Hand.First().Id;
        UseLihun(g, cost, 0); Reach(g, p => p.SkillPrompt?.SkillId == Cost); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Flip);
        Cold(g, r); Continue(g); ReachPlay(g); var same = E<PhaseHandSeizureIssuedEvent>(g).Single();
        Require(same is { TargetSeat: 0, SameHand: true, ObtainedCount: 0 } && Moves(g, Take).Length == 0 && Moves(g, Payment).Length == 1,
            "A male acquiring the shared capability may select self; real discard and turnover pay once, with no fake same-Hand take.");
        var hand = V(g).Hand.Select(c => c.Id).ToArray(); Require(hand.Length == 2, "Uniform fixed entities leave two original hand cards and one equipped armor.");
        EndPlay(g); Reach(g, p => Action(p, "phase-hand-debt-return")); Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([lion]));
        foreach (var id in hand) Answer(g, c => c.Cards.SequenceEqual([id])); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var receipt = Root(g).PhaseHandDebtReturn!; var end = E<PhaseHandDebtSettledEvent>(g).Single();
        Require(receipt is { FrozenHp: 3, RequiredCount: 3, Stage: PhaseHandDebtReturnStage.MovementChildren } && receipt.CardIds.Count == 3 &&
            end is { Reason: "returned", RequiredCount: 3, ActualCount: 1 } && Moves(g, Return) is [var move] && move.CardId == lion &&
            move.From == CardLocation.Equipment(0) && move.To == CardLocation.Hand(0) && g.State.Players[0].Hp == 4 &&
            hand.All(id => !Moves(g, Return).Any(m => m.CardId == id)),
            "Only selected Equipment really enters self Hand and recovers HP; the two already-held cards satisfy their choices without movement or an enlarged invoice.");
        Cold(g, r); Reject(g); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain); Cold(g, r); Continue(g);
        Reach(g, p => ActivationChoice(p, Biyue)); Require(E<PhaseHandDebtSettledEvent>(g).Length == 1 && V(g).Hand.Count == 3,
            "Every equipment recovery/gain child drains once before the actual same-owner phase return."); Cold(g, r);
    }

    public static void LihunSourceLossInsufficientAndForcedEndHostBoundaries()
    {
        var (g, r) = Start(); Equip(g); UseLihun(g, V(g).Hand.First().Id, 1); FinishSeizure(g); Cold(g, r);
        // This explicit host grant/reset/forced-boundary audit begins after a fully command-replayed prefix.
        // Host changes below are intentionally not represented as accepted commands or claimed cold-replay input.
        var owner = HostPlayers(g)[0]; var original = owner.SkillGrants.Grants.Single(grant => grant.SkillId == Lihun);
        owner.SkillGrants.RemoveGrant(original.GrantId); owner.SkillGrants.Grant(new("fixture:phs-regrant", Lihun, original.SkillInstanceId + ":later", "acquired:host-audit"));
        ((SkillRuntimeStateStore)typeof(GameEngine).GetField("_skillRuntimeState", Flags)!.GetValue(g)!).ResetSkill(0, Lihun);
        Require(!g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Lihun), "Reacquisition and ResetSkill cannot erase this actual phase's issued paid-use fact.");
        while (V(g).Hand.Count > 1) { DriverUse(g, "trim", [V(g).Hand.First().Id], []); ReachPlay(g); }
        Invoke(g, "ClearPendingDecision"); Invoke(g, "EndTurn"); Reach(g, p => Action(p, "phase-hand-debt-return"));
        Require(Root(g).SkillInstanceId == original.SkillInstanceId && Root(g).PhaseHandDebtReturn is { FrozenHp: 3, RequiredCount: 2 } &&
            g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(f => f.Continuation == ProgramLifecycleContinuation.CompletePhaseHandDebtForcedEnd),
            "The original lost source remains the due identity; a forced quiet Play departure owns its EndTurn return and all available HE pays an insufficient invoice.");
        var ids = P(g)!.ValidCardIds.ToArray(); foreach (var id in ids) Answer(g, c => c.Cards.SequenceEqual([id]));
        Reach(g, p => p.SkillPrompt?.SkillId == Hp); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain); Continue(g);
        Reach(g, p => ActivationChoice(p, Biyue)); Require(E<PhaseHandDebtSettledEvent>(g).Single() is { RequiredCount: 2, ActualCount: 2 } && Moves(g, Return).Length == 2,
            "Original issued identity pays once through real equipment children before forced Ending, despite current grant replacement.");
    }

    public static void BiyueCurrentHandBranchesSingleActualDrawBatch()
    {
        foreach (var branch in new[] { "empty", "nonempty", "earlier-ending-gain" })
        {
            var (g, r) = Start(earlier: branch == "earlier-ending-gain");
            if (branch != "nonempty") while (V(g).Hand.Count > 0) { DriverUse(g, "trim", [V(g).Hand.First().Id], []); ReachPlay(g); }
            EndPlay(g); Reach(g, p => ActivationChoice(p, Biyue)); var handAtAcceptance = V(g).Hand.Count;
            Require(branch != "earlier-ending-gain" || handAtAcceptance == 1, "An earlier real Ending producer gains one card after the window's originally empty hand capture.");
            Cold(g, r); Activate(g, Biyue); Reach(g, p => p.SkillPrompt?.SkillId == Gain); var actual = Moves(g, "skill-program.ol:biyue.Draw");
            Require(actual.Length == (handAtAcceptance == 0 ? 2 : 1) && actual.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) &&
                g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Batch.Movements.Any(m => m.Reason.Value == "skill-program.ol:biyue.Draw")).Batch.Movements.Count == actual.Length,
                "One Draw instruction reads current Hand once and issues exactly one physical 2/1 batch; neither stale window facts nor post-draw reevaluation creates a third card."); Cold(g, r);
        }
    }

    public static void LihunDeathTerminalClosureAndFrozenReceiptCollections()
    {
        var (dead, r) = Start(); UseLihun(dead, V(dead).Hand.First().Id, 1); FinishSeizure(dead); DriverUse(dead, "kill", [], [1]); ReachPlay(dead);
        Require(!dead.State.Players[1].IsAlive && E<PhaseHandDebtSettledEvent>(dead).Single() is { Reason: "target-death", ActualCount: 0 } &&
            Moves(dead, Return).Length == 0, "The original target's real death closes unpaid debt explicitly without fake transfers."); Cold(dead, r);
        var (terminal, tr) = Start(); UseLihun(terminal, V(terminal).Hand.First().Id, 1); FinishSeizure(terminal); EndPlay(terminal);
        Reach(terminal, p => Action(p, "phase-hand-debt-return")); Answer(terminal, c => c.Cards.Count == 1); Cold(terminal, tr);
        var before = V(terminal).Hand.Select(c => c.Id).ToArray();
        // Producer-specific terminal host audit: it deliberately bypasses no command replay claim.
        Invoke(terminal, "EndAsDraw", "explicit phase-hand terminal audit"); Invoke(terminal, "AssertPhaseHandSeizurePrograms"); Invoke(terminal, "AssertCoreInvariants");
        Require(terminal.State.Status == EngineStatus.Completed && E<GameEndedEvent>(terminal).Single().Winner == Winner.Draw &&
            E<PhaseHandDebtSettledEvent>(terminal).Single() is { Reason: "game-end", ReturnFrameId: 0, RequiredCount: 0, ActualCount: 0 } &&
            Root(terminal).PhaseHandDebtReturn is { Stage: PhaseHandDebtReturnStage.Choosing } && Moves(terminal, Return).Length == 0 &&
            V(terminal).Hand.Select(c => c.Id).SequenceEqual(before), "A real terminal fact closes unfinished selection, retains paid diagnostics and leaves all untransferred cards in place.");
        var terminalState = State(terminal);
        Invoke(terminal, "ResumePhaseHandSeizure", Root(terminal).Id);
        Require(State(terminal) == terminalState, "A genuine terminal resume retains the original paid diagnostic without advancing its parent.");
        Invoke(terminal, "ReturnPhaseHandSeizureMovement", Root(terminal));
        Require(State(terminal) == terminalState, "A genuine terminal return does not require a new move or finish the retained receipt.");
        Invoke(terminal, "DrainPhaseHandSeizureMovement", Root(terminal).Id);
        Require(State(terminal) == terminalState, "A genuine terminal drain cannot push queued recovery or movement children.");
        var ids = new[] { 1, 2 }; var locations = new[] { CardLocation.Hand(0), CardLocation.Equipment(0) };
        var frozen = new ProgramPhaseHandDebtReturnReceipt(1, 1, 1, 2, 2, PhaseHandDebtReturnStage.Choosing, ids, locations);
        ids[0] = 99; locations[0] = CardLocation.DiscardPile; var replacement = new[] { 3, 4 }; var changed = frozen with { CardIds = replacement }; replacement[0] = 88;
        Require(frozen.CardIds.SequenceEqual([1, 2]) && frozen.Locations[0] == CardLocation.Hand(0) && changed.CardIds.SequenceEqual([3, 4]),
            "Construction and init replacement both freeze the new receipt's exposed nested collections.");
    }

    private static (GameEngine, ContentRegistry) Start(bool male = false, bool earlier = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardClassicGeneralPackage(), new Fixture(male, earlier));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:phs-owner", g.Revision, P(g)!.PromptId)); ReachPlay(g); return (g, r);
    }
    private static void Equip(GameEngine g) { var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip); Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind)); ReachPlay(g); }
    private static void UseLihun(GameEngine g, int cost, int target) => Accept(g, new UseProgramSkillCommand(0, Lihun, Activation, [cost], [target], g.Revision, P(g)!.PromptId));
    private static void DriverUse(GameEngine g, string id, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards, targets, g.Revision, P(g)!.PromptId));
    private static void FinishSeizure(GameEngine g) { Reach(g, p => p.SkillPrompt?.SkillId == Cost); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Flip); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain); Continue(g); ReachPlay(g); }
    private static void EndPlay(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static ProgramSkillFrame Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Lihun);
    private static PlayerSnapshot V(GameEngine g) => g.CreateSnapshot(0).Players[0];
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardMovementRecord[] Moves(GameEngine g, string reason) => g.CardMovements.Where(m => m.Reason.Value == reason).ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool ActivationChoice(PendingDecision p, string skill) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill);
    private static void Activate(GameEngine g, string skill) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) { for (var step = 0; step < 128; step++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); } throw new InvalidOperationException("Fixed phase-hand fixture failed to reach its boundary."); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected actual phase-hand command."); }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && before == State(g), "An unpublished choice changes no paid state or observer view."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "The exact command prefix restores frozen costs, physical movements, typed child frames and all four private views.");
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IReadOnlyList<CharacterState> HostPlayers(GameEngine g) => (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", Flags)!.GetValue(g)!;
    private static void Invoke(GameEngine g, string name, params object[] args) => typeof(GameEngine).GetMethod(name, Flags)!.Invoke(g, args);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool male, bool earlier) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-phase-hand-seizure", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":3}]},
                {"id":"trim","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]}]},
              {"id":"{{Hp}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Cost}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementDiscardOnly":true,"movementReasons":["{{Payment}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost-seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Flip}}","revision":1,"triggers":[{"id":"turned","window":"characterTurnedOver","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"flip-seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Gain}}","revision":1,"triggers":[{"id":"gained","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{Take}}","{{Return}}","skill-program.ol:biyue.Draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Earlier}}","revision":1,"triggers":[{"id":"earlier-current-gain","window":"turnEnding","subject":"owner","optional":false,"priority":100,"effects":[{"op":"draw","target":"owner","amount":1}]}]}
            ]}
            """;
            var names = new[] { Driver, Hp, Cost, Flip, Gain, Earlier }.ToDictionary(id => id, id => (object)new { name = id, description = "真实实体、事件和原始帧边界", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = names }));
            foreach (var pair in catalog.Programs) b.AddSkill(new(pair.Key, pair.Key, "真实共享边界") { Program = pair.Value });
            b.AddSkill(new("fixture:phs-idle", "固定其他候选", "无运行技能"));
            b.AddGeneral(new("fixture:phs-owner", "共享阶段归还机制", "supporter", Lihun, "qun", 3,
                earlier ? [Biyue, Driver, Hp, Cost, Flip, Gain, Earlier] : [Biyue, Driver, Hp, Cost, Flip, Gain], male ? GeneralGender.Male : GeneralGender.Female) { InitialHp = 2 });
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:phs-peer-{i}", "固定男性目标", "supporter", "fixture:phs-idle", "wei", 3, [Gain], GeneralGender.Male));
            // Every recipe entry is the same real armor: shuffle/selection RNG cannot affect reachability.
            b.AddDeck(new("fixture:phs-deck", "固定同类实体", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("classic:silver-lion", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "共享阶段真实边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:phs-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:phs-owner", "fixture:phs-peer-1", "fixture:phs-peer-2", "fixture:phs-peer-3"]));
        }
    }
}
