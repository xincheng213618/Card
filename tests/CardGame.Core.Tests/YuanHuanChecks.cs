using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class YuanHuanChecks
{
    private const string Deru = "ol:deru", Linjie = "ol:linjie", Owner = "fixture:yh-owner", Driver = "fixture:yh-driver", Watch = "fixture:yh-watch", Health = "fixture:yh-health", Mode = "identity:classic-yuan-huan-fixture";
    private const string Take = "skill-program.yuan-huan.deru.take", Fill = "skill-program.yuan-huan.deru.fill", Discard = "skill-program.yuan-huan.linjie.discard", Draw = "skill-program.yuan-huan.linjie.draw";
    public static void DeruPresenceSetsAllNoneAndOpaqueRandomPrivacyCold()
    {
        foreach (var guess in new[] { 15, 0, 14 })
        {
            var (g, r) = Start(); Use(g, "lose", [0]); Play(g); var hp = V(g, 0).Hp; var ownHand = V(g, 0).HandCount; var targetIds = V(g, 1).Hand.Select(c => c.Id).ToArray();
            Begin(g, 1); var p = P(g)!;
            Need(p is { PlayerSeat: 0, IsPrivate: true } && p.Choices.Count == 16 && p.Choices.All(c => c.Cards.Count == 0 && c.Targets.Count == 0) &&
                Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null) && Root(g).YuanHuan is { GuessMask: null, ActualMask: null },
                "All 16 four-name presence guesses, including none and all, are privately offered independently of the target's real hidden hand.");
            Reject(g, new("deru-forged-mask-16")); g = Cold(g, r); Guess(g, guess);
            if (guess != 14)
            {
                Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health);
                Need(V(g, 0).Hp == hp + 1 && V(g, 1).Hand.Select(c => c.Id).SequenceEqual(targetIds) && Root(g).YuanHuan is { Stage: YuanHuanStage.RecoveryChildren, BasicCandidates: null, GuessMask: var selected, ActualMask: 1 } && selected == guess,
                    "Matching presence or absence heals before the random take; the original comparison is frozen while the real native recovery child is open.");
                g = Cold(g, r); Continue(g);
            }
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var frame = Root(g); var receipt = frame.YuanHuan!; var movement = E<YuanHuanStepIssuedEvent>(g).Single(e => e.FrameId == frame.Id && e.Reason == Take);
            Need(receipt is { Stage: YuanHuanStage.TakeChildren, ActualMask: 1 } && receipt.GuessMask == guess && receipt.BasicCandidates!.SequenceEqual(targetIds) &&
                movement is { ActualCount: 1, CardId: { } card } && receipt.BasicCandidates![receipt.RandomIndex] == card && V(g, 0).HandCount == ownHand + 1 &&
                V(g, 0).Hand.Any(c => c.Id == card) && V(g, 1).HandCount == targetIds.Length - 1 && Enumerable.Range(1, 3).All(s =>
                    g.CreateSnapshot(s).Players[0].Hand.Count == 0 && !g.CreateSnapshot(s).PublicRevealedCards.Any(c => c.Id == card)),
                "A mismatch obtains one exact native-random basic hand entity from the frozen full candidate set, without exposing the obtained hand or unselected private candidates to peers.");
            g = Cold(g, r); Continue(g); Play(g);
            Need(V(g, 0).Hp == hp + (guess == 14 ? 0 : 1) && E<YuanHuanStepIssuedEvent>(g).Count(e => e.Reason == Take && e.ActualCount == 1) == 1 &&
                E<YuanHuanStepIssuedEvent>(g).Single(e => e.Reason == Fill).ActualCount == 0 && E<YuanHuanCompletedEvent>(g).Count(e => e.FrameId == frame.Id) == 1,
                "All-selected and none-selected guesses may overlap heal and steal; the exact four-name opposite mask gets no heal, and partial correctness never receives the all-correct refill.");
            var before = State(g); var second = g.Submit(new UseProgramSkillCommand(0, Deru, "guess-basic-names", [], [1], g.Revision, P(g)!.PromptId));
            Need(!second.Accepted && State(g) == before, "The same real Play phase cannot activate Deru a second time."); _ = Cold(g, r);
        }
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.YuanHuan is not null);
    private static bool Option(GameEngine g, string id) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id) == true;
    private static void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real YuanHuan command."); }
    private static void Answer(GameEngine g, Func<PromptChoice,bool> predicate) { var p = P(g)!; var choices = p.Choices.Where(predicate).ToArray(); Need(V(g, p.PlayerSeat).IsHuman && choices.Length > 0, "The exact human-owned native choice is absent: " + Diagnostic(g)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choices[0].Id, g.Revision)); }
    private static void Guess(GameEngine g, int mask) => Answer(g, c => c.Parameters.GetValueOrDefault("guess-mask") == mask.ToString());
    private static void Begin(GameEngine g, int target) => Accept(g, new UseProgramSkillCommand(0, Deru, "guess-basic-names", [], [target], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Continue(GameEngine g) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 250; i++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "YuanHuan fixture ended before the actual native boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && V(g, p.PlayerSeat).IsHuman)
            {
                if (p.Kind == DecisionKind.PlayCard) Accept(g, new EndPlayPhaseCommand(p.PlayerSeat, g.Revision, p.PromptId));
                else if (p.SkillPrompt?.SkillId is Health or Watch && Option(g, "continue")) Continue(g);
                else if (p.Kind == DecisionKind.RescueDying) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected native YuanHuan prompt: " + Diagnostic(g));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing actual YuanHuan boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner, Prompt = P(g) is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(5).Select(c => new { c.Id, c.Cards, c.Targets, c.Parameters }) } : null,
        Frames = g.ResolutionStack.Select(f => new { f.Id, Type = f.GetType().Name }), Facts = g.Events.TakeLast(5).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(restored), "Cold JSON replay preserves hidden guesses, exact frozen names, random state, native private chooser and paid child cursor."); return restored; }
    private static void Reject(GameEngine g, ChoiceId choice) { var before = State(g); var p = P(g)!; Need(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice, g.Revision)).Accepted && State(g) == before, "An unpublished guess cannot expose a hand or change a paid cursor."); }
    private static (GameEngine,ContentRegistry) Start(string scenario = "plain")
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario is "death" or "redirect" ? Role.Rebel : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-yuan-huan", new(1,0,0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:yuan-huan");
            var defs = new List<string>
            {
                $$"""
                {"id":"{{Driver}}","revision":1,"activations":[
                 {"id":"lose","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                 {"id":"empty-own","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]},
                 {"id":"empty-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardParticipantCards","target":"selectedTarget","amount":64,"zones":["hand"]}]},
                 {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
                 {"id":"self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                 {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]}]},
                """.TrimEnd(','),
                $$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"actual-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}""",
                """{"id":"fixture:yh-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""
            };
            var tail = scenario == "loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1},{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"lost\",\"options\":[{\"id\":\"after-loss\"}]}" : scenario == "death" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}" : "";
            defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{Take}}","{{Fill}}","{{Draw}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}{{tail}}]}]}""");
            var peerExtra = new List<string> { Health };
            if (scenario == "identity") { peerExtra.Add("fixture:yh-identity"); defs.Add("""{"id":"fixture:yh-identity","revision":1,"cardIdentities":[{"id":"real-hand-fire-slash","zones":["hand"],"inputKinds":["crossbow"],"inputSuits":[],"outputKind":"fireSlash"}]}"""); }
            if (scenario == "view-as") { peerExtra.Add("fixture:yh-view-as"); defs.Add("""{"id":"fixture:yh-view-as","revision":1,"viewAs":[{"id":"optional-slash","inputKinds":["crossbow"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":true}]}"""); }
            if (scenario == "loss") defs.Add($$"""{"id":"fixture:yh-hp-source","revision":1,"hpSkillGrants":[{"id":"healthy-deru","skillId":"{{Deru}}","minimumHp":17}]}""");
            if (scenario == "redirect") { peerExtra.Add("fixture:yh-replacement"); defs.Add("""{"id":"fixture:yh-replacement","revision":1,"cardPolicies":[{"id":"real-lord-recovery","kind":"redirectOwnTurnFactionRecovery","factionId":"qun","ownerRole":"lord","value":1,"providerDrawCount":1}]}"""); }
            var labels = defs.Select(d => JsonNode.Parse(d)!).ToDictionary(d => d["id"]!.GetValue<string>(), d => new { name = d["id"]!.GetValue<string>(), description = "袁涣真实原生夹具",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(key => d[key]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',', defs)}}]}""", JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "原生规则夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:yh-pick", "固定本人选将", "公开角色选择评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000d) });
            b.AddGeneral(new(Owner, "实际袁涣", "ol-yuan-huan", "fixture:yh-pick", "qun", 16, [Driver, Watch, Health, Linjie, scenario == "loss" ? "fixture:yh-hp-source" : Deru], GeneralGender.Male));
            var peers = Enumerable.Range(1,3).Select(i => $"fixture:yh-peer-{i}").ToArray(); foreach (var peer in peers) b.AddGeneral(new(peer, "实际其他角色", "supporter", "fixture:yh-quiet", "qun", 12, peerExtra));
            var kind = scenario switch { "fire" => CardKind.FireSlash, "thunder" => CardKind.ThunderSlash, "identity" or "view-as" => CardKind.Crossbow, _ => CardKind.Slash };
            b.AddCard(new("fixture:yh-material", "实际实体材料", kind == CardKind.Crossbow ? "装备牌" : "基本牌", "原生元素杀或实体身份区分", kind));
            b.AddDeck(new("fixture:yh-deck", "固定同质实体", 4,2,[]) { PhysicalCards = Enumerable.Range(0,64).Select(_ => new ContentDeckPhysicalCard("fixture:yh-material", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "袁涣真实行为边界", 4,4,new Dictionary<string,int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Loyalist)] = 1 }, "fixture:yh-deck", GeneralCandidateCount:4, GeneralPoolIds:[Owner,.. peers]));
        }
    }
}
