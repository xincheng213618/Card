using CardGame.Content.Standard;
using CardGame.Core;

internal static class GeneralDraftAiChecks
{
    public static void OrdinaryRosterRuleCoverage()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new EditionPackage());
        var lord = RuntimeGeneral(registry, "classic:sima-yi");
        foreach (var name in EditionPackage.Names)
        foreach (var role in new[] { Role.Lord, Role.Loyalist, Role.Rebel, Role.Renegade })
        {
            var old = RuntimeGeneral(registry, "classic:" + name);
            var upgrade = RuntimeGeneral(registry, "boundary:" + name);
            var result = new SimpleAiBrain(role == Role.Lord ? 0 : 1, 31, AiDecisionPolicy.CurrentVersion).ChooseGeneral(View(lord, role), role,
                [old, upgrade], 1, contentRegistry: registry);
            Require(Score(result.Thought, upgrade) >= Score(result.Thought, old) - .01,
                $"Legacy weights or missing action tags must not inflate the older edition for {name}/{role}: " +
                System.Text.Json.JsonSerializer.Serialize(result.Thought));
        }
        Require(registry.Skills.Values.All(skill => skill.SelectionWeights is null),
            "Standard content and the audited editions must not restore the sparse legacy skill scoring table.");
        var candidate = RuntimeGeneral(registry, "boundary:gao-shun");
        var tagged = candidate with { Id = "fixture:tagged", Skills = candidate.Skills.Select(skill =>
            skill with { ActionForms = skill.ActionForms | SkillActionForm.Active }).ToArray() };
        var thought = new SimpleAiBrain(0, 31, AiDecisionPolicy.CurrentVersion).ChooseGeneral(View(lord, Role.Rebel), Role.Lord,
            [candidate, tagged], 1, contentRegistry: registry).Thought;
        Require(Math.Abs(Score(thought, candidate) - Score(thought, tagged)) < .01,
            "Executable activation rules must receive the same evaluation with or without optional display action tags.");
    }

    public static void LordBenefitRecipientsAndCosts()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new BenefitPackage());
        var lord = RuntimeGeneral(registry, "fixture:general-self-draw");
        Compare("self-draw", "other-draw", 3, "Drawing only for another character cannot replenish the Lord's own defenses.");
        Compare("self-recover", "other-recover", 10, "Healing only others cannot remove the fragile-Lord penalty.");
        Compare("self-draw", "paid-draw", 4, "Identical draw benefits must account for the active HP payment.");
        Compare("self-recover", "limited-recover", 5, "A limited rescue is not repeatable recovery.");
        Compare("growth", "decay", 8, "One-time awakening HP reduction must not be treated as recurring forced attrition.");
        var alternative = RuntimeGeneral(registry, "fixture:general-alternative-draw");
        var alternativeThought = new SimpleAiBrain(0, 31, AiDecisionPolicy.CurrentVersion).ChooseGeneral(View(lord, Role.Rebel), Role.Lord,
            [lord, alternative], 1, contentRegistry: registry).Thought;
        Require(Math.Abs(Score(alternativeThought, lord) - Score(alternativeThought, alternative)) < .01,
            "Two alternative entries sharing a usage group must not duplicate the same draw benefit.");

        void Compare(string preferred, string weaker, double margin, string message)
        {
            var first = RuntimeGeneral(registry, "fixture:general-" + preferred);
            var second = RuntimeGeneral(registry, "fixture:general-" + weaker);
            var thought = new SimpleAiBrain(0, 31, AiDecisionPolicy.CurrentVersion).ChooseGeneral(View(lord, Role.Rebel), Role.Lord,
                [first, second], 1, contentRegistry: registry).Thought;
            Require(Score(thought, first) > Score(thought, second) + margin,
                message + " " + System.Text.Json.JsonSerializer.Serialize(thought));
        }
    }

    public static void OrdinaryEditionStrengthAndLordSafety()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new MatchupPackage());
        var ordinary = RuntimeGeneral(registry, "classic:sima-yi");
        var upgraded = RuntimeGeneral(registry, "boundary:sima-yi");
        var saferLord = RuntimeGeneral(registry, "fixture:wei-lord");
        foreach (var role in Enum.GetValues<Role>())
        {
            var seat = role == Role.Lord ? 0 : 1;
            var view = View(saferLord, role == Role.Lord ? Role.Rebel : role);
            var result = new SimpleAiBrain(seat, 31, AiDecisionPolicy.CurrentVersion).ChooseGeneral(view, role, [ordinary, upgraded], 1,
                contentRegistry: registry);
            Require(result.General.Id == upgraded.Id && Score(result.Thought, upgraded) > Score(result.Thought, ordinary) + 1,
                "The wider judgment sources and per-damage-point feedback must beat the older edition for every identity.");
        }
        var lordResult = new SimpleAiBrain(0, 31, AiDecisionPolicy.CurrentVersion).ChooseGeneral(View(saferLord, Role.Rebel), Role.Lord,
            [ordinary, saferLord], 1, contentRegistry: registry);
        Require(lordResult.General.Id == saferLord.Id &&
                lordResult.Thought.Candidates.Single(candidate => candidate.GeneralId == ordinary.Id).Reason.Contains("低体力"),
            "A fragile Lord without independent defense must not outrank a safer Lord because of an inflated legacy judgment prior.");
    }

    public static void MatchupsAndPrivacy()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new MatchupPackage());
        var plainLord = General("fixture:plain-lord");
        var defendedLord = General("fixture:defended-lord");
        var weiLord = General("fixture:wei-lord");
        var zhangXiu = General("classic:zhang-xiu");
        var maChao = General("boundary:ma-chao");
        var rebel = Evaluate(Role.Rebel, plainLord, zhangXiu, maChao);
        var guarded = Evaluate(Role.Rebel, defendedLord, zhangXiu, maChao);
        var wei = Evaluate(Role.Rebel, weiLord, zhangXiu, maChao);
        Require(Score(rebel, zhangXiu) > Score(guarded, zhangXiu) + 10 &&
                rebel.Candidates.Single(candidate => candidate.GeneralId == zhangXiu.Id).Reason.Contains("缺少回合外防御"),
            "Zhang Xiu's actual hand lock must favor a vulnerable public Lord over an independently defended one.");
        Require(Score(wei, maChao) > Score(rebel, maChao) &&
                wei.Candidates.Single(candidate => candidate.GeneralId == maChao.Id).Reason.Contains("势力响应"),
            "Boundary Ma Chao must recognize suppression of the public Wei Lord's Hujia, not merely a faction label.");

        var weiSupport = General("fixture:wei-support");
        var loyalist = Evaluate(Role.Loyalist, weiLord, weiSupport, zhangXiu);
        var withoutRequest = Evaluate(Role.Loyalist, plainLord, weiSupport, zhangXiu);
        Require(Score(loyalist, weiSupport) > Score(withoutRequest, weiSupport) &&
                loyalist.Candidates.Single(candidate => candidate.GeneralId == weiSupport.Id).Reason.Contains("同势力"),
            "A loyalist must recognize its actual faction's cooperation with the public Lord's request skill.");
        var renegade = Evaluate(Role.Renegade, plainLord, zhangXiu, defendedLord);
        var defensiveRebel = Evaluate(Role.Rebel, plainLord, zhangXiu, defendedLord);
        Require(Score(renegade, defendedLord) > Score(defensiveRebel, defendedLord),
            "A renegade must value independent defense more than a rebel does.");

        var view = View(weiLord, Role.Rebel);
        var changedHidden = view with { Players = view.Players.Select(player => player.Seat == 2 ? player with
            { GeneralId = zhangXiu.Id, GeneralName = zhangXiu.Name, Skills = zhangXiu.Skills, Role = Role.Loyalist } : player).ToArray() };
        var first = new SimpleAiBrain(1, 31, AiDecisionPolicy.CurrentVersion).ChooseGeneral(view, Role.Rebel, [zhangXiu, maChao], 1, contentRegistry: registry).Thought;
        var second = new SimpleAiBrain(1, 31, AiDecisionPolicy.CurrentVersion).ChooseGeneral(changedHidden, Role.Rebel, [zhangXiu, maChao], 1, contentRegistry: registry).Thought;
        Require(System.Text.Json.JsonSerializer.Serialize(first) == System.Text.Json.JsonSerializer.Serialize(second),
            "Foreign hidden generals and roles must not change draft advice or expose hidden skill evidence.");
        var directStrong = zhangXiu with { Id = "mou:direct-fixture", Name = "实际抽中的谋将" };
        var strongChoice = new SimpleAiBrain(1, 31, AiDecisionPolicy.CurrentVersion).ChooseGeneral(View(plainLord, Role.Rebel), Role.Rebel,
            [directStrong, defendedLord], 1, new Dictionary<string, int> { [directStrong.Id] = 8 }, registry);
        Require(strongChoice.General.Id == directStrong.Id,
            "A restricted series directly present in the candidate list must keep its actual strength preference and remain selectable.");

        AiGeneralThought Evaluate(Role role, GeneralDefinition lord, params GeneralDefinition[] candidates) =>
            new SimpleAiBrain(1, 31, AiDecisionPolicy.CurrentVersion).ChooseGeneral(View(lord, role), role, candidates, 1, contentRegistry: registry).Thought;
        GeneralDefinition General(string id) => RuntimeGeneral(registry, id);
    }

    // Load the actual printed rule programs without loading unrelated characters.
    // Small fixtures vary only the public Lord's defenses and request policy.
    private sealed class MatchupPackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("draft-matchups", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "classic:sima-yi", "boundary:sima-yi");
            RegisterBundle("classic-zhang-xiu", "classic:xiongluan", "classic:congjian");
            RegisterBundle("boundary-ma-chao", "boundary:tieqi");
            RegisterBundle("passive-response-rules", "classic:hujia", "classic:bazhen");
            builder.AddGeneral(new("fixture:plain-lord", "无回合外防御主公", "supporter", "standard:none", "shu"));
            builder.AddGeneral(new("fixture:defended-lord", "八阵主公", "supporter", "classic:bazhen", "shu", 3));
            builder.AddGeneral(new("fixture:wei-lord", "护驾主公", "supporter", "classic:hujia", "wei"));
            builder.AddGeneral(new("fixture:wei-support", "魏国候选", "supporter", "standard:none", "wei"));
            builder.AddGeneral(new("classic:zhang-xiu", "张绣", "supporter", "classic:xiongluan", "qun", 3, ["classic:congjian"]));
            builder.AddGeneral(new("boundary:ma-chao", "界马超", "supporter", "boundary:tieqi", "shu"));
            void RegisterBundle(string bundle, params string[] ids)
            {
                var assembly = typeof(StandardClassicGeneralPackage).Assembly;
                string Read(string suffix)
                {
                    using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
                        .Single(name => name.EndsWith($".{bundle}.{suffix}.json", StringComparison.Ordinal)))!;
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }
                var catalog = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
                foreach (var id in ids)
                {
                    var presentation = catalog.Presentations[id];
                    builder.AddSkill(new(id, presentation.Name, presentation.Description) { Program = catalog.Programs[id],
                        Tags = id == "classic:hujia" ? SkillTag.Lord : SkillTag.None,
                        ActionForms = catalog.Programs[id].Activations.Count > 0 ? SkillActionForm.Active : SkillActionForm.None });
                }
            }
        }
    }

    private sealed class EditionPackage : IGameContentPackage
    {
        internal static readonly string[] Names = ["sima-yi", "xiahou-dun", "zhuge-liang", "huang-yueying",
            "zhou-yu", "gao-shun", "wu-guo-tai", "han-dang"];
        public PackageManifest Manifest { get; } = new("draft-editions", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder) => CharacterFixtureContent.Register(builder,
            Names.SelectMany(name => new[] { "classic:" + name, "boundary:" + name }).ToArray());
    }

    private sealed class BenefitPackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("draft-benefits", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = System.Text.Json.Nodes.JsonNode.Parse($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"fixture:self-draw","revision":1,"activations":[{"id":"draw","targetKind":"anyLiving","effects":[{"op":"draw","target":"owner","amount":2}]}]},
                  {"id":"fixture:alternative-draw","revision":1,"activations":[{"id":"first","usageGroup":"choice","targetKind":"anyLiving","effects":[{"op":"draw","target":"owner","amount":2}]},{"id":"second","usageGroup":"choice","targetKind":"anyLiving","effects":[{"op":"draw","target":"owner","amount":2}]}]},
                  {"id":"fixture:other-draw","revision":1,"activations":[{"id":"draw","targetKind":"otherLiving","effects":[{"op":"draw","target":"selectedTarget","amount":2}]}]},
                  {"id":"fixture:self-recover","revision":1,"activations":[{"id":"heal","targetKind":"anyLiving","effects":[{"op":"recover","target":"owner","amount":1}]}]},
                  {"id":"fixture:other-recover","revision":1,"activations":[{"id":"heal","targetKind":"otherWounded","effects":[{"op":"recover","target":"selectedTarget","amount":1}]}]},
                  {"id":"fixture:paid-draw","revision":1,"activations":[{"id":"draw","targetKind":"anyLiving","effects":[{"op":"loseHp","target":"owner","amount":1},{"op":"draw","target":"owner","amount":2}]}]},
                  {"id":"fixture:limited-recover","revision":1,"activations":[{"id":"heal","targetKind":"anyLiving","usesPerGame":1,"effects":[{"op":"recover","target":"owner","amount":1}]}]},
                  {"id":"fixture:decay","revision":1,"triggers":[{"id":"decay","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
                  {"id":"fixture:growth","revision":1,"triggers":[{"id":"awaken","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"changeMaximumHp","target":"owner","amount":-1},{"op":"grantSkills","target":"owner","skillIds":["fixture:self-draw"]}]}]}
                ]}
                """)!;
            var presentations = new Dictionary<string, object>();
            foreach (var skill in rules["skills"]!.AsArray())
            {
                var id = skill!["id"]!.GetValue<string>();
                presentations.Add(id, new { name = id, description = id });
                if (skill["activations"] is not { } activations) continue;
                foreach (var activation in activations.AsArray())
                {
                    activation!["minCards"] = 0;
                    activation["maxCards"] = 0;
                    activation["minTargets"] = 1;
                    activation["maxTargets"] = 1;
                    activation["usesPerTurn"] = 1;
                }
            }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), System.Text.Json.JsonSerializer.Serialize(new
                { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs)
            {
                builder.AddSkill(new(id, id, id) { Program = program });
                builder.AddGeneral(new(id.Replace("fixture:", "fixture:general-"), id, "supporter", id, "shu", 3));
            }
        }
    }

    private static GeneralDefinition RuntimeGeneral(ContentRegistry registry, string id)
    {
        var definition = registry.Generals[id];
        return new(id, definition.Name, definition.PortraitKey, definition.SkillIds.Select(skillId =>
        {
            var skill = registry.GetSkill(skillId);
            return new GeneralSkillDefinition(skill.Name, skill.Description) { ContentId = skill.Id,
                Tags = skill.Tags, ActionForms = skill.ActionForms, SelectionWeights = skill.SelectionWeights,
                ViewAsOpportunities = skill.Program?.ViewAs.Where(rule => rule.Condition.Kind == SkillProgramConditionKind.Always &&
                    rule.InputCount == 1 && rule.SourceZones.Contains(CardZoneKind.Hand) && rule.InputCategories.Count == 0)
                    .Select(rule => new SkillViewAsOpportunity(rule.OutputKind, rule.InputKinds, rule.InputSuits,
                        rule.ForPlay, rule.ForResponse)).ToArray() };
        }).ToArray(), definition.FactionId, definition.BaseHp) { InitialHp = definition.InitialHp };
    }

    private static double Score(AiGeneralThought thought, GeneralDefinition general) =>
        thought.Candidates.Single(candidate => candidate.GeneralId == general.Id).Score;
    private static GameSnapshot View(GeneralDefinition lord, Role role)
    {
        var players = Enumerable.Range(0, 5).Select(seat => new PlayerSnapshot(seat, $"P{seat}", false,
            seat == 0 ? Role.Lord : seat == 1 ? role : null, seat == 0,
            seat == 0 ? lord.Id : "hidden", seat == 0 ? lord.Name : "未知", "", 4, 4, true, 0, [], seat == 0)
            { Skills = seat == 0 ? lord.Skills : null, FactionId = seat == 0 ? lord.FactionId : null }).ToArray();
        return new(null, -1, EngineStatus.AwaitingHumanGeneralSelection, Winner.None, 0, 0, TurnPhase.NotStarted,
            0, 0, players, null);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
