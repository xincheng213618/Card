using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenGuoJiaCrossAwakeningChecks
{
    private const string Owner = "fixture:huishi-baijia-owner", Quiet = "fixture:huishi-baijia-quiet", Watch = "fixture:huishi-baijia-maximum";
    private const string Mode = "identity:classic-huishi-baijia-fixture";

    public static void RealHuiShiWaivesBaijiaHistoryAndKeepsPaidMaximumCold()
    {
        var (g, registry) = Start(); Play(g); var initialMaximum = V(g).MaxHp;
        Need(!E<BeiMiHuAwakeningMaximumPaidEvent>(g).Any() && !E<BeiMiHuGujuDrawPaidEvent>(g).Any(), "Baijia cannot awaken in the initial preparation with zero actual Guju acquisitions.");
        Accept(g, new UseProgramSkillCommand(0, "mobile:huishi-gift", "grant-awakening", [], [0], g.Revision, P(g)!.PromptId));
        Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("value") == "ol:baijia") == true);
        Answer(g, c => c.Parameters.GetValueOrDefault("value") == "ol:baijia"); Play(g);
        var granted = E<ShenGuoJiaAwakeningOverrideEvent>(g).Single();
        Need(granted is { Recipient: 0, SkillId: "ol:baijia" }, "The real limited Huishi command grants the selected Baijia instance.");
        Need(!E<BeiMiHuAwakeningMaximumPaidEvent>(g).Any(), "Huishi preserves Baijia's later preparation timing.");
        Need(V(g).MaxHp == initialMaximum - 2, "Huishi pays exactly two actual maximum HP: " + JsonSerializer.Serialize(new { Before = initialMaximum, After = V(g).MaxHp }));
        var maximumAfterHuishi = V(g).MaxHp;
        g = Cold(g, registry); Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, () => E<BeiMiHuAwakeningMaximumPaidEvent>(g).Any() && P(g)?.SkillPrompt?.SkillId == Watch);
        var paid = E<BeiMiHuAwakeningMaximumPaidEvent>(g).Single();
        Need(paid.CumulativeActualCards == 0 && paid.MaximumBefore == maximumAfterHuishi && paid.MaximumAfter == maximumAfterHuishi + 1 && paid.Source.SkillInstanceId == granted.InstanceId &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.BeiMiHu is not null).BeiMiHu!.Stage == "maximum-children" && !E<BeiMiHuAwakeningCompletedEvent>(g).Any(),
            "Huishi waives only Baijia's seven-card history threshold: the original preparation pays actual maximum HP before its child pauses.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Play(g);
        Need(E<BeiMiHuAwakeningMaximumPaidEvent>(g).Length == 1 && E<BeiMiHuAwakeningCompletedEvent>(g).Length == 1 &&
            V(g).MaxHp == maximumAfterHuishi + 1 && V(g).Skills!.Any(s => s.ContentId == "ol:canshi") && !V(g).Skills!.Any(s => s.ContentId == "ol:guju"),
            "The paid original frame resumes across cold replay and completes recovery, Guju loss and Canshi gain once, without reenforcing the waived threshold.");
        _ = Cold(g, registry);
    }

    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PlayerSnapshot V(GameEngine g) => g.CreateSnapshot(0).Players[0];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Need(bool yes, string reason) { if (!yes) throw new InvalidOperationException(reason); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Huishi/Baijia command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 160; step++)
        {
            if (done()) return;
            if (P(g)?.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
            if (P(g)?.SkillPrompt?.SkillId == Watch) { Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); continue; }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Huishi/Baijia missed its actual preparation or paid maximum child: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack }));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), g.CardMovements,
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(restored), "Real Huishi grants, exact skill instance, preparation owner, paid maximum and nested child cold-replay identically."); return restored; }
    private static (GameEngine, ContentRegistry) Start()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-huishi-baijia", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "mobile:shen-guo-jia", "ol:bei-mi-hu");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"maximum-paid","window":"afterHealthChanged","subject":"owner","condition":{"kind":"healthChangeKindIs","hpChangeKind":"maximumHp"},"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids = new[] { Quiet, Watch };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "Minimal actual Huishi/Baijia child fixture.", optionLabels = id == Watch ? new Dictionary<string, string> { ["continue"] = "继续原生支付子流程" } : [] }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "Actual Huishi/Baijia child fixture") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "辉逝拜假原生夹具", "supporter", "mobile:huishi-gift", "god", 6, ["ol:baijia", "ol:guju", Watch]));
            var peers = Enumerable.Range(1, 3).Select(s => $"fixture:huishi-baijia-peer-{s}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "安静同桌", "supporter", Quiet, "wei", 8));
            b.AddDeck(new("fixture:huishi-baijia-deck", "Uniform real cards", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(Mode, "辉逝拜假真实准备阶段", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:huishi-baijia-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
