using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

internal static class NativeCardTargetingChecks
{
    public static void NativeDraftGuide(string output)
    {
        using var model = new MainViewModel(false, 721019, false, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        StartLordFixture(model);
        model.SelectGeneralChoiceCommand.Execute(model.GeneralChoices[0]);
        AdvanceToDecision(model);
        var engine = Engine(model);
        var slash = engine.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash &&
            action.CardId is > 0 && action.ConversionSource is null && model.Hand.Any(card => card.Id == action.CardId && card.Kind == CardKind.Slash));
        var slashId = slash.CardId!.Value;
        var slashTarget = slash.TargetSeats.Single();
        var chain = model.Hand.First(card => card.Kind == CardKind.IronChain && card.IsPlayable);
        var pair = engine.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.IronChain &&
            action.CardId == chain.Id && action.ConversionSource is null && action.TargetSeats.Count == 2);
        var accepted = engine.AcceptedCommands.Count;
        var views = SafeViews(engine);
        var cues = model.BattleCues.Count;
        var sounds = 0;
        model.SoundsRequested += (_, _) => sounds++;
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        var guide = (CardTargetingLayer)window.FindName("CardTargeting");
        var combat = (BattleFeedbackLayer)window.FindName("BattleFeedback");
        var viewport = (ScrollViewer)window.FindName("HandViewport");
        try
        {
            Prepare(root, 1120, 740);
            Assert(guide.IsDraftEnabled == model.CanEndTurn && ReferenceEquals(guide.AnchorRoot, window.FindName("GameArea")) &&
                ReferenceEquals(guide.HandRoot, viewport) && ReferenceEquals(guide.PhaseHud, window.FindName("BattlePhaseHud")) &&
                ReferenceEquals(guide.ActionBar, window.FindName("PrimaryActionButtons")) &&
                ReferenceEquals(guide.ActionHint, window.FindName("ActionDockHint")) && ReferenceEquals(guide.Sidebar, window.FindName("TableSidebar")) &&
                ReferenceEquals(guide.SupplementalActions, window.FindName("SupplementalActionBar")) &&
                ReferenceEquals(guide.CenterAnchor, window.FindName("FeedbackCenter")) && guide.IsPromptVisible == model.HasCenterChoices &&
                !guide.IsHitTestVisible && !guide.Focusable && Curves(guide).Length == 0,
                "The native targeting guide must use current table anchors, remain non-interactive, and be empty before selection.");
            Click(CardButton(slashId));
            Prepare(root, 1120, 740);
            Assert(Curves(guide).Length == 0 && Unchanged(), "A card without a chosen portrait must not invent an attack path or commit a command.");
            Click(SeatButton(slashTarget));
            foreach (var size in new[] { (1120, 740), (1920, 1080) })
            {
                Render(root, size.Item1, size.Item2, Path.Combine(output, $"card-targeting-slash-selected-{size.Item1}.png"));
                AssertGuide(slashId, [slashTarget]);
                Assert(model.CanConfirmSelected && Unchanged(), "The actual chosen Slash path must remain a reversible, motion-disabled draft.");
            }
            Click(SeatButton(slashTarget));
            Prepare(root, 1120, 740);
            Assert(Curves(guide).Length == 0 && model.Hand.Single(card => card.Id == slashId).IsSelected && Unchanged(),
                "Toggling the same portrait off must clear the path while retaining the selected physical card.");
            Click(SeatButton(slashTarget));
            Prepare(root, 1120, 740);
            AssertGuide(slashId, [slashTarget]);
            Click(CardButton(chain.Id));
            Prepare(root, 1120, 740);
            Assert(Curves(guide).Length == 0 && model.IsMultiTargetCardSelected && !model.Seats.Any(seat => seat.IsSelectedTarget) && Unchanged(),
                "Switching a still-targeted Slash to the real Iron Chain must remove the old endpoint before its new target selection.");
            Click(SeatButton(pair.TargetSeats[0]));
            Click(SeatButton(pair.TargetSeats[1]));
            foreach (var size in new[] { (1120, 740), (1920, 1080) })
            {
                Render(root, size.Item1, size.Item2, Path.Combine(output, $"card-targeting-chain-pair-{size.Item1}.png"));
                AssertGuide(chain.Id, pair.TargetSeats);
                Assert(model.CanConfirmSelected && Unchanged(), "The real two-target Iron Chain draft must branch from one physical card without combat facts.");
            }
            Click((Button)window.FindName("CancelActionButton"));
            Prepare(root, 1120, 740);
            Assert(Curves(guide).Length == 0 && !model.HasSelection && Unchanged(),
                "The ordinary PlayCard cancel button must clear the guide without using or recasting the selected Iron Chain.");
            Click(CardButton(slashId)); Click(SeatButton(slashTarget));
            Prepare(root, 1120, 740);
            Assert(TableShortcutChecks.Input(window, Key.Escape), "The native Escape route must accept ordinary draft cancellation.");
            Prepare(root, 1120, 740);
            Assert(Curves(guide).Length == 0 && !model.HasSelection && Unchanged(), "Escape must remove the ordinary draft guide and preserve the command boundary.");

            Click(CardButton(slashId)); Click(SeatButton(slashTarget));
            Prepare(root, 1120, 740);
            model.SortHandCommand.Execute(null);
            Surface(slashId).BringIntoView();
            Prepare(root, 1120, 740);
            AssertGuide(slashId, [slashTarget]);
            Assert(Unchanged(), "Real hand sorting must recompute the selected card origin without changing its native draft or snapshots.");

            // This fixed hand naturally fits the normal viewport.
            // Constrain its real viewport to exercise scrolling, then restore it.
            viewport.Width = 220; viewport.HorizontalAlignment = HorizontalAlignment.Left;
            Prepare(root, 1120, 740);
            Assert(viewport.ScrollableWidth > 0, "The constrained native hand must provide a real horizontal scroll range.");
            viewport.ScrollToLeftEnd(); Prepare(root, 1120, 740);
            AssertCurrentVisibility();
            viewport.ScrollToRightEnd(); Prepare(root, 1120, 740);
            Assert(viewport.HorizontalOffset > 0, "The real constrained hand must scroll before measuring the guide's new physical origin.");
            AssertCurrentVisibility();
            Surface(slashId).BringIntoView(); Prepare(root, 1120, 740);
            AssertGuide(slashId, [slashTarget]);
            Assert(Unchanged(), "Scrolling must never retain a detached card origin or publish combat feedback.");
            viewport.ClearValue(FrameworkElement.WidthProperty); viewport.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
            viewport.ScrollToLeftEnd(); Prepare(root, 1120, 740);

            model.SaveGameCommand.Execute(null); model.LoadManualGameCommand.Execute(null);
            engine = Engine(model);
            Prepare(root, 1120, 740);
            Assert(!model.HasSaveError && !model.HasSelection && Curves(guide).Length == 0 &&
                engine.AcceptedCommands.Count == accepted && SafeViews(engine).SequenceEqual(views),
                "Reload must preserve all player-safe snapshots while removing the unsent card-to-portrait draft.");
            Click(CardButton(slashId)); Click(SeatButton(slashTarget));
            Prepare(root, 1120, 740);
            AssertGuide(slashId, [slashTarget]);
            Click((Button)window.FindName("PlayCardButton"));
            Render(root, 1120, 740, Path.Combine(output, "card-targeting-after-confirm.png"));
            Assert(engine.AcceptedCommands.Count == accepted + 1 && engine.AcceptedCommands.Last() is PlayCardCommand command &&
                command.CardId == slashId && command.TargetSeats.SequenceEqual(slash.TargetSeats) && Curves(guide).Length == 0 &&
                !model.Seats.Any(seat => seat.IsSelectedTarget),
                "The native confirm button must submit the original Slash once and remove its draft path before committed feedback.");
            Console.WriteLine($"Card targeting: native Slash {slashId} / Iron Chain {chain.Id}, 1120/1920, cancel/Escape/switch/reorder/constrained scroll/load/confirm, accepted={accepted}->{engine.AcceptedCommands.Count}.");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
        NativeAimGesture(model, output);
        ProtectedAnchorGeometry(output);

        Button CardButton(int id) => Find<Button>(root).Single(button => button.Name == "CardButton" && button.DataContext is CardViewModel card && card.Id == id);
        FrameworkElement Surface(int id) => Find<FrameworkElement>(CardButton(id)).Single(element => BattleFeedbackLayer.GetHandAnchor(element) == id);
        Button SeatButton(int seat) => Find<Button>(root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == seat);
        bool Unchanged() => engine.AcceptedCommands.Count == accepted && SafeViews(engine).SequenceEqual(views) &&
            model.BattleCues.Count == cues && combat.ActiveEffectCount == 0 && !combat.IsFrameTimerRunning && sounds == 0;
        void AssertCurrentVisibility()
        {
            var face = Bounds(Surface(slashId), guide);
            var visible = Bounds(viewport, guide).Contains(new Point(face.Left + face.Width / 2, face.Top + face.Height / 2));
            if (visible) AssertGuide(slashId, [slashTarget]);
            else Assert(Curves(guide).Length == 0, "A selected card outside its actual hand viewport must not leave a detached targeting path.");
        }
        void AssertGuide(int id, IReadOnlyList<int> targets)
        {
            var paths = Curves(guide);
            var card = Bounds(Surface(id), guide);
            var start = new Point(card.Left + card.Width / 2, card.Top);
            Assert(CardTargetingLayer.GetIsDraftCard(Surface(id)) && paths.Length == targets.Count && paths.All(path => Near(path.StartPoint, start)),
                "Every draft branch must start at the actual selected physical card's current top edge.");
            foreach (var seat in targets)
            {
                var button = SeatButton(seat); var portrait = Bounds(button, guide); var nearby = portrait; nearby.Inflate(6, 6);
                Assert(CardTargetingLayer.GetIsDraftTarget(button) && paths.Count(path => nearby.Contains(End(path)) && !portrait.Contains(End(path))) == 1,
                    "Each actual selected portrait must receive one quiet endpoint outside its bounds, with no invented target or selection order.");
            }
            var visibleDashes = Ink(VisualTreeHelper.GetDrawing(guide), dashedOnly: true).ToArray();
            var sourceRegion = new RectangleGeometry(new Rect(start.X - 28, start.Y - 28, 56, 56));
            Assert(visibleDashes.Length == targets.Count && visibleDashes.All(geometry => geometry.GetArea() > 50),
                "Each target must receive substantial actually visible dashed ink after clipping, rather than an isolated arrowhead or unpainted curve bounds.");
            Assert(visibleDashes.All(geometry =>
            {
                var prefix = new CombinedGeometry(GeometryCombineMode.Intersect, geometry, sourceRegion);
                return prefix.GetArea() > 10 && !prefix.Bounds.IsEmpty && prefix.Bounds.Width + prefix.Bounds.Height > 20;
            }), "Every targeting branch must visibly emerge for about twenty DIP near the selected physical card instead of starting beyond an opaque command slab.");
            Console.WriteLine($"Visible targeting ink: width={root.ActualWidth:F0}, card={id}, areas={string.Join(',', visibleDashes.Select(geometry => geometry.GetArea().ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))}.");
            foreach (var element in new[] { guide.ActionBar, guide.SupplementalActions, guide.PhaseHud, guide.Sidebar, guide.IsPromptVisible ? guide.CenterAnchor : null })
                AssertClear(guide, Bounds(element, guide));
            var hintArea = ContentBounds(guide.ActionHint, guide);
            Assert(!hintArea.IsEmpty, "The native action hint must provide actual rendered text bounds before testing its protection.");
            AssertClear(guide, hintArea);
            foreach (var face in Find<FrameworkElement>(root).Where(element => BattleFeedbackLayer.GetPlayAnchor(element) >= 0))
                AssertClear(guide, Bounds(face, guide));
        }
    }

