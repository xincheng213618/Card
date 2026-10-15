using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class IncomingDistanceCourtesyChecks
{
    private const string Biluan = "ol:biluan", Lixia = "ol:lixia", Driver = "fixture:courtesy-driver", Watch = "fixture:courtesy-watch", Loss = "fixture:courtesy-loss";
    private const string Owner = "fixture:courtesy-owner", Mode = "identity:classic-courtesy-fixture";
    public static void ActualFactionCostAndPermanentDirectionAcrossTurns()
    {
        var (g, registry) = Start(courtesy: false);
        Use(g, "extra"); Play(g); End(g); Reach(g, () => Optional(P(g), Biluan)); Activate(g);
        var draft = P(g)!; var pay = draft.Choices.First(c => c.Cards.Count == 1); var card = pay.Cards.Single(); g = Cold(g, registry);
        Answer(g, c => c.Id == pay.Id); Play(g);
        var changed = E<IncomingDistanceChangedEvent>(g).Single();
        Need(changed.Amount == 2 && changed.Source.OwnerSeat == 0 && g.GetCombatDistance(1, 0) == 3 && g.GetCombatDistance(2, 0) == 4 &&
            g.GetCombatDistance(0, 1) == 1 && g.GetCombatDistance(0, 2) == 2, "The two actual factions add directed incoming distance, without using player count or modifying outgoing distance.");
        Need(E<TurnStartedEvent>(g).TakeLast(2).All(e => e.ActorSeat == 0) && E<TurnStartedEvent>(g).Last().TurnNumber > changed.ActualTurnNumber,
            "An actual extra turn begins while the distance remains permanent.");
        Need(g.CardMovements.Count(m => m.CardId == card && m.Reason.Value == "skill-program.ol:biluan.incoming-distance-courtesy.0") == 1,
            "The exact original physical cost is paid once across cold recovery and extra-turn progression.");
        g = Cold(g, registry); End(g); Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        Need(g.GetCombatDistance(1, 0) == 3 && E<IncomingDistanceChangedEvent>(g).Count() == 1,
            "The issued additive distance persists through multiple real other turns and the following ordinary own turn.");
        _ = Cold(g, registry);
    }
    public static void CourtesyDistinctBenefitsForeignPrivacyAndRecovery()
    {
        var (g, registry) = Start(); Use(g, "wound", targets: [1]); Play(g); BeginFirstCourtesy(g);
        var p = P(g)!; Need(p.Choices.Count == 6 && !p.Choices.Any(c => c.Parameters["selection"] == "0,1,2") && !p.Choices.Any(c => c.Parameters["selection"] == "0,0"),
            "Mandatory courtesy exposes exactly the three single options and three distinct pairs, with no decline or repeat.");
        var ownerBefore = V(g, 0).HandCount; var actor = g.State.CurrentSeat; var peerBefore = V(g, actor).HandCount; g = Cold(g, registry);
        Answer(g, c => c.Parameters["selection"] == "0,1"); Reach(g, () => E<IncomingDistanceCourtesyCompletedEvent>(g).Count() == 2);
        Need(V(g, 0).HandCount == ownerBefore + 1 && V(g, actor).HandCount == peerBefore + 2 &&
            E<IncomingDistanceChangedEvent>(g).TakeLast(2).All(e => e.Amount == -1) && g.GetCombatDistance(actor, 0) == 1,
            "The actual own and foreign private draw benefits both complete and decrement distance once each.");
        Need(g.CreateSnapshot(0).Players[actor].Hand.Count == 0 && g.CreateSnapshot(actor).Players[actor].Hand.Count == peerBefore + 2,
            "A foreign benefit publishes only hand count to the skill owner, while its beneficiary sees its real cards.");
        _ = Cold(g, registry);
        var (recover, recoverRegistry) = Start(); Use(recover, "wound", targets: [1]); Play(recover); BeginFirstCourtesy(recover);
        var before = V(recover, 1).Hp; recover = Cold(recover, recoverRegistry); Answer(recover, c => c.Parameters["selection"] == "2"); Reach(recover, () => E<IncomingDistanceCourtesyCompletedEvent>(recover).Count() == 2);
        Need(V(recover, 1).Hp == before + 1 && E<RecoveryAppliedEvent>(recover).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1),
            "The third option uses the native attributed recovery producer."); _ = Cold(recover, recoverRegistry);
        var (full, _) = Start(); BeginFirstCourtesy(full);
        Need(P(full)!.Choices.Count == 3 && !P(full)!.Choices.Any(c => c.Parameters["selection"].Contains('2')),
            "A full-HP turn owner cannot select an ineffective recovery option, but mandatory one/two draw choices remain.");
    }
    private static string Embedded(string suffix) { using var s = typeof(StandardContentPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-shi-xie.{suffix}")!; using var r = new StreamReader(s); return r.ReadToEnd(); }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Actual courtesy command was rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void BeginFirstCourtesy(GameEngine g)
    {
        End(g); Reach(g, () => Optional(P(g), Biluan)); Activate(g); Answer(g, c => c.Cards.Count == 1);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Lixia && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "incoming-distance-courtesy"));
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 180; step++)
        {
            if (done()) return;
            if (P(g) is { PlayerSeat: 0 } p)
            {
                if (Optional(p, Biluan)) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.SkillPrompt?.SkillId == Lixia) Answer(g, c => c.Parameters["selection"] == "0");
                else if (p.Kind is DecisionKind.RescueDying or DecisionKind.Nullification or DecisionKind.RespondSlash or DecisionKind.RespondDodge)
                    Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die" or "take-damage");
                else throw new InvalidOperationException($"Unexpected courtesy fixture prompt {p.Kind}: {p.Prompt}; " +
                    $"owner skills={string.Join(',', V(g, 0).Skills!.Select(s => s.ContentId))}; " +
                    $"recent movement reasons={string.Join(',', g.CardMovements.TakeLast(8).Select(m => m.Reason.Value))}; " +
                    $"watch starts={E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == Watch)}; " +
                    $"courtesy benefit invoices={JsonSerializer.Serialize(E<IncomingDistanceCourtesyMovementIssuedEvent>(g).ToArray())}");
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The bounded fixed courtesy fixture did not reach its actual boundary.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), g.CardMovements,
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(cold), "Cold replay preserves real chosen benefits, physical costs, child cursors and permanent incoming distance facts."); return cold; }
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Start(bool courtesy = true, string child = "none")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(courtesy, child));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g);
        Need(V(g, 0) is { Hp: 4, MaxHp: 4 }, "The selected classic owner must start at 4/4 HP so the four-point child reaches dying or death.");
        return (g, registry);
    }
    private sealed class Fixture(bool courtesy, string child) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("incoming-distance-courtesy-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json"));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id], Tags = id == Lixia ? SkillTag.Locked : SkillTag.None });
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[
              {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
              {"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
              {"id":"regrant-biluan","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Biluan}}"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["skill-program.ol:lixia.incoming-distance-courtesy.0"],"movementOccurrence":"perBatch","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            var trigger = rules["skills"]![1]!["triggers"]![0]!.AsObject();
            if (child == "cost-loss") { trigger["window"] = "cardsMoved"; trigger["sourceZones"] = new JsonArray("hand"); trigger.Remove("destinationZones"); trigger["movementReasons"] = new JsonArray("skill-program.ol:biluan.incoming-distance-courtesy.0"); }
            if (child == "recovery") { trigger["window"] = "afterHpRecovered"; trigger.Remove("destinationZones"); trigger.Remove("movementReasons"); trigger.Remove("movementOccurrence"); }
            var effects = trigger["effects"]!.AsArray();
            if (child is "cost-loss" or "courtesy-loss")
            {
                effects.Add(JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":4}"""));
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Loss}}","revision":1,"triggers":[{"id":"loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":4,"clampToMaxHp":true},{"op":"chooseOption","target":"owner","resultBind":"finish","options":[{"id":"continue"}]},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{(child == "cost-loss" ? Biluan : Lixia)}}"],"sourceBind":"standard:none"}]}]}
                """));
            }
            if (child == "courtesy-death") effects.Add(JsonNode.Parse("""{"op":"damage","target":"owner","amount":4}"""));
            var labels = new Dictionary<string, object> { [Driver] = new { name = "实际准备", description = "实际命令准备" },
                [Watch] = new { name = "真实子窗", description = "真实移动/回复子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            if (child is "cost-loss" or "courtesy-loss") labels[Loss] = new { name = "真实失源", description = "真实濒死回复和失源", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var fixture = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in fixture.Programs) b.AddSkill(new(id, fixture.Presentations[id].Name, fixture.Presentations[id].Description) { Program = program, ProgramPresentation = fixture.Presentations[id] });
            var skills = new List<string> { Driver }; if (courtesy) skills.Add(Lixia); if (child != "none") skills.Add(Watch); if (child is "cost-loss" or "courtesy-loss") skills.Add(Loss);
            b.AddGeneral(new(Owner, "实际距离拥有者", "supporter", Biluan, "qun", 3, skills, GeneralGender.Male));
            const string quietId = "fixture:courtesy-quiet";
            var quiet = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{quietId}}","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]}]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [quietId] = new { name = "固定其他角色", description = "固定其他角色不主动杀" } } }));
            b.AddSkill(new(quietId, "固定其他角色", "固定其他角色不主动杀") { Program = quiet.Programs[quietId], ProgramPresentation = quiet.Presentations[quietId], SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:courtesy-peer-{i}").ToArray(); foreach (var peer in peers) b.AddGeneral(new(peer, "实际其他角色", "supporter", quietId, "wei", 8, child == "recovery" ? [Watch] : []));
            b.AddDeck(new("fixture:courtesy-deck", "固定小牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "真实结束距离", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:courtesy-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
