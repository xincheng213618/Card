using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HuJinDingChecks
{
    private const string Qingyuan = "ol:qingyuan", Zhongshen = "ol:zhongshen", Owner = "fixture:hjd-owner", Driver = "fixture:hjd-driver", Watch = "fixture:hjd-watch", Health = "fixture:hjd-health", Mode = "identity:classic-hu-jin-ding-fixture";
    public static void QingyuanOpeningFirstDamageRandomMarkedHandsAndQuotaCold()
    {
        var (g, r) = Start(opening: true);
        Need(Partner(g) && P(g)!.Choices.Count == 3 && P(g)!.Choices.All(c => c.Cards.Count == 0 && !c.Targets.Contains(0)) &&
            g.CreateSnapshot(0).PendingDecision?.Choices.Count == 3 && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null),
            "Opening Qingyuan is mandatory, with exactly the three other live people and no hidden-card choice; only its native chooser receives the pending prompt.");
        g = Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([1])); Play(g);
        Need(Enumerable.Range(0, 4).All(s => g.CreateSnapshot(s).Players[1].Markers!.Any(m => m.Kind == PlayerMarkerKind.QingyuanPartner)), "The original partner is publicly marked for every viewer.");
        Use(g, "lose", [0]); Play(g); Need(E<QingyuanPartnerSelectedEvent>(g).Length == 1 && E<QingyuanFirstDamageEvent>(g).Length == 0, "Losing HP is not first actual damage.");
        Use(g, "damage-two", [0]); Reach(g, () => Partner(g));
        Need(P(g)!.Choices.SelectMany(c => c.Targets).Order().SequenceEqual([2, 3]) && E<QingyuanFirstDamageEvent>(g) is [{ Amount: 2 }], "One real two-point damage instance chooses one new partner and excludes the first partner.");
        g = Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([2])); Play(g);
        Use(g, "damage-one", [0]); Play(g); Need(E<QingyuanPartnerSelectedEvent>(g).Length == 2, "Later damage cannot select a third person.");
        var peerHands = new[] { 1, 2 }.ToDictionary(s => s, s => V(g, s).Hand.Select(c => c.Id).ToArray());
        var before = V(g, 0).HandCount; Use(g, "draw-other", [1]); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Qingyuan is not null); var paid = E<QingyuanTakePaidEvent>(g).Single();
        Need(root.Qingyuan!.HolderCandidates!.SequenceEqual([1, 2]) && root.Qingyuan.HandCandidates!.Count >= peerHands[paid.HolderSeat].Length &&
            root.Qingyuan.HandCandidates.Contains(paid.CardId) && V(g, 0).HandCount == before + 1 &&
            V(g, 0).Hand.Single(c => c.Id == paid.CardId).SkillMarkLabel == "重身" && Enumerable.Range(1, 3).All(s =>
                g.CreateSnapshot(s).Players[0].Hand.Count == 0 && !g.CreateSnapshot(s).PublicRevealedCards.Any(c => c.Id == paid.CardId)),
            "A marked person's actual hand gain freezes both eligible holders and the selected entire private hand, then pays one opaque native transfer before its real gain child.");
        g = Cold(g, r); Continue(g); Play(g); Use(g, "draw-other", [2]); Play(g);
        Need(E<QingyuanTakePaidEvent>(g).Length == 1, "The other marked person's second gain in the same actual turn does not pay another take.");
        End(g); Reach(g, () => E<QingyuanTakePaidEvent>(g).Length == 2 && P(g)?.SkillPrompt?.SkillId == Watch);
        Need(E<QingyuanTakePaidEvent>(g).Last().ActualTurn != paid.ActualTurn, "The next real turn refreshes Qingyuan, including the native normal Draw.");
        _ = Cold(g, r);
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Partner(GameEngine g) => Action(g, "qingyuan-partner");
    private static bool Action(GameEngine g, string action) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action) == true;
    private static bool Option(GameEngine g, string option) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option) == true;
    private static void Need(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Hu Jin Ding command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match) { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Only the native human chooser may answer."); var choices = p.Choices.Where(match).ToArray(); Need(choices.Length > 0, "The exact requested native Hu Jin Ding choice is absent: " + Diagnostic(g)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choices[0].Id, g.Revision)); }
    private static void Continue(GameEngine g) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 300; step++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "The actual Hu Jin Ding fixture ended before its native boundary: " + Diagnostic(g)); var p = P(g);
            if (p is { Kind: DecisionKind.PlayCard } && V(g, p.PlayerSeat).IsHuman) End(g);
            else if (p is not null && V(g, p.PlayerSeat).IsHuman)
            {
                if (p.SkillPrompt?.SkillId is Watch or Health && Option(g, "continue")) Continue(g);
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash) Answer(g, c => c.Cards.Count == 0);
                else if (p.Kind == DecisionKind.RescueDying) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else if (Action(g, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected Hu Jin Ding native prompt: " + Diagnostic(g));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing Hu Jin Ding native boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner,
        Prompt = P(g) is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, p.PromptId, Choices = p.Choices.Take(6).Select(c => new { c.Id, c.Cards, c.Targets, c.Parameters }) } : null,
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).HandCount, V(g, s).IsAlive }), Frames = g.ResolutionStack.Select(f => new { f.Id, Type = f.GetType().Name }), Facts = g.Events.TakeLast(6).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(restored), "Cold replay preserves every private random receipt, native response, public marker, badge and exact paid child return."); return restored; }
    private static (GameEngine, ContentRegistry) Start(string scenario = "plain", bool opening = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = scenario == "death" ? Role.Rebel : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Reach(g, () => Partner(g));
        if (!opening) { Answer(g, c => c.Targets.SequenceEqual([1])); Play(g); } return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-hu-jin-ding", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:hu-jin-ding");
            b.AddCard(new("fixture:hjd-lion", "真实白银狮子", "装备牌", "实际装备失去回复", CardKind.SilverLion));
            b.AddCard(new("fixture:hjd-ox", "真实木牛流马", "装备牌", "实际粮区限制", CardKind.WoodenOx));
            var defs = new List<string>
            {
                """{"id":"fixture:hjd-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
                $$"""
                {"id":"{{Driver}}","revision":1,"activations":[
                  {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]},
                  {"id":"draw-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":1}]},
                  {"id":"lose","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                  {"id":"damage-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                  {"id":"damage-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                  {"id":"equip-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment","awaitMovementTriggers":true}]},
                  {"id":"discard-all","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"discard"},{"op":"moveBoundCards","target":"owner","sourceBind":"discard","destination":"discardPile","awaitMovementTriggers":true}]},
                  {"id":"draw-many","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]}]}
                """
            };
            var tail = scenario == "loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1},{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"lost\",\"options\":[{\"id\":\"after-loss\"}]}" :
                scenario == "death" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}" : scenario == "silver" ? ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"equipment\"],\"count\":1,\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}" : "";
            defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-take-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.hu-jin-ding.qingyuan.take"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}{{tail}}]}]}""");
            if (scenario == "loss") defs.Add($$"""{"id":"fixture:hjd-hp-source","revision":1,"hpSkillGrants":[{"id":"healthy-qingyuan","skillId":"{{Qingyuan}}","minimumHp":17}]}""");
            if (scenario == "silver") defs.Add($$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"real-recovery","window":"afterHealthChanged","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}""");
            if (scenario == "aoe")
            {
                var driver = JsonNode.Parse(defs[1])!;
                driver["viewAs"] = JsonNode.Parse("""[{"id":"independent-played-dodge","inputKinds":["slash"],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true}]""");
                defs[1] = driver.ToJsonString();
                defs.Add("""{"id":"fixture:hjd-aoe","revision":1,"viewAs":[{"id":"actual-aoe","inputKinds":["slash"],"inputSuits":[],"outputKind":"arrowBarrage","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],"triggers":[{"id":"aoe-only","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnCardActionProhibition","target":"owner","cardKinds":["slash"],"actionTypes":["use"]}]}]}""");
            }
            var names = defs.Select(d => JsonNode.Parse(d)!).ToDictionary(d => d["id"]!.GetValue<string>(), d => new { name = d["id"]!.GetValue<string>(), description = "胡金定真实原生夹具",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(key => d[key]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray())
                    .SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',', defs)}}]}""", JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "实际胡金定机制") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            const string pick = "fixture:hjd-owner-pick"; b.AddSkill(new(pick, "固定本人选将", "公开选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000d) });
            b.AddGeneral(new(Owner, "实际胡金定", "ol-hu-jin-ding", pick, "shu", 16, [Driver, Watch, Zhongshen, scenario == "loss" ? "fixture:hjd-hp-source" : Qingyuan], GeneralGender.Female));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:hjd-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "实际其他角色", "supporter", scenario == "aoe" ? "fixture:hjd-aoe" : "fixture:hjd-quiet", "shu", 12, scenario == "silver" ? [Health] : []));
            var card = scenario == "silver" ? "fixture:hjd-lion" : scenario == "ox" ? "fixture:hjd-ox" : "standard:slash";
            b.AddDeck(new("fixture:hjd-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 40).Select(_ => new ContentDeckPhysicalCard(card, scenario == "black" ? Suit.Spade : Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "胡金定真实原生边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Loyalist)] = 1 }, "fixture:hjd-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
