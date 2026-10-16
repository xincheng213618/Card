using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SharedPhaseSkillScenario
{
    internal const string Shared = "fixture:shared-phase-skill";
    internal const string Draw = "fixture:shared-phase-draw-a";
    internal const string Mode = "fixture:shared-phase-mode";
    internal static readonly string[] Pool = [Draw, "fixture:shared-phase-draw-b", "fixture:shared-phase-draw-c"];
    internal static ContentRegistry Registry(bool real = false, bool permanent = false, bool extraPhase = false, int initialHp = 8, bool rescue = true) =>
        ContentRegistry.Build(new StandardContentPackage(), new Fixture(real, permanent, extraPhase, initialHp, rescue));

    internal static GameEngine Start(ContentRegistry registry, bool ownerHuman = false)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, ownerHuman ? "fixture:shared-owner-1" : "fixture:shared-actor",
            game.Revision, game.PendingDecision!.PromptId));
        return game;
    }

    internal static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        if (!result.Accepted) throw new InvalidOperationException((result.Error?.Message ?? "Expected a shared-skill fixture command.") +
            $" ({command.GetType().Name}; {game.State.Status}, {game.State.Winner}; " +
            string.Join(',', game.Events.TakeLast(5).Select(item => item.Payload.GetType().Name)) + ")");
    }
    internal static void Reach(GameEngine game, Func<PendingDecision?, bool> ready)
    {
        for (var step = 0; step < 192 && !ready(Decision(game)); step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        if (!ready(Decision(game))) throw new InvalidOperationException("The fixed shared-skill fixture did not reach its decision: " +
            $"{game.State.Phase}, seat {game.State.CurrentSeat}, HP {game.CreateSnapshot(0).Players[0].Hp}, {Decision(game)?.Kind}, " +
            string.Join(',', game.Events.TakeLast(5).Select(item => item.Payload.GetType().Name)));
    }
    internal static PendingDecision? Decision(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(decision => decision is not null);
    internal static void Play(GameEngine game) => Reach(game, decision => decision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    internal static void Answer(GameEngine game, Func<PromptChoice, bool> choose)
    {
        var prompt = game.PendingDecision!;
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(choose).Id, game.Revision));
    }

    private sealed class Fixture(bool real, bool permanent, bool extraPhase, int initialHp, bool rescue) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shared-phase-skills", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            if (real)
                CharacterFixtureContent.Register(builder, "ol:zhong-yan", "classic:chen-gong", "boundary:da-qiao", "classic:dian-wei",
                    "classic:diao-chan", "ol:sp-diao-chan", "ol:du-yu", "boundary:hua-tuo", "classic:hua-tuo", "ol:jiang-gan",
                    "boundary:jiang-wei", "ol:liu-xie", "classic:shen-lu-meng", "boundary:sun-quan", "boundary:sun-shangxiang",
                    "classic:taishi-ci", "classic:xun-you", "boundary:xun-yu", "ol:zhang-xing-cai", "classic:zhuge-liang", "classic:sun-shangxiang");
            var rules = JsonNode.Parse("""
                {"skills":[
                  {"id":"fixture:shared-phase-skill","revision":1,"activations":[{"id":"share","otherCharactersOnly":true,"minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"loseHp","target":"activator","amount":1},{"op":"offerRandomPlayPhaseSkill","target":"activator","amount":3,"skillIds":["fixture:shared-phase-draw-a","fixture:shared-phase-draw-b","fixture:shared-phase-draw-c"]}]}],"triggers":[{"id":"own","window":"playPhaseStarting","subject":"owner","turnOwnerScope":"own","optional":true,"effects":[{"op":"offerRandomPlayPhaseSkill","target":"owner","amount":3,"skillIds":["fixture:shared-phase-draw-a","fixture:shared-phase-draw-b","fixture:shared-phase-draw-c"]}]}]},
                  {"id":"fixture:shared-phase-draw-a","revision":1,"activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
                  {"id":"fixture:shared-phase-draw-b","revision":1,"activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
                  {"id":"fixture:shared-phase-draw-c","revision":1,"activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
                  {"id":"fixture:shared-extra-phase","revision":1,"triggers":[{"id":"extra","window":"playEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"insertPhase","target":"selectedTarget","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
                  {"id":"fixture:shared-health-driver","revision":1,"activations":[{"id":"pay","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":4}]}]}
                ]}
                """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var presentation = new JsonObject();
            foreach (var skill in rules["skills"]!.AsArray())
                presentation[skill!["id"]!.GetValue<string>()] = new JsonObject { ["name"] = "共享技能", ["description"] = "共用跨角色出牌阶段技能契约" };
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), new JsonObject { ["schemaVersion"] = 3, ["skills"] = presentation }.ToJsonString());
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id == Shared ? "共享技能" : id == Draw ? "阶段摸牌" : "阶段技能", "共享机制检查")
                { Program = program, ProgramPresentation = catalog.Presentations[id], ActionForms = SkillActionForm.Active });
            var shared = real ? "ol:bolan" : Shared;
            var actorSkills = new List<string>();
            if (permanent) actorSkills.Add(Draw);
            if (extraPhase) actorSkills.Add("fixture:shared-extra-phase");
            if (initialHp == 1) actorSkills.Add("fixture:shared-health-driver");
            builder.AddGeneral(new("fixture:shared-actor", "技能发动者", "supporter", "standard:none", "shu", 8, actorSkills) { InitialHp = initialHp });
            for (var index = 1; index <= 3; index++)
                builder.AddGeneral(new($"fixture:shared-owner-{index}", $"钟琰提供者{index}", "supporter", shared, "jin", 8));
            builder.AddDeck(new("fixture:shared-phase-deck", "固定救援牌堆", 4, 0, [new(rescue ? "standard:peach" : "standard:dodge", 80)]));
            builder.AddMode(new(Mode, "共享阶段技能", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:shared-phase-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:shared-actor", "fixture:shared-owner-1", "fixture:shared-owner-2", "fixture:shared-owner-3"]));
        }
    }
}