    private static void NativeAimGesture(MainViewModel model, string output)
    {
        model.LoadManualGameCommand.Execute(null);
        Assert(!model.HasSaveError, "The coordinate-driven native aim check could not restore its existing saved fixture: " + model.SaveStatus);
        model.IsMotionEnabled = true;
        using var host = new NativeResponseStatusChecks.NativeHost(model);
        var window = host.Window; var root = host.Root; var combat = host.Layer; var clock = host.Clock;
        var guide = (CardTargetingLayer)window.FindName("CardTargeting");
        var viewport = (ScrollViewer)window.FindName("HandViewport");
        var panel = Find<HandPanel>(viewport).Single(); panel.Clock = clock;
        var refresh = typeof(CardLiftSurface).GetMethod("RefreshMotion", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var press = typeof(HandPanel).GetMethod("PressCardAt", BindingFlags.Instance | BindingFlags.NonPublic)!;
        CardViewModel? diagnosticCard = null;
        Button? diagnosticSource = null;
        host.Capture(output, "card-aim-native-cold", 1120); ShareClock();
        var baseline = Boundary();
        var slash = Slash();
        var slashId = slash.CardId!.Value; var targetSeat = slash.TargetSeats.Single();
        try
        {
            // Calling the production entries supplies coordinates without
            // injecting foreground hardware input. Capture itself is real WPF.
            var down = ArmAndPress(Card(slashId));
            var near = panel.TranslatePoint(down, guide) + new Vector(
                SystemParameters.MinimumHorizontalDragDistance / 2, SystemParameters.MinimumVerticalDragDistance / 2);
            Assert(!Aim("UpdateCardAim", near) && !ReferenceEquals(Mouse.Captured, window) && guide.PointerAimPoint is null,
                "A native press below the system drag threshold captured the pointer or invented an aim.");
            Assert(!Aim("ReleaseCardAim", near) && Card(slashId).IsSelected,
                "An ordinary sub-threshold release forced a play or lost its normal click selection.");
            NoCommit(baseline, "sub-threshold click"); ClearedAim();

            ClearDraft(); model.SelectCardCommand.Execute(Card(slashId));
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == targetSeat)); Flush();
            ArmAndPress(Card(slashId));
            Assert(!Card(slashId).IsSelected, "Pressing the already selected card must retain the existing click toggle before dragging begins.");
            Started(TargetPoint(targetSeat));
            Assert(Card(slashId).IsSelected && model.Seats.Single(seat => seat.Seat == targetSeat).IsSelectedTarget,
                "Crossing the aim threshold did not restore the actual pre-press selected card and chosen target draft.");
            var illegal = model.Seats.First(seat => !seat.IsLegalTarget).Seat;
            Assert(Aim("UpdateCardAim", TargetPoint(illegal)) && !guide.IsPointerAimValid,
                "The actual illegal portrait was advertised as a valid drop target.");
            Assert(Aim("ReleaseCardAim", TargetPoint(illegal)) && Card(slashId).IsSelected &&
                model.Seats.Single(seat => seat.Seat == targetSeat).IsSelectedTarget,
                "An invalid started drop did not preserve its restored reversible card/target draft.");
            NoCommit(baseline, "restored draft/illegal portrait"); ClearedAim();

            foreach (var (name, point) in new[]
            {
                ("sidebar", Center(Bounds((FrameworkElement)window.FindName("TableSidebar"), guide))),
                ("outside", new Point(-20, -20))
            })
            {
                StartSlash();
                Assert(Aim("UpdateCardAim", point) && !guide.IsPointerAimValid && Aim("ReleaseCardAim", point),
                    $"The started {name} release did not retain its invalid physical drop.");
                NoCommit(baseline, name); ClearedAim();
            }

            ClearDraft();
            var chain = Engine(model).GetHumanLegalActions().First(action => action.Kind == LegalActionKind.IronChain &&
                action.CardId is > 0 && action.ConversionSource is null && action.TargetSeats.Count == 2);
            var chainCard = Card(chain.CardId!.Value);
            ArmAndPress(chainCard); Started(TargetPoint(chain.TargetSeats[0]));
            host.Capture(output, "card-aim-native-chain-before-release", 1120);
            foreach (var face in Find<CardLiftSurface>(viewport).Where(face => BattleFeedbackLayer.GetHandAnchor(face) > 0))
                AssertClear(guide, Bounds(face, guide));
            Assert(guide.IsPointerAimValid && CardTargetingLayer.GetIsAimTarget(Seat(chain.TargetSeats[0])) &&
                guide.PointerAimLabel == "松开选定目标", "The actual multi-target card must advertise selection rather than an automatic use.");
            Assert(Aim("ReleaseCardAim", TargetPoint(chain.TargetSeats[0])) && chainCard.IsSelected &&
                model.Seats.Count(seat => seat.IsSelectedTarget) == 1 && model.Seats.Single(seat => seat.Seat == chain.TargetSeats[0]).IsSelectedTarget,
                "Iron Chain's real first drop must retain one selected target and require explicit confirmation.");
            NoCommit(baseline, "actual multi-target select-only"); ClearedAim();
            var ownSeat = Engine(model).CreateSnapshot(0).HumanSeat;
            if (Engine(model).GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.IronChain &&
                action.CardId == chainCard.Id && action.TargetSeats.Contains(ownSeat)))
            {
                ClearDraft(); ArmAndPress(chainCard); Started(TargetPoint(ownSeat));
                Assert(guide.IsPointerAimValid && ReferenceEquals(guide.PointerAimAnchor, Seat(ownSeat)) &&
                    Aim("ReleaseCardAim", TargetPoint(ownSeat)) && model.Seats.Single(seat => seat.Seat == ownSeat).IsSelectedTarget,
                    "A genuinely legal self Iron Chain must resolve the real human portrait in HandDock without automatic payment.");
                NoCommit(baseline, "actual own portrait"); ClearedAim();
            }

