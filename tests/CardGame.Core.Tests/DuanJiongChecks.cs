using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DuanJiongChecks
{
    private const string Skill = "ol:saogu", Owner = "fixture:dj-owner", Driver = "fixture:dj-driver", Quiet = "fixture:dj-quiet", Watch = "fixture:dj-watch",
        Commit = "fixture:dj-commit", Health = "fixture:dj-health", Grain = "fixture:dj-grain", Source = "fixture:dj-source", Mode = "identity:classic-duan-jiong-fixture";
    public static void YangYinOriginalSlashesForcedQuotaAndCold()
    {
        var (g, r) = Start("slashes"); Draw(g);
        Native(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]))); Play(g);
        Need(E<CardUseDebitRecordedEvent>(g).Length == 1, "A real ordinary Slash spends its allowance before the Yang sweep.");
        Begin(g); DiscardBoundary(g); var pair = Current(g)!.Candidates.Take(2).Select(c => c.CardId).ToArray();
        var p = P(g)!; var state = State(g);
        Need(p is { PlayerSeat: 0, IsPrivate: true } && g.CreateSnapshot(1).PendingDecision is null &&
            !g.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, g.Revision)).Accepted && state == State(g),
            "The accepted Yang draft is private to its payer and rejects a different seat without any payment.");
        Answer(g, c => c.Cards.SequenceEqual([pair[0]])); g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([pair[1]]));
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        Need(Yin(g) && E<DuanJiongSaoguEntityPaidEvent>(g).Length == 2 && !E<DuanJiongSaoguNativeIssuedEvent>(g).Any(),
            "Both actual entities are discarded once and Yang is consumed before the real payment child and before either native Slash.");
        g = Cold(g, r); Continue(g);
        foreach (var id in pair)
        {
            UseBoundary(g); Need(P(g)!.Choices.All(c => c.Parameters.GetValueOrDefault("branch") == "use") && P(g)!.Choices.All(c => !c.Targets.Contains(2)),
                "Every legally usable paid Slash is mandatory, and the real distance-two target is absent even though ordinary quota is exhausted.");
            Answer(g, c => c.Cards.SequenceEqual([id]) && c.Targets.SequenceEqual([1]));
            Reach(g, () => Current(g)?.Stage == DuanJiongSaoguStage.NativeUse && P(g)?.SkillPrompt?.SkillId == Commit);
            g = Cold(g, r); Continue(g);
        }
        Play(g);
        Need(E<DuanJiongSaoguNativeIssuedEvent>(g).Select(e => e.Origin.CardId).SequenceEqual(pair) && E<DuanJiongSaoguNativeReturnedEvent>(g).Length == 2 &&
            E<DuanJiongSaoguCompletedEvent>(g).Single().UsedCards == 2 && E<CardUseDebitRecordedEvent>(g).Length == 1 &&
            pair.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.DiscardPile && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1),
            "Both original discarded physical Slashes really finish, retain current distance, and neither consumes an ordinary Slash debit or uses a substitute entity.");
        var hand = V(g).HandCount; Begin(g); Reach(g, () => Current(g)?.Stage == DuanJiongSaoguStage.DrawChildren && P(g)?.SkillPrompt?.SkillId == Watch);
        Need(!Yin(g) && V(g).HandCount == hand + 1, "The Yin half draws exactly one actual card and flips to Yang before its gain child.");
        g = Cold(g, r); Continue(g); Play(g);
        Need(E<DuanJiongSaoguBranchPaidEvent>(g).Select(e => e.Yang).SequenceEqual([true, false]) && E<DuanJiongSaoguBranchPaidEvent>(g).Last().ActualCount == 1,
            "Both conversion halves have their own real once-paid receipt, without adding a once-per-phase limit to the alternating skill."); _ = Cold(g, r);
    }
    public static void AllActualDiscardsSuitEligibilityAndPhaseResetCold()
    {
        var (g, r) = Start(); Draw(g);
        var recast = V(g).Hand.First(c => c.Kind == CardKind.IronChain); var discard = V(g).Hand.First(c => c.Suit != recast.Suit && c.Kind != CardKind.Slash);
        Use(g, "trim", cards: [discard.Id]); Play(g); Accept(g, new RecastCardCommand(0, recast.Id, g.Revision, P(g)!.PromptId)); Play(g);
        Need(E<DuanJiongSaoguDiscardSuitEvent>(g).Any(e => e.CardId == discard.Id) && E<DuanJiongSaoguDiscardSuitEvent>(g).All(e => e.CardId != recast.Id),
            "A separate real skill discard is included, while the original IronChain recast is excluded from the actual phase's discard-suit history.");
        Begin(g); DiscardBoundary(g); var phase = Current(g)!.Phase;
        Need(Current(g)!.Candidates.All(c => c.Suit != discard.Suit) && Current(g)!.Candidates.Any(c => c.Suit == recast.Suit),
            "Yang rejects every card of the earlier real discarded suit but still allows the recast suit; the ledger is not restricted to Saogu's own costs.");
        var pair = Current(g)!.Candidates.Where(c => !IsSlash(c.Kind)).Take(2).Select(c => c.CardId).ToArray(); Need(pair.Length == 2, "The small real fixture supplies two non-Slash legal payments.");
        Answer(g, c => c.Cards.SequenceEqual([pair[0]])); g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([pair[1]])); Play(g);
        Need(E<DuanJiongSaoguCompletedEvent>(g).Single().UsedCards == 0 && Yin(g), "A paid Yang pair containing no Slash still executes the full payment and alternates, without fabricating a card use.");
        Begin(g); Play(g); Need(!Yin(g), "A real Yin draw restores Yang before the next actual phase.");
        End(g); Reach(g, () => g.State.TurnNumber > phase.TurnNumber && P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Begin(g); DiscardBoundary(g);
        Need(Current(g)!.Phase.Token != phase.Token && Current(g)!.Candidates.Any(c => c.Suit == discard.Suit),
            "The next genuine own Play owns a different actual phase token, so earlier phase discard suits no longer forbid current physical payments."); _ = Cold(g, r);
    }
    public static void EndingDelegationForeignPayerBothSidesAndPrivacyCold()
    {
        foreach (var yin in new[] { false, true })
        {
            var (g, r) = Start(yin ? "plain" : "delegate-yang"); Draw(g);
            if (yin) { Begin(g); DiscardBoundary(g); Pair(g, c => !IsSlash(c.Kind)); Play(g); }
            Need(Yin(g) == yin, "The ending branch comes from the genuinely consumed original conversion side.");
            var fee = V(g).Hand.First(c => V(g, 1).Hand.Count(h => h.Suit != c.Suit) >= 2); var hand = V(g, 1).HandCount;
            End(g); Reach(g, () => Optional(g)); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(g, () => Current(g)?.Stage == DuanJiongSaoguStage.DelegateChoice); var id = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.DuanJiongSaogu is not null).Id;
            g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([fee.Id]) && c.Targets.SequenceEqual([1]));
            if (!yin)
            {
                DiscardBoundary(g); var p = P(g)!; var before = State(g);
                Need(Current(g)!.ActorSeat == 1 && Current(g)!.RequiredCount == 2 && Current(g)!.Candidates.All(c => c.Suit != fee.Suit) &&
                    p is { PlayerSeat: 1, IsPrivate: true } && g.CreateSnapshot(0).PendingDecision is null &&
                    !g.Submit(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, g.Revision)).Accepted && before == State(g),
                    "After the actual source fee, the affected AI privately pays its own pair, excluding the exact source-fee suit; the human source cannot see or answer it.");
                g = Cold(g, r);
            }
            Reach(g, () => E<DuanJiongSaoguCompletedEvent>(g).Any(e => e.FrameId == id));
            var branch = E<DuanJiongSaoguBranchPaidEvent>(g).Single(e => e.FrameId == id);
            Need(Yin(g) == yin && !branch.Converted && branch.Yang == !yin && branch.ActorSeat == 1 && branch.ActualCount == (yin ? 1 : 2) &&
                E<DuanJiongSaoguEntityPaidEvent>(g).Count(e => e.FrameId == id && e.DelegateFee && e.ActorSeat == 0 && e.CardId == fee.Id) == 1 &&
                E<TurnStartedEvent>(g).All(e => e.ActorSeat != 1),
                "Both ending alternatives keep the source's conversion side unchanged, pay the source fee exactly once, and execute for the original recipient within the same actual Ending.");
            if (yin) Need(V(g, 1).HandCount == hand + 1 && E<DuanJiongSaoguNativeIssuedEvent>(g).All(e => e.Origin.ProgramFrameId != id), "The delegated Yin really draws one for the other actor and never creates a use or an extra turn.");
            else Need(E<DuanJiongSaoguNativeIssuedEvent>(g).Count(e => e.Origin.ProgramFrameId == id && e.Origin.ActorSeat == 1) == 2 &&
                E<DuanJiongSaoguNativeReturnedEvent>(g).Count(e => e.Origin.ProgramFrameId == id) == 2,
                "The foreign actor uses both of its actual discarded Slashes through complete native responses and returns, rather than borrowing the source's hand or replacing them with zero-card Slashes.");
            _ = Cold(g, r);
        }
    }
    public static void PaidLossDeathLionOxAndDelegatedSourceDeathCold()
    {
        foreach (var scenario in new[] { "paid-loss", "source-death", "lion", "ox", "yin-loss", "delegate-death" })
        {
            var (g, r) = Start(scenario); Draw(g); int? equipment = null, grain = null;
            if (scenario is "lion" or "ox")
            {
                var kind = scenario == "lion" ? CardKind.SilverLion : CardKind.WoodenOx; equipment = V(g).Hand.First(c => c.Kind == kind).Id;
                Use(g, "place", [0], [equipment.Value]); Play(g);
                if (scenario == "lion") { Use(g, "lose"); Play(g); }
                else { grain = V(g).Hand.First(c => !IsSlash(c.Kind)).Id; Accept(g, new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain.Value], [], g.Revision, P(g)!.PromptId)); Play(g); }
            }
            if (scenario == "delegate-death")
            {
                End(g); Reach(g, () => Optional(g)); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                Reach(g, () => Current(g)?.Stage == DuanJiongSaoguStage.DelegateChoice);
                var fee = V(g).Hand.First(c => V(g, 1).Hand.Count(h => h.Suit != c.Suit) >= 2).Id;
                Answer(g, c => c.Cards.SequenceEqual([fee]) && c.Targets.SequenceEqual([1]));
            }
            else
            {
                Begin(g); DiscardBoundary(g);
                if (equipment is { } equipped)
                {
                    Answer(g, c => c.Cards.SequenceEqual([equipped]));
                    Answer(g, c => c.Cards.Count == 1 && V(g).Hand.Any(h => h.Id == c.Cards[0] && !IsSlash(h.Kind)));
                }
                else Pair(g, c => scenario is "paid-loss" or "source-death" || !IsSlash(c.Kind));
                if (scenario == "yin-loss") { Play(g); Need(Yin(g), "The first real no-Slash payment reaches Yin before testing its own gain child."); Begin(g); }
            }
            var id = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.DuanJiongSaogu is not null).Id;
            var child = scenario == "lion" ? Health : scenario == "ox" ? Grain : Watch;
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == child); g = Cold(g, r); Continue(g);
            if (scenario == "paid-loss")
            {
                Reach(g, () => V(g).Skills!.All(s => s.ContentId != Skill) && Current(g)?.Stage == DuanJiongSaoguStage.UseChoice);
                g = Cold(g, r); ResolveMandatorySlashes(g, id);
            }
            Reach(g, () => E<DuanJiongSaoguCompletedEvent>(g).Any(e => e.FrameId == id));
            Need(E<DuanJiongSaoguCompletedEvent>(g).Count(e => e.FrameId == id) == 1 && E<DuanJiongSaoguBranchPaidEvent>(g).Count(e => e.FrameId == id) == 1,
                "Every genuine paid movement/HP/dying child returns to one original branch and completes once, without repeating a cost or conversion.");
            if (scenario == "paid-loss") Need(E<DuanJiongSaoguNativeIssuedEvent>(g).Count(e => e.Origin.ProgramFrameId == id) == 2 && V(g).Skills!.All(s => s.ContentId != Skill), "Actual conditional source loss leaves both paid original Slashes available for their mandatory native uses.");
            if (scenario is "source-death" or "delegate-death")
                Need(!V(g).IsAlive && g.State.Winner == Winner.None && E<DuanJiongSaoguCompletedEvent>(g).Single(e => e.FrameId == id).UsedCards == (scenario == "delegate-death" ? 2 : 0),
                    "Real damage/dying kills the non-Lord source: a dead self executor stops, while an already paid delegated living executor still completes its own native Slashes.");
            if (scenario == "lion") Need(V(g).Hp == V(g).MaxHp && g.CardMovements.Count(m => m.CardId == equipment && m.From == CardLocation.Equipment(0) && m.Reason.Value == "skill-program.duan-jiong.saogu.discard") == 1, "The real armor payment produces its native SilverLion recovery and private HP child only once.");
            if (scenario == "ox") Need(g.CardMovements.Count(m => m.CardId == grain && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1 && V(g).WoodenOxGrainCount == 0 && E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 0), "The original Ox cost disposes its real nested grain and drains the grain-movement damage subtree once.");
            if (scenario == "yin-loss") Need(V(g).Skills!.All(s => s.ContentId != Skill) && E<DuanJiongSaoguBranchPaidEvent>(g).Single(e => e.FrameId == id) is { Yang: false, ActualCount: 1 }, "Source loss inside the real Yin draw cannot repeat its draw or roll back the already paid half.");
            _ = Cold(g, r);
        }
    }
    private static void Pair(GameEngine g, Func<DuanJiongSaoguCard, bool> eligible)
    { var cards = Current(g)!.Candidates.Where(eligible).Take(2).Select(c => c.CardId).ToArray(); Need(cards.Length == 2, "Two actual legal payment entities must exist."); Answer(g, c => c.Cards.SequenceEqual([cards[0]])); Answer(g, c => c.Cards.SequenceEqual([cards[1]])); }
    private static void ResolveMandatorySlashes(GameEngine g, long frame)
    {
        for (var n = 0; n < 2; n++)
        {
            Reach(g, () => Current(g)?.Stage == DuanJiongSaoguStage.UseChoice || E<DuanJiongSaoguCompletedEvent>(g).Any(e => e.FrameId == frame));
            if (Current(g)?.Stage != DuanJiongSaoguStage.UseChoice) break;
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "use");
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static DuanJiongSaoguReceipt? Current(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.DuanJiongSaogu is not null)?.DuanJiongSaogu;
    private static bool Yin(GameEngine g) => V(g).SkillRuntimeStates!.Single(s => s.SkillId == Skill).BooleanStates!.Single().Value;
    private static bool IsSlash(CardKind kind) => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;
    private static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real Saogu command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Only the actual human answers a prompt."); var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "Missing exact Saogu choice: " + Diagnostic(g)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Continue(GameEngine g) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Begin(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, Skill, "sweep", [], [], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, int[]? targets = null, int[]? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Draw(GameEngine g) { Use(g, "draw"); Play(g); }
    private static void Native(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources ?? [] });
    private static void DiscardBoundary(GameEngine g) => Reach(g, () => Current(g)?.Stage == DuanJiongSaoguStage.DiscardChoice);
    private static void UseBoundary(GameEngine g) => Reach(g, () => Current(g)?.Stage == DuanJiongSaoguStage.UseChoice);
    private static bool Optional(GameEngine g) => P(g)?.SkillPrompt?.SkillId == Skill && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 260; step++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "Saogu ended before its boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p is { Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) Continue(g);
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is not null) throw new InvalidOperationException("Unexpected real Saogu prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing bounded Saogu boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner, Stage = Current(g)?.Stage, Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Select(c => new { c.Cards, c.Targets, c.Parameters }) } : null, Tail = g.Events.TakeLast(6).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(copy), "Cold replay preserves every private original payer, actual phase suit, converted side, exact discarded entity and native child return."); return copy; }
    private static (GameEngine, ContentRegistry) Start(string scenario = "plain")
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = scenario is "source-death" or "delegate-death" ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-duan-jiong", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:duan-jiong");
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":100,"priority":0}],"activations":[
              {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]},
              {"id":"trim","minCards":1,"maxCards":64,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trimmed"},{"op":"moveBoundCards","target":"owner","sourceBind":"trimmed","destination":"discardPile","awaitMovementTriggers":true}]}]},
             {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{{Health}}}","revision":1,"triggers":[{"id":"health","window":"afterHpRecovered","subject":"owner","hpChangeOccurrence":"perEvent","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            var names = new Dictionary<string, object> { [Driver] = new { name = "真实准备", description = "原生小夹具" }, [Quiet] = new { name = "其他实际回合", description = "仅跳出牌" },
                [Health] = new { name = "原生回复子窗", description = "真实回血", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            void Add(string id, string body) { rules["skills"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"" + id + "\",\"revision\":1," + body + "}")); names[id] = new { name = id, description = "真实子树", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }; }
            var skills = new List<string> { Driver, Health };
            if (scenario is "paid-loss" or "yin-loss")
            { rules["skills"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"" + Source + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"full\",\"skillId\":\"ol:saogu\",\"minimumHp\":9}]}")); names[Source] = new { name = "真实条件来源", description = "真实HP授予" }; skills.Add(Source); }
            else skills.Add(Skill);
            if (scenario is "slashes" or "paid-loss" or "source-death" or "delegate-death" or "yin-loss")
            {
                var gain = scenario == "yin-loss"; var tail = scenario is "source-death" or "delegate-death" ? ",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}" : scenario is "paid-loss" or "yin-loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : "";
                Add(Watch, "\"triggers\":[{\"id\":\"paid-child\",\"window\":\"" + (gain ? "cardsGained" : "cardsMoved") + "\",\"subject\":\"owner\"," + (gain ? "\"destinationZones\":[\"hand\"]," : "\"sourceZones\":[\"hand\"],") +
                    "\"movementReasons\":[\"skill-program.duan-jiong.saogu." + (gain ? "draw" : "discard") + "\"],\"movementOccurrence\":\"perBatch\",\"optional\":false," + (scenario == "slashes" ? "" : "\"usageScope\":\"game\",\"usageLimit\":1,") +
                    "\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"child\",\"options\":[{\"id\":\"continue\"}]}" + tail + "]}]"); skills.Add(Watch);
                if (scenario == "slashes") rules["skills"]!.AsArray().Last()!["triggers"]!.AsArray().Add(JsonNode.Parse("""{"id":"yin-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["skill-program.duan-jiong.saogu.draw"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw","options":[{"id":"continue"}]}]}"""));
            }
            if (scenario == "slashes")
            { Add(Commit, "\"triggers\":[{\"id\":\"native-use\",\"window\":\"cardUseCommitted\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"native\",\"options\":[{\"id\":\"continue\"}]}]}]"); skills.Add(Commit); }
            if (scenario == "ox")
            { Add(Grain, "\"triggers\":[{\"id\":\"grain\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"grain\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}]}]"); skills.Add(Grain); }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:dj-pick-owner", "本人", "固定选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            b.AddGeneral(new(Owner, "真实段颎", "ol-duan-jiong", "fixture:dj-pick-owner", "qun", 8, skills));
            var peers = Enumerable.Range(0, 3).Select(i => "fixture:dj-peer-" + i).ToArray();
            foreach (var (peer, i) in peers.Select((peer, i) => (peer, i))) { var id = "fixture:dj-pick-" + i; b.AddSkill(new(id, "其他角色", "固定顺序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 1000000d * (3 - i)) }); b.AddGeneral(new(peer, "真实其他角色", "supporter", id, "wei", 12, [Quiet])); }
            b.AddCard(new("fixture:dj-lion", "白银狮子", "装备牌", "真实回血", CardKind.SilverLion)); b.AddCard(new("fixture:dj-ox", "木牛流马", "装备牌", "真实粮", CardKind.WoodenOx));
            var ids = scenario is "slashes" or "paid-loss" or "source-death" or "delegate-death" or "delegate-yang" ? new[] { "standard:slash" } : new[] { "standard:slash", "standard:dodge", "standard:iron_chain", "fixture:dj-lion", "fixture:dj-ox" };
            var suits = new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond };
            b.AddDeck(new("fixture:dj-deck", "固定真实小牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 120).Select(i => new ContentDeckPhysicalCard(ids[i % ids.Length], suits[i / ids.Length % 4], i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "段颎原生小局", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:dj-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
