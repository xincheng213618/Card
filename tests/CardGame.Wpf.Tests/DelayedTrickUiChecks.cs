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
                var judgmentMarkers = (ItemsControl)window.FindName("HumanJudgmentStatuses");
                Program.Assert(judgmentMarkers.Items.Count == 0 && judgmentMarkers.Visibility == Visibility.Collapsed,
                    "An empty local judgment zone must not show a hand-dock status marker.");
                game = Program.Engine(model);
                var action = game.GetHumanLegalActions().First(a => a.CardId is { } id &&
                    (a.PlayedCardKind ?? game.CreateSnapshot(0).Players[0].Hand.Single(c => c.Id == id).Kind) == kind &&
                    (kind == CardKind.SupplyShortage ? a.ConversionSource?.SkillId == "boundary:duanliang-current" : a.ConversionSource is null));
                var card = model.Hand.Single(c => c.Id == action.CardId);
                var before = game.Revision;
                var commands = game.AcceptedCommands.Count;
                if (action.ConversionSource is { } source)
                {
                    model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == source.SkillId));
                    model.SelectCardCommand.Execute(card);
                    Program.Assert(!model.HasTargetCombinationChoices,
                        "Duanliang's single target must use portraits without a separate AI-seat choice.");
                }
                else model.SelectCardCommand.Execute(card);
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
                {
                    Program.Assert(game.CreateSnapshot(0).Players[target.Seat].Judgment.Any(c => c.Id == card.Id && c.Kind == kind) &&
                        !game.Events.Any(e => e.Payload is NullificationRequestedEvent),
                        "The confirmed delayed card must enter its target's Judgment zone directly.");
                    root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                    root.UpdateLayout();
                    Program.Assert(target.JudgmentStatuses.Single() is { } status && status.CardId == card.Id && status.Kind == kind &&
                        status.Glyph == (kind == CardKind.Lightning ? "闪" : "兵") && status.Tooltip.Contains(status.Name),
                        "The judgment marker must preserve the physical entity and effective delayed kind, including converted materials.");
                    if (kind == CardKind.Lightning)
                        VerifyHandDockMarker(window, root, model, store, judgmentMarkers, card.Id, output);
                    else
                    {
                        Program.Assert(judgmentMarkers.Items.Count == 0 && judgmentMarkers.Visibility == Visibility.Collapsed && target.ShowPortraitJudgment,
                            "A delayed trick on another player must stay on that player's portrait instead of the local hand dock.");
                        var portrait = Program.Find<Button>(root).Single(button => ReferenceEquals(button.Command, model.SelectTargetCommand) &&
                            ReferenceEquals(button.CommandParameter, target));
                        var markers = Program.Find<ItemsControl>(portrait).Single(control => control.Name == "SeatJudgmentStatuses");
                        var marker = Program.Find<Grid>(markers).Single(grid => grid.DataContext is JudgmentStatusViewModel);
                        var bounds = marker.TransformToAncestor(portrait).TransformBounds(new Rect(marker.RenderSize));
                        Program.Assert(markers.IsVisible && Program.Find<TextBlock>(marker).Single().Text == "兵" &&
                            bounds.Right >= portrait.ActualWidth - 6 && bounds.Bottom >= portrait.ActualHeight,
                            "A converted delayed trick must use the shared diamond at the affected portrait's lower-right corner.");
                        Program.Render(root, 1440, 860, System.IO.Path.Combine(output, "delayed-other-portrait.png"));
                    }
                }
                Console.WriteLine($"{kind}: actual portrait input and confirmation passed; target={target.Seat}; conversion={accepted.ConversionSource?.SkillId ?? "native"}.");
            }
            finally { window.Close(); }
        }
    }

    private static void VerifyHandDockMarker(MainWindow window, FrameworkElement root, MainViewModel model,
        MemorySaveStore store, ItemsControl markers, int cardId, string output)
    {
        var hand = (FrameworkElement)window.FindName("HandViewport");
        var dock = (FrameworkElement)window.FindName("HandDock");
        var marker = Program.Find<Grid>(markers).Single(grid => grid.DataContext is JudgmentStatusViewModel);
        var point = marker.TranslatePoint(new Point(0, 0), root);
        var handPoint = hand.TranslatePoint(new Point(0, 0), root);
        var dockPoint = dock.TranslatePoint(new Point(0, 0), root);
        var badge = (JudgmentStatusViewModel)marker.DataContext;
        Program.Assert(markers.IsVisible && markers.Items.Count == 1 && badge.CardId == cardId &&
            Math.Abs(point.X - dockPoint.X - 8) < 1 && point.X + marker.ActualWidth < handPoint.X &&
            point.Y < handPoint.Y && point.Y + marker.ActualHeight <= handPoint.Y &&
            !model.HumanPlayer!.ShowPortraitJudgment && !Program.Find<TextBlock>(marker).Single().IsVisible &&
            Program.Find<System.Windows.Shapes.Path>(marker).Single(path => path.Name == "LightningGlyph").IsVisible,
            "Local delayed tricks must render as a diamond at the left edge above the hand dock without covering a card or duplicating the portrait label.");
        var center = marker.TranslatePoint(new Point(marker.ActualWidth / 2, marker.ActualHeight / 2), root);
        var hit = root.InputHitTest(center) as DependencyObject;
        while (hit is not null && !ReferenceEquals(hit, marker)) hit = VisualTreeHelper.GetParent(hit);
        Program.Assert(ReferenceEquals(hit, marker) && (string)marker.ToolTip == badge.Tooltip,
            "The visible status marker must receive hover input for its full card and judgment tooltip.");
        Program.Render(root, 1440, 860, System.IO.Path.Combine(output, "delayed-local-hand-dock.png"));

        var emptySave = store.Read(GameSaveSlot.Manual);
        model.SaveGameCommand.Execute(null);
        model.LoadManualGameCommand.Execute(null);
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        root.UpdateLayout();
        Program.Assert(markers.IsVisible && markers.Items.Count == 1 &&
            ((JudgmentStatusViewModel)markers.Items[0]).CardId == cardId,
            "Loading a real save must restore the local delayed marker from the viewer snapshot.");
        store.Write(GameSaveSlot.Manual, emptySave!);
        model.LoadManualGameCommand.Execute(null);
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        root.UpdateLayout();
        Program.Assert(markers.Items.Count == 0 && markers.Visibility == Visibility.Collapsed,
            "Restoring an empty judgment zone must remove the previous delayed status marker.");
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
