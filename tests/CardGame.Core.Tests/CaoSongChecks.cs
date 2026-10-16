using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CaoSongChecks
{
    private const string Lilu = "ol:lilu", Yizheng = "ol:yizheng-cao-song";
    private const string Loss = "fixture:cs-loss";
    private const string Driver = "fixture:cs-driver", Gift = "fixture:cs-gift", Hp = "fixture:cs-hp", Quiet = "fixture:cs-quiet";
    private const string Owner = "fixture:cs-owner", Mode = "identity:classic-cao-song-fixture";

    public static void ActualGiftRemembersEveryPreviousCountAndReturnsCold()
    {
        var (g, registry) = Start("lilu");
        var initialMax = V(g, 0).MaxHp; var initialHp = V(g, 0).Hp;
        var counts = new[] { 2, 2, 1, 2 };
        for (var index = 0; index < counts.Length; index++)
        {
            Reach(g, () => Optional(P(g), Lilu));
            Activate(g);
            Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "recipient") == true);
            Need(V(g, 0).Hand.Count == 5, "Lilu draws to min(maximum HP, five), without adding the normal draw count.");
            Answer(g, c => c.Targets.SequenceEqual([1]));
            var ids = V(g, 0).Hand.Take(counts[index]).Select(c => c.Id).ToArray();
            Need(P(g) is { IsPrivate: true } && g.CreateSnapshot(1).PendingDecision is null,
                "Only the owner sees the actual hand-card toggles and selected gift before payment.");
            foreach (var id in ids) Answer(g, c => c.Parameters.GetValueOrDefault("option") == "card" && c.Cards.SequenceEqual([id]));
            g = Cold(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("option") == "give");
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gift);
            var paid = E<CaoSongLiluGiftCommittedEvent>(g).Last();
            var grows = index is 0 or 3;
            Need(paid.GiftCount == counts[index] && paid.PreviousGiftCount == (index == 0 ? (int?)null : counts[index - 1]) && paid.Grows == grows &&
                ids.All(id => V(g, 1).Hand.Any(c => c.Id == id)) &&
                g.CardMovements.Count(m => ids.Contains(m.CardId) && m.Reason.Value == "skill-program.caosong.lilu.gift") == ids.Length,
                "The actual gift pays each entity once and compares against the immediately previous actual gift, including equal and smaller gifts.");
            var maxBefore = V(g, 0).MaxHp; var hpBefore = V(g, 0).Hp;
            g = Cold(g, registry); Continue(g);
            if (grows)
            {
                Reach(g, () => P(g)?.SkillPrompt?.SkillId == Hp);
                Need(g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single().Change.Kind == HpChangeKind.MaximumHp &&
                    V(g, 0).MaxHp == maxBefore + 1 && V(g, 0).Hp == hpBefore,
                    "Maximum HP is actually increased before the distinct recovery step.");
                g = Cold(g, registry); Continue(g);
                Reach(g, () => P(g)?.SkillPrompt?.SkillId == Hp);
                Need(g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single().Change.Kind == HpChangeKind.Recovery &&
                    V(g, 0).Hp == hpBefore + 1, "The earned recovery happens once after its real maximum-HP child returns.");
                g = Cold(g, registry); Continue(g);
            }
            Play(g);
            Need(V(g, 0).MaxHp == initialMax + (index == 3 ? 2 : 1) &&
                V(g, 0).Hp == initialHp + (index == 3 ? 2 : 1) && E<CaoSongLiluGiftCommittedEvent>(g).Count() == index + 1,
                "Cold movement, maximum-HP and recovery returns never repeat the gift, growth or heal; equal/smaller counts earn neither benefit.");
            if (index + 1 < counts.Length) End(g);
        }
        _ = Cold(g, registry);
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Reach(GameEngine g, Func<bool> reached)
    {
        for (var step = 0; step < 400; step++)
        {
            if (reached()) return;
            if (P(g) is { PlayerSeat: 0 } p)
            {
                if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected actual Cao Song prompt: " + JsonSerializer.Serialize(p));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The actual Cao Song boundary was not reached: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Cao Song command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Every private view, owning receipt, child frame, paid fact, native movement and accepted command cold-restores identically.");
        return restored;
    }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 24 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        return (g, registry);
    }
    private static string Embedded(string suffix)
    { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-cao-song.{suffix}")!; using var r = new StreamReader(stream); return r.ReadToEnd(); }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-cao-song", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog catalog)
            { foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] }); }
            Add(SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json")));
            var effect = scenario is "recovery" or "redirect-original" ? "{\"op\":\"recover\",\"target\":\"selectedTarget\",\"amount\":1}" : "{\"op\":\"damage\",\"target\":\"owner\",\"sourceRef\":{\"kind\":\"selectedTarget\"},\"amount\":1}";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
                {"id":"recover","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":1}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]}],
                "triggers":[{"id":"issued-grant-after-source-loss","window":"judgmentPhaseStarting","subject":"owner","turnOwnerScope":"otherLiving","usageScope":"game","usageLimit":1,"optional":true,"effects":[
                  {"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{{effect}},
                  {"op":"chooseOption","target":"owner","resultBind":"tail","options":[{"id":"continue"}]}]}]},
              {"id":"{{Loss}}","revision":1,"triggers":[{"id":"source-loss-after-grant","window":"turnEnding","subject":"owner","turnOwnerScope":"own","priority":-20,"usageScope":"game","usageLimit":1,"optional":true,"effects":[{"op":"loseSkillsAndObtainNamedCard","target":"owner","skillIds":["{{Yizheng}}"],"outputKind":"qinglongCrescentBlade"}]}]},
              {"id":"{{Gift}}","revision":1,"triggers":[{"id":"native-paid-gift-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.caosong.lilu.gift"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gift","options":[{"id":"continue"}]}]}]},
              {"id":"{{Hp}}","revision":1,"triggers":[{"id":"native-health-child","window":"afterHealthChanged","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """;
            var labels = new[] { Driver, Gift, Hp, Quiet, Loss }.ToDictionary(id => id, id => new
            { name = id, description = "实际曹嵩支付和子窗口", optionLabels = id is Quiet or Loss ? new Dictionary<string, string>() : new Dictionary<string, string> { ["continue"] = "继续" } });
            Add(SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })));
            b.AddSkill(new("fixture:cs-owner-pick", "固定主公", "公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            b.AddSkill(new("fixture:cs-other-pick", "固定其他", "公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100000d) });
            var ownerSkills = new List<string> { Lilu, Yizheng, Driver, Gift, Hp, Loss };
            if (scenario == "redirect-original")
            {
                const string redirectSkill = "fixture:cs-redirect";
                Add(SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{redirectSkill}}","revision":1,"cardPolicies":[{"id":"real-redirect","kind":"redirectOwnTurnFactionRecovery","factionId":"wei","ownerRole":"lord","value":1,"providerDrawCount":1}]}]}""",
                    """{"schemaVersion":3,"skills":{"fixture:cs-redirect":{"name":"真实救援","description":"真实恢复最终对象"}}}"""));
                ownerSkills.Add(redirectSkill);
            }
            b.AddGeneral(new(Owner, "曹嵩真实规则", "ol-cao-song", "fixture:cs-owner-pick", "wei", 6, ownerSkills) { InitialHp = scenario == "lilu" ? 3 : scenario == "redirect-original" ? 1 : 6 });
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new($"fixture:cs-other-{seat}", "固定其他", "supporter", "fixture:cs-other-pick", "wei", 3, [Quiet]) { InitialHp = scenario == "redirect-original" ? 2 : 1 });
            b.AddDeck(new("fixture:cs-deck", "固定实体闪", 1, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "曹嵩真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:cs-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, "fixture:cs-other-1", "fixture:cs-other-2", "fixture:cs-other-3"]));
        }
    }
}
