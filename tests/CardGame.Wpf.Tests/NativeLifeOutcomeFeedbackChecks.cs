using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;
using NativeHost = NativeResponseStatusChecks.NativeHost;

internal static class NativeLifeOutcomeFeedbackChecks
{
    public static void NativeOutcomes(string output)
    {
        var errors = new BindingListener();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        try
        {
            var fixture = StableHandResponseFixture.Create(DecisionKind.RespondDodge, output, lifeOutcome: true);
            using var model = fixture.Model;
            model.IsMotionEnabled = true; model.SelectedPlaybackSpeed = PlaybackSpeed.Normal;
            using var host = new NativeHost(model);
            var baseline = Boundary(Engine(model));
            host.Capture(output, "life-native-before-hit", 1120);
            Assert(host.Layer.ActiveEffectCount == 0 && Engine(model).CreateSnapshot(0).Players[0] is { Hp: 1, IsAlive: true },
                "Cold native life setup replayed history or changed the actual one-HP survivor.");
            Rescue(host, output);
            model.LoadManualGameCommand.Execute(null); Flush(host);
            Assert(!model.HasSaveError && Same(Engine(model), baseline) && host.Layer.ActiveEffectCount == 0,
                "Reloading the actual pre-hit branch changed safe state or replayed its life history.");
            Death(host, output);
            Assert(errors.Errors.Count == 0, "Native life binding diagnostics: " + string.Join(Environment.NewLine, errors.Errors));
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(errors); }
    }

