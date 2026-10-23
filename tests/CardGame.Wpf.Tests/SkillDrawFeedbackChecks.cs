using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using NativeHost = NativeResponseStatusChecks.NativeHost;
using static Program;

internal static class SkillDrawFeedbackChecks
{
    public static void NativeSkillsAndColdRestore(string output)
    {
        NativeNormalDraw(output);
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Fixture.Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 80 && game.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Assert(game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "The fixed skill-draw fixture did not reach its first human Play boundary.");

        using var model = Restore(game.CreateCheckpoint(), registry);
        game = Engine(model);
        using var host = new NativeHost(model);
        host.Capture(output, "native-skill-draw-baseline", 1120);
        var panel = Find<HandPanel>(host.Root).Single(candidate => candidate.IsVisible);
        panel.Clock = host.Clock;
        panel.InvalidateArrange();
        host.Root.UpdateLayout();
        Assert(panel.IsLoaded && panel.ArrivalEnabled && host.Layer.IsPresentationActive && host.Layer.PlaybackTimeScale == 1 &&
            Find<CardLiftSurface>(panel).All(surface => ReferenceEquals(surface.Clock, host.Clock) && surface.OpacityMask is null),
            "The real loaded hand and feedback layer must share one clock without animating the restored baseline.");
        var captured = new List<BattleCue>();
        var visibilitySamples = new List<DrawVisibilitySample>();
        var movie = RecordMotion ? new GifBitmapEncoder() : null;
        var movieTime = 0;
        model.BattleCues.CollectionChanged += CaptureCues;
        try
        {
            var eventCursor = game.Events.Count;
            var movementCursor = game.CardMovements.Count;
            var before = game.CreateSnapshot(0).Players[0];
            var paidCards = model.Hand.Take(2).ToArray();
            var commandCount = game.AcceptedCommands.Count;
            model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == "classic:zhiheng"));
            foreach (var card in paidCards) model.SelectCardCommand.Execute(card);
            model.ConfirmSelectedCommand.Execute(null);
            Assert(game.AcceptedCommands.Count == commandCount + 1 && game.AcceptedCommands.Last() is UseProgramSkillCommand
                { SkillId: "classic:zhiheng", ActivationId: "exchange-owned-cards" } use &&
                use.CardIds.Order().SequenceEqual(paidCards.Select(card => card.Id).Order()),
                "The native Zhiheng draft must confirm the exact two physical cards through one real command.");
            ResumePlay(model);
            var after = game.CreateSnapshot(0).Players[0];
            Assert(after.HandCount == before.HandCount && after.Hp == before.Hp &&
                paidCards.All(card => after.Hand.All(remaining => remaining.Id != card.Id)),
                "Native Zhiheng must exchange two paid cards without changing hand count or HP.");
            var zhiheng = AssertDraws(game, eventCursor, movementCursor, captured, "skill-program.classic:zhiheng.Draw", 2);
            // These IDs come only from the owner's safe snapshot and locate local
            // hand surfaces. Public count-only cues remain independent of them.
            var exchangedIds = after.Hand.Select(card => card.Id).Except(before.Hand.Select(card => card.Id)).ToArray();
            // Preserve the separate pending-arrival boundary: sorting before
            // first layout must retain IDs from the actual committed snapshot.
            model.SortHandCommand.Execute(null);
            Sample(host, panel, output, visibilitySamples, "zhiheng", 0);
            var exchanged = Surfaces(panel, exchangedIds);
            Assert(exchanged.Length == 2 && exchanged.All(IsStartingArrival),
                "Real Zhiheng replacement did not start both local hand arrivals at the committed layout boundary.");
            AssertCommonEntrance(host, exchanged);
            var newCard = model.Hand.Single(card => card.Id == exchangedIds[0]);
            var cardButton = Find<Button>(panel).Single(button => button.Name == "CardButton" && ReferenceEquals(button.DataContext, newCard));
            var playableRevision = game.Revision;
            var playableCommands = game.AcceptedCommands.Count;
            Assert(newCard.IsPlayable && cardButton.IsEnabled && cardButton.Command!.CanExecute(cardButton.CommandParameter),
                "The newly drawn hand card must be selectable while its arrival and public flight are still running.");
            cardButton.Command!.Execute(cardButton.CommandParameter);
            Assert(newCard.IsSelected, "The actual hand button did not select the arriving card.");
            cardButton.Command.Execute(cardButton.CommandParameter);
            Assert(!newCard.IsSelected && game.Revision == playableRevision && game.AcceptedCommands.Count == playableCommands,
                "Selection during a draw animation must stay a local draft without submitting a card play.");
            var zhihengTime = 0;
            AdvanceDrawTo(host, panel, visibilitySamples, "zhiheng", ref zhihengTime, 160);
            var early = Sample(host, panel, output, visibilitySamples, "zhiheng", 160);
            Assert(early.Length == Math.Min(3, zhiheng.Sum(cue => cue.DrawCount)) &&
                exchanged.All(IsStartingArrival),
                "Real Zhiheng backs must travel while the new local faces remain hidden before handoff.");
            AdvanceDrawTo(host, panel, visibilitySamples, "zhiheng", ref zhihengTime, 300);
            var later = Sample(host, panel, output, visibilitySamples, "zhiheng", 300);
            Assert(later.Length == early.Length && later.Average(bounds => bounds.Y) > early.Average(bounds => bounds.Y) + 15 &&
                exchanged.All(IsStartingArrival),
                "The public backs must approach the hand before any new face appears.");
            AdvanceDrawTo(host, panel, visibilitySamples, "zhiheng", ref zhihengTime, 420);
            var arrived = Sample(host, panel, output, visibilitySamples, "zhiheng", 420);
            Assert(arrived.Length == early.Length && exchanged.All(IsStartingArrival),
                "The backs must reach the shared entrance before face unfolding starts.");
            AssertCommonEntrance(host, exchanged);
            AdvanceDrawTo(host, panel, visibilitySamples, "zhiheng", ref zhihengTime, 500);
            Sample(host, panel, output, visibilitySamples, "zhiheng", 500);
            AssertUnfolding(host, exchanged);
            AdvanceDrawTo(host, panel, visibilitySamples, "zhiheng", ref zhihengTime, 600);
            var settled = Sample(host, panel, output, visibilitySamples, "zhiheng", 600);
            Assert(settled.Length == 0 && exchanged.All(surface => surface.OpacityMask is null),
                "At 600ms the new faces must occupy their real slots and the public backs must be gone.");
            host.Clock.Advance(2);
            Sample(host, panel, output, visibilitySamples, "zhiheng-settled", 2600);
            Assert(host.Layer.ActiveEffectCount == 0 && !host.Layer.IsFrameTimerRunning,
                "Completed native skill feedback retained an effect or frame timer.");

