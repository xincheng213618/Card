using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ProgramIronChainUiChecks
{
    public static void SelectionKeepsPortraitGeometry(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(8));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 8, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.ModeId, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:chain-owner", game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Program.Assert(game.PendingDecision?.Kind == DecisionKind.PlayCard, "The eight-player geometry fixture must reach a real Play boundary.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var card = model.Hand.First(item => item.Kind == CardKind.IronChain && item.IsPlayable);
        var revision = Program.Engine(model).Revision;
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            foreach (var size in new[] { (1120, 740), (1440, 860), (1920, 1080) })
            {
                Program.Render(root, size.Item1, size.Item2, Path.Combine(output, $"iron-chain-stable-idle-{size.Item1}.png"));
                Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
                var portraits = Program.Find<Button>(root).Where(button => button.DataContext is SeatViewModel &&
                    BattleFeedbackLayer.GetSeatAnchor(button) >= 0).ToArray();
                Program.Assert(portraits.Length == 8, "All eight actual portraits must be measured.");
                var portraitBounds = portraits.ToDictionary(button => ((SeatViewModel)button.DataContext).Seat, button => Bounds(button));
                var battlefield = (FrameworkElement)window.FindName("Battlefield");
                var dock = (FrameworkElement)window.FindName("ActionBar");
                var baseline = new[] { Bounds(battlefield), Bounds(dock), Bounds((FrameworkElement)window.FindName("PrimaryActionButtons")) };
                void AssertStable()
                {
                    Program.Assert(portraits.All(button => portraitBounds[((SeatViewModel)button.DataContext).Seat] == Bounds(button)) &&
                        baseline.SequenceEqual(new[] { Bounds(battlefield), Bounds(dock), Bounds((FrameworkElement)window.FindName("PrimaryActionButtons")) }),
                        "Selecting Iron Chain, showing Recast, choosing targets or cancelling must never move or shrink portraits or the dock.");
                    Program.Assert(Program.Engine(model).Revision == revision, "A target preview or resize must not submit a command.");
                }
                model.SelectCardCommand.Execute(card);
                Program.Render(root, size.Item1, size.Item2, Path.Combine(output, $"iron-chain-stable-selected-{size.Item1}.png"));
                AssertStable();
                var published = (GameSnapshot)typeof(MainViewModel).GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model)!;
                var hiddenCombination = published.PendingDecision!.Choices.First(choice => choice.Cards.SequenceEqual([card.Id]) && choice.Targets.Count == 2);
                var selectedState = SnapshotJson.Serialize(Program.Engine(model).CreateSnapshot(0));
                var commandCount = Program.Engine(model).AcceptedCommands.Count;
                var movementCount = Program.Engine(model).CardMovements.Count;
                Program.Assert(model.IsMultiTargetCardSelected && model.TargetCombinationChoices.Count == 0,
                    "Iron Chain must expose its portrait draft while its pending combinations remain unpublished.");
                model.SelectTargetCombinationChoiceCommand.Execute(hiddenCombination);
                Program.Assert(card.IsSelected && model.Seats.All(seat => !seat.IsSelectedTarget) &&
                    Program.Engine(model).AcceptedCommands.Count == commandCount && Program.Engine(model).CardMovements.Count == movementCount &&
                    SnapshotJson.Serialize(Program.Engine(model).CreateSnapshot(0)) == selectedState,
                    "An unpublished current-prompt combination must not bypass Iron Chain's visible portrait draft or pay its card.");
                AssertStable();
                var recast = (Button)window.FindName("RecastButton");
                var hint = (FrameworkElement)window.FindName("ActionDockHint");
                Program.Assert(ReferenceEquals(((BattleFeedbackLayer)window.FindName("BattleFeedback")).SupplementalActions,
                    window.FindName("SupplementalActionBar")), "Effects must preserve readability of the floating command controls too.");
                Console.WriteLine($"Iron Chain {size.Item1}: recast={Bounds(recast)}, hint={Bounds(hint)}, battlefield={Bounds(battlefield)}, enabled={recast.IsEnabled}.");
                Program.Assert(recast.Visibility == Visibility.Visible && recast.IsEnabled &&
                    Bounds(recast).Bottom <= Bounds(hint).Top && Bounds(recast).Top >= Bounds(battlefield).Top,
                    "The genuine Recast action must remain accessible above the fixed dock without covering its hint.");
                foreach (var seat in new[] { 1, 2 })
                {
                    model.SelectTargetCommand.Execute(model.Seats.Single(item => item.Seat == seat));
                    root.UpdateLayout();
                    AssertStable();
                }
                model.CancelActionCommand.Execute(null);
                root.UpdateLayout();
                AssertStable();
                Program.Assert(!model.HasSelection && recast.Visibility == Visibility.Collapsed, "Cancel must restore the idle surface.");
            }
            model.SelectCardCommand.Execute(card);
            model.RecastSelectedCommand.Execute(null);
            Program.Assert(Program.Engine(model).Revision == revision + 1 &&
                Program.Engine(model).AcceptedCommands.Last() is RecastCardCommand { CardId: var id } && id == card.Id,
                "The floating Recast button must still submit the exact original card once.");
        }
        finally { window.Content = null; window.Close(); }
    }

    public static void TargetCountGuideAndSubmission()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        foreach (var (generalId, maximum) in new[] { ("boundary:pang-tong", 3), ("fixture:chain-owner", 2) })
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = Fixture.ModeId, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false
            }, registry);
            Accept(game, new StartGameCommand());
            Accept(game, new SelectGeneralCommand(0, generalId, game.Revision, game.PendingDecision!.PromptId));
            for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            Program.Assert(game.PendingDecision?.Kind == DecisionKind.PlayCard, "The fixed Iron Chain UI fixture reaches Play.");
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true,
                contentRegistry: registry) { IsMotionEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError, model.SaveStatus);
            var card = model.Hand.First(item => item.Kind == CardKind.IronChain && item.IsPlayable);
            model.SelectCardCommand.Execute(card);
            model.OpenContextGuideCommand.Execute(null);
            Program.Assert(model.CurrentGuideBody.Contains($"1–{maximum}") &&
                           model.CurrentGuideSteps.Any(step => step.Text.Contains($"1–{maximum}")),
                "The guide must describe the actual current Iron Chain target count.");
            model.IsHelpOpen = false;
            var selected = model.Seats.Take(maximum).Select(seat => seat.Seat).ToArray();
            foreach (var seat in selected) model.SelectTargetCommand.Execute(model.Seats.Single(item => item.Seat == seat));
            Program.Assert(model.CanConfirmSelected && model.Seats.Count(seat => seat.IsSelectedTarget) == maximum,
                "All current Iron Chain targets remain selectable together.");
            model.ConfirmSelectedCommand.Execute(null);
            for (var step = 0; step < 40 && Program.Engine(model).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                model.StepAiCommand.Execute(null);
            Program.Assert(selected.All(seat => Program.Engine(model).CreateSnapshot(0).Players[seat].IsChained) &&
                           Program.Engine(model).CardMovements.Count(move => move.CardId == card.Id &&
                               move.Reason == CardMoveReasons.IronChainUse) == 1,
                "The UI submits the exact target set and pays the physical card once.");
        }
        CardConversionTargetUiChecks.GuhuoKeepsPublishedWholeTargetCombinations();
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected an accepted Iron Chain UI command.");
    }

    private sealed class Fixture(int playerCount = 4) : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-chain-ui";
        public PackageManifest Manifest { get; } = new("fixture-chain-ui", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in new[] { "owner", "one", "two" })
                builder.AddGeneral(new($"fixture:chain-{id}", id, "supporter", "standard:none", "wei", 4));
            var extra = Enumerable.Range(4, playerCount - 4).Select(seat => $"fixture:chain-{seat}").ToArray();
            foreach (var id in extra) builder.AddGeneral(new(id, id, "supporter", "standard:none", "wei", 4));
            builder.AddDeck(new("fixture:chain-ui-deck", "铁索目标界面", 4, 0,
                [new ContentDeckCardCount("standard:iron_chain", Math.Max(32, playerCount * 8))]));
            builder.AddMode(new(ModeId, "铁索目标界面", playerCount, playerCount,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = playerCount - 2 },
                "fixture:chain-ui-deck", GeneralCandidateCount: playerCount,
                GeneralPoolIds: ["boundary:pang-tong", "fixture:chain-owner", "fixture:chain-one", "fixture:chain-two", .. extra]));
        }
    }
}
