using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class LiaoHuaUiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:liao-hua";
    private const string HarnessGeneralId = "fixture:liao-hua-wpf-harness";

    public static void CardExtraPhaseAndFuli(string output)
    {
        RenderGeneralCard(output);
        RenderDangxianExtraPhase(output);
        RenderFuliChoice(output);
    }

    private static void RenderGeneralCard(string output)
    {
        using var viewModel = FindGeneralChoice();
        var liaoHua = viewModel.GeneralChoices.Single(choice => choice.GeneralId == GeneralId);
        var portrait = liaoHua.PortraitBrush as System.Windows.Media.ImageBrush;
        Program.Assert(liaoHua.Name == "廖化" && liaoHua.Kingdom == "蜀" &&
                       liaoHua.SkillName == "当先 / 伏枥" &&
                       liaoHua.SkillDescription.Contains("额外的出牌阶段", StringComparison.Ordinal) &&
                       liaoHua.SkillDescription.Contains("现存势力数", StringComparison.Ordinal) &&
                       liaoHua.SkillDescription.Contains("武将牌翻面", StringComparison.Ordinal) &&
                       liaoHua.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(liaoHua.GeneralId) &&
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
            $"The Liao Hua card must render exact classic skills, Lord health and official art " +
            $"(name={liaoHua.Name}, kingdom={liaoHua.Kingdom}, skills={liaoHua.SkillName}, " +
            $"health={liaoHua.HealthText}, portrait={portrait?.ImageSource.Width}x{portrait?.ImageSource.Height}).");

        viewModel.PreviewGeneralChoiceCommand.Execute(liaoHua);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "203-classic-liao-hua-card.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderDangxianExtraPhase(string output)
    {
        var registry = CreateRegistry();
        var game = CreateGame(registry, ScenarioPackage.FormalModeId);
        StartAndSelect(game, GeneralId);
        ReachHumanPlay(game);

        using var viewModel = Load(game, registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var dangxian = viewModel.HumanSkillCards.SingleOrDefault(skill => skill.Name == "当先");
        var fuli = viewModel.HumanSkillCards.SingleOrDefault(skill => skill.Name == "伏枥");
        var projectedCues = BattleCueProjector.Project(
            game.Events,
            game.CreateSnapshot(HumanSeat, revealAll: true));
        Program.Assert(dangxian is
                       {
                           TypeText: "状态技 · 锁定技",
                           StateText: "规则自动生效"
                       } &&
                       fuli is
                       {
                           TypeText: "触发技 · 限定技",
                           StateText: "等待触发时机 · 本局限一次"
                       } &&
                       projectedCues.Any(cue =>
                           cue.Kind == BattleCueKind.Turn && cue.Label == "当先 · 额外出牌阶段") &&
                       game.CardMovements.All(move =>
                           move.To != CardLocation.Hand(HumanSeat) || move.Reason != CardMoveReasons.Draw),
            $"The skill rail must distinguish Dangxian state/locked from Fuli trigger/limited and show the " +
            $"pre-draw extra phase (dangxian={dangxian?.TypeText}/{dangxian?.StateText}, " +
            $"fuli={fuli?.TypeText}/{fuli?.StateText}, cues={string.Join('|', projectedCues.Select(cue => cue.Label))}, " +
            $"normalDraws={game.CardMovements.Count(move => move.To == CardLocation.Hand(HumanSeat) && move.Reason == CardMoveReasons.Draw)}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "204-classic-liao-hua-dangxian-play.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderFuliChoice(string output)
    {
        var registry = CreateRegistry();
        var game = CreateGame(registry, ScenarioPackage.FuliModeId);
        StartAndSelect(game, HarnessGeneralId);
        ReachHumanPlay(game);
        var initialHp = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hp;
        for (var expectedHp = initialHp - 1; expectedHp >= 1; expectedHp--)
        {
            UseKujin(game);
            ReachHumanPlay(game);
        }
        UseKujin(game);

        using var viewModel = Load(game, registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var fuliChoice = viewModel.DyingChoices.SingleOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("response") == "program-trigger" &&
            choice.Parameters.GetValueOrDefault("skill-id") == "classic:fuli");
        Program.Assert(viewModel.IsDyingSelectionPending &&
                       game.PendingDecision is { Kind: DecisionKind.RescueDying, IsPrivate: true } &&
                       fuliChoice?.Description.Contains("伏枥", StringComparison.Ordinal) == true &&
                       fuliChoice.Description.Contains("现存势力", StringComparison.Ordinal) &&
                       fuliChoice.Parameters.GetValueOrDefault("response") == "program-trigger" &&
                       fuliChoice.Parameters.GetValueOrDefault("skill-id") == "classic:fuli" &&
                       viewModel.DyingChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("response") == "let-die"),
            $"The WPF must expose Fuli as a private optional dying response with four living factions " +
            $"(guide={viewModel.CurrentGuideTitle}, choices={viewModel.DyingChoices.Count}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "205-classic-liao-hua-fuli-choice.png"));

        viewModel.SelectDyingChoiceCommand.Execute(fuliChoice);
        var engine = Program.Engine(viewModel);
        var owner = engine.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat];
        var fuli = viewModel.HumanSkillCards.Single(skill => skill.Name == "伏枥");
        Program.Assert(owner.Hp == 4 && owner.IsFaceDown &&
                       engine.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(result =>
                           result is
                           {
                               SkillId: "classic:fuli",
                               OwnerSeat: HumanSeat,
                               Window: SkillProgramTriggerWindow.SelfDyingResponse,
                               Activated: true,
                               Completed: true
                           }) &&
                       viewModel.BattleCues.Any(cue =>
                           cue.Kind == BattleCueKind.Response && cue.Label == "伏枥 · 已发动") &&
                       fuli.StateText == "已发动 · 本局不可再用",
            $"Confirming Fuli must recover to four, flip the general, publish its cue and mark the limited " +
            $"skill consumed (hp={owner.Hp}, faceDown={owner.IsFaceDown}, state={fuli.StateText}).");
        window.Content = null;
        window.Close();
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
        for (var seed = 1; seed <= 2_048; seed++)
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
        throw new InvalidOperationException("Could not find a deterministic classic Liao Hua WPF card fixture.");
    }

    private static void UseKujin(GameEngine game)
    {
        var prompt = RequirePrompt(game, DecisionKind.PlayCard);
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill &&
            candidate.ProgramSkillId == "classic:kujin");
        var result = game.Submit(new UseProgramSkillCommand(
            HumanSeat, action.ProgramSkillId!, action.ProgramActivationId!, [], [],
            game.Revision, prompt.PromptId));
        Program.Assert(result.Accepted, result.Error?.Message ?? "The Fuli WPF fixture could not use Kujin.");
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 2_048; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Program.Assert(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Liao Hua play.");
            Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Liao Hua WPF fixture could not advance.");
        }
        throw new InvalidOperationException("The Liao Hua WPF fixture did not reach human play in bounded steps.");
    }

    private static void StartAndSelect(GameEngine game, string generalId)
    {
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The Liao Hua WPF fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Program.Assert(selection.ValidContentIds.Contains(generalId, StringComparer.Ordinal),
            $"The Liao Hua WPF fixture did not offer {generalId}.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            generalId,
            game.Revision,
            selection.PromptId));
        Program.Assert(selected.Accepted,
            selected.Error?.Message ?? "The Liao Hua WPF fixture could not select its general.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static GameEngine CreateGame(ContentRegistry registry, string modeId) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1,
            PlayerCount = 4,
            ModeId = modeId,
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
        public const string FormalModeId = "identity:classic-liao-hua-wpf-test-4";
        public const string FuliModeId = "identity:classic-liao-hua-fuli-wpf-test-4";
        private const string DeckId = "fixture:liao-hua-wpf-slash-deck";
        private static readonly (string Id, string Faction)[] BlankGenerals =
        [
            ("fixture:liao-hua-wpf-wei", "wei"),
            ("fixture:liao-hua-wpf-wu", "wu"),
            ("fixture:liao-hua-wpf-qun", "qun")
        ];

        public PackageManifest Manifest { get; } = new(
            "liao-hua-wpf-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 88, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (id, faction) in BlankGenerals)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "廖化界面目标",
                    "supporter",
                    "standard:none",
                    faction,
                    BaseHp: 8));
            }
            builder.AddGeneral(new ContentGeneralDefinition(
                HarnessGeneralId,
                "廖化技能测试",
                "liao_hua",
                "classic:kujin",
                "shu",
                BaseHp: 4,
                AdditionalSkillIds: ["classic:dangxian", "classic:fuli"]));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "廖化界面测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 192)
                    .Select(index => new ContentDeckPhysicalCard(
                        "standard:slash",
                        (Suit)(index % 4),
                        index % 13 + 1))
                    .ToArray()
            });
            AddMode(builder, FormalModeId, "廖化当先界面测试", GeneralId);
            AddMode(builder, FuliModeId, "廖化伏枥界面测试", HarnessGeneralId);
        }

        private static void AddMode(
            IContentRegistryBuilder builder,
            string modeId,
            string name,
            string ownerGeneralId) =>
            builder.AddMode(new ContentModeDefinition(
                modeId,
                name,
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
                GeneralPoolIds: [ownerGeneralId, .. BlankGenerals.Select(item => item.Id)]));
    }
}
