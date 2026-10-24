using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5XiahouPairChecks
{
    private const string Driver = "fixture:s5-xiahou-driver", Watch = "fixture:s5-xiahou-watch";
    private const string External = "fixture:s5-xiahou-other-skip", Deck = "fixture:s5-xiahou-deck";
    private const string Fire = "fixture:s5-xiahou-native-fire";
    private const string Loss = "fixture:s5-xiahou-native-source-loss";
    private const string Ganglie = StandardShanheS5XiahouPairPackage.GanglieId;
    private const string Shensu = StandardShanheS5XiahouPairPackage.ShensuId;
    private const string DiscardReason = "skill-program." + Ganglie + ".SelectAndMoveOwnedCard";

    public static void GanglieEachRealPointJudgesRedTwoOrDiscardsTwoAndPaidChildrenCold()
    {
        foreach (var black in new[] { false, true })
        {
            var s = Scene.Start(shensu: false, black);
            s.Play();
            s.Accept(new UseProgramSkillCommand(0, Driver, "actual-foreign-source", [], [2], s.G.Revision, s.P!.PromptId));
            for (var point = 0; point < 2; point++)
            {
                s.Until(() => s.IsActivation(Ganglie)); s.Activate();
                s.Until(() => s.P?.Kind == DecisionKind.ProgramJudgmentReplacement);
                if (point == 0) s.Cold();
                s.Answer(c => c.Parameters.GetValueOrDefault("action") == "program-judgment-replace-skip");
                if (!black) continue;
                for (var card = 0; card < 2; card++)
                {
                    s.Until(() => s.P?.Choices.Any(c => Action(c) == "select-and-move-owned-card") == true);
                    s.Answer(c => Action(c) == "select-and-move-owned-card");
                    s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
                    Need(s.G.CardMovements.Count(m => m.Reason.Value == DiscardReason &&
                            m.From == CardLocation.Hand(2) && m.To == CardLocation.DiscardPile) == point * 2 + card + 1,
                        "Each source entity is truly discarded before its paid movement child, never prepaid or counted twice.");
                    if (point == 0 && card == 0) s.Cold();
                    s.Continue();
                }
            }
            s.Play();
            Need(s.E<JudgmentResolvedEvent>().Count(e => e.Reason == "shanhe.s5.ganglie") == 2 &&
                s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Ganglie) == 2 &&
                s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 2 && e.TargetSeat == 0 && e.Amount == 2) == 1 &&
                s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 2) == (black ? 0 : 2) &&
                s.V(2).Hp == (black ? 20 : 16) &&
                s.G.CardMovements.Count(m => m.Reason.Value == DiscardReason && m.From == CardLocation.Hand(2) &&
                    m.To == CardLocation.DiscardPile) == (black ? 4 : 0) &&
                s.E<ProgramJudgmentOutcomeRecordedEvent>().Count(e => e.Outcome.Reason == "shanhe.s5.ganglie") == 2,
                "Two actual incoming points separately offer native judgments: each red result deals two damage, each black result discards two genuine source HE entities; completed costs survive retrial and paid-child recovery.");
            s.Cold();
        }
    }

    public static void ShensuThreeNativeCostsFrozenActualSkipsAndOtherSourceDedupCold()
    {
        var s = Scene.Start(shensu: true);
        for (var branch = 0; branch < 3; branch++)
        {
            s.Until(() => s.IsActivation(Shensu)); s.Activate(); s.Target(2);
            if (branch == 0)
            {
                s.Until(() => s.IsActivation(Fire)); s.Activate();
                s.Until(() => s.P?.Choices.Any(c => Action(c) == "current-slash-fire" && c.Parameters.GetValueOrDefault("branch") == "convert") == true);
                s.Answer(c => Action(c) == "current-slash-fire" && c.Parameters.GetValueOrDefault("branch") == "convert");
            }
            if (branch == 2)
            {
                s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
                Need(s.V().IsFaceDown && !s.V().Skills!.Any(k => k.ContentId == Shensu) &&
                    s.E<ShanheS5ShensuPreparedEvent>().Length == 2 &&
                    s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Shensu && f.ShanheS5Shensu is null),
                    "The third branch pays its real turnover, loses the actual Shensu source in that child, and retains its original paid frame before freezing or issuing the final Slash.");
                s.Cold(); s.Continue();
            }
            s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
            var paid = s.E<ShanheS5ShensuPreparedEvent>().Last();
            var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.SourceSeat == 0);
            Need(paid.Receipt.SkippedPhaseCount == branch + 2 &&
                use is { CardId: 0, PhysicalCardIds.Count: 0, Action.PhysicalCards.Count: 0 } &&
                use.CardKind == (branch == 0 ? CardKind.FireSlash : CardKind.Slash) &&
                use.Action!.ConversionChain.First() == paid.Receipt.Source &&
                s.IncomingPrediction(2).DamageAmount == branch + 3 &&
                use.TargetSeats.SequenceEqual([2]) && s.V().Equipment.Count == 0 && s.V().Hand.All(c => c.Kind == CardKind.Peach),
                "The cost-backed native zero-entity Slash freezes exact X=2/3/4 and crosses a genuine committed-use child; the distant target needs no equipment payment.");
            if (branch == 0) s.Cold();
            s.Continue();
        }
        s.Until(() => s.E<TurnEndedEvent>().Any(e => e.ActorSeat == 0 && e.TurnNumber == 1));
        Need(s.E<DamageAppliedEvent>().Where(e => e.SourceSeat == 0 && e.TargetSeat == 2).Select(e => e.Amount).SequenceEqual([3, 4, 5]) &&
            s.V(2).Hp == 8 && s.V().IsFaceDown && s.V().Hand.Count == 4 &&
            s.E<ProgramCurrentSlashFireChangedEvent>() is [{ OriginalKind: CardKind.Slash, FinalKind: CardKind.FireSlash }] &&
            s.E<DamageAppliedEvent>().First(e => e.SourceSeat == 0 && e.TargetSeat == 2).Nature == DamageNature.Fire &&
            s.E<ShanheS5SkippedPhaseRecordedEvent>().Count(e => e.OwnerSeat == 0 && e.ActualTurnNumber == 1) == 4 &&
            s.E<ShanheS5SkippedPhaseRecordedEvent>().Count(e => e.OwnerSeat == 0 && e.Phase == SkillProgramTurnPhase.Draw) == 1 &&
            !s.E<PhaseChangedEvent>().Any(e => e.ActorSeat == 0 && e.Phase == TurnPhase.Play) &&
            !s.E<HandLimitDiscardedEvent>().Any(e => e.ActorSeat == 0),
            "All three distinct native costs settle once; actual later Draw/Play/Discard boundaries do not recount a paid phase, and neither a normal Play nor hand-limit discard is invented.");
        s.Cold();

        var other = Scene.Start(shensu: true, external: true);
        other.Until(() => other.IsActivation(Shensu)); other.Skip();
        other.Until(() => other.IsActivation(Shensu)); other.Activate(); other.Target(2);
        other.Until(() => other.P?.SkillPrompt?.SkillId == Watch);
        Need(other.E<ShanheS5ShensuPreparedEvent>().Single().Receipt.SkippedPhaseCount == 2 &&
            other.E<ShanheS5PhaseSkipCostEvent>().Any(e => e.Phase == SkillProgramTurnPhase.Draw &&
                other.E<ProgramBindingStartedEvent>().Any(b => b.FrameId == e.FrameId && b.SkillId == External)) &&
            other.E<ShanheS5SkippedPhaseRecordedEvent>().Count(e => e.OwnerSeat == 0 && e.Phase == SkillProgramTurnPhase.Draw) == 1,
            "Skipping the first Shensu option does not erase a real other-source skipped Draw: it plus the newly paid Play yields X=2, with no actual-boundary double count.");
        other.Cold(); other.Continue(); other.Until(() => other.IsActivation(Shensu)); other.Skip();
        other.Until(() => other.E<TurnEndedEvent>().Any(e => e.ActorSeat == 0 && e.TurnNumber == 1));
        Need(other.E<DamageAppliedEvent>().Single(e => e.SourceSeat == 0 && e.TargetSeat == 2).Amount == 3 &&
            other.E<ShanheS5ShensuPreparedEvent>().Length == 1 && !other.V().IsFaceDown,
            "The single selected branch uses the native frozen bonus from two genuinely skipped phases, independent of the number of selected Shensu options.");
        other.Cold();
    }

    private static string Action(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") ?? "";
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string Peer(int seat) => "fixture:s5-xiahou-peer-" + seat;
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool IsActivation(string skill) => P?.Choices.Any(c => Action(c) == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill) == true;
        public void Accept(GameCommand command) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.ToString() ?? "Rejected native S5 Xiahou command."); }
        public void Answer(Func<PromptChoice, bool> selector) { var p = P!; var c = p.Choices.FirstOrDefault(selector); Need(c is not null, "Missing native choice: " + JsonSerializer.Serialize(p)); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Activate() => Answer(c => Action(c) == "activate");
        public void Skip() => Answer(c => Action(c) == "skip");
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Target(int target) { Until(() => P?.Choices.Any(c => Action(c) == "select-target" && c.Targets.SequenceEqual([target])) == true); Answer(c => Action(c) == "select-target" && c.Targets.SequenceEqual([target])); }
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public PublicDamagePrediction IncomingPrediction(int viewer)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var attack = typeof(GameEngine).GetProperty("ActiveCardAttack", flags)!.GetValue(G);
            return (PublicDamagePrediction)typeof(GameEngine).GetMethod("PredictPublicIncomingDamage", flags)!
                .Invoke(G, [attack, viewer, null, null, null])!;
        }
        public void Until(Func<bool> ready)
        {
            for (var step = 0; step < 180; step++)
            {
                if (ready()) return;
                Need(G.State.Winner == Winner.None, "Small S5 Xiahou native fixture ended before its expected boundary.");
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected S5 Xiahou boundary: " + JsonSerializer.Serialize(new { P, Frames = G.ResolutionStack.Select(f => f.GetType().Name), Recent = G.Events.TakeLast(4).Select(e => e.Payload.GetType().Name) }));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("S5 Xiahou native boundary missing: " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(g.CreateSnapshot(i))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Zones = g.CreateCardZoneDiagnostics() });
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State(G) == State(restored), "Cold checkpoint replay preserves every native judgment, paid movement, actual phase cost, frozen bonus and typed virtual Slash child."); G = restored;
        }
        public static Scene Start(bool shensu, bool black = false, bool external = false)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
                new StandardShanheS5XiahouPairPackage(), new Fixture(black));
            var owner = shensu ? StandardShanheS5XiahouPairPackage.XiahouyuanId : StandardShanheS5XiahouPairPackage.XiahoudunId;
            string[] extras = shensu ? external ? [Watch, External] : [Watch, Fire, Loss] : [Driver, Watch, "boundary:guidao"];
            var setup = new ShanheBattleSetup(47, 1, "native-s5-xiahou", owner, 8, 4, 0, 0, 0, extras,
                shensu ? [] : [new(CardKind.Dodge, Suit.Club, 9)], [],
                [new(Peer(2)) { StartingHand = shensu ? [] : Enumerable.Range(0, 4).Select(_ => new ShanheStartingCard(CardKind.Peach, Suit.Heart, 3)).ToArray() },
                 new(Peer(3)) { StartingHand = [] }], [new(Peer(1)) { StartingHand = [] }], new string('e', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, HumanRole = null, ModeId = ShanheBattleSetup.BattleModeId,
                ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
            var s = new Scene(g, r); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture(bool black) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-s5-xiahou", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"activations":[{"id":"actual-foreign-source","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
              "effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2,"nature":"normal"}]}]},
             {"id":"{{External}}","revision":1,"triggers":[{"id":"actual-other-draw-cost","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"priority":1000,
              "effects":[{"op":"skipTurnPhases","target":"owner","phases":["draw"]}]}]},
             {"id":"{{Fire}}","revision":1,"triggers":[{"id":"real-same-use-fire","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"optional":true,"priority":100,"usageScope":"game","usageLimit":1,
              "effects":[{"op":"offerCurrentSlashFireAndExtraTarget","target":"owner"}]}]},
             {"id":"{{Loss}}","revision":1,"triggers":[{"id":"real-rescue-and-source-loss","window":"dyingEntering","subject":"owner","optional":false,"priority":1000,"usageScope":"game","usageLimit":1,
              "effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":8,"clampToMaxHp":true},
                         {"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Shensu}}"],"sourceBind":"standard:none"}]}]},
             {"id":"{{Watch}}","revision":1,"triggers":[
              {"id":"native-use","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,
               "effects":[{"op":"chooseOption","target":"owner","resultBind":"use-seen","options":[{"id":"continue"}]}]},
              {"id":"native-turnover","window":"characterTurnedOver","subject":"owner","optional":false,
               "effects":[{"op":"loseHp","target":"owner","amount":20},
                          {"op":"chooseOption","target":"owner","resultBind":"turnover-seen","options":[{"id":"continue"}]}]},
              {"id":"paid-discard","window":"discardPileReceived","subject":"owner","discardOwnerScope":"other","movementReasons":["{{DiscardReason}}"],"movementDiscardOnly":true,"movementOccurrence":"perBatch","optional":false,
               "effects":[{"op":"chooseOption","target":"owner","resultBind":"discard-seen","options":[{"id":"continue"}]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "真实来源伤害", description = "原生伤害2" },
                    [External] = new { name = "实际其他跳阶段", description = "真实跳过摸牌" }, [Fire] = new { name = "实际同杀转火", description = "使用原生转换保留原杀" },
                    [Loss] = new { name = "真实救援与失源", description = "原生濒死救援后失去神速" },
                    [Watch] = new { name = "原生已付孩子", description = "保留实际父帧", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "真实S5技能检查") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new(Peer(seat), "实际原生目标", "supporter", "standard:none", "wei", 20));
            b.AddDeck(new(Deck, "小实际牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:peach", black ? Suit.Spade : Suit.Heart, 3)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "S5双夏侯原生流程", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [StandardShanheS5XiahouPairPackage.XiahoudunId, StandardShanheS5XiahouPairPackage.XiahouyuanId, Peer(1), Peer(2), Peer(3)],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