            captured.Clear();
            eventCursor = game.Events.Count;
            movementCursor = game.CardMovements.Count;
            before = game.CreateSnapshot(0).Players[0];
            commandCount = game.AcceptedCommands.Count;
            model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == "classic:jiefan"));
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 0));
            model.ConfirmSelectedCommand.Execute(null);
            Assert(game.AcceptedCommands.Count == commandCount + 1 && game.AcceptedCommands.Last() is UseProgramSkillCommand
                { SkillId: "classic:jiefan", ActivationId: "aid-by-attack-range", TargetSeats: [0] },
                "The native Jiefan draft must confirm its actual beneficiary through one real command.");
            AdvanceUntilAidResponses(model, eventCursor, 1);
            var afterFirst = game.CreateSnapshot(0).Players[0];
            var firstIds = afterFirst.Hand.Select(card => card.Id).Except(before.Hand.Select(card => card.Id)).ToArray();
            Sample(host, panel, output, visibilitySamples, "jiefan", 0);
            MovieFrame();
            var first = Surfaces(panel, firstIds).Single();
            Assert(IsStartingArrival(first), "The first genuine Jiefan response did not start a local hand arrival.");
            AssertCommonEntrance(host, [first]);
            AdvanceJiefanTo(160);
            var firstBacks = Sample(host, panel, output, visibilitySamples, "jiefan", 160);
            var firstOpacity = ArrivalOpacity(first);
            Assert(firstBacks.Length == 1 && firstOpacity == 0,
                "The first genuine Jiefan draw must still be moving when the next response becomes available.");
            var responseRevision = game.Revision;
            var responseCommands = game.AcceptedCommands.Count;
            AdvanceUntilAidResponses(model, eventCursor, 2);
            ResumePlay(model);
            after = game.CreateSnapshot(0).Players[0];
            var secondIds = after.Hand.Select(card => card.Id).Except(afterFirst.Hand.Select(card => card.Id)).ToArray();
            Sample(host, panel, output, visibilitySamples, "jiefan-second", 160);
            MovieFrame();
            var second = Surfaces(panel, secondIds).Single();
            Assert(game.Revision > responseRevision && game.AcceptedCommands.Count > responseCommands &&
                ReferenceEquals(first, Surfaces(panel, firstIds).Single()) &&
                Math.Abs(ArrivalOpacity(first) - firstOpacity) < .00001 && IsStartingArrival(second),
                "A second real Jiefan response must commit immediately, preserve the first arrival and start only the new surface.");
            AdvanceJiefanTo(300);
            var bothBacks = Sample(host, panel, output, visibilitySamples, "jiefan", 300);
            Assert(bothBacks.Length == 2 && IsStartingArrival(first) && IsStartingArrival(second),
                "Overlapping real Jiefan draws must keep independent arrival ages and both public backs.");
            AdvanceJiefanTo(420);
            Sample(host, panel, output, visibilitySamples, "jiefan", 420);
            Assert(IsStartingArrival(first) && IsStartingArrival(second),
                "The second response must not make either face appear before its own public handoff.");
            AdvanceJiefanTo(500);
            Sample(host, panel, output, visibilitySamples, "jiefan", 500);
            AssertUnfolding(host, [first]);
            Assert(IsStartingArrival(second), "The later Jiefan card must keep its independent 420ms handoff delay.");
            AdvanceJiefanTo(600);
            Sample(host, panel, output, visibilitySamples, "jiefan", 600);
            MovieFrame();
            Assert(first.OpacityMask is null && ArrivalOpacity(second) > 0 && ArrivalOpacity(second) < 1,
                "The first draw must settle while the second is only beginning to unfold.");
            AdvanceJiefanTo(800);
            Sample(host, panel, output, visibilitySamples, "jiefan", 800);
            MovieFrame();
            Assert(first.OpacityMask is null && second.OpacityMask is null,
                "Both real Jiefan hand arrivals must settle after their own elapsed time.");
            var events = game.Events.Skip(eventCursor).Select(envelope => envelope.Payload).ToArray();
            var started = events.OfType<ProgramAttackRangeAidStartedEvent>().Single();
            var responses = events.OfType<ProgramAttackRangeAidChoiceResolvedEvent>().ToArray();
            after = game.CreateSnapshot(0).Players[0];
            Assert(started is { OwnerSeat: 0, TargetSeat: 0 } && started.ResponderSeats.Count == 2 &&
                responses.Length == started.ResponderSeats.Count &&
                responses.Select(response => response.ResponderSeat).SequenceEqual(started.ResponderSeats) &&
                responses.All(response => response.TargetSeat == 0 && response.DiscardedWeaponCardId is null && response.DrawnCardIds.Count == 1) &&
                after.HandCount == before.HandCount + 2 && after.Hp == before.Hp,
                "Both real weaponless Jiefan responders must draw one card for the beneficiary without fabricating healing.");
            AssertDraws(game, eventCursor, movementCursor, captured,
                "skill-program.classic:jiefan.RequestAttackRangeAid", 2);
            Assert(captured.Count(cue => cue.Kind == BattleCueKind.Skill && cue.SkillId == "classic:jiefan") == 1,
                "One real Jiefan activation must show its skill name once across the two response commits and final completion.");
            if (movie is not null)
            {
                var beforeMovieTail = SnapshotJson.Serialize(game.CreateSnapshot(0));
                AdvanceJiefanTo(1120);
                MovieFrame();
                var moviePath = Path.Combine(output, "native-jiefan-consecutive-draws.gif");
                using (var file = File.Create(moviePath)) movie.Save(file);
                BattleEffectsChecks.SetGifTiming(moviePath, 4);
                using var input = File.OpenRead(moviePath);
                var decoded = new GifBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert(decoded.Frames.Count == 29 && decoded.Frames.All(frame => frame.PixelWidth == 728 && frame.PixelHeight == 481 &&
                    Convert.ToInt32(((BitmapMetadata)frame.Metadata).GetQuery("/grctlext/Delay")) == 4) &&
                    SnapshotJson.Serialize(game.CreateSnapshot(0)) == beforeMovieTail,
                    "The real consecutive-draw movie must retain 29 continuous 40ms samples without advancing gameplay.");
            }
            var savedSnapshot = SnapshotJson.Serialize(game.CreateSnapshot(0));
            var savedRevision = game.Revision;
            Assert(host.Layer.ActiveEffectCount > 0, "The live reload must interrupt actual skill feedback.");
            model.SaveGameCommand.Execute(null);
            model.LoadManualGameCommand.Execute(null);
            var reloaded = Engine(model);
            Assert(!model.HasSaveError && !ReferenceEquals(game, reloaded) && reloaded.Revision == savedRevision &&
                SnapshotJson.Serialize(reloaded.CreateSnapshot(0)) == savedSnapshot && model.BattleCues.Count == 0,
                "Reloading the same revision in the existing window must preserve state and clear historical cues.");
            var reloadBacks = Sample(host, panel, output, visibilitySamples, "same-revision-reload", 0);
            Assert(ReferenceEquals(panel, Find<HandPanel>(host.Root).Single(candidate => candidate.IsVisible)) &&
                reloadBacks.Length == 0 && Find<CardLiftSurface>(panel).All(surface => surface.OpacityMask is null) &&
                host.Layer.ActiveEffectCount == 0 && !host.Layer.IsFrameTimerRunning,
                "A real hand source reset must clear arrivals and flights even when the loaded revision is unchanged.");
            game = reloaded;
            host.Clock.Advance(2);
            Sample(host, panel, output, visibilitySamples, "jiefan-settled", 2450);
            Assert(host.Layer.ActiveEffectCount == 0 && !host.Layer.IsFrameTimerRunning,
                "The consecutive native draw flights retained an effect or timer after completion.");

            var saved = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
            using var restored = Restore(saved, registry);
            Assert(SnapshotJson.Serialize(Engine(restored).CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)) &&
                Engine(restored).Events.Any(envelope => envelope.Payload is CardsDrawnEvent) && restored.BattleCues.Count == 0,
                "Cold recovery must retain the native state and draw history without replaying old draw cues.");
            var restoredLayer = new BattleFeedbackLayer { Cues = restored.BattleCues };
            restoredLayer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            try
            {
                Assert(restoredLayer.ActiveEffectCount == 0 && !restoredLayer.IsFrameTimerRunning,
                    "Opening a recovered table must not start a historical draw animation.");
            }
            finally { restoredLayer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
            File.WriteAllText(Path.Combine(output, "native-draw-visibility.json"), JsonSerializer.Serialize(visibilitySamples,
                new JsonSerializerOptions { WriteIndented = true }));
            var obscured = visibilitySamples.Where(sample => sample.BackOpacity >= .5 && sample.ClippedArea > 1 &&
                sample.UncoveredArea < sample.ClippedArea * .95).ToArray();
            Assert(obscured.Length == 0,
                "Later opaque captions covered a traveling card back: " + JsonSerializer.Serialize(obscured));
            var aidBanners = visibilitySamples.Where(sample => sample.Scene.StartsWith("jiefan", StringComparison.Ordinal))
                .SelectMany(sample => sample.SkillBanners).ToArray();
            Assert(aidBanners.Length > 0 && aidBanners.All(bounds => bounds.Zip(aidBanners[0])
                .All(pair => Math.Abs(pair.First - pair.Second) < .5)),
                "The skill banner must stay in one readable position while consecutive backs travel along the same path.");
            Console.WriteLine("Native skill draws: seed31, Zhiheng paid2/drew2, Jiefan two real responses 160ms apart; " +
                "loaded MainWindow hand/flight sampled through 0/160/300/420/500/600/800ms, hidden faces before handoff, visible unfolding, immediate selection and continuation, public quantity, same-revision live reload and cold non-replay passed. " +
                "The existing hidden native host never becomes a foreground gameplay session.");
        }
        finally { model.BattleCues.CollectionChanged -= CaptureCues; }

        void CaptureCues(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems is not null)
                captured.AddRange(args.NewItems.OfType<BattleCue>());
        }

        void AdvanceJiefanTo(int target)
        {
            while (movieTime < target)
            {
                var next = Math.Min((movieTime / 20 + 1) * 20, target);
                AdvanceDrawTo(host, panel, visibilitySamples, "jiefan-continuous", ref movieTime, next);
                if (movieTime < target && movieTime % 40 == 0) MovieFrame();
            }
        }

        void MovieFrame()
        {
            if (movie is null) return;
            foreach (var surface in Find<CardLiftSurface>(panel)) surface.InvalidateVisual();
            host.Layer.InvalidateVisual();
            var bitmap = host.MovieFrame();
            visibilitySamples.AddRange(DrawVisibility(VisualTreeHelper.GetDrawing(host.Layer), "jiefan-continuous", movieTime));
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/grctlext/Delay", (ushort)4); metadata.SetQuery("/grctlext/Disposal", (byte)2);
            movie.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        }
    }

    private static void NativeNormalDraw(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(drawPerTurn: 2));
        var setup = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Fixture.Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(setup.Submit(new StartGameCommand()));
        Accept(setup.Submit(new SelectGeneralCommand(0, Fixture.Owner, setup.Revision, setup.PendingDecision!.PromptId)));
        for (var step = 0; step < 40 && !setup.Events.Any(envelope => envelope.Payload is SetupCompletedEvent); step++)
            Accept(setup.Submit(new AdvanceOneStepCommand(setup.Revision)));
        Assert(setup.CreateSnapshot(0) is { Status: EngineStatus.Running, Phase: TurnPhase.NotStarted } &&
            setup.Events.Any(envelope => envelope.Payload is SetupCompletedEvent) && setup.CreateSnapshot(0).Players[0].HandCount == 4 &&
            !setup.Events.Any(envelope => envelope.Payload is CardsDrawnEvent),
            "The fixed normal-draw fixture must stop after setup and before the first actual draw phase.");
        using var model = Restore(setup.CreateCheckpoint(), registry);
        var game = Engine(model);
        using var host = new NativeHost(model);
        host.Capture(output, "native-normal-draw-baseline", 1120);
        var panel = Find<HandPanel>(host.Root).Single(candidate => candidate.IsVisible);
        panel.Clock = host.Clock;
        panel.InvalidateArrange(); host.Root.UpdateLayout();
        Assert(panel.IsLoaded && panel.ArrivalEnabled &&
            Find<CardLiftSurface>(panel).All(surface => surface.OpacityMask is null),
            "The normal draw must start with the real restored initial hand, without replaying the initial deal.");
        var before = game.CreateSnapshot(0).Players[0];
        var eventCursor = game.Events.Count;
        var movementCursor = game.CardMovements.Count;
        var commandCount = game.AcceptedCommands.Count;
        var captured = new List<BattleCue>();
        model.BattleCues.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems is not null)
                captured.AddRange(args.NewItems.OfType<BattleCue>());
        };
        ResumePlay(model);
        var after = game.CreateSnapshot(0).Players[0];
        Assert(game.AcceptedCommands.Count > commandCount && after.HandCount == before.HandCount + 2 &&
            after.Hp == before.Hp && model.CanEndTurn,
            "The real StepAi continuation must commit the normal draw and expose Play immediately.");
        AssertDraws(game, eventCursor, movementCursor, captured, CardMoveReasons.Draw.Value, 2);
        var ids = after.Hand.Select(card => card.Id).Except(before.Hand.Select(card => card.Id)).ToArray();
        var samples = new List<DrawVisibilitySample>();
        var stable = SnapshotJson.Serialize(game.CreateSnapshot(0));
        var time = 0;
        CardLiftSurface[] incoming = [];
        foreach (var target in new[] { 0, 160, 300, 420, 500, 600 })
        {
            AdvanceDrawTo(host, panel, samples, "normal-draw", ref time, target);
            if (target == 160)
            {
                var oldSlots = ids.ToDictionary(id => id, id => model.Hand.ToList().FindIndex(card => card.Id == id));
                var beforeSortCommands = game.AcceptedCommands.Count;
                model.SortHandCommand.Execute(null);
                Assert(ids.Any(id => oldSlots[id] != model.Hand.ToList().FindIndex(card => card.Id == id)) &&
                    game.AcceptedCommands.Count == beforeSortCommands,
                    "The real 160ms sort must move an arriving card to a different slot without committing a game command.");
            }
            var backs = Sample(host, panel, output, samples, "normal-draw", target);
            incoming = Surfaces(panel, ids);
            Assert(incoming.Length == 2 && model.CanEndTurn && SnapshotJson.Serialize(game.CreateSnapshot(0)) == stable,
                "Sampling a normal draw must preserve the committed hand and immediate Play commands.");
            if (target <= 420)
            {
                Assert(incoming.All(IsStartingArrival), "A normal draw revealed its faces before the public backs arrived.");
                AssertCommonEntrance(host, incoming);
                if (target > 0) Assert(backs.Length == 2, "A normal draw lost one of its two public card backs before handoff.");
            }
            else if (target == 500) AssertUnfolding(host, incoming);
            else Assert(backs.Length == 0 && incoming.All(surface => surface.OpacityMask is null &&
                Math.Abs(surface.RenderTransform.Value.OffsetX) < .1 && Math.Abs(surface.RenderTransform.Value.OffsetY) < .1),
                "The normal draw must finish with complete local faces and no remaining flight backs.");
            AssertDrawPointer(host, panel, ids, target);
        }
        // The same real fixture also checks the explicit reduced-motion route.
        File.WriteAllText(Path.Combine(output, "native-normal-draw-visibility.json"), JsonSerializer.Serialize(samples,
            new JsonSerializerOptions { WriteIndented = true }));
        model.IsMotionEnabled = false;
        var oldIds = game.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).ToHashSet();
        model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == "classic:zhiheng"));
        model.SelectCardCommand.Execute(model.Hand[0]);
        model.ConfirmSelectedCommand.Execute(null);
        ResumePlay(model);
        host.Capture(output, "native-normal-draw-motion-off", 1120);
        var immediateIds = game.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).Except(oldIds).ToArray();
        Assert(immediateIds.Length == 1 && Surfaces(panel, immediateIds).All(surface => surface.OpacityMask is null &&
            ((TranslateTransform)surface.RenderTransform).Y == 0) && host.Layer.ActiveEffectCount == 0,
            "Disabling motion must reveal newly obtained cards immediately without leaving a hidden arrival or flight.");
        Console.WriteLine("Native normal draw: seed31, real StepAi draw2; sort at160ms redirects an arriving card, common entrance420ms, visible unfolding500ms, new slots600ms, reduced motion immediate.");
    }

    private static void AssertDrawPointer(NativeHost host, HandPanel panel, int[] incomingIds, int milliseconds)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var cardButtonAt = typeof(HandPanel).GetMethod("CardButtonAt", flags)!.CreateDelegate<Func<Point, Button?>>(panel);
        var pressCardAt = typeof(HandPanel).GetMethod("PressCardAt", flags)!.CreateDelegate<Func<Point, bool>>(panel);
        var updatePointer = typeof(HandPanel).GetMethod("UpdatePointer", flags)!.CreateDelegate<Action<Point>>(panel);
        var model = host.Model;
        var game = Engine(model);
        var commands = game.AcceptedCommands.Count;
        var snapshot = SnapshotJson.Serialize(game.CreateSnapshot(0));
        var surfaces = Find<CardLiftSurface>(panel).ToDictionary(surface => ((CardViewModel)surface.DataContext).Id);
        var buttons = Find<Button>(panel).Where(button => button.Name == "CardButton")
            .ToDictionary(button => ((CardViewModel)button.DataContext).Id);
        var faces = model.Hand.Select(card => (Card: card, Surface: surfaces[card.Id],
            Bounds: surfaces[card.Id].TransformToVisual(panel).TransformBounds(new Rect(surfaces[card.Id].RenderSize)),
            ButtonBounds: buttons[card.Id].TransformToVisual(panel).TransformBounds(new Rect(buttons[card.Id].RenderSize))))
            .ToArray();
        var hidden = faces.Where(face => ArrivalOpacity(face.Surface) == 0).ToArray();
        var visible = faces.Where(face => ArrivalOpacity(face.Surface) > 0).ToArray();
        var viewport = host.Layer.HandRoot!;
        var clip = viewport.TransformToVisual(panel).TransformBounds(new Rect(viewport.RenderSize));
        var y = 60 * panel.CardScale;
        // Sample interiors between actual face/button edges, never inferred destination slots.
        var edges = faces.SelectMany(face => new[] { face.Bounds.Left, face.Bounds.Right,
            face.ButtonBounds.Left, face.ButtonBounds.Right }).Append(clip.Left).Append(clip.Right)
            .Where(x => x >= clip.Left && x <= clip.Right).Distinct().Order().ToArray();
        var points = edges.Zip(edges.Skip(1)).Where(pair => pair.Second - pair.First > 4)
            .Select(pair => new Point((pair.First + pair.Second) / 2, y)).Where(clip.Contains).ToArray();
        var candidates = points.Select(point => (Point: point,
            Expected: visible.LastOrDefault(face => face.Bounds.Contains(point)).Card))
            .Where(candidate => candidate.Expected is not null).ToArray();
        var covered = candidates.Where(candidate => hidden.Any(face => face.Bounds.Contains(candidate.Point))).ToArray();
        Assert(candidates.Length > 0 && (milliseconds is not (0 or 160) || covered.Length > 0),
            $"Normal draw@{milliseconds}ms must exercise visible old faces beneath the transparent arrival, including after sort.");
        foreach (var candidate in candidates)
        {
            var actual = cardButtonAt(candidate.Point)?.DataContext as CardViewModel;
            Assert(ReferenceEquals(actual, candidate.Expected),
                $"Normal draw@{milliseconds}ms pointer {candidate.Point} must resolve visible card {candidate.Expected.Id}, " +
                $"not card {actual?.Id}; hidden={string.Join(',', hidden.Select(face => face.Card.Id))}.");
        }
        // Exercise the same press entry point used by the native mouse preview handler.
        // Selection is cleared at the same clock instant so hover/lift cannot alter later animation samples.
        var presses = covered.Concat(candidates).DistinctBy(candidate => candidate.Expected.Id).ToArray();
        Assert(presses.Any(candidate => candidate.Expected.IsPlayable), "The real hand must contain a selectable visible card.");
        foreach (var candidate in presses)
        {
            Assert(!model.HasSelection && pressCardAt(candidate.Point) &&
                candidate.Expected.IsSelected == candidate.Expected.IsPlayable &&
                model.Hand.All(card => !card.IsSelected || ReferenceEquals(card, candidate.Expected)),
                $"Normal draw@{milliseconds}ms actual press must select only visible card {candidate.Expected.Id} when playable.");
            model.ClearSelectionCommand.Execute(null);
            updatePointer(new Point(-1, -1));
            Keyboard.ClearFocus();
            host.Root.UpdateLayout();
        }
        if (milliseconds >= 500)
            Assert(candidates.Any(candidate => incomingIds.Contains(candidate.Expected.Id)),
                "Arriving cards must become pointer candidates as soon as their faces are visible.");
        var emptyArrival = points.Where(point => hidden.Any(face => face.ButtonBounds.Contains(point)) &&
            !visible.Any(face => face.Bounds.Contains(point))).ToArray();
        if (milliseconds == 0)
            Assert(emptyArrival.Length > 0, "The real unsorted draw must expose an empty future button slot for hidden-arrival input coverage.");
        foreach (var point in emptyArrival)
            Assert(cardButtonAt(point) is null && pressCardAt(point) && !model.HasSelection,
                $"Normal draw@{milliseconds}ms must consume hidden button slot {point} without selecting an invisible card.");
        updatePointer(new Point(-1, -1));
        Assert(!pressCardAt(new Point(-10, y)) && !model.HasSelection &&
            game.AcceptedCommands.Count == commands && SnapshotJson.Serialize(game.CreateSnapshot(0)) == snapshot,
            "Pointer sampling must preserve ordinary empty margins, the committed hand and all game commands.");
        Console.WriteLine($"Native draw pointer@{milliseconds}ms: visibleCandidates={candidates.Length}; " +
            $"hiddenOverlap={covered.Length}; realPresses={presses.Length}; hiddenEmptySlots={emptyArrival.Length}.");
    }

    private static MainViewModel Restore(GameCheckpoint checkpoint, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, checkpoint));
        var model = new MainViewModel(autoAdvance: false, seed: 31, showSetup: true, saveStore: store,
            useExpandedContent: false, contentRegistry: registry)
        { IsMotionEnabled = true, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Assert(!model.HasSaveError && model.BattleCues.Count == 0, model.SaveStatus);
        return model;
    }

    private static void ResumePlay(MainViewModel model)
    {
        for (var step = 0; step < 80; step++)
        {
            var game = Engine(model);
            if (game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            var revision = game.Revision;
            Assert(model.CanStepAi, "The native skill-draw fixture unexpectedly requires another human choice.");
            model.StepAiCommand.Execute(null);
            Assert(game.Revision > revision, "A native skill continuation failed to advance through the view model.");
        }
        throw new InvalidOperationException("The fixed native skill-draw fixture did not resume Play.");
    }

    private static void AdvanceUntilAidResponses(MainViewModel model, int eventCursor, int expected)
    {
        var game = Engine(model);
        for (var step = 0; step < 80; step++)
        {
            var actual = game.Events.Skip(eventCursor).Count(envelope => envelope.Payload is ProgramAttackRangeAidChoiceResolvedEvent);
            if (actual >= expected)
            {
                Assert(actual == expected, "The real response fixture skipped the requested intermediate Jiefan boundary.");
                return;
            }
            var revision = game.Revision;
            var commands = game.AcceptedCommands.Count;
            Assert(model.CanStepAi, "The next actual Jiefan response must remain immediately available during the current flight.");
            model.StepAiCommand.Execute(null);
            Assert(game.Revision > revision && game.AcceptedCommands.Count == commands + 1,
                "The real Jiefan continuation failed to commit exactly one view-model command.");
        }
        throw new InvalidOperationException("The fixed Jiefan fixture did not reach the requested response boundary.");
    }

    private static BattleCue[] AssertDraws(GameEngine game, int eventCursor, int movementCursor,
        IEnumerable<BattleCue> captured, string reason, int expectedCount)
    {
        var events = game.Events.Skip(eventCursor).ToArray();
        var facts = events.Where(envelope => envelope.Payload is CardsDrawnEvent).ToArray();
        var actualMoves = game.CardMovements.Skip(movementCursor).Where(move =>
            move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0) && move.Reason.Value == reason).ToArray();
        var cues = captured.Where(cue => cue.Kind == BattleCueKind.Draw).ToArray();
        var expected = facts.GroupBy(envelope => (envelope.Revision, ((CardsDrawnEvent)envelope.Payload).PlayerSeat))
            .Select(group => (group.Key.Revision, Seat: group.Key.PlayerSeat, Count: group.Sum(envelope => ((CardsDrawnEvent)envelope.Payload).Count)))
            .OrderBy(item => item.Revision).ThenBy(item => item.Seat).ToArray();
        var observed = cues.Select(cue => (cue.Revision, Seat: cue.SourceSeat, Count: cue.DrawCount))
            .OrderBy(item => item.Revision).ThenBy(item => item.Seat).ToArray();
        Assert(actualMoves.Length == expectedCount && facts.Length > 0 && expected.Sum(item => item.Count) == expectedCount &&
            expected.All(item => item.Seat == 0 && item.Count > 0) && observed.SequenceEqual(expected),
            $"{reason}: actual DrawPile-to-Hand movements, committed public quantities and visible cues must agree exactly.");
        Assert(cues.All(cue => cue.TargetSeats.SequenceEqual([0]) && cue.ActorName == game.CreateSnapshot(0).Players[0].GeneralName &&
            cue.Label == $"摸牌 +{cue.DrawCount}" && cue is
            { HasDrawCards: true, IsInquiry: false, CardKind: null, PublicCardId: null, PublicCard: null, JudgmentCard: null,
                WeaponKind: null, HpDelta: 0, RemainingHp: null, MaximumHp: null }) &&
            !captured.Any(cue => cue.Kind == BattleCueKind.Recovery),
            "Skill draws may expose only the actual beneficiary and quantity, never hidden faces or a fake recovery.");
        for (var viewer = 0; viewer < 4; viewer++)
        {
            var projected = BattleCueProjector.Project(events, game.CreateSnapshot(viewer)).Where(cue => cue.Kind == BattleCueKind.Draw).ToArray();
            Assert(JsonSerializer.Serialize(projected) == JsonSerializer.Serialize(cues),
                "Changing the safe viewer changed public skill-draw feedback.");
        }
        Console.WriteLine($"Native draw reason={reason}: moves={actualMoves.Length}; facts={facts.Length}; cues={cues.Length}; quantities={string.Join(',', cues.Select(cue => cue.DrawCount))}.");
        foreach (var skill in captured.Where(cue => cue.Kind == BattleCueKind.Skill))
        {
            var source = events.Single(envelope => envelope.Sequence == skill.Sequence);
            var detail = source.Payload switch
            {
                SkillUsageConsumedEvent usage => $"owner={usage.SkillOwnerSeat},skill={usage.SkillId},usage={usage.UsageId},scope={usage.Scope},count={usage.Count}",
                ProgramSkillResolvedEvent resolved => $"frame={resolved.FrameId},owner={resolved.OwnerSeat},skill={resolved.SkillId},activation={resolved.ActivationId},completed={resolved.Completed}",
                _ => ""
            };
            Console.WriteLine($"Native skill cue: sequence={skill.Sequence}; revision={skill.Revision}; label={skill.Label}; " +
                $"source={source.Payload.GetType().Name}({detail}).");
        }
        return cues;
    }

    private static Rect[] Sample(NativeHost host, HandPanel panel, string output,
        List<DrawVisibilitySample> visibilitySamples, string label, int milliseconds)
    {
        foreach (var surface in Find<CardLiftSurface>(panel)) surface.InvalidateVisual();
        host.Layer.InvalidateVisual();
        host.Capture(output, $"native-{label}-table-{milliseconds}ms", 1120);
        AssertCompleteDrawFlight(host, label, milliseconds);
        visibilitySamples.AddRange(DrawVisibility(VisualTreeHelper.GetDrawing(host.Layer), label, milliseconds));
        var surfaces = Find<CardLiftSurface>(panel).ToArray();
        Assert(surfaces.Length == host.Model.Hand.Count && surfaces.All(surface => ReferenceEquals(surface.Clock, host.Clock)) &&
            !host.Layer.IsHitTestVisible && !host.Layer.Focusable && !Images(VisualTreeHelper.GetDrawing(host.Layer)).Any(),
            "The real card template must stay complete and share the feedback clock without leaking faces into the public effect layer or intercepting input.");
        var backs = Backs(VisualTreeHelper.GetDrawing(host.Layer), Matrix.Identity).ToArray();
        var captions = CaptionBounds(VisualTreeHelper.GetDrawing(host.Layer), Matrix.Identity).ToArray();
        var hand = host.Layer.HandRoot!;
        var handBounds = hand.TransformToVisual(host.Layer).TransformBounds(new Rect(hand.RenderSize));
        Assert(captions.All(bounds => !bounds.IntersectsWith(handBounds)),
            "Opaque skill and response captions must not cover the live hand viewport or arriving cards.");
        var skillBanners = CaptionBounds(VisualTreeHelper.GetDrawing(host.Layer), Matrix.Identity, skillOnly: true).ToArray();
        var portraits = Find<Button>(host.Root).Where(button => button.IsVisible && BattleFeedbackLayer.GetSeatAnchor(button) >= 0)
            .Select(button => button.TransformToVisual(host.Layer).TransformBounds(new Rect(button.RenderSize))).ToArray();
        Assert(skillBanners.All(banner => portraits.All(portrait => !banner.IntersectsWith(portrait))),
            "Moving an opaque skill banner away from the hand must not cover any other player's portrait.");
        if (label == "jiefan" && milliseconds == 300)
            Assert(skillBanners.Length > 0,
                "The consecutive-draw scene must retain the visible skill banner while avoiding the hand and portraits.");
        Console.WriteLine($"Native hand/flight {label}@{milliseconds}ms: revision={Engine(host.Model).Revision}; " +
            $"hand={surfaces.Length}; opacity={string.Join(',', surfaces.Select(surface => ArrivalOpacity(surface).ToString("F4")))}; " +
            $"liftY={string.Join(',', surfaces.Select(surface => ((TranslateTransform)surface.RenderTransform).Y.ToString("F2")))}; " +
            $"backs={backs.Length}; backY={string.Join(',', backs.Select(bounds => bounds.Y.ToString("F2")))}.");
        return backs;
    }

    private static void AdvanceDrawTo(NativeHost host, HandPanel panel, List<DrawVisibilitySample> samples,
        string scene, ref int current, int target)
    {
        while (current < target)
        {
            var next = Math.Min((current / 20 + 1) * 20, target);
            host.Clock.Advance((next - current) / 1000d); current = next;
            foreach (var surface in Find<CardLiftSurface>(panel)) surface.InvalidateVisual();
            host.Layer.InvalidateVisual();
            Render(host.Root, 1120, 740, null);
            AssertCompleteDrawFlight(host, scene, current);
            samples.AddRange(DrawVisibility(VisualTreeHelper.GetDrawing(host.Layer), scene, current));
        }
    }

    private static void AssertCompleteDrawFlight(NativeHost host, string scene, int milliseconds)
    {
        var shapes = Shapes(VisualTreeHelper.GetDrawing(host.Layer), Matrix.Identity, 1, null).ToArray();
        var backs = shapes.Where(shape => shape.Opacity > .001 && shape.Drawing.Brush is SolidColorBrush brush &&
            brush.Color == Color.FromRgb(48, 39, 29)).ToArray();
        if (milliseconds is > 0 and <= 420)
            Assert(backs.Length > 0, $"{scene}@{milliseconds}ms lost every public card back while faces are still hidden.");
        if (backs.Length == 0) return;
        var obstacles = ActualObstacles(host.Root).ToArray();
        foreach (var back in backs)
        {
            // Shapes includes the drawn outline, not merely the body fill.
            var clipped = Geometry.Combine(back.Raw, back.Visible, GeometryCombineMode.Exclude, null).GetArea();
            Assert(clipped < .1 && back.Raw.Bounds.Width >= 14.72 && back.Raw.Bounds.Height >= 20.48,
                $"{scene}@{milliseconds}ms must retain a complete, recognizable card back including its outline: " +
                $"bounds={back.Raw.Bounds}; clipped={clipped:F3}. " + RouteDiagnostic());
            foreach (var obstacle in obstacles)
                Assert(Geometry.Combine(back.Raw, new RectangleGeometry(obstacle.Bounds), GeometryCombineMode.Intersect, null).GetArea() < .1,
                    $"{scene}@{milliseconds}ms card back covers {obstacle.Name}: back={back.Raw.Bounds}; obstacle={obstacle.Bounds}.");
        }
        var sample = DrawVisibility(VisualTreeHelper.GetDrawing(host.Layer), scene, milliseconds).ToArray();
        Assert(sample.All(item => item.UncoveredArea >= item.RawArea - .1),
            $"{scene}@{milliseconds}ms an opaque caption covered a complete traveling card back: " + JsonSerializer.Serialize(sample));

        IEnumerable<(string Name, Rect Bounds)> ActualObstacles(DependencyObject? element)
        {
            if (element is null || ReferenceEquals(element, host.Layer.HandRoot) || ReferenceEquals(element, host.Layer.DrawPileAnchor) ||
                element is UIElement { Visibility: not Visibility.Visible } or UIElement { Opacity: <= 0 }) yield break;
            if (element is System.Windows.Controls.Primitives.ButtonBase button)
            {
                yield return ($"button {button.Name} seat={BattleFeedbackLayer.GetSeatAnchor(button)}",
                    button.TransformToVisual(host.Layer).TransformBounds(new Rect(button.RenderSize)));
                yield break;
            }
            if (element is TextBlock text)
            {
                foreach (var ink in GlyphBounds(VisualTreeHelper.GetDrawing(text), Matrix.Identity))
                    yield return ($"glyph {text.Name} '{text.Text}'", text.TransformToVisual(host.Layer).TransformBounds(ink));
                yield break;
            }
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
                foreach (var obstacle in ActualObstacles(VisualTreeHelper.GetChild(element, index))) yield return obstacle;
        }

        string RouteDiagnostic()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var cache = (System.Collections.IDictionary)typeof(BattleFeedbackLayer).GetField("_drawRouteCache", flags)!.GetValue(host.Layer)!;
            var routes = cache.Values.Cast<object>().Select(route => new
            {
                NeedsClip = route.GetType().GetProperty("NeedsClip")!.GetValue(route),
                Launch = route.GetType().GetField("_launch", flags)!.GetValue(route),
                Destination = route.GetType().GetField("_destination", flags)!.GetValue(route),
                Obstacles = route.GetType().GetField("_obstacles", flags)!.GetValue(route)
            });
            return JsonSerializer.Serialize(routes);
        }
    }

    private static IEnumerable<Rect> GlyphBounds(Drawing? drawing, Matrix transform)
    {
        if (drawing is GlyphRunDrawing glyph)
            yield return new MatrixTransform(transform).TransformBounds(glyph.GlyphRun.BuildGeometry().Bounds);
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            foreach (var child in group.Children)
                foreach (var bounds in GlyphBounds(child, next)) yield return bounds;
        }
    }

    private static CardLiftSurface[] Surfaces(HandPanel panel, IEnumerable<int> ownCardIds) => ownCardIds
        .Select(id => Find<CardLiftSurface>(panel).Single(surface => BattleFeedbackLayer.GetHandAnchor(surface) == id)).ToArray();

    private static double ArrivalOpacity(CardLiftSurface surface) => surface.OpacityMask?.Opacity ?? 1;
    private static bool IsStartingArrival(CardLiftSurface surface) =>
        ArrivalOpacity(surface) < .00001 && ((TranslateTransform)surface.RenderTransform).Y < 0;

    private static void AssertCommonEntrance(NativeHost host, IEnumerable<CardLiftSurface> surfaces)
    {
        var viewport = host.Layer.HandRoot!;
        var entry = viewport.TranslatePoint(new Point(viewport.ActualWidth / 2, 7), host.Layer);
        Assert(surfaces.All(surface => (surface.TranslatePoint(
            new Point(surface.ActualWidth / 2, surface.ActualHeight / 2), host.Layer) - entry).Length < 1),
            "New local faces must start at the same real hand entrance as public count-only flights.");
    }

    private static void AssertUnfolding(NativeHost host, IEnumerable<CardLiftSurface> surfaces)
    {
        var viewport = host.Layer.HandRoot!;
        var clip = viewport.TransformToVisual(host.Layer).TransformBounds(new Rect(viewport.RenderSize));
        foreach (var surface in surfaces)
        {
            var face = surface.TransformToVisual(host.Layer).TransformBounds(new Rect(surface.RenderSize));
            var visible = Rect.Intersect(face, clip);
            Assert(ArrivalOpacity(surface) > .1 && ArrivalOpacity(surface) < 1 &&
                ((TranslateTransform)surface.RenderTransform).Y < -.1 && !visible.IsEmpty &&
                visible.Width * visible.Height > face.Width * face.Height * .75,
                "The new face must visibly unfold toward its slot inside the real hand viewport, not remain clipped away.");
        }
    }

    private sealed record DrawVisibilitySample(string Scene, int Milliseconds, int BackIndex,
        double BackOpacity, double RawArea, double ClippedArea, double UncoveredArea, double[] BackBounds,
        double[][] SkillBanners);

    private static IEnumerable<DrawVisibilitySample> DrawVisibility(Drawing? drawing, string scene, int milliseconds)
    {
        var shapes = Shapes(drawing, Matrix.Identity, 1, null).ToArray();
        var banners = CaptionBounds(drawing, Matrix.Identity, skillOnly: true).Select(RectValues).ToArray();
        var backIndex = 0;
        for (var index = 0; index < shapes.Length; index++)
        {
            var shape = shapes[index];
            if (shape.Drawing.Brush is not SolidColorBrush back || back.Color != Color.FromRgb(48, 39, 29)) continue;
            var uncovered = shape.Visible;
            foreach (var later in shapes.Skip(index + 1))
                if (later.Opacity >= .5 && later.Drawing.Brush is SolidColorBrush brush &&
                    (brush.Color == Color.FromRgb(19, 31, 34) || brush.Color == Color.FromRgb(64, 50, 34)))
                    uncovered = Geometry.Combine(uncovered, later.Visible, GeometryCombineMode.Exclude, null);
            yield return new(scene, milliseconds, backIndex++, shape.Opacity, shape.Raw.GetArea(), shape.Visible.GetArea(),
                uncovered.GetArea(), RectValues(shape.Raw.Bounds), banners);
        }
        static double[] RectValues(Rect bounds) => [bounds.X, bounds.Y, bounds.Width, bounds.Height];
    }

    private static IEnumerable<(GeometryDrawing Drawing, Geometry Raw, Geometry Visible, double Opacity)> Shapes(
        Drawing? drawing, Matrix transform, double opacity, Geometry? clip)
    {
        if (drawing is GeometryDrawing shape)
        {
            var geometry = shape.Geometry.Clone();
            if (shape.Pen is not null)
                geometry = Geometry.Combine(geometry, geometry.GetWidenedPathGeometry(shape.Pen), GeometryCombineMode.Union, null);
            var existing = geometry.Transform?.Value ?? Matrix.Identity; existing.Append(transform);
            geometry.Transform = new MatrixTransform(existing);
            yield return (shape, geometry, clip is null ? geometry :
                Geometry.Combine(geometry, clip, GeometryCombineMode.Intersect, null), opacity);
        }
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            var nextClip = clip;
            if (group.ClipGeometry is { } local)
            {
                var actual = local.Clone(); actual.Transform = new MatrixTransform(next);
                nextClip = clip is null ? actual : Geometry.Combine(clip, actual, GeometryCombineMode.Intersect, null);
            }
            foreach (var child in group.Children)
                foreach (var nested in Shapes(child, next, opacity * group.Opacity, nextClip)) yield return nested;
        }
    }

    private static IEnumerable<Rect> Backs(Drawing? drawing, Matrix transform)
    {
        if (drawing is GeometryDrawing { Brush: SolidColorBrush brush, Geometry: RectangleGeometry back } &&
            brush.Color == Color.FromRgb(48, 39, 29)) yield return new MatrixTransform(transform).TransformBounds(back.Bounds);
        if (drawing is DrawingGroup group)
        {
            var nested = group.Transform?.Value ?? Matrix.Identity; nested.Append(transform);
            foreach (var child in group.Children) foreach (var bounds in Backs(child, nested)) yield return bounds;
        }
    }

    private static IEnumerable<ImageDrawing> Images(Drawing? drawing)
    {
        if (drawing is ImageDrawing image) yield return image;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children) foreach (var nested in Images(child)) yield return nested;
    }

    private static IEnumerable<Rect> CaptionBounds(Drawing? drawing, Matrix transform, bool skillOnly = false)
    {
        if (drawing is GeometryDrawing { Brush: SolidColorBrush brush, Geometry: RectangleGeometry rectangle } shape &&
            (brush.Color == Color.FromRgb(19, 31, 34) && (!skillOnly || shape.Pen?.Brush is SolidColorBrush ink &&
                ink.Color == Color.FromRgb(255, 203, 100)) || !skillOnly && brush.Color == Color.FromRgb(64, 50, 34)))
        {
            var bounds = new MatrixTransform(transform).TransformBounds(rectangle.Bounds);
            bounds.Inflate(3, 3); // Include the skill banner's offset shadow.
            yield return bounds;
        }
        if (drawing is DrawingGroup group)
        {
            var nested = group.Transform?.Value ?? Matrix.Identity; nested.Append(transform);
            foreach (var child in group.Children)
                foreach (var bounds in CaptionBounds(child, nested, skillOnly)) yield return bounds;
        }
    }

    private static void Accept(CommandResult result) => Assert(result.Accepted, result.Error?.Message ?? "Native skill-draw setup failed.");

    private sealed class Fixture(int drawPerTurn = 0) : IGameContentPackage
    {
        public const string Mode = "identity:skill-draw-feedback", Owner = "fixture:skill-draw-owner";
        public PackageManifest Manifest { get; } = new("fixture-skill-draw-feedback", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "classic:sun-quan", "classic:han-dang");
            builder.AddGeneral(new(Owner, "技能摸牌", "supporter", "classic:zhiheng", "wu", 4, ["classic:jiefan"]));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:skill-draw-peer-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "邻座", "supporter", "standard:none", "wei", 4));
            builder.AddDeck(new("fixture:skill-draw-deck", "固定原生牌堆", 4, drawPerTurn, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, index % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "技能摸牌", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:skill-draw-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
