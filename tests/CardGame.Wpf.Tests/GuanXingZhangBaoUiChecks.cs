using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class GuanXingZhangBaoUiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:guan-xing-zhang-bao";

    public static void CardDraftAndTurnGrant(string output)
    {
        RenderGeneralCard(output);
        RenderActiveDraft(output);
        RenderGrantedSkills(output);
    }

    private static void RenderGeneralCard(string output)
    {
        using var viewModel = FindGeneralChoice();
        var general = viewModel.GeneralChoices.Single(choice => choice.GeneralId == GeneralId);
        var portrait = general.PortraitBrush as System.Windows.Media.ImageBrush;
        Program.Assert(general.Name == "关兴张苞" && general.Kingdom == "蜀" &&
                       general.SkillName == "父魂" &&
                       general.SkillDescription.Contains("两张手牌", StringComparison.Ordinal) &&
                       general.SkillDescription.Contains("武圣", StringComparison.Ordinal) &&
                       general.SkillDescription.Contains("咆哮", StringComparison.Ordinal) &&
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
            $"The Guan Xing & Zhang Bao card must render exact Fuhun text, Lord health and official art " +
            $"(name={general.Name}, kingdom={general.Kingdom}, skill={general.SkillName}, " +
            $"health={general.HealthText}, portrait={portrait?.ImageSource.Width}x{portrait?.ImageSource.Height}).");

        viewModel.PreviewGeneralChoiceCommand.Execute(general);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "206-classic-guan-xing-zhang-bao-card.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderActiveDraft(string output)
    {
        var registry = CreateRegistry();
        var game = CreateGame(registry);
        StartAndSelect(game);
        ReachHumanPlay(game);

        using var viewModel = Load(game, registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var engine = Program.Engine(viewModel);
        var fuhun = viewModel.HumanSkillCards.Single(skill => skill.Name == "父魂");
        var action = viewModel.HumanActiveSkillActions.Single(candidate =>
            candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Fuhun);
        var targetSeat = action.SelectableTargetSeats.First();
        var costIds = action.SelectableCardIds.Take(2).ToArray();

        viewModel.SelectActiveSkillCommand.Execute(action);
        foreach (var cardId in costIds)
            viewModel.SelectCardCommand.Execute(viewModel.Hand.Single(card => card.Id == cardId));
        viewModel.SelectTargetCommand.Execute(viewModel.Seats.Single(seat => seat.Seat == targetSeat));

        Program.Assert(fuhun is
                       {
                           TypeText: "主动技 · 状态技",
                           StateText: "当前可发动",
                           IsAvailable: true
                       } &&
                       viewModel.IsActiveSkillSelectionPending &&
                       viewModel.CanConfirmActiveSkill &&
                       viewModel.Hand.Count(card => card.IsSelected) == 2 &&
                       viewModel.Seats.Single(seat => seat.Seat == targetSeat).IsSelectedTarget,
            $"The shared Fuhun draft must expose State+Active metadata and select exactly two hand cards plus one target " +
            $"(type={fuhun.TypeText}, state={fuhun.StateText}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "207-classic-fuhun-active-draft.png"));
        var visibleText = Program.Find<TextBlock>(root).Select(text => text.Text).ToArray();
        Program.Assert(visibleText.Any(text => text.Contains("父魂", StringComparison.Ordinal)) &&
                       visibleText.Any(text => text.Contains("牌 2/2", StringComparison.Ordinal)) &&
                       visibleText.Any(text => text.Contains("目标 1/1", StringComparison.Ordinal)),
            "The central Fuhun draft did not explain its exact two-card and one-target contract.");

        viewModel.ConfirmSelectedCommand.Execute(null);
        var command = engine.AcceptedCommands.OfType<UseSkillCommand>().LastOrDefault();
        Program.Assert(command is not null && command.Skill == SkillKind.Fuhun &&
                       command.CardIds.SequenceEqual(costIds.Order()) &&
                       command.TargetSeats.SequenceEqual([targetSeat]) &&
                       engine.Events.Select(item => item.Payload).OfType<FuhunConvertedEvent>().Any(item => item.IsUse),
            "The WPF Fuhun draft did not commit the typed skill command and both physical costs.");
        window.Content = null;
        window.Close();
    }

    private static void RenderGrantedSkills(string output)
    {
        var registry = CreateRegistry();
        var game = CreateGame(registry);
        StartAndSelect(game);
        ReachHumanPlay(game);
        var prompt = RequirePrompt(game, DecisionKind.PlayCard);
        var action = game.GetHumanLegalActions().Single(candidate => candidate.Skill == SkillKind.Fuhun);
        var result = game.Submit(new UseSkillCommand(
            HumanSeat,
            SkillKind.Fuhun,
            action.SelectableCardIds.Take(2).ToArray(),
            [action.SelectableTargetSeats.First()],
            game.Revision,
            prompt.PromptId));
        Program.Assert(result.Accepted, result.Error?.Message ?? "The Fuhun grant fixture rejected its skill use.");
        ReachHumanPlay(game);

        using var viewModel = Load(game, registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var fuhun = viewModel.HumanSkillCards.Single(skill => skill.Name == "父魂");
        var wusheng = viewModel.HumanSkillCards.Single(skill => skill.Name == "武圣");
        var paoxiao = viewModel.HumanSkillCards.Single(skill => skill.Name == "咆哮");
        var projectedCues = BattleCueProjector.Project(
            game.Events,
            game.CreateSnapshot(HumanSeat, revealAll: true));
        Program.Assert(fuhun.StateText == "本回合已获得武圣／咆哮" &&
                       wusheng.SourceText.EndsWith("父魂获得", StringComparison.Ordinal) &&
                       paoxiao.SourceText.EndsWith("父魂获得", StringComparison.Ordinal) &&
                       projectedCues.Any(cue => cue.Label == "父魂 · 两牌化杀") &&
                       projectedCues.Any(cue => cue.Label == "父魂 · 获得武圣／咆哮"),
            $"The skill rail and public cues must show the turn-scoped Fuhun grant " +
            $"(state={fuhun.StateText}, wusheng={wusheng.SourceText}, paoxiao={paoxiao.SourceText}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "208-classic-fuhun-parent-skills.png"));
        window.Content = null;
        window.Close();
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
        throw new InvalidOperationException("Could not find a deterministic Guan Xing & Zhang Bao WPF card fixture.");
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 2_048; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Program.Assert(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Fuhun play.");
            Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Fuhun WPF fixture could not advance.");
        }
        throw new InvalidOperationException("The Fuhun WPF fixture did not reach human play.");
    }

    private static void StartAndSelect(GameEngine game)
    {
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The Fuhun WPF fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Program.Assert(selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
            "The Fuhun WPF fixture omitted its formal general.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat, GeneralId, game.Revision, selection.PromptId));
        Program.Assert(selected.Accepted, selected.Error?.Message ?? "The Fuhun WPF fixture could not select its general.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { Kind: var actual, PlayerSeat: HumanSeat } prompt && actual == kind
            ? prompt
            : throw new InvalidOperationException($"Expected human {kind}, found {game.PendingDecision?.Kind}.");

    private static GameEngine CreateGame(ContentRegistry registry) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1,
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

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-fuhun-wpf-test-4";
        private const string DeckId = "fixture:fuhun-wpf-heart-peach-deck";
        private static readonly string[] BlankGeneralIds =
            ["fixture:fuhun-wpf-a", "fixture:fuhun-wpf-b", "fixture:fuhun-wpf-c"];

        public PackageManifest Manifest { get; } = new(
            "guan-xing-zhang-bao-wpf-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 89, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in BlankGeneralIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "父魂界面目标", "supporter", "standard:none", "wei", BaseHp: 8));
            }
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "父魂界面测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(1, 192)
                    .Select(index => new ContentDeckPhysicalCard(
                        "standard:peach", Suit.Heart, index % 13 + 1))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "父魂界面测试",
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