            StartSlash();
            Assert(TableShortcutChecks.Input(window, Key.Escape), "The actual Escape route did not accept native aim cancellation.");
            Assert(!Aim("ReleaseCardAim", TargetPoint(targetSeat)), "Escape left a stale aim able to consume release.");
            NoCommit(baseline, "Escape"); ClearedAim();

            StartSlash();
            viewport.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Assert(!Aim("ReleaseCardAim", TargetPoint(targetSeat)), "The native right-button route left a stale aim able to consume release.");
            NoCommit(baseline, "right button"); ClearedAim();

            StartSlash(); Mouse.Capture(null);
            Assert(!Aim("ReleaseCardAim", TargetPoint(targetSeat)), "Real capture loss retained an old actionable aim.");
            NoCommit(baseline, "capture loss"); ClearedAim();

            StartSlash(); model.IsHelpOpen = true; Flush();
            Assert(!Aim("ReleaseCardAim", TargetPoint(targetSeat)), "Opening the real help overlay retained the covered table's aim.");
            NoCommit(baseline, "help overlay"); ClearedAim(); model.IsHelpOpen = false; Flush();

            StartSlash(); var handBeforeSort = model.Hand.Select(card => card.Id).ToArray();
            model.SortHandCommand.Execute(null); Flush();
            Assert(!model.Hand.Select(card => card.Id).SequenceEqual(handBeforeSort), "The fixed native stale-sort check did not actually reorder the hand.");
            Assert(!Aim("ReleaseCardAim", TargetPoint(targetSeat)), "Sorting actual physical hand containers retained a stale captured gesture.");
            NoCommit(baseline, "sort"); ClearedAim();

