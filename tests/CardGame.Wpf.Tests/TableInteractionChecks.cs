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
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TableInteractionChecks
{
    public static void SelectionCost(string output)
    {
        using var vm = new MainViewModel(false, 1, false, new MemorySaveStore());
        Program.StartLordFixture(vm);
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
        Dictionary<int, Rect>? compactPortraits = null;
        Rect compactHand = default;
        var inspectedCardId = vm.Hand[0].Id;
        foreach (var (width, height, suffix) in new[]
        {
            (1120, 740, "1120"), (1440, 860, "1440"), (1920, 1080, "1920"),
            (1920, 740, "1920x740"), (1120, 1080, "1120x1080"), (1120, 740, "1120-return")
        })
        {
            Program.Render(root, width, height, Path.Combine(output, $"table-reference-play-{suffix}.png"));
            Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
            Rect NamedBounds(string name) => Bounds((FrameworkElement)window.FindName(name));
            var rings = new[] { "TopSeatRing", "LeftSeatRing", "RightSeatRing" }
                .Select(name => (ItemsControl)window.FindName(name)).ToArray();
            var portraits = rings.SelectMany(ring => Program.Find<Button>(ring))
                .Where(button => button.DataContext is SeatViewModel)
                .ToArray();
            var bounds = portraits.Select(Bounds).ToArray();
            Program.Assert(bounds.Length == 7 && bounds.All(rect => rect.Width >= 80 && rect.Height >= 105) &&
                           bounds.All(a => bounds.Count(b => a.IntersectsWith(b)) == 1),
                "Ring portraits overlap or become unreadable at a supported viewport.");
            var layoutHandViewport = (FrameworkElement)window.FindName("HandViewport");
            var surface = Program.Find<CardLiftSurface>(layoutHandViewport).Single(element =>
                element.DataContext is CardViewModel item && item.Id == inspectedCardId);
            var handArtwork = Bounds(surface);
            var humanPortrait = Program.Find<Button>((FrameworkElement)window.FindName("HumanPortrait"))
                .Single(button => button.DataContext is SeatViewModel);
            var allPortraits = portraits.Append(humanPortrait).ToDictionary(button => ((SeatViewModel)button.DataContext).Seat, Bounds);
            var equipmentBounds = NamedBounds("EquipmentRail");
            var hand = Bounds(layoutHandViewport);
            var skillPanelBounds = NamedBounds("HumanSkillPanel");
            var human = Bounds(humanPortrait);
            Program.Assert(equipmentBounds.Right <= hand.Left + 1 && hand.Right <= skillPanelBounds.Left + 1 && skillPanelBounds.Right <= human.Left + 1 &&
                           human.Right <= width + 1 && hand.Bottom <= height + 1 &&
                           NamedBounds("TableSidebar").Bottom <= NamedBounds("HandDock").Top + 1,
                $"Responsive table rails overlap or clip persistent controls at {width}x{height}.");

            if (compactPortraits is null)
            {
                compactPortraits = allPortraits;
                compactHand = handArtwork;
                Program.Assert(Math.Abs(compactHand.Width - 110) < .1 && Math.Abs(compactHand.Height - 156) < .1,
                    "The minimum layout changed the existing physical hand artwork size.");
            }
            if (width == 1920 && height == 1080)
            {
                Program.Assert(handArtwork.Width >= 136 && handArtwork.Height >= 193 &&
                               allPortraits.Values.All(rect => rect.Width >= 170 && rect.Height >= 224),
                    "The spacious table did not enlarge the actual transformed hand artwork and all eight portraits.");
            }
            else
            {
                Program.Assert(Math.Abs(handArtwork.Width - compactHand.Width) < .1 &&
                               Math.Abs(handArtwork.Height - compactHand.Height) < .1,
                    $"The {width}x{height} table enlarged cards without enough width and height together.");
            }
            if (suffix == "1120-return")
                Program.Assert(allPortraits.All(pair => NearlyEqual(pair.Value, compactPortraits[pair.Key])) &&
                               NearlyEqual(handArtwork, compactHand),
                    "Returning from a wide or tall table retained stale portrait/card scale or positions.");
            Program.Assert(engine.Revision == beforeRevision,
                "Responsive layout or measuring transformed artwork submitted a game command.");
            Console.WriteLine($"Responsive table {width}x{height}: hand={handArtwork.Width:F1}x{handArtwork.Height:F1}, " +
                $"opponents={bounds.Min(rect => rect.Width):F1}–{bounds.Max(rect => rect.Width):F1}px, human={human.Width:F1}x{human.Height:F1}.");
        }

        static bool NearlyEqual(Rect left, Rect right) => Math.Abs(left.X - right.X) < .1 && Math.Abs(left.Y - right.Y) < .1 &&
            Math.Abs(left.Width - right.Width) < .1 && Math.Abs(left.Height - right.Height) < .1;

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
        try
        {
            VerifyRetiredHand(vm, window);
            VerifyRetiredTarget();
            engine = Program.Engine(vm);
            vm.NewGameCommand.Execute(null);
            vm.StartNewGameCommand.Execute(null);
            Program.Assert(!ReferenceEquals(engine, Program.Engine(vm)) &&
                           ((IReadOnlyList<LegalAction>)query.Invoke(vm, null)!).Count == 0 &&
                           ((IReadOnlyList<HandCardGuidance>)guidanceQuery.Invoke(vm, null)!).Count == 0,
                "A new match reused the previous engine's cached actions.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void VerifyRetiredHand(MainViewModel vm, MainWindow window)
    {
        Program.AdvanceToDecision(vm);
        var engine = Program.Engine(vm);
        var retiredCard = vm.Hand.First(card => card.IsPlayable);
        var items = (ItemsControl)window.FindName("HandCards");
        items.UpdateLayout();
        var button = Program.Find<Button>(items).Single(candidate => ReferenceEquals(candidate.DataContext, retiredCard));
        var capturedCommand = button.Command!;
        var capturedParameter = button.CommandParameter;
        Program.Assert(button.IsEnabled && ReferenceEquals(capturedParameter, retiredCard),
            "The retired-input fixture must capture the actual enabled hand button and parameter.");
        vm.SaveGameCommand.Execute(null);
        vm.LoadManualGameCommand.Execute(null);
        engine = Program.Engine(vm);
        var revision = engine.Revision;
        var state = SnapshotJson.Serialize(engine.CreateSnapshot(0));
        var currentCard = vm.Hand.Single(card => card.Id == retiredCard.Id);
        Program.Assert(!ReferenceEquals(currentCard, retiredCard), "A real load must retire the old hand parameter.");
        capturedCommand.Execute(capturedParameter);
        Program.Assert(!vm.HasSelection && !currentCard.IsSelected && engine.Revision == revision &&
                       SnapshotJson.Serialize(engine.CreateSnapshot(0)) == state,
            "A captured retired hand-button parameter changed the current play draft after load.");

        capturedCommand.Execute(currentCard);
        Program.Assert(currentCard.IsSelected && vm.HasSelection && engine.Revision == revision &&
                       SnapshotJson.Serialize(engine.CreateSnapshot(0)) == state,
            "The current hand-button parameter must still prepare its selection after a real load.");
        var quickConfirm = typeof(MainWindow).GetMethod("TryQuickConfirm", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Program.Assert(quickConfirm.Invoke(window, [retiredCard]) is false && currentCard.IsSelected && engine.Revision == revision,
            "A retired hand object must not quick-confirm or disturb the current draft.");
        vm.ClearSelectionCommand.Execute(null);
        Program.Assert(!vm.HasSelection, "The current hand draft must still cancel normally.");
    }

    private static void VerifyRetiredTarget()
    {
        // Reuse the existing first-play Wusheng fixture; no seed search or complete match is needed.
        var saveStore = new MemorySaveStore();
        using var vm = new MainViewModel(false, 721019, false, saveStore) { IsMotionEnabled = false };
        Program.StartLordFixture(vm);
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices.Single(choice => choice.Name == "关羽"));
        Program.AdvanceToDecision(vm);
        var engine = Program.Engine(vm);
        var redCard = vm.Hand.First(card => card.Name != "杀" && card.SuitGlyph is "♥" or "♦");
        var action = engine.GetHumanLegalActions().First(candidate => candidate.CardId == redCard.Id &&
            candidate.PlayedCardKind == CardKind.Slash && candidate.ConversionSource?.SkillId == "standard:wusheng" &&
            candidate.TargetSeats.Count == 1 && candidate.TargetSeats[0] >= 5);
        void PrepareConversion()
        {
            vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == action.CardId));
            var conversionChoice = vm.EquipmentPlayChoices.Single(choice =>
                choice.Cards.SequenceEqual([redCard.Id]) &&
                choice.Parameters.GetValueOrDefault("conversion-skill-id") == action.ConversionSource!.SkillId &&
                choice.Parameters.GetValueOrDefault("conversion-binding-id") == action.ConversionSource.BindingId &&
                choice.Parameters.GetValueOrDefault("conversion-instance-id") == action.ConversionSource.SkillInstanceId);
            vm.SelectEquipmentPlayChoiceCommand.Execute(conversionChoice);
        }
        PrepareConversion();
        var retiredSeat = vm.Seats.Single(seat => seat.Seat == action.TargetSeat);
        var capturedCommand = vm.SelectTargetCommand;
        capturedCommand.Execute(retiredSeat);
        Program.Assert(redCard.IsSelected && retiredSeat.IsLegalTarget && retiredSeat.IsSelectedTarget && vm.CanConfirmSelected,
            "The fixed eight-player fixture must prepare its real legal target before shrinking the table.");
        Console.WriteLine($"Retired target fixture: seed 721019, Wusheng target seat {retiredSeat.Seat}, eight to five players.");
        var original = engine.CreateCheckpoint();
        var revision = engine.Revision;
        var state = SnapshotJson.Serialize(engine.CreateSnapshot(0));
        var selectedTarget = typeof(MainViewModel).GetField("_selectedTargetSeat", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var registry = (ContentRegistry)typeof(MainViewModel).GetField("_contentRegistry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        var smaller = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false
        }, registry);
        Program.Assert(smaller.Submit(new StartGameCommand()).Accepted, "The five-player checkpoint must start through a real command.");
        for (var step = 0; step < 16 && smaller.PendingDecision?.Kind != DecisionKind.SelectGeneral; step++)
            Program.Assert(smaller.Submit(new AdvanceOneStepCommand(smaller.Revision)).Accepted,
                "The five-player checkpoint must reach its actual general-selection boundary.");
        Program.Assert(smaller.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 },
            "The smaller-table fixture must contain a real human pending selection.");
        saveStore.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, smaller.CreateCheckpoint()));
        vm.LoadManualGameCommand.Execute(null);
        var smallerEngine = Program.Engine(vm);
        var smallerState = SnapshotJson.Serialize(smallerEngine.CreateSnapshot(0));
        Program.Assert(vm.Seats.Count == 5 && !vm.Seats.Contains(retiredSeat) && retiredSeat.IsLegalTarget &&
                       !vm.HasSelection && selectedTarget.GetValue(vm) is null,
            "Loading five players must remove the formerly legal outer target and clear its old draft.");
        capturedCommand.Execute(retiredSeat);
        Program.Assert(selectedTarget.GetValue(vm) is null && !vm.HasSelection && vm.Seats.All(seat => !seat.IsSelectedTarget) &&
                       SnapshotJson.Serialize(smallerEngine.CreateSnapshot(0)) == smallerState,
            "A removed target parameter left a ghost draft on the smaller table.");

        saveStore.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, original));
        vm.LoadManualGameCommand.Execute(null);
        engine = Program.Engine(vm);
        var restoredCard = vm.Hand.Single(card => card.Id == action.CardId);
        var restoredSeat = vm.Seats.Single(seat => seat.Seat == action.TargetSeats[0]);
        Program.Assert(!ReferenceEquals(restoredSeat, retiredSeat), "Returning to eight players must create a current outer-seat object.");
        PrepareConversion();
        capturedCommand.Execute(restoredSeat);
        Program.Assert(restoredCard.IsSelected && restoredSeat.IsSelectedTarget && vm.CanConfirmSelected &&
                       engine.Revision == revision && SnapshotJson.Serialize(engine.CreateSnapshot(0)) == state,
            "Current hand and target objects stopped preparing the exact restored legal action.");
        capturedCommand.Execute(retiredSeat);
        Program.Assert(restoredSeat.IsSelectedTarget && vm.CanConfirmSelected &&
                       (int?)selectedTarget.GetValue(vm) == restoredSeat.Seat && engine.Revision == revision,
            "A retired target with the same seat number disturbed the restored current draft.");
        capturedCommand.Execute(restoredSeat);
        Program.Assert(!restoredSeat.IsSelectedTarget && !vm.CanConfirmSelected && selectedTarget.GetValue(vm) is null,
            "The restored current target must still toggle off normally.");
        vm.ClearSelectionCommand.Execute(null);
        Program.Assert(!vm.HasSelection && selectedTarget.GetValue(vm) is null,
            "The restored current draft must still cancel normally.");
    }
}
