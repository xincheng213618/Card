using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class HandPointerChecks
{
    public static void HoverFollowsLayout(string output)
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore())
        {
            IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false
        };
        Program.StartLordFixture(vm);
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        var revision = Program.Engine(vm).Revision;
        var window = new CardGame.Wpf.MainWindow(vm);
        try
        {
            var root = (FrameworkElement)window.Content;
            vm.Hand.Clear();
            for (var index = 0; index < 22; index++) vm.Hand.Add(new CardViewModel
            {
                Id = 9000 + index, Kind = CardKind.Slash, Name = "杀", KindLabel = "基本牌",
                SuitGlyph = "♠", Rank = (22 - index).ToString("D2"), IsPlayable = true,
                Description = "手牌排序与尺寸变化后的悬停检查"
            });
            Layout(1120);
            var items = (ItemsControl)window.FindName("HandCards");
            var viewport = (HandScrollViewer)window.FindName("HandViewport");
            var panel = Program.Find<HandPanel>(items).Single();
            var move = typeof(HandPanel).GetMethod("UpdatePointer", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var resolver = typeof(HandPanel).GetMethod("CardButtonAt", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var point = new Point(160, 80);
            move.Invoke(panel, [point]);
            var original = (Button)resolver.Invoke(panel, [point])!;
            var originalCard = (CardViewModel)original.DataContext;
            vm.SelectCardCommand.Execute(originalCard);
            Layout(1120);
            Program.Assert(HandPanel.GetIsPointerCard(original) && originalCard.IsSelected && viewport.HorizontalOffset == 0,
                "The hovered card must start selected at its original physical slot.");

            vm.SortHandCommand.Execute(null);
            Layout(1120);
            var current = (Button)resolver.Invoke(panel, [point])!;
            Program.Assert(!ReferenceEquals(current, original) && !HandPanel.GetIsPointerCard(original) &&
                           HandPanel.GetIsPointerCard(current) && originalCard.IsSelected && viewport.HorizontalOffset == 0,
                "Sorting must move the hover to the card now beneath the stationary pointer and preserve the draft.");
            Program.Render(root, 1120, 740, Path.Combine(output, "hand-sort-stationary-pointer.png"));

            // A wider viewport changes the overlap spacing without a mouse move.
            Layout(2000);
            var resized = (Button)resolver.Invoke(panel, [point])!;
            Program.Assert(!ReferenceEquals(current, resized) && !HandPanel.GetIsPointerCard(current) &&
                           HandPanel.GetIsPointerCard(resized) && originalCard.IsSelected,
                "Resizing must align the lifted card with its new physical slot without changing selection.");

            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Program.Assert(!HandPanel.GetIsPointerCard(resized),
                "Leaving the table must retire its pointer highlight.");
            Layout(1120);
            Program.Assert(Program.Find<Button>(items).All(button => !HandPanel.GetIsPointerCard(button)) &&
                           originalCard.IsSelected && Program.Engine(vm).Revision == revision,
                "A retired pointer must not reappear on layout or change the match and preserved draft.");

            VerifyReflowPointer(vm, root, items, panel, viewport, Layout, output);

            void Layout(int width)
            {
                root.Measure(new Size(width, 740));
                root.Arrange(new Rect(0, 0, width, 740));
                root.UpdateLayout();
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                root.UpdateLayout();
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void VerifyReflowPointer(MainViewModel vm, FrameworkElement root, ItemsControl items, HandPanel panel,
        HandScrollViewer viewport, Action<int> layout, string output)
    {
        const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
        var move = typeof(HandPanel).GetMethod("UpdatePointer", access)!;
        var resolver = typeof(HandPanel).GetMethod("CardButtonAt", access)!;
        var press = typeof(HandPanel).GetMethod("PressCardAt", access)!;
        var refresh = typeof(CardLiftSurface).GetMethod("RefreshMotion", access)!;
        var offset = typeof(CardLiftSurface).GetProperty("HorizontalOffset", access)!;
        var moving = typeof(CardLiftSurface).GetProperty("IsReflowAnimating", access)!;
        var timer = typeof(CardLiftSurface).GetProperty("IsFrameTimerRunning", access)!;
        var game = Program.Engine(vm);
        var revision = game.Revision;
        var accepted = game.AcceptedCommands.Count;
        var views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
            .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();

        // Reuse this check's control-only hand. Four wide slots expose an actual
        // moving gap; the 22-card overflow case above has overlapping strips.
        vm.ClearSelectionCommand.Execute(null);
        while (vm.Hand.Count > 4) vm.Hand.RemoveAt(vm.Hand.Count - 1);
        vm.Hand.Move(0, vm.Hand.Count - 1); // Ensure the later real sort changes order.
        layout(1120);
        panel.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        var cards = vm.Hand.ToArray();
        var containers = cards.ToDictionary(card => card, Container);
        var buttons = cards.ToDictionary(card => card, Button);
        var clock = new ReflowClock();
        vm.IsMotionEnabled = true;
        layout(1120);
        InjectClock();
        var before = cards.ToDictionary(card => card, VisibleX);
        var survivor = cards[2];
        var last = cards[3];
        var stationary = new Point(before[survivor] - 20, 80);
        move.Invoke(panel, [stationary]);
        vm.Hand.Remove(cards[1]);
        layout(1120);
        Program.Assert(vm.Hand.All(card => ReferenceEquals(containers[card], Container(card))) &&
                       Math.Abs(VisibleX(survivor) - before[survivor]) < .01 && IsMoving(Surface(survivor)),
            "Removing a middle hand card recreated survivors or jumped their visible faces straight to their final slots.");
        Program.Assert(resolver.Invoke(panel, [stationary]) is null && press.Invoke(panel, [stationary]) is false &&
                       !vm.Hand.Any(card => HandPanel.GetIsPointerCard(Button(card))),
            "An animated gap captured a final-slot card which had not yet moved beneath the pointer.");
        var visiblePress = new Point(VisibleX(survivor) + 2, 80);
        Program.Assert(ReferenceEquals(resolver.Invoke(panel, [visiblePress]), buttons[survivor]) &&
                       press.Invoke(panel, [visiblePress]) is true && survivor.IsSelected && !last.IsSelected &&
                       Math.Abs(VisibleX(survivor) - before[survivor]) < .01,
            "Clicking the moving visible strip selected its final-slot neighbor or snapped the hand before resolving input.");
        vm.ClearSelectionCommand.Execute(null);
        move.Invoke(panel, [stationary]);
        Program.Render(root, 1120, 740, Path.Combine(output, "hand-reflow-pointer-start.png"));
        Advance(.045);
        var middle = VisibleX(survivor);
        Program.Assert(middle < before[survivor] && middle > SlotX(survivor) &&
                       ReferenceEquals(resolver.Invoke(panel, [stationary]), buttons[survivor]) &&
                       HandPanel.GetIsPointerCard(buttons[survivor]) &&
                       vm.Hand.Count(card => HandPanel.GetIsPointerCard(Button(card))) == 1,
            "The shrinking gap did not continuously move the actual card and its hover beneath a stationary pointer.");
        panel.InvalidateArrange(); layout(1120);
        Program.Assert(Math.Abs(VisibleX(survivor) - middle) < .01 && IsMoving(Surface(survivor)),
            "An unchanged arrange restarted or finished the in-progress hand reflow.");
        Program.Render(root, 1120, 740, Path.Combine(output, "hand-reflow-pointer-middle.png"));
        Advance(.045);
        var secondStart = vm.Hand.ToDictionary(card => card, VisibleX);
        vm.Hand.Remove(cards[0]);
        layout(1120);
        Program.Assert(vm.Hand.All(card => Math.Abs(VisibleX(card) - secondStart[card]) < .01 &&
                       ReferenceEquals(containers[card], Container(card))),
            "A second removal during reflow jumped from the previous destination instead of its current visible pose.");
        Advance(.045);
        Program.Assert(VisibleX(survivor) < secondStart[survivor] && VisibleX(survivor) > SlotX(survivor) &&
                       ReferenceEquals(resolver.Invoke(panel, [stationary]), buttons[last]) && HandPanel.GetIsPointerCard(buttons[last]),
            "A second closing gap failed to update the stationary hover to the card actually crossing that point.");
        Program.Render(root, 1120, 740, Path.Combine(output, "hand-reflow-pointer-second-middle.png"));
        vm.SortHandCommand.Execute(null);
        layout(1120);
        Program.Assert(ReferenceEquals(vm.Hand[0], last) && Settled(),
            "Sorting an actually reordered hand retained an old reflow displacement.");

        InjectClock();
        vm.Hand.RemoveAt(0); layout(1120); Advance(.045);
        Program.Assert(IsMoving(Surface(survivor)), "The same-arrange deadline boundary did not begin with an actual moving survivor.");
        panel.InvalidateArrange(); layout(1120);
        Advance(.14);
        Program.Assert(Settled(), "Repeated same-count arrangement restarted reflow instead of ending on its original deadline.");

        vm.Hand.Add(last); vm.Hand.Move(1, 0); layout(1120); InjectClock();
        // Inserting before the sole survivor may itself move it right. Finish
        // that setup transition before removing the front card, so the resize
        // boundary starts with a genuine nonzero leftward displacement.
        Advance(.2);
        Program.Assert(Settled(), "The resize setup did not finish its preceding insertion reflow.");
        vm.Hand.RemoveAt(0); layout(1120); Advance(.045);
        Program.Assert(IsMoving(Surface(survivor)), "The resize boundary did not begin with an actual moving survivor.");
        layout(1920);
        Program.Assert(Settled(), "Resizing retained a hand displacement measured against the old viewport.");

        vm.Hand.Add(cards[0]); layout(1120); InjectClock();
        vm.Hand.RemoveAt(0); layout(1120); Advance(.045);
        Program.Assert(IsMoving(Surface(cards[0])), "The motion-off boundary did not begin with an actual closing gap.");
        vm.IsMotionEnabled = false; layout(1120);
        Program.Assert(Settled(), "Motion-off failed to settle horizontal hand motion immediately.");
        Advance(.3);
        vm.IsMotionEnabled = true; layout(1120);
        Program.Assert(Settled(), "Motion-on replayed an old hand reflow instead of retaining the final slots.");

        vm.Hand.Add(last); layout(1120); InjectClock();
        vm.Hand.RemoveAt(0); layout(1120); Advance(.045);
        Program.Assert(IsMoving(Surface(last)), "The reset boundary did not begin with an actual moving old card.");
        var replacement = new CardViewModel
        {
            Id = last.Id, Kind = last.Kind, Name = last.Name, KindLabel = last.KindLabel,
            SuitGlyph = last.SuitGlyph, Rank = last.Rank, IsPlayable = true, Description = last.Description
        };
        vm.Hand.Clear(); vm.Hand.Add(replacement); layout(1120); InjectClock();
        Program.Assert(!ReferenceEquals(last, vm.Hand[0]) && Settled() && Math.Abs(VisibleX(replacement) - SlotX(replacement)) < .01,
            "Resetting the hand reused an old pose merely because a new card model had the same physical ID.");
        Program.Assert(viewport.HorizontalOffset == 0 && game.Revision == revision && game.AcceptedCommands.Count == accepted &&
                       Enumerable.Range(0, views.Length).All(seat => views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
            "Control-only removal, pointer routing or motion boundaries scrolled the hand or changed gameplay/player-safe views.");
        Console.WriteLine($"Hand reflow pointer: middle gap/current-strip click, stationary hover, continuous second removal, " +
                          $"same-layout preservation, sort/resize/motion/reset boundaries; accepted={accepted}, safe views={views.Length} unchanged.");

        VerifyArrivalPointer(vm, root, items, panel, viewport, cards, output);

        FrameworkElement Container(CardViewModel card) => (FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(card);
        Button Button(CardViewModel card) => Program.Find<Button>(Container(card)).Single(button => button.Name == "CardButton");
        CardLiftSurface Surface(CardViewModel card) => Program.Find<CardLiftSurface>(Container(card)).Single();
        double SlotX(CardViewModel card) => Container(card).TransformToAncestor(panel).TransformBounds(new Rect(Container(card).RenderSize)).Left;
        double VisibleX(CardViewModel card) => SlotX(card) + (double)offset.GetValue(Surface(card))!;
        bool IsMoving(CardLiftSurface surface) => (bool)moving.GetValue(surface)!;
        bool Settled() => vm.Hand.All(card => !IsMoving(Surface(card)) && Math.Abs((double)offset.GetValue(Surface(card))!) < .01 &&
                                            !(bool)timer.GetValue(Surface(card))!);
        void InjectClock()
        {
            clock = new ReflowClock();
            // This unshown control fixture has no native Window load pass to
            // reactivate a presenter after ItemsControl moves/reuses it. Drain
            // its queued lifecycle events, then establish a component Loaded
            // boundary with the controlled clock before the next removal.
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            foreach (var surface in Program.Find<CardLiftSurface>(items))
            {
                surface.Clock = clock;
                surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            }
            Program.Assert(Program.Find<CardLiftSurface>(items).All(surface => !(bool)timer.GetValue(surface)!),
                "Loading a component with an injected reflow clock started a real dispatcher frame timer.");
        }
        void Advance(double seconds)
        {
            clock.Advance(seconds);
            foreach (var surface in Program.Find<CardLiftSurface>(items)) refresh.Invoke(surface, null);
            root.UpdateLayout();
            Program.Assert(Program.Find<CardLiftSurface>(items).All(surface => !(bool)timer.GetValue(surface)!),
                "An injected hand-reflow clock started a real dispatcher frame timer.");
        }
    }

    private static void VerifyArrivalPointer(MainViewModel vm, FrameworkElement root, ItemsControl items, HandPanel panel,
        HandScrollViewer viewport, CardViewModel[] cards, string output)
    {
        const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
        var refresh = typeof(CardLiftSurface).GetMethod("RefreshMotion", access)!;
        var arriving = typeof(CardLiftSurface).GetProperty("IsArrivalAnimating", access)!;
        var alpha = typeof(CardLiftSurface).GetProperty("ArrivalOpacity", access)!;
        var timer = typeof(CardLiftSurface).GetProperty("IsFrameTimerRunning", access)!;
        var resolver = typeof(HandPanel).GetMethod("CardButtonAt", access)!;
        var press = typeof(HandPanel).GetMethod("PressCardAt", access)!;
        var game = Program.Engine(vm);
        var actual = game.CreateSnapshot(0);
        var revision = game.Revision;
        var accepted = game.AcceptedCommands.Count;
        var views = Enumerable.Range(0, actual.Players.Count)
            .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        var clock = new ReflowClock();
        var loadedSurfaces = new HashSet<CardLiftSurface>();
        var presentationRevision = revision + 100;

        // These small record copies exercise component snapshot/collection
        // handoff only. They never alter the engine or claim a committed gain.
        Prime(cards[0], cards[1]);
        Program.Assert(Static(), "The first component hand layout replayed an initial deal.");
        var retained = vm.Hand.ToDictionary(card => card, Container);
        Publish(vm.Hand, ++presentationRevision); Layout();
        Program.Assert(Static(), "A forward component snapshot with no new IDs replayed the hand.");
        Publish([.. vm.Hand, cards[2]], presentationRevision);
        vm.Hand.Add(cards[2]); Layout();
        Program.Assert(Static(), "A same-revision component addition was treated as a committed arrival.");
        Advance(.3);

        // The authoritative snapshot can reach layout before its new item.
        Publish(cards, ++presentationRevision); Layout();
        Program.Assert(Static(), "A pending incoming ID animated an unrelated existing card.");
        cards[3].IsPlayable = false;
        vm.Hand.Add(cards[3]); Layout();
        var unavailable = Surface(cards[3]);
        Console.WriteLine($"Hand arrival early-layout: animating={IsArriving(unavailable)}, alpha={Alpha(unavailable):F4}, " +
                          $"opacity={unavailable.Opacity:F4}, mask={unavailable.OpacityMask?.GetType().Name ?? "null"}, " +
                          $"enabled={panel.ArrivalEnabled}, revision={panel.HandSnapshot?.Revision}, owner={panel.HandSnapshot?.HumanSeat}, " +
                          $"motion={unavailable.MotionEnabled}, retired={SurfaceState(unavailable, "_retired")}, " +
                          $"surfaceLoaded={SurfaceState(unavailable, "_loaded")}, width={PanelState("_layoutWidth")}, " +
                          $"loaded={PanelState("_panelLoaded")}, baseline={PanelState("_hasHandBaseline")}, " +
                          $"awaiting={PanelState("_awaitingSnapshotAfterReset")}, pending=" +
                          $"[{string.Join(",", (IEnumerable<int>)PanelState("_pendingArrivalIds")!)}], retained=" +
                          string.Join(",", vm.Hand.Take(2).Select(card => $"{card.Id}:{ReferenceEquals(retained[card], Container(card))}")));
        Program.Assert(IsArriving(unavailable) && Alpha(unavailable) >= .71 && Alpha(unavailable) < 1 &&
                       unavailable.Opacity == .55 && unavailable.OpacityMask is not null &&
                       vm.Hand.Take(2).All(card => ReferenceEquals(retained[card], Container(card))),
            "Early layout lost the later incoming surface, rebuilt survivors or overwrote unavailable-card dimming.");
        Program.Render(root, 1120, 740, Path.Combine(output, "hand-arrival-component-unavailable.png"));
        Advance(.3);
        Program.Assert(Static() && unavailable.Opacity == .55 && unavailable.OpacityMask is null,
            "Completing arrival failed to remove its mask or restore the existing unavailable opacity.");
        cards[3].IsPlayable = true;

        Prime(cards[0], cards[1]);
        Gain(cards[2]);
        var incoming = Surface(cards[2]);
        var startAlpha = Alpha(incoming);
        var point = new Point(Container(cards[2]).TranslatePoint(new Point(0, 0), panel).X +
                              ((TranslateTransform)incoming.RenderTransform).X + 4, 80);
        Program.Assert(startAlpha >= .71 && startAlpha < 1 &&
                       ReferenceEquals(resolver.Invoke(panel, [point]), Button(cards[2])) &&
                       press.Invoke(panel, [point]) is true && cards[2].IsSelected && !cards[1].IsSelected &&
                       Math.Abs(Alpha(incoming) - startAlpha) < .001,
            "A visibly arriving card delayed input, selected its neighbor or restarted arrival on selection.");
        Advance(.09);
        var middleAlpha = Alpha(incoming);
        Program.Assert(middleAlpha > startAlpha && middleAlpha < 1 &&
                       ((TranslateTransform)incoming.RenderTransform).Y >= -18,
            "Arrival did not visibly progress alongside the independent selected-card lift.");
        panel.InvalidateArrange(); Layout();
        Program.Assert(Math.Abs(Alpha(incoming) - middleAlpha) < .001,
            "An unchanged arrangement restarted an arriving card.");
        Advance(.12);
        Program.Assert(Static(), "Selection or repeated arrangement extended the original arrival deadline.");

        Prime(); Gain(cards[0]);
        Program.Assert(IsArriving(Surface(cards[0])) && Alpha(Surface(cards[0])) >= .71,
            "A primed empty hand failed to animate its genuinely new component ID.");
        vm.IsMotionEnabled = false; Layout();
        Program.Assert(Static(), "Motion-off retained an arrival mask or animation.");
        vm.IsMotionEnabled = true; Layout();
        Program.Assert(Static(), "Motion-on replayed a previously finished arrival.");

        Prime(cards[0]); Gain(cards[1]);
        RequireArrival(cards[1], "cover");
        panel.ArrivalEnabled = false; Layout();
        Program.Assert(Static(), "Covering the table retained an incoming-card animation.");
        Gain(cards[2]);
        Program.Assert(Static(), "A covered table animated a newly published hand card.");
        panel.ArrivalEnabled = true; Layout();
        Program.Assert(Static(), "Uncovering the table replayed a gain already consumed while covered.");

        Prime(cards[1], cards[2], cards[3]); Gain(cards[0]);
        RequireArrival(cards[0], "sort");
        var beforeSort = vm.Hand.ToArray();
        vm.SortHandCommand.Execute(null); Layout();
        Program.Assert(!beforeSort.SequenceEqual(vm.Hand) && Static(),
            "An actual hand reorder retained old arrival motion.");
        Prime(cards[0]); Gain(cards[1]); RequireArrival(cards[1], "resize"); Layout(1920);
        Program.Assert(Static(), "Resize retained arrival geometry against an old viewport.");

        Prime(cards[0]); Gain(cards[1]);
        RequireArrival(cards[1], "collection reset");
        vm.Hand.Clear(); foreach (var card in cards.Take(2)) vm.Hand.Add(card);
        Layout();
        Program.Assert(Static(), "A collection reset without an empty intermediate layout replayed replacement cards.");
        // A reset waits for the replacement session's first authoritative
        // snapshot; it cannot treat that handoff itself as a fresh gain.
        Publish(vm.Hand, presentationRevision); Layout();
        Program.Assert(Static(), "The first post-reset snapshot replayed its replacement hand baseline.");
        Gain(cards[2]);
        RequireArrival(cards[2], "rollback");
        Publish(vm.Hand, --presentationRevision); Layout();
        Program.Assert(Static(), "A snapshot rollback retained an old arrival candidate or mask.");
        Gain(cards[3]);
        RequireArrival(cards[3], "owner switch");
        Publish(vm.Hand, ++presentationRevision, owner: 1); Layout();
        Program.Assert(Static(), "A local hand-owner switch replayed cards from the previous owner's presentation.");

        Prime(cards[0]);
        // Even an exposed foreign hand in a diagnostic snapshot is not local
        // authority for this component's still-visible synthetic hand item.
        var foreign = Snapshot(vm.Hand, ++presentationRevision);
        foreign = foreign with { Players = foreign.Players.Select(player => player.Seat == 1
            ? player with { Hand = [Physical(cards[1])], HandCount = 1 } : player).ToArray() };
        panel.HandSnapshot = foreign; vm.Hand.Add(cards[1]); Layout();
        Program.Assert(Static(), "A foreign player's card ID triggered a local hand arrival.");

        Prime(cards[0]); Gain(cards[1]);
        RequireArrival(cards[1], "unload");
        foreach (var surface in Surfaces()) surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Gain(cards[2]);
        panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        foreach (var surface in Surfaces()) surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Layout();
        Program.Assert(Static(), "Reloading a component replayed arrival history observed while unloaded.");
        Program.Assert(viewport.HorizontalOffset == 0 && game.Revision == revision && game.AcceptedCommands.Count == accepted &&
                       Enumerable.Range(0, views.Length).All(seat => views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
            "Component arrival snapshots, pointer selection or lifecycle boundaries changed gameplay or player-safe views.");
        Console.WriteLine($"Hand arrival component: cold/same-revision/no-delta/early-layout/empty/input/mask/" +
                          $"motion/covered/sort/resize/reset/rollback/owner/foreign/unload; accepted={accepted}, safe views={views.Length} unchanged.");

        CardSnapshot Physical(CardViewModel card) => new(card.Id, card.Kind!.Value, Suit.Spade,
            int.Parse(card.Rank), card.Name, card.Rank);
        GameSnapshot Snapshot(IEnumerable<CardViewModel> hand, long nextRevision, int owner = 0)
        {
            var physical = hand.Select(Physical).ToArray();
            return actual with { HumanSeat = owner, Revision = nextRevision,
                Players = actual.Players.Select(player => player with { IsHuman = player.Seat == owner,
                    Hand = player.Seat == owner ? physical : [], HandCount = player.Seat == owner ? physical.Length : 0,
                    WoodenOxGrain = [], WoodenOxGrainCount = 0 }).ToArray() };
        }
        void Publish(IEnumerable<CardViewModel> hand, long nextRevision, int owner = 0)
            => panel.HandSnapshot = Snapshot(hand, nextRevision, owner);
        FrameworkElement Container(CardViewModel card) => (FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(card);
        Button Button(CardViewModel card) => Program.Find<Button>(Container(card)).Single(button => button.Name == "CardButton");
        CardLiftSurface Surface(CardViewModel card) => Program.Find<CardLiftSurface>(Container(card)).Single();
        CardLiftSurface[] Surfaces() => Program.Find<CardLiftSurface>(items).ToArray();
        object? PanelState(string field) => typeof(HandPanel).GetField(field, access)!.GetValue(panel);
        object? SurfaceState(CardLiftSurface surface, string field) => typeof(CardLiftSurface).GetField(field, access)!.GetValue(surface);
        bool IsArriving(CardLiftSurface surface) => (bool)arriving.GetValue(surface)!;
        double Alpha(CardLiftSurface surface) => (double)alpha.GetValue(surface)!;
        bool Static() => Surfaces().All(surface => !IsArriving(surface) && Math.Abs(Alpha(surface) - 1) < .001 &&
                                                   !(bool)timer.GetValue(surface)!);
        void RequireArrival(CardViewModel card, string boundary) => Program.Assert(IsArriving(Surface(card)) && Alpha(Surface(card)) < 1,
            $"The {boundary} boundary did not begin with an actually arriving component card.");
        void Prime(params CardViewModel[] hand)
        {
            vm.IsMotionEnabled = false; vm.ClearSelectionCommand.Execute(null);
            foreach (var surface in Surfaces()) surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            panel.HandSnapshot = null;
            vm.Hand.Clear(); foreach (var card in hand) vm.Hand.Add(card);
            clock = new ReflowClock(); panel.Clock = clock; loadedSurfaces.Clear(); panel.ArrivalEnabled = true;
            Publish(hand, ++presentationRevision); Layout();
            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            vm.IsMotionEnabled = true; Layout();
            Program.Assert(Static(), "Priming a new component baseline animated its opening hand.");
        }
        void Gain(CardViewModel card)
        {
            Publish([.. vm.Hand, card], ++presentationRevision);
            vm.Hand.Add(card); Layout();
        }
        void Layout(int width = 1120)
        {
            panel.Clock = clock;
            root.Measure(new Size(width, 740));
            foreach (var surface in Surfaces())
            {
                surface.Clock = clock;
                if (loadedSurfaces.Add(surface)) surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            }
            root.Arrange(new Rect(0, 0, width, 740)); root.UpdateLayout();
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); root.UpdateLayout();
        }
        void Advance(double seconds)
        {
            clock.Advance(seconds);
            foreach (var surface in Surfaces()) refresh.Invoke(surface, null);
            root.UpdateLayout();
        }
    }

    public static void OverlapAndPointer(string output)
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore());
        Program.StartLordFixture(vm);
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        var window = new CardGame.Wpf.MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        var revision = Program.Engine(vm).Revision;
        vm.Hand.Clear();
        for (var index = 0; index < 22; index++) vm.Hand.Add(new CardViewModel
        {
            Id = 9000 + index, Kind = CardKind.Slash, Name = "杀", KindLabel = "基本牌",
            SuitGlyph = "♠", Rank = (index % 9 + 1).ToString(), Description = "手牌重叠布局检查",
            IsPlayable = true
        });
        vm.Hand[2].IsSelected = true;
        Program.Render(root, 1120, 740, Path.Combine(output, "hand-pointer-before.png"));
        var items = (ItemsControl)window.FindName("HandCards");
        var panel = Program.Find<HandPanel>(items).Single();
        var next = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(3);
        var point = next.TranslatePoint(new Point(8, 80), panel);
        var move = typeof(HandPanel).GetMethod("UpdatePointer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        move.Invoke(panel, [point]);
        root.UpdateLayout();
        var hit = VisualTreeHelper.HitTest(panel, point)?.VisualHit;
        while (hit is not null && hit is not Button) hit = VisualTreeHelper.GetParent(hit);
        var card = (hit as FrameworkElement)?.DataContext as CardViewModel;
        Console.WriteLine($"Overlap pointer: intended={vm.Hand[3].Id}, visual hit={card?.Id}");
        Program.Assert(card?.Id == vm.Hand[3].Id, "An elevated selected card intercepted the adjacent card's pointer region.");
        var press = typeof(HandPanel).GetMethod("PressCardAt", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Program.Assert(press.Invoke(panel, [point]) is true && vm.Hand[3].IsSelected && !vm.Hand[2].IsSelected,
            "A press selected the elevated neighbor instead of the pointer's physical card.");
        var resolver = typeof(HandPanel).GetMethod("CardButtonAt", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var button = (Button)resolver.Invoke(panel, [point])!;
        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        Program.Assert(vm.Hand[3].IsSelected,
            "Mouse release selected the same card again after the press had already selected it.");
        for (var repeat = 0; repeat < 5; repeat++)
        {
            move.Invoke(panel, [point]);
            root.UpdateLayout();
            Program.Assert(ReferenceEquals(button, resolver.Invoke(panel, [point])),
                "Lifting a hovered card changed the pointer's slot.");
        }
        vm.ClearSelectionCommand.Execute(null);
        vm.Hand[3].IsPlayable = false;
        root.UpdateLayout();
        Program.Assert(press.Invoke(panel, [point]) is true && !vm.HasSelection,
            "An unavailable card selected itself or allowed the press to reach a covered card.");
        Program.Assert(press.Invoke(panel, [new Point(-10, 80)]) is false && !vm.HasSelection,
            "An empty hand margin selected a card.");
        vm.Hand[3].IsPlayable = true;
        press.Invoke(panel, [point]);
        Program.Render(root, 1120, 740, Path.Combine(output, "hand-pointer-after.png"));
        var viewport = (HandScrollViewer)window.FindName("HandViewport");
        var presenter = Program.Find<ScrollContentPresenter>(viewport).Single();
        var first = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(0);
        var face = Program.Find<Grid>(first).Single(element => element.Name == "CardSurface");
        var faceBounds = face.TransformToAncestor(presenter).TransformBounds(new Rect(face.RenderSize));
        Console.WriteLine($"Hand viewport: face bottom={faceBounds.Bottom:F1}, visible height={presenter.ActualHeight:F1}");
        Program.Assert(faceBounds.Bottom <= presenter.ActualHeight + .5,
            "The horizontal scrollbar clips the lower edge of unselected hand cards.");
        vm.ClearSelectionCommand.Execute(null);
        viewport.ScrollToLeftEnd();
        root.UpdateLayout();
        var edgePoint = new Point(presenter.ActualWidth - 8, 80);
        var edgeButton = (Button)resolver.Invoke(panel, [presenter.TranslatePoint(edgePoint, panel)])!;
        var edgeCard = (CardViewModel)edgeButton.DataContext;
        var offset = viewport.HorizontalOffset;
        Program.Assert(press.Invoke(panel, [presenter.TranslatePoint(edgePoint, panel)]) is true && edgeCard.IsSelected,
            "The visible right-edge strip must select its physical card.");
        edgeButton.BringIntoView(); // Focus also requests visibility through this routed event.
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        root.UpdateLayout();
        var afterEdge = (Button)resolver.Invoke(panel, [presenter.TranslatePoint(edgePoint, panel)])!;
        Console.WriteLine($"Hand edge selection: offset {offset:F1} -> {viewport.HorizontalOffset:F1}; " +
                          $"intended={edgeCard.Id}, next press={((CardViewModel)afterEdge.DataContext).Id}");
        Program.Assert(viewport.HorizontalOffset == offset && ReferenceEquals(edgeButton, afterEdge),
            "Auto-revealing a mouse-selected edge card moved the next press onto a different physical card.");
        vm.ClearSelectionCommand.Execute(null);
        move.Invoke(panel, [new Point(-10, 80)]);
        root.UpdateLayout();
        vm.SelectCardCommand.Execute(vm.Hand.Last());
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        root.UpdateLayout();
        var keyboardTail = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(vm.Hand.Count - 1);
        var keyboardBounds = keyboardTail.TransformToAncestor(presenter).TransformBounds(new Rect(keyboardTail.RenderSize));
        Program.Assert(viewport.HorizontalOffset > 0 && keyboardBounds.Left >= -.5 &&
                       keyboardBounds.Right <= presenter.ActualWidth + .5,
            "Keyboard selection stopped revealing an offscreen card in full.");
        vm.ClearSelectionCommand.Execute(null);
        viewport.ScrollToRightEnd();
        root.UpdateLayout();
        Program.Assert(viewport.HorizontalOffset > 0, "The overlap fixture did not exercise horizontal scrolling.");
        var tail = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(vm.Hand.Count - 1);
        var screenPoint = tail.TranslatePoint(new Point(50, 80), viewport);
        var scrolledPoint = viewport.TranslatePoint(screenPoint, panel);
        move.Invoke(panel, [scrolledPoint]);
        root.UpdateLayout();
        Program.Assert(((Button)resolver.Invoke(panel, [scrolledPoint])!).DataContext == vm.Hand.Last() &&
                       press.Invoke(panel, [scrolledPoint]) is true && vm.Hand.Last().IsSelected,
            "Scrolling changed the physical card resolved beneath the pointer.");
        try
        {
            VerifySortedPointerAndKeyboard(vm, window, output);
            vm.Hand.Clear();
            root.UpdateLayout();
            Program.Assert(resolver.Invoke(panel, [scrolledPoint]) is null &&
                           press.Invoke(panel, [scrolledPoint]) is false,
                "An empty hand retained an old pointer target.");
            Program.Assert(Program.Engine(vm).Revision == revision, "Pointer inspection changed the match.");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void VerifySortedPointerAndKeyboard(MainViewModel vm, CardGame.Wpf.MainWindow window, string output)
    {
        vm.IsMusicEnabled = false;
        vm.IsSoundEnabled = false;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        window.Top = -30000;
        window.Width = 1120;
        window.Height = 820;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        ShowWindow(handle, 0);
        Pump();
        Program.Assert(window.IsLoaded && window.IsVisible && !IsWindowVisible(handle) && GetForegroundWindow() != handle,
            $"The hand keyboard fixture must load its hidden Window: loaded={window.IsLoaded}, " +
            $"WPF visible={window.IsVisible}, native visible={IsWindowVisible(handle)}, owns foreground={GetForegroundWindow() == handle}.");

        var items = (ItemsControl)window.FindName("HandCards");
        var viewport = (HandScrollViewer)window.FindName("HandViewport");
        var presenter = Program.Find<ScrollContentPresenter>(viewport).Single();
        var panel = Program.Find<HandPanel>(items).Single();
        var move = typeof(HandPanel).GetMethod("UpdatePointer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var card = vm.Hand.First(item => item.Rank == "9");
        var beforeIndex = vm.Hand.IndexOf(card);
        vm.ClearSelectionCommand.Execute(null);
        viewport.ScrollToLeftEnd();
        Pump();
        var beforeContainer = (FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(card);
        var point = beforeContainer.TranslatePoint(new Point(8, 80), panel);
        var beforeBounds = beforeContainer.TransformToAncestor(presenter).TransformBounds(new Rect(beforeContainer.RenderSize));
        Program.Assert(beforeBounds.Left >= 0 && beforeBounds.Right <= presenter.ActualWidth,
            "The sorting fixture must begin with its rank-nine card fully visible.");
        move.Invoke(panel, [point]);
        var beforeButton = Program.Find<Button>(beforeContainer).Single();
        Program.Assert(HandPanel.GetIsPointerCard(beforeButton) && ReferenceEquals(beforeButton.CommandParameter, card),
            "The sorting fixture must begin with its actual published button under the pointer.");

        vm.SortHandCommand.Execute(null);
        Pump();
        var sortedIndex = vm.Hand.IndexOf(card);
        var container = (FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(card);
        var button = Program.Find<Button>(container).Single();
        var sortedBounds = container.TransformToAncestor(presenter).TransformBounds(new Rect(container.RenderSize));
        Program.Assert(sortedIndex > beforeIndex && sortedBounds.Left >= presenter.ActualWidth &&
                       viewport.HorizontalOffset == 0 && ReferenceEquals(button.CommandParameter, card),
            "The real hand-sort command must move the same published card beyond the viewport before keyboard activation.");
        Console.WriteLine($"Hand sort pointer: card={card.Id}, index {beforeIndex}->{sortedIndex}, " +
                          $"same button={ReferenceEquals(beforeButton, button)}, pointer={HandPanel.GetIsPointerCard(button)}.");
        panel.PreviewMouseMove += LogPointerEvent;
        panel.MouseLeave += LogPointerEvent;
        try
        {
            Program.Assert(button.Focus() && ReferenceEquals(Keyboard.FocusedElement, button),
                "The sorted hand card must receive actual keyboard focus in the loaded Window.");
            LogPointerState("focused");
            WaitForIdleInput("space-down");
            RaiseKey(Keyboard.PreviewKeyDownEvent, Keyboard.KeyDownEvent);
            LogPointerState("space-down");
            WaitForIdleInput("space-up");
            RaiseKey(Keyboard.PreviewKeyUpEvent, Keyboard.KeyUpEvent);
            LogPointerState("space-up");
            Pump();
            LogPointerState("pumped");
        }
        finally
        {
            panel.PreviewMouseMove -= LogPointerEvent;
            panel.MouseLeave -= LogPointerEvent;
            if (ReferenceEquals(Mouse.Captured, button)) button.ReleaseMouseCapture();
        }
        Program.Assert(card.IsSelected && vm.Hand.Count(item => item.IsSelected) == 1,
            "Space must activate the actual sorted hand button exactly once.");
        var root = (FrameworkElement)window.Content;
        Program.Render(root, (int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight),
            Path.Combine(output, "hand-sort-keyboard-after.png"));
        var keyboardBounds = container.TransformToAncestor(presenter).TransformBounds(new Rect(container.RenderSize));
        Console.WriteLine($"Hand sort keyboard: offset={viewport.HorizontalOffset:F1}, " +
                          $"bounds={keyboardBounds.Left:F1}..{keyboardBounds.Right:F1}, viewport={presenter.ActualWidth:F1}.");
        Program.Assert(viewport.HorizontalOffset > 0 && keyboardBounds.Left >= -.5 &&
                       keyboardBounds.Right <= presenter.ActualWidth + .5,
            "Sorting retained a pointer marker on an offscreen card and blocked its subsequent keyboard reveal.");

        void Pump()
        {
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.UpdateLayout();
        }

        void LogPointerState(string stage, bool? handled = null) => Console.WriteLine($"Hand sort {stage}: " +
            $"pointer={HandPanel.GetIsPointerCard(button)}, capture={button.IsMouseCaptured}, " +
            $"hand mouse-over={panel.IsMouseOver}, offset={viewport.HorizontalOffset:F1}, " +
            $"pressed={button.IsPressed}, left={Mouse.LeftButton}, {NativeLeftState()}, modifiers={Keyboard.Modifiers}, " +
            $"owns focus={ReferenceEquals(Keyboard.FocusedElement, button)}, handled={handled?.ToString() ?? "n/a"}.");

        void WaitForIdleInput(string stage)
        {
            LogPointerState($"{stage}-idle-before");
            var elapsed = Stopwatch.StartNew();
            while (!IsIdleInput() && elapsed.Elapsed < TimeSpan.FromSeconds(2))
            {
                Pump();
                if (!IsIdleInput() && elapsed.Elapsed < TimeSpan.FromSeconds(2)) Thread.Sleep(10);
            }
            LogPointerState($"{stage}-idle-after ({elapsed.ElapsedMilliseconds} ms)");
            Program.Assert(IsIdleInput(),
                $"The synthetic {stage} requires an idle input device within two seconds: " +
                $"left={Mouse.LeftButton}, {NativeLeftState()}, modifiers={Keyboard.Modifiers}, pressed={button.IsPressed}, " +
                $"owns focus={ReferenceEquals(Keyboard.FocusedElement, button)}, capture={button.IsMouseCaptured}.");
        }

        static bool IsIdleInput() => Mouse.LeftButton == MouseButtonState.Released &&
            (GetAsyncKeyState(0x01) & 0x8000) == 0 && Keyboard.Modifiers == ModifierKeys.None;

        static string NativeLeftState()
        {
            var queued = GetKeyState(0x01);
            var physical = GetAsyncKeyState(0x01);
            return $"key-state-left={((queued & 0x8000) == 0 ? "Released" : "Pressed")}({(ushort)queued:X4}), " +
                $"async-left={((physical & 0x8000) == 0 ? "Released" : "Pressed")}({(ushort)physical:X4})";
        }

        void LogPointerEvent(object sender, MouseEventArgs args) => Console.WriteLine(
            $"Hand sort {args.RoutedEvent.Name}: position={args.GetPosition(panel)}, " +
            $"pointer={HandPanel.GetIsPointerCard(button)}, capture={button.IsMouseCaptured}.");

        void RaiseKey(RoutedEvent preview, RoutedEvent bubble)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(button)!, 0, Key.Space)
                { RoutedEvent = preview };
            LogPointerState($"{preview.Name}-before", args.Handled);
            button.RaiseEvent(args);
            LogPointerState($"{preview.Name}-after", args.Handled);
            if (args.Handled) return;
            args.RoutedEvent = bubble;
            button.RaiseEvent(args);
            LogPointerState($"{bubble.Name}-after", args.Handled);
        }
    }

    private sealed class ReflowClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