            Reload(); baseline = Boundary();
            viewport.Width = 220; viewport.HorizontalAlignment = HorizontalAlignment.Left; Flush();
            viewport.ScrollToRightEnd(); Flush();
            Assert(viewport.ScrollableWidth > 0 && viewport.HorizontalOffset > 0,
                "The existing fixed native hand did not provide its real constrained scroll boundary.");
            StartSlash(); var beforeScroll = viewport.HorizontalOffset;
            viewport.ScrollToLeftEnd(); Flush();
            Assert(Math.Abs(viewport.HorizontalOffset - beforeScroll) > .01,
                "The stale-scroll boundary did not actually change its physical horizontal offset.");
            Assert(!Aim("ReleaseCardAim", TargetPoint(targetSeat)), "Actual hand scrolling retained a captured origin from the old viewport.");
            NoCommit(baseline, "scroll"); ClearedAim();
            viewport.ClearValue(FrameworkElement.WidthProperty); viewport.ClearValue(FrameworkElement.HorizontalAlignmentProperty); Flush();

            StartSlash(); root.Width = 1200; window.UpdateLayout(); Flush();
            Assert(root.ActualWidth == 1200, "The loaded native stale-resize check did not actually resize its root.");
            Assert(!Aim("ReleaseCardAim", TargetPoint(targetSeat)), "Actual loaded window resizing retained its stale gesture geometry.");
            NoCommit(baseline, "resize"); ClearedAim(); root.Width = 1120; Flush();

            StartSlash(); model.LoadManualGameCommand.Execute(null); Flush(); ShareClock();
            Assert(!model.HasSaveError && !Aim("ReleaseCardAim", TargetPoint(targetSeat)) &&
                Engine(model).AcceptedCommands.Count == baseline.Accepted && SafeViews(Engine(model)).SequenceEqual(baseline.Views),
                "Actual same-window checkpoint load retained a retired captured gesture or altered the baseline's safe game state.");
            ClearedAim(); baseline = Boundary();

