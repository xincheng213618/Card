using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class WuhunUiChecks
{
    private const string WuhunSkillId = "wuhun-ui:wuhun";
    private const string WuhunRules = """
    {"schemaVersion":53,"skills":[{"id":"wuhun-ui:wuhun","revision":3,"minimumRulesVersion":163,
    "modifiers":[],"viewAs":[],"activations":[],"triggers":[
      {"id":"damage-nightmare","window":"damageAppliedBeforeDying","subject":"owner","damageOccurrence":"perDamagePoint","optional":false,"priority":0,
       "effects":[{"op":"changeAttributedMarker","target":"owner","targetRef":{"kind":"eventSource"},"marker":"nightmare","amount":1}]},
      {"id":"death-judgment","window":"ownerDied","subject":"owner","optional":false,"priority":0,
       "effects":[{"op":"selectTarget","target":"owner","targetKind":"maximumAttributedMarker","marker":"nightmare"},
                  {"op":"startJudgment","target":"selectedTarget","judgmentReason":"skill.wuhun.death","resultBind":"judgment","visibility":"public"},
                  {"op":"causeDeathUnlessBoundCardKind","target":"selectedTarget","sourceBind":"judgment","excludedCardKinds":["peach","peachGarden"]}]}
    ],"contributions":[],"cardIdentities":[],"states":[]}]}
    """;
    private const string WuhunPresentation = """
    {"schemaVersion":1,"skills":{"wuhun-ui:wuhun":{"name":"武魂","description":"锁定技，受到伤害后令来源获得梦魇；死亡时令梦魇最多的角色判定，非桃或桃园结义则直接死亡。"}}}
    """;

    public static void DeathTargetChoice(string output)
    {
        var (registry, game) = FindHumanTargetFixture();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(
            1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var viewModel = new MainViewModel(
            autoAdvance: false,
            seed: game.Seed,
            showSetup: true,
            saveStore: store,
            contentRegistry: registry)
        {
            IsMotionEnabled = false
        };
        viewModel.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var engine = Program.Engine(viewModel);
        var prompt = engine.PendingDecision ??
            throw new InvalidOperationException("The restored Wuhun game has no pending decision.");

        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.IsSkillSelectionPending &&
                       prompt is
                       {
                           Kind: DecisionKind.ProgramTrigger,
                           PlayerSeat: 0,
                           IsPrivate: true,
                           SkillPrompt.SkillId: WuhunSkillId
                       } &&
                       viewModel.SkillChoices.Count == prompt.Choices.Count &&
                       viewModel.SkillChoices.All(choice => choice.Targets.Count == 1) &&
                       viewModel.EventStack.Any(line => line.Contains($"Skill(武魂, id: {WuhunSkillId})", StringComparison.Ordinal)) &&
                       engine.ResolutionStack.OfType<ProgramDeathTriggerWindowFrame>().Any() &&
                       engine.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == WuhunSkillId),
            $"WPF must restore the dead owner's private composed Wuhun target choice. " +
            $"saveError={viewModel.SaveStatus}; pending={prompt?.Kind}; choices={viewModel.SkillChoices.Count}.");

        Program.Render(root, 1120, 740, Path.Combine(output, "162-wuhun-death-target.png"));
        var visibleText = Program.Find<TextBlock>(root).Select(text => text.Text).ToArray();
        Program.Assert(visibleText.Any(text => text.Contains("武魂 · 选择目标", StringComparison.Ordinal)) &&
                       visibleText.Any(text => text.Contains("梦魇", StringComparison.Ordinal)) &&
                       viewModel.HumanSkillCards.Any(skill => skill.Name == "武魂"),
            "The Wuhun choice surface must visibly expose its maximum-Nightmare judgment action.");

        viewModel.OpenContextGuideCommand.Execute(null);
        Program.Assert(viewModel.CurrentGuideTitle == "武魂 · 选择目标" &&
                       viewModel.CurrentGuideBody == prompt!.Prompt &&
                       viewModel.CurrentGuideSteps.Any(step => step.Text.Contains("梦魇最多", StringComparison.Ordinal)) &&
                       viewModel.CurrentGuideSteps.Any(step => step.Text.Contains("直接死亡", StringComparison.Ordinal)),
            "The shared program guide must expose Wuhun's marker maximum and direct-death result.");
        Program.Render(root, 1120, 740, Path.Combine(output, "163-wuhun-death-guide.png"));
        viewModel.ToggleHelpCommand.Execute(null);

        var choice = viewModel.SkillChoices[0];
        var targetSeat = choice.Targets.Single();
        viewModel.SelectSkillChoiceCommand.Execute(choice);
        var after = engine.CreateSnapshot(0, revealAll: true);
        Program.Assert(engine.Events.Select(item => item.Payload).OfType<ProgramSkillCauseDeathDeclaredEvent>().Any(item =>
                           item.SourceSeat == 0 && item.TargetSeat == targetSeat && item.SkillId == WuhunSkillId) &&
                       !engine.Events.Select(item => item.Payload).OfType<PlayerDyingEvent>().Any(item =>
                           item.VictimSeat == targetSeat) &&
                       !after.Players[targetSeat].IsAlive &&
                       engine.PendingDecision?.Choices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("skill-id") == WuhunSkillId) != true,
            "Selecting a composed Wuhun target through WPF must complete its direct-death chain without a rescue prompt.");

        window.Content = null;
        window.Close();
    }

    private static (ContentRegistry Registry, GameEngine Game) FindHumanTargetFixture()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new WuhunUiFixturePackage());
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 4,
                HumanSeat = 0,
                HumanRole = Role.Rebel,
                ModeId = WuhunUiFixturePackage.ModeId,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 20
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted)
                continue;
            var full = game.CreateSnapshot(0, revealAll: true);
            if (full.Players[0].GeneralId != WuhunUiFixturePackage.OwnerId)
                continue;

            for (var step = 0; step < 256; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } targetPrompt &&
                    targetPrompt.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "select-target" &&
                        choice.Parameters.GetValueOrDefault("target-kind") ==
                        nameof(SkillProgramTargetKind.MaximumAttributedMarker)))
                    return (registry, game);
                if (game.State.Status == EngineStatus.Completed)
                    break;

                GameCommand command = game.PendingDecision is { PlayerSeat: 0 } pending
                    ? pending.Kind == DecisionKind.PlayCard
                        ? new EndPlayPhaseCommand(0, game.Revision, pending.PromptId)
                        : new AnswerPromptCommand(
                            0,
                            pending.PromptId,
                            pending.Choices.First(choice => choice.Cards.Count == 0).Id,
                            game.Revision)
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted)
                    break;
            }
        }

        throw new InvalidOperationException("Could not find a bounded WPF Wuhun target fixture.");
    }

    private sealed class WuhunUiFixturePackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-wuhun-ui-4";
        public const string AttackerId = "wuhun-ui:attacker";
        public const string OwnerId = "wuhun-ui:owner";
        private const string DeckId = "wuhun-ui:deck";

        public PackageManifest Manifest { get; } = new(
            "wuhun-ui",
            new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var wuhun = SkillProgramCatalog.Load(WuhunRules, WuhunPresentation).Programs[WuhunSkillId];
            builder.AddSkill(new ContentSkillDefinition(
                WuhunSkillId,
                "武魂",
                "锁定技，受到伤害后令来源获得梦魇；死亡时令梦魇最多的角色判定，非桃或桃园结义则直接死亡。")
            {
                Program = wuhun,
                Tags = SkillTag.Locked,
                ExecutionForms = SkillExecutionForm.State
            });
            builder.AddGeneral(new ContentGeneralDefinition(
                AttackerId, "攻击者", "guan_yu", "standard:none", "wei", BaseHp: 4));
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerId, "武魂测试者", "shen-guan-yu", "wuhun-ui:wuhun", "god", BaseHp: 1));
            builder.AddGeneral(new ContentGeneralDefinition(
                "wuhun-ui:bystander-1", "旁观者一", "liu_bei", "standard:none", "shu", BaseHp: 4));
            builder.AddGeneral(new ContentGeneralDefinition(
                "wuhun-ui:bystander-2", "旁观者二", "sun_quan", "standard:none", "wu", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "武魂 WPF 测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 0,
                [new ContentDeckCardCount("standard:slash", 60)]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "武魂 WPF 测试",
                MinPlayers: 4,
                MaxPlayers: 4,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 1,
                GeneralPoolIds:
                [
                    AttackerId,
                    OwnerId,
                    "wuhun-ui:bystander-1",
                    "wuhun-ui:bystander-2"
                ]));
        }
    }
}