    private static void Rescue(NativeHost host, string output)
    {
        var model = host.Model; var game = Engine(model);
        var cursor = game.Events.Count; var before = game.AcceptedCommands.Count;
        var start = host.Clock.GetTimestamp();
        Assert(model.CanStepAi && game.CreateSnapshot(0) is { CurrentSeat: 1, Phase: TurnPhase.Play, PendingDecision: null },
            "The native last hit must begin before the real AI1 Play action, without a fabricated human response prompt.");
        model.StepAiCommand.Execute(null);
        var (damage, dying) = Entered(host, cursor);
        Assert(game.AcceptedCommands.Count == before + 1 && game.AcceptedCommands.Last() is AdvanceOneStepCommand && game.CreateSnapshot(0).PendingDecision is
            { Kind: DecisionKind.RescueDying, PlayerSeat: 0, TargetSeat: 0 } && game.CreateSnapshot(0).Players[0] is { Hp: 0, IsAlive: true },
            "One real AI action must commit its physical Slash, damage and an alive zero-HP native rescue prompt immediately.");
        AssertLifeCue(host, BattleCueKind.Dying, dying.ResolutionId, .72);
        var dyingBoundary = Boundary(game);
        var currentBoundary = dyingBoundary;
        var movie = RecordMotion ? new GifBitmapEncoder() : null;
        var movieIndex = 0;
        Sample(0, "life-native-hit-committed", false, false);
        Sample(.04, "life-native-hit-early", false, false);
        Sample(.18, "life-native-hit-middle", false, false);
        Sample(.44, "life-native-hit-start", false, false);
        Sample(.60, "life-native-hit-readable", false, false);
        Sample(.76, "life-native-dying-start", true, false);
        Sample(1.04, "life-native-dying-readable", true, false);
        host.Capture(output, "life-native-dying-readable", 1920);
        AssertCaptionSafety(host); Assert(Same(game, dyingBoundary), "Rendering dying at another size changed a safe view or command.");
        var peach = model.Hand.Single(card => card.Id == 45 && card.Kind == CardKind.Peach && card.IsPlayable);
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        var choice = prompt.Choices.Single(item => item.Cards.SequenceEqual(new[] { peach.Id }));
        model.SelectCardCommand.Execute(peach); Assert(model.CanConfirmSelected, "The real pending rescue did not accept its native Peach.");
        model.ConfirmSelectedCommand.Execute(null);
        Assert(game.AcceptedCommands.Count == before + 2 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
            answer.PromptId == prompt.PromptId && answer.Choice == choice.Id,
            "Native rescue must pay exactly the currently published physical Peach response.");
        model.ConfirmSelectedCommand.Execute(null);
        Assert(game.AcceptedCommands.Count == before + 2, "Repeated rescue confirmation paid twice.");
        var recovery = game.Events.Skip(cursor).Select(item => item.Payload).OfType<RecoveryAppliedEvent>().Single(item => item.TargetSeat == 0);
        var resolved = game.Events.Skip(cursor).Select(item => item.Payload).OfType<DyingResolvedEvent>().Single(item => item.ResolutionId == dying.ResolutionId);
        Assert(resolved.Survived && recovery is { SourceSeat: 0, Amount: 1, RemainingHp: 1 } &&
            game.CreateSnapshot(0).Players[0] is { Hp: 1, IsAlive: true } &&
            game.Events.Skip(cursor).Select(item => item.Payload).OfType<DyingResponseEvent>().Count(item => item.ResolutionId == dying.ResolutionId &&
                item.ResponderSeat == 0 && item.UsedPeach && item.PeachCardId == peach.Id) == 1 &&
            game.CardMovements.Count(item => item.CardId == peach.Id && item.From == CardLocation.Hand(0) && item.To == CardLocation.Processing) == 1 &&
            !game.Events.Skip(cursor).Any(item => item.Payload is PlayerDiedEvent { VictimSeat: 0 }),
            "One actual Peach must recover the native victim once, complete the same dying ID and never publish death.");
        Assert(game.Events.Single(item => ReferenceEquals(item.Payload, recovery)).Sequence <
            game.Events.Single(item => ReferenceEquals(item.Payload, resolved)).Sequence,
            "Rescue success preceded its actual recovery fact.");
        var recoveryCue = host.Model.BattleCues.Last(item => item.Kind == BattleCueKind.Recovery && item.TargetSeats.Contains(0));
        Assert(Math.Abs(Active(host.Layer).Single(item => item.Cue.Sequence == recoveryCue.Sequence).Delay - .28) < .0001,
            "The actual Peach recovery did not wait for its new physical card's arrival.");
        AssertLifeCue(host, BattleCueKind.Rescued, dying.ResolutionId, .56);
        Assert(!Active(host.Layer).Any(item => item.Cue.Kind == BattleCueKind.Dying && item.Cue.TargetSeats.Contains(0)),
            "The committed same-ID recovery retained its superseded transient dying pulse.");
        var rescuedBoundary = Boundary(game);
        currentBoundary = rescuedBoundary;
        Sample(1.08, "life-native-peach-flight-early", false, false);
        Sample(1.22, "life-native-peach-flight-middle", false, false);
        Sample(1.36, "life-native-recovery-early", false, false);
        Sample(1.50, "life-native-recovery-middle", false, false);
        Sample(1.64, "life-native-rescued-start", false, true);
        Sample(1.88, "life-native-rescued-readable", false, true);
        host.Capture(output, "life-native-rescued-readable", 1920); AssertCaptionSafety(host);
        Sample(3.56, "life-native-rescued-expired", false, false);
        Assert(host.Layer.ActiveEffectCount == 0 && Same(game, rescuedBoundary), "Life playback did not expire or altered the actual rescued state.");
        if (movie is not null)
        {
            var path = Path.Combine(output, "life-native-hit-rescue.gif");
            using (var file = File.Create(path)) movie.Save(file);
            BattleEffectsChecks.SetGifTiming(path, 4);
            using var stream = File.OpenRead(path);
            var decoded = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert(decoded.Frames.Count == 90 && decoded.Frames.All(frame => frame.PixelWidth == 728 && frame.PixelHeight == 481 &&
                frame.Metadata is BitmapMetadata metadata && (ushort)metadata.GetQuery("/grctlext/Delay") == 4),
                "The real last-hit/rescue recording lost its 90 forward-clock frames, compact dimensions or 40ms timing.");
            Assert(!Pixels(decoded.Frames[1]).SequenceEqual(Pixels(decoded.Frames[20])) &&
                !Pixels(decoded.Frames[35]).SequenceEqual(Pixels(decoded.Frames[47])),
                "The native damage/dying/recovery/rescue film became static repeated images.");
        }
        WriteEvidence(game, cursor, output, "life-native-rescue-events");
        Console.WriteLine($"Native life rescue: seed31, actual Slash from{damage.SourceSeat}, HP1->0->1, lifeID{dying.ResolutionId}, " +
            $"Peach{peach.Id} paid once, commands{before}->{game.AcceptedCommands.Count}, Card0ms/Damage420ms/Dying720ms; " +
            "Peach at1040ms, Recovery1320ms/Rescued1600ms, four safe views unchanged.");

        void Sample(double seconds, string label, bool expectDying, bool expectRescued)
        {
            AdvanceTo(seconds);
            host.Capture(output, label, 1120); AssertCaptionSafety(host);
            Assert(HasLifeCaption(host.Layer, BattleCueKind.Dying) == expectDying &&
                HasLifeCaption(host.Layer, BattleCueKind.Rescued) == expectRescued && !HasDeathInk(host.Layer),
                $"The actual rescue frame {label} predicted death, skipped its causal boundary or retained an obsolete life caption.");
            Assert(Same(game, currentBoundary), "Sampling a real life frame changed the safe game.");
        }
        void AdvanceTo(double seconds)
        {
            if (movie is not null)
                while (movieIndex < 90 && movieIndex * .04 <= seconds + 1e-8)
                {
                    MoveTo(movieIndex * .04);
                    Flush(host); SetSize(host, 1120);
                    var nativeFrame = new RenderTargetBitmap(1120, 740, 96, 96, PixelFormats.Pbgra32);
                    nativeFrame.Render(host.Root);
                    var frame = new RenderTargetBitmap(728, 481, 96, 96, PixelFormats.Pbgra32);
                    var drawing = new DrawingVisual();
                    using (var dc = drawing.RenderOpen()) dc.DrawImage(nativeFrame, new Rect(0, 0, 728, 481));
                    frame.Render(drawing); movie.Frames.Add(BitmapFrame.Create(frame)); movieIndex++;
                }
            MoveTo(seconds);
        }
        void MoveTo(double seconds)
        {
            var current = host.Clock.GetElapsedTime(start, host.Clock.GetTimestamp()).TotalSeconds;
            Assert(seconds + 1e-8 >= current, "Native life recording must never move the controlled clock backwards.");
            host.Clock.Advance(Math.Max(0, seconds - current)); host.Layer.InvalidateVisual();
        }
    }

