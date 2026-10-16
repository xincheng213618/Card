using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DeathGrowthSkillsChecks
{
    private const string Zhengnan = "ol:zhengnan", Xiefang = "ol:xiefang", Driver = "fixture:death-growth-driver", Quiet = "fixture:death-growth-quiet";
    private const string Owner = "fixture:death-growth-owner", Mode = "identity:classic-death-growth", Watch = "fixture:death-growth-watch", Loss = "fixture:death-growth-loss";
    private static readonly string[] Grants = ["boundary:wusheng", "classic:dangxian", "classic:zhiman"];
    public static void ActualOtherDeathsDrawChooseAllSkillsAndExtraPlayCold()
    {
        var (g, registry) = Start();
        foreach (var id in Grants)
        {
            var victim = Enumerable.Range(1, 5).First(s => V(g, s).IsAlive); var hand = V(g, 0).Hand.Count;
            Use(g, "kill-other", [victim]); Reach(g, () => Optional(P(g), Zhengnan));
            Need(!V(g, victim).IsAlive && E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == victim), "Only a real other-player death creates this optional growth opportunity.");
            Activate(g); Reach(g, () => GrowthChoice(g));
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.DeathGrowthSkill is not null); var r = root.DeathGrowthSkill!;
            Need(r.VictimSeat == victim && r.DrawCount == 3 && V(g, 0).Hand.Count == hand + 3 &&
                P(g)!.Choices.All(c => c.Cards.Count == 0 && c.Targets.Count == 0) && Enumerable.Range(1, 5).All(s => g.CreateSnapshot(s).PendingDecision is null),
                "The exact real draw precedes the owner's private missing-skill choice, without hidden hand entities or foreign authority.");
            var old = State(g); Need(!g.Submit(new AnswerPromptCommand(victim, P(g)!.PromptId, P(g)!.Choices[0].Id, g.Revision)).Accepted && State(g) == old,
                "A deceased or foreign chooser cannot mutate this living owner's issued skill choice.");
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("skill-id") == id); Play(g);
            Need(V(g, 0).Skills!.Count(s => s.ContentId == id) == 1 && E<DeathGrowthSkillGrantedEvent>(g).Count(e => e.ProgramFrameId == root.Id && e.GrantedSkillId == id) == 1 &&
                E<DeathGrowthDrawIssuedEvent>(g).Count(e => e.ProgramFrameId == root.Id) == 1 && !g.ResolutionStack.Any(f => f.Id == root.Id),
                "One original other-death draw grants exactly one permanent current-linked skill and returns its typed frame once.");
            if (id == Grants[0])
            {
                var far = Enumerable.Range(1, 5).First(s => V(g, s).IsAlive && g.GetCombatDistance(0, s) > g.GetAttackRange(0));
                Need(g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([far])),
                    "The actually learned current Wusheng permits this native Diamond Slash beyond the remaining ring's attack range.");
            }
        }
        var turn = g.CreateSnapshot(0).TurnNumber; End(g); Reach(g, () => g.CreateSnapshot(0).TurnNumber > turn && P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        var nextTurn = g.CreateSnapshot(0).TurnNumber;
        Need(E<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == "classic:dangxian" && e.OwnerSeat == 0), "The actually learned plain Dangxian starts its native extra Play on the next turn.");
        g = Cold(g, registry); End(g); Play(g);
        Need(g.CreateSnapshot(0).TurnNumber == nextTurn && Grants.All(id => V(g, 0).Skills!.Any(s => s.ContentId == id)),
            "The extra Play returns to normal flow in that same actual turn and all three learned skills survive cold recovery.");
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 6).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool GrowthChoice(GameEngine g) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "death-growth-skill") == true;
    private static void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual death-growth command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Use(GameEngine g, string id, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 180; step++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "The small actual death-growth fixture ended before its boundary."); var p = P(g);
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying or DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification }) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed small death-growth command bound was exceeded. " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack }));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 6).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), g.CardMovements,
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(restored), "Every safe viewer and exact death/skill/movement cursor survives command replay."); return restored; }
    private static string Embedded(string bundle, string suffix)
    { using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{bundle}.{suffix}")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    private static (GameEngine, ContentRegistry) Start(string scenario = "normal")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 6, HumanSeat = 0, HumanRole = scenario == "paid-death" ? Role.Rebel : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("death-growth-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog catalog, IReadOnlyList<string>? only = null)
            { foreach (var (id, program) in catalog.Programs.Where(x => only is null || only.Contains(x.Key))) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id], Tags = id is Xiefang or "classic:dangxian" ? SkillTag.Locked : SkillTag.None,
                    SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => id == Quiet ? 10000d : -10000d) }); }
            foreach (var (bundle, id) in new[] { ("boundary-guan-yu", Grants[0]), ("classic-lifecycle-skills", Grants[1]), ("classic-ma-su", Grants[2]) })
                Add(SkillProgramCatalog.Load(Embedded(bundle, "rules.json"), Embedded(bundle, "presentation.json")), [id]);
            Add(SkillProgramCatalog.Load(Embedded("ol-guan-suo", "rules.json"), Embedded("ol-guan-suo", "presentation.json")));
            var defs = new List<string> { $$"""
                {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"kill-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},
                {"id":"damage-kill-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"grant-all","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["boundary:wusheng","classic:dangxian","classic:zhiman"]}]}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
                """ };
            if (scenario.StartsWith("paid-", StringComparison.Ordinal)) defs.Add($$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-issued-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch",
                "movementReasons":["skill-program.other-death-growth.draw"],"usageScope":"game","usageLimit":1,"optional":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}
                """);
            if (scenario.StartsWith("paid-loss", StringComparison.Ordinal)) defs.Add($$"""
                {"id":"{{Loss}}","revision":1,"triggers":[{"id":"actual-source-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,
                "effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Zhengnan}}"],"sourceBind":"standard:none"}]}]}
                """);
            var rules = JsonNode.Parse($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',', defs)}}]}""")!;
            var labels = rules["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实其他角色死亡成长驱动",
                optionLabels = n.ToJsonString().Contains("chooseOption", StringComparison.Ordinal)
                    ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            Add(SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })));
            var skills = new List<string> { Zhengnan, Xiefang }; if (scenario.StartsWith("paid-", StringComparison.Ordinal)) skills.Add(Watch); if (scenario.StartsWith("paid-loss", StringComparison.Ordinal)) skills.Add(Loss);
            b.AddGeneral(new(Owner, "真实死亡成长者", "supporter", Driver, "shu", 12, skills, scenario == "female-owner" ? GeneralGender.Female : GeneralGender.Male));
            var peers = Enumerable.Range(1, 5).Select(i => $"fixture:death-growth-peer-{i}").ToArray();
            foreach (var (peer, index) in peers.Select((id, i) => (id, i))) b.AddGeneral(new(peer, "真实其他角色", "supporter", Quiet, "wei", 12,
                Gender: index == 0 && scenario is "female-peer" or "female-owner" ? GeneralGender.Female : GeneralGender.Male));
            b.AddDeck(new("fixture:death-growth-deck", "固定实际方片杀", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 128).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Diamond, 7)).ToArray() });
            b.AddMode(new(Mode, "真实死亡与成长", 6, 6, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 5 }, "fixture:death-growth-deck", GeneralCandidateCount: 6, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
