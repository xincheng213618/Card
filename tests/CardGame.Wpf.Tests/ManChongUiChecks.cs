using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ManChongUiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:man-chong";

    public static void CardActiveSelectionAndYucePrompt(string output)
    {
        RenderGeneralCard(output);
        RenderJunxingSelection(output);
        RenderYucePrompt(output);
    }

    private static void RenderGeneralCard(string output)
    {
        using var viewModel = FindGeneralChoice();
        var general = viewModel.GeneralChoices.Single(choice => choice.GeneralId == GeneralId);
        var portrait = general.PortraitBrush as System.Windows.Media.ImageBrush;
        Program.Assert(general.Name == "满宠" && general.Kingdom == "魏" &&
                       general.SkillName == "峻刑 / 御策" &&
                       general.SkillDescription.Contains("类别", StringComparison.Ordinal) &&
                       general.SkillDescription.Contains("回复1点体力", StringComparison.Ordinal) &&
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
            $"The Man Chong card must render both classic skills, Lord health and official art " +
            $"(name={general.Name}, skills={general.SkillName}, health={general.HealthText}).");
        viewModel.PreviewGeneralChoiceCommand.Execute(general);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "224-classic-man-chong-card.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderJunxingSelection(string output)
    {
        var registry = Registry();
        var game = CreateGame(registry, seed: 1);
        ReachHumanPlay(game);
        using var viewModel = Load(game, registry);
        var action = viewModel.HumanActiveSkillActions.Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill &&
            candidate.ProgramSkillId == "classic:junxing" &&
            candidate.ProgramActivationId == "category-punishment");
        viewModel.SelectActiveSkillCommand.Execute(action);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var junxing = viewModel.HumanSkillCards.Single(skill => skill.Name == "峻刑");
        var yuce = viewModel.HumanSkillCards.Single(skill => skill.Name == "御策");
        Program.Assert(viewModel.IsActiveSkillSelectionPending &&
                       action.MinCardCount == 1 && action.MaxCardCount >= viewModel.Hand.Count &&
                       action.MinTargetCount == 1 && action.MaxTargetCount == 1 &&
                       action.SelectableCardIds.Count == viewModel.Hand.Count &&
                       action.SelectableTargetSeats.Count == 5 &&
                       viewModel.Hand.All(card => card.IsPlayable &&
                           card.AvailabilityText.Contains("峻刑", StringComparison.Ordinal)) &&
                       junxing.TypeText == "主动技" && yuce.TypeText == "触发技" &&
                       viewModel.ActiveSkillButtonText.Contains("峻刑", StringComparison.Ordinal),
            $"The generic active-skill surface must expose Junxing's one-or-more hand cost and one other target " +
            $"(button={viewModel.ActiveSkillButtonText}, cards={action.SelectableCardIds.Count}, targets={action.SelectableTargetSeats.Count}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "225-classic-junxing-selection.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderYucePrompt(string output)
    {
        var fixture = FindYucePrompt();
        using (var activationViewModel = Load(fixture.Game, fixture.Registry))
        {
            Program.Assert(activationViewModel.IsSkillSelectionPending &&
                           activationViewModel.CurrentGuideTitle == "御策 · 是否发动" &&
                           activationViewModel.CurrentDecisionContext is
                           {
                               Title: "御策 · 是否发动",
                               TargetSeat: HumanSeat
                           } &&
                           activationViewModel.SkillChoices.Select(choice =>
                                   choice.Parameters.GetValueOrDefault("program-action"))
                               .Order(StringComparer.Ordinal)
                               .SequenceEqual(["activate", "skip"]) &&
                           activationViewModel.EventStack.Any(line =>
                               line.Contains("Skill(御策, id: classic:yuce)", StringComparison.Ordinal)) &&
                           activationViewModel.EventStack.Any(line =>
                               line.Contains("AskForActivation(ProgramTrigger)", StringComparison.Ordinal)),
                $"The shared WPF prompt must render Yuce's optional activation gate " +
                $"(guide={activationViewModel.CurrentGuideTitle}, choices={activationViewModel.SkillChoices.Count}).");
        }
        var activationPrompt = fixture.Game.PendingDecision ??
            throw new InvalidOperationException("The Yuce WPF fixture lost its activation prompt.");
        Answer(fixture.Game, activationPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate"));

        using var viewModel = Load(fixture.Game, fixture.Registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var prompt = fixture.Game.PendingDecision ??
            throw new InvalidOperationException("The Yuce WPF fixture lost its prompt.");
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle == "御策 · 选择区域牌" &&
                       viewModel.CurrentDecisionContext is
                       {
                           Title: "御策 · 选择区域牌",
                           TargetSeat: HumanSeat
                       } &&
                       viewModel.SkillChoices.Count > 0 &&
                       viewModel.SkillChoices.All(choice =>
                           choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards" &&
                           choice.Cards.Count == 1) &&
                       viewModel.SkillChoices.Count ==
                           fixture.Game.CreateSnapshot(HumanSeat).Players[HumanSeat].HandCount &&
                       viewModel.EventStack.Any(line =>
                           line.Contains("AskForActivation(ProgramTrigger)", StringComparison.Ordinal)) &&
                       prompt.IsPrivate,
            $"The generic WPF surface must render Yuce's exact-card reveal choices after the optional gate " +
            $"(guide={viewModel.CurrentGuideTitle}, choices={viewModel.SkillChoices.Count}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "226-classic-yuce-reveal-choice.png"));
        window.Content = null;
        window.Close();
    }

    private static (GameEngine Game, ContentRegistry Registry) FindYucePrompt()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = CreateGame(registry, seed);
            for (var step = 0; step < 800 && game.State.Winner == Winner.None; step++)
            {
                if (game.PendingDecision is { PlayerSeat: HumanSeat } prompt)
                {
                    if (prompt.Kind == DecisionKind.ProgramTrigger &&
                        prompt.SkillPrompt?.SkillId == "classic:yuce" &&
                        prompt.Choices.Any(choice =>
                            choice.Parameters.GetValueOrDefault("program-action") == "activate"))
                        return (game, registry);
                    if (prompt.Kind == DecisionKind.PlayCard)
                    {
                        var ended = game.Submit(new EndPlayPhaseCommand(
                            HumanSeat, game.Revision, prompt.PromptId));
                        Program.Assert(ended.Accepted, ended.Error?.Message ?? "Could not end Man Chong Play.");
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
                Program.Assert(advanced.Accepted, advanced.Error?.Message ?? "Could not advance Man Chong fixture.");
            }
        }
        throw new InvalidOperationException("Could not find a bounded WPF Yuce fixture.");
    }

    private static GameEngine CreateGame(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 6,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 40
        }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The WPF Man Chong fixture failed to start.");
        var prompt = RequirePrompt(game, DecisionKind.SelectGeneral);
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat, GeneralId, game.Revision, prompt.PromptId));
        Program.Assert(selected.Accepted, selected.Error?.Message ?? "The WPF fixture could not select Man Chong.");
        return game;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Program.Assert(advanced.Accepted, advanced.Error?.Message ?? "Could not advance to Man Chong Play.");
        }
        throw new InvalidOperationException("The WPF Man Chong fixture did not reach Play.");
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
        throw new InvalidOperationException("Could not find a deterministic Man Chong WPF card fixture.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("No human prompt is pending.");
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
        public const string ModeId = "identity:classic-man-chong-wpf-test-6";
        private const string DeckId = "fixture:man-chong-wpf-categories";
        private static readonly string[] TargetIds =
        [
            "fixture:man-chong-wpf-target-1", "fixture:man-chong-wpf-target-2",
            "fixture:man-chong-wpf-target-3", "fixture:man-chong-wpf-target-4",
            "fixture:man-chong-wpf-target-5"
        ];

        public PackageManifest Manifest { get; } = new(
            "man-chong-wpf-test", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 118, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in TargetIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "满宠界面目标", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId, "满宠界面类别测试牌堆", 4, 2,
                [
                    new ContentDeckCardCount("standard:slash", 32),
                    new ContentDeckCardCount("standard:dodge", 16),
                    new ContentDeckCardCount("standard:dismantlement", 24),
                    new ContentDeckCardCount("standard:crossbow", 24)
                ]));
            builder.AddMode(new ContentModeDefinition(
                ModeId, "六人经典身份（满宠界面场景）", 6, 6,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 3,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId, 6, [GeneralId, .. TargetIds]));
        }
    }
}
