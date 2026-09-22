using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class GuoHuaiUiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:guo-huai";

    public static void CardAndJingcePrompt(string output)
    {
        RenderGeneralCard(output);
        RenderJingcePrompt(output);
    }

    private static void RenderGeneralCard(string output)
    {
        using var viewModel = FindGeneralChoice();
        var general = viewModel.GeneralChoices.Single(choice => choice.GeneralId == GeneralId);
        var portrait = general.PortraitBrush as System.Windows.Media.ImageBrush;
        Program.Assert(general.Name == "郭淮" && general.Kingdom == "魏" &&
                       general.SkillName == "精策" &&
                       general.SkillDescription.Contains("使用过的牌的数量", StringComparison.Ordinal) &&
                       general.SkillDescription.Contains("当前的体力值", StringComparison.Ordinal) &&
                       general.HealthText == "体力上限 5" &&
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
            $"The Guo Huai card must render exact Jingce, Lord health and official art " +
            $"(name={general.Name}, skill={general.SkillName}, health={general.HealthText}).");
        viewModel.PreviewGeneralChoiceCommand.Execute(general);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "222-classic-guo-huai-card.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderJingcePrompt(string output)
    {
        var (game, registry) = CreateReadyGame();
        PlayCrossbows(game, count: 5);
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        var ended = game.Submit(new EndPlayPhaseCommand(
            HumanSeat, game.Revision, play.PromptId));
        Program.Assert(ended.Accepted, ended.Error?.Message ?? "Could not end Guo Huai Play.");
        using var viewModel = Load(game, registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var loadedGame = Program.Engine(viewModel);
        var prompt = RequirePrompt(loadedGame, DecisionKind.ProgramTrigger);
        var presentation = prompt.SkillPrompt;
        var jingce = viewModel.HumanSkillCards.Single(skill => skill.Name == "精策");
        Program.Assert(presentation is
                       {
                           SkillId: "classic:jingce",
                           Name: "精策",
                           Title: "精策 · 是否发动"
                       } &&
                       viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle == presentation.Title &&
                       viewModel.CurrentGuideSteps.Count == 1 &&
                       viewModel.CurrentGuideSteps[0].Text == presentation.Instructions &&
                       viewModel.CurrentDecisionContext is
                       {
                           Title: "精策 · 是否发动",
                           TargetSeat: HumanSeat
                       } &&
                       viewModel.SkillChoices.Select(choice =>
                           choice.Parameters.GetValueOrDefault("program-action"))
                           .Order(StringComparer.Ordinal)
                           .SequenceEqual(["activate", "skip"]) &&
                       viewModel.SkillChoices.All(choice =>
                           choice.Parameters.GetValueOrDefault("cards-used-this-turn") == "5" &&
                           choice.Parameters.GetValueOrDefault("current-hp") == "5") &&
                       jingce.TypeText == "触发技" &&
                       viewModel.EventStack.Any(line =>
                           line.Contains("Skill(精策, id: classic:jingce)", StringComparison.Ordinal)),
            $"The generic WPF surface must expose Jingce's exact count and current-HP comparison " +
            $"(guide={viewModel.CurrentGuideTitle}, choices={viewModel.SkillChoices.Count}).");
        var promptId = prompt.PromptId;
        var handBefore = loadedGame.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count;
        var use = viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "223-classic-jingce-play-end.png"));
        viewModel.SelectSkillChoiceCommand.Execute(use);
        Program.Assert(loadedGame.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count == handBefore + 2 &&
                       loadedGame.PendingDecision?.PromptId != promptId &&
                       loadedGame.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                           .Count(item => item.SkillId == "classic:jingce" && item.OwnerSeat == HumanSeat &&
                               item.Activated && item.Completed) == 1 &&
                       viewModel.BattleCues.Count(cue => cue.Label.StartsWith("精策 ·", StringComparison.Ordinal)) == 1,
            "The metadata-driven Jingce choice must resume play-end exactly once without duplicate battle cues.");
        window.Content = null;
        window.Close();
    }

    private static (GameEngine Game, ContentRegistry Registry) CreateReadyGame()
    {
        var registry = Registry();
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 670021,
            PlayerCount = 6,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The WPF Guo Huai fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat, GeneralId, game.Revision, selection.PromptId));
        Program.Assert(selected.Accepted, selected.Error?.Message ?? "The WPF fixture could not select Guo Huai.");
        ReachHumanPlay(game);
        return (game, registry);
    }

    private static void PlayCrossbows(GameEngine game, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var prompt = RequirePrompt(game, DecisionKind.PlayCard);
            var hand = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand;
            var action = game.GetHumanLegalActions().First(candidate =>
                candidate.CardId is { } cardId &&
                hand.Single(card => card.Id == cardId).Kind == CardKind.Crossbow);
            var played = game.Submit(new PlayCardCommand(
                HumanSeat,
                action.CardId!.Value,
                action.TargetSeats,
                game.Revision,
                prompt.PromptId,
                action.PlayedCardKind,
                action.TargetCardId)
            {
                ConversionSource = action.ConversionSource,
                CardKindModifierSkill = action.CardKindModifierSkill,
                TargetCountModifierSkill = action.TargetCountModifierSkill
            });
            Program.Assert(played.Accepted, played.Error?.Message ?? "Could not use Crossbow.");
            ReachHumanPlay(game);
        }
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 128; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Program.Assert(advanced.Accepted, advanced.Error?.Message ?? "Could not advance Guo Huai fixture.");
        }
        throw new InvalidOperationException("The WPF fixture did not reach human Play.");
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
        throw new InvalidOperationException("Could not find a deterministic Guo Huai WPF card fixture.");
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
        public const string ModeId = "identity:classic-guo-huai-wpf-test-6";
        private const string DeckId = "fixture:guo-huai-wpf-card-use";
        private static readonly string[] TargetIds =
        [
            "fixture:guo-huai-wpf-target-1", "fixture:guo-huai-wpf-target-2",
            "fixture:guo-huai-wpf-target-3", "fixture:guo-huai-wpf-target-4",
            "fixture:guo-huai-wpf-target-5"
        ];

        public PackageManifest Manifest { get; } = new(
            "guo-huai-wpf-test", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 99, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in TargetIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "郭淮界面目标", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId, "郭淮界面用牌计数测试牌堆", 4, 2,
                [new ContentDeckCardCount("standard:crossbow", 96)]));
            builder.AddMode(new ContentModeDefinition(
                ModeId, "六人经典身份（郭淮界面场景）", 6, 6,
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
