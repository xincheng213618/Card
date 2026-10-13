using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ActiveSkillSuitUiChecks
{
    public static void DistinctSuitsRequireLegalDraft()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var original = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(new StartGameCommand());
        Accept(new SelectGeneralCommand(0, Fixture.Owner, original.Revision, original.PendingDecision!.PromptId));
        for (var step = 0; step < 40 && original.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(new AdvanceOneStepCommand(original.Revision));
        Program.Assert(original.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "The fixed suit fixture must reach a native human Play prompt.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, original.CreateCheckpoint()));
        using var model = new MainViewModel(false, 31, true, store, contentRegistry: registry)
        { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var game = Program.Engine(model);
        var action = model.HumanActiveSkillActions.Single(a => a.ProgramSkillId == "ol:yeyan" && a.ProgramActivationId == "two");
        var human = game.CreateSnapshot(0).Players[0];
        var suits = new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond };
        Program.Assert(!model.HasSaveError && action.SelectedCardsDistinctSuits && action.MinCardCount == 4 &&
            action.MaxCardCount == 4 && suits.All(suit => human.Hand.Any(c => c.Suit == suit)) && human.Hp > 3,
            "The real OL Yeyan action must publish its four-distinct-suit cost and the fixed hand must contain it.");
        var valid = suits.Select(suit => human.Hand.First(c => c.Suit == suit).Id).Order().ToArray();
        var repeated = human.Hand.GroupBy(c => c.Suit).First(group => group.Count() > 1);
        var omittedSuit = suits.First(suit => suit != repeated.Key);
        var invalid = valid.Where(id => human.Hand.Single(c => c.Id == id).Suit != omittedSuit)
            .Append(repeated.Skip(1).First().Id).Order().ToArray();
        var target = action.SelectableTargetSeats.First(seat => seat != 0);
        var before = Views(game);
        var commands = game.AcceptedCommands.Count;
        var movements = game.CardMovements.Count;
        var events = game.Events.Count;
        var revision = game.Revision;
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        var native = GameReplay.Restore(game.CreateCheckpoint(), registry);
        var nativeResult = native.Submit(new UseProgramSkillCommand(0, "ol:yeyan", "two", valid, [target], native.Revision,
            native.CreateSnapshot(0).PendingDecision!.PromptId));
        Program.Assert(nativeResult.Accepted && native.CreateSnapshot(0).Players[0].Hp == human.Hp - 3 &&
            native.CreateSnapshot(0).Players[target].Hp == game.CreateSnapshot(0).Players[target].Hp - 2 &&
            Views(game).SequenceEqual(before), "The native reference must accept the exact four-suit payment without changing the live UI fixture.");
        var rejectedNative = GameReplay.Restore(game.CreateCheckpoint(), registry);
        var rejection = rejectedNative.Submit(new UseProgramSkillCommand(0, "ol:yeyan", "two", invalid, [target],
            rejectedNative.Revision, rejectedNative.CreateSnapshot(0).PendingDecision!.PromptId));
        Program.Assert(!rejection.Accepted && rejection.Error?.Code == CommandErrorCode.InvalidCard &&
            rejectedNative.Revision == revision && rejectedNative.AcceptedCommands.Count == commands &&
            rejectedNative.CardMovements.Count == movements && rejectedNative.Events.Count == events &&
            Views(rejectedNative).SequenceEqual(before), "The native boundary must reject the repeated-suit set without payment or committed state changes.");
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            Select(invalid);
            Layout();
            var confirm = (Button)window.FindName("PlayCardButton");
            Console.WriteLine($"OL Yeyan draft: ids={string.Join(',', invalid)}, suits={string.Join(',', invalid.Select(id => human.Hand.Single(c => c.Id == id).Suit))}, publishedDistinct={action.SelectedCardsDistinctSuits}, canConfirm={model.CanConfirmActiveSkill}, buttonEnabled={confirm.IsEnabled}.");
            Program.Assert(model.IsActiveSkillSelectionPending && model.Hand.Count(c => c.IsSelected) == 4 &&
                model.Seats.Single(s => s.Seat == target).IsSelectedTarget && !model.CanConfirmActiveSkill &&
                !model.CanConfirmSelected && !confirm.IsEnabled && !confirm.Command!.CanExecute(confirm.CommandParameter),
                "An exact-count but repeated-suit native Yeyan draft must keep the real confirmation button disabled.");
            Program.Assert(!Shortcut(Key.Enter) && Views(game).SequenceEqual(before) && game.Revision == revision &&
                game.AcceptedCommands.Count == commands && game.CardMovements.Count == movements && game.Events.Count == events,
                "Invalid-suit Enter must not submit, pay, change player views or discard the draft.");
            Program.Assert(model.IsActiveSkillSelectionPending && model.HasSelection &&
                model.Hand.Where(c => c.IsSelected).Select(c => c.Id).Order().SequenceEqual(invalid) &&
                model.Seats.Where(s => s.IsSelectedTarget).Select(s => s.Seat).SequenceEqual([target]),
                "Invalid-suit Enter must preserve the exact selected physical materials and target for correction.");
            Program.Assert(model.ActionHint.Contains("花色各不相同"), "The draft must explain the actual distinct-suit requirement.");
            model.OpenContextGuideCommand.Execute(null);
            Program.Assert(model.CurrentGuideBody.Contains("花色各不相同"), "The current guide must retain the suit restriction.");
            model.ToggleHelpCommand.Execute(null);
            model.ClearSelectionCommand.Execute(null);
            Program.Assert(!model.HasSelection && Views(game).SequenceEqual(before), "Cancelling an invalid suit draft must not change the game.");
            Select(valid);
            Layout();
            Program.Assert(model.CanConfirmActiveSkill && model.CanConfirmSelected && confirm.IsEnabled &&
                confirm.Command!.CanExecute(confirm.CommandParameter) && Views(game).SequenceEqual(before),
                "A complete four-distinct-suit draft must enable the same real confirmation button without paying early.");
            typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(confirm, null);
            Program.Assert(game.AcceptedCommands.Count == commands + 1 && game.AcceptedCommands.Last() is UseProgramSkillCommand paid &&
                paid.SkillId == "ol:yeyan" && paid.ActivationId == "two" && paid.CardIds.SequenceEqual(valid) &&
                paid.TargetSeats.SequenceEqual([target]) && paid.ExpectedRevision == revision && paid.PromptId == prompt.PromptId &&
                valid.All(id => game.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) &&
                    m.To == CardLocation.DiscardPile) == 1) && !model.HasSelection &&
                Views(game).SequenceEqual(Views(native)), "Confirmation must submit and pay the exact native Yeyan action once, matching the native reference.");
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Program.Assert(Views(restored).SequenceEqual(Views(game)), "Cold restore must retain every player-safe view of the actual four-suit payment.");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }

        void Accept(GameCommand command)
        {
            var result = original.Submit(command);
            Program.Assert(result.Accepted, result.Error?.Message ?? "The suit fixture native command was rejected.");
        }
        void Select(int[] ids)
        {
            model.SelectActiveSkillCommand.Execute(action);
            foreach (var id in ids) model.SelectCardCommand.Execute(model.Hand.Single(c => c.Id == id));
            model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == target));
        }
        void Layout()
        {
            root.Measure(new Size(1120, 740));
            root.Arrange(new Rect(0, 0, 1120, 740));
            root.UpdateLayout();
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            root.UpdateLayout();
        }
        bool Shortcut(Key key) => (bool)typeof(MainWindow).GetMethod("HandleShortcut", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, [key, ModifierKeys.None])!;
        static string[] Views(GameEngine engine) => Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(engine.CreateSnapshot(seat))).ToArray();
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Mode = "identity:classic-ui-suits";
        internal const string Owner = "fixture:ui-suits-owner";
        public PackageManifest Manifest { get; } = new("fixture-ui-suits", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new(Owner, "四花色夹具", "supporter", "ol:yeyan", "wei", 6));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:ui-suits-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "其他角色", "supporter", "standard:none", "shu", 6));
            builder.AddDeck(new("fixture:ui-suits-deck", "固定花色", 12, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard("standard:peach",
                new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i % 4], i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "花色费用", 4, 4, new Dictionary<string, int>
            { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:ui-suits-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
