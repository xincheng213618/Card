using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Content.Standard.Skills;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ZongshiModuleUiChecks
{
    private const int HumanSeat = 0;
    private const string PhaseSkillId = "fixture:zongshi-wpf-pindian";

    public static void GenericClaimPromptAndContinuation(string output)
    {
        var registry = Registry();
        var fixture = ReachZongshiPrompt(registry);
        Program.Assert(fixture.Game.CreateSnapshot(1).PendingDecision is null,
            "Another player must not see the private Zongshi result choice.");

        using var viewModel = Load(fixture.Game, registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var game = Program.Engine(viewModel);
        var prompt = RequireZongshiPrompt(game);
        var presentation = prompt.SkillPrompt!;
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.SkillChoices.Select(choice =>
                               choice.Parameters.GetValueOrDefault("action"))
                           .Order(StringComparer.Ordinal)
                           .SequenceEqual(["pindian-claim", "pindian-skip"]) &&
                       viewModel.CurrentDecisionContext is
                       {
                           Title: "纵适 · 是否获得拼点牌",
                           TargetSeat: HumanSeat
                       } &&
                       viewModel.CurrentDecisionContext.Description == prompt.Prompt &&
                       viewModel.CurrentGuideTitle == presentation.Title &&
                       viewModel.CurrentGuideSteps.Count == 1 &&
                       viewModel.CurrentGuideSteps[0].Text == presentation.Instructions &&
                       viewModel.EventStack.Any(line =>
                           line.Contains("Skill(纵适, id: classic:jianyong-zongshi)", StringComparison.Ordinal)),
            "The generic WPF skill surface must expose Zongshi metadata, both actions, private context, and guidance.");

        var handBefore = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count;
        var claim = viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "pindian-claim");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "229-zongshi-pindian-claim.png"));
        var pinnedChoices = (ItemsControl)window.FindName("PinnedPublicModuleChoices");
        var scrollingChoices = (ItemsControl)window.FindName("ScrollingSkillChoices");
        var responseViewport = (ScrollViewer)window.FindName("ResponseViewport");
        var publicTitle = (TextBlock)window.FindName("PublicRevealTitleText");
        var pinnedButtons = Program.Find<Button>(pinnedChoices).ToArray();
        Program.Assert(viewModel.PublicRevealTitle == "纵适 · 公开牌" &&
                       publicTitle.Text == "纵适 · 公开牌" &&
                       publicTitle.ActualWidth > 0 && publicTitle.ActualHeight > 0,
            $"The public-card heading must come from generic skill metadata, found '{viewModel.PublicRevealTitle}'.");
        Program.Assert(viewModel.HasPinnedPublicModuleChoices &&
                       pinnedChoices.Visibility == Visibility.Visible &&
                       scrollingChoices.Visibility == Visibility.Collapsed &&
                       pinnedButtons.Length == 2,
            $"The two module actions must use the fixed choice row " +
            $"(computed={viewModel.HasPinnedPublicModuleChoices}, pinned={pinnedChoices.Visibility}, " +
            $"scrolling={scrollingChoices.Visibility}, buttons={pinnedButtons.Length}).");
        var viewportTop = responseViewport.TranslatePoint(new Point(), root).Y;
        foreach (var button in pinnedButtons)
        {
            var origin = button.TranslatePoint(new Point(), root);
            Program.Assert(button.Visibility == Visibility.Visible && button.ActualWidth > 0 && button.ActualHeight > 0 &&
                           origin.X >= 0 && origin.Y >= 0 &&
                           origin.X + button.ActualWidth <= root.ActualWidth &&
                           origin.Y + button.ActualHeight <= root.ActualHeight &&
                           origin.Y + button.ActualHeight <= viewportTop + 1,
                $"Pinned module action is clipped or not above the public-card viewport: " +
                $"origin={origin}, size={button.ActualWidth}x{button.ActualHeight}, viewportTop={viewportTop}, " +
                $"root={root.ActualWidth}x{root.ActualHeight}.");
        }
        viewModel.SelectSkillChoiceCommand.Execute(claim);

        var claimed = game.Events.Select(item => item.Payload).OfType<PindianCardClaimedEvent>().Single();
        Program.Assert(claimed.SkillId == "classic:jianyong-zongshi" && claimed.OwnerSeat == HumanSeat,
            $"The claim event lost the stable Zongshi identity or owner " +
            $"(skill={claimed.SkillId}, owner={claimed.OwnerSeat}).");
        Program.Assert(claimed.CardId == fixture.ExpectedCardId,
            $"Zongshi claimed card {claimed.CardId}, expected {fixture.ExpectedCardId} from the Pindian result.");
        var ownerHand = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand;
        Program.Assert(ownerHand.Any(card => card.Id == fixture.ExpectedCardId) && ownerHand.Count == handBefore + 1,
            $"The claimed physical card did not enter the owner's hand exactly once " +
            $"(expected={fixture.ExpectedCardId}, before={handBefore}, after={ownerHand.Count}).");
        Program.Assert(game.State.Phase == TurnPhase.Play,
            $"Zongshi restored phase {game.State.Phase}, expected Play.");
        Program.Assert(!game.ResolutionStack.OfType<PindianFrame>().Any() &&
                       !game.ResolutionStack.OfType<PhaseSkillFrame>().Any(),
            $"Zongshi left a child or parent frame behind: " +
            string.Join(", ", game.ResolutionStack.Select(frame => frame.Kind)));
        var zongshiCueCount = viewModel.BattleCues.Count(cue => cue.Label == "纵适 · 已发动");
        Program.Assert(zongshiCueCount == 1,
            $"Expected one Chinese Zongshi cue, found {zongshiCueCount}; cues=" +
            string.Join(" | ", viewModel.BattleCues.Select(cue => cue.Label)));
        Program.Assert(game.PendingDecision is null or { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat },
            $"The WPF command resumed to an unexpected prompt {game.PendingDecision?.Kind} for seat " +
            $"{game.PendingDecision?.PlayerSeat}.");
        if (game.PendingDecision is null)
        {
            Program.Assert(viewModel.CanStepAi,
                "A restored Play phase without a prompt must expose its normal continuation.");
            viewModel.StepAiCommand.Execute(null);
        }
        Program.Assert(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat },
            "One normal engine step must publish the restored Play prompt after the Zongshi claim.");
        window.Content = null;
        window.Close();
    }

    private static (GameEngine Game, int ExpectedCardId) ReachZongshiPrompt(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 700022,
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
        Accept(game.Submit(new StartGameCommand()));
        var selection = game.PendingDecision ?? throw new InvalidOperationException("Expected general selection.");
        Program.Assert(selection.ValidContentIds.Contains(ScenarioPackage.GeneralId),
            "The fixed WPF Zongshi fixture did not publish its human general.");
        Accept(game.Submit(new SelectGeneralCommand(
            HumanSeat, ScenarioPackage.GeneralId, game.Revision, selection.PromptId)));

        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { SkillPrompt.SkillId: PhaseSkillId } activation &&
                activation.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "fixture-pindian-use"))
            {
                Accept(game.Submit(new AnswerPromptCommand(
                    HumanSeat,
                    activation.PromptId,
                    activation.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("action") == "fixture-pindian-use").Id,
                    game.Revision)));
                break;
            }
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }

        var start = game.PendingDecision is { SkillPrompt.SkillId: PhaseSkillId } pending &&
                    pending.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("action") == "pindian-start")
            ? pending
            : throw new InvalidOperationException("The WPF fixture did not reach the shared Pindian selection.");
        var hand = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand;
        var selected = start.Choices
            .OrderByDescending(choice => hand.Single(card => card.Id == choice.Cards.Single()).Rank)
            .ThenBy(choice => choice.Targets.Single())
            .ThenBy(choice => choice.Cards.Single())
            .First();
        Accept(game.Submit(new AnswerPromptCommand(
            HumanSeat, start.PromptId, selected.Id, game.Revision)));
        Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        _ = RequireZongshiPrompt(game);
        var result = game.Events.Select(item => item.Payload).OfType<PindianResultDeterminedEvent>().Single().Result;
        var expected = result.WonBy(HumanSeat)
            ? result.SourceRank < result.OpponentRank ? result.SourceCardId : result.OpponentCardId
            : result.CardOf(HumanSeat);
        return (game, expected);
    }

    private static PendingDecision RequireZongshiPrompt(GameEngine game) =>
        game.PendingDecision is
        {
            Kind: DecisionKind.SkillModule,
            PlayerSeat: HumanSeat,
            IsPrivate: true,
            SkillPrompt:
            {
                SkillId: "classic:jianyong-zongshi",
                Name: "纵适",
                Title: "纵适 · 是否获得拼点牌"
            }
        } prompt
            ? prompt
            : throw new InvalidOperationException("Expected the generic private Zongshi result prompt.");

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

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private sealed class PindianPhaseModule : IPhaseSkillModule
    {
        public string SkillId => PhaseSkillId;
        public int Revision => 1;
        public PhaseSkillWindow Window => PhaseSkillWindow.PlayStarting;

        public SkillActivationPlan CreatePlan(PhaseSkillContext context)
        {
            PromptChoice Choice(bool use) => new(
                new ChoiceId($"fixture.zongshi-wpf-pindian.{(use ? "use" : "skip")}"),
                use ? "发动测试拼点。" : "跳过测试拼点。", [], [],
                new Dictionary<string, string>
                {
                    ["action"] = use ? "fixture-pindian-use" : "fixture-pindian-skip"
                });
            return new SkillActivationPlan(
                new(PhaseSkillId, "测试巧说", "测试巧说 · 是否拼点", "出牌阶段开始时，可以与一名角色拼点。"),
                "是否发动测试拼点？", Choice(true), Choice(false), [new BeginSkillPindian()]);
        }
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-zongshi-module-wpf-test-5";
        public const string GeneralId = "fixture:zongshi-wpf-owner";
        private const string DeckId = "fixture:zongshi-module-wpf-deck";
        private static readonly string[] TargetIds =
        [
            "fixture:zongshi-wpf-target-1", "fixture:zongshi-wpf-target-2",
            "fixture:zongshi-wpf-target-3", "fixture:zongshi-wpf-target-4"
        ];

        public PackageManifest Manifest { get; } = new(
            "zongshi-module-wpf-test", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new(PhaseSkillId, "测试巧说", "出牌阶段开始时，可以发起共享拼点。")
            {
                PhaseSkill = new PindianPhaseModule()
            });
            builder.AddSkill(new("classic:jianyong-zongshi", "纵适", "拼点后，可以获得规则指定的拼点牌。")
            {
                PindianResultSkill = new ZongshiModule()
            });
            builder.AddGeneral(new(
                GeneralId, "纵适界面测试武将", "supporter", PhaseSkillId, "shu", BaseHp: 3,
                AdditionalSkillIds: ["classic:jianyong-zongshi"]));
            foreach (var id in TargetIds)
                builder.AddGeneral(new(id, "纵适界面测试目标", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(DeckId, "纵适界面固定拼点牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 80)
                    .Select(index => new ContentDeckPhysicalCard(
                        "standard:crossbow", (Suit)(index % 4), index % 13 + 1))
                    .ToArray()
            });
            builder.AddMode(new(
                ModeId, "五人纵适界面模块场景", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, DeckId, 5, [GeneralId, .. TargetIds]));
        }
    }

    private static void Accept(CommandResult result) =>
        Program.Assert(result.Accepted, result.Error?.Message ?? "Command rejected.");
}
