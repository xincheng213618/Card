using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using DrawingGeometry = System.Windows.Media.Geometry;
using static Program;

internal static class BasicCardTransferChecks
{
    public static void NativeTransfers(string output)
    {
        foreach (var kind in new[] { CardKind.Snatch, CardKind.Dismantlement })
        {
            var (game, registry) = StalePromptChoiceChecks.CreatePublicTargetFixture(
                kind == CardKind.Snatch ? "standard:snatch" : "standard:dismantlement");
            foreach (var zone in new[] { CardZoneKind.Hand, CardZoneKind.Equipment })
                TargetEffect(output, game, registry, kind, zone);
        }
        HarvestReveal(output);
        Harvest(output);
    }

    private static void TargetEffect(string output, GameEngine source, ContentRegistry registry, CardKind kind, CardZoneKind zone)
    {
        using var model = Restore(source, registry);
        var game = Engine(model);
        using var host = new NativeResponseStatusChecks.NativeHost(model);
        // Keep the native regression at the narrow layout that exposed clipped transfers.
        ((Grid)host.Window.FindName("GameArea")).ColumnDefinitions[1].Width = new GridLength(300);
        var label = $"{kind}-{zone}";
        host.Capture(output, "basic-transfer-" + label + "-before", 1120);
        var hand = PrepareHand(host);
        var before = Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat)).ToArray();
        var weapon = before[0].Players[1].Equipment.Single(card => card.Kind == CardKind.Crossbow);
        var actionKind = kind == CardKind.Snatch ? LegalActionKind.Snatch : LegalActionKind.Dismantlement;
        var action = game.GetHumanLegalActions().First(candidate => candidate.Kind == actionKind && candidate.TargetSeat == 1 &&
            candidate.ConversionSource is null && (zone == CardZoneKind.Hand ? candidate.TargetCardId is null : candidate.TargetCardId == weapon.Id));
        model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == action.CardId));
        if (!model.Seats.Single(seat => seat.Seat == 1).IsSelectedTarget)
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 1));
        var choice = model.PublicTargetChoices.First(candidate => candidate.Cards.SequenceEqual([action.CardId!.Value]) &&
            (zone == CardZoneKind.Hand ? candidate.Parameters.ContainsKey("target-hand-slot") :
                candidate.Parameters.GetValueOrDefault("target-card-id") == weapon.Id.ToString()));
        Render(host.Root, 1120, 740, null);
        var origins = HandOrigins(hand);
        var eventCursor = game.Events.Count;
        var moveCursor = game.CardMovements.Count;
        var commandCount = game.AcceptedCommands.Count;
        Click(BoundButton(host.Root, model.SelectPublicTargetChoiceCommand, choice));
        Assert(game.AcceptedCommands.Count == commandCount + 1 && game.AcceptedCommands.Last() is PlayCardCommand played &&
            played.CardId == action.CardId && played.TargetSeats.SequenceEqual([1]) &&
            (zone != CardZoneKind.Equipment || played.TargetCardId == weapon.Id),
            "The actual public-target button must commit the native trick and exact selected target once.");
        ReachPlay(model);
        var events = game.Events.Skip(eventCursor).ToArray();
        var envelope = events.Single(item => kind == CardKind.Snatch
            ? item.Payload is TargetCardTakenEvent : item.Payload is TargetCardDiscardedEvent);
        var cue = model.BattleCues.Single(item => item.Sequence == envelope.Sequence);
        var declaration = events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Single();
        Assert(model.BattleCues.Single(item => item.Kind == BattleCueKind.Card && item.PublicCardId == action.CardId)
                .SourceCardUseResolutionId == declaration.ResolutionId &&
            cue.SourceCardUseResolutionId == declaration.ResolutionId,
            "The real trick declaration and its transfer must retain the same exact public use identity.");
        var after = game.CreateSnapshot(0);
        var removed = zone == CardZoneKind.Hand
            ? before[1].Players[1].Hand.Select(card => card.Id).Except(game.CreateSnapshot(1).Players[1].Hand.Select(card => card.Id)).Single()
            : weapon.Id;
        var expectedKind = kind == CardKind.Snatch ? BattleCueKind.CardTransfer : BattleCueKind.CardDiscard;
        var (resolution, eventSource, eventTarget, eventZone, publicId, publicKind) = envelope.Payload switch
        {
            TargetCardTakenEvent taken => (taken.ResolutionId, taken.SourceSeat, taken.TargetSeat, taken.FromZone, taken.PublicCardId, taken.PublicCardKind),
            TargetCardDiscardedEvent discarded => (discarded.ResolutionId, discarded.SourceSeat, discarded.TargetSeat, discarded.FromZone, discarded.PublicCardId, discarded.PublicCardKind),
            _ => throw new InvalidOperationException("The real target effect did not publish its typed event.")
        };
        Assert(eventSource == 0 && eventTarget == 1 && eventZone == zone && cue.Kind == expectedKind &&
            cue.SourceSeat == 1 && cue.TargetSeats.SequenceEqual(kind == CardKind.Snatch ? new[] { 0 } : []) &&
            cue.OriginZone == zone && cue.TransferCount == 1 && cue.SourceCardUseResolutionId == resolution &&
            cue.PublicCardId == publicId && cue.CardKind == publicKind &&
            (zone == CardZoneKind.Hand ? cue is { PublicCardId: null, PublicCard: null, CardKind: null } :
                cue.PublicCardId == weapon.Id && cue.CardKind == weapon.Kind &&
                (cue.PublicCard is null || JsonSerializer.Serialize(cue.PublicCard) == JsonSerializer.Serialize(weapon))),
            "Target transfer/discard feedback must use the actual losing seat, destination and public origin, with no hidden hand identity.");
        var moves = game.CardMovements.Skip(moveCursor).Where(move => move.CardId == removed).ToArray();
        Assert(moves.Any(move => move.From == new CardLocation(zone, 1) && move.To == CardLocation.Processing) &&
            moves.Any(move => move.From == CardLocation.Processing && move.To ==
                (kind == CardKind.Snatch ? CardLocation.Hand(0) : CardLocation.DiscardPile)) &&
            (kind == CardKind.Snatch ? after.Players[0].Hand.Any(card => card.Id == removed) :
                after.Players[0].Hand.All(card => card.Id != removed)) &&
            after.Players[1].Equipment.All(card => zone != CardZoneKind.Equipment || card.Id != removed),
            "The feedback must describe the native card movement, not an invented draw or public play.");
        AssertPublicProjection(events, before, game, cue);
        AssertOnlyTrickWasPlayed(model, events, action.CardId!.Value, removed);
        SampleEffect(host, hand, output, label, cue, origins, kind == CardKind.Snatch ? removed : null);
        AssertColdRestore(game, registry);
        Console.WriteLine($"Native {label}: seed31, event={envelope.Sequence}@{envelope.Revision}, source=1, " +
            $"destination={(kind == CardKind.Snatch ? "Hand0" : "Discard")}, public-id={cue.PublicCardId?.ToString() ?? "hidden"}.");
    }

    private static void HarvestReveal(string output)
    {
        var (source, registry) = StalePromptChoiceChecks.CreatePublicTargetFixture("standard:five_grains");
        foreach (var (motion, earlyChoice) in new[] { (true, false), (true, true), (false, false) })
        {
            using var model = Restore(source, registry);
            model.IsMotionEnabled = motion;
            var game = Engine(model);
            using var host = new NativeResponseStatusChecks.NativeHost(model);
            ((Grid)host.Window.FindName("GameArea")).ColumnDefinitions[1].Width = new GridLength(300);
            var label = earlyChoice ? "Harvest-reveal-early-choice" : motion ? "Harvest-reveal" : "Harvest-reveal-motion-off";
            host.Capture(output, "basic-transfer-" + label + "-before", 1120);
            var action = game.GetHumanLegalActions().First(candidate => candidate.Kind == LegalActionKind.FiveGrains);
            var commands = game.AcceptedCommands.Count;
            var cursor = game.Events.Count;
            model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == action.CardId));
            Assert(model.CanConfirmSelected && game.AcceptedCommands.Count == commands,
                "The native Five Grains card must prepare its actual declaration without committing the selection draft.");
            model.ConfirmSelectedCommand.Execute(null);
            Assert(game.AcceptedCommands.Count == commands + 1 && game.AcceptedCommands.Last() is PlayCardCommand play &&
                play.CardId == action.CardId && play.TargetSeats.SequenceEqual(action.TargetSeats),
                "The native Five Grains confirmation must submit its actual physical card and published targets once.");
            for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.SelectHarvestCard; step++)
            {
                Assert(model.CanStepAi, "The fixed native Five Grains reveal unexpectedly requires another human choice.");
                model.StepAiCommand.Execute(null);
            }
            Assert(game.PendingDecision is { Kind: DecisionKind.SelectHarvestCard, PlayerSeat: 0 },
                "The real Five Grains declaration must reach the first local public-card choice.");
            var revealed = game.Events.Skip(cursor).Select(envelope => envelope.Payload).OfType<CardsRevealedEvent>().Single();
            Assert(revealed.Cards.Count == 4 && revealed.Cards.Select(card => card.Id)
                .SequenceEqual(game.CreateSnapshot(0).PublicRevealedCards.Select(card => card.Id)),
                "The first displayed Harvest pool must be the four actual publicly revealed cards from this use.");
            var stable = Boundary();
            host.Capture(output, "basic-transfer-" + label + "-0ms", 1120);
            var buttons = model.PublicRevealedCards.Select(card => BoundButton(host.Root, model.SelectRevealedCardCommand, card)).ToArray();
            var surfaces = buttons.Select(button => Find<PublicRevealSurface>(button).SingleOrDefault()).ToArray();
            Assert(surfaces.Length == revealed.Cards.Count && surfaces.All(surface => surface is not null),
                "The first native Harvest layout needs a reveal surface for each public face instead of appearing instantly.");
            var faces = surfaces.Select(surface => surface!).ToArray();
            var bounds = buttons.Select(Bounds).ToArray();
            var clockOrigin = host.Clock.GetTimestamp();
            double[]? early = null;
            foreach (var milliseconds in earlyChoice ? new[] { 0, 80, 160 } : motion ? new[] { 0, 80, 160, 600 } : new[] { 0, 600 })
            {
                var targetTick = clockOrigin + (long)Math.Ceiling(milliseconds / 1000d * host.Clock.TimestampFrequency);
                var ticks = targetTick - host.Clock.GetTimestamp();
                if (ticks > 0) host.Clock.Advance((ticks + .25) / host.Clock.TimestampFrequency);
                var progress = faces.Select(face => face.RevealProgress).ToArray();
                foreach (var face in faces) face.InvalidateVisual();
                if (milliseconds != 0 && !earlyChoice) host.Capture(output, $"basic-transfer-{label}-{milliseconds}ms", 1120);
                else if (milliseconds != 0) Render(host.Root, 1120, 740, null);
                for (var index = 0; index < buttons.Length; index++)
                {
                    var button = buttons[index];
                    var card = (CardViewModel)button.CommandParameter;
                    var actual = revealed.Cards.Single(candidate => candidate.Id == card.Id);
                    Assert(button.IsLoaded && button.IsVisible && button.IsEnabled && Bounds(button) == bounds[index] &&
                        ReferenceEquals(button.Command, model.SelectRevealedCardCommand) && button.Command!.CanExecute(card) &&
                        ReferenceEquals(button.CommandParameter, model.PublicRevealedCards[index]) && HitButton(button) &&
                        card.Kind == actual.Kind && card.Name == actual.DisplayName && card.Rank == actual.RankText &&
                        card.Artwork is not null && Find<Image>(faces[index]).Any(image => ReferenceEquals(image.Source, card.Artwork)),
                        $"Harvest@{milliseconds}ms moved a hit target, changed its real face, or detached its original selectable card. " +
                        $"scene={label}; index={index}; id={card.Id}; loaded={button.IsLoaded}; visible={button.IsVisible}; enabled={button.IsEnabled}; " +
                        $"bounds={Bounds(button)}; originalBounds={bounds[index]}; sameBounds={Bounds(button) == bounds[index]}; " +
                        $"commandRef={ReferenceEquals(button.Command, model.SelectRevealedCardCommand)}; canExecute={button.Command?.CanExecute(card)}; " +
                        $"parameterRef={ReferenceEquals(button.CommandParameter, model.PublicRevealedCards[index])}; hit={HitButton(button)}; " +
                        $"hitPoint={VisibleHitPoint(button)?.ToString() ?? "none"}; " +
                        $"hitType={host.Root.InputHitTest(VisibleHitPoint(button) ?? new Point(-1, -1))?.GetType().Name ?? "null"}; " +
                        $"kind={card.Kind}/{actual.Kind}; name='{card.Name}'/'{actual.DisplayName}'; rank='{card.Rank}'/'{actual.RankText}'; " +
                        $"artwork={card.Artwork?.GetType().Name ?? "null"}; imageRefs=[{string.Join(',', Find<Image>(faces[index]).Select(image => ReferenceEquals(image.Source, card.Artwork)))}].");
                }
                if (!motion || milliseconds == 600)
                    Assert(progress.All(value => value == 1) && faces.All(face => face.Opacity == 1 && face.RenderTransform.Value.IsIdentity),
                        "Disabled or completed Harvest reveals must show every actual public face fully settled.");
                else if (milliseconds == 0)
                    Assert(progress.All(value => value == 0) && faces.All(face => face.Opacity is > 0 and < 1 &&
                        face.RenderTransform.Value.M11 is > .6 and < 1),
                        "The first live Harvest frame must begin visibly compact and translucent, before its final pose.");
                else if (milliseconds == 80)
                {
                    Assert(progress[0] is > 0 and < 1 && progress[^1] == 0,
                        "Harvest public faces must unfold in their actual revealed order with a visible stagger.");
                    early = progress;
                }
                else
                    Assert(early is not null && progress.Zip(early).All(pair => pair.First >= pair.Second) &&
                        progress[0] > early[0] && progress[^1] > 0 && progress[^1] < 1,
                        "The native Harvest faces must progress between sampled frames instead of snapping into place.");
                Assert(Boundary() == stable, "Animating the first public Harvest pool must not submit a command or change any safe player view.");
                Console.WriteLine($"Native {label}@{milliseconds}ms: progress={string.Join(',', progress.Select(value => value.ToString("F3")))}; stable hit targets={buttons.Length}.");
            }
            var selected = (CardViewModel)buttons[0].CommandParameter;
            var choice = model.HarvestChoices.Single(candidate => candidate.Cards.SequenceEqual([selected.Id]));
            var prompt = game.PendingDecision!.PromptId;
            var beforeChoiceProgress = faces.ToDictionary(face => face.CardId, face => face.RevealProgress);
            commands = game.AcceptedCommands.Count;
            Click(buttons[0]);
            Assert(game.AcceptedCommands.Count == commands + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                answer.PromptId == prompt && answer.Choice == choice.Id &&
                game.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == selected.Id),
                "The original native Harvest button must still choose its actual published card after the reveal.");
            if (earlyChoice)
            {
                Render(host.Root, 1120, 740, null);
                var remaining = RemainingFaces();
                Assert(remaining.Length == revealed.Cards.Count - 1 && remaining.All(face => face.CardId != selected.Id &&
                    Math.Abs(face.RevealProgress - beforeChoiceProgress[face.CardId]) < .000001 && face.RevealProgress is > 0 and < 1),
                    "Choosing a real Harvest card at 160ms must preserve the original reveal ages when the remaining faces rebuild.");
                var afterChoice = Boundary();
                host.Clock.Advance(.04);
                Render(host.Root, 1120, 740, null);
                Assert(remaining.All(face => face.RevealProgress > beforeChoiceProgress[face.CardId] + .05) && Boundary() == afterChoice,
                    "The remaining Harvest faces must continue the original clock after an early choice without advancing the match.");

                // The same tiny fixture naturally gives the next choice to an AI;
                // retain ages across that actual pick as well, without a new seed.
                var beforeAi = remaining.ToDictionary(face => face.CardId, face => face.RevealProgress);
                var aiCursor = game.Events.Count;
                for (var step = 0; step < 12 && !game.Events.Skip(aiCursor).Any(envelope =>
                    envelope.Payload is HarvestCardSelectedEvent { PlayerSeat: not 0 }); step++)
                {
                    Assert(model.CanStepAi, "The first real AI Harvest pick unexpectedly requires human input.");
                    model.StepAiCommand.Execute(null);
                }
                var aiPick = game.Events.Skip(aiCursor).Select(envelope => envelope.Payload)
                    .OfType<HarvestCardSelectedEvent>().Single(pick => pick.PlayerSeat != 0);
                Render(host.Root, 1120, 740, null);
                remaining = RemainingFaces();
                Assert(remaining.Length == revealed.Cards.Count - 2 && remaining.All(face => face.CardId != aiPick.CardId &&
                    Math.Abs(face.RevealProgress - beforeAi[face.CardId]) < .000001 && face.RevealProgress is > 0 and < 1),
                    "The first actual AI Harvest pick must preserve the original reveal ages of the remaining public faces.");
                var beforeDisable = Boundary();
                model.IsMotionEnabled = false;
                Render(host.Root, 1120, 740, null);
                Assert(remaining.All(face => face.RevealProgress == 1 && face.Opacity == 1 && face.RenderTransform.Value.IsIdentity) &&
                    Boundary() == beforeDisable,
                    "Disabling motion during the real Harvest entrance must settle every remaining face immediately without changing gameplay.");
                model.IsMotionEnabled = true;
                Render(host.Root, 1120, 740, null);
                Assert(RemainingFaces().All(face => face.RevealProgress == 1 && face.Opacity == 1 && face.RenderTransform.Value.IsIdentity) &&
                    Boundary() == beforeDisable,
                    "Re-enabling motion must not replay a public Harvest pool already settled by disabling animation.");
                Console.WriteLine($"Native Harvest early choice: Human0@160ms; AI{aiPick.PlayerSeat}@200ms; " +
                    $"original ages retained for {remaining.Length} faces; dynamic motion-off settled immediately without replay.");
            }

            Rect Bounds(FrameworkElement element) => element.TransformToVisual(host.Root).TransformBounds(new Rect(element.RenderSize));
            PublicRevealSurface[] RemainingFaces() => Find<PublicRevealSurface>(host.Root).Where(face => face.IsVisible).ToArray();
            bool HitButton(Button button)
            {
                if (VisibleHitPoint(button) is not { } point) return false;
                for (var current = host.Root.InputHitTest(point) as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
                    if (ReferenceEquals(current, button)) return true;
                return false;
            }
            Point? VisibleHitPoint(Button button)
            {
                var visible = Rect.Intersect(Bounds(button), new Rect(host.Root.RenderSize));
                for (var parent = VisualTreeHelper.GetParent(button); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                    if (parent is ScrollContentPresenter viewport) visible.Intersect(Bounds(viewport));
                return visible.IsEmpty || visible.Width <= 0 || visible.Height <= 0
                    ? null : new Point(visible.X + visible.Width / 2, visible.Y + visible.Height / 2);
            }
            string Boundary() => JsonSerializer.Serialize(new
            {
                game.Revision, Commands = game.AcceptedCommands, Movements = game.CardMovements,
                Views = Enumerable.Range(0, 4).Select(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer))).ToArray()
            });
        }
    }

    private static void Harvest(string output)
    {
        var (source, registry) = StalePromptChoiceChecks.CreatePublicTargetFixture("standard:five_grains");
        var action = source.GetHumanLegalActions().First(candidate => candidate.Kind == LegalActionKind.FiveGrains);
        Accept(source.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, source.Revision,
            source.PendingDecision!.PromptId, action.PlayedCardKind)));
        for (var step = 0; step < 40 && source.PendingDecision?.Kind != DecisionKind.SelectHarvestCard; step++)
            Accept(source.Submit(new AdvanceOneStepCommand(source.Revision)));
        Assert(source.PendingDecision is { Kind: DecisionKind.SelectHarvestCard, PlayerSeat: 0 },
            "The fixed native Five Grains must reach Human0's actual public-card choice.");
        // Restore while the cards are still publicly revealed. The later pick
        // must use that public cache, without consulting its new owner's hand.
        using var model = Restore(source, registry);
        var game = Engine(model);
        using var host = new NativeResponseStatusChecks.NativeHost(model);
        // Keep the native regression at the narrow layout that exposed clipped transfers.
        ((Grid)host.Window.FindName("GameArea")).ColumnDefinitions[1].Width = new GridLength(300);
        host.Capture(output, "basic-transfer-Harvest-before", 1120);
        var coldFaces = Find<PublicRevealSurface>(host.Root).Where(face => face.IsVisible).ToArray();
        Assert(coldFaces.Length == model.PublicRevealedCards.Count && coldFaces.Length > 0 &&
            coldFaces.All(face => face.RevealProgress == 1 && face.Opacity == 1 && face.RenderTransform.Value.IsIdentity),
            "Cold loading an already revealed Harvest pool must show settled faces without replaying its entrance.");
        var hand = PrepareHand(host);
        var before = Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat)).ToArray();
        var choice = model.HarvestChoices.First();
        var chosenId = choice.Cards.Single();
        var publicFace = before[0].PublicRevealedCards.Single(card => card.Id == chosenId);
        Assert(before.All(view => view.PublicRevealedCards.Any(card => card.Id == chosenId)) && model.BattleCues.Count == 0,
            "Cold loading the real Harvest prompt must retain its public faces without replaying history.");
        var origins = HandOrigins(hand);
        var commandCount = game.AcceptedCommands.Count;
        var eventCursor = game.Events.Count;
        var moveCursor = game.CardMovements.Count;
        var prompt = game.PendingDecision!.PromptId;
        var publicCard = model.PublicRevealedCards.Single(card => card.Id == chosenId);
        Click(BoundButton(host.Root, model.SelectRevealedCardCommand, publicCard));
        Assert(game.AcceptedCommands.Count == commandCount + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
            answer.PromptId == prompt && answer.Choice == choice.Id,
            "The actual Harvest card button must submit its original published choice once.");
        var events = game.Events.Skip(eventCursor).ToArray();
        var envelope = events.Single(item => item.Payload is HarvestCardSelectedEvent { PlayerSeat: 0 });
        var selected = (HarvestCardSelectedEvent)envelope.Payload;
        var cue = model.BattleCues.Single(item => item.Sequence == envelope.Sequence);
        Assert(selected.CardId == chosenId && cue is { Kind: BattleCueKind.CardTransfer, SourceSeat: -1, TransferCount: 1,
                OriginZone: CardZoneKind.Processing } && cue.TargetSeats.SequenceEqual([0]) &&
            cue.PublicCardId == chosenId && cue.CardKind == publicFace.Kind &&
            JsonSerializer.Serialize(cue.PublicCard) == JsonSerializer.Serialize(publicFace) &&
            cue.SourceCardUseResolutionId == selected.ResolutionId &&
            game.CardMovements.Skip(moveCursor).Count(move => move.CardId == chosenId &&
                move.From == CardLocation.Processing && move.To == CardLocation.Hand(0) && move.Reason == CardMoveReasons.HarvestPick) == 1 &&
            game.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == chosenId),
            "Harvest feedback must fly its actual previously revealed physical face from Processing to its chosen recipient.");
        AssertPublicProjection(events, before, game, cue);
        AssertOnlyTrickWasPlayed(model, events, action.CardId!.Value, chosenId);
        SampleEffect(host, hand, output, "Harvest", cue, origins, chosenId);
        ReachPlay(model);
        AssertColdRestore(game, registry);
        Console.WriteLine($"Native Harvest: seed31, cold public prompt, event={envelope.Sequence}@{envelope.Revision}, " +
            $"Processing->Hand0, public face={chosenId}:{publicFace.Kind}, no new public play.");
    }

    private static void AssertPublicProjection(EventEnvelope[] events, GameSnapshot[] before, GameEngine game, BattleCue expected)
    {
        for (var viewer = 0; viewer < 4; viewer++)
        {
            var state = new BattleCueProjectionState();
            state.ObservePublicView(before[viewer]);
            var actual = BattleCueProjector.Project(events, game.CreateSnapshot(viewer), state)
                .Single(cue => cue.Sequence == expected.Sequence);
            Assert(JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(expected),
                "Changing the viewer exposed a hidden transfer face or changed the actual public card movement feedback.");
        }
    }

    private static void AssertOnlyTrickWasPlayed(MainViewModel model, EventEnvelope[] events, int usedId, int movedId)
    {
        Assert(!BattleCueProjector.ProjectCardPlays(events, model.ChoiceSnapshot).Any(cue => cue.PublicCardId == movedId) &&
            model.RecentPlays.Count == 1 && model.RecentPlays[0].PublicCardId == usedId &&
            !model.BattleCues.Any(cue => cue.Kind is BattleCueKind.Card or BattleCueKind.Response && cue.PublicCardId == movedId) &&
            !model.BattleCues.Any(cue => cue.Kind == BattleCueKind.Draw),
            "Taking, discarding or picking a card must not manufacture a card use, played public row, or draw from the deck.");
    }

    private static void SampleEffect(NativeResponseStatusChecks.NativeHost host, HandPanel hand, string output,
        string label, BattleCue cue, Dictionary<int, (CardLiftSurface Surface, double X)> origins, int? gainedId)
    {
        var model = host.Model;
        var game = Engine(model);
        var commands = game.AcceptedCommands.Count;
        var views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        var samples = new List<string>();
        var time = 0;
        var clockOrigin = host.Clock.GetTimestamp();
        var wallSeconds = 0d;
        // Target fixtures commit the declaration and result before the injected
        // clock advances. Cold Harvest has no in-flight declaration to await.
        var delay = label == "Harvest" ? 0 : 333;
        var delayedLayout = label == "Snatch-Equipment";
        var changeSpeed = label == "Snatch-Hand" && !RecordMotion;
        if (delayedLayout)
            Assert(!Find<CardLiftSurface>(hand).Any(surface => ((CardViewModel)surface.DataContext).Id == gainedId),
                "The delayed-layout fixture must not generate its new native surface before advancing the clock.");
        CardLiftSurface? arrival = null;
        double previousOpacity = 0;
        var flightSamples = new Dictionary<int, TransferVisual>();
        var destinationAnchor = cue.Kind == BattleCueKind.CardDiscard ? host.Layer.DiscardPileAnchor! : host.Layer.HandRoot!;
        var destinationBounds = destinationAnchor.TransformToVisual(host.Layer).TransformBounds(new Rect(destinationAnchor.RenderSize));
        var destination = cue.Kind == BattleCueKind.CardDiscard
            ? new Point(destinationBounds.Left - 48, destinationBounds.Top - 24)
            : new Point(destinationBounds.Left + destinationBounds.Width / 2, destinationBounds.Top + 7);
        var movie = RecordMotion ? new GifBitmapEncoder() : null;
        int[] assertionTimes = new[] { 0, 20, 120, 240, Math.Max(0, delay - 1), delay + 20, delay + 120, delay + 240,
            delay + 420, delay + 500, delay + 600 }.Distinct().Order().ToArray();
        var movieFrames = (int)Math.Ceiling((delay + 840) / 40d) + 1;
        IEnumerable<int> sampleTimes = assertionTimes;
        if (movie is not null) sampleTimes = sampleTimes.Concat(Enumerable.Range(0, movieFrames).Select(index => index * 40));
        foreach (var milliseconds in sampleTimes.Distinct().Order())
        {
            // Advancing each scaled delta independently truncates TimeSpan ticks
            // and can leave the nominal final frame before its real deadline.
            wallSeconds += (milliseconds - time) / 1000d * model.BattleAnimationTimeScale;
            var targetTick = clockOrigin + (long)Math.Ceiling(wallSeconds * host.Clock.TimestampFrequency);
            var remainingTicks = targetTick - host.Clock.GetTimestamp();
            if (remainingTicks > 0) host.Clock.Advance((remainingTicks + .25) / host.Clock.TimestampFrequency);
            time = milliseconds;
            if (delayedLayout && milliseconds < 240) continue;
            foreach (var surface in Find<CardLiftSurface>(hand)) surface.InvalidateVisual();
            host.Layer.InvalidateVisual();
            Render(host.Root, 1120, 740, null);
            if (movie is not null && milliseconds % 40 == 0)
            {
                var metadata = new BitmapMetadata("gif");
                metadata.SetQuery("/grctlext/Delay", (ushort)4);
                metadata.SetQuery("/grctlext/Disposal", (byte)2);
                movie.Frames.Add(BitmapFrame.Create(host.MovieFrame(), null, metadata, null));
            }
            // Off-grid assertion frames do not alter the 40ms movie cadence.
            if (!assertionTimes.Contains(milliseconds)) continue;
            var flights = TransferVisuals(VisualTreeHelper.GetDrawing(host.Layer), cue.Kind,
                Matrix.Identity, 1, null).ToArray();
            var flightTime = milliseconds - delay;
            Assert(flights.Length == (flightTime <= 0 || flightTime >= 580 ? 0 : 1),
                $"Native {label} must wait for its exact declaration, then fly once: {milliseconds}ms, flight-age={flightTime}ms count={flights.Length}.");
            if (flights.SingleOrDefault() is { } flight)
            {
                flightSamples.Add(flightTime, flight);
                Assert(flight.Opacity > .1 && flight.VisibleArea >= flight.FullArea * .99,
                    $"Native {label} has a clipped or transparent transfer card at {milliseconds}ms: opacity={flight.Opacity}, visible-area={flight.VisibleArea}/{flight.FullArea}.");
                var hidden = cue.PublicCardId is null;
                Assert(hidden ? flight.Images.Length == 0 && flight.HasBack :
                    flight.Images.Length == 1 && ReferenceEquals(flight.Images[0], CardArt.Get(cue.CardKind)) && !flight.HasBack,
                    "The actual transfer drawing must show only a back for a hidden hand card and the already public artwork for equipment/Harvest.");
                if (flightTime is 420 or 500)
                    Assert((flight.Center - destination).Length < .1 &&
                        Math.Abs(flight.Bounds.Width - 18.4) < .1 && Math.Abs(flight.Bounds.Height - 25.6) < .1,
                        "The actual transfer card must reach the shared hand entry or discard destination at 420ms and fade there.");
                samples.Add($"{milliseconds}ms flight={flight.Center}, opacity={flight.Opacity:F3}, visible-area={flight.VisibleArea:F1}");
            }
            var current = Find<CardLiftSurface>(hand).ToDictionary(surface => ((CardViewModel)surface.DataContext).Id);
            foreach (var (cardId, old) in origins.Where(pair => current.ContainsKey(pair.Key)))
                Assert(ReferenceEquals(current[cardId], old.Surface) && (milliseconds != 0 || Math.Abs(X(current[cardId]) - old.X) < .1),
                    "A real card acquisition must keep surviving native hand surfaces and their current visible positions.");
            if (gainedId is { } arrivalId)
            {
                Assert(current.TryGetValue(arrivalId, out var gained), "The actually acquired card has no real local hand surface.");
                arrival ??= gained;
                var opacity = gained!.OpacityMask?.Opacity ?? 1;
                Assert(ReferenceEquals(arrival, gained) && opacity >= previousOpacity &&
                    (flightTime > 420 || opacity < .001) &&
                    (flightTime != 500 || opacity > .01 && opacity < .99) &&
                    (flightTime != 600 || gained.OpacityMask is null && opacity == 1),
                    $"The acquired real hand face must share the incoming flight's delayed clock, unfold once, and retain elapsed time across layout and speed changes: " +
                    $"{label} nominal={milliseconds}ms flight={flightTime}ms tick={host.Clock.GetTimestamp()} scale={model.BattleAnimationTimeScale:G17}, " +
                    $"opacity={opacity:G17}, previous={previousOpacity:G17}, mask-null={gained.OpacityMask is null}, pose={gained.RenderTransform.Value}.");
                if (flightTime is 500 or 600)
                {
                    var faceBounds = gained.TransformToVisual(host.Layer).TransformBounds(new Rect(gained.RenderSize));
                    var viewport = (FrameworkElement)host.Window.FindName("HandViewport");
                    var visible = Rect.Intersect(faceBounds,
                        viewport.TransformToVisual(host.Layer).TransformBounds(new Rect(viewport.RenderSize)));
                    Assert(!visible.IsEmpty && visible.Width > 10 && visible.Height > 10 &&
                        (flightTime != 600 || Math.Abs(gained.RenderTransform.Value.OffsetX) < .1 &&
                            Math.Abs(gained.RenderTransform.Value.OffsetY) < .1),
                        "The acquired hand face must actually enter the visible hand viewport and finish at its own slot.");
                }
                previousOpacity = opacity;
                samples.Add($"{milliseconds}ms arrival-opacity={opacity:F4}");
            }
            if (changeSpeed && (milliseconds == 120 || flightTime == 500))
            {
                var surface = current[gainedId!.Value];
                var opacity = surface.OpacityMask?.Opacity ?? 1;
                var pose = surface.RenderTransform.Value;
                model.SelectedPlaybackSpeed = milliseconds == 120 ? PlaybackSpeed.Fast : PlaybackSpeed.Normal;
                surface.InvalidateVisual(); host.Layer.InvalidateVisual();
                Render(host.Root, 1120, 740, null);
                Assert(Math.Abs((surface.OpacityMask?.Opacity ?? 1) - opacity) < .00001 &&
                    Math.Abs(surface.RenderTransform.Value.OffsetX - pose.OffsetX) < .001 &&
                    Math.Abs(surface.RenderTransform.Value.OffsetY - pose.OffsetY) < .001,
                    "Changing speed during the declaration wait or face unfold must preserve its exact visible pose and opacity.");
            }
            Assert(game.AcceptedCommands.Count == commands && Enumerable.Range(0, 4).All(seat =>
                views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
                "Card movement presentation changed an accepted command or a player-safe state.");
            if (flightTime is 20 or 240 or 420 or 500 or 600)
                host.Capture(output, $"basic-transfer-{label}-{milliseconds}ms", 1120);
        }
        Assert((flightSamples[20].Center - flightSamples[120].Center).Length > 8 &&
            (flightSamples[120].Center - flightSamples[240].Center).Length > 8 &&
            (flightSamples[240].Center - flightSamples[420].Center).Length > 8 &&
            flightSamples[20].Bounds.Width > flightSamples[240].Bounds.Width &&
            flightSamples[420].Opacity > .99 && flightSamples[500].Opacity is > .1 and < .9,
            "The native transfer must visibly travel and shrink through intermediate frames, then fade exactly once at its arrival.");
        if (movie is not null)
        {
            var path = Path.Combine(output, $"native-transfer-{label}.gif");
            using (var file = File.Create(path)) movie.Save(file);
            BattleEffectsChecks.SetGifTiming(path, 4);
            using var input = File.OpenRead(path);
            var decoded = new GifBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var expectedFrames = delayedLayout ? movieFrames - 6 : movieFrames;
            Assert(decoded.Frames.Count == expectedFrames && decoded.Frames.All(frame => frame.PixelWidth == 728 && frame.PixelHeight == 481 &&
                Convert.ToInt32(((BitmapMetadata)frame.Metadata).GetQuery("/grctlext/Delay")) == 4),
                "The native transfer movie must retain its 40ms cadence, without inserting assertion frames or changing the native clock.");
            Assert(game.AcceptedCommands.Count == commands && Enumerable.Range(0, 4).All(seat =>
                views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
                "Recording the transfer movie tail must not advance gameplay or change a player-safe state.");
        }
        Console.WriteLine($"Native {label} local hand: " + string.Join("; ", samples));
        double X(CardLiftSurface surface) => surface.TransformToAncestor(hand).Transform(new Point()).X;
    }

    private sealed record TransferVisual(Rect Bounds, double Opacity, double VisibleArea, double FullArea, ImageSource[] Images, bool HasBack)
    {
        public Point Center => new(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height / 2);
    }

    private static IEnumerable<TransferVisual> TransferVisuals(Drawing? drawing, BattleCueKind kind,
        Matrix transform, double opacity, DrawingGeometry? clip)
    {
        if (drawing is not DrawingGroup group) yield break;
        var next = group.Transform?.Value ?? Matrix.Identity;
        next.Append(transform);
        var nextOpacity = opacity * group.Opacity;
        var nextClip = clip;
        if (group.ClipGeometry is { } localClip)
        {
            var currentClip = InScene(localClip, next);
            nextClip = clip is null ? currentClip : DrawingGeometry.Combine(clip, currentClip, GeometryCombineMode.Intersect, null);
        }
        var accent = kind == BattleCueKind.CardDiscard ? Color.FromRgb(233, 148, 110) : Color.FromRgb(121, 218, 197);
        var body = group.Children.OfType<GeometryDrawing>().SingleOrDefault(part =>
            part.Geometry is RectangleGeometry rectangle && rectangle.Rect.Width == 46 && rectangle.Rect.Height == 64 &&
            part.Brush is SolidColorBrush fill && fill.Color == Color.FromRgb(31, 43, 43) &&
            part.Pen is { Thickness: 1.5, Brush: SolidColorBrush pen } && pen.Color == accent);
        if (body is not null)
        {
            var shape = InScene(body.Geometry, next);
            var visible = nextClip is null ? shape : DrawingGeometry.Combine(shape, nextClip, GeometryCombineMode.Intersect, null);
            var back = group.Children.OfType<GeometryDrawing>().Any(part => part.Brush is null &&
                part.Geometry is RectangleGeometry && part.Pen is { Thickness: .8 });
            yield return new(shape.Bounds, nextOpacity, visible.GetArea(), shape.GetArea(),
                group.Children.OfType<ImageDrawing>().Select(part => part.ImageSource).ToArray(), back);
            yield break;
        }
        foreach (var child in group.Children)
            foreach (var result in TransferVisuals(child, kind, next, nextOpacity, nextClip)) yield return result;
    }

    private static DrawingGeometry InScene(DrawingGeometry shape, Matrix transform)
    {
        var result = shape.CloneCurrentValue();
        var existing = result.Transform?.Value ?? Matrix.Identity;
        existing.Append(transform);
        result.Transform = new MatrixTransform(existing);
        return result;
    }

    private static HandPanel PrepareHand(NativeResponseStatusChecks.NativeHost host)
    {
        var hand = Find<HandPanel>(host.Root).Single(panel => panel.IsVisible);
        hand.Clock = host.Clock; hand.InvalidateArrange(); host.Root.UpdateLayout();
        Assert(hand.IsLoaded && hand.ArrivalEnabled && ReferenceEquals(hand.ArrivalTimeline, host.Layer.ArrivalTimeline) && Find<CardLiftSurface>(hand)
            .All(surface => ReferenceEquals(surface.Clock, host.Clock) && surface.OpacityMask is null),
            "The actual restored native hand must begin settled on the presentation clock.");
        return hand;
    }

    private static Dictionary<int, (CardLiftSurface Surface, double X)> HandOrigins(HandPanel hand) =>
        Find<CardLiftSurface>(hand).ToDictionary(surface => ((CardViewModel)surface.DataContext).Id,
            surface => (surface, surface.TransformToAncestor(hand).Transform(new Point()).X));

    private static MainViewModel Restore(GameEngine source, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, source.CreateCheckpoint()));
        var model = new MainViewModel(false, source.Seed, true, store, contentRegistry: registry,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = true, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Assert(!model.HasSaveError && model.BattleCues.Count == 0, model.SaveStatus);
        return model;
    }

    private static void ReachPlay(MainViewModel model)
    {
        for (var step = 0; step < 40 && Engine(model).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
        {
            Assert(model.CanStepAi, "The fixed native trick unexpectedly requires another human choice.");
            model.StepAiCommand.Execute(null);
        }
        Assert(Engine(model).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "The native trick did not return to the human Play boundary.");
    }

    private static void AssertColdRestore(GameEngine game, ContentRegistry registry)
    {
        using var restored = Restore(game, registry);
        Assert(restored.BattleCues.Count == 0 && Enumerable.Range(0, 4).All(seat =>
            SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(Engine(restored).CreateSnapshot(seat))),
            "Cold replay must retain the actual card transfer while leaving completed animations retired.");
    }

    private static Button BoundButton(DependencyObject root, ICommand command, object choice) => Find<Button>(root)
        .Single(button => button.IsVisible && ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, choice));
    private static void Click(Button button)
    {
        Assert(button.IsLoaded && button.IsVisible && button.IsEnabled && button.ActualWidth > 0 && button.ActualHeight > 0,
            "The actual native card choice must be loaded, rendered and enabled.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
    private static void Accept(CommandResult result) => Assert(result.Accepted, result.Error?.Message ?? "The fixed native transfer fixture failed.");
}
