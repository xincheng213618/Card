using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class XunYouUiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:xun-you";

    public static void CardAndPrivatePrompts(string output)
    {
        RenderGeneralCard(output);
        RenderQicePrompt(output);
        RenderZhiyuPrompt(output);
    }

    private static void RenderGeneralCard(string output)
    {
        using var viewModel = FindGeneralChoice();
        var xunYou = viewModel.GeneralChoices.Single(choice => choice.GeneralId == GeneralId);
        var portrait = xunYou.PortraitBrush as System.Windows.Media.ImageBrush;
        Program.Assert(xunYou.Name == "荀攸" && xunYou.Kingdom == "魏" &&
                       xunYou.SkillName == "奇策 / 智愚" &&
                       xunYou.SkillDescription.Contains("所有手牌", StringComparison.Ordinal) &&
                       xunYou.SkillDescription.Contains("展示所有手牌", StringComparison.Ordinal) &&
                       xunYou.HealthText == "体力上限 4" &&
                       GeneralArt.HasPortrait(xunYou.GeneralId) &&
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
            $"The Xun You card must render exact classic skills, Lord health and official art " +
            $"(name={xunYou.Name}, kingdom={xunYou.Kingdom}, skills={xunYou.SkillName}, " +
            $"health={xunYou.HealthText}, portrait={portrait?.ImageSource.Width}x{portrait?.ImageSource.Height}).");

        viewModel.PreviewGeneralChoiceCommand.Execute(xunYou);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "200-classic-xun-you-card.png"));
        window.Content = null;
        window.Close();
    }

    private static void RenderQicePrompt(string output)
    {
        var registry = CreateRegistry();
        var game = CreateGame(registry, seed: 1, Role.Lord);
        StartAndSelect(game);
        ReachHumanPlay(game);

        using var viewModel = Load(game, registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var qice = viewModel.HumanSkillCards.SingleOrDefault(skill => skill.Name == "奇策");
        var zhiyu = viewModel.HumanSkillCards.SingleOrDefault(skill => skill.Name == "智愚");
        var action = viewModel.HumanActiveSkillActions.Single(candidate => candidate.Skill == SkillKind.Qice);
        Program.Assert(qice is { TypeText: "主动技", StateText: "当前可发动", IsAvailable: true } &&
                       zhiyu is { TypeText: "触发技", StateText: "等待触发时机" } &&
                       action.MinCardCount == viewModel.Hand.Count && action.MaxCardCount == viewModel.Hand.Count,
            $"The skill rail must classify Qice as available Active and Zhiyu as waiting Trigger " +
            $"(qice={qice?.TypeText}/{qice?.StateText}, zhiyu={zhiyu?.TypeText}/{zhiyu?.StateText}, " +
            $"cards={action.MinCardCount}/{action.MaxCardCount}/{viewModel.Hand.Count}).");

        viewModel.SelectActiveSkillCommand.Execute(action);
        foreach (var card in viewModel.Hand.ToArray()) viewModel.SelectCardCommand.Execute(card);
        Program.Assert(viewModel.IsActiveSkillSelectionPending && viewModel.CanConfirmActiveSkill &&
                       viewModel.Hand.All(card => card.IsSelected),
            "Qice must require every visible hand card in the shared active-skill draft.");
        viewModel.ConfirmSelectedCommand.Execute(null);
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle == "为奇策选择普通锦囊" &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("card-kind") == nameof(CardKind.DrawTwo)) &&
                       viewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("card-kind") == nameof(CardKind.Duel)),
            $"The second Qice stage must expose legal ordinary-trick choices through the private skill surface " +
            $"(guide={viewModel.CurrentGuideTitle}, choices={viewModel.SkillChoices.Count}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "201-classic-xun-you-qice-choice.png"));

        viewModel.SelectSkillChoiceCommand.Execute(viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("card-kind") == nameof(CardKind.DrawTwo)));
        Program.Assert(viewModel.BattleCues.Any(cue =>
                           cue.Kind == BattleCueKind.Response && cue.Label == "奇策 · 无中生有") &&
                       Program.Engine(viewModel).Events.Select(item => item.Payload).OfType<QiceConvertedEvent>().Any(),
            "Confirming Qice as Draw Two must publish the typed conversion and public battle cue.");
        window.Content = null;
        window.Close();
    }

    private static void RenderZhiyuPrompt(string output)
    {
        var fixture = FindZhiyuFixture();
        using var viewModel = Load(fixture.Game, fixture.Registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       viewModel.CurrentGuideTitle == "决定是否发动智愚" &&
                       viewModel.SkillChoices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                           .Order(StringComparer.Ordinal).SequenceEqual(["zhiyu-skip", "zhiyu-use"]),
            $"The WPF must render the optional private Zhiyu trigger without flattening it into an active button " +
            $"(guide={viewModel.CurrentGuideTitle}, choices={viewModel.SkillChoices.Count}).");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "202-classic-xun-you-zhiyu-choice.png"));

        viewModel.SelectSkillChoiceCommand.Execute(viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "zhiyu-use"));
        Program.Assert(!viewModel.IsSkillSelectionPending && viewModel.CanStepAi,
            "The AI source's exact Zhiyu discard prompt must remain hidden while the host can advance it.");
        viewModel.StepAiCommand.Execute(null);
        Program.Assert(Program.Engine(viewModel).Events.Select(item => item.Payload).OfType<ZhiyuResolvedEvent>()
                           .Any(result => result is { Used: true, AllSameColor: true, DiscardedCardId: not null }) &&
                       viewModel.BattleCues.Any(cue =>
                           cue.Kind == BattleCueKind.Response && cue.Label.StartsWith("智愚 · 展示", StringComparison.Ordinal)),
            "Resolving the hidden source discard must produce the public Zhiyu result and battle cue.");
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
        throw new InvalidOperationException("Could not find a deterministic classic Xun You WPF card fixture.");
    }

    private static Fixture FindZhiyuFixture()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = CreateGame(registry, seed, Role.Rebel);
            StartAndSelect(game);
            for (var step = 0; step < 1_024; step++)
            {
                if (game.PendingDecision is
                    { Kind: DecisionKind.Zhiyu, PlayerSeat: HumanSeat, SourceSeat: var sourceSeat } &&
                    sourceSeat is { } source &&
                    game.CreateSnapshot(HumanSeat, revealAll: true).Players[source].HandCount > 0)
                {
                    return new Fixture(game, registry);
                }
                if (game.PendingDecision is { PlayerSeat: HumanSeat }) break;
                Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                    "The Xun You WPF damage fixture could not advance.");
            }
        }
        throw new InvalidOperationException("No bounded Xun You WPF fixture reached Zhiyu.");
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 1_024; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Program.Assert(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} before Qice play.");
            Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Xun You WPF fixture could not reach human play.");
        }
        throw new InvalidOperationException("The Xun You WPF fixture did not reach human play.");
    }

    private static void StartAndSelect(GameEngine game)
    {
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The Xun You WPF fixture failed to start.");
        var selection = game.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: HumanSeat } prompt
            ? prompt
            : throw new InvalidOperationException("The Xun You WPF fixture did not pause for general selection.");
        Program.Assert(selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
            "The Xun You WPF fixture omitted its formal general.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat, GeneralId, game.Revision, selection.PromptId));
        Program.Assert(selected.Accepted, selected.Error?.Message ?? "The Xun You WPF fixture could not select its general.");
    }

    private static GameEngine CreateGame(ContentRegistry registry, int seed, Role role) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = role,
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

    private sealed record Fixture(GameEngine Game, ContentRegistry Registry);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-xun-you-wpf-test-4";
        private const string DeckId = "fixture:xun-you-wpf-red-slash-deck";
        private static readonly string[] BlankGeneralIds =
        [
            "fixture:xun-you-wpf-a",
            "fixture:xun-you-wpf-b",
            "fixture:xun-you-wpf-c"
        ];

        public PackageManifest Manifest { get; } = new(
            "xun-you-wpf-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 87, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in BlankGeneralIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "智愚界面目标",
                    "supporter",
                    "standard:none",
                    "shu",
                    BaseHp: 8));
            }
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "荀攸奇策智愚界面测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 192)
                    .Select(index => new ContentDeckPhysicalCard("standard:slash", Suit.Heart, index % 13 + 1))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "荀攸技能链界面测试",
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
