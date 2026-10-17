using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class WumiaoFloodUiChecks
{
    public static void OrderedPortraitTargetsUndoAimAndCommit(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var prepared = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Fixture.Mode, UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 3 }, registry);
        Accept(prepared, new StartGameCommand());
        Accept(prepared, new SelectGeneralCommand(0, Fixture.Owner, prepared.Revision,
            prepared.CreateSnapshot(0).PendingDecision!.PromptId));
        for (var step = 0; step < 40 && prepared.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(prepared, new AdvanceOneStepCommand(prepared.Revision));
        Program.Assert(prepared.CreateSnapshot(0).PendingDecision?.Kind == DecisionKind.PlayCard,
            "The fixed native Flood fixture must reach real human play.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow,
            false, prepared.CreateCheckpoint()));
        using var model = new MainViewModel(false, prepared.Seed, true, store, contentRegistry: registry,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError, model.SaveStatus);
            var game = Program.Engine(model);
            var action = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.WumiaoFlood &&
                item.TargetSeats.SequenceEqual([2, 1]));
            var card = model.Hand.Single(item => item.Id == action.CardId);
            var revision = game.Revision;
            var commands = game.AcceptedCommands.Count;
            var moves = game.CardMovements.Count;
            var views = Views(game);
            model.SelectCardCommand.Execute(card);
            Layout(root);
            Program.Assert(model.IsMultiTargetCardSelected && !model.CanConfirmSelected && !model.HasTargetCombinationChoices,
                "Flood selects its ordered recipients through reversible portraits, before any use.");
            Click(Button(root, model.SelectTargetCommand, model.Seats.Single(seat => seat.Seat == 2)));
            Click(Button(root, model.SelectTargetCommand, model.Seats.Single(seat => seat.Seat == 1)));
            Program.Assert(Selected(model).SequenceEqual([2, 1]) && model.CanConfirmSelected,
                "The different first/second effects must retain reverse-seat click order.");
            Program.Assert(model.ActionHint.Contains("03号位（雷伤后弃牌）", StringComparison.Ordinal) &&
                model.ActionHint.Contains("02号位（雷伤后摸牌）", StringComparison.Ordinal) &&
                model.ActionHint.IndexOf("03号位", StringComparison.Ordinal) < model.ActionHint.IndexOf("02号位", StringComparison.Ordinal),
                "The visible hint must identify each selected seat and its distinct ordered effect.");
            var aim = typeof(MainViewModel).GetMethod("CapturePlayCardAimDraft", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, [card]);
            Program.Assert(aim is not null, "The native card aim must capture its ordered draft.");
            model.ClearSelectionCommand.Execute(null);
            Program.Assert((bool)typeof(MainViewModel).GetMethod("RestorePlayCardAimDraft", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, [aim])! && Selected(model).SequenceEqual([2, 1]),
                "Restoring the same-revision pointer aim must preserve the full ordered draft.");
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 2));
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 2));
            Program.Assert(Selected(model).SequenceEqual([1, 2]), "Removing and selecting again must append that target.");
            model.ClearSelectionCommand.Execute(null);
            model.SelectCardCommand.Execute(model.Hand.Single(item => item.Id == action.CardId));
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 2));
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 1));
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 3));
            Program.Assert(Selected(model).SequenceEqual([2, 1]), "A third portrait cannot exceed the native two-target limit.");
            model.OpenContextGuideCommand.Execute(null);
            Program.Assert(model.CurrentGuideSteps.Any(step => step.Text.Contains("点选顺序", StringComparison.Ordinal)),
                "The actual contextual help must explain why target order matters.");
            model.ToggleHelpCommand.Execute(null);
            Program.Assert(game.Revision == revision && game.AcceptedCommands.Count == commands &&
                game.CardMovements.Count == moves && Views(game).SequenceEqual(views),
                "Selection, cancellation, aim restore and help cannot advance or reveal the match.");
            Layout(root);
            Program.Render(root, 1120, 740, Path.Combine(output, "wumiao-flood-ordered-targets.png"));
            var expected = new PlayCardCommand(0, action.CardId!.Value, [2, 1], revision,
                game.CreateSnapshot(0).PendingDecision!.PromptId);
            var reference = GameReplay.Restore(game.CreateCheckpoint(), registry);
            Accept(reference, expected);
            Click((Button)window.FindName("PlayCardButton"));
            Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Count == commands + 1 &&
                CommandJson.Serialize([game.AcceptedCommands.Last()]) == CommandJson.Serialize([expected]) &&
                game.CardMovements.Skip(moves).Count(move => move.CardId == action.CardId &&
                    move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 &&
                Views(game).SequenceEqual(Views(reference)),
                "Confirming must submit the exact ordered native command and pay its original entity once.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static IReadOnlyList<int> Selected(MainViewModel model) => (IReadOnlyList<int>)typeof(MainViewModel)
        .GetMethod("SelectedPlayTargets", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null)!;
    private static string[] Views(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
    private static Button Button(FrameworkElement root, ICommand command, object value) => Program.Find<Button>(root)
        .Single(button => ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, value));
    private static void Click(Button button)
    { Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible, "The real bound button must be enabled.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null); }
    private static void Layout(FrameworkElement root)
    { root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout(); }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Owner = "fixture:wumiao-flood-ui-owner";
        internal const string Mode = "identity:classic-wumiao-flood-ui";
        public PackageManifest Manifest { get; } = new("fixture-wumiao-flood-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new(Owner, "水淹次序", "supporter", "standard:none", "shu", 6));
            var peers = Enumerable.Range(1, 3).Select(index => "fixture:wumiao-flood-ui-peer-" + index).ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "次序目标", "supporter", "standard:none", "wei", 6));
            builder.AddDeck(new("fixture:wumiao-flood-ui-deck", "原生水淹实体", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard("wumiao:flood", Suit.Club, 2)).ToArray() });
            builder.AddMode(new(Mode, "水淹目标顺序", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:wumiao-flood-ui-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
