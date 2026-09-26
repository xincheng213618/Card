using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class GuanPingUiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:guan-ping";

    public static void CardAndLongyinPrompt(string output)
    {
        RenderGeneralCard(output);
        RenderLongyinPrompt(output);
    }

    private static void RenderGeneralCard(string output)
    {
        using var viewModel = FindGeneralChoice();
        var general = viewModel.GeneralChoices.Single(choice => choice.GeneralId == GeneralId);
        var portrait = general.PortraitBrush as System.Windows.Media.ImageBrush;
        Program.Assert(general.Name == "关平" && general.Kingdom == "蜀" &&
                       general.SkillName == "龙吟" &&
                       general.SkillDescription.Contains("不计入限制的使用次数", StringComparison.Ordinal) &&
                       general.SkillDescription.Contains("红色", StringComparison.Ordinal) &&
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
            $"The Guan Ping card must render Longyin, Lord health and official art " +
            $"(name={general.Name}, skills={general.SkillName}, health={general.HealthText}).");
        viewModel.PreviewGeneralChoiceCommand.Execute(general);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "227-classic-guan-ping-card.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderLongyinPrompt(string output)
    {
        var fixture = FindLongyinPrompt();
        var activation = fixture.Game.PendingDecision!;
        using (var preview = Load(fixture.Game, fixture.Registry))
        {
            Program.Assert(preview.IsSkillSelectionPending && preview.SkillChoices.Count == 2 &&
                           preview.CurrentDecisionContext?.Title.Contains("龙吟", StringComparison.Ordinal) == true &&
                           preview.SkillChoices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip"),
                "The common trigger surface must display optional activation and skip.");
        }
        var use = activation.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        var accepted = fixture.Game.Submit(new AnswerPromptCommand(HumanSeat, activation.PromptId, use.Id, fixture.Game.Revision));
        Program.Assert(accepted.Accepted, accepted.Error?.Message ?? "The common activation failed.");
        using var viewModel = Load(fixture.Game, fixture.Registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var prompt = fixture.Game.PendingDecision!;
        var owner = fixture.Game.CreateSnapshot(HumanSeat).Players[HumanSeat];
        Program.Assert(prompt.SkillPrompt?.SkillId == "classic:longyin" && prompt.IsPrivate &&
                       viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentDecisionContext?.Title.Contains("龙吟", StringComparison.Ordinal) == true &&
                       viewModel.SkillChoices.Count == owner.HandCount + owner.Equipment.Count &&
                       viewModel.SkillChoices.All(choice => choice.Cards.Count == 1 &&
                           choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") &&
                       fixture.Game.CreateSnapshot(1).PendingDecision is null &&
                       viewModel.HumanSkillCards.Single(skill => skill.Name == "龙吟").TypeText == "触发技",
            "The generic WPF selection surface must display private exact owned-card costs after activation.");
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "228-classic-longyin-choice.png"));
        window.Content = null;
        window.Close();
    }

    private static (GameEngine Game, ContentRegistry Registry) FindLongyinPrompt()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = CreateGame(registry, seed);
            ReachHumanPlay(game);
            var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.Slash && candidate.CardId is not null);
            if (action is null) continue;
            var prompt = RequirePrompt(game, DecisionKind.PlayCard);
            var played = game.Submit(new PlayCardCommand(
                HumanSeat,
                action.CardId!.Value,
                action.TargetSeats,
                game.Revision,
                prompt.PromptId,
                action.PlayedCardKind));
            Program.Assert(played.Accepted, played.Error?.Message ?? "Could not play the WPF Longyin Slash.");
            if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat, SkillPrompt.SkillId: "classic:longyin" })
                return (game, registry);
        }
        throw new InvalidOperationException("Could not find a bounded WPF Longyin fixture.");
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
            MaxTurns = 20
        }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The WPF Guan Ping fixture failed to start.");
        var prompt = RequirePrompt(game, DecisionKind.SelectGeneral);
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat, GeneralId, game.Revision, prompt.PromptId));
        Program.Assert(selected.Accepted, selected.Error?.Message ?? "The WPF fixture could not select Guan Ping.");
        return game;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Program.Assert(advanced.Accepted, advanced.Error?.Message ?? "Could not advance to Guan Ping Play.");
        }
        throw new InvalidOperationException("The WPF Guan Ping fixture did not reach Play.");
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
        throw new InvalidOperationException("Could not find a deterministic Guan Ping WPF card fixture.");
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
        public const string ModeId = "identity:classic-guan-ping-wpf-test-6";
        private const string DeckId = "fixture:guan-ping-wpf-slashes";
        private static readonly string[] TargetIds =
        [
            "fixture:guan-ping-wpf-target-1", "fixture:guan-ping-wpf-target-2",
            "fixture:guan-ping-wpf-target-3", "fixture:guan-ping-wpf-target-4",
            "fixture:guan-ping-wpf-target-5"
        ];

        public PackageManifest Manifest { get; } = new(
            "guan-ping-wpf-test", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 97, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in TargetIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "关平界面目标", "supporter", "standard:none", "wei", BaseHp: 8));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId, "关平界面龙吟测试牌堆", 8, 2,
                [
                    new ContentDeckCardCount("standard:slash", 96),
                    new ContentDeckCardCount("standard:dodge", 48)
                ]));
            builder.AddMode(new ContentModeDefinition(
                ModeId, "六人经典身份（关平界面场景）", 6, 6,
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
