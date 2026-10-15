using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class EndRefillNextTurnChecks
{
    private const string Gang = "ol:gangzhi", Bei = "ol:beizhan", Driver = "fixture:end-refill-driver";
    private const string Watch = "fixture:end-refill-watch", Loss = "fixture:end-refill-loss", Owner = "fixture:end-refill-owner";
    private const string Mode = "identity:classic-end-refill";
    private static string Reason => $"skill-program.{Bei}.end-refill-next-turn.draw";

    public static void ActualEndRefillCappedAndNextTurnMostIncludesTie()
    {
        foreach (var target in new[] { 0, 1 })
        {
            var (g, registry) = Start(peerHp: 8); var firstTurn = g.CreateSnapshot(0).TurnNumber;
            if (target == 0) { Use(g, "trim", cards: V(g, 0).Hand.Take(V(g, 0).HandCount - 2).Select(c => c.Id).ToArray()); Play(g); }
            End(g); Reach(g, () => Optional(P(g), Bei));
            Need(E<TurnEndedEvent>(g).Any(e => e.TurnNumber == firstTurn && e.ActorSeat == 0), "The skill is offered after the actual own TurnEnded fact.");
            g = Cold(g, registry); Activate(g); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "end-refill-next-turn") == true);
            Need(P(g)!.Choices.Any(c => c.Targets.SequenceEqual([0])) && P(g)!.Choices.Any(c => c.Targets.SequenceEqual([1])), "Self and every living other seat remain legal even when no draw is needed.");
            var before = V(g, target).HandCount; var cap = Math.Min(V(g, target).MaxHp, 5);
            Answer(g, c => c.Targets.SequenceEqual([target]));
            Reach(g, () => E<EndRefillNextTurnCompletedEvent>(g).Any());
            var issued = E<EndRefillNextTurnIssuedEvent>(g).Single();
            Need(issued.Receipt.DrawRequested == Math.Max(0, cap - before) && issued.Receipt.DrawActual == issued.Receipt.DrawRequested &&
                g.CardMovements.Count(m => m.Reason.Value == Reason && m.To == CardLocation.Hand(target)) == issued.Receipt.DrawActual,
                "Refill uses the actual chosen hand and MaxHP, draws real private cards, and caps the resulting hand at five.");
            g = Cold(g, registry);
            Reach(g, () => E<EndRefillNextTurnConsumedEvent>(g).Any(e => e.ProgramFrameId == issued.ProgramFrameId));
            var consumed = E<EndRefillNextTurnConsumedEvent>(g).Single(e => e.ProgramFrameId == issued.ProgramFrameId);
            Need(consumed.ActualTurnNumber > firstTurn && consumed.TargetSeat == target &&
                consumed.Restricted == (consumed.HandCount >= consumed.GreatestLivingHandCount), "The selected seat's next actual turn consumes the original issued source and uses a dynamic greatest-hand comparison including ties.");
            Need(E<CardTargetRestrictionGrantedEvent>(g).Count(e => e.Restriction.ParentFrameId == issued.ProgramFrameId) == (consumed.Restricted ? 1 : 0),
                "The due restriction is granted exactly once through the native turn target policy.");
            _ = Cold(g, registry);
        }
        var (tie, tieRegistry) = Start(peerHp: 3); End(tie); Reach(tie, () => Optional(P(tie), Bei)); Activate(tie);
        Reach(tie, () => P(tie)?.Choices.Any(c => c.Targets.SequenceEqual([1])) == true); Answer(tie, c => c.Targets.SequenceEqual([1]));
        Reach(tie, () => E<EndRefillNextTurnConsumedEvent>(tie).Any());
        Need(E<EndRefillNextTurnIssuedEvent>(tie).Single().Receipt.DrawActual == 0 && E<EndRefillNextTurnConsumedEvent>(tie).Single() is
            { HandCount: 4, GreatestLivingHandCount: 4, Restricted: true }, "A full hand still schedules the next turn, and tied most qualifies.");
        _ = Cold(tie, tieRegistry);
    }

    public static void ActualBidirectionalHpReplacementSelfSourceLessAndChain()
    {
        foreach (var activation in new[] { "outgoing", "incoming" })
        {
            var (g, registry) = Start(); var target = activation == "outgoing" ? 1 : 0; var before = V(g, target).Hp;
            Use(g, activation, targets: [1]); Play(g);
            var replaced = E<OtherParticipantDamageReplacedEvent>(g).Single();
            Need(V(g, target).Hp == before - 2 && replaced.Source.OwnerSeat == 0 && replaced.DamageSourceSeat == (activation == "outgoing" ? 0 : 1) &&
                replaced.TargetSeat == target && replaced.BaseAmount == 2 && !E<DamageAppliedEvent>(g).Any(),
                "Both outgoing and incoming real other-participant attacks become two HP loss, while retaining policy and actual source separately.");
            _ = Cold(g, registry);
        }
        foreach (var activation in new[] { "self-damage", "source-less" })
        {
            var (g, registry) = Start(); Use(g, activation); Play(g);
            Need(!E<OtherParticipantDamageReplacedEvent>(g).Any() && E<DamageAppliedEvent>(g).Single().SourceLess == (activation == "source-less"),
                "Self damage and damage without an attributed source remain genuine damage.");
            _ = Cold(g, registry);
        }
        var (chain, chainRegistry) = Start(); Use(chain, "chain", targets: [0]); Play(chain); Use(chain, "chain", targets: [1]); Play(chain);
        Use(chain, "self-fire"); Play(chain);
        Need(E<DamageAppliedEvent>(chain).Count() == 1 && E<DamageAppliedEvent>(chain).Single().TargetSeat == 0 &&
            E<OtherParticipantDamageReplacedEvent>(chain).Single() is { TargetSeat: 1, ChainPropagation: true } && !V(chain, 0).IsChained && !V(chain, 1).IsChained,
            "Genuine self fire starts the native chain, while its other-seat propagated portion independently converts to HP loss.");
        _ = Cold(chain, chainRegistry);
        var (loss, lossRegistry) = Start(); Use(loss, "chain", targets: [0]); Play(loss); Use(loss, "chain", targets: [1]); Play(loss);
        Use(loss, "outgoing-fire", targets: [1]); Play(loss);
        Need(!E<DamageAppliedEvent>(loss).Any() && V(loss, 0).IsChained && V(loss, 1).IsChained, "An initial other-seat HP replacement neither unchains nor propagates elemental damage.");
        _ = Cold(loss, lossRegistry);
    }

    private static string Embedded(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-shen-pei.{suffix}")!; using var r = new StreamReader(stream); return r.ReadToEnd(); }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Accept(GameEngine g, GameCommand cmd) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([cmd])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native refill command was rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string activation, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => Accept(g,
        new UseProgramSkillCommand(0, Driver, activation, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 160; step++)
        {
            if (done()) return;
            if (P(g) is { PlayerSeat: 0 } p)
            {
                if (Optional(p, Bei)) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind is DecisionKind.RescueDying or DecisionKind.Nullification or DecisionKind.RespondSlash or DecisionKind.RespondDodge)
                    Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die" or "take-damage");
                else throw new InvalidOperationException($"Unexpected native refill prompt {p.Kind}: {p.Prompt}");
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed refill fixture did not reach its bounded actual boundary.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(cold), "Cold replay preserves exact target drafts, issued draws, nested child cursors and delayed next-turn source."); return cold; }
    private static void Need(bool okay, string message) { if (!okay) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Start(int peerHp = 8, string child = "none", string card = "standard:dodge")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(peerHp, child, card));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(int peerHp, string child, string card) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("end-refill-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var content = SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json"));
            foreach (var (id, program) in content.Programs) b.AddSkill(new(id, content.Presentations[id].Name, content.Presentations[id].Description) { Program = program, ProgramPresentation = content.Presentations[id], Tags = id == Gang ? SkillTag.Locked : SkillTag.None });
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"trim","minCards":1,"maxCards":64,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trim"},{"op":"moveBoundCards","target":"owner","sourceBind":"trim","destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"outgoing","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
                {"id":"self-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                {"id":"self-fire","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1,"nature":"fire"}]},
                {"id":"outgoing-fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                {"id":"turn-over","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"owner"}]},
                {"id":"extra-turn","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                {"id":"draw20","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"strip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardParticipantCards","target":"selectedTarget","zones":["hand"],"amount":64}]},
                {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{Reason}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            var effects = rules["skills"]![1]!["triggers"]![0]!["effects"]!.AsArray();
            if (child == "change-hand") effects.Add(JsonNode.Parse("""{"op":"draw","target":"owner","amount":1}"""));
            if (child == "damage") effects.Add(JsonNode.Parse("""{"op":"damage","target":"owner","amount":1}"""));
            if (child == "lose-source")
            {
                effects.Add(JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":4}"""));
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Loss}}","revision":1,"triggers":[{"id":"source-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":4,"clampToMaxHp":true},{"op":"chooseOption","target":"owner","resultBind":"finish","options":[{"id":"continue"}]},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Bei}}"],"sourceBind":"standard:none"}]}]}
                """));
            }
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实规则准备", description = "真实规则实体准备" },
                [Watch] = new { name = "获得子窗", description = "真实已付获得子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            if (child == "lose-source") labels[Loss] = new { name = "濒死源替换", description = "真实濒死回复及源替换", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var fixture = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in fixture.Programs) b.AddSkill(new(id, fixture.Presentations[id].Name, fixture.Presentations[id].Description) { Program = program, ProgramPresentation = fixture.Presentations[id] });
            var skills = new List<string> { Bei, Driver }; if (child != "none") skills.Add(Watch); if (child == "lose-source") skills.Add(Loss);
            b.AddGeneral(new(Owner, "实际审配技能拥有者", "supporter", Gang, "qun", 3, skills, GeneralGender.Male));
            const string peerSkill = "fixture:end-refill-peer-skill";
            var quiet = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{peerSkill}}","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [peerSkill] = new { name = "固定其他角色", description = "固定其他角色不主动杀" } } }));
            b.AddSkill(new(peerSkill, "固定其他角色", "固定其他角色选择") { Program = quiet.Programs[peerSkill], ProgramPresentation = quiet.Presentations[peerSkill], SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:end-refill-peer-{i}").ToArray(); foreach (var peer in peers) b.AddGeneral(new(peer, "实际其他角色", "supporter", peerSkill, "wei", peerHp));
            if (card == "fixture:end-refill-mixed")
            {
                b.AddCard(new("fixture:end-refill-blade", "古锭刀", "装备牌", "真实古锭刀", CardKind.GudingBlade));
                b.AddCard(new("fixture:end-refill-vine", "藤甲", "装备牌", "真实藤甲", CardKind.Tengjia));
                b.AddCard(new("fixture:end-refill-lion", "白银狮子", "装备牌", "真实白银狮子", CardKind.SilverLion));
            }
            var sequence = card == "fixture:end-refill-mixed" ? new[] { "fixture:end-refill-blade", "fixture:end-refill-vine", "fixture:end-refill-lion", "standard:fire_slash", "standard:alcohol", "standard:peach" } : [card];
            b.AddDeck(new("fixture:end-refill-deck", "固定实体牌", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(sequence[i % sequence.Length], Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "实际回合和伤害替代", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:end-refill-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
