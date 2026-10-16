using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WangKuangChecks
{
    private const string Skill = "ol:renxia", Owner = "fixture:wk-owner", Driver = "fixture:wk-driver", Quiet = "fixture:wk-quiet",
        Watch = "fixture:wk-watch", Health = "fixture:wk-health", Before = "fixture:wk-before", Conditional = "fixture:wk-source", Extra = "fixture:wk-extra",
        Mode = "identity:classic-wang-kuang-fixture";
    public static void BothDoWhileOrdersPrivateDiscardAndNativeExhaustionCold()
    {
        {
            var (g, r) = Start(); var initial = V(g).HandCount; Begin(g); Branch(g, "draw"); Play(g);
            Need(E<WangKuangRenxiaCompletedEvent>(g) is [{ Ending: false, Branch: WangKuangRenxiaBranch.Draw, Iterations: 1 }] && V(g).HandCount == initial + 2 &&
                !g.GetHumanLegalActions().Any(a => a.SkillId == Skill), "A hand already containing damage still draws two once and spends its one actual Play allowance.");
            var promise = E<WangKuangRenxiaObligationIssuedEvent>(g).Single(); g = Cold(g, r); End(g); Reach(g, () => Current(g) is { Ending: true, Stage: WangKuangRenxiaStage.DiscardChoice });
            Need(Current(g)!.ObligationId == promise.ActiveFrameId && Current(g)!.Branch == WangKuangRenxiaBranch.Discard, "The exact same PlayEnding forces the opposite branch rather than offering another free choice.");
            var first = P(g)!.Choices[0].Cards.Single(); var privateState = State(g); var prompt = P(g)!;
            Need(P(g) is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null) &&
                !g.Submit(new AnswerPromptCommand(1, prompt.PromptId, prompt.Choices[0].Id, g.Revision)).Accepted && privateState == State(g), "Only the real owner sees or can answer its frozen HE discard set.");
            g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([first]));
            Need(!E<WangKuangRenxiaIterationPaidEvent>(g).Any(e => e.FrameId == g.ResolutionStack.OfType<ProgramSkillFrame>().Last().Id), "Selecting the first of two cards does not partially pay the atomic discard.");
            g = Cold(g, r); FinishRenxia(g);
            Need(V(g).HandCount == 0 && E<WangKuangRenxiaEndingConsumedEvent>(g).Count(e => e.ActiveFrameId == promise.ActiveFrameId) == 1 &&
                E<WangKuangRenxiaCompletedEvent>(g).Single(e => e.Ending).Iterations == (initial + 2) / 2,
                "The forced discard really repeats two-card native payments until no Slash remains, and consumes its issued ending once."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("unexpected"); var initial = V(g).HandCount;
            Need(V(g).Hand.All(c => c.Kind == CardKind.UnexpectedAssault), "The additional damaging trick is a real registered physical hand identity.");
            Begin(g); Branch(g, "draw"); Play(g);
            Need(E<WangKuangRenxiaCompletedEvent>(g) is [{ Ending: false, Iterations: 1 }] && V(g).HandCount == initial + 2,
                "Real UnexpectedAssault is a damaging trick and stops the first draw pair just as the official broad category requires.");
            g = Cold(g, r); End(g); Reach(g, () => Current(g) is { Ending: true, Stage: WangKuangRenxiaStage.DiscardChoice }); FinishRenxia(g);
            Need(V(g).HandCount == 0 && E<WangKuangRenxiaCompletedEvent>(g).Single(e => e.Ending).Iterations == (initial + 2) / 2,
                "The opposite branch repeats through every real UnexpectedAssault instead of mistaking the remaining hand for harmless cards."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("exhausted"); var initial = V(g).HandCount; Begin(g); Branch(g, "discard");
            Reach(g, () => Current(g) is { Stage: WangKuangRenxiaStage.DiscardChoice }); g = Cold(g, r); FinishRenxia(g); Play(g);
            Need(V(g).HandCount == initial - 2 && E<WangKuangRenxiaCompletedEvent>(g) is [{ Ending: false, Iterations: 1 }],
                "Even a hand with no damage card pays the initial chosen discard once before checking its condition.");
            End(g); Reach(g, () => E<WangKuangRenxiaCompletedEvent>(g).Any(e => e.Ending));
            var ending = E<WangKuangRenxiaCompletedEvent>(g).Single(e => e.Ending); var paid = E<WangKuangRenxiaIterationPaidEvent>(g).Where(e => e.FrameId == ending.FrameId).ToArray();
            Need(ending.Branch == WangKuangRenxiaBranch.Draw && paid.Length > 1 && paid[^1].ActualCount == 0 && paid.Take(paid.Length - 1).All(e => e.ActualCount == 2) &&
                g.CardMovements.Any(m => m.Reason == CardMoveReasons.Reshuffle), "A truly damage-free small deck repeats genuine pairs, reshuffles the actual discard pile, and stops when no actual entity can be drawn."); _ = Cold(g, r);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static WangKuangRenxiaReceipt? Current(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.WangKuangRenxia is not null)?.WangKuangRenxia;
    private static bool Option(GameEngine g, string option) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option) == true;
    private static void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real Wang Kuang command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Only a real human prompt is answered."); var choices = p.Choices.Where(pick).ToArray(); Need(choices.Length > 0, "No exact Wang Kuang choice: " + Diagnostic(g)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choices[0].Id, g.Revision)); }
    private static void Continue(GameEngine g) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Begin(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, Skill, "choose-first-branch", [], [], g.Revision, P(g)!.PromptId));
    private static void Branch(GameEngine g, string branch) { Reach(g, () => Current(g) is { Stage: WangKuangRenxiaStage.BranchChoice }); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == branch); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void FinishRenxia(GameEngine g)
    {
        for (var step = 0; step < 100; step++)
        {
            if (Current(g) is null) return;
            if (Current(g) is { Stage: WangKuangRenxiaStage.DiscardChoice })
            {
                var damage = V(g).Hand.Where(c => c.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.UnexpectedAssault).Select(c => c.Id).ToHashSet();
                var choices = P(g)!.Choices; var selected = choices.FirstOrDefault(c => c.Cards.Count == 1 && damage.Contains(c.Cards[0])) ?? choices[0];
                Answer(g, c => c.Id == selected.Id);
            }
            else Reach(g, () => Current(g) is null || Current(g) is { Stage: WangKuangRenxiaStage.DiscardChoice });
        }
        throw new InvalidOperationException("Renxia did not finish its bounded actual small payment loop: " + Diagnostic(g));
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 300; step++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "Wang Kuang ended before its real boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p is { Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (Option(g, "continue")) Continue(g);
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is not null) throw new InvalidOperationException("Unexpected Wang Kuang prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing Wang Kuang actual boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner, Prompt = P(g) is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Select(c => c.Parameters) } : null,
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).IsAlive, V(g, s).HandCount }), Frames = g.ResolutionStack.Select(f => new { f.Id, Type = f.GetType().Name }), Tail = g.Events.TakeLast(8).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(copy), "Cold replay preserves all filtered views, privately frozen discard entities, issued actual-phase obligations and already paid native child cursors."); return copy; }
    private static (GameEngine, ContentRegistry) Start(string scenario = "plain")
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario == "paid-death" ? Role.Renegade : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wang-kuang", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:wang-kuang");
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":50,"priority":0}],"activations":[
              {"id":"trim","minCards":1,"maxCards":64,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","sourceZones":["hand"],"usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trimmed"},{"op":"moveBoundCards","target":"owner","sourceBind":"trimmed","destination":"discardPile","awaitMovementTriggers":true}]},
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]}]},
             {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!;
            var names = new Dictionary<string, object> { [Driver] = new { name = "真实准备命令", description = "仅真实移动和失体力" }, [Quiet] = new { name = "安静其他回合", description = "真实跳出牌" } };
            void Add(string id, string body, string[]? options = null)
            { rules["skills"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"" + id + "\",\"revision\":1," + body + "}")); names[id] = new { name = id, description = "真实原生孩子", optionLabels = (options ?? []).ToDictionary(x => x, x => x) }; }
            var skills = new List<string> { Driver };
            if (scenario is "loss" or "paid-loss") { Add(Conditional, "\"hpSkillGrants\":[{\"id\":\"full-source\",\"skillId\":\"ol:renxia\",\"minimumHp\":13}]"); skills.Add(Conditional); } else skills.Add(Skill);
            if (scenario == "extra") { Add(Extra, "\"triggers\":[{\"id\":\"real-extra-play\",\"window\":\"playEnding\",\"subject\":\"owner\",\"optional\":false,\"priority\":100,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"insertPhase\",\"target\":\"selectedTarget\",\"phase\":\"play\",\"phaseContinuation\":\"beforeNormalPreparation\"}]}]"); skills.Add(Extra); }
            if (scenario is "draw-child" or "draw-recompare" or "paid-loss" or "paid-death")
            {
                var child = scenario == "draw-recompare" ? "{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}" : scenario == "draw-child" ? "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}" :
                    scenario == "paid-death" ? "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}" : "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}";
                Add(Watch, "\"triggers\":[{\"id\":\"real-pair-gain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.wang-kuang.renxia.draw\"],\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"pause\",\"options\":[{\"id\":\"continue\"}]}," + child + ",{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"after\",\"options\":[{\"id\":\"after-child\"}]}]}]", ["continue", "after-child"]); skills.Add(Watch);
            }
            if (scenario is "draw-child" or "lion")
            {
                Add(Health, "\"triggers\":[{\"id\":\"native-recovery\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"hpChangeOccurrence\":\"perEvent\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"pause\",\"options\":[{\"id\":\"continue\"}]}]}]", ["continue"]); skills.Add(Health);
                if (scenario == "draw-child") { Add(Before, "\"triggers\":[{\"id\":\"native-damage\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"pause\",\"options\":[{\"id\":\"continue\"}]}]}]", ["continue"]); skills.Add(Before); }
            }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:wk-owner-pick", "本人公开评分", "确定选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            b.AddGeneral(new(Owner, "王匡本人", "ol-wang-kuang", "fixture:wk-owner-pick", "qun", scenario == "paid-death" ? 13 : 12, skills));
            var peers = Enumerable.Range(0, 3).Select(i => "fixture:wk-peer-" + i).ToArray();
            foreach (var (peer, index) in peers.Select((peer, index) => (peer, index)))
            { var pick = "fixture:wk-pick-" + index; b.AddSkill(new(pick, "公开选将次序", "确定其他角色") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 1000000d * (3 - index)) }); b.AddGeneral(new(peer, "真实其他角色", "supporter", pick, "wei", 12, [Quiet])); }
            b.AddCard(new("fixture:wk-lion", "白银狮子", "装备牌", "真实原生离场回复", CardKind.SilverLion));
            b.AddCard(new("fixture:wk-unexpected", "出其不意", "锦囊牌", "真实伤害锦囊实体", CardKind.UnexpectedAssault));
            b.AddDeck(new("fixture:wk-deck", "固定小实体牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, scenario == "exhausted" ? 24 : 96).Select(i => new ContentDeckPhysicalCard(scenario == "exhausted" ? "standard:dodge" : scenario == "unexpected" ? "fixture:wk-unexpected" : scenario == "lion" && i % 5 == 0 ? "fixture:wk-lion" : "standard:slash", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "王匡真实行为边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:wk-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
