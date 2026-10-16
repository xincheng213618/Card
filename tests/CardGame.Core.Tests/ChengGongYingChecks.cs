using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ChengGongYingChecks
{
    private const string Skill = "ol:kuangxiang", Hongji = "ol:hongji", Owner = "fixture:cgy-owner", Driver = "fixture:cgy-driver", Quiet = "fixture:cgy-quiet", Watch = "fixture:cgy-watch", Health = "fixture:cgy-health", Conditional = "fixture:cgy-conditional", Phase = "fixture:cgy-phase", Prime = "fixture:cgy-prime", Boundary = "fixture:cgy-boundary", Mode = "identity:classic-cheng-gong-ying-fixture", Legacy = "identity:cheng-gong-ying-legacy-fixture";
    private const string DrawReason = "skill-program.cheng-gong-ying.kuangxiang.draw", DiscardReason = "skill-program.cheng-gong-ying.kuangxiang.discard";
    public static void PreparationAdjustmentsPrivateDiscardExactGainAndCold()
    {
        {
            var (g, r) = Start("black"); Need(V(g).HandCount == 2 && V(g).Hp == 9 && V(g).MaxHp == 9, "A genuine initial hand and native Lord HP are present before optional Preparation.");
            Activate(g); Need(Current(g) is { Stage: ChengGongYingStage.TargetChoice } && P(g)!.Choices.SelectMany(c => c.Targets).Order().SequenceEqual([0, 1, 2, 3]), "The actual optional Preparation can select any living character, including itself.");
            g = Cold(g, r); Target(g, 0); Play(g);
            var draw = E<KuangxiangAdjustmentIssuedEvent>(g).Single(); var gained = E<KuangxiangCardObtainedEvent>(g);
            Need(draw is { Draw: true, Recipient: 0, ActualCount: 2 } && gained.Length == 2 && gained.All(e => e.EffectiveSuit == Suit.Spade && g.CardMovements.Any(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == DrawReason)) && V(g).HandCount == 6,
                "Only the two real adjustment draws receive entity invoices; the later normal Draw2 remains independent."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("discard"); Activate(g); Target(g, 1); Reach(g, () => Current(g) is { Stage: ChengGongYingStage.DiscardChoice });
            var prompt = P(g)!; Need(prompt.PlayerSeat == 1 && prompt.IsPrivate && g.CreateSnapshot(0).PendingDecision is null && g.CreateSnapshot(2).PendingDecision is null, "The foreign target chooses its own hand discards privately; the source cannot inspect its hand selection.");
            var before = State(g); Need(!g.Submit(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, g.Revision)).Accepted && State(g) == before, "The source cannot answer a private foreign discard choice.");
            g = Cold(g, r); Play(g); Need(V(g, 1).HandCount == 4 && E<KuangxiangAdjustmentIssuedEvent>(g).Single() is { Draw: false, ActualCount: 2 } && g.CardMovements.Count(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile && m.Reason.Value == DiscardReason) == 2 && E<KuangxiangCardObtainedEvent>(g).Length == 0,
                "The native target really discards its own excess hand cards simultaneously, without inventing obtained marks or touching equipment."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("equal"); Activate(g); Target(g, 0); Play(g);
            Need(E<KuangxiangAdjustmentIssuedEvent>(g).Single() is { Draw: false, ActualCount: 0 } && E<KuangxiangCardObtainedEvent>(g).Length == 0 && V(g).HandCount == 6, "Choosing an existing four-card hand is legal, with zero physical adjustment and normal Draw2 afterwards."); _ = Cold(g, r);
            (g, r) = Start("black"); Skip(g); Play(g); Need(E<ChengGongYingStartedEvent>(g).Length == 0 && V(g).HandCount == 4, "The current-client optional wording permits a genuine decline without issuing any adjustment receipt."); _ = Cold(g, r);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static ChengGongYingReceipt? Current(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ChengGongYing is not null)?.ChengGongYing;
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Cheng Gong Ying command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Only answer the actual human; never submit AI responses."); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static bool Optional(GameEngine g) => P(g)?.SkillPrompt?.SkillId == Skill && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool OptionalHongji(GameEngine g) => P(g)?.SkillPrompt?.SkillId == Hongji && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Skip(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Target(GameEngine g, int target) => Answer(g, c => c.Targets.SequenceEqual([target]));
    private static void Continue(GameEngine g) { var p = P(g)!; if (V(g, p.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var n = 0; n < 500; n++)
        {
            if (done()) return; if (g.State.Status == EngineStatus.Completed) throw new InvalidOperationException("Cheng Gong Ying ended before actual boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) End(g);
            else if (p is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification, PlayerSeat: 0 }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (Optional(g)) Skip(g);
            else if (OptionalHongji(g)) Skip(g);
            else if (p?.SkillPrompt?.SkillId == Boundary) Continue(g);
            else if (p?.SkillPrompt?.SkillId == Prime) Answer(g, _ => true);
            else if (p is not null) throw new InvalidOperationException("Unexpected Cheng Gong Ying prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing Cheng Gong Ying actual boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase, g.State.Winner, Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).HandCount, V(g, s).IsAlive }), Prompt = P(g) is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(3).Select(c => c.Parameters) } : null, Frames = g.ResolutionStack.TakeLast(5).Select(f => new { f.Id, Type = f.GetType().Name }), Facts = g.Events.TakeLast(10).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(cold), "Cold replay preserves all four privacy views, physical obtained-card invoices, next actual Draw obligations and exact paid native children without duplicate cost."); return cold; }
    private static (GameEngine, ContentRegistry) Start(string scene)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "source-death" ? Role.Renegade : Role.Lord, ModeId = scene.EndsWith("legacy", StringComparison.Ordinal) ? Legacy : Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Reach(g, () => Optional(g) || scene == "hongji" && OptionalHongji(g)); return (g, registry);
    }
    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-cheng-gong-ying", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:cheng-gong-ying");
            if (scene == "hongji") CharacterFixtureContent.Register(builder, "ol:zhang-shi-ping");
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"real-slash-room","query":"slashLimit","operation":"add","value":8,"priority":0},{"id":"real-distance","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0},{"id":"keep-real-hand","query":"handLimit","operation":"add","value":20,"priority":0}],"viewAs":[{"id":"one-real-entity","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true},{"id":"two-real-entities","inputKinds":[],"inputSuits":[],"inputCount":2,"sourceZones":["hand"],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true}],"activations":[
              {"id":"pair","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two-real-entities","outputKind":"slash"}]},
              {"id":"trim","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
              {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
              {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Health}}","revision":1,"triggers":[{"id":"real-health-pause","window":"{{(scene=="loss"?"afterHpRecovered":"afterDamageApplied")}}","subject":"owner",{{(scene=="loss"?"":"\"damageOccurrence\":\"perDamage\",")}}"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]},
             {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"real-healthy-source","skillId":"{{Skill}}","minimumHp":8}]},
             {"id":"{{Prime}}","revision":1,"triggers":[{"id":"real-small-deck-emptying","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":4},{"op":"selectOwnedCards","target":"owner","zones":["hand"],"amount":4,"resultBind":"original"},{"op":"moveBoundCards","target":"owner","sourceBind":"original","destination":"discardPile","awaitMovementTriggers":true}]}]},
             {"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-paid-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{DrawReason}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}{{(scene=="loss"?",{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1},{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}":",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}")}}]}]}
            ]}
            """)!;
            if (scene == "skip") rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Phase}}","revision":1,"triggers":[{"id":"native-skip-draw","window":"turnStartBeforeNormalFlow","subject":"owner","priority":1000,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["draw"]}]}]}"""));
            if (scene == "replacement") rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Phase}}","revision":1,"triggers":[{"id":"draw-three","window":"drawPhaseStarting","subject":"owner","optional":false,"drawPhaseMode":"replacement","usageScope":"game","usageLimit":1,"effects":[{"op":"draw","target":"owner","amount":3}]}]}"""));
            if (scene == "hongji")
            {
                var effects = rules["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Prime)!["triggers"]![0]!["effects"]!.AsArray(); effects.RemoveAt(0); effects[0]!["amount"] = 2;
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Boundary}}","revision":1,"triggers":[{"id":"real-hongji-draw-ended-child","window":"drawPhaseEnded","subject":"owner","priority":-1000,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"return","options":[{"id":"continue"}]}]}]}"""));
            }
            var labels = rules["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "成公英真实原生边界夹具", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })); foreach (var (id, p) in catalog.Programs) builder.AddSkill(new(id, id, "实际原生边界") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            var role = scene == "source-death" ? Role.Renegade : Role.Lord; builder.AddSkill(new("fixture:cgy-pick", "固定本人", "真实公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == role ? 100000d : -100000d) });
            var own = new List<string> { Driver, scene == "loss" ? Conditional : Skill }; if (scene is "loss" or "source-death") own.Add(Watch); if (scene is "loss" or "lightning") own.Add(Health); if (scene is "reshuffle" or "hongji") own.Add(Prime); if (scene == "hongji") { own.Add(Hongji); own.Add(Boundary); }
            builder.AddGeneral(new(Owner, "实际成公英", "ol-cheng-gong-ying", "fixture:cgy-pick", "qun", 8, own) { InitialHp = scene == "loss" ? 7 : 8 }); var peers = Enumerable.Range(1, 3).Select(i => "fixture:cgy-peer-" + i).ToArray();
            foreach (var (id, index) in peers.Select((id, index) => (id, index)))
            {
                var pick = "fixture:cgy-pick-" + index; builder.AddSkill(new(pick, "固定其他角色", "真实公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == role ? -100000d : 100003d - index) });
                var skills = new List<string> { Quiet }; if (index == 0 && scene is "skip" or "replacement") skills.Add(Phase); if (index == 0 && scene == "stack") skills.Add(Skill); if (index == 0 && scene == "recipient-death") skills.Add(Watch); if (index == 0 && scene.StartsWith("chain-", StringComparison.Ordinal)) skills.Add(Health);
                builder.AddGeneral(new(id, "实际其他角色", "supporter", pick, "wei", 8, skills));
            }
            var kind = scene switch { "duel" => CardKind.Duel, "aoe" => CardKind.ArrowBarrage, "conversion" => CardKind.Crossbow, "chain-formal" or "chain-legacy" => CardKind.ThunderSlash, "lightning" => CardKind.Lightning, _ => CardKind.Slash };
            builder.AddCard(new("fixture:cgy-card", "真实固定实体", kind is CardKind.Slash or CardKind.ThunderSlash ? "基本牌" : kind == CardKind.Crossbow ? "装备牌" : "锦囊牌", "原生牌实体", kind));
            builder.AddDeck(new("fixture:cgy-deck", "成公英固定小牌堆", scene == "discard" ? 6 : scene is "equal" or "hongji" ? 4 : 2, 2, []) { PhysicalCards = Enumerable.Range(0, scene == "reshuffle" ? 12 : scene == "empty-deck" ? 8 : 80).Select(i => new ContentDeckPhysicalCard("fixture:cgy-card", scene is "red" or "stack" or "skip" or "replacement" or "hongji" ? Suit.Heart : Suit.Spade, scene == "lightning" ? 5 : i % 13 + 1)).ToArray() });
            builder.AddMode(new(scene.EndsWith("legacy", StringComparison.Ordinal) ? Legacy : Mode, "成公英实际原生规则", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:cgy-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
