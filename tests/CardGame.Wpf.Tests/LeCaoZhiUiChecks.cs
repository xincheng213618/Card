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

internal static class LeCaoZhiUiChecks
{
    public static void PrivateFuAliasDraftAndExactNativePayment(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.Mode, UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Program.Assert(game.PendingDecision?.Kind == DecisionKind.PlayCard, "The native Fu fixture must reach human play.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, 31, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            var legal = game.GetHumanLegalActions();
            var alias = legal.First(action => action.ConversionSource?.SkillId == "le:fule" &&
                action.PlayedCardKind == CardKind.IronChain && action.TargetSeats.SequenceEqual([1]));
            var cardId = alias.CardId!.Value;
            var printed = model.Hand.Single(card => card.Id == cardId);
            Program.Assert(printed.Kind == CardKind.Crossbow &&
                legal.Any(action => action.CardId == cardId && action.ConversionSource is null &&
                    action.Kind == LegalActionKind.Equip),
                "An actual Fu must retain its printed native equipment use beside its one legal alias.");
            var own = game.CreateSnapshot(0);
            Program.Assert(own.Players[0].Hand.Single(card => card.Id == cardId).SkillMarkLabel is { } label &&
                label.Contains("赋", StringComparison.Ordinal) && label.Contains("铁索连环", StringComparison.Ordinal) &&
                Enumerable.Range(1, 3).All(seat => game.CreateSnapshot(seat).Players[0].Hand.Count == 0),
                "Only the authorized physical Hand may expose its Fu mark and alternate name.");
            var faces = model.Hand.ToDictionary(card => card.Id, card =>
                (card.Kind, card.Name, card.Artwork, card.VerticalName, card.SuitGlyph, card.Rank));
            var revision = game.Revision;
            var commands = game.AcceptedCommands.Count;
            Layout(root);
            Click(Bound(root, model.ActivateHumanSkillCommand,
                model.HumanSkillCards.Single(skill => skill.ContentId == "le:fule")));
            Program.Assert(model.HasSelection && model.Hand.All(card => !card.IsSelected) && !model.CanConfirmSelected &&
                model.EquipmentPlayChoices.Count == 0 && model.SelectedCardText.Contains("铁索连环", StringComparison.Ordinal),
                "A single currently legal Fu alias must start a material draft without an empty output chooser.");
            Program.Assert(model.Hand.Where(card => card.IsPlayable).Select(card => card.Id).ToHashSet().SetEquals(
                legal.Where(action => action.ConversionSource?.SkillId == "le:fule" && action.PlayedCardKind == CardKind.IronChain)
                    .Select(action => action.CardId!.Value)),
                "The alias draft may enable only the real entities that possess that exact alias.");
            Click(Bound(root, model.SelectCardCommand, printed));
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 1));
            Layout(root);
            Program.Assert(model.CanConfirmSelected && model.IsMultiTargetCardSelected &&
                game.Revision == revision && game.AcceptedCommands.Count == commands &&
                model.Hand.All(card => (card.Kind, card.Name, card.Artwork, card.VerticalName, card.SuitGlyph, card.Rank) == faces[card.Id]),
                "Choosing the Fu name, material and native targets must preserve the physical face and defer payment until confirmation.");
            Program.Render(root, 1120, 740, Path.Combine(output, "le-cao-zhi-fu-alias-selection.png"));
            var reference = GameReplay.Restore(game.CreateCheckpoint(), registry);
            var expected = new PlayCardCommand(0, cardId, alias.TargetSeats, revision, game.PendingDecision!.PromptId,
                alias.PlayedCardKind, alias.TargetCardId)
                { ConversionSource = alias.ConversionSource, AdditionalConversionSources = alias.AdditionalConversionSources };
            Accept(reference, expected);
            model.ConfirmSelectedCommand.Execute(null);
            Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Count == commands + 1 &&
                CommandJson.Serialize([game.AcceptedCommands.Last()]) == CommandJson.Serialize([expected]) &&
                game.CardMovements.SequenceEqual(reference.CardMovements) && !model.HasSelection &&
                Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(reference.CreateSnapshot(seat))),
                "Confirmation must pay the exact Fu entity once with its selected name and native targets, matching checkpoint replay.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Mode = "identity:le-cao-zhi-ui";
        internal const string Owner = "fixture:le-cao-zhi-ui-owner";
        public PackageManifest Manifest { get; } = new("fixture-le-cao-zhi-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "le:cao-zhi");
            builder.AddGeneral(new(Owner, "赋乐选名", "supporter", "le:fule", "wei", 6,
                AdditionalSkillIds: ["le:wenlan"]));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:le-cao-zhi-ui-peer-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "合法目标", "supporter", "standard:none", "qun", 6));
            builder.AddDeck(new("fixture:le-cao-zhi-ui-deck", "固定模式牌名", 8, 0, [])
            {
                PhysicalCards = [.. Enumerable.Range(0, 80).Select(index =>
                    new ContentDeckPhysicalCard("standard:crossbow", (Suit)(index % 4), index % 13 + 1)),
                    new ContentDeckPhysicalCard("standard:iron_chain", Suit.Club, 6)]
            });
            builder.AddMode(new(Mode, "赋乐选名", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:le-cao-zhi-ui-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Native command rejected.");
    }
    private static Button Bound(FrameworkElement root, ICommand command, object parameter) => Program.Find<Button>(root)
        .Single(button => ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, parameter));
    private static void Click(Button button)
    {
        Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible, "The actual bound control must be actionable.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }
}
