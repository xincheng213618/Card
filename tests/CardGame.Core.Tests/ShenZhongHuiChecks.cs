using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenZhongHuiChecks
{
    private const string Owner = "fixture:shen-zh-owner", Driver = "fixture:shen-zh-driver", Watch = "fixture:shen-zh-watch", Quiet = "fixture:shen-zh-quiet", Probe = "fixture:shen-zh-probe", Skipper = "fixture:shen-zh-skipper", Stock = "fixture:shen-zh-stock";
    public static void LinjieDuzhangAndAwakeningCold()
    {
        var s = new Scene(true);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "anniversary:linjie");
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.Root?.ShenZhongHui is { Stage: ShenZhongHuiStage.RoundChoice }); s.Cold();
        var initial = s.V(1).Hp; s.Answer(c => c.Targets.SequenceEqual([1])); s.Play();
        Need(s.V(1).Hp == initial - 1 && s.Mark(1) == 1 && !s.E<LinjieRandomDiscardedEvent>().Any(), "The genuine RoundStarting choice resumes cold, deals damage first, and only then grants Lin without applying its punishment to the initial damage.");
        s.Use("trim", [1]); s.Play(); Need(s.V(1).HandCount == 1, "The real fixture trim leaves precisely one physical hand card.");
        var hp = s.V(1).Hp; s.Use("poke", [1]); s.Play();
        var payment = s.E<LinjieRandomDiscardedEvent>().Single();
        Need(payment is { TargetSeat: 1, LastHand: true } && s.V(1).Hp == hp - 2 && s.V(1).HandCount == 0 && s.Mark(1) == 0 &&
            s.G.CardMovements.Any(m => m.CardId == payment.CardId && m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.anniversary:linjie.random-discard"), "A genuine damage event randomly discards the last physical hand card, causes a separate one-point damage, and removes Lin exactly once.");
        var hand = s.V(0).HandCount;
        s.Slash(1); s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch && s.Root?.ShenZhongHui?.Operation == SkillProgramEffectOp.ResolveDuzhang); s.Cold(); s.Skip(); s.Play();
        Need(s.Mark(0) == 1 && s.V(0).HandCount == hand && s.Limit(0) == s.V(0).Hp + 1 && s.E<ShenZhongHuiDrawReturnedEvent>().Single().Requested == 1, "The first sole-target black native use draws one through its original native draw child and then grants the real hand-limit marker.");
        s.Slash(1); s.Play(); Need(s.Mark(0) == 1 && s.E<ShenZhongHuiDrawReturnedEvent>().Count() == 1, "Another sole black card in the same actual turn cannot repeat Duzhang.");
        s.Use("self", []); s.Play(); s.Use("poke", [2]); s.Play(); s.Use("poke", [3]); s.Play();
        var maximum = s.V(0).MaxHp; var turn = s.G.CreateSnapshot(0).TurnNumber; s.End();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch && s.Root?.ShenZhongHui?.Operation == SkillProgramEffectOp.AwakenJianghuo || s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && s.G.CreateSnapshot(0).TurnNumber > turn);
        Need(s.P?.SkillPrompt?.SkillId == Watch, "The next real own-turn boundary must issue Jianghuo: " + JsonSerializer.Serialize(new { Damaged = s.E<DamageAppliedEvent>().Select(e => e.TargetSeat).Distinct().ToArray(), Skills = s.V(0).Skills!.Select(k => k.ContentId).ToArray(), Turn = s.G.CreateSnapshot(0).TurnNumber })); s.Cold(); s.Skip(); s.Play();
        Need(s.V(0).MaxHp == maximum + 1 && s.Mark(0) == 1 && Enumerable.Range(1, 3).All(i => s.Mark(i) == 0) &&
            s.V(0).Skills!.Any(k => k.ContentId == "anniversary:lishi") && !s.V(0).Skills!.Any(k => k.ContentId == "anniversary:linjie") && s.E<SkillAwakenedEvent>().Count(e => e.SkillId == "anniversary:jianghuo") == 1,
            "The actual next turn awakens after every living role has truly received damage, gathers Lin, performs its native draw, raises maximum HP, and replaces Linjie with the full derived Lishi.");
        s.Replay();
    }
    public static void LishiFuturePhasesJudgmentAndDiscardCold()
    {
        var s = new Scene(false); s.Play(); s.Use("stock", []); s.Play();
        foreach (var seat in Enumerable.Range(1, 3)) { s.Use("poke", [seat]); s.Play(); }
        s.End(); s.Play(); Need(s.Mark(0) == 5 && s.V(0).Skills!.Any(k => k.ContentId == "anniversary:lishi"), "The genuine awakening retains the five real fixture-issued markers needed by all five Lishi options.");
        s.Use("refill", []); s.Play();
        foreach (var seat in Enumerable.Range(1, 3)) { s.Use("restock", [seat]); s.Play(); }
        s.End(); s.Reach(() => s.Root?.ShenZhongHui is { Stage: ShenZhongHuiStage.LishiChoice }); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("value") == "31");
        s.Reach(() => s.Root?.ShenZhongHui is { Stage: ShenZhongHuiStage.PhaseChoice, TargetSeat: 1 });
        Need(s.Mark(0) == 0 && s.E<LishiPunishmentIssuedEvent>().Count() == 15, "Five once-paid Lin create all five precise future-phase obligations for each of the three other roles."); s.Cold();
        s.Reach(() => s.E<LishiHandTypeUsedEvent>().Any(e => e.ActorSeat == 1));
        var phase = s.E<LishiHandTypeUsedEvent>().First(e => e.ActorSeat == 1).Phase;
        Need(s.V(1).HandCount > 0 && !s.Actions(1).Any(a => a.Kind == LegalActionKind.Slash) && s.E<LishiDrawPhaseSettledEvent>().Any(e => e.Phase.ActorSeat == 1 && e.DrawnCount == 2 && e.DiscardedCount == 2),
            "The actual Draw phase discards its two same-color real draws, and one actual hand-card use removes all further Basic uses despite the holder's genuine unlimited-Slash fixture capability.");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Quiet && s.G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.WindowContext is { Window: SkillProgramTriggerWindow.TurnEnding, OwnerSeat: 1 });
        Need(s.Limit(1) == s.V(1).Hp && s.E<LishiPhaseAppliedEvent>().Any(e => e.TargetSeat == 1 && e.Option == 1 && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Ending) &&
            s.E<LishiDiscardRedirectedEvent>().Count(e => e.TargetSeat == 1) == 3 && s.AtomicRedirectOwners.Contains(1), "The exact next Ending phase suppresses the real nonlocked zero-hand-limit modifier, while the preceding Discard phase has already redirected the three genuine discarded entities in one native batch into the source's hand.");
        s.Play();
        Need(s.Limit(1) == 0 && s.E<LishiJudgmentReturnedEvent>().Count() == 6 && s.E<LishiDrawPhaseSettledEvent>().Count() == 3 &&
            s.E<LishiDiscardRedirectedEvent>().Count() == 9 && Enumerable.Range(1, 3).All(i => s.E<LishiHandTypeUsedEvent>().Count(e => e.ActorSeat == i) == 1 && s.AtomicRedirectOwners.Contains(i)),
            "All three roles complete two distinct native judgments, once-only phase draw/hand-type effects and real discard replacement; the source's next turn restores their ordinary nonlocked modifiers.");
        var before = s.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Nature == DamageNature.Thunder && e.SourceLess);
        s.End(); s.Reach(() => s.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Nature == DamageNature.Thunder && e.SourceLess) > before);
        Need(s.Mark(0) == 0, "With no remaining Lin, the actual Ending phase inflicts a genuine source-less thunder damage instead of issuing new phase obligations.");
        s.Replay();
    }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Scene
    {
        public GameEngine G; private readonly ContentRegistry _registry;
        public HashSet<int> AtomicRedirectOwners { get; } = [];
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ShenZhongHui is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public IEnumerable<T> E<T>() where T : IGameEvent => G.Events.Select(e => e.Payload).OfType<T>();
        public int Mark(int seat) => V(seat).Markers?.Where(m => m.Kind == PlayerMarkerKind.LinCold).Sum(m => m.Count) ?? 0;
        private System.Collections.IList Players => (System.Collections.IList)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(G)!;
        public int Limit(int seat) => (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(G, [Players[seat]])!;
        public IReadOnlyList<LegalAction> Actions(int seat) => (IReadOnlyList<LegalAction>)typeof(GameEngine).GetMethod("BuildLegalActions", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(G, [Players[seat], true, null])!;
        public Scene(bool black)
        {
            _registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(black));
            G = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:classic-shen-zh-fixture", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, _registry);
            Accept(new StartGameCommand()); Reach(() => P is { Kind: DecisionKind.SelectGeneral }); Accept(new SelectGeneralCommand(0, Owner, G.Revision, P!.PromptId));
        }
        private void Accept(GameCommand c) { var r = G.Submit(c); Need(r.Accepted, r.Error?.Message ?? "Native command rejected."); }
        public void Answer(Func<PromptChoice, bool> choose) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, G.Revision)); }
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Use(string id, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets, G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Slash(int target)
        {
            var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]) && a.ConversionSource is null);
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        private void ObserveRedirectBatches()
        {
            foreach (var window in G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>())
            {
                if (window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
                    window.Candidates[window.CandidateIndex] is not { SkillId: Watch, OwnerSeat: 0, BindingId: "native-draw-child" }) continue;
                var batch = window.Batch;
                var receipts = E<LishiDiscardRedirectedEvent>().Where(e => batch.Movements.Any(m => m.Sequence == e.MovementSequence)).ToArray();
                if (receipts.Length == 0) continue;
                var seat = receipts[0].TargetSeat;
                Need(batch.Id > 0 && receipts.Length == 3 && batch.Movements.Count == 3 &&
                    receipts.All(e => e.TargetSeat == seat && e.SourceSeat == 0) && batch.Movements.All(m => m.From == CardLocation.Hand(seat) && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.HandLimitDiscard),
                    "Each holder's three actual Lishi hand-limit discards must retain one native per-batch movement window and its exact three redirection receipts.");
                AtomicRedirectOwners.Add(seat);
            }
        }
        public void Reach(Func<bool> done)
        {
            for (var n = 0; n < 1400; n++)
            {
                ObserveRedirectBatches();
                if (done()) return;
                if (P is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Skip();
                else if (P is { PlayerSeat: 0 } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "select-god-faction")) Answer(c => c.Parameters.GetValueOrDefault("faction-id") == "wei");
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying }) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Shen Zhong Hui boundary not reached: " + JsonSerializer.Serialize(new { Prompt = P, Frames = G.ResolutionStack }));
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Moves = G.CardMovements });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), _registry); Need(before == State(), "Cold restore must preserve the original Shen Zhong Hui/phase/judgment parent and exact accepted entity payments."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, _registry); Need(before == State(), "Accepted-command replay preserves every original marker, judgment and entity transfer."); }
    }
    private sealed class Fixture(bool black) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shen-zh", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var json = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"modifiers":[{"id":"unlimited","query":"slashLimit","operation":"unlimited","value":0,"priority":0}],"activations":[{"id":"trim","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardParticipantCards","target":"owner","zones":["hand"],"amount":3}]},{"id":"poke","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},{"id":"self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},{"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},{"id":"refill","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":20}]},{"id":"restock","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":4}]}]},{"id":"{{Watch}}","revision":1,"triggers":[{"id":"native-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:duzhang.draw","skill-program.anniversary:jianghuo.draw","rule.hand-limit-discard"],"optional":true,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-pause","options":[{"id":"continue"}]}]}]},{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"ending-pause","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"ending-pause","options":[{"id":"continue"}]}]}]},{"id":"{{Probe}}","revision":1,"modifiers":[{"id":"real-empty-limit","query":"handLimit","operation":"set","value":0,"priority":0},{"id":"real-unlimited-slash","query":"slashLimit","operation":"unlimited","value":0,"priority":0},{"id":"real-range","query":"attackRange","operation":"add","value":10,"priority":0}]},{"id":"{{Skipper}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},{"id":"{{Stock}}","revision":1,"triggers":[{"id":"real-damage-markers","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"changeAttributedMarker","target":"owner","targetRef":{"kind":"owner"},"marker":"linCold","amount":5}]}]}]}""";
            var ids = new[] { Driver, Watch, Quiet, Probe, Skipper, Stock };
            var catalog = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = ids.ToDictionary(id => id, id => new { name = "原生检查", description = "真实手牌、技能资格、阶段、伤害与标记驱动。", optionLabels = id is Watch or Quiet ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id], Tags = id is Watch or Quiet or Skipper ? SkillTag.Locked : SkillTag.None });
            b.AddGeneral(new(Owner, "神钟会夹具", "supporter", "anniversary:linjie", "god", 20, black ? ["anniversary:duzhang", "anniversary:jianghuo", Driver, Watch] : ["anniversary:duzhang", "anniversary:jianghuo", Driver, Watch, Stock], GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:shen-zh-peer-" + i).ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "安静同桌", "supporter", Quiet, "wei", 20, black ? [Probe, Skipper] : [Probe]));
            b.AddDeck(new("fixture:shen-zh-deck", "固定真实杀", 4, black ? 0 : 2, []) { PhysicalCards = Enumerable.Range(0, 160).Select(_ => new ContentDeckPhysicalCard("standard:slash", black ? Suit.Spade : Suit.Heart, 5)).ToArray() });
            b.AddMode(new("identity:classic-shen-zh-fixture", "神钟会原生检查", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:shen-zh-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
