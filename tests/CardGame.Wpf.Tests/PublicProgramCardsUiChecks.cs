using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PublicProgramCardsUiChecks
{
    public static void RevealedChoicesRestoreAndSubmit(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 1, HumanSeat = 0, HumanRole = Role.Lord,
            PlayerCount = 4, ModeId = "identity:ui-public-program", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 2 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:ui-public-owner", game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 64 && game.CreateSnapshot(0).PendingDecision?.SkillPrompt is null; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        var shown = game.CreateSnapshot(0).PublicRevealedCards.ToArray();
        Program.Assert(shown.Length == 2 && game.CreateSnapshot(0).PendingDecision!.Choices.All(choice => choice.Cards.Count == 1) &&
                       Enumerable.Range(0, 4).All(seat => game.CreateSnapshot(seat).PublicRevealedCards.Select(card => card.Id)
                           .SequenceEqual(shown.Select(card => card.Id))),
            "The real shared program must reveal two physical cards to all observers and publish exact single-card choices.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true,
            contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError && model.PublicRevealedCards.Count == 2 &&
                       model.PublicRevealedCards.All(face => face.IsPublicChoice && face.PublicCardLabel == "点击选取" &&
                           model.SelectRevealedCardCommand.CanExecute(face)) &&
                       shown.All(card => model.PublicRevealedCards.Any(face => face.Id == card.Id && face.Kind == card.Kind &&
                           face.Rank == card.RankText)),
            "Restored generic skill choices must make the actual public faces selectable through the shared command.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "238-public-program-choices.png"));
        var panel = Program.Find<ItemsControl>(root).Single(control => control.Name == "PublicRevealedCards");
        var buttons = Program.Find<Button>(panel).Where(button => button.Command == model.SelectRevealedCardCommand).ToArray();
        Program.Assert(buttons.Length == 2 && buttons.All(button => button.IsEnabled && button.ActualWidth > 0),
            "The shared XAML face buttons must expose both legal choices.");
        var selected = model.PublicRevealedCards.First();
        model.SelectRevealedCardCommand.Execute(selected);
        game = Program.Engine(model);
        Program.Assert(game.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == selected.Id) &&
                       game.CardMovements.Any(move => move.CardId == selected.Id && move.To == CardLocation.Hand(0)) &&
                       !model.SelectRevealedCardCommand.CanExecute(selected),
            "Clicking the public face must submit the real current prompt and retire the old choice after actual gain.");
        window.Content = null;
        window.Close();
    }

    private static void Accept(CommandResult result) => Program.Assert(result.Accepted, result.Error?.Message ?? "Expected accepted fixture command.");

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ui-public-program", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:ui-public-program","revision":1,
                "triggers":[{"id":"reveal-and-choose","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,
                "effects":[{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"shown","visibility":"public"},
                {"op":"selectCardSubset","target":"owner","sourceBind":"shown","resultBind":"chosen","minimumCards":1,"maximumCards":1,"maximumRankSum":208,"aiOrder":"mostCardsThenRankSum"},
                {"op":"moveBoundCards","target":"owner","sourceBind":"chosen","destination":"ownerHand"},
                {"op":"moveBoundCards","target":"owner","sourceBind":"shown","exceptBind":"chosen","destination":"discardPile"}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:ui-public-program":{"name":"公开选牌","description":"通过共享牌面选择实际展示牌"}}}""");
            builder.AddSkill(new("fixture:ui-public-program", "公开选牌", "通过共享牌面选择实际展示牌")
            { Program = catalog.Programs["fixture:ui-public-program"], ProgramPresentation = catalog.Presentations["fixture:ui-public-program"] });
            builder.AddGeneral(new("fixture:ui-public-owner", "选牌者", "supporter", "fixture:ui-public-program", "wei", 9));
            var others = Enumerable.Range(1, 3).Select(seat => $"fixture:ui-public-{seat}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "旁观者", "supporter", "standard:none", "wu", 9));
            builder.AddDeck(new("fixture:ui-public-deck", "公开选择实体牌", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard("standard:dodge", (Suit)(index % 4), index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:ui-public-program", "公开选择界面测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:ui-public-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:ui-public-owner", .. others]));
        }
    }
}