    private static void Death(NativeHost host, string output)
    {
        var model = host.Model; var game = Engine(model); var cursor = game.Events.Count; var before = game.AcceptedCommands.Count;
        Assert(model.CanStepAi && game.CreateSnapshot(0) is { CurrentSeat: 1, Phase: TurnPhase.Play, PendingDecision: null },
            "The restored death branch did not retain its actual pre-action AI checkpoint.");
        model.StepAiCommand.Execute(null);
        var (_, dying) = Entered(host, cursor);
        host.Clock.Advance(.04); host.Capture(output, "life-native-death-hit-before-decline", 1120);
        var decline = model.DyingChoices.Single(choice => choice.Parameters.GetValueOrDefault("response") == "let-die");
        var rescuePrompt = game.CreateSnapshot(0).PendingDecision!;
        model.SelectDyingChoiceCommand.Execute(decline);
        Assert(game.AcceptedCommands.Count == before + 2 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
            answer.PromptId == rescuePrompt.PromptId && answer.Choice == decline.Id,
            "The native let-die choice must submit once against its actual current rescue prompt.");
        var advances = 0;
        for (; advances < 12 && !game.Events.Skip(cursor).Any(item => item.Payload is PlayerDiedEvent { VictimSeat: 0 }); advances++)
        {
            Assert(model.CanStepAi && game.CreateSnapshot(0).PendingDecision is null,
                "The fixed death branch unexpectedly requested another human choice; do not search a different seed.");
            model.StepAiCommand.Execute(null);
        }
        var died = game.Events.Skip(cursor).Select(item => item.Payload).OfType<PlayerDiedEvent>().Single(item => item.VictimSeat == 0);
        var resolved = game.Events.Skip(cursor).Select(item => item.Payload).OfType<DyingResolvedEvent>().Single(item => item.ResolutionId == dying.ResolutionId);
        Assert(!resolved.Survived && game.CreateSnapshot(0).Players[0] is { Hp: 0, IsAlive: false } &&
            game.AcceptedCommands.Count == before + 2 + advances && model.Hand.All(card => card.Id != 45) &&
            !game.Events.Skip(cursor).Any(item => item.Payload is RecoveryAppliedEvent { TargetSeat: 0 }),
            "Actual all-responder refusal must commit one death without paying Peach or creating a recovery.");
        Assert(game.CardMovements.Count(item => item.CardId == 45 && item.From == CardLocation.Hand(0) && item.To == CardLocation.Processing) == 0 &&
            game.Events.Single(item => ReferenceEquals(item.Payload, died)).Sequence >
                game.Events.Single(item => ReferenceEquals(item.Payload, dying)).Sequence,
            "Death cleanup became a phantom Peach payment or death preceded dying.");
        AssertLifeCue(host, BattleCueKind.Death, null, .92);
        var boundary = Boundary(game);
        host.Clock.Advance(.04); host.Capture(output, "life-native-death-before-arrival", 1120);
        AssertDeathShade(host, pending: true);
        Assert(!HasDeathInk(host.Layer), "The committed death marker appeared before the damage and dying visual boundaries.");
        host.Clock.Advance(.84); host.Capture(output, "life-native-death-before-closure", 1120);
        AssertDeathShade(host, pending: true);
        Assert(!HasDeathInk(host.Layer), "Death rendered before the pending dying pulse's causal boundary.");
        host.Clock.Advance(.08); host.Capture(output, "life-native-death-start", 1120);
        AssertDeathShade(host, pending: false);
        Assert(HasDeathInk(host.Layer) && !HasLifeCaption(host.Layer, BattleCueKind.Dying),
            "The actual committed death failed to close its own portrait or retained a superseded dying caption.");
        host.Clock.Advance(.22); host.Capture(output, "life-native-death-readable", 1920); AssertCaptionSafety(host);
        AssertDeathReadouts(host);
        Assert(HasDeathInk(host.Layer) && Same(game, boundary), "Native death playback changed actual HP, commands, cost or safe views.");
        Console.WriteLine($"Native death visibility: winner={game.CreateSnapshot(0).Winner}, status={game.CreateSnapshot(0).Status}; " +
            $"actual IsAlive=false before transient closure; additional native advances={advances}.");
        model.IsMotionEnabled = false; Flush(host);
        AssertDeathShade(host, pending: false);
        Assert(host.Layer.ActiveEffectCount == 0 && !host.Layer.IsFrameTimerRunning, "Motion off retained pending life outcomes.");
        model.IsMotionEnabled = true; host.Clock.Advance(.04); host.Capture(output, "life-native-motion-on-no-replay", 1120);
        Assert(host.Layer.ActiveEffectCount == 0 && !HasDeathInk(host.Layer) && Same(game, boundary),
            "Restoring motion replayed an old death or changed an authoritative snapshot.");
        model.SaveGameCommand.Execute(null); model.LoadManualGameCommand.Execute(null); Flush(host);
        Assert(!model.HasSaveError && host.Layer.ActiveEffectCount == 0 && Same(Engine(model), boundary),
            "Actual save/load replayed historical death or changed a player-safe view.");
        host.Capture(output, "life-native-death-loaded-no-replay", 1120);
        AssertDeathShade(host, pending: false);
        var closedPortrait = Find<Button>(host.Root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == 0);
        var closedShade = Find<Border>(closedPortrait).Single(border => border.Name == "DeathShade");
        host.Window.Content = null;
        host.Window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        Console.WriteLine($"Native life detached portrait: pending={BattleFeedbackLayer.GetIsDeathPending(closedPortrait)}, " +
            $"shade.Visibility={closedShade.Visibility}, root.DataContext={host.Root.DataContext?.GetType().Name ?? "null"}, " +
            $"portrait.DataContext={closedPortrait.DataContext?.GetType().Name ?? "null"}, " +
            $"shade.DataContext={closedShade.DataContext?.GetType().Name ?? "null"}, " +
            $"root.IsLoaded={host.Root.IsLoaded}, portrait.IsLoaded={closedPortrait.IsLoaded}, shade.IsLoaded={closedShade.IsLoaded}.");
        Assert(host.Layer.ActiveEffectCount == 0 && !host.Layer.IsFrameTimerRunning,
            "Closing the owned native window left a life animation or frame timer running.");
        Assert(!BattleFeedbackLayer.GetIsDeathPending(closedPortrait) && !host.Root.IsLoaded,
            "Unloading the real portrait left its pending presentation flag or failed to detach the loaded root.");
        // An off-tree root loses its inherited model context, so template
        // bindings can legitimately return to their default collapsed badge.
        // Reattach the same root to verify the real authoritative presentation.
        host.Window.Content = host.Root;
        host.Window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        host.Clock.Advance(.04); host.Capture(output, "life-native-death-reattached-no-replay", 1120);
        Assert(host.Root.IsLoaded && ReferenceEquals(host.Root.DataContext, model),
            "The same native root did not recover its real model context when reattached.");
        AssertDeathShade(host, pending: false);
        Assert(host.Layer.ActiveEffectCount == 0 && !host.Layer.IsFrameTimerRunning && !HasDeathInk(host.Layer) &&
            Same(Engine(model), boundary),
            "Reattaching the actual dead scene replayed its historical closure, retained a timer or changed a safe view/command.");
        WriteEvidence(game, cursor, output, "life-native-death-events");
    }

