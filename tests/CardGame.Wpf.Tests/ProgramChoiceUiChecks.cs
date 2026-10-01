using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ProgramChoiceUiChecks
{
    private const string SkillId = "fixture:ui-choice";

    public static void NamedChoiceUsesSharedSurfaceAndCommand(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new ChoicePackage());
        var game = ReachChoice(registry);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(autoAdvance: false, seed: game.Seed, showSetup: true,
            saveStore: store, useExpandedContent: true, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var prompt = game.PendingDecision!;
        Program.Assert(model.IsSkillSelectionPending && model.SkillChoices.Count == 2 &&
                       model.SkillChoices.Select(choice => choice.Description).SequenceEqual(["摸两张牌", "保持当前状态"]) &&
                       model.CurrentDecisionContext is { Title: "公共选择 · 选择效果", TargetSeat: 0 } &&
                       model.CurrentGuideTitle == prompt.SkillPrompt!.Title &&
                       game.CreateSnapshot(1).PendingDecision is null,
            "Named choices must use shared metadata, labels, guide and private responder projection.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "230-program-named-choice.png"));
        var before = game.CreateSnapshot(0).Players[0].HandCount;
        model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("option-id") == "draw"));
        Program.Assert(game.CreateSnapshot(0).Players[0].HandCount == before + 2 &&
                       game.Events.Count(item => item.Payload is ProgramOptionChosenEvent { SkillId: SkillId, OptionId: "draw" }) == 1 &&
                       model.BattleCues.Any(cue => cue.Label == "公共选择 · 摸两张牌") &&
                       !game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Any(),
            $"The shared WPF command must resolve one named choice and resume the parent once. " +
            $"Hand={before}->{game.CreateSnapshot(0).Players[0].HandCount}; " +
            $"choices={game.Events.Count(item => item.Payload is ProgramOptionChosenEvent)}; " +
            $"parents={game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Count()}; " +
            $"cues={string.Join("|", model.BattleCues.Select(cue => cue.Label))}");
        window.Content = null;
        window.Close();
        PublicHandRevealRestoresAndLeavesAfterColorPayment(output);
    }

    private static void PublicHandRevealRestoresAndLeavesAfterColorPayment(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new PublicHandRevealPackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 11, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = PublicHandRevealPackage.ModeId, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "classic:gongsun-yuan", game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 64 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Program.Assert(game.PendingDecision is { Kind: DecisionKind.PlayCard }, "The actual Huaiyi fixture did not reach Play.");
        var hand = game.CreateSnapshot(0).Players[0].Hand.ToArray();
        Accept(game.Submit(new UseProgramSkillCommand(0, "classic:huaiyi", "reveal-color-take", [], [], game.Revision, game.PendingDecision!.PromptId)));
        Program.Assert(game.PendingDecision is { IsPrivate: true, SkillPrompt.SkillId: "classic:huaiyi" } &&
                       game.PendingDecision.Choices.All(choice => choice.Parameters.GetValueOrDefault("stage") == "color"),
            "The actual Huaiyi fixture must pause before paying its color cost.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(autoAdvance: false, seed: game.Seed, showSetup: true,
            saveStore: store, useExpandedContent: true, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        string Glyph(Suit suit) => suit switch { Suit.Spade => "♠", Suit.Heart => "♥", Suit.Club => "♣", Suit.Diamond => "♦", _ => throw new InvalidOperationException("Unknown physical suit.") };
        Program.Assert(model.HasPublicRevealedCards && model.PublicRevealedCards.Count == hand.Length &&
                       model.PublicRevealTitle == "怀异 · 公开牌" && hand.All(card => model.PublicRevealedCards.Any(face =>
                           face.Id == card.Id && face.Kind == card.Kind && face.SuitGlyph == Glyph(card.Suit) && face.Rank == card.RankText)) &&
                       model.PublicRevealedCards.All(face => !face.IsPlayable && !face.IsPublicChoice && !face.IsSelected &&
                           !model.SelectRevealedCardCommand.CanExecute(face)) && game.CreateSnapshot(1).PendingDecision is null,
            "Restoring Huaiyi must show each real ID/kind/suit/rank as a read-only public face while preserving private color choices.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "233-huaiyi-public-hand.png"));
        var panel = Program.Find<ItemsControl>(root).Single(items => ReferenceEquals(items.ItemsSource, model.PublicRevealedCards));
        var faces = Program.Find<Button>(panel).Where(button => button.Command == model.SelectRevealedCardCommand).ToArray();
        Program.Assert(panel.Items.Count == hand.Length && panel.ActualWidth > 0 && panel.ActualHeight > 0 &&
                       faces.Length == hand.Length && faces.All(button => !button.IsEnabled && button.ActualWidth > 0 && button.ActualHeight > 0) &&
                       ((TextBlock)window.FindName("PublicRevealTitleText")).Visibility == Visibility.Visible,
            "The real shared XAML ItemsControl must render all public faces without an enabled card-selection action.");
        var colorChoice = model.SkillChoices.First(choice => choice.Parameters.GetValueOrDefault("stage") == "color");
        var red = colorChoice.Parameters["hand-control-action"] == "red";
        var paid = hand.Where(card => (card.Suit is Suit.Heart or Suit.Diamond) == red).ToArray();
        model.SelectSkillChoiceCommand.Execute(colorChoice);
        Program.Assert(paid.Length > 0 && paid.All(card => game.CardMovements.Any(move => move.CardId == card.Id &&
                           move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile)) &&
                       !model.HasPublicRevealedCards && model.PublicRevealedCards.Count == 0 &&
                       Enumerable.Range(0, 4).All(seat => game.CreateSnapshot(seat).PublicRevealedCards.Count == 0),
            "Paying an actual color through the shared WPF choice must discard its physical cards and retire the public faces for every viewer.");
        root.UpdateLayout();
        Program.Assert(panel.Items.Count == 0 && ((TextBlock)window.FindName("PublicRevealTitleText")).Visibility == Visibility.Collapsed,
            "After real payment, the existing XAML face collection and reveal heading must leave the shared panel.");
        window.Content = null;
        window.Close();
    }

    private static GameEngine ReachChoice(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 32; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
                ModeId = ChoicePackage.ModeId, UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
            }, registry);
            Accept(game.Submit(new StartGameCommand()));
            if (game.PendingDecision is not { Kind: DecisionKind.SelectGeneral } setup ||
                !setup.ValidContentIds.Contains("fixture:chooser")) continue;
            Accept(game.Submit(new SelectGeneralCommand(0, "fixture:chooser", game.Revision, setup.PromptId)));
            for (var step = 0; step < 64; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: SkillId }) return game;
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play)
                    Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId)));
                else Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            }
        }
        throw new InvalidOperationException("The bounded UI fixture did not reach the named choice.");
    }

    private static void Accept(CommandResult result) => Program.Assert(result.Accepted,
        result.Error?.Message ?? "Expected accepted fixture command.");

    private sealed class PublicHandRevealPackage : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-ui-public-hand-4";
        public PackageManifest Manifest { get; } = new("fixture-ui-public-hand", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var others = Enumerable.Range(1, 3).Select(index => $"fixture:public-hand-other-{index}").ToArray();
            foreach (var id in others)
                builder.AddGeneral(new ContentGeneralDefinition(id, "展示目标", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:ui-public-hand-deck", "公开手牌测试", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard(
                    index % 2 == 0 ? "standard:slash" : "standard:dodge", (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(ModeId, "公开手牌测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "fixture:ui-public-hand-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["classic:gongsun-yuan", .. others]));
        }
    }

    private sealed class ChoicePackage : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-ui-choice-4";
        public PackageManifest Manifest { get; } = new("fixture-ui-choice", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 0, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load("""
                {"schemaVersion":62,"skills":[{"id":"fixture:ui-choice","revision":1,
                "minimumRulesVersion": 171,"triggers":[{"id":"choice","window":"turnEnding",
                "subject":"owner","optional":false,"effects":[
                {"op":"chooseOption","target":"owner","resultBind":"answer","options":[{"id":"draw"},{"id":"stay"}]},
                {"op":"draw","target":"owner","amount":2,"condition":{"kind":"choiceIs","sourceBind":"answer","optionId":"draw"}}
                ]}]}]}
                """, """
                {"schemaVersion":3,"skills":{"fixture:ui-choice":{"name":"公共选择","description":"公共选项与普通摸牌组合。",
                "optionLabels":{"draw":"摸两张牌","stay":"保持当前状态"}}}}
                """);
            builder.AddSkill(new ContentSkillDefinition(SkillId, "公共选择", "公共选项与普通摸牌组合。")
            { Program = catalog.Programs[SkillId], ProgramPresentation = catalog.Presentations[SkillId] });
            builder.AddGeneral(new ContentGeneralDefinition("fixture:chooser", "选择者", "supporter", SkillId, "shu", BaseHp: 4));
            for (var index = 1; index < 4; index++)
                builder.AddGeneral(new ContentGeneralDefinition($"fixture:other-{index}", $"角色{index}", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:ui-choice-deck", "测试牌堆", 4, 0,
                [new ContentDeckCardCount("standard:slash", 60)]));
            builder.AddMode(new ContentModeDefinition(ModeId, "公共选择测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "fixture:ui-choice-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:chooser", "fixture:other-1", "fixture:other-2", "fixture:other-3"]));
        }
    }
}
