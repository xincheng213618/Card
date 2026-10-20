using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheDuoduoYishanChecks
{
    private const string First = "shanhe:current-duoduo-yishan-2082", Second = "shanhe:current-duoduo-yishan-2083", Third = "shanhe:current-duoduo-yishan-2084";
    private const string FirstReward = "tactic-duoduo-yishan-current-2082", SecondReward = "tactic-duoduo-yishan-current-2083", ThirdReward = "tactic-duoduo-yishan-current-2084";
    private const string Capacity = StandardShanheCampaignCatalog.CurrentJinnangJiSkillId, Zaiqi = StandardShanheCampaignCatalog.S5ZaiqiSkillId;
    private const string Owner = "fixture:ddys-owner", Deck = "fixture:ddys-deck", Driver = "fixture:ddys-driver", ThirdDraw = "fixture:ddys-third-draw";
    private const string Gain = "fixture:ddys-gain", Mutation = "fixture:ddys-mutation", Gate = "fixture:ddys-hp-source";
    private const string Extra = "fixture:ddys-phase-draw", OffTurn = "fixture:ddys-off-turn", Quiet = "fixture:ddys-quiet";
    private static string Peer(int seat) => "fixture:ddys-peer-" + seat;
    private static readonly string[] Families = [First, Second, Third];
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture:ddys-production", "classic:guan-yu", 4, 0, 0, 0, 0, [Zaiqi], [], [],
            [new(StandardShanheS5BossPackage.ZhangliaoHuangkongId)], [], new string('b', 64))
        { TacticSkillIds = [First, Second, Third, Capacity] }));

    public static void ActualDrawOrdinalsStackThreeVersionsAndFreezeProductionLoadoutCold()
    {
        VerifyConditionLoaderBoundaries();
        VerifyProductionLoadout();
        var s = Scene.Start("stack"); s.Play();
        var initial = s.V().HandCount;
        Need(initial == 3 && s.Draws().Length == 0 && Families.All(id => s.Paid(id).Length == 0),
            "Three genuinely dealt initial cards are neither three native Draw calls nor an early family reward.");
        s.Begin("one"); s.Until(() => s.AtOrdinal(1));
        var proof = s.Proof!;
        Need(proof.RequestedCount == 1 && proof.ActualCount == 1 && proof.LastBatchId == s.Batch!.Id &&
            proof.OrdinalEntitlements?.Any(entry => entry.Source.SkillId == Third && entry.Source.OwnerSeat == 0) == true,
            "The first real Draw owns its frozen ordinal, final gain batch and original live tactic source.");
        Frozen(proof.OrdinalEntitlements!); s.Privacy(); s.Cold(); s.Continue();
        s.Until(() => s.AtOrdinal(2));
        Need(s.Proof is { RequestedCount: 2, ActualCount: 2 } && s.FromSkill(Third) && s.Paid(Third).Length == 1,
            "The third version's actual Draw2 is one second native invocation, even though it generates two physical gain batches.");
        s.Cold(); s.Play();
        s.Begin("one"); s.Until(() => s.AtOrdinal(4));
        Need(s.Proof is { RequestedCount: 1, ActualCount: 1 } && s.FromSkill(Second) && s.Paid(Second).Length == 1,
            "The next original Draw is third and triggers II once; its real reward is the fourth invocation.");
        s.Cold(); s.Play();
        s.Begin("one"); s.Until(() => s.AtOrdinal(6));
        Need(s.Proof is { RequestedCount: 1, ActualCount: 1 } && s.FromSkill(First) && s.Paid(First).Length == 1,
            "The fifth original Draw triggers the unsuffixed version once and produces a real sixth invocation.");
        s.Cold(); s.Play();
        Need(s.Draws().Select(draw => draw.ActualTurnDrawOrdinal).SequenceEqual([1, 2, 3, 4, 5, 6]) &&
            s.Draws().Select(draw => draw.ActualCount).SequenceEqual([1, 2, 1, 1, 1, 1]) &&
            s.V().HandCount == initial + 7 && Families.All(id => s.Paid(id).Length == 1),
            "Three independently owned production versions coexist without an invented upgrade or recursion exclusion; each issues exactly its one real reward.");
        s.AssertPureQueries(); s.Cold();
    }

    public static void CapturedDrawEligibilityRejectsLateSourcesAndPreservesPaidChildrenCold()
    {
        foreach (var scene in new[] { "late-before", "late-child", "loss-before", "paid-loss", "paid-death" })
        {
            var s = Scene.Start(scene); s.Play();
            s.Begin("one"); s.Play(); s.Begin("one"); s.Play();
            Need(s.Draws().Select(draw => draw.ActualTurnDrawOrdinal).SequenceEqual([1, 2]) && s.Paid(Second).Length == 0,
                "Two positive original Draw calls establish the real prefix without consuming II's third-call opportunity.");
            if (scene == "late-before") { s.Begin("lose"); s.Play(); Need(s.Has(Second), "A real HP payment obtains II before the third native invocation begins."); }
            s.BeginThird();
            if (scene is "late-child" or "loss-before")
            {
                s.Until(() => s.AtMutation("mutated"));
                Need((scene == "late-child" ? s.Has(Second) : !s.Has(Second)) && s.Paid(Second).Length == 0 &&
                    s.Draws().Single(draw => draw.ActualTurnDrawOrdinal == 3).ActualCount == 2,
                    "A genuine early gain child changes the live HP-derived source before the final batch can offer a reward. " + s.Diagnostic());
                s.Cold(); s.Continue(); s.Until(() => s.AtOrdinal(3));
                var captured = s.Proof!.OrdinalEntitlements?.Any(entry => entry.Source.SkillId == Second) == true;
                Need(captured == (scene == "loss-before") && s.Paid(Second).Length == 0,
                    "Eligibility records the source at native Draw start, rather than borrowing a grant from an earlier batch of that same Draw.");
                s.Cold(); s.Play();
                Need(s.Paid(Second).Length == 0 && s.Draws().Length == 3,
                    "Late acquisition cannot backfill an unqualified third Draw; withdrawal before reward payment also cancels it.");
                if (scene == "loss-before") { s.Begin("lose"); s.Play(); }
                s.Begin("one"); s.Play();
                Need(s.Has(Second) && s.Draws().Last().ActualTurnDrawOrdinal == 4 && s.Paid(Second).Length == 0,
                    "A later active source cannot turn the fourth native Draw into the already passed third-call opportunity.");
                s.Cold(); continue;
            }
            if (scene is "paid-loss" or "paid-death")
            {
                s.Until(() => s.AtMutation("paid"));
                var paid = s.Paid(Second).Single();
                var paidProgram = paid.ParentFrameId ?? throw new InvalidOperationException("The real reward must retain its producing program frame.");
                Need(paid.ActualTurnDrawOrdinal == 4 && paid.ActualCount == 1 && s.V().HandCount == 5,
                    "The genuine reward has already paid its one entity before its own gained-card child changes the source.");
                s.Privacy(); s.Cold(); s.Continue();
                if (scene == "paid-loss")
                {
                    s.Until(() => s.AtMutation("mutated"));
                    Need(!s.Has(Second) && s.V().Hp == 6, "Real recovery withdraws the HP-conditioned source inside the paid reward's child.");
                    s.Cold(); s.Play();
                }
                else
                {
                    s.Until(() => !s.V().IsAlive && s.G.ResolutionStack.All(frame => frame.Id != paidProgram) &&
                        s.E<ProgramBindingResolvedEvent>().Any(fact => fact.FrameId == paidProgram && fact.SkillId == Second));
                    Need(s.G.CreateSnapshot(0).Winner == Winner.None && s.G.CreateSnapshot(0).Players[1].IsAlive &&
                        s.E<PlayerDiedEvent>().Any(fact => fact.VictimSeat == 0),
                        "The real LoseHp/Dying/LetDie child returns the paid reward program while a living ally keeps the match running; game-over cleanup cannot conceal a broken return.");
                }
                Need(s.Paid(Second).Length == 1 && s.Draws().Count(draw => draw.InvocationId == paid.InvocationId) == 1 &&
                    s.G.CardMovements.Count(move => move.Reason == paid.Reason && move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0)) == 1 &&
                    s.E<ProgramBindingResolvedEvent>().Count(fact => fact.FrameId == paidProgram && fact.SkillId == Second) == 1 &&
                    s.G.ResolutionStack.All(frame => frame.Id != paidProgram),
                    "Source loss or death after payment cannot retract, repeat or orphan the one real reward entity.");
                s.Cold(); continue;
            }
            s.Play();
            Need(s.Draws().Select(draw => draw.ActualTurnDrawOrdinal).SequenceEqual([1, 2, 3, 4]) && s.Paid(Second).Length == 1,
                "An actually acquired source before the third invocation qualifies against the genuine earlier unowned draw prefix.");
            s.Cold();
        }
    }

    public static void DrawPhaseBonusesFeedJinnangJiButReplacementGainsAndOtherTurnsStayScopedCold()
    {
        var phase = Scene.Start("phase"); phase.Until(() => phase.AtOrdinal(3));
        Need(phase.Draws().Select(draw => draw.ActualCount).SequenceEqual([3, 2, 2]) && phase.DrawPhaseTotal() == 7 &&
            phase.V().HandLimit == 9 && phase.Paid(Second).Length == 0,
            "The third actual invocation is Draw2, not the third physical card; before its reward, seven Draw-phase cards add floor(7/2). " + phase.Diagnostic());
        phase.Cold(); phase.Continue(); phase.Until(() => phase.AtOrdinal(4));
        Need(phase.FromSkill(Second) && phase.DrawPhaseTotal() == 8 && phase.V().HandLimit == 10,
            "II's genuine reward remains in the actual Draw phase and raises Jinnang Ji from three to four extra hand capacity.");
        phase.Cold(); phase.Play();

        var cross = Scene.Start("cross"); cross.Play();
        Need(cross.Draws().Select(draw => draw.ActualCount).SequenceEqual([3, 2]) && cross.Paid(Second).Length == 0 && cross.V().HandLimit == 8,
            "A Draw3 followed by Draw2 consists of two invocations despite five acquired entities.");
        cross.BeginThird(); cross.Play();
        Need(cross.Draws().Select(draw => draw.ActualTurnDrawOrdinal).SequenceEqual([1, 2, 3, 4]) &&
            cross.Paid(Second).Length == 1 && cross.DrawPhaseTotal() == 5 && cross.V().HandLimit == 8,
            "The same turn's Play-phase third Draw qualifies for II, while neither it nor its reward can borrow Draw-phase hand capacity.");
        cross.Cold();

        foreach (var scene in new[] { "zaiqi", "tuxi" })
        {
            var s = Scene.Start(scene); var replacement = scene == "zaiqi" ? Zaiqi : "classic:tuxi";
            s.Until(() => s.P?.SkillPrompt?.SkillId == replacement && s.P.Choices.Any(choice => Action(choice) == "activate"));
            s.Cold(); s.Answer(choice => Action(choice) == "activate");
            if (scene == "tuxi")
            {
                s.Until(() => s.P?.Choices.Any(choice => choice.Targets.SequenceEqual([1, 2])) == true);
                s.Answer(choice => choice.Targets.SequenceEqual([1, 2]));
            }
            s.Until(s.AtGain);
            Need(s.Draws().Length == 0 && s.Paid(Second).Length == 0 && s.V().HandLimit == s.V().Hp &&
                (scene == "zaiqi" ? s.E<ProgramCardsRevealedEvent>().Any(fact => fact.SkillId == Zaiqi && fact.Cards.Count == 3) :
                    s.E<ProgramRandomHandCardsTakenEvent>().Any(fact => fact.TargetSeats.SequenceEqual([1, 2]))),
                "A real 887 reveal/gain or Tuxi hand transfer does not become a native Draw, an ordinal, or Jinnang Ji capacity.");
            s.Cold(); s.Play();
            for (var call = 0; call < 3; call++) { s.Begin("one"); s.Play(); }
            Need(s.Draws().Select(draw => draw.ActualTurnDrawOrdinal).SequenceEqual([1, 2, 3, 4]) && s.Paid(Second).Length == 1 &&
                s.DrawPhaseTotal() == 0 && s.V().HandLimit == s.V().Hp,
                "Only three subsequent real Play Draw calls reach II's opportunity, independently of earlier dealt or transferred cards.");
            s.Cold();
        }

        var off = Scene.Start("offturn"); off.Play();
        for (var call = 0; call < 3; call++) { off.Begin("one"); off.Play(); }
        var firstTurn = off.Turn; off.EndPlay();
        off.Until(() => off.AtOrdinal(3) && off.Proof?.ActualTurnOwnerSeat == 1);
        Need(off.Turn > firstTurn && off.Draws().Select(draw => draw.ActualTurnDrawOrdinal).SequenceEqual([1, 2, 3]) &&
            off.DrawPhaseTotal() == 0 && off.V().HandLimit is null,
            "A real allied NPC turn starts a fresh recipient-owned draw ordinal; its support draws are not the human's Draw phase, and the inactive human's view does not project a hand limit. " +
            $"FirstTurn={firstTurn}, DrawPhaseTotal={off.DrawPhaseTotal()}. " + off.Diagnostic());
        off.Cold(); off.Continue(); off.Until(() => off.AtOrdinal(4) && off.FromSkill(Second));
        Need(off.Paid(Second).Length == 2 && off.Draws().All(draw => draw.ActualTurnOwnerSeat == 1) && off.V().HandLimit is null,
            "The family genuinely triggers once on another character's third draw; the inactive human's hand-limit projection remains absent rather than exposing an old phase value.");
        off.Privacy(); off.Cold();
    }

    private static void VerifyProductionLoadout()
    {
        var definition = StandardShanheCampaignCatalog.CreateCodexDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId);
        var old = StandardShanheCampaignCatalog.CreateCodexDefinition(1, StandardShanheCampaignCatalog.LocalEditionId);
        string[] rewards = [FirstReward, SecondReward, ThirdReward];
        var defaults = StandardShanheCampaignCatalog.GetDefaultCodexUnlockedRewardIds(definition);
        var oldDefaults = StandardShanheCampaignCatalog.GetDefaultCodexUnlockedRewardIds(old);
        Need(rewards.All(id => definition.Rewards.Any(reward => reward.Id == id) && old.Rewards.All(reward => reward.Id != id)),
            "Only the actual current edition offers these three separately acquirable tactic versions.");
        Need(defaults.Contains(FirstReward) && defaults.Contains(SecondReward) && !defaults.Contains(ThirdReward) &&
            defaults.All(id => definition.Rewards.Any(reward => reward.Id == id)) && rewards.All(id => !oldDefaults.Contains(id)),
            "The actual definition-aware default pool exposes I/II without inventing a tree node, while III still requires research and the original edition receives no unsupported reward ID.");
        var run = ShanheCampaignSession.Create(definition, new(83, "classic:guan-yu")
        { UseCodexRewardPool = true, UnlockedRewardIds = defaults.Append(ThirdReward).ToArray(), EquippedTacticIds = rewards }, StandardShanheCampaignCatalog.CreateBattleRegistry);
        void ColdRun()
        {
            var before = JsonSerializer.Serialize(run.Snapshot);
            run = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(run.CreateCheckpoint())),
                definition, StandardShanheCampaignCatalog.CreateBattleRegistry);
            Need(JsonSerializer.Serialize(run.Snapshot) == before, "Cold recovery preserves the production loadout, actual map route and issued tactic recipe.");
        }
        ColdRun();
        for (var step = 0; step < 6 && run.Snapshot.Phase != ShanheCampaignPhase.Battle; step++)
        {
            ShanheCampaignCommand command = run.Snapshot.Phase == ShanheCampaignPhase.Map
                ? new MoveShanheNodeCommand(run.Snapshot.AvailableNodes[0].Id, run.Revision)
                : new ChooseShanheNodeOptionCommand(run.Snapshot.NodeChoices.First(choice => choice.RewardId == "supplies").Id, null, run.Revision);
            var accepted = run.Submit(command); Need(accepted.Accepted, accepted.Error ?? "The actual equipped route was rejected."); ColdRun();
        }
        var setup = run.PendingBattleSetup ?? throw new InvalidOperationException("The actual current-edition route did not issue its first battle.");
        Need(setup.TacticSkillIds.SequenceEqual(Families) && setup.ExtraSkillIds.Count == 0,
            "An actual equipped campaign issues all three distinct tactic programs, rather than copying probe rewards into general-skill slots.");
        var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup);
        var battle = StandardShanheCampaignCatalog.CreateBattle(setup);
        var result = battle.Submit(new StartGameCommand()); Need(result.Accepted, result.Error?.Message ?? "The actual production encounter failed to start.");
        Need(Families.All(id => battle.CreateSnapshot(0).Players[0].Skills?.Count(skill => skill.ContentId == id) == 1),
            "The native encounter really grants each separately equipped production definition once.");
        var beforeBattle = State(battle);
        battle = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(battle.CreateCheckpoint())), registry);
        Need(beforeBattle == State(battle), "The real equipped native battle cold-restores its actual sources, private views and physical card ledger.");
    }

    private static void VerifyConditionLoaderBoundaries()
    {
        const string id = "fixture:ddys-ordinal-condition";
        object Condition(int ordinal) => new { kind = "nativeDrawActualTurnOrdinalIs", value = ordinal };
        Dictionary<string, object> Trigger() => new()
        {
            ["id"] = "exact-native-draw", ["window"] = "cardsGained", ["subject"] = "owner", ["destinationZones"] = new[] { "hand" },
            ["movementOccurrence"] = "perBatch", ["optional"] = false, ["condition"] = Condition(3),
            ["effects"] = new[] { new { op = "draw", target = "owner", amount = 1 } }
        };
        var presentation = JsonSerializer.Serialize(new
        {
            schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new Dictionary<string, object> { [id] = new { name = "原生摸牌序号条件", description = "只读取原调用的冻结序号和来源" } }
        });
        void Load(Dictionary<string, object> trigger) => _ = SkillProgramCatalog.Load(JsonSerializer.Serialize(new
        {
            schemaVersion = SkillProgramCatalog.RulesSchemaVersion,
            skills = new[] { new { id, revision = 1, triggers = new[] { trigger } } }
        }), presentation);
        Load(Trigger());
        var nested = Trigger(); nested["condition"] = new { kind = "all", children = new[] { Condition(3) } }; Load(nested);
        foreach (var change in new Action<Dictionary<string, object>>[]
        {
            trigger => trigger["condition"] = Condition(0), trigger => trigger["condition"] = Condition(-1),
            trigger => trigger["condition"] = new { kind = "nativeDrawActualTurnOrdinalIs" },
            trigger => trigger["condition"] = new { kind = "always", value = 3 },
            trigger => trigger["window"] = "turnEnding", trigger => trigger["subject"] = "any",
            trigger => trigger["destinationZones"] = new[] { "equipment" }, trigger => trigger.Remove("movementOccurrence"),
            trigger => trigger["evaluateConditionAtResolution"] = true,
            trigger => { trigger["condition"] = new { kind = "all", children = new[] { Condition(3) } }; trigger["window"] = "turnEnding"; }
        })
        {
            var trigger = Trigger(); change(trigger); var rejected = false;
            try { Load(trigger); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "Native ordinal conditions reject nonpositive/missing values, unrelated values, wrong or nested windows, subjects, zones, occurrences and live reevaluation.");
        }
    }

    private static string Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action") ?? "";
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Frozen<T>(IReadOnlyList<T> values)
    {
        Need(values is IList<T> { IsReadOnly: true }, "The native draw entitlement list is frozen.");
        try { ((IList<T>)values).Add(default!); throw new InvalidOperationException("Draw entitlements allowed mutation."); }
        catch (NotSupportedException) { }
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, game.PlayerCount).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))),
        Frames = game.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
        Facts = game.Events.Select(fact => JsonSerializer.Serialize(fact.Payload, fact.Payload.GetType())),
        game.Revision, game.CardMovements, Zones = game.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(game.AcceptedCommands)
    });

    private sealed class Scene(GameEngine game, ContentRegistry registry, string scene)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public int Turn => G.CreateSnapshot(0).TurnNumber;
        public PlayerSnapshot V() => G.CreateSnapshot(0).Players[0];
        public T[] E<T>() => G.Events.Select(fact => fact.Payload).OfType<T>().ToArray();
        public CardMovementBatchContext? Batch => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(frame => frame.WindowContext?.MovementBatch is not null)?.WindowContext?.MovementBatch;
        public NativeDrawInvocationProof? Proof => Batch?.NativeDrawInvocation;
        public NativeDrawInvocationRecordedEvent[] Draws() => E<NativeDrawInvocationRecordedEvent>().Where(fact => fact.OwnerSeat == 0 && fact.TurnNumber == Turn && fact.ActualTurnDrawOrdinal is not null).ToArray();
        public NativeDrawInvocationRecordedEvent[] Paid(string id) => E<NativeDrawInvocationRecordedEvent>().Where(fact => fact.OwnerSeat == 0 && fact.Reason == new CardMoveReason("skill-program." + id + ".Draw")).ToArray();
        public int DrawPhaseTotal() => Draws().Where(draw => draw.Phase is { Kind: ActualDiscardRecoveryPhaseKind.Draw, ActorSeat: 0 }).Sum(draw => draw.ActualCount);
        public bool Has(string id) => V().Skills?.Any(skill => skill.ContentId == id) == true;
        public bool AtGain() => P?.SkillPrompt?.SkillId == Gain;
        public bool AtOrdinal(int ordinal) => AtGain() && Proof is { } proof && proof.ActualTurnDrawOrdinal == ordinal && proof.LastBatchId == Batch!.Id;
        public bool AtMutation(string option) => P is { SkillPrompt.SkillId: Mutation } prompt && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == option);
        public bool FromSkill(string id) => Proof is { ParentFrameId: { } parent } proof && proof.Reason == new CardMoveReason("skill-program." + id + ".Draw") &&
            G.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.Id == parent && frame.SkillId == id && frame.OwnerSeat == 0) &&
            E<ProgramBindingStartedEvent>().Any(fact => fact.FrameId == parent && fact.SkillId == id && fact.OwnerSeat == 0);
        public void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, (result.Error?.Message ?? "Actual draw command rejected.") + " " + Diagnostic());
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P ?? throw new InvalidOperationException("Missing real draw decision."); Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Continue() => Answer(choice => choice.Parameters.GetValueOrDefault("option-id") is "continue" or "mutated" or "paid");
        public void Until(Func<bool> stop)
        {
            for (var step = 0; step < 320; step++)
            {
                if (stop()) return;
                if (P is { PlayerSeat: 0 })
                {
                    if (AtGain() || AtMutation("mutated") || AtMutation("paid")) { Continue(); continue; }
                    if (P.Kind == DecisionKind.RescueDying) { Answer(choice => choice.Parameters.GetValueOrDefault("response") == "let-die"); continue; }
                    throw new InvalidOperationException("Unexpected native draw boundary: " + Diagnostic());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Bounded real draw boundary not reached: " + Diagnostic());
        }
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Begin(string activation) => Accept(new UseProgramSkillCommand(0, Driver, activation, [], [], G.Revision, P!.PromptId));
        public void BeginThird() => Accept(new UseProgramSkillCommand(0, ThirdDraw, "third", [], [], G.Revision, P!.PromptId));
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Cold()
        { var before = State(G); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(G), "Cold replay retains exact native ordinals, entitlements, real children and once-paid entities."); }
        public void Privacy()
        {
            for (var viewer = 0; viewer < G.PlayerCount; viewer++)
            {
                var snapshot = G.CreateSnapshot(viewer);
                Need(snapshot.Players.Where(player => player.Seat != viewer).All(player => player.Hand.Count == 0), "Native draw proof cannot reveal another player's private hand identities.");
                if (viewer != 0) Need(snapshot.PendingDecision is null, "Only the real owner receives the private gain-child prompt.");
            }
        }
        public void AssertPureQueries()
        { var before = State(G); for (var repeat = 0; repeat < 3; repeat++) for (var viewer = 0; viewer < G.PlayerCount; viewer++) _ = G.CreateSnapshot(viewer); Need(before == State(G), "Ordinal and hand-limit projection is pure and issues no second reward."); }
        public string Diagnostic() => JsonSerializer.Serialize(new
        {
            scene, Turn, V().Hp, V().HandCount, V().HandLimit, Alive = V().IsAlive,
            Prompt = P is null ? null : new { P.Kind, Skill = P.SkillPrompt?.SkillId, P.Choices },
            Draws = E<NativeDrawInvocationRecordedEvent>().Select(draw => new { draw.InvocationId, draw.OwnerSeat, draw.TurnNumber, draw.ActualTurnOwnerSeat,
                draw.ActualTurnDrawOrdinal, draw.Requested, draw.ActualCount, draw.PhaseActorSeat, PhaseKind = draw.Phase?.Kind,
                Producer = draw.DirectProducer?.SkillId, draw.LastBatchId }),
            Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind })
        });
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            var health = scene == "zaiqi" ? 4 : scene is "loss-before" or "paid-loss" or "paid-death" ? 5 : 6;
            var allied = scene is "offturn" or "paid-death";
            var allies = allied ? new ShanheCombatantDefinition[] { new(Peer(1)) } : [];
            var enemies = (allied ? new[] { 2, 3 } : new[] { 1, 2, 3 }).Select(seat => new ShanheCombatantDefinition(Peer(seat))).ToArray();
            var setup = new ShanheBattleSetup(83, 1, "fixture:ddys-" + scene, Owner, health, 0, 0, 0, 0, [], [], [], enemies, allies, new string('b', 64));
            var game = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
            }, registry);
            var result = new Scene(game, registry, scene); result.Accept(new StartGameCommand()); return result;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ddys", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var id in Families.Append(Capacity)) b.AddSkill(Production.Value.GetSkill(id));
            if (scene == "zaiqi") b.AddSkill(Production.Value.GetSkill(Zaiqi));
            if (scene == "tuxi") b.AddSkill(Production.Value.GetSkill("classic:tuxi"));
            var definitions = new List<Dictionary<string, object>>();
            var labels = new Dictionary<string, object>();
            object Pause(string option) => new { op = "chooseOption", target = "owner", resultBind = option, options = new[] { new { id = option } } };
            object Effect(string op, int amount) => new { op, target = "owner", amount };
            void Add(string id, string property, object value, params string[] options)
            {
                definitions.Add(new() { ["id"] = id, ["revision"] = 1, [property] = value });
                labels[id] = new { name = id, description = "真实原生摸牌、来源与子流程，不修改运行时快照或注入事实", optionLabels = options.ToDictionary(option => option, option => option) };
            }
            object Activation(string id, string op, int amount) => new
            { id, minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { Effect(op, amount) } };
            Add(Driver, "activations", new[] { Activation("one", "draw", 1), Activation("lose", "loseHp", 1) });
            Add(ThirdDraw, "activations", new[] { Activation("third", "draw", 2) });
            Add(Gain, "triggers", new[]
            {
                new { id = "actual-gain", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" },
                    movementOccurrence = "perBatch", priority = 100, optional = false,
                    movementReasons = new[] { CardMoveReasons.Draw.Value, "skill-program." + Driver + ".Draw", "skill-program." + ThirdDraw + ".Draw",
                        "skill-program." + Extra + ".Draw", "skill-program." + OffTurn + ".Draw", "skill-program." + First + ".Draw",
                        "skill-program." + Second + ".Draw", "skill-program." + Third + ".Draw", "skill-program." + Zaiqi + ".MoveBoundCards",
                        "skill-program.classic:tuxi.TakeRandomHandCardFromSelectedTargets" }, effects = new[] { Pause("continue") } }
            }, "continue");
            if (scene is "late-before" or "late-child" or "loss-before" or "paid-loss" or "paid-death")
                Add(Gate, "hpSkillGrants", new[] { new { id = "real-hp-source", skillId = Second, maximumHp = 5 } });
            if (scene is "late-child" or "loss-before" or "paid-loss" or "paid-death")
            {
                var paid = scene.StartsWith("paid-", StringComparison.Ordinal);
                var effects = new List<object>(); if (paid) effects.Add(Pause("paid"));
                effects.Add(Effect(scene == "late-child" || scene == "paid-death" ? "loseHp" : "recover", scene == "paid-death" ? 20 : 1));
                if (scene != "paid-death") effects.Add(Pause("mutated"));
                Add(Mutation, "triggers", new[]
                {
                    new { id = "real-source-child", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" }, movementOccurrence = "perBatch",
                        movementReasons = new[] { "skill-program." + (paid ? Second : ThirdDraw) + ".Draw" }, priority = 200, optional = false,
                        usageScope = "game", usageLimit = 1, effects = effects.ToArray() }
                }, scene == "paid-death" ? ["paid"] : paid ? ["paid", "mutated"] : ["mutated"]);
            }
            if (scene is "phase" or "cross") Add(Extra, "triggers", new[]
            {
                new { id = "actual-phase-draws", window = "drawPhaseEnded", subject = "owner", optional = false,
                    effects = scene == "phase" ? new[] { Effect("draw", 2), Effect("draw", 2) } : new[] { Effect("draw", 2) } }
            });
            if (scene == "offturn") Add(OffTurn, "triggers", new[]
            {
                new { id = "ally-actual-turn", window = "turnStartBeforeNormalFlow", subject = "owner", optional = false,
                    effects = new object[] { new { op = "selectAllPublicTeamTargets", target = "owner", targetKind = "otherPublicTeamAllies", allowEmpty = false },
                        new { op = "draw", target = "selectedTargets", amount = 1 }, new { op = "draw", target = "selectedTargets", amount = 1 },
                        new { op = "draw", target = "selectedTargets", amount = 1 } } }
            });
            Add(Quiet, "triggers", new[]
            {
                new { id = "quiet-dodge-holder", window = "afterNormalDraw", subject = "owner", optional = false,
                    effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } }
            });
            var programs = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in programs.Programs) b.AddSkill(new(id, programs.Presentations[id].Name, programs.Presentations[id].Description)
            { Program = program, ProgramPresentation = programs.Presentations[id] });
            var own = new List<string> { ThirdDraw, Gain };
            if (scene == "stack") own.AddRange(Families);
            else if (scene is "phase" or "cross" or "offturn" or "zaiqi" or "tuxi") own.AddRange([Second, Capacity]);
            else own.Add(Gate);
            if (scene is "late-child" or "loss-before" or "paid-loss" or "paid-death") own.Add(Mutation);
            if (scene is "phase" or "cross") own.Add(Extra);
            if (scene == "zaiqi") own.Add(Zaiqi);
            if (scene == "tuxi") own.Add("classic:tuxi");
            b.AddGeneral(new(Owner, "多多益善真实摸牌者", "", Driver, "qun", 6, own));
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new(Peer(seat), "只持真实闪的角色", "", Quiet, "wei", 6,
                scene == "offturn" && seat == 1 ? [OffTurn] : []));
            b.AddDeck(new(Deck, "固定黑色闪", scene == "stack" ? 3 : scene == "tuxi" ? 2 : 0, scene is "phase" or "cross" ? 3 : scene is "zaiqi" or "tuxi" ? 2 : 0, [])
            { PhysicalCards = Enumerable.Range(0, 112).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "多多益善原生调用", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = scene is "offturn" or "paid-death" ? 2 : 1, [ShanheBattleSetup.EnemyTeamId] = scene is "offturn" or "paid-death" ? 2 : 3 }));
        }
    }
}