            // The one genuine paid Slash uses this same restored engine/host.
            // Its release occurs after exactly fifty ms of the visible lift.
            var game = Engine(model); var beforePaid = game.AcceptedCommands.Count;
            var card = Card(slashId); var surface = Surface(card);
            var film = RecordMotion ? new GifBitmapEncoder() : null;
            var filmTime = 0d; var nextFrame = 0;
            AdvanceFilm(.04); var start = ArmAndPress(card);
            var startInGuide = panel.TranslatePoint(start, guide);
            AdvanceFilm(.06); Started(startInGuide + (TargetPoint(targetSeat) - startInGuide) * .5);
            AdvanceFilm(.078); Assert(Aim("UpdateCardAim", TargetPoint(targetSeat)) && guide.IsPointerAimValid,
                "A real portrait drop did not become valid from the current native legal target.");
            AdvanceFilm(.09);
            host.Capture(output, "card-aim-native-slash-ready", 1120);
            var origin = Bounds(surface, combat);
            var y = ((TranslateTransform)surface.RenderTransform).Y;
            Assert(y is < 0 and > -18 && Curves(guide).Length == 1 && ReferenceEquals(guide.PointerAimAnchor, Seat(targetSeat)) &&
                CardTargetingLayer.GetIsAimTarget(Seat(targetSeat)) && guide.PointerAimLabel == "松开出牌",
                "The actual mid-lift Slash did not draw one ready path to its public portrait before release.");
            NoCommit(baseline, "held ready Slash");
            void ArrangeRemoved(GameSnapshot _)
            {
                if (model.Hand.Contains(card)) return;
                Flush();
                Assert(!surface.IsLoaded && Math.Abs(((TranslateTransform)surface.RenderTransform).Y - y) < .001,
                    "A gesture release lost its last loaded source pose before public feedback arrived.");
            }
            game.StateChanged += ArrangeRemoved;
            try { Assert(Aim("ReleaseCardAim", TargetPoint(targetSeat)), "The started legal Slash release did not consume its physical drop."); }
            finally { game.StateChanged -= ArrangeRemoved; }
            Assert(game.AcceptedCommands.Count == beforePaid + 1 && game.AcceptedCommands.Last() is PlayCardCommand paid &&
                paid.CardId == slashId && paid.TargetSeats.SequenceEqual(slash.TargetSeats) &&
                game.CardMovements.Count(move => move.CardId == slashId && move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Use) == 1 && !model.Hand.Any(item => item.Id == slashId),
                "The actual legal aim/drop did not submit precisely its fresh physical Slash and current target once.");
            Assert(model.BattleCues.Last(cue => cue.PublicCardId == slashId && cue.Kind == BattleCueKind.Card).PublicCard?.Id == slashId &&
                game.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Count(item => item.CardId == slashId && item.SourceSeat == 0) == 1,
                "The paid aim/drop did not expose exactly its real physical Slash declaration and public face.");
            ClearedAim();
            Assert(!Aim("ReleaseCardAim", TargetPoint(targetSeat)) && game.AcceptedCommands.Count == beforePaid + 1,
                "A repeated physical release committed a second use.");
            var paidViews = SafeViews(game);
            AdvanceFilm(.102); host.Capture(output, "card-aim-native-slash-launch", 1120);
            var expected = new Rect(origin.X + 1, origin.Y + 1, origin.Width - 2, origin.Height - 2);
            var faces = FlightFaces(CardKind.Slash).ToArray();
            Assert(faces.Any(face => NearRect(face.Bounds, expected) && face.Opacity > .99),
                $"The actual aim/drop first flight used a stale origin. Expected {expected}; actual [{string.Join(';', faces.Select(face => face.Bounds))}].");
            if (film is not null)
            {
                AdvanceFilm(1.96);
                var path = Path.Combine(output, "card-aim-native-slash.gif");
                using (var file = File.Create(path)) film.Save(file);
                BattleEffectsChecks.SetGifTiming(path, 4);
                using var stream = File.OpenRead(path);
                var decoded = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert(decoded.Frames.Count == 50 && decoded.Frames.All(frame => frame.PixelWidth == 728 && frame.PixelHeight == 481 &&
                    frame.Metadata is BitmapMetadata metadata && (ushort)metadata.GetQuery("/grctlext/Delay") == 4),
                    "The same actual Slash gesture recording lost its fifty native frames or 40ms compact encoding.");
                Assert(!FilmPixels(1).SequenceEqual(FilmPixels(2)) && !FilmPixels(2).SequenceEqual(FilmPixels(3)) &&
                    !FlightFaces(CardKind.Slash).Any(), "The native gesture movie lost its moving guide/actual flight or failed to expire.");
                byte[] FilmPixels(int index)
                {
                    var bitmap = new FormatConvertedBitmap(decoded.Frames[index], PixelFormats.Pbgra32, null, 0);
                    var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
                }
            }
            Assert(game.AcceptedCommands.Count == beforePaid + 1 && SafeViews(game).SequenceEqual(paidViews),
                "Sampling the committed aim/drop flight changed commands or any safe player view.");
            Console.WriteLine($"Native loaded aim Slash: seed721019, physical{slashId}->seat{targetSeat}, one command {beforePaid}->{game.AcceptedCommands.Count}, " +
                $"50ms current Y={y:F3}, origin={origin}, opaque firstflight={expected}; {paidViews.Length} safe views unchanged; actual WPF capture, coordinate entries, no hardware injection.");

            Reload(); baseline = Boundary();
            var targetless = Engine(model).GetHumanLegalActions().FirstOrDefault(action => action.Kind is LegalActionKind.DrawTwo or LegalActionKind.Equip &&
                action.CardId is > 0 && action.ConversionSource is null &&
                (action.PlayedCardKind is null || action.PlayedCardKind == Card(action.CardId.Value).Kind) &&
                action.TargetSeat is null && action.TargetSeats.Count == 0);
            Assert(targetless?.CardId is { }, "The already saved fixed hand has no actual zero-target DrawTwo/equipment; do not search another seed.");
            var untargetedCard = Card(targetless!.CardId!.Value); var untargetedGame = Engine(model);
            ArmAndPress(untargetedCard);
            var open = OpenTablePoint(); Started(open);
            host.Capture(output, "card-aim-native-targetless-ready", 1120);
            Assert(guide.IsPointerAimValid && guide.PointerAimAnchor is not null && guide.PointerAimLabel == "松开出牌",
                "The actual no-target card did not authorize its visible open-table release.");
            Assert(Aim("ReleaseCardAim", open) && untargetedGame.AcceptedCommands.Count == baseline.Accepted + 1 &&
                untargetedGame.AcceptedCommands.Last() is PlayCardCommand free && free.CardId == untargetedCard.Id && free.TargetSeats.Count == 0,
                "The actual open-table drop did not submit exactly the current physical card's zero-target action once.");
            ClearedAim();
            Console.WriteLine($"Native loaded targetless aim: actual {untargetedCard.Id}/{untargetedCard.Kind}, open-table{open}, one paid command; " +
                "under-threshold/restored-draft/illegal/sidebar/outside/multi-target/Escape/right/capture-loss/help/sort/scroll/resize/load boundaries passed.");

