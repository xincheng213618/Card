using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShanheS5TaoyuanChecks
{
    private const string Skill = StandardShanheS5TaoyuanPackage.TishenId;
    private const string Owner = "fixture:s5-tishen-owner", Wound = "fixture:s5-tishen-wound";
    private const string Child = "fixture:s5-tishen-recovery-child", Quiet = "fixture:s5-tishen-quiet";
    private const string Mode = "identity:classic-s5-tishen";

    public static void RepeatableTishenDrawsActualRecoveryAcrossPaidChildCold()
    {
        var (g, registry) = Start();
        var receipts = new List<ProgramRecoveryReceipt>();
        for (var preparation = 0; preparation < 2; preparation++)
        {
            Reach(g, () => Prompt(g)?.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("skill-id") == Skill &&
                choice.Parameters.GetValueOrDefault("program-action") == "activate") == true);
            var before = View(g);
            var recovered = before.MaxHp - before.Hp;
            Need(recovered > 0, "The actual preparation HP-loss driver leaves a wounded owner before each optional recovery.");
            Answer(g, choice => choice.Parameters.GetValueOrDefault("skill-id") == Skill &&
                choice.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(g, () => Prompt(g)?.SkillPrompt?.SkillId == Child);
            var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(item => item.SkillId == Skill);
            Need(frame.RecoveryReceipt is { Drawn: false } receipt && receipt.Recovered == recovered &&
                View(g).Hp == View(g).MaxHp && View(g).Hand.Count == before.Hand.Count,
                "Actual recovery is already paid, while its original receipt has not drawn before the real recovery child returns.");
            receipts.Add(frame.RecoveryReceipt!);
            g = Cold(g, registry);
            Answer(g, choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
            Reach(g, () => Prompt(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            Need(View(g).Hp == View(g).MaxHp - 1 && View(g).Hand.Count == before.Hand.Count + recovered,
                "The recovery child really loses one HP; Tishen still draws the original actual recovery amount once, rather than the new lost HP.");
            g = Cold(g, registry);
            if (preparation == 0) Accept(g, new EndPlayPhaseCommand(0, g.Revision, Prompt(g)!.PromptId));
        }
        var recoveries = g.Events.Select(item => item.Payload).OfType<RecoveryAppliedEvent>()
            .Where(item => item.TargetSeat == 0).ToArray();
        Need(receipts.Select(item => item.RecoveryFrameId).Distinct().Count() == 2 &&
            recoveries.Select(item => item.Amount).SequenceEqual(receipts.Select(item => item.Recovered)),
            "The second real preparation grants a fresh full recovery and receipt; the S5 skill never inherits boundary Tishen's game-once quota.");
    }

    private static PlayerSnapshot View(GameEngine g) => g.CreateSnapshot(0).Players[0];
    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, 4)
        .Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Need(result.Accepted, result.Error?.Message ?? "Rejected native S5 Tishen command.");
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var prompt = Prompt(g)!;
        Accept(g, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, g.Revision));
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 160; step++)
        {
            if (done()) return;
            Need(g.State.Winner == Winner.None, "Small S5 Tishen fixture ended before its expected preparation.");
            var prompt = Prompt(g);
            if (prompt is { PlayerSeat: 0, Kind: DecisionKind.PlayCard })
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, prompt.PromptId));
            else if (prompt is { PlayerSeat: 0 } && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip"))
                Answer(g, choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Small S5 Tishen fixture missed its native boundary: " + JsonSerializer.Serialize(Prompt(g)));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(g.CreateSnapshot(seat))),
        Frames = g.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
        Events = g.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands)
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "The actual paid recovery receipt, pending child and physical draw history cold-restore identically.");
        return restored;
    }
    private static (GameEngine, ContentRegistry) Start()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10
        }, registry);
        Accept(g, new StartGameCommand());
        Reach(g, () => Prompt(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, Prompt(g)!.PromptId));
        return (g, registry);
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("s5-tishen-native-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            void Add(SkillProgramCatalog catalog)
            {
                foreach (var (id, program) in catalog.Programs)
                    builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    {
                        Program = program, ProgramPresentation = catalog.Presentations[id],
                        SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => id == Quiet ? 10000d : -10000d)
                    });
            }
            string Embedded(string suffix)
            {
                using var stream = typeof(StandardShanheS5TaoyuanPackage).Assembly.GetManifestResourceStream(
                    "CardGame.Content.Standard.SkillPrograms.shanhe-s5-taoyuan." + suffix)!;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            Add(SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json")));
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Wound}}","revision":1,
                   "modifiers":[{"id":"keep-cards","query":"handLimit","operation":"add","value":20,"priority":0}],
                   "triggers":[{"id":"actual-preparation-loss","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"priority":100,
                     "effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
                  {"id":"{{Child}}","revision":1,"triggers":[
                    {"id":"actual-recovery-child","window":"afterHpRecovered","subject":"owner","optional":false,
                     "effects":[{"op":"chooseOption","target":"owner","resultBind":"resume","options":[{"id":"continue"}]},
                                {"op":"loseHp","target":"owner","amount":1}]}]},
                  {"id":"{{Quiet}}","revision":1,"triggers":[
                    {"id":"skip-play","window":"afterNormalDraw","subject":"owner","optional":false,
                     "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
                ]}
                """;
            var labels = new Dictionary<string, object>
            {
                [Wound] = new { name = "实际准备扣血", description = "每次准备阶段先真实失去一点体力" },
                [Child] = new { name = "实际回复子步骤", description = "回复后等待，再真实失去一点体力", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Quiet] = new { name = "静默", description = "其他角色跳过出牌" }
            };
            Add(SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })));
            builder.AddGeneral(new(Owner, "专版替身拥有者", "supporter", Skill, "shu", 4, [Wound, Child]) { InitialHp = 2 });
            var peers = Enumerable.Range(1, 3).Select(index => $"fixture:s5-tishen-peer-{index}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "真实静默其他角色", "supporter", Quiet, "wei", 8));
            builder.AddDeck(new("fixture:s5-tishen-deck", "固定实际回复摸牌", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Diamond, 7)).ToArray()
            });
            builder.AddMode(new(Mode, "专版替身真实准备", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:s5-tishen-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
