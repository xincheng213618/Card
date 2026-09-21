using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class ChengPuUiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:cheng-pu";

    public static void CardStorageAndRescue(string output)
    {
        RenderGeneralCard(output);
        RenderStoragePromptAndPublicPile(output);
        RenderDyingRescue(output);
    }

    private static void RenderGeneralCard(string output)
    {
        using var viewModel = FindGeneralChoice();
        var general = viewModel.GeneralChoices.Single(choice => choice.GeneralId == GeneralId);
        var portrait = general.PortraitBrush as System.Windows.Media.ImageBrush;
        Program.Assert(general.Name == "程普" && general.Kingdom == "吴" &&
                       general.SkillName == "疠火 / 醇醪" &&
                       general.SkillDescription.Contains("火杀", StringComparison.Ordinal) &&
                       general.SkillDescription.Contains("武将牌上", StringComparison.Ordinal) &&
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
            $"The Cheng Pu card must render both classic skills, Lord health and the 574x761 portrait " +
            $"(name={general.Name}, skills={general.SkillName}, health={general.HealthText}, " +
            $"portrait={portrait?.ImageSource.Width}x{portrait?.ImageSource.Height}).");

        viewModel.PreviewGeneralChoiceCommand.Execute(general);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "212-classic-cheng-pu-card.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderStoragePromptAndPublicPile(string output)
    {
        var registry = CreateRegistry();
        var game = CreateStartedGame(registry, seed: 1);
        ReachHumanPlay(game);
        EndHumanPlay(game);
        ReachPrompt(game, DecisionKind.Chunlao, 32);
        Program.Assert(game.PendingDecision is { Kind: DecisionKind.Chunlao, PlayerSeat: HumanSeat },
            "The WPF fixture did not reach the Chunlao end-phase prompt.");

        using var viewModel = Load(game, registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var lihuo = viewModel.HumanSkillCards.Single(skill => skill.Name == "疠火");
        var chunlao = viewModel.HumanSkillCards.Single(skill => skill.Name == "醇醪");
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle == "选择醇醪的“醇”" &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "chunlao-select") &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "chunlao-skip") &&
                       lihuo.TypeText == "状态技" &&
                       chunlao.TypeText == "触发技" &&
                       viewModel.EventStack.Any(line =>
                           line.Contains("SelectSlashForChunlao", StringComparison.Ordinal)),
            $"The generic WPF prompt must expose Chunlao while retaining Lihuo/Chunlao skill identities " +
            $"(guide={viewModel.CurrentGuideTitle}, lihuo={lihuo.TypeText}, chunlao={chunlao.TypeText}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "213-classic-chunlao-select.png"));

        viewModel.SelectSkillChoiceCommand.Execute(viewModel.SkillChoices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "chunlao-select"));
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "chunlao-finish") &&
                       viewModel.SkillChoices.All(choice =>
                           choice.Parameters.GetValueOrDefault("action") != "chunlao-skip"),
            "After selecting one Slash, WPF must keep the prompt open and replace skip with finish.");
        viewModel.SelectSkillChoiceCommand.Execute(viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "chunlao-finish"));
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        Program.Assert(viewModel.HumanPlayer is
                       {
                           ChunlaoText: "醇 ×1",
                           HasChunlao: true
                       } owner &&
                       owner.ChunlaoTooltip.Contains("公开的“醇”", StringComparison.Ordinal) &&
                       viewModel.HumanSummary.Contains("醇 1", StringComparison.Ordinal) &&
                       viewModel.BattleCues.Any(cue =>
                           cue.Label == "醇醪 · 醇 1" && cue.Detail == "公开置于武将牌上"),
            "Finishing the prompt must expose the public Chun pile on the seat, summary and battle cues.");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "214-classic-chunlao-public-pile.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderDyingRescue(string output)
    {
        var fixture = FindDyingFixture();
        using var viewModel = Load(fixture.Game, fixture.Registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var choice = viewModel.DyingChoices.Single(item =>
            item.Parameters.GetValueOrDefault("response") == "chunlao");
        Program.Assert(viewModel.IsDyingSelectionPending &&
                       choice.Cards.Count == 1 &&
                       viewModel.HumanPlayer?.ChunlaoText == "醇 ×1",
            "The shared dying surface must expose the exact public Chun card as a Chunlao response.");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "215-classic-chunlao-dying-rescue.png"));

        viewModel.SelectDyingChoiceCommand.Execute(choice);
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        Program.Assert(Program.Engine(viewModel).Events.Select(item => item.Payload)
                           .OfType<ChunlaoRescueEvent>().Any() &&
                       viewModel.BattleCues.Any(cue =>
                           cue.Label == "醇醪 · 酒救援" && cue.Detail == "回复1点体力"),
            "Selecting the Chunlao dying response must resolve and publish its recovery cue.");
        window.Content = null;
        window.Close();
    }

    private static (GameEngine Game, ContentRegistry Registry) FindDyingFixture()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 128; seed++)
        {
            var game = CreateStartedGame(registry, seed);
            ReachHumanPlay(game);
            EndHumanPlay(game);
            ReachPrompt(game, DecisionKind.Chunlao, 32);
            Answer(game, RequirePrompt(game, DecisionKind.Chunlao).Choices.First(choice =>
                choice.Parameters.GetValueOrDefault("action") == "chunlao-select"));
            Answer(game, RequirePrompt(game, DecisionKind.Chunlao).Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "chunlao-finish"));

            for (var step = 0; step < 256 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.RescueDying, PlayerSeat: HumanSeat } dying &&
                    dying.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "chunlao"))
                {
                    return (game, registry);
                }

                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat } play)
                {
                    var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                        action.Kind == LegalActionKind.Slash &&
                        action.TargetSeats.Count == 1 &&
                        Player(game, action.TargetSeats[0]).Hp <= 1);
                    if (slash is not null)
                    {
                        Program.Assert(Play(game, slash).Accepted,
                            "The WPF Chunlao fixture could not use its lethal Slash.");
                    }
                    else
                    {
                        var ended = game.Submit(new EndPlayPhaseCommand(
                            HumanSeat, game.Revision, play.PromptId));
                        Program.Assert(ended.Accepted,
                            ended.Error?.Message ?? "The WPF Chunlao fixture could not end Play.");
                    }
                    continue;
                }

                if (game.PendingDecision is { PlayerSeat: HumanSeat } human)
                {
                    var decline = human.Choices.FirstOrDefault(item =>
                        item.Cards.Count == 0 && item.Targets.Count == 0);
                    if (decline is null) break;
                    Answer(game, decline);
                    continue;
                }

                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Program.Assert(advanced.Accepted,
                    advanced.Error?.Message ?? "The WPF Chunlao fixture could not advance.");
            }
        }
        throw new InvalidOperationException("Could not find a bounded WPF Chunlao dying fixture.");
    }

    private static MainViewModel Load(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(
            1,
            DateTimeOffset.UtcNow,
            false,
            game.CreateCheckpoint()));
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
        for (var seed = 1; seed <= 4_096; seed++)
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
        throw new InvalidOperationException("Could not find a deterministic Cheng Pu WPF card fixture.");
    }

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private static GameEngine CreateStartedGame(ContentRegistry registry, int seed)
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
        Program.Assert(game.Submit(new StartGameCommand()).Accepted,
            "The WPF Cheng Pu fixture failed to start.");
        var prompt = RequirePrompt(game, DecisionKind.SelectGeneral);
        Program.Assert(prompt.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
            "The WPF fixture did not offer Cheng Pu.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat, GeneralId, game.Revision, prompt.PromptId));
        Program.Assert(selected.Accepted,
            selected.Error?.Message ?? "The WPF fixture could not select Cheng Pu.");
        return game;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 512; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Program.Assert(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Cheng Pu play.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Program.Assert(advanced.Accepted,
                advanced.Error?.Message ?? "The WPF Cheng Pu fixture could not advance.");
        }
        throw new InvalidOperationException("The WPF Cheng Pu fixture did not reach Play.");
    }

    private static void EndHumanPlay(GameEngine game)
    {
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        var ended = game.Submit(new EndPlayPhaseCommand(
            HumanSeat, game.Revision, play.PromptId));
        Program.Assert(ended.Accepted,
            ended.Error?.Message ?? "The WPF Cheng Pu fixture could not end Play.");
    }

    private static void ReachPrompt(GameEngine game, DecisionKind kind, int maximumSteps)
    {
        for (var step = 0; step < maximumSteps; step++)
        {
            if (game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind) return;
            Program.Assert(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching {kind}.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Program.Assert(advanced.Accepted,
                advanced.Error?.Message ?? $"The WPF Cheng Pu fixture could not reach {kind}.");
        }
        throw new InvalidOperationException($"The WPF Cheng Pu fixture did not reach {kind}.");
    }

    private static CommandResult Play(GameEngine game, LegalAction action) => game.Submit(new PlayCardCommand(
        HumanSeat,
        action.CardId!.Value,
        action.TargetSeats,
        game.Revision,
        game.PendingDecision!.PromptId,
        action.PlayedCardKind,
        action.TargetCardId)
    {
        ConversionSource = action.ConversionSource,
        CardKindModifierSkill = action.CardKindModifierSkill,
        TargetCountModifierSkill = action.TargetCountModifierSkill
    });

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var answered = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            game.PendingDecision?.PromptId ?? throw new InvalidOperationException("No human prompt is pending."),
            choice.Id,
            game.Revision));
        Program.Assert(answered.Accepted,
            answered.Error?.Message ?? "The WPF Cheng Pu prompt answer was rejected.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static PlayerSnapshot Player(GameEngine game, int seat) =>
        game.CreateSnapshot(HumanSeat, revealAll: true).Players.Single(player => player.Seat == seat);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-cheng-pu-wpf-test-4";
        private const string DeckId = "fixture:cheng-pu-wpf-slash-deck";
        private static readonly string[] TargetIds =
            ["fixture:cheng-pu-wpf-target-1", "fixture:cheng-pu-wpf-target-2", "fixture:cheng-pu-wpf-target-3"];

        public PackageManifest Manifest { get; } = new(
            "cheng-pu-wpf-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 92, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in TargetIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "醇醪界面目标",
                    "supporter",
                    "standard:none",
                    "wei",
                    BaseHp: 1));
            }
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "醇醪界面测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 0,
                Cards: [new ContentDeckCardCount("standard:slash", 96)]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "四人经典身份（程普界面场景）",
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
                GeneralCandidateCount: 4,
                GeneralPoolIds: [GeneralId, .. TargetIds]));
        }
    }
}