            void AdvanceFilm(double seconds)
            {
                if (film is not null)
                    while (nextFrame < 50 && nextFrame * .04 <= seconds + .0000001)
                    {
                        Advance(nextFrame * .04);
                        var metadata = new BitmapMetadata("gif");
                        if (nextFrame == 0)
                        {
                            metadata.SetQuery("/appext/application", System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                            metadata.SetQuery("/appext/data", new byte[] { 3, 1, 0, 0, 0 });
                        }
                        var bitmap = new RenderTargetBitmap(1120, 740, 96, 96, PixelFormats.Pbgra32);
                        root.UpdateLayout(); root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render); bitmap.Render(root);
                        Assert(root.RenderSize == new Size(1120, 740), "The actual aim movie lost the fixed native output geometry.");
                        film.Frames.Add(BitmapFrame.Create(new TransformedBitmap(bitmap, new ScaleTransform(.65, .65)), null, metadata, null)); nextFrame++;
                    }
                Advance(seconds);
            }
            void Advance(double seconds)
            {
                Assert(seconds >= filmTime - .0000001, "The native gesture movie tried to rewind an accepted command clock.");
                clock.Advance(Math.Max(0, seconds - filmTime)); filmTime = seconds; RefreshCards(); guide.InvalidateVisual(); combat.InvalidateVisual();
            }
        }
        finally { Cancel(); }

        LegalAction Slash() => Engine(model).GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash &&
            action.CardId is > 0 && action.ConversionSource is null && Card(action.CardId.Value).Kind == CardKind.Slash);
        CardViewModel Card(int id) => model.Hand.Single(card => card.Id == id);
        CardLiftSurface Surface(CardViewModel card) => Find<CardLiftSurface>(Find<Button>(viewport).Single(button =>
            button.Name == "CardButton" && ReferenceEquals(button.DataContext, card))).Single();
        Button Seat(int seat) => Find<Button>(root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == seat);
        Point TargetPoint(int seat) => Center(Bounds(Seat(seat), guide));
        bool Aim(string name, Point point) => (bool)typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [point])!;
        void Cancel() => typeof(MainWindow).GetMethod("CancelCardAim", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        void ShareClock() { foreach (var surface in Find<CardLiftSurface>(viewport)) surface.Clock = clock; }
        void RefreshCards() { foreach (var surface in Find<CardLiftSurface>(viewport)) refresh.Invoke(surface, null); }
        void Flush()
        {
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded); ShareClock(); RefreshCards();
            guide.InvalidateVisual(); combat.InvalidateVisual();
        }
        void ClearDraft() { Cancel(); model.ClearSelectionCommand.Execute(null); Flush(); }
        Point ArmAndPress(CardViewModel card)
        {
            Surface(card).BringIntoView(); Flush();
            var button = Find<Button>(viewport).Single(button => button.Name == "CardButton" && ReferenceEquals(button.DataContext, card));
            var slot = Bounds(button, panel); var point = new Point(slot.Left + 3, 80);
            diagnosticCard = card; diagnosticSource = button;
            var armed = typeof(MainWindow).GetMethod("ArmCardAim", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [panel, point]);
            Assert(armed is true,
                $"The actual loaded physical card {card.Id} could not arm at its real visible hand slot {point}.");
            var pressed = press.Invoke(panel, [point]);
            Assert(pressed is true, "The actual armed physical slot did not use its ordinary hand press command.");
            Flush(); return point;
        }
        void Started(Point point)
        {
            var started = Aim("UpdateCardAim", point);
            if (!started || !ReferenceEquals(Mouse.Captured, window)) TraceAim($"afterUpdate result={started} point={point}");
            Assert(started && ReferenceEquals(Mouse.Captured, window),
                $"The loaded native aim failed actual WPF capture: started={started}, captured={Mouse.Captured?.GetType().Name ?? "null"}, " +
                $"window loaded={window.IsLoaded}/visible={window.IsVisible}. Do not bypass capture in this check.");
        }
        void TraceAim(string stage)
        {
            // Read only the private presentation gesture for failure evidence;
            // it never supplies a card, target, snapshot or capture to this test.
            var gesture = typeof(MainWindow).GetField("_cardAim", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
            object? Property(string name) => gesture?.GetType().GetProperty(name)?.GetValue(gesture);
            var snapshot = Property("Snapshot") as GameSnapshot;
            var source = Property("Source") as Button ?? diagnosticSource;
            var card = Property("Card") as CardViewModel ?? diagnosticCard;
            Console.WriteLine($"Native aim diagnostic {stage}: gesture={gesture is not null}, started={Property("Started") ?? "absent"}, " +
                $"physical={card?.Id}, selected={card?.IsSelected}, currentCardRef={card is not null && model.Hand.Any(item => ReferenceEquals(item, card))}, " +
                $"snapshotSame={snapshot is not null && ReferenceEquals(snapshot, model.ChoiceSnapshot)}, " +
                $"snapshotRevision={snapshot?.Revision}, currentRevision={model.ChoiceSnapshot.Revision}, " +
                $"window={window.IsLoaded}/{window.IsVisible}, source={source?.IsLoaded}/{source?.IsVisible}/{source?.IsEnabled}, " +
                $"sourceCardSame={source is not null && ReferenceEquals(source.DataContext, card)}, " +
                $"captured={Mouse.Captured?.GetType().Name ?? "null"}, windowCaptured={ReferenceEquals(Mouse.Captured, window)}.");
        }
        void StartSlash() { ClearDraft(); ArmAndPress(Card(slashId)); Started(TargetPoint(targetSeat)); }
        void ClearedAim() => Assert(guide.PointerAimPoint is null && guide.PointerAimAnchor is null && !guide.IsPointerAimValid &&
            guide.PointerAimLabel.Length == 0 && !ReferenceEquals(Mouse.Captured, window) &&
            Find<Button>(root).Where(button => BattleFeedbackLayer.GetSeatAnchor(button) >= 0).All(button => !CardTargetingLayer.GetIsAimTarget(button)),
            "A retired native gesture retained its pointer, portrait mark, label or real mouse capture.");
        AimBoundary Boundary() => new(Engine(model), Engine(model).Revision, Engine(model).AcceptedCommands.Count,
            Engine(model).CardMovements.Count, SafeViews(Engine(model)), model.BattleCues.Count);
        void NoCommit(AimBoundary before, string stage) => Assert(ReferenceEquals(Engine(model), before.Engine) &&
            Engine(model).Revision == before.Revision && Engine(model).AcceptedCommands.Count == before.Accepted &&
            Engine(model).CardMovements.Count == before.Movements && SafeViews(Engine(model)).SequenceEqual(before.Views) &&
            model.BattleCues.Count == before.Cues && combat.ActiveEffectCount == 0,
            $"The {stage} gesture changed a real command, movement, safe snapshot or battle cue before confirmation.");
        void Reload()
        {
            Cancel(); model.IsHelpOpen = false; viewport.ClearValue(FrameworkElement.WidthProperty);
            viewport.ClearValue(FrameworkElement.HorizontalAlignmentProperty); model.LoadManualGameCommand.Execute(null);
            Assert(!model.HasSaveError, "The existing native aim fixture could not reload its saved baseline: " + model.SaveStatus);
            model.IsMotionEnabled = true; root.Width = 1120; root.Height = 740; viewport.ScrollToLeftEnd(); Flush();
        }
        Point OpenTablePoint()
        {
            var table = Bounds((FrameworkElement)window.FindName("Battlefield"), guide);
            var predicate = typeof(CardTargetingLayer).GetMethod("IsOpenTablePoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
            for (var row = 1; row < 10; row++) for (var column = 1; column < 10; column++)
            {
                var point = new Point(table.Left + table.Width * column / 10, table.Top + table.Height * row / 10);
                if (predicate.Invoke(guide, [point]) is true) return point;
            }
            throw new InvalidOperationException("The actual native table has no public unprotected space for its no-target card.");
        }
        IEnumerable<(Rect Bounds, double Opacity)> FlightFaces(CardKind kind) => FlightImages(VisualTreeHelper.GetDrawing(combat), Matrix.Identity)
            .Where(image => ReferenceEquals(image.Image.ImageSource, CardArt.Get(kind))).Select(image => (image.Bounds, image.Opacity));
    }

    private sealed record AimBoundary(GameEngine Engine, long Revision, int Accepted, int Movements, string[] Views, int Cues);
    private static Point Center(Rect bounds) => new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
    private static bool NearRect(Rect actual, Rect expected) => Math.Abs(actual.X - expected.X) < .1 && Math.Abs(actual.Y - expected.Y) < .1 &&
        Math.Abs(actual.Width - expected.Width) < .1 && Math.Abs(actual.Height - expected.Height) < .1;
    private static IEnumerable<(ImageDrawing Image, Rect Bounds, double Opacity)> FlightImages(Drawing? drawing, Matrix transform, double opacity = 1)
    {
        if (drawing is ImageDrawing image) yield return (image, new MatrixTransform(transform).TransformBounds(image.Rect), opacity);
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            foreach (var child in group.Children) foreach (var nested in FlightImages(child, next, opacity * group.Opacity)) yield return nested;
        }
    }

    private static void ProtectedAnchorGeometry(string output)
    {
        // Control-only projection: exercise the existing public anchor contract
        // without fabricating a game event or modifying a real player's snapshot.
        var root = new Canvas { Width = 900, Height = 600, Background = Brushes.DarkSlateGray };
        var hand = Place(new Canvas { Width = 600, Height = 100 }, 100, 490);
        var card = Place(new Border { Width = 110, Height = 80, Background = Brushes.Wheat }, 350, 0);
        BattleFeedbackLayer.SetHandAnchor(card, 9); CardTargetingLayer.SetIsDraftCard(card, true); hand.Children.Add(card); root.Children.Add(hand);
        var target = Place(new Border { Width = 100, Height = 180, Background = Brushes.DarkSeaGreen }, 760, 80);
        BattleFeedbackLayer.SetSeatAnchor(target, 1); CardTargetingLayer.SetIsDraftTarget(target, true); root.Children.Add(target);
        var face = Place(new Border { Width = 80, Height = 120, Background = Brushes.Wheat }, 620, 240);
        BattleFeedbackLayer.SetPlayAnchor(face, 1); root.Children.Add(face);
        var hud = Place(new Border { Width = 120, Height = 80, Background = Brushes.DarkBlue }, 510, 300); root.Children.Add(hud);
        var action = Place(new Border { Width = 700, Height = 60, Background = Brushes.DarkRed }, 0, 430); root.Children.Add(action);
        var hint = Place(new TextBlock { Width = 700, Height = 24, Text = "确认前选择目标", TextAlignment = TextAlignment.Center, FontSize = 16, Foreground = Brushes.White }, 0, 394); root.Children.Add(hint);
        var guide = new CardTargetingLayer { Width = 900, Height = 600, AnchorRoot = root, HandRoot = hand, PhaseHud = hud, ActionBar = action, ActionHint = hint, IsDraftEnabled = true };
        root.Children.Add(guide);
        Render(root, 900, 600, Path.Combine(output, "card-targeting-protected-anchors.png"));
        var path = Curves(guide).Single();
        var unmasked = new PathGeometry([path]).GetWidenedPathGeometry(new Pen(Brushes.White, 1.5));
        foreach (var protectedElement in new[] { face, hud, action })
        {
            var area = Bounds(protectedElement, guide);
            Assert(unmasked.FillContainsWithDetail(new RectangleGeometry(area)) != IntersectionDetail.Empty,
                "The control-only protected-anchor fixture must actually cross each protected public face, HUD and action rectangle before clipping.");
            AssertClear(guide, area);
        }
        var hintWords = ContentBounds(hint, guide);
        Assert(!hintWords.IsEmpty && hintWords.Width < hint.ActualWidth / 2 &&
            Ink(VisualTreeHelper.GetDrawing(guide), dashedOnly: true).Any(ink =>
                new CombinedGeometry(GeometryCombineMode.Intersect, ink, new RectangleGeometry(Bounds(hint, guide))).GetArea() > 5),
            "A stretched action hint must protect its actual words while retaining visible dashed guide ink in the surrounding blank layout slot.");
        AssertClear(guide, hintWords);
        CardTargetingLayer.SetIsDraftTarget(target, false); Prepare(root, 900, 600);
        Assert(Curves(guide).Length == 0, "An attached target marker must refresh its sibling guide immediately without a layout change or frame timer.");
        CardTargetingLayer.SetIsDraftTarget(target, true); Prepare(root, 900, 600);
        Assert(Curves(guide).Length == 1, "The same attached marker must restore its sibling guide from the current geometry.");
        guide.IsDraftEnabled = false; Prepare(root, 900, 600);
        Assert(Curves(guide).Length == 0, "Disabling the real drafting context must remove paths even when old markers remain present.");
        guide.AnchorRoot = null; guide.HandRoot = null; root.Children.Clear();
    }

    private static T Place<T>(T element, double x, double y) where T : FrameworkElement
    { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); return element; }
    private static string[] SafeViews(GameEngine engine) => Enumerable.Range(0, engine.CreateSnapshot(0).Players.Count)
        .Select(seat => SnapshotJson.Serialize(engine.CreateSnapshot(seat))).ToArray();
    private static Rect Bounds(FrameworkElement? element, Visual guide) => element is null || element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0
        ? Rect.Empty : element.TransformToVisual(guide).TransformBounds(new Rect(element.RenderSize));
    private static Rect ContentBounds(FrameworkElement? element, Visual guide)
    {
        if (element is null || element.Visibility != Visibility.Visible) return Rect.Empty;
        var content = VisualTreeHelper.GetContentBounds(element);
        return content.IsEmpty ? Rect.Empty : element.TransformToVisual(guide).TransformBounds(content);
    }
    private static bool Near(Point actual, Point expected) => (actual - expected).Length < .25;
    private static Point End(PathFigure path) => ((BezierSegment)path.Segments.Single()).Point3;
    private static PathFigure[] Curves(CardTargetingLayer layer) => Shapes(VisualTreeHelper.GetDrawing(layer))
        .Where(shape => shape.Pen?.DashStyle.Dashes.Count > 0).Select(shape => PathGeometry.CreateFromGeometry(shape.Geometry).Figures.Single()).ToArray();
    private static IEnumerable<GeometryDrawing> Shapes(Drawing? drawing)
    {
        if (drawing is GeometryDrawing shape) yield return shape;
        if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var nested in Shapes(child)) yield return nested;
    }
    private static IEnumerable<Geometry> Ink(Drawing? drawing, Geometry? clip = null, bool dashedOnly = false)
    {
        if (drawing is DrawingGroup group)
        {
            var current = group.ClipGeometry is null ? clip : clip is null ? group.ClipGeometry : new CombinedGeometry(GeometryCombineMode.Intersect, clip, group.ClipGeometry);
            foreach (var child in group.Children) foreach (var ink in Ink(child, current, dashedOnly)) yield return ink;
        }
        if (drawing is GeometryDrawing { Pen: not null } shape && (!dashedOnly || shape.Pen.DashStyle.Dashes.Count > 0))
        {
            var stroke = shape.Geometry.GetWidenedPathGeometry(shape.Pen);
            yield return clip is null ? stroke : new CombinedGeometry(GeometryCombineMode.Intersect, stroke, clip);
        }
    }
    private static void AssertClear(CardTargetingLayer guide, Rect area)
    {
        if (area.IsEmpty) return;
        Assert(Ink(VisualTreeHelper.GetDrawing(guide)).All(ink => ink.FillContainsWithDetail(new RectangleGeometry(area)) == IntersectionDetail.Empty),
            "The targeting guide must leave actual public card faces, phase HUD and command regions completely readable.");
    }
    private static void Click(Button button)
    {
        Assert(button.IsEnabled && button.Command!.CanExecute(button.CommandParameter), "The native targeting fixture requires its actual enabled button command.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
    private static void Prepare(FrameworkElement root, int width, int height)
    {
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }
}
