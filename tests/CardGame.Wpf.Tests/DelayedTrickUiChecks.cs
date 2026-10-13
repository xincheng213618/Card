using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class DelayedTrickUiChecks
{
    public static void PortraitPressAndSelfTarget(string output)
    {
        foreach (var kind in new[] { CardKind.FireAttack, CardKind.SupplyShortage, CardKind.Lightning })
        {
            var (game, registry) = DelayedTrickScenario.Create();
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry)
                { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError, model.SaveStatus);
            var window = new MainWindow(model) { ShowActivated = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, Width = 1440, Height = 860 };
            try
            {
                window.Show();
                var root = (FrameworkElement)window.Content;
                game = Program.Engine(model);
                var action = game.GetHumanLegalActions().First(a => a.CardId is { } id &&
                    (a.PlayedCardKind ?? game.CreateSnapshot(0).Players[0].Hand.Single(c => c.Id == id).Kind) == kind &&
                    (kind == CardKind.SupplyShortage ? a.ConversionSource?.SkillId == "boundary:duanliang-current" : a.ConversionSource is null));
                var card = model.Hand.Single(c => c.Id == action.CardId);
                var before = game.Revision;
                var commands = game.AcceptedCommands.Count;
                model.SelectCardCommand.Execute(card);
                if (action.ConversionSource is { } source)
                {
                    var choice = model.EquipmentPlayChoices.Single(c => c.Cards.SequenceEqual([card.Id]) &&
                        c.Parameters.GetValueOrDefault("conversion-skill-id") == source.SkillId &&
                        c.Parameters.GetValueOrDefault("conversion-binding-id") == source.BindingId);
                    model.SelectEquipmentPlayChoiceCommand.Execute(choice);
                    Program.Assert(!model.HasTargetCombinationChoices,
                        "Duanliang's single target must use portraits without a separate AI-seat choice.");
                }
                Program.Render(root, 1440, 860, System.IO.Path.Combine(output, $"target-{kind}-before.png"));
                var target = model.Seats.Single(s => s.Seat == action.TargetSeats.Single());
                if (kind == CardKind.Lightning)
                {
                    Program.Assert(target.IsHuman && target.IsSelectedTarget && model.CanConfirmSelected,
                        "Selecting Lightning must prepare its own seat without another target click.");
                    VerifyLightningAim(window, root, model, game, card, target, before, commands);
                }
                else
                {
                    var expectedTargets = game.GetHumanLegalActions().Where(a => a.CardId == card.Id && a.ConversionSource == action.ConversionSource)
                        .SelectMany(a => a.TargetSeats).ToHashSet();
                    Program.Assert(model.Seats.Where(s => s.IsLegalTarget).Select(s => s.Seat).ToHashSet().SetEquals(expectedTargets),
                        "The current physical/converted card must highlight its exact published targets.");
                    foreach (var seat in model.Seats.Where(s => s.IsLegalTarget).ToArray())
                    {
                        PressPortrait(root, model, seat);
                        Program.Assert(seat.IsSelectedTarget && model.CanConfirmSelected && game.Revision == before && game.AcceptedCommands.Count == commands,
                            "A real portrait press must prepare the exact target without submitting or losing the draft.");
                        PressPortrait(root, model, seat);
                        Program.Assert(!seat.IsSelectedTarget && !model.CanConfirmSelected,
                            "Pressing a drafted target again must cancel it exactly once.");
                    }
                    PressPortrait(root, model, target);
                }
                Program.Render(root, 1440, 860, System.IO.Path.Combine(output, $"target-{kind}-selected.png"));
                var confirm = (Button)window.FindName("PlayCardButton");
                Program.Assert(confirm.IsEnabled && model.CanConfirmSelected, "The main confirmation button must accept this exact current draft.");
                typeof(ButtonBase).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(confirm, null);
                var accepted = game.AcceptedCommands.Skip(commands).OfType<PlayCardCommand>().Single();
                Program.Assert(accepted.CardId == card.Id && accepted.TargetSeats.SequenceEqual(action.TargetSeats) &&
                    accepted.ConversionSource == action.ConversionSource,
                    "The portrait draft must submit the exact original material, selected targets and conversion source.");
                if (kind is CardKind.Lightning or CardKind.SupplyShortage)
                    Program.Assert(game.CreateSnapshot(0).Players[target.Seat].Judgment.Any(c => c.Id == card.Id && c.Kind == kind) &&
                        !game.Events.Any(e => e.Payload is NullificationRequestedEvent),
                        "The confirmed delayed card must enter its target's Judgment zone directly.");
                Console.WriteLine($"{kind}: actual portrait input and confirmation passed; target={target.Seat}; conversion={accepted.ConversionSource?.SkillId ?? "native"}.");
            }
            finally { window.Close(); }
        }
    }

    private static void VerifyLightningAim(MainWindow window, FrameworkElement root, MainViewModel model,
        GameEngine game, CardViewModel card, SeatViewModel self, long revision, int commands)
    {
        const BindingFlags access = BindingFlags.NonPublic | BindingFlags.Instance;
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        root.UpdateLayout();
        var hand = Program.Find<HandPanel>(root).Single();
        var source = Program.Find<Button>(root).Single(button => ReferenceEquals(button.Command, model.SelectCardCommand) &&
            ReferenceEquals(button.CommandParameter, card));
        var surface = Program.Find<CardLiftSurface>(source).Single();
        var pressPoint = surface.TranslatePoint(new Point(surface.ActualWidth / 2, surface.ActualHeight / 2), hand);
        var portrait = Program.Find<Button>(root).Single(button => ReferenceEquals(button.Command, model.SelectTargetCommand) &&
            ReferenceEquals(button.CommandParameter, self));
        var guide = (CardTargetingLayer)window.FindName("CardTargeting");
        var dropPoint = portrait.TranslatePoint(new Point(portrait.ActualWidth / 2, portrait.ActualHeight / 2), guide);
        var views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
            .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        var moves = game.CardMovements.Count;
        var events = game.Events.Count;
        Program.Assert(typeof(MainWindow).GetMethod("ArmCardAim", access)!.Invoke(window, [hand, pressPoint]) is true &&
            typeof(HandPanel).GetMethod("PressCardAt", access)!.Invoke(hand, [pressPoint]) is true &&
            !card.IsSelected && !self.IsSelectedTarget,
            "A real selected Lightning press must keep its existing reversible toggle before aiming.");
        Program.Assert(typeof(MainWindow).GetMethod("UpdateCardAim", access)!.Invoke(window, [dropPoint]) is true &&
            ReferenceEquals(Mouse.Captured, window) && ReferenceEquals(guide.PointerAimAnchor, portrait) &&
            guide.IsPointerAimValid && card.IsSelected && self.IsSelectedTarget && !model.IsMotionEnabled,
            "Motion-off Lightning aim must restore its exact self draft and hit the actual HandDock portrait.");
        typeof(MainWindow).GetMethod("CancelCardAim", access)!.Invoke(window, null);
        Program.Assert(!ReferenceEquals(Mouse.Captured, window) && guide.PointerAimPoint is null &&
            !CardTargetingLayer.GetIsAimTarget(portrait) && card.IsSelected && self.IsSelectedTarget && model.CanConfirmSelected &&
            game.Revision == revision && game.AcceptedCommands.Count == commands && game.CardMovements.Count == moves &&
            game.Events.Count == events && Enumerable.Range(0, views.Length).All(seat =>
                views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
            "Cancelling self-target aim must release capture and retain the draft without commands, card movement or changed safe views.");
        Console.WriteLine("Lightning aim: actual HandDock portrait, motion off, capture and restored self draft; cancelled without a command.");
    }

    private static void PressPortrait(FrameworkElement root, MainViewModel model, SeatViewModel seat)
    {
        root.UpdateLayout();
        var button = Program.Find<Button>(root).Single(b => ReferenceEquals(b.Command, model.SelectTargetCommand) && ReferenceEquals(b.CommandParameter, seat));
        var point = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), root);
        var hit = root.InputHitTest(point) as DependencyObject;
        while (hit is not null && hit is not Button) hit = VisualTreeHelper.GetParent(hit);
        Program.Assert(ReferenceEquals(hit, button), "The enabled portrait must receive input at its visual center.");
        var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
        button.RaiseEvent(press);
        Program.Assert(press.Handled, "The table must consume the portrait's press after selecting its exact target.");
        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        root.UpdateLayout();
    }
}
