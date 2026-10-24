using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Tests;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class CardDistributionUiChecks
{
    public static void TuoyuMultiSelectRegionsConfirmAndRestore(string output)
    {
        var (game, registry) = ShenDengAiChecks.CreateAllocationTableFixture();
        var store = new MemorySaveStore();
        Save();
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            Program.Assert(model.IsCardDistributionPending && !model.IsCardPaymentSelectionPending && !model.IsHandResponsePending &&
                model.DistributionCards.Count == model.Hand.Count && model.DistributionDestinations.Count == 3,
                "The actual allocation shows each hand entity once and three named domains, outside the payment UI.");
            Program.Assert(model.DistributionDestinations.Any(t => t.Label.Contains("无次数与距离限制")) &&
                model.DistributionDestinations.Any(t => t.Label.Contains("伤害与回复值 +1")) &&
                model.DistributionDestinations.Any(t => t.Label.Contains("无法被响应")), "All domain effects must be visible before selecting a card.");
            var staleCard = model.DistributionCards[^1];
            var staleTarget = model.DistributionDestinations[1];
            var ids = model.Hand.TakeLast(2).Select(card => card.Id).ToArray();
            var before = game.Events.Count(e => e.Payload is ShenDengAiAssignedEvent);
            var movements = game.CardMovements.Count;
            foreach (var id in ids) model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == id));
            model.SelectDistributionTargetCommand.Execute(model.DistributionDestinations.Single(t => t.Key == "domain:2"));
            Program.Assert(model.CanConfirmSelected && model.Hand.Count(card => card.IsSelected) == 2 &&
                model.DistributionCards.Count(card => card.IsSelected) == 2 && game.Events.Count(e => e.Payload is ShenDengAiAssignedEvent) == before,
                "Multi-select and choosing a region only draft the assignment until explicit confirmation.");
            model.SelectDistributionCardCommand.Execute(model.DistributionCards.Single(card => card.Id == ids[0]));
            Program.Assert(model.Hand.Count(card => card.IsSelected) == 1, "Click-again cancellation stays synchronized with the real hand.");
            model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == ids[0]));
            Program.Render(root, 1120, 740, Path.Combine(output, "tuoyu-batch-selected.png"));
            Program.Assert(((ItemsControl)window.FindName("DistributionCardChoices")).Visibility == Visibility.Collapsed &&
                Program.Find<Button>((ItemsControl)window.FindName("DistributionTargetChoices")).Count() == 3,
                "The rendered picker uses the real hand without duplicate faces and exposes one named button per region.");
            model.ConfirmSelectedCommand.Execute(null);
            Program.Assert(game.Events.Select(e => e.Payload).OfType<ShenDengAiAssignedEvent>().Skip(before).Select(e => e.CardId).ToHashSet().SetEquals(ids) &&
                game.CreateSnapshot(0).Players[0].Hand.Where(card => ids.Contains(card.Id)).All(card => card.SkillMarkLabel == "丰田") &&
                game.CardMovements.Count == movements && !model.CanConfirmSelected,
                "One confirmation assigns exactly the selected cards without physical movement or unintended repeats.");
            Save(); model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
            model.SelectDistributionCardCommand.Execute(staleCard); model.SelectDistributionTargetCommand.Execute(staleTarget);
            Program.Assert(model.Hand.All(card => !card.IsSelected) && !model.CanConfirmSelected &&
                model.DistributionDestinations.Single(t => t.Key == "domain:2").Label.Contains("2 张"),
                "Restore retains the committed region counts, clears the draft, and rejects retired card and region controls.");
            model.SelectAllDistributionCardsCommand.Execute(null);
            Program.Assert(model.DistributionCards.All(card => card.IsSelected), "Select-all includes every remaining legal candidate.");
            model.ClearSelectionCommand.Execute(null);
            Program.Assert(model.DistributionCards.All(card => !card.IsSelected) && model.Hand.All(card => !card.IsSelected),
                "Esc clears both selection surfaces without ending mandatory allocation.");
            Program.Render(root, 1600, 1000, Path.Combine(output, "tuoyu-batch-restored.png"));
            GameReplay.Restore(game.CreateCheckpoint(), registry);
        }
        finally { window.Content = null; window.Close(); }
        void Save() => store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
    }

    public static void ConsecutiveBoundGiftsShareBatchAndRespectLimit(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "fixture:batch-gift", UseInteractiveSetup = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
        Accept(new StartGameCommand());
        for (var i = 0; i < 50 && game.CreateSnapshot(0).PendingDecision?.Choices.Any(c => c.Parameters.ContainsKey("distribution-target")) != true; i++)
            Accept(new AdvanceOneStepCommand(game.Revision));
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
        Program.Assert(model.IsCardDistributionPending && model.DistributionCards.Count >= 3, "The real consecutive give instructions expose their owned cards in the common picker.");
        model.SelectAllDistributionCardsCommand.Execute(null);
        var ids = model.Hand.Where(card => card.IsSelected).Select(card => card.Id).ToArray();
        Program.Assert(ids.Length == 2, "Selecting all respects the remaining two-gift instruction budget.");
        model.SelectDistributionTargetCommand.Execute(model.DistributionDestinations.Single(t => t.Seat == 1));
        var before = game.CreateSnapshot(1).Players[1].Hand.Select(card => card.Id).ToHashSet();
        Program.Assert(model.CanConfirmSelected, "A two-card batch can be confirmed for one legal recipient.");
        model.ConfirmSelectedCommand.Execute(null);
        Program.Assert(game.CreateSnapshot(1).Players[1].Hand.Select(card => card.Id).Except(before).ToHashSet().SetEquals(ids) &&
            game.Events.Select(e => e.Payload).OfType<ProgramBoundCardGivenEvent>().Count() == 2 && !model.IsCardDistributionPending,
            "A single confirmation commits both original instructions exactly once and stops at the following effect.");
        GameReplay.Restore(game.CreateCheckpoint(), registry);
        void Accept(GameCommand command) { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Fixture rejected."); }
    }

    public static void PublicPileCardsRemainVisibleAndStopAtGainChild(string output)
    {
        var (game, registry) = Fame2017PublicPileChecks.CreateDistributionTableFixture();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
        var window = new MainWindow(model);
        try
        {
            Program.Assert(model.IsCardDistributionPending && model.ShowDistributionCards && model.DistributionCards.Count == 4 &&
                model.DistributionCards.All(card => card.Kind is not null) && model.DistributionFinishChoices.Count == 0,
                "Public pile distribution displays each authorized real face once and keeps its mandatory completion rule.");
            model.SelectAllDistributionCardsCommand.Execute(null);
            model.SelectDistributionTargetCommand.Execute(model.DistributionDestinations.Single(target => target.Seat == 1));
            Program.Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, "public-pile-batch-selected.png"));
            var before = game.CardMovements.Count;
            model.ConfirmSelectedCommand.Execute(null);
            Program.Assert(game.CardMovements.Skip(before).Count(move => move.Reason.Value == "skill-program.public-pile.distribute") == 1 &&
                !model.IsCardDistributionPending,
                "A gain child stops the batch after the first real gift without losing remaining pile entities.");
            PendingDecision? Child() => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision)
                .FirstOrDefault(prompt => prompt?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "choose-option") == true);
            for (var i = 0; i < 30 && Child() is null; i++)
            {
                var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Program.Assert(result.Accepted, result.Error?.Message ?? "Cannot reach gain child.");
            }
            var child = Child();
            Program.Assert(child is not null && game.CardMovements.Skip(before).Count(move => move.Reason.Value == "skill-program.public-pile.distribute") == 1,
                "The intervening child must remain unanswered by the batch and no second card may leave the pile.");
            GameReplay.Restore(game.CreateCheckpoint(), registry);
            var answer = game.Submit(new AnswerPromptCommand(child!.PlayerSeat, child.PromptId, child.Choices.Single().Id, game.Revision));
            Program.Assert(answer.Accepted, answer.Error?.Message ?? "Cannot finish gain child.");
            for (var i = 0; i < 30 && !model.IsCardDistributionPending; i++)
            {
                var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Program.Assert(result.Accepted, result.Error?.Message ?? "Cannot resume native distribution.");
            }
            Program.Assert(model.IsCardDistributionPending && model.DistributionCards.Count == 3 &&
                model.DistributionCards.All(card => !card.IsSelected) && !model.CanConfirmSelected,
                "Returning from the gain child shows exactly the remaining public cards with a fresh selection.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-batch-gift", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:batch-give","revision":1,"triggers":[
                  {"id":"give","window":"gameStarting","subject":"owner","optional":false,"effects":[
                    {"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"available"},
                    {"op":"giveBoundCard","target":"owner","sourceBind":"available","targetKind":"otherLiving"},
                    {"op":"giveBoundCard","target":"owner","sourceBind":"available","targetKind":"otherLiving"}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { ["fixture:batch-give"] = new { name = "赠牌夹具", description = "连续交出两张牌" } } }));
            builder.AddSkill(new("fixture:batch-give", "赠牌夹具", "连续交出两张牌") { Program = catalog.Programs["fixture:batch-give"] });
            builder.AddGeneral(new("fixture:batch-owner", "赠牌者", "", "fixture:batch-give", "wei", 4));
            for (var seat = 1; seat <= 3; seat++) builder.AddGeneral(new("fixture:batch-peer-" + seat, "同桌" + seat, "", "fixture:batch-give", "wei", 4));
            builder.AddDeck(new("fixture:batch-deck", "固定牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 60).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            builder.AddMode(new("fixture:batch-gift", "赠牌小局", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:batch-deck", GeneralCandidateCount: 1, GeneralPoolIds: ["fixture:batch-owner", "fixture:batch-peer-1", "fixture:batch-peer-2", "fixture:batch-peer-3"]));
        }
    }
}
