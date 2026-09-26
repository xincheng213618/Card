using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ProgramChoiceUiChecks
{
    private const string SkillId = "fixture:ui-choice";

    public static void NamedChoiceUsesSharedSurfaceAndCommand(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new ChoicePackage());
        var game = ReachChoice(registry);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(autoAdvance: false, seed: game.Seed, showSetup: true,
            saveStore: store, useExpandedContent: true, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var prompt = game.PendingDecision!;
        Program.Assert(model.IsSkillSelectionPending && model.SkillChoices.Count == 2 &&
                       model.SkillChoices.Select(choice => choice.Description).SequenceEqual(["摸两张牌", "保持当前状态"]) &&
                       model.CurrentDecisionContext is { Title: "公共选择 · 选择效果", TargetSeat: 0 } &&
                       model.CurrentGuideTitle == prompt.SkillPrompt!.Title &&
                       game.CreateSnapshot(1).PendingDecision is null,
            "Named choices must use shared metadata, labels, guide and private responder projection.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "230-program-named-choice.png"));
        var before = game.CreateSnapshot(0).Players[0].HandCount;
        model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("option-id") == "draw"));
        Program.Assert(game.CreateSnapshot(0).Players[0].HandCount == before + 2 &&
                       game.Events.Count(item => item.Payload is ProgramOptionChosenEvent { SkillId: SkillId, OptionId: "draw" }) == 1 &&
                       model.BattleCues.Any(cue => cue.Label == "公共选择 · 摸两张牌") &&
                       !game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Any(),
            $"The shared WPF command must resolve one named choice and resume the parent once. " +
            $"Hand={before}->{game.CreateSnapshot(0).Players[0].HandCount}; " +
            $"choices={game.Events.Count(item => item.Payload is ProgramOptionChosenEvent)}; " +
            $"parents={game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Count()}; " +
            $"cues={string.Join("|", model.BattleCues.Select(cue => cue.Label))}");
        window.Content = null;
        window.Close();
    }

    private static GameEngine ReachChoice(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 32; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
                ModeId = ChoicePackage.ModeId, UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
            }, registry);
            Accept(game.Submit(new StartGameCommand()));
            if (game.PendingDecision is not { Kind: DecisionKind.SelectGeneral } setup ||
                !setup.ValidContentIds.Contains("fixture:chooser")) continue;
            Accept(game.Submit(new SelectGeneralCommand(0, "fixture:chooser", game.Revision, setup.PromptId)));
            for (var step = 0; step < 64; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: SkillId }) return game;
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play)
                    Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId)));
                else Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            }
        }
        throw new InvalidOperationException("The bounded UI fixture did not reach the named choice.");
    }

    private static void Accept(CommandResult result) => Program.Assert(result.Accepted,
        result.Error?.Message ?? "Expected accepted fixture command.");

    private sealed class ChoicePackage : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-ui-choice-4";
        public PackageManifest Manifest { get; } = new("fixture-ui-choice", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 0, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load("""
                {"schemaVersion":60,"skills":[{"id":"fixture:ui-choice","revision":1,
                "minimumRulesVersion":170,"triggers":[{"id":"choice","window":"turnEnding",
                "subject":"owner","optional":false,"effects":[
                {"op":"chooseOption","target":"owner","resultBind":"answer","options":[{"id":"draw"},{"id":"stay"}]},
                {"op":"draw","target":"owner","amount":2,"condition":{"kind":"choiceIs","sourceBind":"answer","optionId":"draw"}}
                ]}]}]}
                """, """
                {"schemaVersion":3,"skills":{"fixture:ui-choice":{"name":"公共选择","description":"公共选项与普通摸牌组合。",
                "optionLabels":{"draw":"摸两张牌","stay":"保持当前状态"}}}}
                """);
            builder.AddSkill(new ContentSkillDefinition(SkillId, "公共选择", "公共选项与普通摸牌组合。")
            { Program = catalog.Programs[SkillId], ProgramPresentation = catalog.Presentations[SkillId] });
            builder.AddGeneral(new ContentGeneralDefinition("fixture:chooser", "选择者", "supporter", SkillId, "shu", BaseHp: 4));
            for (var index = 1; index < 4; index++)
                builder.AddGeneral(new ContentGeneralDefinition($"fixture:other-{index}", $"角色{index}", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:ui-choice-deck", "测试牌堆", 4, 0,
                [new ContentDeckCardCount("standard:slash", 60)]));
            builder.AddMode(new ContentModeDefinition(ModeId, "公共选择测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "fixture:ui-choice-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:chooser", "fixture:other-1", "fixture:other-2", "fixture:other-3"]));
        }
    }
}
