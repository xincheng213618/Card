using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class BuLianShiUiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:bu-lian-shi";

    public static void CardAnxuAndZhuiyi(string output)
    {
        RenderGeneralCard(output);
        RenderAnxuDraftAndCue(output);
        RenderZhuiyiPrompt(output);
    }

    private static void RenderGeneralCard(string output)
    {
        using var viewModel = FindGeneralChoice();
        var general = viewModel.GeneralChoices.Single(choice => choice.GeneralId == GeneralId);
        var portrait = general.PortraitBrush as System.Windows.Media.ImageBrush;
        Program.Assert(general.Name == "步练师" && general.Kingdom == "吴" &&
                       general.SkillName == "安恤 / 追忆" &&
                       general.SkillDescription.Contains("手牌数不同", StringComparison.Ordinal) &&
                       general.SkillDescription.Contains("死亡时", StringComparison.Ordinal) &&
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
            $"The Bu Lian Shi card must render exact classic skills, Lord health and official art " +
            $"(name={general.Name}, kingdom={general.Kingdom}, skills={general.SkillName}, " +
            $"health={general.HealthText}, portrait={portrait?.ImageSource.Width}x{portrait?.ImageSource.Height}).");

        viewModel.PreviewGeneralChoiceCommand.Execute(general);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "209-classic-bu-lian-shi-card.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderAnxuDraftAndCue(string output)
    {
        var fixture = FindAnxuFixture();
        using var viewModel = Load(fixture.Game, fixture.Registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var anxuSkill = viewModel.HumanSkillCards.Single(skill => skill.Name == "安恤");
        var zhuiyiSkill = viewModel.HumanSkillCards.Single(skill => skill.Name == "追忆");
        var action = viewModel.HumanActiveSkillActions.Single(candidate => candidate.Skill == SkillKind.Anxu);
        var pair = FindUnequalPair(viewModel, action.SelectableTargetSeats);

        viewModel.SelectActiveSkillCommand.Execute(action);
        var equalPair = FindEqualPair(viewModel, action.SelectableTargetSeats);
        if (equalPair is { } invalid)
        {
            viewModel.SelectTargetCommand.Execute(viewModel.Seats.Single(seat => seat.Seat == invalid.FirstSeat));
            viewModel.SelectTargetCommand.Execute(viewModel.Seats.Single(seat => seat.Seat == invalid.SecondSeat));
            Program.Assert(!viewModel.CanConfirmActiveSkill,
                "The Anxu draft must not confirm two targets with equal hand counts.");
            viewModel.SelectTargetCommand.Execute(viewModel.Seats.Single(seat => seat.Seat == invalid.FirstSeat));
            viewModel.SelectTargetCommand.Execute(viewModel.Seats.Single(seat => seat.Seat == invalid.SecondSeat));
        }
        viewModel.SelectTargetCommand.Execute(viewModel.Seats.Single(seat => seat.Seat == pair.ReceiverSeat));
        viewModel.SelectTargetCommand.Execute(viewModel.Seats.Single(seat => seat.Seat == pair.DonorSeat));
        Program.Assert(anxuSkill is
                       {
                           TypeText: "主动技",
                           StateText: "当前可发动",
                           IsAvailable: true
                       } &&
                       zhuiyiSkill is
                       {
                           TypeText: "触发技",
                           StateText: "等待触发时机"
                       } &&
                       viewModel.IsActiveSkillSelectionPending &&
                       viewModel.CanConfirmActiveSkill &&
                       viewModel.Seats.Count(seat => seat.IsSelectedTarget) == 2,
            $"The skill rail and shared draft must distinguish Anxu Active from Zhuiyi Trigger and select two targets " +
            $"(anxu={anxuSkill.TypeText}/{anxuSkill.StateText}, zhuiyi={zhuiyiSkill.TypeText}/{zhuiyiSkill.StateText}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "210-classic-anxu-active-draft.png"));
        var visibleText = Program.Find<TextBlock>(root).Select(text => text.Text).ToArray();
        Program.Assert(visibleText.Any(text => text.Contains("安恤", StringComparison.Ordinal)) &&
                       visibleText.Any(text => text.Contains("目标 2/2", StringComparison.Ordinal)),
            "The central Anxu draft must visibly require exactly two targets.");

        viewModel.ConfirmSelectedCommand.Execute(null);
        Program.Assert(!viewModel.IsSkillSelectionPending && viewModel.CanStepAi &&
                       Program.Engine(viewModel).CreateSnapshot(pair.ReceiverSeat).PendingDecision is
                       {
                           Kind: DecisionKind.Anxu,
                           IsPrivate: true
                       } receiverPrompt &&
                       receiverPrompt.Choices.All(choice => choice.Cards.Count == 0),
            "After the active draft, only the receiving AI should see opaque Anxu hand slots.");
        viewModel.StepAiCommand.Execute(null);
        Program.Assert(Program.Engine(viewModel).Events.Select(item => item.Payload).OfType<AnxuResolvedEvent>().Any() &&
                       viewModel.BattleCues.Any(cue => cue.Label == "安恤 · 获得并展示一张"),
            "Resolving the hidden Anxu choice must publish the typed result and public battle cue.");
        window.Content = null;
        window.Close();
    }

    private static void RenderZhuiyiPrompt(string output)
    {
        var fixture = FindZhuiyiFixture();
        using var viewModel = Load(fixture.Game, fixture.Registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var targetChoice = viewModel.SkillChoices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "zhuiyi-target" &&
            choice.Targets.Count == 1 &&
            Player(fixture.Game, choice.Targets[0]).Hp == Player(fixture.Game, choice.Targets[0]).MaxHp);

        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle == "决定是否发动追忆" &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "zhuiyi-skip") &&
                       viewModel.EventStack.Any(line => line.Contains("DeathSkill(Zhuiyi)", StringComparison.Ordinal)) &&
                       viewModel.CurrentDecisionContext?.Title == "追忆 · 选择受益角色或跳过",
            $"The WPF must render Zhuiyi as an optional dead-owner target prompt " +
            $"(guide={viewModel.CurrentGuideTitle}, choices={viewModel.SkillChoices.Count}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "211-classic-zhuiyi-target-choice.png"));

        viewModel.SelectSkillChoiceCommand.Execute(targetChoice);
        Program.Assert(Program.Engine(viewModel).Events.Select(item => item.Payload).OfType<ZhuiyiResolvedEvent>()
                           .Any(result => result is { OwnerSeat: HumanSeat, DrawnCardCount: 3, RecoveredHp: 0 }) &&
                       viewModel.BattleCues.Any(cue => cue.Label == "追忆 · 摸三张" &&
                                                       cue.Detail == "目标体力已满"),
            "Selecting a full-health Zhuiyi target through WPF must draw three and show the no-recovery cue.");
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
        throw new InvalidOperationException("Could not find a deterministic Bu Lian Shi WPF card fixture.");
    }

    private static Fixture FindAnxuFixture()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 128; seed++)
        {
            var game = CreateGame(registry, ScenarioPackage.AnxuModeId, seed, Role.Loyalist, GeneralId);
            ReachHumanPlay(game);
            if (game.GetHumanLegalActions().Any(action => action.Skill == SkillKind.Anxu))
            {
                return new Fixture(game, registry);
            }
        }
        throw new InvalidOperationException("Could not find a bounded WPF Anxu fixture.");
    }

    private static Fixture FindZhuiyiFixture()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 256; seed++)
        {
            var game = CreateGame(
                registry,
                ScenarioPackage.ZhuiyiModeId,
                seed,
                Role.Rebel,
                ScenarioPackage.ZhuiyiOwnerId);
            for (var step = 0; step < 1_024 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.ZhuiyiTarget, PlayerSeat: HumanSeat } prompt &&
                    prompt.Choices.Any(choice =>
                        choice.Targets.Count == 1 &&
                        Player(game, choice.Targets[0]).Hp == Player(game, choice.Targets[0]).MaxHp))
                {
                    return new Fixture(game, registry);
                }

                if (game.PendingDecision is { PlayerSeat: HumanSeat } human)
                {
                    if (human.Kind == DecisionKind.PlayCard)
                    {
                        var ended = game.Submit(new EndPlayPhaseCommand(
                            HumanSeat,
                            game.Revision,
                            human.PromptId));
                        Program.Assert(ended.Accepted, ended.Error?.Message ?? "The Zhuiyi WPF fixture could not end Play.");
                        continue;
                    }
                    var skip = human.Choices.FirstOrDefault(choice =>
                        choice.Cards.Count == 0 && choice.Targets.Count == 0);
                    if (skip is null) break;
                    var answered = game.Submit(new AnswerPromptCommand(
                        HumanSeat,
                        human.PromptId,
                        skip.Id,
                        game.Revision));
                    Program.Assert(answered.Accepted, answered.Error?.Message ?? "The Zhuiyi WPF fixture could not skip a response.");
                    continue;
                }

                AdvanceOne(game);
            }
        }
        throw new InvalidOperationException("Could not find a bounded WPF Zhuiyi prompt.");
    }

    private static (int ReceiverSeat, int DonorSeat) FindUnequalPair(
        MainViewModel viewModel,
        IReadOnlyList<int> selectableSeats)
    {
        var players = selectableSeats.Select(seat => viewModel.Seats.Single(item => item.Seat == seat)).ToArray();
        for (var first = 0; first < players.Length; first++)
        {
            for (var second = first + 1; second < players.Length; second++)
            {
                if (players[first].HandCount == players[second].HandCount) continue;
                return players[first].HandCount < players[second].HandCount
                    ? (players[first].Seat, players[second].Seat)
                    : (players[second].Seat, players[first].Seat);
            }
        }
        throw new InvalidOperationException("The WPF Anxu action published no unequal-hand pair.");
    }

    private static (int FirstSeat, int SecondSeat)? FindEqualPair(
        MainViewModel viewModel,
        IReadOnlyList<int> selectableSeats)
    {
        var players = selectableSeats.Select(seat => viewModel.Seats.Single(item => item.Seat == seat)).ToArray();
        for (var first = 0; first < players.Length; first++)
        {
            for (var second = first + 1; second < players.Length; second++)
            {
                if (players[first].HandCount == players[second].HandCount)
                {
                    return (players[first].Seat, players[second].Seat);
                }
            }
        }
        return null;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 2_048; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Program.Assert(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Anxu play.");
            AdvanceOne(game);
        }
        throw new InvalidOperationException("The WPF Bu Lian Shi fixture did not reach Play.");
    }

    private static void AdvanceOne(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Program.Assert(result.Accepted, result.Error?.Message ?? "The WPF Bu Lian Shi fixture could not advance.");
    }

    private static GameEngine CreateGame(
        ContentRegistry registry,
        string modeId,
        int seed,
        Role humanRole,
        string generalId)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = modeId,
            HumanSeat = HumanSeat,
            HumanRole = humanRole,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 40
        }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted,
            "The WPF Bu Lian Shi fixture failed to start.");
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("The WPF fixture has no general selection.");
        Program.Assert(prompt.Kind == DecisionKind.SelectGeneral &&
                       prompt.ValidContentIds.Contains(generalId, StringComparer.Ordinal),
            $"The WPF fixture did not offer {generalId}.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            generalId,
            game.Revision,
            prompt.PromptId));
        Program.Assert(selected.Accepted, selected.Error?.Message ?? "The WPF fixture could not select its owner general.");
        return game;
    }

    private static PlayerSnapshot Player(GameEngine game, int seat) =>
        game.CreateSnapshot(HumanSeat, revealAll: true).Players.Single(player => player.Seat == seat);

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private sealed record Fixture(GameEngine Game, ContentRegistry Registry);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string AnxuModeId = "identity:classic-bu-lian-shi-anxu-wpf-test-4";
        public const string ZhuiyiModeId = "identity:classic-bu-lian-shi-zhuiyi-wpf-test-4";
        public const string ZhuiyiOwnerId = "fixture:zhuiyi-wpf-owner";
        private const string AnxuDeckId = "fixture:anxu-wpf-spade-peach-deck";
        private const string ZhuiyiDeckId = "fixture:zhuiyi-wpf-slash-deck";
        private static readonly string[] AnxuTargets =
            ["fixture:anxu-wpf-target-1", "fixture:anxu-wpf-target-2", "fixture:anxu-wpf-target-3"];
        private static readonly string[] ZhuiyiTargets =
            ["fixture:zhuiyi-wpf-target-1", "fixture:zhuiyi-wpf-target-2", "fixture:zhuiyi-wpf-target-3"];

        public PackageManifest Manifest { get; } = new(
            "bu-lian-shi-wpf-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 90, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in AnxuTargets)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "安恤界面目标",
                    "supporter",
                    "classic:yingzi",
                    "wu",
                    BaseHp: 4,
                    AdditionalSkillIds: ["classic:hongyan"],
                    Gender: GeneralGender.Female));
            }
            builder.AddGeneral(new ContentGeneralDefinition(
                ZhuiyiOwnerId,
                "追忆界面拥有者",
                "bu_lian_shi",
                "classic:zhuiyi",
                "wu",
                BaseHp: 1,
                Gender: GeneralGender.Female));
            foreach (var id in ZhuiyiTargets)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "追忆界面目标",
                    "supporter",
                    "standard:none",
                    "wei",
                    BaseHp: 4));
            }

            builder.AddDeck(new ContentDeckRecipe(
                AnxuDeckId,
                "安恤界面测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(1, 192)
                    .Select(index => new ContentDeckPhysicalCard(
                        "standard:peach", Suit.Spade, index % 13 + 1))
                    .ToArray()
            });
            builder.AddDeck(new ContentDeckRecipe(
                ZhuiyiDeckId,
                "追忆界面测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(1, 192)
                    .Select(index => new ContentDeckPhysicalCard(
                        "standard:slash",
                        index % 2 == 0 ? Suit.Spade : Suit.Club,
                        index % 13 + 1))
                    .ToArray()
            });
            AddMode(builder, AnxuModeId, AnxuDeckId, [GeneralId, .. AnxuTargets]);
            AddMode(builder, ZhuiyiModeId, ZhuiyiDeckId, [ZhuiyiOwnerId, .. ZhuiyiTargets]);
        }

        private static void AddMode(
            IContentRegistryBuilder builder,
            string modeId,
            string deckId,
            IReadOnlyList<string> generalIds) =>
            builder.AddMode(new ContentModeDefinition(
                modeId,
                "步练师界面测试",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                deckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: generalIds));
    }
}
