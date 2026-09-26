using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class HanDangUiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:han-dang";

    public static void CardGongqiAndJiefan(string output)
    {
        RenderGeneralCard(output);
        RenderGongqiPrompt(output);
        RenderJiefanPrompt(output);
    }

    private static void RenderGeneralCard(string output)
    {
        using var viewModel = FindGeneralChoice();
        var general = viewModel.GeneralChoices.Single(choice => choice.GeneralId == GeneralId);
        var portrait = general.PortraitBrush as System.Windows.Media.ImageBrush;
        Program.Assert(general.Name == "韩当" && general.Kingdom == "吴" &&
                       general.SkillName == "弓骑 / 解烦" &&
                       general.SkillDescription.Contains("攻击范围无限", StringComparison.Ordinal) &&
                       general.SkillDescription.Contains("限定技", StringComparison.Ordinal) &&
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
            $"The Han Dang card must render both classic skills, Lord health and the 574x761 official portrait " +
            $"(name={general.Name}, skills={general.SkillName}, health={general.HealthText}).");
        viewModel.PreviewGeneralChoiceCommand.Execute(general);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "216-classic-han-dang-card.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderGongqiPrompt(string output)
    {
        var registry = CreateRegistry();
        var game = CreateStartedGame(registry);
        ReachHumanPlay(game);
        var cost = Player(game, HumanSeat).Hand.First(card => EquipmentCatalog.IsEquipment(card.Kind));
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.ProgramSkillId == "classic:gongqi");
        var used = game.Submit(new UseProgramSkillCommand(
            HumanSeat, action.ProgramSkillId!, action.ProgramActivationId!, [cost.Id], [],
            game.Revision, game.PendingDecision!.PromptId));
        Program.Assert(used.Accepted, used.Error?.Message ?? "The WPF fixture could not activate Gongqi.");

        using var viewModel = Load(game, registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var gongqi = viewModel.HumanSkillCards.Single(skill => skill.Name == "弓骑");
        var jiefan = viewModel.HumanSkillCards.Single(skill => skill.Name == "解烦");
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle == "弓骑 · 选择其他角色的牌" &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-discard" &&
                           choice.Cards.Count == 0) &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-decline") &&
                       gongqi.TypeText == "主动技 · 状态技" &&
                       jiefan.TypeText == "主动技 · 限定技" &&
                       viewModel.EventStack.Any(line =>
                           line.Contains("Skill(弓骑, id: classic:gongqi)", StringComparison.Ordinal)),
            $"The generic skill surface must preserve Gongqi privacy and both Han Dang metadata axes " +
            $"(guide={viewModel.CurrentGuideTitle}, gongqi={gongqi.TypeText}, jiefan={jiefan.TypeText}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "217-classic-gongqi-private-discard.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderJiefanPrompt(string output)
    {
        var registry = CreateRegistry();
        var game = CreateStartedGame(registry);
        ReachHumanPlay(game);
        var equipmentCards = Player(game, HumanSeat).Hand
            .Where(card => EquipmentCatalog.IsEquipment(card.Kind)).ToArray();
        var equip = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Equip && action.CardId == equipmentCards[0].Id);
        Program.Assert(game.Submit(new PlayCardCommand(
                HumanSeat, equip.CardId!.Value, equip.TargetSeats, game.Revision, game.PendingDecision!.PromptId)).Accepted,
            "The WPF Jiefan fixture could not equip its weapon.");
        ReachHumanPlay(game);
        var gongqi = game.GetHumanLegalActions().Single(candidate =>
            candidate.ProgramSkillId == "classic:gongqi");
        Program.Assert(game.Submit(new UseProgramSkillCommand(
                HumanSeat, gongqi.ProgramSkillId!, gongqi.ProgramActivationId!, [equipmentCards[1].Id], [],
                game.Revision, game.PendingDecision!.PromptId)).Accepted,
            "The WPF Jiefan fixture could not activate Gongqi.");
        Answer(game, RequirePrompt(game, DecisionKind.ProgramTrigger).Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-decline"));
        ReachHumanPlay(game);
        var targetSeat = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Where(player => player.IsAlive && player.Seat != HumanSeat)
            .OrderByDescending(player => game.GetCombatDistance(HumanSeat, player.Seat))
            .First().Seat;
        var jiefan = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill &&
            candidate.ProgramSkillId == "classic:jiefan");
        Program.Assert(game.Submit(new UseProgramSkillCommand(
                HumanSeat, jiefan.ProgramSkillId!, jiefan.ProgramActivationId!, [], [targetSeat],
                game.Revision, game.PendingDecision!.PromptId)).Accepted,
            "The WPF fixture could not activate Jiefan.");

        using var viewModel = Load(game, registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle == "解烦 · 响应方式" &&
                       viewModel.CurrentDecisionContext is { TargetSeat: var contextTarget } && contextTarget == targetSeat &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("program-action") ==
                           "attack-range-aid-discard-weapon") &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("program-action") == "attack-range-aid-draw") &&
                       viewModel.EventStack.Any(line =>
                           line.Contains("Skill(解烦, id: classic:jiefan)", StringComparison.Ordinal)) &&
                       viewModel.EventStack.Any(line =>
                           line.Contains("AskForActivation(ProgramTrigger)", StringComparison.Ordinal)),
            $"The generic skill surface must present the exact mandatory Jiefan branches and target context " +
            $"(guide={viewModel.CurrentGuideTitle}, choices={viewModel.SkillChoices.Count}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "218-classic-jiefan-response.png"));
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
        throw new InvalidOperationException("Could not find a deterministic Han Dang WPF card fixture.");
    }

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private static GameEngine CreateStartedGame(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17,
            PlayerCount = 4,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The WPF Han Dang fixture failed to start.");
        var prompt = RequirePrompt(game, DecisionKind.SelectGeneral);
        Program.Assert(prompt.ValidContentIds.Contains(GeneralId), "The WPF fixture did not offer Han Dang.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat, GeneralId, game.Revision, prompt.PromptId));
        Program.Assert(selected.Accepted, selected.Error?.Message ?? "The WPF fixture could not select Han Dang.");
        return game;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Program.Assert(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Han Dang play.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Program.Assert(advanced.Accepted, advanced.Error?.Message ?? "The WPF Han Dang fixture could not advance.");
        }
        throw new InvalidOperationException("The WPF Han Dang fixture did not reach Play.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var answered = game.Submit(new AnswerPromptCommand(
            game.PendingDecision?.PlayerSeat ?? throw new InvalidOperationException("No prompt is pending."),
            game.PendingDecision.PromptId,
            choice.Id,
            game.Revision));
        Program.Assert(answered.Accepted, answered.Error?.Message ?? "The WPF Han Dang prompt answer was rejected.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException($"Expected {kind}, found {game.PendingDecision?.Kind}.");

    private static PlayerSnapshot Player(GameEngine game, int seat) =>
        game.CreateSnapshot(HumanSeat, revealAll: true).Players.Single(player => player.Seat == seat);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-han-dang-wpf-test-4";
        private const string DeckId = "fixture:han-dang-wpf-weapons";
        private static readonly string[] TargetIds =
            ["fixture:han-dang-wpf-target-1", "fixture:han-dang-wpf-target-2", "fixture:han-dang-wpf-target-3"];

        public PackageManifest Manifest { get; } = new(
            "han-dang-wpf-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 93, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in TargetIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "解烦界面目标", "supporter", "standard:none", "wei", BaseHp: 4));
            }
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "韩当界面武器测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 0,
                Cards: [new ContentDeckCardCount("standard:crossbow", 64)]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "四人经典身份（韩当界面场景）",
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
