using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;
using FrameClock = NativeResponseStatusChecks.FrameClock;
using NativeHost = NativeResponseStatusChecks.NativeHost;

internal static class HandLiftMotionChecks
{
    private static readonly MethodInfo Refresh = typeof(CardLiftSurface).GetMethod("RefreshMotion", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo Animating = typeof(CardLiftSurface).GetProperty("IsLiftAnimating", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo Reflowing = typeof(CardLiftSurface).GetProperty("IsReflowAnimating", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo Offset = typeof(CardLiftSurface).GetProperty("HorizontalOffset", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo Timer = typeof(CardLiftSurface).GetProperty("IsFrameTimerRunning", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo Arriving = typeof(CardLiftSurface).GetProperty("IsArrivalAnimating", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo ArrivalAlpha = typeof(CardLiftSurface).GetProperty("ArrivalOpacity", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo Pointer = typeof(HandPanel).GetMethod("UpdatePointer", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo Resolve = typeof(HandPanel).GetMethod("CardButtonAt", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static void NativeLiftAndFlight(string output)
    {
        using (var vm = new MainViewModel(false, 721019, false, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore()))
        {
            StartLordFixture(vm);
            vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
            AdvanceToDecision(vm);
            var action = Engine(vm).GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash && action.CardId == 9 &&
                vm.Hand.Any(card => card.Id == action.CardId && card.Kind == CardKind.Slash));
            NativeCard(vm, vm.Hand.Single(card => card.Id == action.CardId), action.TargetSeat, output, "use", loaded: true);
        }
        var fixture = StableHandResponseFixture.Create(DecisionKind.RespondDodge, output, arrowBarrage: true);
        using var response = fixture.Model;
        var dodges = response.Hand.Where(card => card.Kind == CardKind.Dodge && card.IsPlayable).ToArray();
        Assert(dodges.Length == 4, "The fixed Arrow Barrage response must keep its four real physical Dodges; do not search a replacement seed.");
        NativeCard(response, dodges[2], null, output, "response", loaded: true);
        ConsecutiveResponseOrigins(output);
        NativeIncomingCards(output);
        if (RecordMotion) RecordNativeUse(output);
    }

    private static void NativeCard(MainViewModel vm, CardViewModel card, int? target, string output, string name, bool loaded)
    {
        vm.IsMotionEnabled = true; vm.IsSoundEnabled = false; vm.IsMusicEnabled = false;
        using var host = new NativeHost(vm);
        var window = host.Window; var root = host.Root; var layer = host.Layer; var clock = host.Clock;
        try
        {
            host.Capture(output, $"hand-reflow-{name}-before", 1120);
            var button = Find<Button>(root).Single(button => button.Name == "CardButton" && ReferenceEquals(button.DataContext, card));
            var surface = Find<CardLiftSurface>(button).Single(surface => surface.Name == "CardSurface");
            foreach (var item in Find<CardLiftSurface>(root)) item.Clock = clock;
            var panel = Find<HandPanel>(root).Single();
            var items = (ItemsControl)window.FindName("HandCards");
            var container = (FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(card);
            var slot = container.TransformToAncestor(panel).TransformBounds(new Rect(container.RenderSize));
            var point = new Point(slot.Left + 2, 80);
            var game = Engine(vm); var revision = game.Revision; var acceptedBefore = game.AcceptedCommands.Count;
            var draftViews = SafeViews(game);
            var changes = 0;
            RoutedEventHandler onLift = (_, args) => { if (ReferenceEquals(args.OriginalSource, surface)) changes++; };
            layer.HandRoot!.AddHandler(CardLiftSurface.LiftChangedEvent, onLift);
            try
            {
                Assert(surface.IsLoaded, "The real hidden native hand surface was not loaded before motion sampling.");
                Assert(!RunningTimer(surface), "An injected hand clock started a real dispatcher timer.");
                if (loaded && name == "use") VerifyMotion(vm, card, root, button, surface, panel, point, clock, output, () => changes);
                Assert(game.Revision == revision && game.AcceptedCommands.Count == acceptedBefore && SameViews(game, draftViews) &&
                    layer.ActiveEffectCount == 0, "Lifting or clearing a physical hand draft committed gameplay or exposed a public flight.");
                Assert(Enumerable.Range(0, draftViews.Length).Where(seat => seat != layer.HandOwnerSeat)
                    .All(seat => game.CreateSnapshot(seat).Players.Single(player => player.Seat == layer.HandOwnerSeat).Hand.Count == 0),
                    "Hand motion exposed undisclosed physical cards to another viewer.");

                // Establish the original slot once, then confirm during the lift
                // without a further Render/UpdateLayout refreshing the cached origin.
                host.Capture(output, $"hand-reflow-{name}-ready", 1120);
                vm.SelectCardCommand.Execute(card);
                if (target is { } seat) vm.SelectTargetCommand.Execute(vm.Seats.Single(player => player.Seat == seat));
                Bindings(root);
                Assert(surface.IsSelected && vm.CanConfirmSelected && Math.Abs(Y(surface)) < .001,
                    "A new native selection jumped to its final lift before the clock advanced.");
                clock.Advance(.05);
                Refresh.Invoke(surface, null);
                Assert(Y(surface) is < 0 and > -18 && Moving(surface) && changes > 0,
                    "The native card has no continuous 50 ms selection pose or bubbling origin refresh.");
                var origin = surface.TransformToVisual(layer).TransformBounds(new Rect(surface.RenderSize));
                var beforeRemoval = HandPoses(items, panel, layer);
                var currentY = Y(surface); var removedWhileLoaded = false;
                void OnCommittedState(GameSnapshot _)
                {
                    if (!loaded || vm.Hand.Contains(card)) return;
                    // Force the real removed container's layout and Unloaded
                    // boundary before its later committed public cue is projected.
                    removedWhileLoaded = true;
                    var layoutRefreshes = 0;
                    EventHandler onLayout = (_, _) => layoutRefreshes++;
                    layer.HandRoot!.LayoutUpdated += onLayout;
                    try
                    {
                        Bindings(root); root.UpdateLayout();
                        root.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                    }
                    finally { layer.HandRoot.LayoutUpdated -= onLayout; }
                    Assert(!surface.IsLoaded && Math.Abs(Y(surface) - currentY) < .001 && !Moving(surface) && !RunningTimer(surface),
                        "Committed hand removal changed the last visible lift or retained its real loaded surface/timer.");
                    Assert(layoutRefreshes > 0 && items.ItemContainerGenerator.ContainerFromItem(card) is null &&
                        !Find<Button>(items).Any(item => ReferenceEquals(item.DataContext, card)),
                        "The committed-state observer did not arrange the actual removed physical hand container.");
                    Console.WriteLine($"Committed hand layout boundary: physical {card.Id}, {layoutRefreshes} LayoutUpdated events, " +
                        $"actual last pose {origin}.");
                }
                game.StateChanged += OnCommittedState;
                try { vm.ConfirmSelectedCommand.Execute(null); }
                finally { game.StateChanged -= OnCommittedState; }
                Assert(game.AcceptedCommands.Count == acceptedBefore + 1 && !vm.Hand.Any(item => item.Id == card.Id) &&
                    (!loaded || removedWhileLoaded), "The real lifted card was not committed once through its removal boundary.");
                var reflow = AssertReflowStart(beforeRemoval, card.Id, items, panel, layer, name);
                var cue = vm.BattleCues.Last(cue => cue.PublicCardId == card.Id && cue.CardKind == card.Kind);
                Assert(cue.SourceSeat == layer.HandOwnerSeat && cue.PublicCard is not null,
                    "The committed lift did not publish its matching physical public face.");
                vm.ConfirmSelectedCommand.Execute(null);
                Assert(game.AcceptedCommands.Count == acceptedBefore + 1, "A repeated quick confirmation paid the physical hand card twice.");
                var accepted = game.AcceptedCommands.Count; var committedViews = SafeViews(game);
                clock.Advance(.012);
                layer.InvalidateVisual();
                RefreshAll(root); host.Capture(output, $"hand-reflow-{name}-launch", 1120);
                var launch = FaceBounds(layer, card.Kind).Single();
                var expected = new Rect(origin.X + 1, origin.Y + 1, origin.Width - 2, origin.Height - 2);
                Console.WriteLine($"Mid-lift first face {name}: physical {card.Id}, expected {expected}, actual {launch}.");
                Assert(Near(launch, expected), "A mid-lift confirmation launched from a stale/final hand pose instead of the current physical pose.");
                var images = Images(VisualTreeHelper.GetDrawing(layer), Matrix.Identity).ToArray();
                Assert(images.Single(item => ReferenceEquals(item.Image.ImageSource, card.Artwork)).Opacity > .99 &&
                    images.Any(item => ReferenceEquals(item.Image.ImageSource, card.RankArtwork)) &&
                    images.Any(item => ReferenceEquals(item.Image.ImageSource, card.SuitArtwork)),
                    "The lifted physical card lost its opaque face, actual rank or suit on flight handoff.");
                var landing = Find<Border>(root).Single(border => BattleFeedbackLayer.GetPlayAnchor(border) == cue.Sequence);
                Assert(((FrameworkElement)landing.Parent).Opacity < .001,
                    "The public row showed a second copy before the lifted physical card arrived.");
                clock.Advance(.028); RefreshAll(root); layer.InvalidateVisual();
                host.Capture(output, $"hand-reflow-{name}-40ms", 1120);
                AssertReflowMiddle(reflow, layer, name);
                clock.Advance(.14); RefreshAll(root); layer.InvalidateVisual();
                host.Capture(output, $"hand-reflow-{name}-180ms", 1120);
                AssertReflowSettled(reflow, layer, name);
                var beforeSortFace = FaceBounds(layer, card.Kind).Single();
                vm.SortHandCommand.Execute(null);
                layer.InvalidateVisual();
                host.Capture(output, $"hand-reflow-{name}-after-sort", 1120);
                Assert(Near(FaceBounds(layer, card.Kind).Single(), beforeSortFace), "Sorting the remaining hand moved a committed mid-lift origin.");
                Assert(game.AcceptedCommands.Count == accepted && SameViews(game, committedViews),
                    "Motion, rendering or sorting changed an accepted command or any player-safe view.");
                vm.IsMotionEnabled = false;
                Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning,
                    "Disabling hand motion left the accepted flight running.");
                Assert(game.AcceptedCommands.Count == accepted && SameViews(game, committedViews),
                    "Stopping hand motion changed a committed player view.");
                Console.WriteLine($"Native hand lift {name}: physical {card.Id}/{card.Kind}, Y {currentY:F3}, origin {origin}, " +
                    $"launch {launch}; loaded removal={removedWhileLoaded}, {changes} pose changes, one commit, {committedViews.Length} safe views unchanged.");
            }
            finally { layer.HandRoot!.RemoveHandler(CardLiftSurface.LiftChangedEvent, onLift); }
        }
        finally
        {
            foreach (var surface in Find<CardLiftSurface>(root)) surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
    }

    private static void ConsecutiveResponseOrigins(string output)
    {
        var fixture = StableHandResponseFixture.Create(DecisionKind.RespondDodge, output, wushuang: true);
        using var vm = fixture.Model;
        vm.IsMotionEnabled = true;
        using var host = new NativeHost(vm);
        host.Capture(output, "hand-reflow-double-response-before", 1120);
        var root = host.Root; var layer = host.Layer; var clock = host.Clock;
        var items = (ItemsControl)host.Window.FindName("HandCards");
        var panel = Find<HandPanel>(items).Single();
        foreach (var surface in Find<CardLiftSurface>(root)) surface.Clock = clock;
        var cards = vm.Hand.Where(card => card.Kind == CardKind.Dodge && card.IsPlayable).ToArray();
        Assert(cards.Select(card => card.Id).Order().SequenceEqual(new[] { 24, 32 }),
            "The existing seed31 Wushuang response must retain its two actual physical Dodges.");
        // Pay the earlier physical hand slot first, so the remaining real Dodge
        // necessarily participates in this same-width removal reflow.
        var firstCard = cards[0]; var secondCard = cards[1];
        var game = Engine(vm); var accepted = game.AcceptedCommands.Count; var cursor = game.Events.Count;
        var hp = game.CreateSnapshot(0).Players[0].Hp; var draft = SafeViews(game);
        vm.SelectCardCommand.Execute(firstCard); Bindings(root);
        Assert(vm.CanConfirmSelected, "The first native Wushuang physical Dodge cannot be confirmed.");
        clock.Advance(.05); RefreshAll(root);
        var beforeFirst = HandPoses(items, panel, layer);
        ConfirmAndArrange(firstCard, beforeFirst[firstCard.Id].Surface);
        var progress = game.Events.Skip(cursor).Select(item => item.Payload).OfType<RequiredResponseProgressEvent>().ToArray();
        Assert(game.AcceptedCommands.Count == accepted + 1 && progress.Length == 1 &&
            progress[0] is { ResponderSeat: 0, ResponseCount: 1, RequiredResponseCount: 2, RequiredCardKind: CardKind.Dodge } &&
            game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 } &&
            !vm.Hand.Any(card => card.Id == firstCard.Id) && vm.Hand.Single(card => card.Id == secondCard.Id).IsPlayable &&
            game.CreateSnapshot(0).Players[0].Hp == hp,
            "The first real paid Dodge must leave actual 1/2 progress and another native response without granting final avoidance.");
        var firstReflow = AssertReflowStart(beforeFirst, firstCard.Id, items, panel, layer, "double-response-first");
        var firstPaidViews = SafeViews(game);
        clock.Advance(.04); RefreshAll(root); layer.InvalidateVisual();
        host.Capture(output, "hand-reflow-double-response-40ms", 1120);
        AssertReflowMiddle(firstReflow, layer, "double-response-first");
        Assert(game.AcceptedCommands.Count == accepted + 1 && SameViews(game, firstPaidViews),
            "The first response's reflow committed gameplay or changed a viewer-safe state.");
        var secondContainer = (FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(secondCard);
        var secondSurface = Find<CardLiftSurface>(secondContainer).Single();
        Assert(X(secondSurface) > .1 && ReflowMoving(secondSurface),
            "The second physical Dodge did not occupy an actual intermediate horizontal reflow pose.");
        var visible = secondSurface.TransformToVisual(panel).TransformBounds(new Rect(secondSurface.RenderSize));
        var point = new Point(visible.Left + 2, 80);
        Pointer.Invoke(panel, [point]); Bindings(root);
        Assert(ReferenceEquals(Resolve.Invoke(panel, [point]), Find<Button>(secondContainer).Single(button => button.Name == "CardButton")),
            "The moving second physical Dodge is not resolved from its current visible horizontal slot.");
        vm.SelectCardCommand.Execute(secondCard); Bindings(root);
        Assert(vm.CanConfirmSelected, "The second actual Wushuang Dodge lost its native response action during reflow.");
        clock.Advance(.05); RefreshAll(root);
        var secondOrigin = Bounds(secondSurface, layer);
        var secondX = X(secondSurface); var secondY = Y(secondSurface);
        Assert(secondX > .1 && ReflowMoving(secondSurface) && secondY is < 0 and > -18 && Moving(secondSurface),
            "The second paid response was not confirmed during both its actual horizontal reflow and vertical lift.");
        var beforeSecond = HandPoses(items, panel, layer);
        // No Render or UpdateLayout occurs between this current X/Y sample and
        // the actual second command; LiftChanged must already refresh its origin.
        ConfirmAndArrange(secondCard, secondSurface);
        progress = game.Events.Skip(cursor).Select(item => item.Payload).OfType<RequiredResponseProgressEvent>().ToArray();
        var responses = game.Events.Skip(cursor).Select(item => item.Payload).OfType<CardRespondedEvent>()
            .Where(item => item.ResponderSeat == 0).ToArray();
        Assert(game.AcceptedCommands.Count == accepted + 2 && responses.Select(item => item.CardId).SequenceEqual(new[] { firstCard.Id, secondCard.Id }) &&
            progress.Length == 2 && progress[1].ResolutionId == progress[0].ResolutionId &&
            progress[1] is { ResponseCount: 2, RequiredResponseCount: 2 } &&
            !game.Events.Skip(cursor).Any(item => item.Payload is DamageAppliedEvent { TargetSeat: 0 }) &&
            game.CreateSnapshot(0).Players[0].Hp == hp && !vm.Hand.Any(card => card.Id == secondCard.Id) &&
            Math.Abs(X(secondSurface) - secondX) < .001 && Math.Abs(Y(secondSurface) - secondY) < .001,
            "The two native Dodges must pay exactly once, keep actual HP and preserve the removed second card's current X/Y.");
        var secondReflow = AssertReflowStart(beforeSecond, secondCard.Id, items, panel, layer, "double-response-second");
        var cue = vm.BattleCues.Last(cue => cue.PublicCardId == secondCard.Id && cue.Kind == BattleCueKind.Response);
        Assert(cue.SourceSeat == 0 && cue.PublicCard?.Id == secondCard.Id,
            "The second native response lost its actual public physical identity.");
        vm.ConfirmSelectedCommand.Execute(null);
        Assert(game.AcceptedCommands.Count == accepted + 2, "A repeated second response paid another physical card.");
        var settled = SafeViews(game);
        clock.Advance(.012); RefreshAll(root); layer.InvalidateVisual();
        host.Capture(output, "hand-reflow-double-response-launch", 1120);
        var expected = new Rect(secondOrigin.X + 1, secondOrigin.Y + 1, secondOrigin.Width - 2, secondOrigin.Height - 2);
        var faces = FaceBounds(layer, CardKind.Dodge).ToArray();
        Assert(faces.Length == 2 && faces.Any(face => Near(face, expected)),
            $"The second paid Dodge must fly from its current reflow/lift pose while preserving the earlier real flight. Expected {expected}, actual [{string.Join("; ", faces)}].");
        root.UpdateLayout(); layer.InvalidateVisual();
        host.Capture(output, "hand-reflow-double-response-launch-stable", 1120);
        Assert(FaceBounds(layer, CardKind.Dodge).Any(face => Near(face, expected)),
            "A second same-clock layout replaced the already captured second response origin.");
        clock.Advance(.028); RefreshAll(root); layer.InvalidateVisual();
        host.Capture(output, "hand-reflow-double-response-next-40ms", 1120);
        AssertReflowMiddle(secondReflow, layer, "double-response-second");
        clock.Advance(.14); RefreshAll(root); layer.InvalidateVisual();
        host.Capture(output, "hand-reflow-double-response-settled", 1120);
        AssertReflowSettled(secondReflow, layer, "double-response-second");
        Assert(game.AcceptedCommands.Count == accepted + 2 && SameViews(game, settled) && !draft.SequenceEqual(settled),
            "The two accepted responses must change only their real committed states; subsequent native frames cannot alter any safe view.");
        Console.WriteLine($"Native hand reflow double response: seed31, physical {firstCard.Id}->{secondCard.Id}, " +
            $"commands {accepted}->{game.AcceptedCommands.Count}, actual 1/2->2/2, second X={secondX:F3}/Y={secondY:F3}, " +
            $"second origin {secondOrigin}, matching first face {expected}, {settled.Length} safe views unchanged.");

        void ConfirmAndArrange(CardViewModel card, CardLiftSurface surface)
        {
            var x = X(surface); var y = Y(surface);
            void ArrangeRemoved(GameSnapshot _)
            {
                if (vm.Hand.Contains(card)) return;
                Bindings(root); root.UpdateLayout();
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                Assert(items.ItemContainerGenerator.ContainerFromItem(card) is null && !surface.IsLoaded &&
                    Math.Abs(X(surface) - x) < .001 && Math.Abs(Y(surface) - y) < .001 && !RunningTimer(surface),
                    "A real loaded response removal changed its last pose or retained its native container/timer.");
            }
            game.StateChanged += ArrangeRemoved;
            try { vm.ConfirmSelectedCommand.Execute(null); }
            finally { game.StateChanged -= ArrangeRemoved; }
        }
    }

    private static void NativeIncomingCards(string output)
    {
        var (prepared, registry) = LastZoneConversionScenario.Create("equipment");
        var drawAction = prepared.GetHumanLegalActions().FirstOrDefault(action => action.Kind == LegalActionKind.DrawTwo);
        var preparatoryDraws = 0;
        // This is the existing Core fixture's bounded preparation, using real
        // published skill commands; the seed, physical deck and hand stay intact.
        while (drawAction is null && preparatoryDraws < 8)
        {
            LastZoneConversionScenario.Activate(prepared, "draw");
            LastZoneConversionScenario.ReachPlay(prepared);
            preparatoryDraws++;
            drawAction = prepared.GetHumanLegalActions().FirstOrDefault(action => action.Kind == LegalActionKind.DrawTwo);
        }
        Assert(drawAction?.CardId is { }, "The existing seed31 equipment fixture did not supply a genuine DrawTwo within its eight native preparatory draws.");
        var lastId = drawAction!.CardId!.Value;
        var discarded = prepared.CreateSnapshot(0).Players.Single(player => player.Seat == 0).Hand
            .Where(card => card.Id != lastId).Select(card => card.Id).ToArray();
        LastZoneConversionScenario.Activate(prepared, "drop", discarded);
        LastZoneConversionScenario.ReachPlay(prepared);
        Assert(prepared.CreateSnapshot(0).Players.Single(player => player.Seat == 0).Hand is { Count: 1 } lastHand &&
            lastHand[0].Id == lastId && lastHand[0].Kind == CardKind.DrawTwo,
            "The real preparatory discard did not leave exactly the physical DrawTwo in Hand.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, prepared.CreateCheckpoint()));
        using var vm = new MainViewModel(false, prepared.Seed, true, store, useExpandedContent: true,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore(), contentRegistry: registry)
            { IsMotionEnabled = true, IsSoundEnabled = false, IsMusicEnabled = false };
        vm.LoadManualGameCommand.Execute(null);
        Assert(!vm.HasSaveError && vm.Hand.Count == 1 && vm.Hand[0].Id == lastId, vm.SaveStatus);
        using var host = new NativeHost(vm);
        var root = host.Root; var layer = host.Layer; var clock = host.Clock;
        var items = (ItemsControl)host.Window.FindName("HandCards");
        var panel = Find<HandPanel>(items).Single();
        panel.Clock = clock;
        RoutedEventHandler shareClock = (_, args) =>
        {
            if (args.OriginalSource is CardLiftSurface surface) surface.Clock = clock;
        };
        root.AddHandler(FrameworkElement.LoadedEvent, shareClock, true);
        var encoder = RecordMotion ? new GifBitmapEncoder() : null;
        var elapsed = 0d; var nextMovieFrame = 0;
        try
        {
            host.Capture(output, "hand-arrival-native-cold", 1120);
            ShareClocks(); AssertNoArrival("cold checkpoint");
            Assert(layer.ActiveEffectCount == 0 && !vm.IsOpeningDealVisible,
                "Cold loading the actual one-card checkpoint replayed a public flight or opening deal.");
            var game = Engine(vm); var accepted = game.AcceptedCommands.Count; var cursor = game.Events.Count;
            var cold = SafeViews(game);
            var lastCard = vm.Hand.Single();
            AdvanceTo(.04);
            vm.SelectCardCommand.Execute(lastCard); Bindings(root);
            Assert(vm.CanConfirmSelected, "The actual last DrawTwo lost its native play action after cold loading.");
            AdvanceTo(.09);
            ConfirmAndArrange(lastCard, Surface(lastCard));
            var afterDraw = game.CreateSnapshot(0);
            var fresh = afterDraw.Players.Single(player => player.Seat == 0).Hand.ToArray();
            var facts = game.Events.Skip(cursor).Select(item => item.Payload).ToArray();
            Console.WriteLine($"Native last-card draw: seed31, preparatory draws={preparatoryDraws}, dropped={discarded.Length}, " +
                $"physical DrawTwo{lastId}, commands {accepted}->{game.AcceptedCommands.Count}, hand={string.Join(',', fresh.Select(card => $"{card.Id}/{card.Kind}"))}, " +
                $"pending={afterDraw.PendingDecision?.Kind}/{afterDraw.PendingDecision?.PlayerSeat}.");
            Assert(game.AcceptedCommands.Count == accepted + 1 && fresh.Length == 2 && !fresh.Any(card => card.Id == lastId) &&
                facts.OfType<CardUseDeclaredEvent>().Count(fact => fact.CardId == lastId && fact.CardKind == CardKind.DrawTwo && fact.SourceSeat == 0) == 1 &&
                !facts.OfType<NullificationRespondedEvent>().Any(),
                "The genuine final DrawTwo must pay once and return two actual new Hand cards without a fabricated counterspell.");
            Assert(game.CardMovements.Count(move => move.CardId == lastId && move.From == CardLocation.Hand(0) &&
                move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Use) == 1,
                "The last physical DrawTwo did not leave the real Hand exactly once.");
            vm.ConfirmSelectedCommand.Execute(null);
            Assert(game.AcceptedCommands.Count == accepted + 1, "Repeated DrawTwo confirmation paid another physical card before the next actual decision.");
            ShareClocks();
            var freshIds = fresh.Select(card => card.Id).ToHashSet();
            var freshSurfaces = vm.Hand.Select(Surface).ToArray();
            Assert(freshSurfaces.Length == 2 && freshSurfaces.All(surface => surface.IsLoaded && ArrivalMoving(surface) &&
                ArrivalOpacity(surface) is >= .72 and < 1 && Y(surface) is >= -12 and < 0 && !RunningTimer(surface)),
                "The two genuine new cards must arrive visibly under the shared clock instead of appearing at their final pose.");
            Assert(vm.BattleCues.All(cue => cue.PublicCardId is not { } id || !freshIds.Contains(id)) &&
                Enumerable.Range(1, 3).All(viewer => game.CreateSnapshot(viewer).Players.Single(player => player.Seat == 0).Hand.Count == 0),
                "An undisclosed drawn entity appeared in a public battle cue or another player's hand view.");
            var drawnViews = SafeViews(game);
            AdvanceTo(.13); // Forty ms after the actual last-card command.
            host.Capture(output, "hand-arrival-native-40ms", 1120);
            Assert(freshSurfaces.All(surface => ArrivalMoving(surface) && ArrivalOpacity(surface) is > .72 and < 1) &&
                Y(freshSurfaces[0]) > Y(freshSurfaces[1]) + .1 &&
                ArrivalOpacity(freshSurfaces[0]) > ArrivalOpacity(freshSurfaces[1]) + .001,
                "The two-card batch lost its real intermediate arrival or bounded 35 ms stagger.");
            Assert(game.AcceptedCommands.Count == accepted + 1 && SameViews(game, drawnViews) && !cold.SequenceEqual(drawnViews),
                "Arrival rendering changed a committed command or any of the four safe snapshots.");
            // The effect has already drawn its physical cards, but the real
            // command session publishes the next Play prompt on a later step.
            // Advance that existing native workflow at this same frame clock;
            // it must not restart arrival or be counted as another card payment.
            var nativeAdvances = 0;
            while (game.CreateSnapshot(0).PendingDecision is null && vm.CanStepAi && nativeAdvances < 8)
            {
                vm.StepAiCommand.Execute(null); nativeAdvances++;
            }
            Assert(game.CreateSnapshot(0).PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } &&
                game.AcceptedCommands.Count == accepted + 1 + nativeAdvances &&
                game.AcceptedCommands.Skip(accepted + 1).All(command => command is AdvanceOneStepCommand) &&
                game.CreateSnapshot(0).Players.Single(player => player.Seat == 0).Hand.Select(card => card.Id).SequenceEqual(fresh.Select(card => card.Id)),
                "Bounded native continuation did not publish the genuine next Play prompt, or it added another payment/hand mutation.");
            Bindings(root); root.UpdateLayout(); ShareClocks();
            Assert(freshSurfaces.All(ArrivalMoving), "Publishing the actual next Play prompt ended an in-flight incoming batch.");
            var readyViews = SafeViews(game);
            Console.WriteLine($"Native incoming Play continuation: {nativeAdvances} actual AdvanceOneStep commands at the unchanged 40ms arrival pose; " +
                $"commands {accepted + 1}->{game.AcceptedCommands.Count}, no additional paid card.");
            var next = game.GetHumanLegalActions().Where(action => action.CardId is { } id && freshIds.Contains(id) &&
                action.Kind is LegalActionKind.Equip or LegalActionKind.DrawTwo && action.PlayedCardKind is null)
                .OrderBy(action => action.Kind == LegalActionKind.Equip ? 0 : 1).FirstOrDefault();
            Assert(next?.CardId is { }, "Neither actual freshly drawn equipment nor DrawTwo has its native immediate play action.");
            var nextCard = vm.Hand.Single(card => card.Id == next!.CardId);
            var nextSurface = Surface(nextCard);
            var visible = Bounds(nextSurface, panel);
            var point = new Point(visible.Left + 2, 80);
            Assert(ReferenceEquals(Resolve.Invoke(panel, [point]), Find<Button>(items).Single(button =>
                button.Name == "CardButton" && ReferenceEquals(button.DataContext, nextCard))),
                "The visible arriving physical card cannot be picked from its current hand slot.");
            vm.SelectCardCommand.Execute(nextCard);
            if (next!.TargetSeat is { } target) vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == target));
            Bindings(root);
            Assert(vm.CanConfirmSelected && nextSurface.IsSelected && ArrivalMoving(nextSurface) && SameViews(game, readyViews),
                "Selecting an arriving card delayed its real action, ended its arrival or committed the pending draft.");
            AdvanceTo(.18); // Fifty ms of lift; the arrival remains in progress.
            var origin = Bounds(nextSurface, layer);
            var combinedY = Y(nextSurface); var combinedX = X(nextSurface);
            Assert(ArrivalMoving(nextSurface) && Moving(nextSurface) && combinedY is >= -18 and < 0,
                "The next real command was not prepared during its independently combined arrival and lift.");
            // LiftChanged already owns this current visible origin. Do not render
            // or arrange again between sampling it and the actual confirmation.
            ConfirmAndArrange(nextCard, nextSurface);
            var expectedCommands = accepted + 2 + nativeAdvances;
            Assert(game.AcceptedCommands.Count == expectedCommands && !vm.Hand.Any(card => card.Id == nextCard.Id) &&
                Math.Abs(Y(nextSurface) - combinedY) < .001 && Math.Abs(X(nextSurface) - combinedX) < .001,
                "Quick confirmation did not commit immediately once or preserve the unloaded card's combined physical pose.");
            var equipment = next!.Kind == LegalActionKind.Equip;
            var paymentReason = equipment ? CardMoveReasons.EquipmentUse : CardMoveReasons.Use;
            Assert(game.AcceptedCommands.Skip(accepted).OfType<PlayCardCommand>().Count(command =>
                    command.ActorSeat == 0 && command.CardId == nextCard.Id) == 1 &&
                game.Events.Skip(cursor).Count(item => item.Payload is CardUseDeclaredEvent declared && declared.CardId == nextCard.Id) == 1 &&
                game.CardMovements.Count(move => move.CardId == nextCard.Id && move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing && move.Reason == paymentReason) == 1 &&
                game.AcceptedCommands.Skip(accepted).Count(command => command is PlayCardCommand) == 2 &&
                game.AcceptedCommands.Skip(accepted).Count(command => command is AdvanceOneStepCommand) == nativeAdvances,
                "The arriving physical entity must have one genuine declaration and one Hand payment.");
            if (equipment)
                Assert(game.Events.Skip(cursor).Count(item => item.Payload is EquipmentChangedEvent changed &&
                        changed.PlayerSeat == 0 && changed.CardId == nextCard.Id && changed.CardKind == nextCard.Kind) == 1 &&
                    game.CardMovements.Count(move => move.CardId == nextCard.Id && move.From == CardLocation.Processing &&
                        move.To == CardLocation.Equipment(0) && move.Reason == CardMoveReasons.EquipmentEnter) == 1 &&
                    game.CreateSnapshot(0).Players.Single(player => player.Seat == 0).Equipment.Any(card => card.Id == nextCard.Id),
                    "The genuine freshly drawn equipment did not enter its actual owner slot once through the native equipment lifecycle.");
            vm.ConfirmSelectedCommand.Execute(null);
            Assert(game.AcceptedCommands.Count == expectedCommands, "Repeated quick confirmation paid an arriving physical card twice.");
            var cue = vm.BattleCues.Last(cue => cue.PublicCardId == nextCard.Id && cue.Kind == BattleCueKind.Card);
            Assert(cue.PublicCard?.Id == nextCard.Id && cue.CardKind == nextCard.Kind && cue.SourceSeat == 0,
                "The arriving card's genuine use lost its actual printed public face.");
            Assert(vm.BattleCues.All(item => item.PublicCardId is not { } id || !freshIds.Contains(id) || id == nextCard.Id),
                "Using one arriving entity exposed the other private draw.");
            var paidViews = SafeViews(game);
            AdvanceTo(.192); layer.InvalidateVisual();
            host.Capture(output, "hand-arrival-native-use-launch", 1120);
            var expected = new Rect(origin.X + 1, origin.Y + 1, origin.Width - 2, origin.Height - 2);
            var faces = FaceBounds(layer, nextCard.Kind).ToArray();
            Assert(faces.Any(face => Near(face, expected)) && (nextCard.Kind != CardKind.DrawTwo || faces.Length == 2),
                $"The first fresh-card flight must start at its current arrival/lift origin. Expected {expected}, actual [{string.Join("; ", faces)}].");
            Assert(Images(VisualTreeHelper.GetDrawing(layer), Matrix.Identity).Any(image =>
                ReferenceEquals(image.Image.ImageSource, nextCard.Artwork) && Near(image.Bounds, expected) && image.Opacity > .99),
                "The public flight inherited its private arrival fade instead of handing off an opaque actual face.");
            AdvanceTo(.45);
            host.Capture(output, "hand-arrival-native-settled", 1120);
            Assert(Find<CardLiftSurface>(items).All(surface => !ArrivalMoving(surface) && Math.Abs(ArrivalOpacity(surface) - 1) < .001),
                "A real arriving batch did not settle after its 200 ms duration and bounded stagger.");
            Assert(game.AcceptedCommands.Count == expectedCommands && SameViews(game, paidViews),
                "Settling or sampling the actual arrival changed a command or safe snapshot.");
            if (encoder is not null)
            {
                AdvanceTo(1.96);
                var path = Path.Combine(output, "hand-arrival-native.gif");
                using (var file = File.Create(path)) encoder.Save(file);
                BattleEffectsChecks.SetGifTiming(path, 4);
                using var stream = File.OpenRead(path);
                var decoded = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert(decoded.Frames.Count == 50 && decoded.Frames.All(frame => frame.PixelWidth == 728 && frame.PixelHeight == 481 &&
                    frame.Metadata is BitmapMetadata metadata && (ushort)metadata.GetQuery("/grctlext/Delay") == 4),
                    "The actual arrival movie lost its 50 native frames, compact dimensions or 40 ms encoding.");
                Assert(!Pixels(2).SequenceEqual(Pixels(3)) && !Pixels(3).SequenceEqual(Pixels(4)) && !Pixels(5).SequenceEqual(Pixels(8)),
                    "The decoded arrival, independent lift and genuine public flight became repeated static frames.");
                Assert(!FaceBounds(layer, nextCard.Kind).Any() && game.AcceptedCommands.Count == expectedCommands && SameViews(game, paidViews),
                    "The recorded actual flight did not expire or its sampling mutated the accepted game.");
                Console.WriteLine($"Native incoming movie: {decoded.Frames.Count} actual 40ms frames, 728x481; DrawTwo at90ms, fresh selection130ms, confirmation180ms, first-flight192ms.");

                byte[] Pixels(int index)
                {
                    var bitmap = new FormatConvertedBitmap(decoded.Frames[index], PixelFormats.Pbgra32, null, 0);
                    var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                    bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
                }
            }
            vm.SaveGameCommand.Execute(null);
            Assert(vm.HasManualSave, "The genuine current arrival scenario did not save through the existing manual store.");
            vm.LoadManualGameCommand.Execute(null); game = Engine(vm);
            Bindings(root); root.UpdateLayout(); ShareClocks();
            host.Capture(output, "hand-arrival-native-reloaded", 1120);
            Assert(!vm.HasSaveError && SameViews(game, paidViews) && layer.ActiveEffectCount == 0,
                "Loading the actual current checkpoint changed a safe view or replayed its historical public flight.");
            AssertNoArrival("same-window real reload");
            vm.IsMotionEnabled = false; Bindings(root); vm.IsMotionEnabled = true; Bindings(root);
            AdvanceTo(elapsed + .3);
            AssertNoArrival("motion disabled then restored");
            Assert(SameViews(game, paidViews) && game.AcceptedCommands.Count == expectedCommands,
                "Restoring presentation preferences replayed a command or changed any player-safe snapshot.");
            Console.WriteLine($"Native hand arrival: seed31, last DrawTwo{lastId} paid once, actual fresh[{string.Join(',', fresh.Select(card => card.Id))}], " +
                $"immediate fresh use {nextCard.Id}/{nextCard.Kind}, combined X={combinedX:F3}/Y={combinedY:F3}, " +
                $"origin={origin}, firstface={expected}; two paid commands + {nativeAdvances} genuine continuation steps, " +
                $"four safe views; cold/reload/motion-on no replay.");
        }
        finally { root.RemoveHandler(FrameworkElement.LoadedEvent, shareClock); }

        CardLiftSurface Surface(CardViewModel card) => Find<CardLiftSurface>((FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(card)).Single();
        void ShareClocks() { foreach (var surface in Find<CardLiftSurface>(items)) surface.Clock = clock; }
        void AssertNoArrival(string boundary) => Assert(Find<CardLiftSurface>(items).All(surface => !ArrivalMoving(surface) &&
            Math.Abs(ArrivalOpacity(surface) - 1) < .001 && Math.Abs(Y(surface)) < .001 && !RunningTimer(surface)),
            $"The {boundary} boundary replayed local incoming motion instead of keeping the actual hand static.");
        void ConfirmAndArrange(CardViewModel card, CardLiftSurface surface)
        {
            var game = Engine(vm); var x = X(surface); var y = Y(surface);
            void ArrangeCommitted(GameSnapshot _)
            {
                if (vm.Hand.Contains(card)) return;
                Bindings(root); root.UpdateLayout(); ShareClocks();
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                Assert(!surface.IsLoaded && items.ItemContainerGenerator.ContainerFromItem(card) is null &&
                    Math.Abs(X(surface) - x) < .001 && Math.Abs(Y(surface) - y) < .001 && !RunningTimer(surface),
                    "The real incoming-card removal lost the last combined origin before its public cue was projected.");
            }
            game.StateChanged += ArrangeCommitted;
            try { vm.ConfirmSelectedCommand.Execute(null); }
            finally { game.StateChanged -= ArrangeCommitted; }
        }
        void AdvanceTo(double seconds)
        {
            if (encoder is not null && nextMovieFrame < 50)
                while (nextMovieFrame < 50 && nextMovieFrame * .04 <= seconds + .0000001)
                {
                    Advance(nextMovieFrame * .04);
                    var metadata = new BitmapMetadata("gif");
                    if (nextMovieFrame == 0)
                    {
                        metadata.SetQuery("/appext/application", System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                        metadata.SetQuery("/appext/data", new byte[] { 3, 1, 0, 0, 0 });
                    }
                    var bitmap = MovieFrame();
                    encoder.Frames.Add(BitmapFrame.Create(new TransformedBitmap(bitmap, new ScaleTransform(.65, .65)), null, metadata, null));
                    nextMovieFrame++;
                }
            Advance(seconds);
        }
        void Advance(double seconds)
        {
            Assert(seconds >= elapsed - .0000001, "A native arrival sample attempted to rewind its accepted command clock.");
            clock.Advance(Math.Max(0, seconds - elapsed)); elapsed = seconds;
            ShareClocks(); RefreshAll(root); layer.InvalidateVisual();
        }
        RenderTargetBitmap MovieFrame()
        {
            root.UpdateLayout(); root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            Assert(root.RenderSize == new Size(1120, 740) && host.Window.IsLoaded && host.Window.IsVisible && root.IsVisible,
                "The native arrival movie lost its existing loaded 1120x740 host.");
            var bitmap = new RenderTargetBitmap(1120, 740, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root); return bitmap;
        }
    }

    private sealed record HandPose(CardViewModel Card, FrameworkElement Container, CardLiftSurface Surface, Rect Bounds, double SlotX);
    private sealed record ReflowWitness(HandPose Before, double TargetX, double InitialOffset);

    private static Dictionary<int, HandPose> HandPoses(ItemsControl items, HandPanel panel, BattleFeedbackLayer layer) =>
        items.Items.Cast<CardViewModel>().ToDictionary(card => card.Id, card =>
        {
            var container = (FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(card);
            var surface = Find<CardLiftSurface>(container).Single();
            return new HandPose(card, container, surface, Bounds(surface, layer), Bounds(container, panel).X);
        });

    private static ReflowWitness[] AssertReflowStart(Dictionary<int, HandPose> before, int removed,
        ItemsControl items, HandPanel panel, BattleFeedbackLayer layer, string label)
    {
        var after = HandPoses(items, panel, layer);
        Assert(after.Count == before.Count - 1 && !after.ContainsKey(removed),
            $"Native {label} did not remove exactly its paid physical hand card.");
        var moving = new List<ReflowWitness>();
        foreach (var current in after.Values)
        {
            var previous = before[current.Card.Id];
            Assert(ReferenceEquals(previous.Card, current.Card) && ReferenceEquals(previous.Container, current.Container) &&
                ReferenceEquals(previous.Surface, current.Surface) && Math.Abs(current.Bounds.X - previous.Bounds.X) < .1,
                $"Native {label} survivor {current.Card.Id} changed identity/container or jumped from {previous.Bounds.X:F3} to {current.Bounds.X:F3} at removal.");
            var offset = X(current.Surface); var targetX = current.Bounds.X - offset;
            if (Math.Abs(previous.SlotX - current.SlotX) > .1 || Math.Abs(offset) > .1)
            {
                Assert(ReflowMoving(current.Surface) && Math.Abs(offset) > .1,
                    $"Native {label} moved survivor {current.Card.Id} without an actual horizontal transition.");
                moving.Add(new(previous, targetX, offset));
            }
        }
        Assert(moving.Count > 0, $"Native {label} fixture has no actual surviving card whose physical slot changes.");
        Console.WriteLine($"Native hand reflow {label}: {before.Count}->{after.Count} physical cards, same survivor containers; " +
            string.Join("; ", moving.Select(item => $"{item.Before.Card.Id}: X {item.Before.Bounds.X:F3}->{item.TargetX:F3}, offset {item.InitialOffset:F3}")));
        return moving.ToArray();
    }

    private static void AssertReflowMiddle(ReflowWitness[] moving, BattleFeedbackLayer layer, string label)
    {
        foreach (var item in moving)
        {
            var offset = X(item.Before.Surface); var current = Bounds(item.Before.Surface, layer);
            Assert(ReflowMoving(item.Before.Surface) && Math.Abs(offset) is > .1 && Math.Abs(offset) < Math.Abs(item.InitialOffset) &&
                Math.Sign(offset) == Math.Sign(item.InitialOffset) && Math.Abs(current.X - item.TargetX - offset) < .1,
                $"Native {label} survivor {item.Before.Card.Id} lacks a continuous intermediate X: initial={item.InitialOffset:F3}, current={offset:F3}.");
        }
    }

    private static void AssertReflowSettled(ReflowWitness[] moving, BattleFeedbackLayer layer, string label)
    {
        foreach (var item in moving)
            Assert(!ReflowMoving(item.Before.Surface) && Math.Abs(X(item.Before.Surface)) < .001 &&
                Math.Abs(Bounds(item.Before.Surface, layer).X - item.TargetX) < .1 && !RunningTimer(item.Before.Surface),
                $"Native {label} survivor {item.Before.Card.Id} failed to settle at its true new slot or retained a timer.");
    }

    private static void VerifyMotion(MainViewModel vm, CardViewModel card, FrameworkElement root, Button button, CardLiftSurface surface,
        HandPanel panel, Point point, FrameClock clock, string output, Func<int> changes)
    {
        Pointer.Invoke(panel, [point]); Bindings(root);
        Assert(surface.IsPointerOver && Math.Abs(Y(surface)) < .001, "Hover snapped the physical card directly to its raised pose.");
        clock.Advance(.04); Refresh.Invoke(surface, null);
        Assert(Y(surface) is < 0 and > -7 && Moving(surface), "Hover has no intermediate lift frame.");
        Assert(ReferenceEquals(Resolve.Invoke(panel, [point]), button), "A moving hand face changed its original physical input slot.");
        Render(root, 1120, 740, Path.Combine(output, "hand-reflow-use-hover-middle.png"));
        clock.Advance(.12); Refresh.Invoke(surface, null);
        Assert(Math.Abs(Y(surface) + 7) < .001 && !Moving(surface), "Hover did not settle at its existing seven-DIP pose.");
        var hoverY = Y(surface);
        vm.SelectCardCommand.Execute(card); Bindings(root);
        Assert(surface.IsSelected && Math.Abs(Y(surface) - hoverY) < .001, "Selection jumped away from the current hover pose.");
        clock.Advance(.05); Refresh.Invoke(surface, null);
        var selectedY = Y(surface);
        Assert(selectedY is < -7 and > -18 && Moving(surface), "Selection has no intermediate frame between hover and full lift.");
        Render(root, 1120, 740, Path.Combine(output, "hand-reflow-use-selected-middle.png"));
        var count = changes(); Refresh.Invoke(surface, null);
        Assert(changes() == count, "An unchanged controlled-clock pose emitted another LiftChanged event.");
        vm.ClearSelectionCommand.Execute(null); Bindings(root);
        Assert(!surface.IsSelected && Math.Abs(Y(surface) - selectedY) < .001, "Cancelling selection restarted from a final pose instead of its current Y.");
        clock.Advance(.03); Refresh.Invoke(surface, null);
        var reverseY = Y(surface);
        Assert(reverseY > selectedY && reverseY < -7, "Clearing selection did not reverse toward its current hovered pose.");
        Assert(ReferenceEquals(Resolve.Invoke(panel, [point]), button), "A reversing hand face took a neighbor's input slot.");
        Pointer.Invoke(panel, [new Point(-10, 80)]); Bindings(root);
        Assert(!surface.IsPointerOver && Math.Abs(Y(surface) - reverseY) < .001, "Leaving the pointer teleported a reversing card.");
        clock.Advance(.16); surface.InvalidateVisual();
        Render(root, 1120, 740, Path.Combine(output, "hand-reflow-use-returned.png"));
        Assert(Math.Abs(Y(surface)) < .001 && !Moving(surface), "Offscreen rendering did not refresh and settle the injected hand clock.");

        vm.SelectCardCommand.Execute(card); Bindings(root);
        clock.Advance(.04); Refresh.Invoke(surface, null);
        Assert(Y(surface) is < 0 and > -18, "The motion-off fixture did not enter an actual selection transition.");
        vm.IsMotionEnabled = false; Bindings(root);
        Assert(!surface.MotionEnabled && Math.Abs(Y(surface) + 18) < .001 && !Moving(surface) && !RunningTimer(surface),
            "Motion-off did not stop the timer and immediately show the selected static pose.");
        vm.IsMotionEnabled = true; Bindings(root);
        Assert(!Moving(surface), "Motion-on replayed the old selection transition.");
        vm.ClearSelectionCommand.Execute(null); Bindings(root);
        clock.Advance(.02); Refresh.Invoke(surface, null);
        var beforeUnload = Y(surface); count = changes();
        surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        clock.Advance(.3); Refresh.Invoke(surface, null);
        Assert(Math.Abs(Y(surface) - beforeUnload) < .001 && changes() == count && !Moving(surface) && !RunningTimer(surface),
            "Unloading changed the cached physical pose, emitted a stale origin refresh or kept its timer.");
        surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert(Math.Abs(Y(surface)) < .001 && !Moving(surface) && !RunningTimer(surface),
            "Reloading replayed an old lift rather than restoring the current static draft.");

        surface.Clock = TimeProvider.System;
        vm.SelectCardCommand.Execute(card); Bindings(root);
        Assert(Moving(surface) && RunningTimer(surface), "A loaded runtime hand selection did not start its dispatcher animation timer.");
        surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert(!Moving(surface) && !RunningTimer(surface), "A runtime hand timer survived unloading.");
        vm.ClearSelectionCommand.Execute(null); Bindings(root);
        surface.Clock = clock;
        surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert(Math.Abs(Y(surface)) < .001 && !Moving(surface), "The native hand did not return to its unselected pose after timer checks.");

        // A real pointer observer may retarget the lift synchronously while the
        // previous motion's final pose bubbles; it must continue from that pose.
        var continuation = new CardLiftSurface { Clock = clock };
        continuation.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        continuation.IsSelected = true;
        clock.Advance(.16); Refresh.Invoke(continuation, null);
        var retargets = 0;
        RoutedEventHandler onFinalPose = (_, args) =>
        {
            if (!ReferenceEquals(args.OriginalSource, continuation) || continuation.IsSelected ||
                continuation.IsPointerOver || Math.Abs(Y(continuation)) > .001) return;
            retargets++; continuation.IsPointerOver = true;
        };
        continuation.AddHandler(CardLiftSurface.LiftChangedEvent, onFinalPose);
        try
        {
            continuation.IsSelected = false;
            clock.Advance(.16); Refresh.Invoke(continuation, null);
            Assert(retargets == 1 && Math.Abs(Y(continuation)) < .001 && Moving(continuation),
                "A synchronous pointer retarget snapped the new hover lift at the old motion's final frame.");
            clock.Advance(.04); Refresh.Invoke(continuation, null);
            Assert(Y(continuation) is < 0 and > -7 && Moving(continuation),
                "A synchronous hover continuation lost its actual intermediate pose.");
            clock.Advance(.12); Refresh.Invoke(continuation, null);
            Assert(Math.Abs(Y(continuation) + 7) < .001 && !Moving(continuation) && !RunningTimer(continuation),
                "A synchronous hover continuation did not settle and retire normally.");
        }
        finally
        {
            continuation.RemoveHandler(CardLiftSurface.LiftChangedEvent, onFinalPose);
            continuation.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
    }

    private static void RecordNativeUse(string output)
    {
        // A fresh native fixture keeps the recorded command independent of the
        // already accepted use/response checks; no replay or clock rewind is needed.
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore());
        StartLordFixture(vm);
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        AdvanceToDecision(vm);
        var game = Engine(vm);
        var action = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash && action.CardId == 9 &&
            vm.Hand.Any(card => card.Id == action.CardId && card.Kind == CardKind.Slash));
        var card = vm.Hand.Single(card => card.Id == action.CardId);
        vm.IsMotionEnabled = true; vm.IsSoundEnabled = false; vm.IsMusicEnabled = false;
        using var host = new NativeHost(vm);
        var window = host.Window; var root = host.Root; var layer = host.Layer; var clock = host.Clock;
        var encoder = new GifBitmapEncoder();
        try
        {
            host.Capture(output, "hand-reflow-native-use-before", 1120);
            Capture();
            var button = Find<Button>(root).Single(button => button.Name == "CardButton" && ReferenceEquals(button.DataContext, card));
            var surface = Find<CardLiftSurface>(button).Single(surface => surface.Name == "CardSurface");
            var panel = Find<HandPanel>(root).Single();
            var items = (ItemsControl)window.FindName("HandCards");
            var container = (FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(card);
            var slot = container.TransformToAncestor(panel).TransformBounds(new Rect(container.RenderSize));
            var point = new Point(slot.Left + 2, 80);
            Assert(surface.IsLoaded, "The actual recorded physical hand card is not loaded in its hidden native host.");
            var acceptedBefore = game.AcceptedCommands.Count;
            var draftViews = SafeViews(game);
            string[]? committedViews = null;
            BattleCue? cue = null;
            var origin = Rect.Empty;
            var confirmY = 0d;
            ReflowWitness[]? reflow = null;
            for (var frame = 0; frame < 52; frame++)
            {
                if (frame == 8)
                {
                    // The hover has settled for 160 ms at .28 s. Select at .298 s,
                    // between encoded samples, so the .36 s frame can show the
                    // real first flight 12 ms after a precise 50 ms confirmation.
                    Advance(.018);
                    vm.SelectCardCommand.Execute(card);
                    Assert(action.TargetSeat is { }, "The recorded real Slash has no physical target.");
                    vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == action.TargetSeat));
                    Bindings(root);
                    Assert(surface.IsSelected && vm.CanConfirmSelected && Math.Abs(Y(surface) + 7) < .001,
                        "The recorded selection jumped away from its settled native hover.");
                    Advance(.022);
                }
                else if (frame == 9)
                {
                    Advance(.028);
                    Assert(Moving(surface) && Y(surface) is < -7 and > -18 &&
                        game.AcceptedCommands.Count == acceptedBefore && SameViews(game, draftViews),
                        "The recording lost its actual 50 ms selection pose or committed before confirmation.");
                    origin = surface.TransformToVisual(layer).TransformBounds(new Rect(surface.RenderSize));
                    confirmY = Y(surface);
                    var beforeRemoval = HandPoses(items, panel, layer);
                    void UnloadCommittedCard(GameSnapshot _)
                    {
                        if (vm.Hand.Contains(card)) return;
                        Bindings(root); root.UpdateLayout();
                        root.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                    }
                    game.StateChanged += UnloadCommittedCard;
                    try { vm.ConfirmSelectedCommand.Execute(null); }
                    finally { game.StateChanged -= UnloadCommittedCard; }
                    Assert(game.AcceptedCommands.Count == acceptedBefore + 1 && !vm.Hand.Any(item => item.Id == card.Id) &&
                        !surface.IsLoaded && Math.Abs(Y(surface) - confirmY) < .001 && !RunningTimer(surface),
                        "The recording did not remove and preserve the native lifted card after one accepted command.");
                    cue = vm.BattleCues.Last(cue => cue.PublicCardId == card.Id && cue.CardKind == CardKind.Slash);
                    reflow = AssertReflowStart(beforeRemoval, card.Id, items, panel, layer, "recorded-use");
                    committedViews = SafeViews(game);
                    Advance(.012);
                }
                else if (frame > 0) Advance(.04);
                if (frame == 3)
                {
                    Pointer.Invoke(panel, [point]); Bindings(root);
                    Assert(surface.IsPointerOver && ReferenceEquals(Resolve.Invoke(panel, [point]), button),
                        "The recording did not hover the actual physical hand slot.");
                }
                var bitmap = Capture();
                if (frame == 4) Assert(Y(surface) is < 0 and > -7, "The recorded native hover skipped its 40 ms intermediate pose.");
                if (frame == 7) Assert(Math.Abs(Y(surface) + 7) < .001, "The recorded native hover never settled.");
                if (frame < 9)
                    Assert(game.AcceptedCommands.Count == acceptedBefore && SameViews(game, draftViews),
                        "Recording a hand draft changed a command or player-safe view.");
                else
                    Assert(game.AcceptedCommands.Count == acceptedBefore + 1 && committedViews is not null && SameViews(game, committedViews),
                        "Recording the committed flight changed its command or any player-safe view.");
                if (frame == 9)
                {
                    var launch = FaceBounds(layer, CardKind.Slash).Single();
                    Assert(Near(launch, new Rect(origin.X + 1, origin.Y + 1, origin.Width - 2, origin.Height - 2)),
                        "The recorded first flight did not begin at its actual 50 ms hand pose.");
                }
                if (frame is 10 or 12) AssertReflowMiddle(reflow!, layer, "recorded-use");
                if (frame == 14) AssertReflowSettled(reflow!, layer, "recorded-use");
                if (frame is 7 or 9 or 10 or 14 or 51)
                    host.Capture(output, $"hand-reflow-native-use-frame-{frame:D2}", 1120);
                if (frame == 51)
                {
                    var landing = Find<Border>(root).Single(border => BattleFeedbackLayer.GetPlayAnchor(border) == cue!.Sequence);
                    Assert(!FaceBounds(layer, CardKind.Slash).Any() && !BattleFeedbackLayer.GetIsFlightPending(landing) &&
                        ((FrameworkElement)landing.Parent).Opacity > .99,
                        "The recorded native face did not land and retire into its existing public card.");
                }
                var metadata = new BitmapMetadata("gif");
                if (frame == 0)
                {
                    metadata.SetQuery("/appext/application", System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                    metadata.SetQuery("/appext/data", new byte[] { 3, 1, 0, 0, 0 });
                }
                encoder.Frames.Add(BitmapFrame.Create(new TransformedBitmap(bitmap, new ScaleTransform(.65, .65)), null, metadata, null));
            }
            var path = Path.Combine(output, "hand-reflow-native-use.gif");
            using (var file = File.Create(path)) encoder.Save(file);
            BattleEffectsChecks.SetGifTiming(path, 4);
            using var recorded = File.OpenRead(path);
            var decoder = new GifBitmapDecoder(recorded, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert(decoder.Frames.Count == 52 && decoder.Frames.All(frame => frame.PixelWidth == 728 && frame.PixelHeight == 481 &&
                frame.Metadata is BitmapMetadata data && (ushort)data.GetQuery("/grctlext/Delay") == 4),
                "The native lift recording lost its 52 physical frames, compact size or 40 ms timing.");
            Assert(!DecodedPixels(3).SequenceEqual(DecodedPixels(4)) && !DecodedPixels(7).SequenceEqual(DecodedPixels(8)) &&
                !DecodedPixels(9).SequenceEqual(DecodedPixels(13)),
                "The encoded native hover, selection or committed flight lost its actual moving frames.");
            Console.WriteLine($"Native hand reflow recording: physical {card.Id}/{card.Kind}, 50 ms Y={confirmY:F3}, origin={origin}, " +
                $"one command {acceptedBefore}->{game.AcceptedCommands.Count}; {decoder.Frames.Count} actual 40 ms frames, 728x481, {committedViews!.Length} safe views unchanged.");

            byte[] DecodedPixels(int index)
            {
                var frame = new FormatConvertedBitmap(decoder.Frames[index], PixelFormats.Pbgra32, null, 0);
                var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
                frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
                return pixels;
            }
        }
        finally
        {
            foreach (var surface in Find<CardLiftSurface>(root)) surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }

        void Advance(double seconds)
        {
            clock.Advance(seconds);
            foreach (var surface in Find<CardLiftSurface>(root)) Refresh.Invoke(surface, null);
            layer.InvalidateVisual();
        }
        RenderTargetBitmap Capture()
        {
            foreach (var surface in Find<CardLiftSurface>(root))
            {
                surface.Clock = clock;
                Refresh.Invoke(surface, null);
                surface.InvalidateVisual();
            }
            layer.InvalidateVisual();
            root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout();
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            Assert(root.RenderSize == new Size(1120, 740) && window.IsLoaded && window.IsVisible && root.IsVisible,
                "The actual native hand reflow movie lost its loaded 1120x740 root.");
            var bitmap = new RenderTargetBitmap(1120, 740, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            return bitmap;
        }
    }

    private static void Bindings(FrameworkElement root) => root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
    private static double Y(CardLiftSurface surface) => ((TranslateTransform)surface.RenderTransform).Y;
    private static double X(CardLiftSurface surface) => (double)Offset.GetValue(surface)!;
    private static bool ReflowMoving(CardLiftSurface surface) => (bool)Reflowing.GetValue(surface)!;
    private static bool ArrivalMoving(CardLiftSurface surface) => (bool)Arriving.GetValue(surface)!;
    private static double ArrivalOpacity(CardLiftSurface surface) => (double)ArrivalAlpha.GetValue(surface)!;
    private static Rect Bounds(FrameworkElement element, Visual relative) => element.TransformToVisual(relative).TransformBounds(new Rect(element.RenderSize));
    private static void RefreshAll(FrameworkElement root)
    {
        foreach (var surface in Find<CardLiftSurface>(root)) Refresh.Invoke(surface, null);
    }
    private static bool Moving(CardLiftSurface surface) => (bool)Animating.GetValue(surface)!;
    private static bool RunningTimer(CardLiftSurface surface) => (bool)Timer.GetValue(surface)!;
    private static string[] SafeViews(GameEngine game) => Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
        .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
    private static bool SameViews(GameEngine game, string[] views) => Enumerable.Range(0, views.Length)
        .All(seat => views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat)));
    private static bool Near(Rect first, Rect second) => Math.Abs(first.X - second.X) < .1 && Math.Abs(first.Y - second.Y) < .1 &&
        Math.Abs(first.Width - second.Width) < .1 && Math.Abs(first.Height - second.Height) < .1;
    private static IEnumerable<Rect> FaceBounds(BattleFeedbackLayer layer, CardKind? kind) =>
        Images(VisualTreeHelper.GetDrawing(layer), Matrix.Identity).Where(item => ReferenceEquals(item.Image.ImageSource, CardArt.Get(kind))).Select(item => item.Bounds);
    private static IEnumerable<(ImageDrawing Image, Rect Bounds, double Opacity)> Images(Drawing? drawing, Matrix transform, double opacity = 1)
    {
        if (drawing is ImageDrawing image) yield return (image, new MatrixTransform(transform).TransformBounds(image.Rect), opacity);
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            foreach (var child in group.Children) foreach (var item in Images(child, next, opacity * group.Opacity)) yield return item;
        }
    }
}
