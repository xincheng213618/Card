using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SkillChangeOpeningChecks
{
    private const string Driver = "fixture:skill-change-opening", Rescue = "fixture:skill-change-rescue";
    private const string Owner = "fixture:skill-change-owner", Mode = "identity:classic-skill-change-opening";

    public static void DamageAndDyingChangesBeforeFirstRoundCold()
    {
        foreach (var scenario in new[] { "danxin", "fengliang", "niepan", "fuzhan", "qianxin" })
        {
            try
            {
                var s = Scene.Start(scenario);
                switch (scenario)
                {
                    case "danxin":
                        s.Reach(() => s.Offer("classic:danxin")); s.BeforeRound(); s.Cold(); s.Activate();
                        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "upgrade") == true);
                        s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "upgrade");
                        s.Reach(() => s.E<ConfiguredConversionTierChangedEvent>().Any());
                        Require(s.E<ConfiguredConversionTierChangedEvent>().Single() is { StateId: "declared-card-tier", Tier: 1 },
                            "Ordinary Danxin can modify Jiaozhao after real opening damage.");
                        break;
                    case "fengliang":
                        s.Reach(() => s.E<ProgramOwnerSkillsReplacedEvent>().Any()); s.BeforeRound();
                        Require(s.V.Hp == 2 && s.Has("ol:kunfen-awakened") && s.Has("ol:sp-jiang-wei-tiaoxin") && !s.Has("ol:kunfen"),
                            "Opening Dying awakens Fengliang, restores HP and replaces Kunfen with its optional form.");
                        break;
                    case "niepan":
                        s.Reach(() => s.P is { Kind: DecisionKind.RescueDying } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "mou-mobile:niepan"));
                        s.BeforeRound(); s.Cold();
                        s.Answer(c => c.Parameters.GetValueOrDefault("skill-id") == "mou-mobile:niepan");
                        s.Reach(() => s.E<MouPangTongUpgradedEvent>().Any());
                        Require(s.V.Hp == 2 && s.V.HandCount == 2 && s.E<MouPangTongUpgradedEvent>().Length == 1,
                            "Opening Niepan completes native discard, draw and recovery before one permanent Lianhuan upgrade.");
                        break;
                    case "fuzhan":
                        s.Reach(() => s.Offer("mou:fuzhan")); s.BeforeRound(); s.Cold(); s.Activate();
                        s.Reach(() => s.E<ProgramBooleanStateChangedEvent>().Any(e => e.SkillId == "mou:juemou" && e.StateId == "modified" && e.Value));
                        Require(s.V.Hp == s.V.MaxHp && s.E<ProgramBooleanStateChangedEvent>().Count(e => e.SkillId == "mou:juemou" && e.StateId == "modified" && e.Value) == 1,
                            "Fuzhan can follow a genuine opening Dying exit, recover fully and modify Juemou once.");
                        break;
                    case "qianxin":
                        s.Reach(() => s.E<SkillsAcquiredEvent>().Any(e => e.SkillIds.Contains("boundary:jianyan-current"))); s.BeforeRound();
                        Require(s.Has("boundary:jianyan-current") && s.V.MaxHp == 3,
                            "Qianxin handles actual opening self-damage and acquires Jianyan after the maximum-HP cost.");
                        break;
                }
                s.Cold(); s.Play(); s.Cold();
                Require(!s.G.IsFaulted && s.E<TurnStartedEvent>().Length == 1 && s.G.State.TurnNumber == 1,
                    "An opening skill change cold-restores and resumes into its first actual turn without a stopped session.");
            }
            catch (Exception error) { throw new InvalidOperationException($"Opening skill change '{scenario}': {error.Message}", error); }
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V => G.CreateSnapshot(0).Players[0];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Has(string id) => V.Skills!.Any(skill => skill.ContentId == id);
        public bool Offer(string id) => P?.SkillPrompt?.SkillId == id && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void BeforeRound() => Require(G.State.TurnNumber == 0 && E<RoundStartedEvent>().Length == 0, "The actual trigger must occur before Round 1.");
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(match).Id, G.Revision)); }
        public void Submit(GameCommand command) { var result = G.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Real command rejected."); }
        public void Reach(Func<bool> reached)
        {
            for (var step = 0; step < 140; step++)
            {
                if (reached()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    Require(p.Kind != DecisionKind.PlayCard, "The requested skill change was not completed before ordinary play.");
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                        Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                    else Answer(_ => true); // Juemou's genuine initial polarity choice.
                }
                else Submit(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Opening skill change did not reach its bounded native continuation.");
        }
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Cold()
        {
            var before = SnapshotJson.Serialize(G.CreateSnapshot(0));
            var commands = G.AcceptedCommands.Count;
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Require(SnapshotJson.Serialize(G.CreateSnapshot(0)) == before && G.AcceptedCommands.Count == commands,
                "Cold replay preserves the native choice, changed skills, HP and command prefix.");
        }
        public static Scene Start(string scenario)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var scene = new Scene(game, registry);
            scene.Submit(new StartGameCommand()); scene.Reach(() => scene.P is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
            scene.Submit(new SelectGeneralCommand(0, Owner, game.Revision, scene.P!.PromptId));
            return scene;
        }
    }

    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:skill-change-opening", "1.0.0", "Real opening damage and skill changes");
        public void Register(IContentRegistryBuilder builder)
        {
            var (bundle, skills) = scenario switch
            {
                "danxin" => ("classic-guo-huanghou", new[] { "classic:jiaozhao", "classic:danxin" }),
                "fengliang" => ("ordinary-sp-jiang-wei", new[] { "ol:kunfen", "ol:fengliang" }),
                "niepan" => ("mou-pang-tong-mobile", new[] { "mou-mobile:lianhuan", "mou-mobile:niepan" }),
                "fuzhan" => ("mou-jiang-wei", new[] { "mou:juemou", "mou:fuzhan" }),
                _ => ("boundary-xu-shu", new[] { "boundary:zhuhai-current", "boundary:qianxin-current" })
            };
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{bundle}.rules.json")!);
            using var presentation = new StreamReader(assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{bundle}.presentation.json")!);
            var production = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
            foreach (var (id, program) in production.Programs)
                builder.AddSkill(new(id, production.Presentations[id].Name, "Production skill change") { Program = program, ProgramPresentation = production.Presentations[id] });

            var damage = scenario is "fengliang" or "niepan" or "fuzhan" ? 4 : 1;
            var fixture = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"triggers":[{"id":"opening-damage","window":"gameStarting","subject":"owner","priority":-1000,"optional":false,"effects":[{"op":"damage","target":"owner","amount":{{damage}}}]}]},
                  {"id":"{{Rescue}}","revision":1,"triggers":[{"id":"opening-rescue","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":1,"clampToMaxHp":true}]}]}
                ]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Driver] = new { name = "Opening damage", description = "Native damage before Round 1" }, [Rescue] = new { name = "Opening rescue", description = "Native Dying exit before Round 1" } } }));
            foreach (var (id, program) in fixture.Programs) builder.AddSkill(new(id, id, id) { Program = program });
            builder.AddSkill(new("fixture:skill-change-plain", "Plain", "No effects"));
            builder.AddGeneral(new(Owner, "Opening skill owner", "supporter", skills[0], "wei", 3,
                skills.Skip(1).Concat([Driver]).Concat(scenario == "fuzhan" ? [Rescue] : []).ToArray()));
            for (var i = 1; i < 4; i++) builder.AddGeneral(new($"fixture:skill-change-other-{i}", "Other", "supporter", "fixture:skill-change-plain", "qun", 8));
            builder.AddDeck(new("fixture:skill-change-deck", "Opening fixed deck", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "Opening changes", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:skill-change-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, "fixture:skill-change-other-1", "fixture:skill-change-other-2", "fixture:skill-change-other-3"]));
        }
    }
}
