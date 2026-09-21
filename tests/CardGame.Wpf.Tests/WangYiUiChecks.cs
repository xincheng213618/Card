using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class WangYiUiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:wang-yi";

    public static void CardAndPrivatePrompts(string output)
    {
        RenderGeneralCard(output);
        RenderRuntimePrompts(output);
    }

    private static void RenderGeneralCard(string output)
    {
        using var viewModel = FindGeneralChoice();
        var wangYi = viewModel.GeneralChoices.Single(choice => choice.GeneralId == GeneralId);
        var portrait = wangYi.PortraitBrush as System.Windows.Media.ImageBrush;
        Program.Assert(wangYi.Name == "王异" &&
                       wangYi.Kingdom == "魏" &&
                       wangYi.SkillName == "贞烈 / 秘计" &&
                       wangYi.SkillDescription.Contains("令此牌对你无效", StringComparison.Ordinal) &&
                       wangYi.SkillDescription.Contains("若你已受伤", StringComparison.Ordinal) &&
                       wangYi.SkillDescription.Contains("等量的手牌交给其他角色", StringComparison.Ordinal) &&
                       wangYi.HealthText == "体力上限 4" &&
                       GeneralArt.HasPortrait(wangYi.GeneralId) &&
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
            $"The formal Wang Yi card must render Wei, both exact skills, Lord health and official art " +
            $"(name={wangYi.Name}, kingdom={wangYi.Kingdom}, skills={wangYi.SkillName}, " +
            $"health={wangYi.HealthText}, portrait={portrait?.ImageSource.Width}x{portrait?.ImageSource.Height}).");

        viewModel.PreviewGeneralChoiceCommand.Execute(wangYi);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "193-classic-wang-yi-card.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderRuntimePrompts(string output)
    {
        var fixture = FindSlashFixture();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(
            1,
            DateTimeOffset.UtcNow,
            false,
            fixture.Game.CreateCheckpoint()));
        using var viewModel = new MainViewModel(
            autoAdvance: false,
            seed: fixture.Game.Seed,
            showSetup: true,
            saveStore: store,
            useExpandedContent: true,
            contentRegistry: fixture.Registry)
        {
            IsMotionEnabled = false
        };
        viewModel.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;

        var zhenlie = viewModel.HumanSkillCards.SingleOrDefault(skill => skill.Name == "贞烈");
        var miji = viewModel.HumanSkillCards.SingleOrDefault(skill => skill.Name == "秘计");
        Program.Assert(!viewModel.HasSaveError &&
                       zhenlie is not null &&
                       miji is not null &&
                       viewModel.IsSkillSelectionPending &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "zhenlie-use") &&
                       viewModel.CurrentGuideTitle == "决定是否发动贞烈" &&
                       zhenlie?.TypeText == "触发技" &&
                       zhenlie.StateText == "等待触发时机" &&
                       miji?.TypeText == "触发技" &&
                       miji.StateText == "等待触发时机",
            $"The WPF Zhenlie prompt must expose its private choices and trigger metadata " +
            $"(guide={viewModel.CurrentGuideTitle}, Zhenlie={zhenlie?.TypeText}/{zhenlie?.StateText}, " +
            $"Miji={miji?.TypeText}/{miji?.StateText}, saveStatus={viewModel.SaveStatus}, " +
            $"skills=[{string.Join(',', viewModel.HumanSkillCards.Select(skill => skill.Name))}]).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "194-classic-wang-yi-zhenlie-choice.png"));

        viewModel.SelectSkillChoiceCommand.Execute(viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "zhenlie-use"));
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle == "选择贞烈弃牌" &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "zhenlie-discard-hand" &&
                           choice.Cards.Count == 0) &&
                       Program.Engine(viewModel).CreateSnapshot(HumanSeat, revealAll: true)
                           .Players[HumanSeat].Hp == 3,
            "After paying Zhenlie HP, WPF must render opaque source-hand slots and the exact discard guidance.");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "195-classic-wang-yi-zhenlie-discard.png"));

        viewModel.SelectSkillChoiceCommand.Execute(viewModel.SkillChoices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "zhenlie-discard-hand"));
        if (viewModel.CanStepAi) viewModel.RunToHumanCommand.Execute(null);
        Program.Assert(Program.Engine(viewModel).PendingDecision is
            { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat },
            "The WPF Wang Yi fixture did not return to human play after Zhenlie.");
        viewModel.EndTurnCommand.Execute(null);
        if (viewModel.CanStepAi) viewModel.RunToHumanCommand.Execute(null);
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        Program.Assert(Program.Engine(viewModel).PendingDecision is
                       {
                           Kind: DecisionKind.Miji,
                           PlayerSeat: HumanSeat,
                           IsPrivate: true
                       } &&
                       viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle == "决定是否发动秘计" &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "miji-use"),
            $"The injured end-phase WPF state must render Miji's optional private choice " +
            $"(pending={Program.Engine(viewModel).PendingDecision?.Kind}, guide={viewModel.CurrentGuideTitle}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "196-classic-wang-yi-miji-choice.png"));
        window.Content = null;
        window.Close();
    }

    private static MainViewModel FindGeneralChoice()
    {
        for (var seed = 1; seed <= 1_024; seed++)
        {
            var candidate = new MainViewModel(
                autoAdvance: false,
                seed: seed,
                showSetup: false,
                saveStore: new MemorySaveStore(),
                useExpandedContent: true)
            {
                IsMotionEnabled = false
            };
            if (candidate.GeneralChoices.Any(choice => choice.GeneralId == GeneralId)) return candidate;
            candidate.Dispose();
        }
        throw new InvalidOperationException("Could not find a deterministic classic Wang Yi WPF card fixture.");
    }

    private static Fixture FindSlashFixture()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new ScenarioPackage());
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 4,
                ModeId = ScenarioPackage.ModeId,
                HumanSeat = HumanSeat,
                HumanRole = Role.Lord,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 40
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision?.ValidContentIds.Contains(GeneralId) != true)
            {
                continue;
            }
            var selection = game.PendingDecision!;
            if (!game.Submit(new SelectGeneralCommand(
                    HumanSeat,
                    GeneralId,
                    game.Revision,
                    selection.PromptId)).Accepted)
            {
                continue;
            }
            for (var step = 0; step < 256; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat } play)
                {
                    if (!game.Submit(new EndPlayPhaseCommand(
                            HumanSeat,
                            game.Revision,
                            play.PromptId)).Accepted)
                    {
                        break;
                    }
                    break;
                }
                if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            }
            for (var step = 0; step < 512; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.Zhenlie, PlayerSeat: HumanSeat })
                {
                    return new Fixture(game, registry);
                }
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) break;
                if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            }
        }
        throw new InvalidOperationException("Could not find a deterministic WPF Zhenlie fixture.");
    }

    private sealed record Fixture(GameEngine Game, ContentRegistry Registry);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-wang-yi-wpf-test-4";
        private const string DeckId = "fixture:wang-yi-wpf-deck";
        private static readonly string[] BlankGeneralIds =
        [
            "fixture:wang-yi-wpf-lord",
            "fixture:wang-yi-wpf-supporter-a",
            "fixture:wang-yi-wpf-supporter-b"
        ];

        public PackageManifest Manifest { get; } = new(
            "wang-yi-wpf-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 85, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in BlankGeneralIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "贞烈界面目标",
                    "supporter",
                    "standard:none",
                    "shu",
                    BaseHp: 8));
            }
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "王异界面测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 160)
                    .Select(index => new ContentDeckPhysicalCard(
                        "standard:slash",
                        (Suit)(index % 4),
                        index % 13 + 1))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "王异界面测试",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: [GeneralId, .. BlankGeneralIds]));
        }
    }
}
