using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class CaoChongUiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:cao-chong";

    public static void CardAndDamagePrompts(string output)
    {
        RenderGeneralCard(output);
        RenderChengxiangPrompt(output);
        RenderRenxinPrompt(output);
    }

    private static void RenderGeneralCard(string output)
    {
        using var viewModel = FindGeneralChoice();
        var general = viewModel.GeneralChoices.Single(choice => choice.GeneralId == GeneralId);
        var portrait = general.PortraitBrush as System.Windows.Media.ImageBrush;
        Program.Assert(general.Name == "曹冲" && general.Kingdom == "魏" &&
                       general.SkillName == "称象 / 仁心" &&
                       general.SkillDescription.Contains("点数之和小于等于13", StringComparison.Ordinal) &&
                       general.SkillDescription.Contains("防止此伤害", StringComparison.Ordinal) &&
                       general.HealthText == "体力上限 4" &&
                       GeneralArt.HasPortrait(general.GeneralId) &&
                       portrait is
                       {
                           Stretch: System.Windows.Media.Stretch.UniformToFill,
                           AlignmentY: System.Windows.Media.AlignmentY.Top,
                           ImageSource: System.Windows.Media.Imaging.BitmapSource
                           {
                               PixelWidth: 574,
                               PixelHeight: 761
                           }
                       },
            $"The Cao Chong card must render both exact skills, Lord health and official art " +
            $"(name={general.Name}, skills={general.SkillName}, health={general.HealthText}).");
        viewModel.PreviewGeneralChoiceCommand.Execute(general);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "219-classic-cao-chong-card.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderChengxiangPrompt(string output)
    {
        var fixture = FindPrompt(DecisionKind.ProgramTrigger, "classic:chengxiang");
        AnswerAction(fixture.Game, "activate", "program-action");
        using var viewModel = Load(fixture.Game, fixture.Registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var chengxiang = viewModel.HumanSkillCards.Single(skill => skill.Name == "称象");
        var renxin = viewModel.HumanSkillCards.Single(skill => skill.Name == "仁心");
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle == "称象 · 选择牌" &&
                       viewModel.PublicRevealTitle == "称象 · 公开牌" &&
                       viewModel.PublicRevealedCards.Count == 4 &&
                       viewModel.SkillChoices.All(choice =>
                           choice.Parameters.GetValueOrDefault("program-action") == "select-subset") &&
                       chengxiang.TypeText == "触发技" && renxin.TypeText == "触发技" &&
                       fixture.Game.PendingDecision is
                       {
                           Kind: DecisionKind.ProgramTrigger,
                           IsPrivate: true,
                           SkillPrompt.SkillId: "classic:chengxiang"
                       },
            $"The generic WPF surface must expose Chengxiang public cards and legal subset buttons " +
            $"(guide={viewModel.CurrentGuideTitle}, public={viewModel.PublicRevealedCards.Count}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "220-classic-chengxiang-subset.png"));
        var selected = viewModel.SkillChoices
            .Where(choice => choice.Cards.Count > 0)
            .OrderByDescending(choice => choice.Cards.Count)
            .First();
        var selectedIds = selected.Cards.ToHashSet();
        viewModel.SelectSkillChoiceCommand.Execute(selected);
        var engine = Program.Engine(viewModel);
        Program.Assert(selectedIds.All(cardId => engine.CardMovements.Any(move =>
                           move.CardId == cardId && move.To == CardLocation.Hand(HumanSeat))) &&
                       engine.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                           item.SkillId == "classic:chengxiang" && item is { Activated: true, Completed: true }),
            "The generic ProgramTrigger subset command must be accepted and move the chosen cards to hand.");
        window.Content = null;
        window.Close();
    }

    private static void RenderRenxinPrompt(string output)
    {
        var fixture = FindPrompt(DecisionKind.ProgramTrigger, "classic:renxin");
        AnswerAction(fixture.Game, "activate", "program-action");
        using var viewModel = Load(fixture.Game, fixture.Registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var targetSeat = fixture.Game.PendingDecision?.TargetSeat;
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle == "仁心 · 选择支付牌" &&
                       viewModel.CurrentDecisionContext is { TargetSeat: var contextTarget } &&
                       contextTarget == targetSeat &&
                       viewModel.SkillChoices.Count > 0 &&
                       viewModel.SkillChoices.All(choice =>
                            choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card" &&
                            choice.Cards.Count == 1) &&
                       viewModel.EventStack.Any(line =>
                            line.Contains("Skill(仁心", StringComparison.Ordinal)),
            $"The generic WPF surface must show exact Renxin equipment costs and its protected target " +
            $"(guide={viewModel.CurrentGuideTitle}, choices={viewModel.SkillChoices.Count}, target={targetSeat}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "221-classic-renxin-prevention.png"));
        window.Content = null;
        window.Close();
    }

    private static (GameEngine Game, ContentRegistry Registry) FindPrompt(
        DecisionKind sought,
        string? skillId = null)
    {
        var registry = Registry();
        for (var seed = 1; seed <= 1024; seed++)
        {
            var game = TryCreateGame(registry, seed);
            if (game is null) continue;
            for (var step = 0; step < 1200 && game.State.Winner == Winner.None; step++)
            {
                if (game.PendingDecision is { PlayerSeat: HumanSeat } prompt)
                {
                    if (prompt.Kind == sought &&
                        (skillId is null || prompt.SkillPrompt?.SkillId == skillId)) return (game, registry);
                    if (prompt.Kind == DecisionKind.PlayCard)
                    {
                        var ended = game.Submit(new EndPlayPhaseCommand(
                            HumanSeat, game.Revision, prompt.PromptId));
                        Program.Assert(ended.Accepted, ended.Error?.Message ?? "Could not end Cao Chong Play.");
                        continue;
                    }
                    if (prompt.Kind == DecisionKind.ProgramTrigger)
                    {
                        AnswerAction(game, "skip", "program-action");
                        continue;
                    }
                    if (prompt.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying)
                    {
                        Answer(game, prompt.Choices.First(choice => choice.Cards.Count == 0));
                        continue;
                    }
                    break;
                }

                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Program.Assert(advanced.Accepted, advanced.Error?.Message ?? "Could not advance Cao Chong fixture.");
            }
        }
        throw new InvalidOperationException($"Could not find a bounded WPF {sought} fixture.");
    }

    private static GameEngine? TryCreateGame(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 6,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Rebel,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 40
        }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The WPF Cao Chong fixture failed to start.");
        var prompt = RequirePrompt(game, DecisionKind.SelectGeneral);
        if (!prompt.ValidContentIds.Contains(GeneralId)) return null;
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat, GeneralId, game.Revision, prompt.PromptId));
        Program.Assert(selected.Accepted, selected.Error?.Message ?? "The WPF fixture could not select Cao Chong.");
        return game;
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

    private static MainViewModel FindGeneralChoice()
    {
        for (var seed = 1; seed <= 4096; seed++)
        {
            var candidate = new MainViewModel(
                autoAdvance: false,
                seed,
                showSetup: false,
                saveStore: new MemorySaveStore(),
                useExpandedContent: true)
            {
                IsMotionEnabled = false
            };
            if (candidate.GeneralChoices.Any(choice => choice.GeneralId == GeneralId)) return candidate;
            candidate.Dispose();
        }
        throw new InvalidOperationException("Could not find a deterministic Cao Chong WPF card fixture.");
    }

    private static void AnswerAction(GameEngine game, string action, string parameter = "action")
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("No prompt is pending.");
        Answer(game, prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault(parameter) == action));
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("No prompt is pending.");
        var answered = game.Submit(new AnswerPromptCommand(
            prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
        Program.Assert(answered.Accepted, answered.Error?.Message ?? $"The {prompt.Kind} answer was rejected.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException($"Expected {kind}, found {game.PendingDecision?.Kind}.");

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-cao-chong-wpf-test-6";
        private const string DeckId = "fixture:cao-chong-wpf-combat";
        private static readonly string[] TargetIds =
        [
            "fixture:cao-chong-wpf-target-1", "fixture:cao-chong-wpf-target-2",
            "fixture:cao-chong-wpf-target-3", "fixture:cao-chong-wpf-target-4",
            "fixture:cao-chong-wpf-target-5"
        ];

        public PackageManifest Manifest { get; } = new(
            "cao-chong-wpf-test", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 94, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in TargetIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "曹冲界面目标", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId, "曹冲界面伤害测试牌堆", 4, 2,
                [
                    new ContentDeckCardCount("standard:slash", 64),
                    new ContentDeckCardCount("standard:crossbow", 24),
                    new ContentDeckCardCount("standard:peach", 16)
                ]));
            builder.AddMode(new ContentModeDefinition(
                ModeId, "六人经典身份（曹冲界面场景）", 6, 6,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 3,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId, 1, [GeneralId, .. TargetIds]));
        }
    }
}
