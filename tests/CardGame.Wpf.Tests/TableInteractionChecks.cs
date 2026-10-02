using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using Rectangle = System.Windows.Shapes.Rectangle;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TableInteractionChecks
{
    public static void SelectionCost(string output)
    {
        using var vm = new MainViewModel(false, 1, false, new MemorySaveStore());
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        // Use the stable base catalogue for presentation costs; expanded-pool behavior has separate coverage.
        for (var step = 0; step < 8 && vm.SkillChoices.Count > 0; step++)
        {
            vm.SelectSkillChoiceCommand.Execute(vm.SkillChoices.Last());
            Program.AdvanceToDecision(vm);
        }
        var card = vm.Hand.First(card => card.IsPlayable);
        var engine = Program.Engine(vm);
        var beforeRevision = engine.Revision;
        var refresh = typeof(MainViewModel).GetMethod("RefreshCurrentView", BindingFlags.Instance | BindingFlags.NonPublic)!;
        refresh.Invoke(vm, null);
        var refreshBytes = GC.GetAllocatedBytesForCurrentThread();
        var refreshTimer = Stopwatch.StartNew();
        for (var index = 0; index < 10; index++) refresh.Invoke(vm, null);
        refreshTimer.Stop();
        refreshBytes = GC.GetAllocatedBytesForCurrentThread() - refreshBytes;
        var refreshCost = $"10 same-revision table refreshes: {refreshTimer.Elapsed.TotalMilliseconds:F1} ms; {refreshBytes:N0} bytes";
        Console.WriteLine(refreshCost);
        File.WriteAllText(Path.Combine(output, "table-refresh-cost.txt"), refreshCost);
        Program.Assert(refreshBytes < 15_000_000 && engine.Revision == beforeRevision,
            "A local presentation refresh recomputed hand rules or submitted a command.");
        for (var i = 0; i < 3; i++) { vm.SelectCardCommand.Execute(card); vm.ClearSelectionCommand.Execute(null); }
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 30; i++) { vm.SelectCardCommand.Execute(card); vm.ClearSelectionCommand.Execute(null); }
        timer.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var result = $"30 select/cancel pairs: {timer.Elapsed.TotalMilliseconds:F1} ms; {allocated:N0} bytes";
        Console.WriteLine(result);
        File.WriteAllText(Path.Combine(output, "table-interaction-cost.txt"), result);
        Program.Assert(allocated < 4_000_000 && engine.Revision == beforeRevision,
            "Pure selection rebuilt expensive rules queries or submitted a command.");
        Program.Assert(vm.TopSeats.Select(seat => seat.Seat).SequenceEqual([5, 4, 3]) &&
                       vm.LeftSeats.Select(seat => seat.Seat).SequenceEqual([6, 7]) &&
                       vm.RightSeats.Select(seat => seat.Seat).SequenceEqual([2, 1]),
            "Eight-player seats must follow turn order around the table with no duplicated seats.");
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        foreach (var (width, height) in new[] { (1120, 740), (1440, 860), (1920, 1080) })
        {
            Program.Render(root, width, height, Path.Combine(output, $"table-reference-play-{width}.png"));
            var rings = new[] { "TopSeatRing", "LeftSeatRing", "RightSeatRing" }
                .Select(name => (ItemsControl)window.FindName(name)).ToArray();
            var bounds = rings.SelectMany(ring => Program.Find<Button>(ring))
                .Where(button => button.DataContext is SeatViewModel)
                .Select(button => button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize))).ToArray();
            Program.Assert(bounds.Length == 7 && bounds.All(rect => rect.Width >= 80 && rect.Height >= 105) &&
                           bounds.All(a => bounds.Count(b => a.IntersectsWith(b)) == 1),
                "Ring portraits overlap or become unreadable at a supported viewport.");
        }

        var query = typeof(MainViewModel).GetMethod("GetViewLegalActions", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void UpdateView()
        {
            root.UpdateLayout();
            root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        }
        refresh.Invoke(vm, null);
        UpdateView();
        allocated = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        for (var index = 0; index < 10; index++) { refresh.Invoke(vm, null); UpdateView(); }
        timer.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        result = $"10 same-revision refreshes with layout: {timer.Elapsed.TotalMilliseconds:F1} ms; {allocated:N0} bytes";
        Console.WriteLine(result);
        File.AppendAllText(Path.Combine(output, "table-refresh-cost.txt"), Environment.NewLine + result);
        var equipmentRows = vm.HumanEquipmentSlots;
        Program.Assert(ReferenceEquals(equipmentRows, vm.HumanEquipmentSlots),
            "Reading the unchanged equipment rail replaced its rows.");
        var equipmentRail = (ItemsControl)window.FindName("HumanEquipmentSlots");
        var equipmentContainer = equipmentRail.ItemContainerGenerator.ContainerFromIndex(0);
        refresh.Invoke(vm, null);
        UpdateView();
        Program.Assert(ReferenceEquals(equipmentRows, vm.HumanEquipmentSlots) &&
                       ReferenceEquals(equipmentContainer, equipmentRail.ItemContainerGenerator.ContainerFromIndex(0)),
            "An unchanged table refresh recreated the equipment controls.");
        var healthRail = Program.Find<ItemsControl>(root).Single(items =>
            items.DataContext is SeatViewModel { IsHuman: true } &&
            BindingOperations.GetBinding(items, ItemsControl.ItemsSourceProperty)?.Path?.Path == nameof(SeatViewModel.HealthImages));
        var healthImages = healthRail.ItemsSource;
        var healthContainer = healthRail.ItemContainerGenerator.ContainerFromIndex(0);
        var guideRows = vm.GuideHand.ToArray();
        var guideChanges = 0;
        var guideReset = false;
        vm.GuideHand.CollectionChanged += (_, _) => guideChanges++;
        vm.CurrentGuideSteps.CollectionChanged += (_, e) => guideReset |= e.Action == NotifyCollectionChangedAction.Reset;
        var skills = vm.HumanSkillCards;
        var handViewport = (FrameworkElement)window.FindName("HandViewport");
        var cardArtwork = Program.Find<Image>(handViewport).Single(image => image.Name == "CardArtwork" &&
            image.DataContext is CardViewModel displayed && displayed.Id == card.Id);
        var portrait = Program.Find<Rectangle>(root).Single(image => image.Name == "SinglePortrait" &&
            image.DataContext is SeatViewModel { IsHuman: true });
        var cardSize = cardArtwork.RenderSize;
        var portraitSize = portrait.RenderSize;
        vm.SelectCardCommand.Execute(card);
        UpdateView();
        Program.Assert(cardArtwork.RenderSize == cardSize && portrait.RenderSize == portraitSize,
            "Selecting a card or target resized its artwork instead of overlaying a highlight.");
        vm.ClearSelectionCommand.Execute(null);
        UpdateView();
        for (var i = 0; i < 3; i++)
        {
            vm.SelectCardCommand.Execute(card); UpdateView();
            vm.ClearSelectionCommand.Execute(null); UpdateView();
        }
        allocated = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        for (var i = 0; i < 30; i++)
        {
            vm.SelectCardCommand.Execute(card); UpdateView();
            vm.ClearSelectionCommand.Execute(null); UpdateView();
        }
        timer.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        result = $"30 select/cancel pairs with layout: {timer.Elapsed.TotalMilliseconds:F1} ms; {allocated:N0} bytes";
        Console.WriteLine(result);
        File.AppendAllText(Path.Combine(output, "table-interaction-cost.txt"), Environment.NewLine + result);
        Program.Assert(allocated < 25_000_000 && engine.Revision == beforeRevision,
            "Selection recreated unchanged controls or submitted a command during layout.");
        Program.Assert(guideChanges == 0 && !guideReset &&
                       vm.GuideHand.Zip(guideRows).All(pair => ReferenceEquals(pair.First, pair.Second)) &&
                       ReferenceEquals(skills, vm.HumanSkillCards),
            "Selection recreated unchanged guide rows or skill controls.");
        var cached = query.Invoke(vm, null);
        var guidanceQuery = typeof(MainViewModel).GetMethod("GetViewHandGuidance", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var cachedGuidance = guidanceQuery.Invoke(vm, null);
        vm.SelectCardCommand.Execute(card);
        var cancel = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent, Source = window.FindName("HandViewport")
        };
        window.RaiseEvent(cancel);
        Program.Assert(cancel.Handled && !vm.HasSelection && engine.Revision == beforeRevision,
            "Right-clicking the hand surface must cancel without submitting a command.");
        var seats = vm.Seats.ToArray();
        var logReset = false;
        vm.FilteredBattleLog.CollectionChanged += (_, e) => logReset |= e.Action == NotifyCollectionChangedAction.Reset;
        var equip = engine.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Equip);
        var equipment = vm.Hand.Single(item => item.Id == equip.CardId);
        vm.SelectCardCommand.Execute(equipment);
        var quickConfirm = typeof(MainWindow).GetMethod("TryQuickConfirm", BindingFlags.Instance | BindingFlags.NonPublic)!;
        vm.IsHelpOpen = true;
        Program.Assert(quickConfirm.Invoke(window, [equipment]) is false && engine.Revision == beforeRevision,
            "Quick confirmation bypassed an open modal.");
        vm.IsHelpOpen = false;
        GameSnapshot? published = null;
        engine.StateChanged += snapshot => published = snapshot;
        Program.Assert(quickConfirm.Invoke(window, [equipment]) is true && engine.Revision == beforeRevision + 1 &&
                       engine.CreateSnapshot(0).Players[0].Equipment.Any(item => item.Id == equipment.Id),
            "Quick confirmation must use the selected legal equipment exactly once.");
        Program.Assert(published is not null && ReferenceEquals(published,
                typeof(MainViewModel).GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)),
            "The ordinary view must reuse the committed player snapshot without rebuilding it.");
        Program.Assert(quickConfirm.Invoke(window, [equipment]) is false && !logReset &&
                       vm.Seats.Zip(seats).All(pair => ReferenceEquals(pair.First, pair.Second)) &&
                       !ReferenceEquals(cached, query.Invoke(vm, null)) &&
                       !ReferenceEquals(cachedGuidance, guidanceQuery.Invoke(vm, null)) &&
                       ((IReadOnlyList<HandCardGuidance>)guidanceQuery.Invoke(vm, null)!).SequenceEqual(engine.GetHumanHandGuidance()),
            "A command reused stale actions, rebuilt the public log or replaced the seat containers.");
        UpdateView();
        Program.Assert(vm.HumanEquipmentSlots.Any(slot => slot.Card?.Id == equipment.Id) &&
                       !ReferenceEquals(equipmentRows, vm.HumanEquipmentSlots),
            "Equipping a physical card left the old equipment rail visible.");
        Program.Assert(ReferenceEquals(healthImages, healthRail.ItemsSource) &&
                       ReferenceEquals(healthContainer, healthRail.ItemContainerGenerator.ContainerFromIndex(0)),
            "A hand-count or equipment change recreated unchanged health icons.");
        // Presentation-only snapshots exercise health changes without changing game rules.
        var committedView = engine.CreateSnapshot(0);
        var applyView = typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var (hp, maxHp, icons, filled) in new[] { (3, 4, 4, 3), (0, 4, 4, 0), (5, 5, 5, 5), (9, 10, 8, 8) })
        {
            applyView.Invoke(vm, [committedView with
            {
                Players = committedView.Players.Select(player => player.IsHuman
                    ? player with { Hp = hp, MaxHp = maxHp } : player).ToArray()
            }]);
            UpdateView();
            Program.Assert(healthRail.Items.Count == icons &&
                           healthRail.Items.Cast<string>().Count(source => source.EndsWith("hp-wu.png")) == filled,
                "A health or maximum-health change left stale icons in the bound seat template.");
        }
        applyView.Invoke(vm, [committedView]);
        UpdateView();
        Program.Assert(engine.Revision == committedView.Revision,
            "Inspecting health presentation changed the match.");
        vm.IsDeveloperView = true;
        vm.IsDeveloperView = false;
        var permitted = engine.CreateSnapshot(0);
        Program.Assert(vm.Seats.All(seat => permitted.Players[seat.Seat].Role is not null || seat.RoleLabel == "?"),
            "Reusing seat instances retained a hidden role from the developer view.");
        vm.NewGameCommand.Execute(null);
        vm.StartNewGameCommand.Execute(null);
        Program.Assert(!ReferenceEquals(engine, Program.Engine(vm)) &&
                       ((IReadOnlyList<LegalAction>)query.Invoke(vm, null)!).Count == 0 &&
                       ((IReadOnlyList<HandCardGuidance>)guidanceQuery.Invoke(vm, null)!).Count == 0,
            "A new match reused the previous engine's cached actions.");
        window.Content = null;
        window.Close();
    }
}
