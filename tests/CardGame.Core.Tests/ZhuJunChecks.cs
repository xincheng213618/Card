using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhuJunChecks
{
    private const string Cuipo = "ol:cuipo", Owner = "fixture:zj-owner", Driver = "fixture:zj-driver", Quiet = "fixture:zj-quiet", Watch = "fixture:zj-watch", Health = "fixture:zj-health", Conditional = "fixture:zj-conditional", Rescue = "fixture:zj-rescue", Legacy = "fixture:zj-legacy", Mode = "identity:classic-zhu-jun-fixture";
    public static void CuipoNativeSlashEffectiveNamesDuelAndChainCold()
    {
        foreach (var scenario in new[] { "slash", "legacy", "fire", "thunder" })
        {
            var (g, r) = Start(scenario); var target = 1;
            if (scenario is "fire" or "thunder") { Use(g, "chain", [target]); Play(g); Use(g, "chain", [2]); Play(g); }
            var hp = V(g, target).Hp; var other = V(g, 2).Hp;
            if (scenario == "legacy") Answer(g, c => c.Targets.SequenceEqual([target])); else Native(g, a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]));
            Play(g); var issued = E<ZhuJunCuipoQualifiedEvent>(g).Single(e => e.Source.OwnerSeat == 0);
            Need(issued.Ordinal == 1 && issued.NameLength == 1 && issued.Damage && V(g, target).Hp == hp - 2,
                "The first actual native/legacy/elemental Slash uses the effective one-character name and receives one original-use increment: " + scenario);
            if (scenario is "fire" or "thunder") Need(V(g, 2).Hp == other - 2 && E<DamageAppliedEvent>(g).Select(e => e.Amount).SequenceEqual([2, 2]), "The true elemental propagation inherits two and never reapplies Cuipo on a later chained recipient.");
            if (scenario == "legacy") Need(issued.ActionId is null && E<CurrentTurnCardUseKindsRecordedEvent>(g).Single(e => e.ActorSeat == 0).CardActionId is null, "A real Action-null zero-entity Slash occupies its actual turn slot and receives the same damage rule.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("duel"); Use(g, "wound-peer", [1]); Play(g); NativeKind(g, CardKind.Alcohol); Play(g); var hp = V(g).Hp;
            NativeKind(g, CardKind.Duel, [1]); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
            g = Cold(g, r); Answer(g, c => c.Cards.Count == 0); Play(g);
            Need(E<ZhuJunCuipoQualifiedEvent>(g).Any(e => e.EffectiveKind == CardKind.Duel && e.Ordinal == 2 && e.Damage) && V(g).Hp == hp - 2 && E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 2),
                "The second-use Duel remains an enhanced card when the actual opposing Duel responder becomes the damage source."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("multi"); Use(g, "wound"); Play(g); Need(V(g).Skills!.All(s => s.Id != Cuipo), "The original HP-dependent source is actually absent before acquisition.");
            Use(g, "grant"); Play(g); Use(g, "heal"); Play(g); NativeKind(g, CardKind.Alcohol); Play(g);
            Need(E<ZhuJunCuipoDrawIssuedEvent>(g).Count() == 2 && E<ZhuJunCuipoQualifiedEvent>(g).Where(e => !e.Damage).Select(e => e.Source.SkillInstanceId).Distinct().Count() == 2,
                "Restoring the old conditional source alongside a newly acquired source follows native per-instance trigger semantics, without overwriting either draw.");
            var hp = V(g, 1).Hp; NativeKind(g, CardKind.Duel, [1]); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health && g.ResolutionStack.OfType<CardUseFrame>().Any(u => u.ZhuJunCuipoDamage?.Grants.Count == 2));
            Need(P(g)!.PlayerSeat == 1 && g.CreateSnapshot(0).PendingDecision is null, "The actual damaged AI owns the genuine private HP child while both original-use source receipts remain frozen.");
            g = Cold(g, r); Play(g); Need(V(g, 1).Hp == hp - 3 && E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 3) && E<ZhuJunCuipoQualifiedEvent>(g).Count(e => e.Damage) == 2, "Two distinct issued original-source bonuses remain frozen on one native Duel and add exactly twice."); _ = Cold(g, r);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(command); Need(r.Accepted, r.Error?.Message ?? "Rejected native command"); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Need(p.PlayerSeat == 0, "Only the real human answers; AI choices advance natively."); Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string id, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Native(GameEngine g, Func<LegalAction, bool> predicate)
    { var a = g.GetHumanLegalActions().First(predicate); Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource }); }
    private static void NativeKind(GameEngine g, CardKind kind, int[]? targets = null) => Native(g, a => (a.PlayedCardKind ?? V(g).Hand.FirstOrDefault(c => c.Id == a.CardId)?.Kind) == kind && (targets is null || a.TargetSeats.SequenceEqual(targets)));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 700; i++)
        {
            if (done()) return; if (g.State.Status == EngineStatus.Completed) throw new InvalidOperationException("Cuipo game ended before boundary: " + Diagnostic(g)); var p = P(g);
            if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId == Rescue && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying } rescue && rescue.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "program-trigger" && c.Parameters.GetValueOrDefault("skill-id") == Rescue)) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "program-trigger" && c.Parameters.GetValueOrDefault("skill-id") == Rescue);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId == Health) Continue(g);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.SelectTargetCard }) Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == "0" || c.Cards.Count == 1);
            else if (p is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Cuipo prompt: " + Diagnostic(g)); else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing Cuipo actual boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase, Prompt = P(g) is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(5).Select(c => new { c.Targets, c.Parameters }) } : null, Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).HandCount, V(g, s).IsAlive }), Frames = g.ResolutionStack.Select(f => new { f.Id, Kind = f.GetType().Name }), Tail = g.Events.TakeLast(10).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var c = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(c), "Cold replay preserves exact actual-use ordinals, issued native damage, private draw children and typed legacy recovery without repaying."); return c; }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario == "paid-death" ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); if (scenario == "legacy") Reach(g, () => P(g)?.SkillPrompt?.SkillId == Legacy); else Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zhu-jun", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:zhu-jun");
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":60,"priority":0}],"viewAs":[],"activations":[
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]},
                {"id":"incoming-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Cuipo}}"],"sourceBind":"standard:none"}]},
                {"id":"grant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Cuipo}}"]}]},
                {"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"wound-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
                {"id":"dying-damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":20}]},
                {"id":"heal","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":1}]},
                {"id":"trim","minCards":1,"maxCards":1,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trimmed"},{"op":"moveBoundCards","target":"owner","sourceBind":"trimmed","destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"dying","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]},
              {"id":"{{Legacy}}","revision":1,"triggers":[{"id":"true-action-null-slash","window":"playPhaseStarting","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":false}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-cuipo","skillId":"{{Cuipo}}","minimumHp":9}]},
              {"id":"{{Rescue}}","revision":1,"triggers":[{"id":"true-self-wine","window":"selfDyingResponse","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"useVirtualDyingAlcohol","target":"owner"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.zhu-jun.cuipo.draw"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]},
              {"id":"{{Health}}","revision":1,"triggers":[{"id":"true-hp-child","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            var view = rules["skills"]![0]!["viewAs"]!.AsArray();
            foreach (var kind in new[] { CardKind.Alcohol, CardKind.Duel, CardKind.IronChain, CardKind.DrawTwo, CardKind.BorrowedSword, CardKind.ArrowBarrage, CardKind.UnexpectedAssault, CardKind.Slash })
            {
                var node = JsonSerializer.SerializeToNode(new { id = "native-" + kind, inputKinds = Array.Empty<string>(), inputSuits = Array.Empty<string>(), outputKind = char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..], forPlay = true, forResponse = false, allowSameKind = true });
                if (kind is CardKind.Duel or CardKind.IronChain or CardKind.DrawTwo or CardKind.BorrowedSword or CardKind.ArrowBarrage) node!["singleCardTrickUse"] = true;
                view.Add(node);
            }
            if (scenario is "paid-loss" or "paid-death") rules["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Watch)!["triggers"]![0]!["effects"]!.AsArray().Add(JsonNode.Parse("{\"op\":\"damage\",\"target\":\"owner\",\"amount\":" + (scenario == "paid-death" ? "20" : "1") + "}"));
            var ids = new[] { Driver, Quiet, Conditional, Rescue, Watch, Health, Legacy };
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = ids.ToDictionary(id => id, id => new { name = id, description = "朱儁真实原生规则边界", optionLabels = id is Watch or Health ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "实际边界") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            var human = scenario == "paid-death" ? Role.Renegade : Role.Lord;
            b.AddSkill(new("fixture:zj-owner-pick", "本人公开评分", "固定本人") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == human ? 100000d : -100000d) });
            var skills = new List<string> { Driver, scenario is "paid-loss" or "multi" ? Conditional : Cuipo };
            if (scenario == "legacy") skills.Add(Legacy);
            if (scenario is "paid-loss" or "paid-death" or "reshuffle" or "legacy-alcohol" or "legacy-alcohol-damage") skills.Add(Watch);
            if (scenario is "paid-loss" or "paid-death") skills.Add(Health); if (scenario.StartsWith("legacy-alcohol", StringComparison.Ordinal)) skills.Add(Rescue);
            b.AddGeneral(new(Owner, "朱儁实际本人", "ol-zhu-jun", "fixture:zj-owner-pick", "qun", 8, skills));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:zj-peer-" + i).ToArray();
            foreach (var (peer, index) in peers.Select((peer, index) => (peer, index)))
            { var pick = "fixture:zj-pick-" + index; b.AddSkill(new(pick, "其他公开评分", "固定顺序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == human ? -100000d : 100003d - index) }); b.AddGeneral(new(peer, "其他实际角色", "supporter", pick, "qun", 12, scenario == "multi" ? [Quiet, Health] : [Quiet])); }
            b.AddCard(new("fixture:zj-fire", "火杀", "基本牌", "真实属性杀", CardKind.FireSlash)); b.AddCard(new("fixture:zj-thunder", "雷杀", "基本牌", "真实属性杀", CardKind.ThunderSlash)); b.AddCard(new("fixture:zj-gear", "麒麟弓", "装备牌", "真实长距离武器", CardKind.QilinBow));
            var count = scenario == "reshuffle" ? 18 : 80;
            b.AddDeck(new("fixture:zj-deck", "小固定实体牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, count).Select(i => new ContentDeckPhysicalCard(
                scenario == "fire" ? "fixture:zj-fire" : scenario == "thunder" ? "fixture:zj-thunder" : scenario is "history" or "dodge" or "multi" ? "standard:dodge" : scenario == "counterspell" ? "standard:nullification" : scenario == "lightning" ? "standard:lightning" : scenario is "equipment" or "borrowed" && i % 4 == 0 ? "fixture:zj-gear" : "standard:slash", (Suit)(i % 4), i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "朱儁原生规则", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:zj-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