    private static (DamageAppliedEvent Damage, PlayerDyingEvent Dying) Entered(NativeHost host, int cursor)
    {
        var game = Engine(host.Model);
        var damage = game.Events.Skip(cursor).Select(item => item.Payload).OfType<DamageAppliedEvent>().Single(item => item.TargetSeat == 0);
        var dying = game.Events.Skip(cursor).Select(item => item.Payload).OfType<PlayerDyingEvent>().Single(item => item.VictimSeat == 0);
        var declared = game.Events.Skip(cursor).Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Single(item => item.CardKind == CardKind.Slash);
        var targets = game.Events.Skip(cursor).Select(item => item.Payload).OfType<TargetsConfirmedEvent>().Single(item => item.ResolutionId == declared.ResolutionId);
        var physical = declared.CardId;
        Assert(declared.SourceSeat == 1 && physical == 37 && targets.TargetSeats.SequenceEqual(new[] { 0 }) &&
            game.CardMovements.Count(item => item.CardId == physical && item.From == CardLocation.Hand(declared.SourceSeat) && item.To == CardLocation.Processing) == 1 &&
            damage.SourceSeat == declared.SourceSeat && damage is { Amount: 1, RemainingHp: 0 } &&
            game.Events.Single(item => ReferenceEquals(item.Payload, damage)).Sequence <
                game.Events.Single(item => ReferenceEquals(item.Payload, dying)).Sequence,
            "Native life feedback requires the actual paid Slash, exact damage and later public dying event; no injected HP or fake cue.");
        var cardCue = host.Model.BattleCues.Single(item => item.PublicCardId == physical && item.Kind == BattleCueKind.Card);
        var damageCue = host.Model.BattleCues.Single(item => item.Sequence == game.Events.Single(envelope => ReferenceEquals(envelope.Payload, damage)).Sequence);
        Assert(cardCue.CardKind == CardKind.Slash && cardCue.SourceSeat == declared.SourceSeat && cardCue.TargetSeats.SequenceEqual(new[] { 0 }) &&
            Math.Abs(Active(host.Layer).Single(item => item.Cue.Sequence == cardCue.Sequence).Delay) < .0001 &&
            Math.Abs(Active(host.Layer).Single(item => item.Cue.Sequence == damageCue.Sequence).Delay - .42) < .0001,
            "The actual new AI Slash face or its damage arrival delay disagreed with the committed public action.");
        return (damage, dying);
    }
    private static void AssertLifeCue(NativeHost host, BattleCueKind kind, long? lifeId, double delay)
    {
        var cue = host.Model.BattleCues.Last(item => item.Kind == kind && item.TargetSeats.Contains(0));
        Assert(cue.SourceSeat == 0 && cue.TargetSeats.SequenceEqual(new[] { 0 }) && cue.LifeResolutionId == lifeId &&
            cue.PublicCardId is null && cue.CardKind is null && !cue.IsInquiry,
            "A life outcome inferred a card or victim instead of projecting its public event.");
        var active = Active(host.Layer).Single(item => item.Cue.Sequence == cue.Sequence);
        Assert(Math.Abs(active.Delay - delay) < .0001,
            $"Native {kind} ignored its preceding real hit/recovery: actual delay={active.Delay:F6}, expected={delay:F6}.");
    }
    private static IEnumerable<(BattleCue Cue, double Delay)> Active(BattleFeedbackLayer layer)
    {
        var field = typeof(BattleFeedbackLayer).GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var item in (IEnumerable)field.GetValue(layer)!)
        {
            if (item is null) continue;
            var type = item.GetType();
            yield return ((BattleCue)type.GetProperty("Cue")!.GetValue(item)!, (double)type.GetProperty("Delay")!.GetValue(item)!);
        }
    }
    private static bool HasLifeCaption(BattleFeedbackLayer layer, BattleCueKind kind)
    {
        var color = kind switch { BattleCueKind.Dying => Color.FromRgb(232, 145, 120), BattleCueKind.Rescued => Color.FromRgb(145, 227, 170),
            _ => Color.FromRgb(196, 181, 155) };
        return Shapes(VisualTreeHelper.GetDrawing(layer), Matrix.Identity, 1).Any(item => item.Opacity > .005 &&
            item.Shape.Brush is SolidColorBrush fill && fill.Color == Color.FromRgb(19, 31, 34) &&
            item.Shape.Pen?.Brush is SolidColorBrush brush && brush.Color == color);
    }
    private static bool HasDeathInk(BattleFeedbackLayer layer) => Shapes(VisualTreeHelper.GetDrawing(layer), Matrix.Identity, 1)
        .Any(item => item.Opacity > .001 && item.Shape.Pen?.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(204, 187, 158));
    private static void AssertCaptionSafety(NativeHost host)
    {
        var obstacles = Find<FrameworkElement>(host.Root).Where(element => element.IsVisible && element.ActualWidth > 0 &&
            element.ActualHeight > 0 && (element is Button or TextBlock || element is HealthMeter))
            .Select(element => element.TransformToVisual(host.Layer).TransformBounds(new Rect(element.RenderSize))).ToArray();
        var bounds = new Rect(host.Layer.RenderSize);
        foreach (var item in Shapes(VisualTreeHelper.GetDrawing(host.Layer), Matrix.Identity, 1).Where(item => item.Opacity > .05 &&
            item.Shape.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(19, 31, 34)))
        {
            Assert(bounds.Contains(item.Bounds), "A native life caption was clipped by the output.");
            Assert(obstacles.All(obstacle => !item.Bounds.IntersectsWith(obstacle)),
                $"A native life caption obscured an actual portrait, HP, readout or action; caption={item.Bounds}.");
        }
    }
    private static void AssertDeathReadouts(NativeHost host)
    {
        var portrait = Find<Button>(host.Root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == 0);
        var shade = Find<Border>(portrait).Single(border => border.Name == "DeathShade");
        Assert(shade.IsVisible && !shade.IsHitTestVisible, "The actual dead seat did not show its passive static death shade.");
        var grid = (Grid)VisualTreeHelper.GetParent(shade);
        var name = Find<TextBlock>(grid).Single(text => text.Name == "SeatVerticalName");
        var icons = Find<ItemsControl>(grid).Single(control => control.Name == "SeatHealthIcons");
        var metadata = Find<StackPanel>(grid).Single(control => control.Name == "SeatNameRail");
        var publicHand = Find<TextBlock>(metadata).Single(control => control.Name == "SeatHandCountText");
        var seat = (SeatViewModel)portrait.DataContext;
        var publicHealth = Find<Border>(grid).Single(control => control.Name == "SeatHealthRail");
        Assert(System.Windows.Automation.AutomationProperties.GetName(publicHealth) == seat.HealthValue,
            "The public HP value must remain available on the health rail after death.");
        var publicRole = Find<TextBlock>(grid).Single(text => text.Text == seat.RoleBadgeText);
        var titleBranch = DirectChild(metadata);
        Assert(grid.Children.IndexOf(shade) < grid.Children.IndexOf(DirectChild(name)) && grid.Children.IndexOf(shade) < grid.Children.IndexOf(DirectChild(icons)) &&
            grid.Children.IndexOf(shade) < grid.Children.IndexOf(titleBranch),
            "Static death shading covered the later name/HP/readout instead of staying behind the metadata.");
        var output = new Rect(host.Root.RenderSize);
        foreach (var element in new FrameworkElement[] { icons, metadata, publicHand, publicHealth, publicRole, shade }.Concat(name.IsVisible ? new[] { name } : []))
        {
            var bounds = element.TransformToVisual(host.Root).TransformBounds(new Rect(element.RenderSize));
            Assert(element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0 &&
                output.Contains(bounds),
                $"The native dead-seat metadata was hidden/cropped: element={element.Name}/{element.GetType().Name}, " +
                $"visibility={element.Visibility}, isVisible={element.IsVisible}, bounds={bounds}, output={output}.");
        }
        Assert(icons.Items.Count == Math.Clamp(seat.MaxHp, 0, 8) &&
            icons.Items.Cast<string>().All(path => path.EndsWith("gallery-hp-empty.png", StringComparison.Ordinal)) &&
            System.Windows.Automation.AutomationProperties.GetName(publicHealth) == $"0/{seat.MaxHp}" &&
            publicRole.Text.Length > 0 && shade.Child is TextBlock { Text: "阵 亡" },
            "The dead portrait lost its exact zero-HP title, revealed public role or actual static death badge.");
        // Fixture generals deliberately have no bundled portrait artwork. The
        // existing template hides their alternate vertical portrait name; the
        // committed death caption still displays the victim's public identity.
        Assert(name.Visibility == (seat.HasPortrait ? Visibility.Visible : Visibility.Collapsed),
            $"The alternate portrait name no longer follows its actual artwork: HasPortrait={seat.HasPortrait}, " +
            $"element={name.Name}, visibility={name.Visibility}, bounds={name.TransformToVisual(host.Root).TransformBounds(new Rect(name.RenderSize))}.");
        var captionGlyphs = Glyphs(VisualTreeHelper.GetDrawing(host.Layer), Matrix.Identity, 1).Where(item => item.Opacity > .05 &&
            item.Glyph.ForegroundBrush is SolidColorBrush brush && brush.Color == Color.FromRgb(196, 181, 155)).ToArray();
        var visibleCaption = string.Concat(captionGlyphs.Select(item => item.Glyph.GlyphRun.Characters is { } characters ? new string(characters.ToArray()) : string.Empty));
        var expectedName = host.Model.BattleCues.Last(cue => cue.Kind == BattleCueKind.Death && cue.TargetSeats.Contains(0)).ActorName;
        Assert(expectedName.Length > 0 && visibleCaption.Contains(expectedName, StringComparison.Ordinal) &&
            captionGlyphs.All(item => new Rect(host.Layer.RenderSize).Contains(item.Bounds)),
            $"The actual rendered death caption lost/clipped its public victim name: expected={expectedName}, rendered={visibleCaption}, " +
            $"glyphBounds=[{string.Join(";", captionGlyphs.Select(item => item.Bounds))}].");
        UIElement DirectChild(DependencyObject child)
        {
            while (VisualTreeHelper.GetParent(child) is { } parent && !ReferenceEquals(parent, grid)) child = parent;
            return (UIElement)child;
        }
    }
    private static IEnumerable<(GlyphRunDrawing Glyph, Rect Bounds, double Opacity)> Glyphs(Drawing? drawing, Matrix transform, double opacity)
    {
        if (drawing is GlyphRunDrawing glyph) yield return (glyph, new MatrixTransform(transform).TransformBounds(glyph.Bounds), opacity);
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            foreach (var child in group.Children) foreach (var item in Glyphs(child, next, opacity * group.Opacity)) yield return item;
        }
    }
    private static void AssertDeathShade(NativeHost host, bool pending)
    {
        var portrait = Find<Button>(host.Root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == 0);
        var shade = Find<Border>(portrait).Single(border => border.Name == "DeathShade");
        var frame = Find<Border>(portrait).Single(border => border.Name == "SeatFrame");
        Assert(BattleFeedbackLayer.GetIsDeathPending(portrait) == pending &&
            shade.Visibility == (pending ? Visibility.Collapsed : Visibility.Visible) && shade.IsVisible == !pending &&
            Math.Abs(frame.Opacity - (pending ? 1 : .55)) < .0001,
            $"The actual dead-seat badge crossed its visual boundary incorrectly: pending={BattleFeedbackLayer.GetIsDeathPending(portrait)}, " +
            $"visibility={shade.Visibility}, visible={shade.IsVisible}, frame.Opacity={frame.Opacity}, expected pending={pending}.");
    }
    private static IEnumerable<(GeometryDrawing Shape, Rect Bounds, double Opacity)> Shapes(Drawing? drawing, Matrix transform, double opacity)
    {
        if (drawing is GeometryDrawing shape) yield return (shape, new MatrixTransform(transform).TransformBounds(shape.Geometry.Bounds), opacity);
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            foreach (var child in group.Children) foreach (var item in Shapes(child, next, opacity * group.Opacity)) yield return item;
        }
    }
    private static void Flush(NativeHost host)
    {
        host.Root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); host.Root.UpdateLayout(); host.Layer.InvalidateVisual();
        host.Root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }
    private static void SetSize(NativeHost host, int width)
    {
        host.Root.Width = width; host.Root.Height = width == 1120 ? 740 : 1080;
        host.Window.SizeToContent = SizeToContent.WidthAndHeight; host.Window.UpdateLayout(); Flush(host);
        Assert(host.Root.RenderSize == new Size(host.Root.Width, host.Root.Height), "Recording used a cropped native root.");
    }
    private sealed record SafeBoundary(long Revision, int Commands, int Events, int Movements, string[] Views);
    private static SafeBoundary Boundary(GameEngine game) => new(game.Revision, game.AcceptedCommands.Count, game.Events.Count, game.CardMovements.Count,
        Enumerable.Range(0, game.CreateSnapshot(0).Players.Count).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray());
    private static bool Same(GameEngine game, SafeBoundary before) => game.Revision == before.Revision && game.AcceptedCommands.Count == before.Commands &&
        game.Events.Count == before.Events && game.CardMovements.Count == before.Movements &&
        Enumerable.Range(0, before.Views.Length).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == before.Views[seat]);
    private static void WriteEvidence(GameEngine game, int cursor, string output, string name) => File.WriteAllText(Path.Combine(output, name + ".json"),
        JsonSerializer.Serialize(new { game.Seed, Commands = game.AcceptedCommands.Select(command => command.GetType().Name).ToArray(),
            Events = game.Events.Skip(cursor).Select(item => new { item.Sequence, Kind = item.Payload.GetType().Name, Payload = (object)item.Payload }).ToArray() },
            new JsonSerializerOptions { WriteIndented = true }));
    private static byte[] Pixels(BitmapSource source)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0); return bytes;
    }
    private sealed class BindingListener : TraceListener
    {
        internal List<string> Errors { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        { if (message is not null && (message.Contains("Error:") || message.Contains("Warning:"))) Errors.Add(message); }
    }
}
