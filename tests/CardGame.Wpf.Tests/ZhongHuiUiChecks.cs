using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class ZhongHuiUiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:zhong-hui";
    private const string PaiyiSkillId = "classic:paiyi";

    public static void CardPromptsAuthorityAndPaiyi(string output)
    {
        RenderGeneralCard(output);
        RenderQuanjiPrompt(output);
        RenderZiliAndPaiyi(output);
    }

    private static void RenderGeneralCard(string output)
    {
        using var viewModel = FindGeneralChoice();
        var zhongHui = viewModel.GeneralChoices.Single(choice => choice.GeneralId == GeneralId);
        var portrait = zhongHui.PortraitBrush as System.Windows.Media.ImageBrush;
        Program.Assert(zhongHui.Name == "钟会" &&
                       zhongHui.Kingdom == "魏" &&
                       zhongHui.SkillName == "权计 / 自立" &&
                       zhongHui.SkillDescription.Contains("每当你受到1点伤害后", StringComparison.Ordinal) &&
                       zhongHui.SkillDescription.Contains("获得“排异”", StringComparison.Ordinal) &&
                       zhongHui.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(zhongHui.GeneralId) &&
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
            $"The Zhong Hui card must render exact classic skills, Lord health and official art " +
            $"(name={zhongHui.Name}, kingdom={zhongHui.Kingdom}, skills={zhongHui.SkillName}, " +
            $"health={zhongHui.HealthText}, portrait={portrait?.ImageSource.Width}x{portrait?.ImageSource.Height}).");

        viewModel.PreviewGeneralChoiceCommand.Execute(zhongHui);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "197-classic-zhong-hui-card.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderQuanjiPrompt(string output)
    {
        var registry = CreateRegistry();
        var game = CreateFixtureGame(registry, FindFixtureSeed());
        BeginSelfFireAttack(game);
        Program.Assert(game.PendingDecision is
                       {
                           Kind: DecisionKind.ProgramTrigger,
                           PlayerSeat: HumanSeat,
                           IsPrivate: true
                       },
            "The WPF fixture did not pause at the private Quanji trigger.");

        using var viewModel = Load(game, registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var quanji = viewModel.HumanSkillCards.SingleOrDefault(skill => skill.Name == "权计");
        var zili = viewModel.HumanSkillCards.SingleOrDefault(skill => skill.Name == "自立");
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle.Contains("权计", StringComparison.Ordinal) &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("program-action") == "activate") &&
                       quanji is { TypeText: "状态技 · 触发技", StateText: "等待触发时机" } &&
                       zili is not null &&
                       zili.TypeText.Contains("觉醒技", StringComparison.Ordinal) &&
                       zili.TypeText.Contains("触发技", StringComparison.Ordinal),
            $"The private Quanji prompt or skill concepts were not projected correctly " +
            $"(guide={viewModel.CurrentGuideTitle}, quanji={quanji?.TypeText}/{quanji?.StateText}, zili={zili?.TypeText}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "198-classic-zhong-hui-quanji-choice.png"));

        viewModel.SelectSkillChoiceCommand.Execute(viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle.Contains("权计", StringComparison.Ordinal) &&
                       viewModel.SkillChoices.All(choice =>
                           choice.Parameters.GetValueOrDefault("program-action") == "select-source-card" &&
                           choice.Parameters.ContainsKey("slot-index")),
            "After drawing for Quanji, WPF must require one exact hand card to become Authority.");
        viewModel.SelectSkillChoiceCommand.Execute(viewModel.SkillChoices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-source-card"));
        if (viewModel.CanStepAi) viewModel.RunToHumanCommand.Execute(null);
        Program.Assert(viewModel.HumanPlayer is { AuthorityText: "权 ×1", HasAuthority: true } &&
                       viewModel.HumanSummary.Contains("权 1", StringComparison.Ordinal),
            "The first Authority must be visible on the seat and in the human summary.");
        window.Content = null;
        window.Close();
    }

    private static void RenderZiliAndPaiyi(string output)
    {
        var registry = CreateRegistry();
        var game = CreateFixtureGame(registry, FindFixtureSeed());
        for (var index = 0; index < 3; index++) ResolveSelfFireAttack(game);
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        Program.Assert(game.Submit(new EndPlayPhaseCommand(HumanSeat, game.Revision, play.PromptId)).Accepted,
            "The WPF Zhong Hui fixture could not end its play phase.");
        ReachPrompt(game, DecisionKind.ProgramTrigger, 2_048);

        using var viewModel = Load(game, registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle == "自立 · 选择方式" &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("program-action") == "activate" &&
                           choice.Parameters.GetValueOrDefault("binding-id") == "recover") &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("program-action") == "activate" &&
                           choice.Parameters.GetValueOrDefault("binding-id") == "draw") &&
                       viewModel.SkillChoices.All(choice =>
                           choice.Parameters.GetValueOrDefault("program-action") != "skip") &&
                       viewModel.HumanPlayer?.AuthorityText == "权 ×3",
            $"The mandatory Zili choice must expose both legal benefits and three Authorities " +
            $"(guide={viewModel.CurrentGuideTitle}, authority={viewModel.HumanPlayer?.AuthorityText}).");

        viewModel.SelectSkillChoiceCommand.Execute(viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate" &&
            choice.Parameters.GetValueOrDefault("binding-id") == "recover"));
        if (viewModel.CanStepAi) viewModel.RunToHumanCommand.Execute(null);
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        var paiyi = viewModel.HumanSkillCards.SingleOrDefault(skill => skill.Name == "排异");
        var action = viewModel.HumanActiveSkillActions.SingleOrDefault(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill && candidate.ProgramSkillId == PaiyiSkillId);
        Program.Assert(Program.Engine(viewModel).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat } &&
                       paiyi is
                       {
                           TypeText: "主动技",
                           StateText: "当前可发动",
                           SourceText: "钟会 · 觉醒获得",
                           IsAvailable: true
                       } &&
                       action is not null,
            $"Zili must add an available acquired Paiyi card in the same turn " +
            $"(pending={Program.Engine(viewModel).PendingDecision?.Kind}, paiyi={paiyi?.TypeText}/{paiyi?.StateText}/{paiyi?.SourceText}).");

        viewModel.SelectActiveSkillCommand.Execute(action);
        Program.Assert(viewModel.IsActiveSkillSelectionPending &&
                       viewModel.ActiveSkillEquipmentChoices.Count == 3 &&
                       viewModel.ActiveSkillEquipmentChoices.All(choice =>
                           choice.Description.Contains("“权”", StringComparison.Ordinal)),
            "Paiyi must expose the three public Authority cards in the shared active-skill draft.");
        viewModel.SelectActiveSkillEquipmentChoiceCommand.Execute(viewModel.ActiveSkillEquipmentChoices[0]);
        viewModel.SelectTargetCommand.Execute(viewModel.Seats.Single(seat => seat.IsHuman));
        Program.Assert(viewModel.CanConfirmActiveSkill &&
                       viewModel.CurrentGuideTitle == "确认发动【排异】" &&
                       viewModel.HumanPlayer?.AuthorityText == "权 ×3",
            $"Selecting one Authority and one living target must enable Paiyi confirmation " +
            $"(confirm={viewModel.CanConfirmActiveSkill}, guide={viewModel.CurrentGuideTitle}, " +
            $"selection={viewModel.SelectedCardText}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "199-classic-zhong-hui-zili-paiyi.png"));

        viewModel.UseActiveSkillCommand.Execute(null);
        if (viewModel.CanStepAi) viewModel.RunToHumanCommand.Execute(null);
        Program.Assert(viewModel.HumanPlayer?.AuthorityText == "权 ×2" &&
                       viewModel.BattleCues.Any(cue =>
                           cue.Kind == BattleCueKind.Response &&
                           cue.Label == "排异 · 已发动"),
            "Confirmed Paiyi must remove one Authority and publish its public battle cue.");
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
        throw new InvalidOperationException("Could not find a deterministic classic Zhong Hui WPF card fixture.");
    }

    private static int FindFixtureSeed()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateGame(registry, seed);
            StartAndSelect(game);
            var human = game.CreateSnapshot(HumanSeat, revealAll: true).Players.Single(player => player.Seat == HumanSeat);
            if (human.Hand.Count(card => card.Kind == CardKind.FireAttack) == 3 &&
                game.GetHumanLegalActions().Count(action =>
                    action.Kind == LegalActionKind.FireAttack && action.TargetSeat == HumanSeat) == 3)
            {
                return seed;
            }
        }
        throw new InvalidOperationException("No bounded Zhong Hui WPF fixture dealt all three Fire Attacks.");
    }

    private static GameEngine CreateFixtureGame(ContentRegistry registry, int seed)
    {
        var game = CreateGame(registry, seed);
        StartAndSelect(game);
        Program.Assert(game.CreateSnapshot(HumanSeat, revealAll: true).Players
                           .Single(player => player.Seat == HumanSeat).Hand
                           .Count(card => card.Kind == CardKind.FireAttack) == 3,
            $"Seed {seed} did not reproduce the Zhong Hui WPF deal.");
        return game;
    }

    private static void BeginSelfFireAttack(GameEngine game)
    {
        ReachPrompt(game, DecisionKind.PlayCard, 512);
        var action = game.GetHumanLegalActions().First(candidate =>
            candidate.Kind == LegalActionKind.FireAttack && candidate.TargetSeat == HumanSeat);
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        var result = game.Submit(new PlayCardCommand(
            HumanSeat,
            action.CardId!.Value,
            [HumanSeat],
            game.Revision,
            play.PromptId));
        Program.Assert(result.Accepted, result.Error?.Message ?? "The self Fire Attack was rejected.");
        AnswerCard(game, DecisionKind.FireAttackReveal, CardKind.Dodge);
        AnswerCard(game, DecisionKind.FireAttackDiscard, CardKind.Dodge);
        Program.Assert(game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat },
            "Self Fire Attack did not reach Quanji.");
    }

    private static void ResolveSelfFireAttack(GameEngine game)
    {
        BeginSelfFireAttack(game);
        var activate = RequirePrompt(game, DecisionKind.ProgramTrigger).Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Answer(game, activate);
        var dodgeIds = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Single(player => player.Seat == HumanSeat).Hand
            .Where(card => card.Kind == CardKind.Dodge)
            .Select(card => card.Id)
            .ToHashSet();
        var hand = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand;
        var store = RequirePrompt(game, DecisionKind.ProgramTrigger).Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-source-card" &&
            int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"), out var slot) &&
            slot >= 0 && slot < hand.Count && dodgeIds.Contains(hand[slot].Id));
        Answer(game, store);
        ReachPrompt(game, DecisionKind.PlayCard, 512);
    }

    private static void AnswerCard(GameEngine game, DecisionKind kind, CardKind cardKind)
    {
        var ids = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Single(player => player.Seat == HumanSeat).Hand
            .Where(card => card.Kind == cardKind)
            .Select(card => card.Id)
            .ToHashSet();
        var choice = RequirePrompt(game, kind).Choices.First(candidate =>
            candidate.Cards.Count == 1 && ids.Contains(candidate.Cards[0]));
        Answer(game, choice);
    }

    private static void Answer(GameEngine game, DecisionKind kind, string action) =>
        Answer(game, RequirePrompt(game, kind).Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == action));

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("There is no Zhong Hui WPF prompt.");
        var result = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Program.Assert(result.Accepted, result.Error?.Message ?? "The Zhong Hui WPF prompt answer was rejected.");
    }

    private static void ReachPrompt(GameEngine game, DecisionKind kind, int limit)
    {
        for (var step = 0; step < limit; step++)
        {
            if (game.PendingDecision is { PlayerSeat: HumanSeat } prompt)
            {
                if (prompt.Kind == kind) return;
                throw new InvalidOperationException($"Unexpected human prompt {prompt.Kind} before {kind}.");
            }
            var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Program.Assert(result.Accepted, result.Error?.Message ?? $"Could not advance to {kind}.");
        }
        throw new InvalidOperationException($"The Zhong Hui WPF fixture did not reach {kind}.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static void StartAndSelect(GameEngine game)
    {
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The Zhong Hui WPF fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Program.Assert(selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
            "The Zhong Hui WPF fixture omitted its formal general.");
        Program.Assert(game.Submit(new SelectGeneralCommand(
                HumanSeat,
                GeneralId,
                game.Revision,
                selection.PromptId)).Accepted,
            "The Zhong Hui WPF fixture could not select its formal general.");
        ReachPrompt(game, DecisionKind.PlayCard, 512);
    }

    private static GameEngine CreateGame(ContentRegistry registry, int seed) =>
        GameEngine.CreateStandard(new GameOptions
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

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-zhong-hui-wpf-test-4";
        private const string DeckId = "fixture:zhong-hui-wpf-deck";
        private static readonly string[] BlankGeneralIds =
        [
            "fixture:zhong-hui-wpf-a",
            "fixture:zhong-hui-wpf-b",
            "fixture:zhong-hui-wpf-c"
        ];

        public PackageManifest Manifest { get; } = new(
            "zhong-hui-wpf-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 86, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in BlankGeneralIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "排异界面目标",
                    "supporter",
                    "standard:none",
                    "shu",
                    BaseHp: 8));
            }

            var cards = new List<ContentDeckPhysicalCard>();
            for (var index = 0; index < 3; index++)
                cards.Add(new ContentDeckPhysicalCard("standard:fire_attack", Suit.Spade, index + 1));
            for (var index = 3; index < 160; index++)
                cards.Add(new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, index % 13 + 1));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "钟会权计自立排异界面测试牌堆",
                InitialHandSize: 12,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = cards.AsReadOnly()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "钟会技能链界面测试",
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
