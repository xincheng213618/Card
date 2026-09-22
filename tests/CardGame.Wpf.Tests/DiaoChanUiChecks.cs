using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class DiaoChanUiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:diao-chan";

    public static void BiyuePromptAndContinuation(string output)
    {
        var activationFixture = CreateReadyGame();
        using (var viewModel = Load(activationFixture.Game, activationFixture.Registry))
        {
            var window = new MainWindow(viewModel);
            window.ApplyTemplate();
            var root = (FrameworkElement)window.Content;
            var game = Program.Engine(viewModel);
            var prompt = RequireBiyuePrompt(game);
            AssertGenericPrompt(viewModel, prompt);
            var handBefore = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count;
            var use = viewModel.SkillChoices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate");

            Program.Render(root, 1120, 740,
                Path.Combine(output, "224-classic-biyue-finished-phase.png"));
            viewModel.SelectSkillChoiceCommand.Execute(use);

            Program.Assert(game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count == handBefore + 1 &&
                           game.PendingDecision?.PromptId != prompt.PromptId &&
                           !game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Any() &&
                           game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                               .Count(item => item.SkillId == "classic:biyue" && item.OwnerSeat == HumanSeat &&
                                   item.Activated && item.Completed) == 1 &&
                           viewModel.BattleCues.Count(cue => cue.Label == "闭月 · 已发动") == 1,
                "Clicking the metadata-driven Biyue activation must draw once, resume the turn ending, and emit one cue.");
            window.Content = null;
            window.Close();
        }

        var skipFixture = CreateReadyGame();
        using (var viewModel = Load(skipFixture.Game, skipFixture.Registry))
        {
            var game = Program.Engine(viewModel);
            var prompt = RequireBiyuePrompt(game);
            AssertGenericPrompt(viewModel, prompt);
            var handBefore = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count;
            var skip = viewModel.SkillChoices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "skip");

            viewModel.SelectSkillChoiceCommand.Execute(skip);

            Program.Assert(game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count == handBefore &&
                           game.PendingDecision?.PromptId != prompt.PromptId &&
                           !game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Any() &&
                           game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                               .Count(item => item.SkillId == "classic:biyue" && item.OwnerSeat == HumanSeat &&
                                   !item.Activated && !item.Completed) == 1,
                "Skipping the metadata-driven Biyue choice must preserve the hand and resume the turn ending.");
        }
    }

    private static void AssertGenericPrompt(MainViewModel viewModel, PendingDecision prompt)
    {
        var presentation = prompt.SkillPrompt;
        Program.Assert(prompt.IsPrivate &&
                       presentation is
                       {
                           SkillId: "classic:biyue",
                           Name: "闭月",
                           Title: "闭月 · 是否发动"
                       } &&
                       viewModel.IsSkillSelectionPending &&
                       viewModel.SkillChoices.Select(choice => choice.Parameters.GetValueOrDefault("program-action"))
                           .Order(StringComparer.Ordinal)
                           .SequenceEqual(["activate", "skip"]) &&
                       viewModel.CurrentDecisionContext is
                       {
                           Title: "闭月 · 是否发动",
                           TargetSeat: HumanSeat
                       } &&
                       viewModel.CurrentDecisionContext.Description == prompt.Prompt &&
                       viewModel.CurrentGuideTitle == presentation.Title &&
                       viewModel.CurrentGuideSteps.Count == 1 &&
                       viewModel.CurrentGuideSteps[0].Text == presentation.Instructions &&
                       viewModel.EventStack.Any(line =>
                           line.Contains("Skill(闭月, id: classic:biyue)", StringComparison.Ordinal)),
            "The generic WPF skill surface must expose Biyue metadata, both actions, private context, and guidance.");
    }

    private static (GameEngine Game, ContentRegistry Registry) CreateReadyGame()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = ScenarioPackage.ModeId,
                HumanSeat = HumanSeat,
                HumanRole = Role.Lord,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 20
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { } selection ||
                !selection.ValidContentIds.Contains(GeneralId))
            {
                continue;
            }
            var selected = game.Submit(new SelectGeneralCommand(
                HumanSeat, GeneralId, game.Revision, selection.PromptId));
            Program.Assert(selected.Accepted, selected.Error?.Message ?? "The WPF fixture could not select Diao Chan.");
            ReachHumanPlay(game);
            var play = game.PendingDecision!;
            var ended = game.Submit(new EndPlayPhaseCommand(HumanSeat, game.Revision, play.PromptId));
            Program.Assert(ended.Accepted, ended.Error?.Message ?? "The WPF fixture could not end Diao Chan's Play phase.");
            for (var step = 0; step < 32 && game.PendingDecision?.SkillPrompt?.SkillId != "classic:biyue"; step++)
            {
                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Program.Assert(advanced.Accepted, advanced.Error?.Message ?? "The WPF fixture could not reach Biyue.");
            }
            RequireBiyuePrompt(game);
            return (game, registry);
        }
        throw new InvalidOperationException("Could not find a deterministic WPF Biyue fixture.");
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 128; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Program.Assert(advanced.Accepted, advanced.Error?.Message ?? "Could not advance the WPF Biyue fixture.");
        }
        throw new InvalidOperationException("The WPF Biyue fixture did not reach human Play.");
    }

    private static MainViewModel Load(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var viewModel = new MainViewModel(
            autoAdvance: false,
            seed: game.Seed,
            showSetup: true,
            saveStore: store,
            useExpandedContent: true,
            contentRegistry: registry)
        {
            IsMotionEnabled = false
        };
        viewModel.LoadManualGameCommand.Execute(null);
        Program.Assert(!viewModel.HasSaveError, viewModel.SaveStatus);
        return viewModel;
    }

    private static PendingDecision RequireBiyuePrompt(GameEngine game) =>
        game.PendingDecision is { PlayerSeat: HumanSeat, SkillPrompt.SkillId: "classic:biyue" } prompt
            ? prompt
            : throw new InvalidOperationException(
                $"Expected the metadata-driven Biyue prompt, found {game.PendingDecision?.Kind} / {game.PendingDecision?.SkillPrompt?.SkillId}.");

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-diao-chan-wpf-test-5";
        private const string DeckId = "fixture:diao-chan-wpf-deck";
        private static readonly string[] TargetIds =
        [
            "fixture:diao-chan-wpf-target-1", "fixture:diao-chan-wpf-target-2",
            "fixture:diao-chan-wpf-target-3", "fixture:diao-chan-wpf-target-4"
        ];

        public PackageManifest Manifest { get; } = new(
            "diao-chan-wpf-test", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in TargetIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "闭月界面目标", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId, "闭月界面测试牌堆", 4, 1,
                [new ContentDeckCardCount("standard:crossbow", 64)]));
            builder.AddMode(new ContentModeDefinition(
                ModeId, "五人经典身份（闭月界面场景）", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId, 5, [GeneralId, .. TargetIds]));
        }
    }
}
