using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class QianZhaoChecks
{
    private const string Weifu = "ol:weifu", Kuansai = "ol:kuansai", Owner = "fixture:qz-owner", Driver = "fixture:qz-driver", Quiet = "fixture:qz-quiet";
    private const string Watch = "fixture:qz-watch", Replace = "fixture:qz-replace", Claim = "fixture:qz-claim", Health = "fixture:qz-health", Mode = "identity:classic-qian-zhao-fixture";
    public static void WeifuNativeJudgmentCategoryStacksAndExpiryCold()
    {
        var (g, registry) = Start();
        Need(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])), "The opposite seat begins outside the actual native Slash range.");
        var cards = V(g, 0).HandCount;
        for (var i = 0; i < 2; i++) { Begin(g); Play(g); }
        Need(E<WeifuGrantedEvent>(g).Length == 2 && E<WeifuGrantedEvent>(g).All(e => e.Category == SkillProgramCardCategory.Basic) &&
            E<JudgmentResolvedEvent>(g).Count(e => e.Reason == "weifu") == 2 && V(g, 0).HandCount == cards &&
            E<WeifuDrawPaidEvent>(g).All(e => e.Count == 1), "Two genuine HE costs and matching native judgments draw actual replacement cards and independently stack two Basic grants.");
        Native(g, a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])); Reach(g, () => Branch(g, "weifu-target"));
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.WeifuUse is not null);
        Need(use.WeifuUse!.Grants.Count == 2 && use.TargetSeats.SequenceEqual([2]) && E<WeifuGrantConsumedEvent>(g).Length == 2,
            "The accepted out-of-range native Slash freezes both grants on its actual frame and consumes them before choosing extra targets.");
        g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([1]));
        Need(P(g)!.Choices.Where(c => c.Targets.Count > 0).All(c => !c.Targets.Contains(1) && !c.Targets.Contains(2)), "A true extension excludes the original and already selected targets.");
        g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([3])); Play(g);
        Need(E<WeifuTargetAddedEvent>(g).Where(e => e.UseId == use.Id).Select(e => e.TargetSeat).SequenceEqual([1, 3]) &&
            Enumerable.Range(1, 3).All(s => E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == s && e.Amount == 1) == 1) &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1, "The actual physical Slash resolves every original and appended native response cursor once.");
        Begin(g); Play(g); Use(g, "virtual", [1]); Reach(g, () => Branch(g, "weifu-target"));
        g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([2])); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Driver && Option(g, "continue"));
        var virtualUse = E<WeifuUseIssuedEvent>(g).Last();
        Need(E<CardActionAcceptedEvent>(g).Any(e => e.Action.ActionId == virtualUse.ActionId && e.Action.PhysicalCards.Count == 0 && e.Action.ActorSeat == 0) &&
            E<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 2) == 2, "A genuine zero-entity native Slash owns the added target and returns to the paused original producer.");
        g = Cold(g, registry); Continue(g); Play(g); Begin(g); Play(g);
        var unspent = E<WeifuGrantedEvent>(g).Last(); var turn = g.State.TurnNumber; End(g); Reach(g, () => g.State.TurnNumber > turn && P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Need(E<WeifuGrantExpiredEvent>(g).Count(e => e.GrantFrameId == unspent.FrameId) == 1 &&
            !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])), "An unused grant expires at its real actual-turn boundary, while the later turn has its ordinary native range again.");
        _ = Cold(g, registry);
        {
            var (changed, cr) = Start("actor"); Begin(changed); Play(changed); Native(changed, a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
            Reach(changed, () => P(changed)?.SkillPrompt?.SkillId == "fixture:qz-role" && P(changed)!.Choices.Any(c => c.Targets.SequenceEqual([3])));
            Answer(changed, c => c.Targets.SequenceEqual([3])); Reach(changed, () => Option(changed, "continue"));
            var actual = changed.ResolutionStack.OfType<CardUseFrame>().Single(u => u.WeifuUse is not null); var receipt = actual.WeifuUse!;
            Need(actual.SourceSeat == 3 && actual.Action!.ActorSeat == 3 && actual.Action.ProviderSeat == 0 && receipt.ActorSeat == 0 &&
                E<WeifuActorChangedEvent>(changed) is [{ PreviousActor: 0, Actor: 3 }] && E<WeifuGrantConsumedEvent>(changed) is [{ ActorSeat: 0 }],
                "A genuine finalized-role producer changes the live actor and retains the original provider and already consumed grant owner.");
            changed = Cold(changed, cr); Continue(changed); Reach(changed, () => Branch(changed, "weifu-target")); Answer(changed, c => c.Targets.SequenceEqual([2])); Play(changed);
            Need(E<DamageAppliedEvent>(changed).Count(e => e.SourceSeat == 3 && e.TargetSeat is 1 or 2) == 2 &&
                E<WeifuUseIssuedEvent>(changed).Length == 1 && E<WeifuGrantConsumedEvent>(changed).Length == 1 &&
                E<CardUseFinishedEvent>(changed).Count(e => e.ResolutionId == actual.Id) == 1,
                "The same changed-source native Slash resolves both actual targets cold without creating a second actor entitlement or repeating the original grant payment.");
            _ = Cold(changed, cr);
        }
        {
            var (redirected, rr) = Start("redirect"); var redirector = Enumerable.Range(1, 3).Single(s => V(redirected, s).Skills!.Any(k => k.ContentId == "fixture:qz-redirect"));
            var primary = Enumerable.Range(1, 3).First(s => s != redirector); var hpBefore = Enumerable.Range(0, 4).Select(s => V(redirected, s).Hp).ToArray();
            Begin(redirected); Play(redirected); Native(redirected, a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([primary]));
            Reach(redirected, () => Branch(redirected, "weifu-target")); Answer(redirected, c => c.Targets.SequenceEqual([redirector]));
            Reach(redirected, () => E<WeifuRedirectedEvent>(redirected).Length == 1); var redirectedUse = redirected.ResolutionStack.OfType<CardUseFrame>().Single(u => u.WeifuUse is not null);
            var redirect = E<WeifuRedirectedEvent>(redirected).Single();
            Need(redirectedUse.Action!.TargetSeats.SequenceEqual([primary, redirector]) && redirectedUse.TargetSeats.SequenceEqual([primary, redirect.TargetSeat]) &&
                redirect.PreviousSeat == redirector && redirect.TargetIndex == 1 && redirect.TargetSeat != 0 && redirect.TargetSeat != redirector,
                "A real paid native redirect preserves the immutable designated header and changes only the current actual target cursor, proven from the frozen Weifu prefix.");
            redirected = Cold(redirected, rr); Play(redirected);
            Need(E<DamageAppliedEvent>(redirected).Length == 2 && V(redirected, primary).Hp == hpBefore[primary] - (redirect.TargetSeat == primary ? 2 : 1) &&
                (redirect.TargetSeat == primary || V(redirected, redirect.TargetSeat).Hp == hpBefore[redirect.TargetSeat] - 1) && V(redirected, redirector).Hp == hpBefore[redirector] &&
                E<CardUseFinishedEvent>(redirected).Count(e => e.ResolutionId == redirectedUse.Id) == 1,
                "The exact redirected target receives the actual native response and damage once after cold resume."); _ = Cold(redirected, rr);
        }
        {
            var (ai, ar) = Start("ai"); var seat = Enumerable.Range(0, 4).Single(s => V(ai, s).GeneralId == Owner);
            Need(!V(ai, seat).IsHuman && E<WeifuPaidEvent>(ai).Any(e => e.Source.OwnerSeat == seat), "The fixed native AI, rather than a forged human command, actually chooses and pays Weifu.");
            var aiTurn = ai.State.TurnNumber; ai = Cold(ai, ar); Reach(ai, () => E<TurnEndedEvent>(ai).Any(e => e.ActorSeat == seat));
            var costs = E<WeifuPaidEvent>(ai).Where(e => e.ActualTurn == aiTurn && e.Source.OwnerSeat == seat).ToArray();
            Need(costs.Length is >= 1 and <= 4 && E<WeifuJudgedEvent>(ai).Count(e => costs.Any(c => c.FrameId == e.FrameId)) == costs.Length &&
                E<TurnEndedEvent>(ai).Count(e => e.ActorSeat == seat) == 1 && E<WeifuGrantedEvent>(ai).Where(e => e.ActualTurn == aiTurn && e.Source.OwnerSeat == seat)
                    .All(e => E<WeifuGrantConsumedEvent>(ai).Any(c => c.GrantFrameId == e.FrameId) || E<WeifuGrantExpiredEvent>(ai).Any(c => c.GrantFrameId == e.FrameId)),
                "Public opportunity value, the real selected-card cost and diminishing same-category grants let this actual AI finish its turn without a repeated-judgment loop or stranded entitlement.");
            _ = Cold(ai, ar);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int s) => g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Branch(GameEngine g, string branch) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static bool Option(GameEngine g, string option) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option) == true;
    private static void Need(bool yes, string message) { if (!yes) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, (result.Error?.Message ?? "Rejected Qian Zhao command.") + " " + JsonSerializer.Serialize(P(g))); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Only the actual human may send an answer."); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Begin(GameEngine g, int? card = null) => Accept(g, new UseProgramSkillCommand(0, Weifu, "discard-and-judge", [card ?? V(g, 0).Hand[0].Id], [], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Native(GameEngine g, Func<LegalAction, bool> predicate) { var a = g.GetHumanLegalActions().FirstOrDefault(a => a.CardId is not null && predicate(a)) ?? throw new InvalidOperationException("Missing required native Qian Zhao action: " + Diagnostic(g)); Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource }); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 230; step++)
        {
            if (done()) return; var p = P(g); Need(g.State.Winner == Winner.None || !V(g, 0).IsAlive, "The fixed Qian Zhao fixture unexpectedly ended before its native boundary.");
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (Option(g, "continue")) Continue(g);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.Nullification or DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.RescueDying })
                Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else if (p is { PlayerSeat: 0 } && g.ResolutionStack.OfType<RecoveryReplacementFrame>().Any()) Answer(g, c => c.Parameters.GetValueOrDefault("action") == "recovery-keep" || c.Parameters.GetValueOrDefault("branch") == "keep" || c.Id.Value.Contains("keep", StringComparison.Ordinal));
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.SelectHarvestCard }) Answer(g, _ => true);
            else if (p is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected fixed Qian Zhao prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing bounded Qian Zhao boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner, Prompt = P(g),
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Role, V(g, s).GeneralId, V(g, s).Hp, V(g, s).MaxHp, V(g, s).HandCount, V(g, s).IsAlive, Skills = V(g, s).Skills?.Select(k => k.ContentId) }),
        Frames = g.ResolutionStack.Select(f => new { f.Id, Kind = f.GetType().Name }),
        Actions = g.GetHumanLegalActions().Select(a => new { a.Kind, a.CardId, a.PlayedCardKind, a.TargetSeats, a.ConversionSource }).Take(28),
        Facts = g.Events.TakeLast(12).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements,
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(cold), "Cold replay preserves every viewer, private chooser, native judgment and paid child cursor without reissuing a cost or grant."); return cold; }
    private static (GameEngine, ContentRegistry) Start(string scenario = "basic")
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var human = scenario == "ai" ? 1 : 0;
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = human, HumanRole = scenario is "death" or "recovery-replacement" or "ai" ? Role.Rebel : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral } p && p.PlayerSeat == human);
        Accept(g, new SelectGeneralCommand(human, human == 0 ? Owner : "fixture:qz-peer-1", g.Revision, P(g)!.PromptId));
        if (scenario == "ai") Reach(g, () => E<WeifuPaidEvent>(g).Length > 0); else Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-qian-zhao", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:qian-zhao");
            b.AddCard(new("fixture:qz-lion", "实际白银狮子", "装备牌", "真实装备失去回复", CardKind.SilverLion));
            if (scenario == "ai") b.AddCard(new("fixture:qz-alcohol", "真实酒", "基本牌", "固定AI牌堆", CardKind.Alcohol));
            var defs = new List<string>
            {
                $$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
                $$"""{"id":"{{Driver}}","revision":1,"viewAs":[{"id":"basic","inputKinds":["drawTwo"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false},{"id":"delayed","inputKinds":["drawTwo"],"inputSuits":[],"outputKind":"indulgence","forPlay":true,"forResponse":false},{"id":"global","inputKinds":["silverLion"],"inputSuits":[],"outputKind":"fiveGrains","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],"activations":[{"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]},{"id":"virtual","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useVirtualSlash","target":"selectedTarget"},{"op":"chooseOption","target":"owner","resultBind":"native-return","options":[{"id":"continue"}]}]},{"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},{"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"real-equipment"}]},{"id":"trim-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]}]}"""
            };
            var own = scenario == "ai" ? new List<string>() : new List<string> { Driver, Kuansai }; var peersExtra = new List<string>();
            if (scenario == "actor")
            {
                own.Add("fixture:qz-role"); defs.Add("""{"id":"fixture:qz-role","revision":1,"triggers":[{"id":"actual-actor","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"priority":1000,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"},{"op":"chooseOption","target":"owner","resultBind":"role-return","options":[{"id":"continue"}]}]}]}""");
            }
            if (scenario == "redirect") defs.Add("""{"id":"fixture:qz-redirect","revision":1,"triggers":[{"id":"actual-redirect","window":"slashTargetRedirecting","ownerRelation":"target","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"slashRedirectable"},{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand","equipment"],"count":1,"destination":"discardPile","awaitMovementTriggers":true},{"op":"redirectCurrentAttack","target":"selectedTarget"},{"op":"chooseOption","target":"owner","resultBind":"redirect-return","options":[{"id":"continue"}]}]}]}""");
            if (scenario == "loss")
            {
                own.AddRange(["fixture:qz-hp-source", Watch]); defs.Add($$"""{"id":"fixture:qz-hp-source","revision":1,"hpSkillGrants":[{"id":"healthy-weifu","skillId":"{{Weifu}}","minimumHp":4}]}""");
            }
            else own.Add(Weifu);
            if (scenario is "loss" or "death")
            {
                if (scenario == "death") own.Add(Watch);
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-paid-cost","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["skill-program.qian-zhao.weifu.discard"],"movementDiscardOnly":true,"movementOccurrence":"perBatch","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"before","options":[{"id":"continue"}]},{{(scenario == "loss" ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1},{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"after\",\"options\":[{\"id\":\"after-loss\"}]}" : "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}")}}]}]}""");
            }
            if (scenario == "claim")
            {
                own.AddRange([Replace, Claim]);
                defs.Add($$"""{"id":"{{Replace}}","revision":1,"triggers":[{"id":"replace","window":"judgmentReplacing","subject":"owner","excludedReasons":[],"optional":true,"effects":[{"op":"replaceJudgment","target":"owner","zones":["hand","equipment"],"suits":["spade","club","heart","diamond","none"],"oldCardDestination":"discardPile"}]}]}""");
                defs.Add($$"""{"id":"{{Claim}}","revision":1,"triggers":[{"id":"claim","window":"judgmentFinalized","subject":"owner","judgmentReasons":["weifu"],"suits":["spade","club","heart","diamond","none"],"minimumRank":1,"maximumRank":13,"excludedReasons":[],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"claim","options":[{"id":"continue"}]},{"op":"claimJudgmentCard","target":"owner"}]}]}""");
            }
            if (scenario is "recover" or "recovery-replacement" or "silver")
            {
                own.Add(Health); peersExtra.Add(Health); defs.Add($$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"real-health","window":"afterHealthChanged","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}""");
            }
            if (scenario == "recovery-replacement")
            {
                peersExtra.Add("fixture:qz-native-replacement"); defs.Add("""{"id":"fixture:qz-native-replacement","revision":1,"cardPolicies":[{"id":"actual-lord-replacement","kind":"redirectOwnTurnFactionRecovery","factionId":"wei","ownerRole":"lord","value":1,"providerDrawCount":1}]}""");
            }
            var labels = defs.Select(d => JsonNode.Parse(d)!).ToDictionary(n => n["id"]!.GetValue<string>(), n => new
            {
                name = n["id"]!.GetValue<string>(), description = "牵招实际命令夹具",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(key => n[key]?.AsArray() ?? new JsonArray())
                    .SelectMany(binding => binding!["effects"]!.AsArray()).SelectMany(effect => effect!["options"]?.AsArray() ?? new JsonArray())
                    .Select(option => option!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id == "after-loss" ? "继续原判定" : "继续")
            });
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',', defs)}}]}""",
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "实际牵招行为") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            const string ownerPick = "fixture:qz-owner-pick";
            b.AddSkill(new(ownerPick, "固定本人选将", "公开角色评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => scenario == "ai" && r == Role.Lord ? 1000000d : -100000d) });
            b.AddGeneral(new(Owner, "实际牵招本人", "ol-qian-zhao", ownerPick, "wei", 3, own));
            var peers = Enumerable.Range(1, 3).Select(s => $"fixture:qz-peer-{s}").ToArray();
            foreach (var (peer, index) in peers.Select((peer, index) => (peer, index)))
            {
                var pick = $"fixture:qz-peer-pick-{index}";
                b.AddSkill(new(pick, "固定其他选将顺序", "公开角色评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => scenario == "ai" && r == Role.Lord ? -1000000d : 100003d - index) });
                b.AddGeneral(new(peer, "实际另一角色", "supporter", pick, "wei", scenario == "recovery-replacement" ? 2 : 8,
                    new[] { Quiet }.Concat(peersExtra).Concat(scenario == "redirect" && index == 2 ? new[] { "fixture:qz-redirect" } : []).ToArray()));
            }
            var card = scenario is "gift" or "recover" or "recovery-replacement" ? "standard:five_grains" : scenario == "trick" ? "standard:draw_two" : scenario == "silver" ? "fixture:qz-lion" : scenario == "ai" ? "fixture:qz-alcohol" : scenario == "dodge" ? "standard:dodge" : "standard:slash";
            b.AddDeck(new("fixture:qz-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard(card, Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "牵招真实规则边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Loyalist)] = 1 }, "fixture:qz-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}




