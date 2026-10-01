using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class DeferredCardsUiChecks
{
    public static void PrivateViewAndPublicPileRestoreThroughSharedControls(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture());
        var game = ReachPrivateView(registry);
        var cards = game.CreateSnapshot(0).PrivateRevealedCards!.ToArray();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store,
            useExpandedContent: true, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        Program.Assert(model.HasPrivatelyViewedCards && model.PrivateRevealTitle == "看牌测试 · 仅你可见" &&
                       SameFaces(model.PrivatelyViewedCards, cards) && model.PrivatelyViewedCards.All(face =>
                           face.PublicCardLabel == "仅你可见" && !model.SelectRevealedCardCommand.CanExecute(face)) &&
                       Enumerable.Range(1, 3).All(seat => game.CreateSnapshot(seat).PrivateRevealedCards is null) &&
                       Enumerable.Range(0, 4).All(seat => game.CreateSnapshot(seat).PublicRevealedCards.Count == 0),
            "Restored private top faces must preserve physical identity and remain hidden from every other viewer.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "234-private-top-view.png"));
        var privatePanel = Program.Find<ItemsControl>(root).Single(items => items.Name == "PrivatelyViewedCards");
        ReadOnlyFaces(privatePanel, model, cards.Length);
        model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Single());
        Program.Assert(!model.HasPrivatelyViewedCards && model.PrivatelyViewedCards.Count == 0 &&
                       game.CreateSnapshot(0).PrivateRevealedCards is null,
            "The shared confirmation must retire private faces after the real top-card continuation.");

        for (var step = 0; step < 500 && !game.CreateSnapshot(0).Players.Any(player => player.PublicDeferredPileCount > 0); step++)
            Drive(game);
        var owner = game.CreateSnapshot(0).Players.FirstOrDefault(player => player.PublicDeferredPileCount > 0) ??
                    throw new InvalidOperationException("The bounded fixture must create an actual provider's public deferred pile.");
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var pile = model.DeferredPublicPiles.Single(item => item.OwnerSeat == owner.Seat);
        Program.Assert(model.HasDeferredPublicPiles && model.HasCenterChoices &&
                       pile.Title.Contains(owner.GeneralName + " · 弼") &&
                       SameFaces(pile.Cards, owner.PublicDeferredPileCards!) && pile.Cards.All(face =>
                           face.PublicCardLabel == "公开牌" && !model.SelectRevealedCardCommand.CanExecute(face)) &&
                       model.Seats.Single(seat => seat.Seat == owner.Seat).HasDeferredPile,
            "Restored public piles must retain their owner, actual faces, named seat badge and a visible read-only panel.");
        Program.Render(root, 1120, 740, Path.Combine(output, "235-deferred-public-pile.png"));
        var publicPanel = Program.Find<ItemsControl>(root).Single(items => items.Name == "DeferredPublicPiles");
        ReadOnlyFaces(publicPanel, model, model.DeferredPublicPiles.Sum(item => item.Cards.Count));
        window.Content = null;
        window.Close();
    }

    private static bool SameFaces(IEnumerable<CardViewModel> faces, IReadOnlyList<CardSnapshot> cards)
    {
        var actual = faces.ToArray();
        string Glyph(Suit suit) => suit switch { Suit.Spade => "♠", Suit.Heart => "♥", Suit.Club => "♣", Suit.Diamond => "♦", _ => "" };
        return actual.Length == cards.Count && cards.All(card => actual.Any(face =>
            face.Id == card.Id && face.Kind == card.Kind && face.SuitGlyph == Glyph(card.Suit) && face.Rank == card.RankText)) &&
            actual.All(face => !face.IsPlayable && !face.IsPublicChoice && !face.IsSelected);
    }

    private static void ReadOnlyFaces(ItemsControl panel, MainViewModel model, int count)
    {
        var faces = Program.Find<Button>(panel).Where(button => button.Command == model.SelectRevealedCardCommand).ToArray();
        Program.Assert(panel.ActualWidth > 0 && panel.ActualHeight > 0 && faces.Length == count &&
                       faces.All(button => !button.IsEnabled && button.ActualWidth > 0 && button.ActualHeight > 0),
            "Actual shared XAML must render the complete face set without an enabled card-selection action.");
    }

    private static GameEngine ReachPrivateView(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 1, HumanSeat = 0, HumanRole = Role.Lord,
            PlayerCount = 4, ModeId = Fixture.ModeId, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:ui-deferred-viewer", game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 64; step++)
        {
            if (game.CreateSnapshot(0).PrivateRevealedCards is { Count: > 0 }) return game;
            Drive(game);
        }
        throw new InvalidOperationException("The bounded fixture did not reach a real private top-card view.");
    }

    private static void Drive(GameEngine game)
    {
        var pending = game.PendingDecision ?? Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
        if (pending is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play)
            Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId)));
        else if (pending is { Choices.Count: > 0 } prompt && prompt.Kind != DecisionKind.PlayCard)
        {
            var choice = prompt.Choices.FirstOrDefault(choice => choice.Targets.Contains(0)) ??
                         prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards") ??
                         prompt.Choices.First();
            Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision)));
        }
        else Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    }

    private static void Accept(CommandResult result) => Program.Assert(result.Accepted, result.Error?.Message ?? "Expected accepted fixture command.");

    private sealed class Fixture : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-ui-deferred-4";
        public PackageManifest Manifest { get; } = new("fixture-ui-deferred", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:ui-deferred","revision":1,
                "triggers":[{"id":"private-view-and-deposit","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},
                {"op":"viewTopCardsAndObtainMatchingCards","target":"selectedTarget","amount":2,"cardCategories":["basic"]},
                {"op":"selectOwnedCards","target":"selectedTarget","zones":["hand","equipment"],"minimumCards":1,"maximumCards":3,"resultBind":"provided"},
                {"op":"depositBoundCardsUntilNextTurn","target":"owner","sourceBind":"provided"}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:ui-deferred":{"name":"看牌测试","description":"共享私有看牌与延迟牌堆界面","authorityName":"弼"}}}""");
            builder.AddSkill(new("fixture:ui-deferred", "看牌测试", "共享私有看牌与延迟牌堆界面")
                { Program = catalog.Programs["fixture:ui-deferred"], ProgramPresentation = catalog.Presentations["fixture:ui-deferred"] });
            builder.AddGeneral(new("fixture:ui-deferred-viewer", "观看者", "supporter", "standard:none", "wei", 20));
            var others = Enumerable.Range(1, 3).Select(index => $"fixture:ui-deferred-{index}").ToArray();
            foreach (var id in others)
                builder.AddGeneral(new(id, "牌堆拥有者", "supporter", "fixture:ui-deferred", "wu", 20));
            builder.AddDeck(new("fixture:ui-deferred-deck", "看牌测试牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard(
                    index % 2 == 0 ? "standard:dodge" : "standard:bagua", (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(ModeId, "看牌界面测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:ui-deferred-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:ui-deferred-viewer", .. others]));
        }
    }
}
