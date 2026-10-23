using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class NativeActionFeedbackChecks
{
    public static void ResponseSemantics(string output)
    {
        foreach (var kind in new[] { DecisionKind.RespondDodge, DecisionKind.FireAttackReveal })
        {
            var (model, registry) = StableHandResponseFixture.Create(kind, output);
            using (model)
            {
                model.IsSoundEnabled = model.IsMusicEnabled = false;
                foreach (var selected in new[] { false, true })
                {
                    model.LoadManualGameCommand.Execute(null);
                    var game = Engine(model);
                    var prompt = model.ChoiceSnapshot.PendingDecision!;
                    var checkpoint = game.CreateCheckpoint();
                    var before = Boundary(game);
                    var count = game.AcceptedCommands.Count;
                    var movements = game.CardMovements.Count;
                    var card = model.Hand.Last(candidate => candidate.IsPlayable);
                    var decline = prompt.Choices.SingleOrDefault(choice => choice.Cards.Count == 0);
                    Assert(prompt.Kind == kind && (decline is not null) == (kind == DecisionKind.RespondDodge),
                        "The native action fixture must publish its real optional response or mandatory reveal.");
                    var notifications = new List<string?>();
                    System.ComponentModel.PropertyChangedEventHandler observe = (_, args) => notifications.Add(args.PropertyName);
                    model.PropertyChanged += observe;
                    var window = new MainWindow(model);
                    try
                    {
                        if (selected) model.SelectCardCommand.Execute(card);
                        var root = (FrameworkElement)window.Content;
                        Render(root, 1120, 740, Path.Combine(output, $"native-action-{kind}-{selected}-before.png"));
                        var cancel = (Button)window.FindName("CancelActionButton");
                        var confirm = (Button)window.FindName("PlayCardButton");
                        Assert(Boundary(game) == before && model.HasSelection == selected && confirm.IsEnabled == selected,
                            "Choosing a native response must only prepare a draft and its actual confirmation state.");
                        Assert(model.SelectionArea.ConfirmText == model.PlayButtonText && model.SelectionArea.CancelText == model.ActionDockCancelText &&
                            model.SelectionArea.CanConfirm == selected && model.SelectionArea.ConfirmCommand == model.ConfirmSelectedCommand,
                            "Responses must retain their card-specific text, decline meaning and native command in the shared selection area.");
                        AssertCaption(cancel, model, decline is not null ? "放 弃" : "取 消", selected || decline is not null);
                        if (selected) Assert(notifications.Contains(nameof(MainViewModel.ActionDockCancelText)),
                            "The actual cancel caption must be refreshed with the response draft.");
                        if (decline is not null)
                            Assert(model.ActionHint.Contains($"「{model.ActionDockCancelText}」", StringComparison.Ordinal) &&
                                model.CancelActionHint.Contains("Esc 仅取消选牌", StringComparison.Ordinal),
                                "Selected and unselected optional responses must explain the visible decline action and reversible Esc.");
                        else if (!selected) AssertBackground(cancel, "selfseat__selfSeatCancelBtn_disabled.png");
                        if (!confirm.IsEnabled) AssertBackground(confirm, "selfseat__selfSeatOkBtn_disabled.png");

                        var clicks = 0;
                        cancel.Click += (_, _) => clicks++;
                        if (cancel.IsEnabled)
                        {
                            if (kind == DecisionKind.RespondDodge && selected)
                                PressSpace(window, cancel, game, output);
                            else Click(cancel);
                        }
                        window.UpdateLayout();
                        if (decline is null)
                            Assert(Boundary(game) == before && !model.HasSelection && clicks == (selected ? 1 : 0),
                                "Mandatory native revealing must only clear the selected draft, retaining every command, card and prompt.");
                        else
                        {
                            var reference = GameReplay.Restore(checkpoint, registry);
                            Assert(reference.Submit(new AnswerPromptCommand(0, prompt.PromptId, decline.Id, reference.Revision)).Accepted,
                                "The published native decline must be independently accepted.");
                            var exactDecline = clicks == 1 && game.AcceptedCommands.Count == count + 1 &&
                                game.Revision == checkpoint.Revision + 1 &&
                                game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                                answer.ActorSeat == 0 && answer.PromptId == prompt.PromptId && answer.Choice == decline.Id &&
                                answer.ExpectedRevision == checkpoint.Revision && !model.HasSelection &&
                                !game.CardMovements.Skip(movements).Any(move => move.CardId == card.Id &&
                                    move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Respond) &&
                                Boundary(game) == Boundary(reference);
                            Assert(exactDecline,
                                "The actual native decline button must submit once without paying the previously selected response card." +
                                (exactDecline ? string.Empty : " Diagnostic: " + DescribeDeclineBoundary(game, reference, model, window,
                                    cancel, prompt, decline, card.Id, clicks, count, movements, checkpoint.Revision)));
                            var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                            Assert(Boundary(cold) == Boundary(game), "The native decline must retain every safe viewer boundary after cold replay.");
                        }
                        Assert(Equals(cancel.Content, model.ActionDockCancelText) &&
                            AutomationProperties.GetName(cancel) == model.ActionDockCancelText,
                            "After an action, the existing button must follow its current caption and accessible name.");
                        Console.WriteLine($"Native action {kind}: selected={selected}, caption={(decline is null ? "cancel" : "decline")}, clicks={clicks}, added commands={game.AcceptedCommands.Count - count}.");
                    }
                    finally
                    {
                        model.PropertyChanged -= observe;
                        if (Mouse.Captured is Button captured && ReferenceEquals(Window.GetWindow(captured), window))
                            captured.ReleaseMouseCapture();
                        window.Content = null;
                        // This fixture reuses the model for the next draft case;
                        // its enclosing using owns disposal, rather than Window_Closed.
                        window.DataContext = null;
                        window.Close();
                    }
                }
            }
        }
        OptionalSkill(output);
        NormalDraft(output);
        SlashAndDodgeMotion(output);
        GroupResponseMotion(output);
        NullificationMotion(output);
    }

    private static void SlashAndDodgeMotion(string output)
    {
        var (prepared, registry) = StableHandResponseFixture.Create(DecisionKind.RespondDodge, output);
        using (prepared)
        {
            var checkpoint = Engine(prepared).CreateCheckpoint();
            Assert(Engine(prepared).PendingDecision is { IncomingCard: CardKind.Slash, PlayerSeat: 0 } &&
                checkpoint.Commands.Last() is AdvanceOneStepCommand,
                "The fixed native fixture must expose the physical ordinary Slash opened by its final AI step.");
            var prefix = GameReplay.Replay(checkpoint.Options, checkpoint.Commands.SkipLast(1), registry);
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, prefix.CreateCheckpoint()));
            using var model = new MainViewModel(false, prefix.Seed, true, store, useExpandedContent: false,
                contentRegistry: registry, historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
                { IsMotionEnabled = true, IsSoundEnabled = false, IsMusicEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            Assert(!model.HasSaveError && model.BattleCues.Count == 0 && model.CanStepAi,
                "The native Slash sample must restore before its actual declaration without replaying old effects.");
            using var host = new NativeResponseStatusChecks.NativeHost(model);
            host.Capture(output, "native-slash-dodge-before", 1120);
            var game = Engine(model);
            var eventCursor = game.Events.Count;
            var targetHp = game.CreateSnapshot(0).Players[0].Hp;
            model.StepAiCommand.Execute(null);
            var declaration = game.Events.Skip(eventCursor).Single(envelope =>
                envelope.Payload is CardUseDeclaredEvent { CardKind: CardKind.Slash, SourceSeat: 1 });
            var use = (CardUseDeclaredEvent)declaration.Payload;
            var attack = model.BattleCues.Single(cue => cue.Sequence == declaration.Sequence);
            Assert(game.PendingDecision is { Kind: DecisionKind.RespondDodge, IncomingCard: CardKind.Slash, PlayerSeat: 0 } &&
                attack is { Kind: BattleCueKind.Card, CardKind: CardKind.Slash, SourceSeat: 1, PublicCard: not null } &&
                attack.TargetSeats.SequenceEqual([0]) && attack.SourceCardUseResolutionId == use.ResolutionId &&
                attack.PublicCardId == use.CardId,
                "The live declaration must describe the AI's real physical Slash and its confirmed human target.");
            var unpaidBoundary = Boundary(game);
            var seats = Find<Button>(host.Root).Where(button => button.IsVisible && BattleFeedbackLayer.GetSeatAnchor(button) >= 0)
                .ToDictionary(BattleFeedbackLayer.GetSeatAnchor,
                    button => button.TransformToVisual(host.Layer).TransformBounds(new Rect(button.RenderSize)));
            var direction = Center(seats[0]) - Center(seats[1]); direction.Normalize();
            var time = 0;
            Rect[] first = [], second = [];
            var visibility = new List<double>();
            for (var milliseconds = 100; milliseconds <= 500; milliseconds += 20)
            {
                AdvanceTo(milliseconds, milliseconds is 120 or 260 ? $"native-slash-{milliseconds}ms" : null, unpaidBoundary);
                var drawing = VisualTreeHelper.GetDrawing(host.Layer);
                var ink = InkEvidence(drawing).ToArray();
                var totalArea = ink.Sum(sample => sample.TotalArea);
                var visibleArea = ink.Sum(sample => sample.VisibleArea);
                var ratio = totalArea > 0 ? visibleArea / totalArea : 0;
                visibility.Add(ratio);
                var complete = ink.Length > 0 && ink.All(sample => sample.Opacity > .1) &&
                    totalArea > 1 && visibleArea > 1 && ratio >= .98;
                Assert(complete,
                    $"The real Slash must retain at least 98% of its main blade ink throughout its route: {milliseconds}ms, visible={ratio:P3}." +
                    (complete ? string.Empty : " Diagnostic: " + JsonSerializer.Serialize(new
                    {
                        Cue = attack, Source = seats[1].ToString(), Target = seats[0].ToString(),
                        TimeScale = model.BattleAnimationTimeScale, Ink = ink
                    })));
                if (milliseconds == 120) first = VisibleMotionShapes(drawing, IsSlashInk).ToArray();
                if (milliseconds == 260) second = VisibleMotionShapes(drawing, IsSlashInk).ToArray();
            }
            Assert(first.Length > 0 && second.Length > 0 &&
                Vector.Multiply(Center(Union(second)) - Center(Union(first)), direction) > 10,
                "The actual ordinary Slash blade must visibly advance from its declared source toward its confirmed target.");
            AdvanceTo(700, "native-slash-retired-700ms", unpaidBoundary);
            Assert(!VisibleMotionShapes(VisualTreeHelper.GetDrawing(host.Layer), IsSlashInk).Any() &&
                game.PendingDecision?.Kind == DecisionKind.RespondDodge,
                "The ordinary Slash blade must retire while an unanswered response keeps its real prompt open.");

            var prompt = game.PendingDecision!;
            var dodge = model.Hand.Last(card => card.IsPlayable && card.Kind == CardKind.Dodge);
            var payment = prompt.Choices.Single(choice => choice.Cards.SequenceEqual([dodge.Id]));
            var commands = game.AcceptedCommands.Count;
            model.SelectCardCommand.Execute(dodge);
            model.ConfirmSelectedCommand.Execute(null);
            var response = model.BattleCues.Single(cue => cue.Kind == BattleCueKind.Response && cue.PublicCardId == dodge.Id);
            Assert(game.AcceptedCommands.Count == commands + 1 &&
                game.AcceptedCommands.Last() is AnswerPromptCommand answer && answer.PromptId == prompt.PromptId && answer.Choice == payment.Id &&
                game.Events.Skip(eventCursor).Any(envelope => envelope.Payload is CardRespondedEvent paid &&
                    paid.CardId == dodge.Id && paid.ResponderSeat == 0 && paid.EffectiveCardKind == CardKind.Dodge) &&
                response is { CardKind: CardKind.Dodge, SourceSeat: 0, IsInquiry: false, PublicCard: not null },
                "The sidestep must follow one exact native payment of the real physical Dodge, not an inquiry or fabricated cue.");
            var paidBoundary = Boundary(game);
            AdvanceTo(780, "native-dodge-80ms", paidBoundary);
            var firstGhost = VisibleMotionShapes(VisualTreeHelper.GetDrawing(host.Layer), IsDodgeGhost).ToArray();
            AdvanceTo(940, "native-dodge-240ms", paidBoundary);
            var secondGhost = VisibleMotionShapes(VisualTreeHelper.GetDrawing(host.Layer), IsDodgeGhost).ToArray();
            Assert(firstGhost.Length == 1 && secondGhost.Length == 1 &&
                firstGhost.All(seats[0].Contains) && secondGhost.All(seats[0].Contains) &&
                secondGhost[0].X > firstGhost[0].X + 2,
                "The paid Dodge's visible leading silhouette must move sideways inside its responder's portrait.");
            AdvanceTo(1340, "native-dodge-retired-640ms", paidBoundary);
            Assert(!VisibleMotionShapes(VisualTreeHelper.GetDrawing(host.Layer), IsDodgeGhost).Any(),
                "The paid Dodge silhouette must retire rather than becoming a persistent success shield.");
            Console.WriteLine($"Native Slash/Dodge: seed31 physical Slash{use.CardId} use{use.ResolutionId}; " +
                $"{visibility.Count} blade samples 100..500ms every20ms, minimum visible ink={visibility.Min():P3}; " +
                $"blade shapes {first.Length}/{second.Length} at 120/260ms, retired700ms awaiting response; " +
                $"paid Dodge{dodge.Id}, leading ghost X {firstGhost[0].X:F2}->{secondGhost[0].X:F2} at 80/240ms, retired640ms; " +
                "no premature hit; commands, physical movements and all viewer snapshots unchanged during both animations.");

            void AdvanceTo(int milliseconds, string? label, string boundary)
            {
                host.Clock.Advance((milliseconds - time) / 1000d); time = milliseconds;
                host.Layer.InvalidateVisual();
                if (label is null) Render(host.Root, 1120, 740, null);
                else host.Capture(output, label, 1120);
                Assert(Boundary(game) == boundary && game.CreateSnapshot(0).Players[0].Hp == targetHp &&
                    !game.Events.Skip(eventCursor).Any(envelope => envelope.Payload is DamageAppliedEvent) &&
                    !model.BattleCues.Any(cue => cue.Kind == BattleCueKind.Damage) &&
                    !VisibleMotionShapes(VisualTreeHelper.GetDrawing(host.Layer), IsHitInk).Any(),
                    "Animating an unpaid Slash or a paid Dodge must not invent a portrait hit, lose HP, or alter any committed viewer boundary.");
            }
        }
        static bool IsSlashInk(GeometryDrawing shape) => shape is
            { Geometry: StreamGeometry, Brush: SolidColorBrush fill, Pen: null } && fill.Color == Color.FromRgb(255, 203, 100);
        static bool IsDodgeGhost(GeometryDrawing shape) => shape is
            { Geometry: StreamGeometry, Brush: null, Pen: { Thickness: 2.6, Brush: SolidColorBrush edge } } && edge.Color == Color.FromRgb(116, 231, 255);
        static bool IsHitInk(GeometryDrawing shape) =>
            shape.Brush is SolidColorBrush fill && fill.Color == Color.FromRgb(255, 91, 62) ||
            shape.Pen?.Brush is SolidColorBrush edge && edge.Color == Color.FromRgb(255, 91, 62);
        static Point Center(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        static Rect Union(IEnumerable<Rect> bounds) => bounds.Aggregate(Rect.Empty, Rect.Union);
        static IEnumerable<MotionInkSample> InkEvidence(Drawing? drawing, Matrix? inherited = null, Geometry? inheritedClip = null, double opacity = 1)
        {
            var transform = inherited ?? Matrix.Identity;
            if (drawing is DrawingGroup group)
            {
                var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
                var clip = inheritedClip;
                if (group.ClipGeometry is { } own)
                {
                    var current = own.Clone(); current.Transform = new MatrixTransform(next);
                    clip = clip is null ? current : new CombinedGeometry(GeometryCombineMode.Intersect, clip, current);
                }
                foreach (var child in group.Children)
                    foreach (var evidence in InkEvidence(child, next, clip, opacity * group.Opacity)) yield return evidence;
            }
            else if (drawing is GeometryDrawing shape && IsSlashInk(shape))
            {
                var whole = shape.Geometry.Clone(); whole.Transform = new MatrixTransform(transform);
                Geometry visible = inheritedClip is null ? whole : new CombinedGeometry(GeometryCombineMode.Intersect, whole, inheritedClip);
                yield return new(opacity, whole.Bounds.ToString(), inheritedClip?.Bounds.ToString(),
                    visible.Bounds.ToString(), whole.GetArea(), visible.GetArea());
            }
        }
    }

    private sealed record MotionInkSample(double Opacity, string Bounds, string? ClipBounds,
        string VisibleBounds, double TotalArea, double VisibleArea);

    internal static void VerifyDuelMotion(NativeResponseStatusChecks.NativeHost host, BattleCue response, string? output = null)
    {
        var game = Engine(host.Model);
        var state = Boundary(game);
        var seats = Find<Button>(host.Root).Where(button => button.IsVisible && BattleFeedbackLayer.GetSeatAnchor(button) >= 0)
            .ToDictionary(BattleFeedbackLayer.GetSeatAnchor,
                button => button.TransformToVisual(host.Layer).TransformBounds(new Rect(button.RenderSize)));
        var guard = VisibleMotionShapes(VisualTreeHelper.GetDrawing(host.Layer), IsGuardBlade).ToArray();
        Assert(response is { Kind: BattleCueKind.Response, CardKind: CardKind.Slash, TargetSeats.Count: 0,
                DuelOpponentSeat: not null, ResponseResolutionId: not null } &&
            guard.Length > 0 && guard.All(seats[response.SourceSeat].Contains),
            "The genuine paid Duel Slash must have a visible local parry and no outgoing attack on its opponent.");
        var useId = response.ResponseResolutionId;
        var facts = game.Events.Where(envelope => envelope.Payload is CardUseDeclaredEvent use && use.ResolutionId == useId ||
            envelope.Payload is TargetsConfirmedEvent targets && targets.ResolutionId == useId).ToArray();
        var declaration = BattleCueProjector.Project(facts, game.CreateSnapshot(0))
            .Single(cue => cue.Kind == BattleCueKind.Card && cue.CardKind == CardKind.Duel);
        Assert(declaration.SourceSeat == response.DuelOpponentSeat && declaration.TargetSeats.Contains(response.SourceSeat),
            "The Duel's opposed blade cue must come from its real public declaration and confirmed participants.");
        var first = PaintActual(declaration, .13);
        var later = PaintActual(declaration, .35);
        Assert(VisibleMotionShapes(first, IsGuardBlade).Any() && VisibleMotionShapes(first, IsGoldBlade).Any() &&
            first.Bounds != later.Bounds && !seats[declaration.SourceSeat].Contains(later.Bounds) &&
            !seats[response.SourceSeat].Contains(later.Bounds),
            "A real declared Duel must move both opposed blades through the table corridor, not reuse a target aura or portrait hit.");
        if (output is not null) host.Capture(output, "native-duel-paid-parry-160ms", 1120);
        Assert(Boundary(game) == state, "Inspecting the actual Duel motion must not alter its command boundary.");

        DrawingGroup PaintActual(BattleCue cue, double progress)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                typeof(BattleFeedbackLayer).Assembly.GetType("CardGame.Wpf.Controls.BattleEffectPainter")!
                    .GetMethod("Draw", BindingFlags.Static | BindingFlags.Public)!
                    .Invoke(null, [dc, cue, progress, seats[cue.SourceSeat], seats]);
            return visual.Drawing;
        }
        static bool IsGoldBlade(GeometryDrawing shape) => shape is
            { Geometry: StreamGeometry, Brush: SolidColorBrush fill, Pen.Brush: SolidColorBrush edge } &&
            fill.Color == Color.FromRgb(255, 249, 218) && edge.Color == Color.FromRgb(255, 203, 100);
    }

    private static bool IsGuardBlade(GeometryDrawing shape) => shape is
        { Geometry: StreamGeometry, Brush: SolidColorBrush fill, Pen.Brush: SolidColorBrush edge } &&
        fill.Color == Color.FromRgb(255, 249, 218) && edge.Color == Color.FromRgb(116, 231, 255);

    private static void NullificationMotion(string output)
    {
        var (model, _) = StableHandResponseFixture.Create(DecisionKind.Nullification, output);
        using (model)
        {
            model.IsMotionEnabled = true;
            using var host = new NativeResponseStatusChecks.NativeHost(model);
            host.Capture(output, "native-nullification-before", 1120);
            var game = Engine(model);
            var cursor = game.Events.Count;
            var card = model.Hand.Last(candidate => candidate.IsPlayable && candidate.Kind == CardKind.Nullification);
            var prompt = game.PendingDecision!;
            var payment = prompt.Choices.Single(choice => choice.Cards.SequenceEqual([card.Id]));
            model.SelectCardCommand.Execute(card);
            model.ConfirmSelectedCommand.Execute(null);
            Assert(game.AcceptedCommands.Last() is AnswerPromptCommand answer && answer.PromptId == prompt.PromptId &&
                answer.Choice == payment.Id && game.Events.Skip(cursor).Any(envelope => envelope.Payload is NullificationRespondedEvent),
                "The fixed native counterspell must actually pay its public response before showing a seal.");
            var cue = model.BattleCues.Single(candidate => candidate.Kind == BattleCueKind.Response && candidate.PublicCardId == card.Id);
            Assert(cue is { CardKind: CardKind.Nullification, IsInquiry: false, HpDelta: 0, RemainingHp: null } &&
                !game.Events.Skip(cursor).Any(envelope => envelope.Payload is DamageAppliedEvent),
                "A paid counterspell must keep its actual public chain state without generating damage.");
            var state = Boundary(game);
            var movie = RecordMotion ? new GifBitmapEncoder() : null;
            var time = 0;
            Sample();
            AdvanceTo(240);
            host.Capture(output, "native-nullification-seal-240ms", 1120);
            var responder = Find<Button>(host.Root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == cue.SourceSeat);
            var seat = responder.TransformToVisual(host.Layer).TransformBounds(new Rect(responder.RenderSize));
            var first = VisibleMotionShapes(VisualTreeHelper.GetDrawing(host.Layer), IsCounterStrip).ToArray();
            AdvanceTo(480);
            host.Capture(output, "native-nullification-dissolve-480ms", 1120);
            var second = VisibleMotionShapes(VisualTreeHelper.GetDrawing(host.Layer), IsCounterStrip).ToArray();
            Assert(first.Length > 0 && second.Length > 0 && first.All(seat.Contains) && second.All(seat.Contains) &&
                !first.SequenceEqual(second),
                "A real paid Nullification must assemble and dissolve its own distinct seal inside the responder's portrait.");
            AdvanceTo(800);
            Render(host.Root, 1120, 740, null);
            Assert(!VisibleMotionShapes(VisualTreeHelper.GetDrawing(host.Layer), IsCounterStrip).Any() && Boundary(game) == state,
                "The counterspell seal must retire without guessing or advancing the chain outcome.");
            if (movie is not null)
            {
                var path = Path.Combine(output, "native-nullification-paid.gif");
                using (var file = File.Create(path)) movie.Save(file);
                BattleEffectsChecks.SetGifTiming(path, 4);
                using var input = File.OpenRead(path);
                var decoded = new GifBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert(decoded.Frames.Count == 21 && decoded.Frames.All(frame =>
                    Convert.ToInt32(((BitmapMetadata)frame.Metadata).GetQuery("/grctlext/Delay")) == 4),
                    "The real paid counterspell movie must retain continuous 40ms frames.");
            }
            Console.WriteLine($"Native Nullification: paid physical {card.Id}, cue seq{cue.Sequence}, rev{cue.Revision}; seal strips {first.Length}/{second.Length}; unchanged committed chain.");

            void AdvanceTo(int target)
            {
                if (movie is not null)
                    while (time + 40 <= target) { host.Clock.Advance(.04); time += 40; Sample(); }
                if (time < target) { host.Clock.Advance((target - time) / 1000d); time = target; }
                host.Layer.InvalidateVisual();
            }
            void Sample()
            {
                if (movie is null) return;
                host.Layer.InvalidateVisual(); movie.Frames.Add(BitmapFrame.Create(host.MovieFrame()));
            }
        }
        static bool IsCounterStrip(GeometryDrawing shape) => shape is
            { Geometry: StreamGeometry, Brush: SolidColorBrush fill, Pen: null } &&
            (fill.Color == Color.FromRgb(116, 231, 255) || fill.Color == Color.FromRgb(175, 151, 255));
    }

    private static void GroupResponseMotion(string output)
    {
        var signatures = new List<string>();
        foreach (var incoming in new[] { CardKind.ArrowBarrage, CardKind.BarbarianAssault })
        {
            var kind = incoming == CardKind.ArrowBarrage ? DecisionKind.RespondDodge : DecisionKind.RespondSlash;
            var (prepared, registry) = StableHandResponseFixture.Create(kind, output, arrowBarrage: incoming == CardKind.ArrowBarrage);
            using (prepared)
            {
                var checkpoint = Engine(prepared).CreateCheckpoint();
                Assert(checkpoint.Commands.Last() is AdvanceOneStepCommand,
                    "The fixed group response must be opened by its final real AI step.");
                var prefix = GameReplay.Replay(checkpoint.Options, checkpoint.Commands.SkipLast(1), registry);
                var store = new MemorySaveStore();
                store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, prefix.CreateCheckpoint()));
                using var model = new MainViewModel(false, prefix.Seed, true, store, useExpandedContent: false,
                    contentRegistry: registry, historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
                    { IsMotionEnabled = true, IsSoundEnabled = false, IsMusicEnabled = false };
                model.LoadManualGameCommand.Execute(null);
                Assert(!model.HasSaveError && model.BattleCues.Count == 0 && model.CanStepAi,
                    "The native group sample must restore immediately before its actual public inquiry.");
                using var host = new NativeResponseStatusChecks.NativeHost(model);
                var label = "native-group-" + incoming;
                host.Capture(output, label + "-before", 1120);
                var game = Engine(model);
                var eventCursor = game.Events.Count;
                model.StepAiCommand.Execute(null);
                Assert(game.PendingDecision is { PlayerSeat: 0 } prompt && prompt.Kind == kind && prompt.IncomingCard == incoming,
                    "One native AI step must publish the verified human group response.");
                var request = game.Events.Skip(eventCursor).Last(envelope => envelope.Payload is ResponseRequestedEvent response &&
                    response.TargetSeat == 0 && response.IncomingCard == incoming);
                var cue = model.BattleCues.Single(candidate => candidate.Sequence == request.Sequence);
                Assert(cue is { Kind: BattleCueKind.ResponseWindow, IsInquiry: true, SourceSeat: 0, HpDelta: 0 } &&
                    cue.CardKind == incoming && cue.TargetSeats.SequenceEqual([1]) && cue.PublicCard is null,
                    "The native focus must use the exact committed inquiry, without a paid face or fabricated damage.");
                var unchanged = Boundary(game);
                var movie = RecordMotion ? new GifBitmapEncoder() : null;
                var time = 0;
                Sample();
                AdvanceTo(200);
                host.Capture(output, label + "-200ms", 1120);
                var responder = Find<Button>(host.Root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == 0);
                var seat = responder.TransformToVisual(host.Layer).TransformBounds(new Rect(responder.RenderSize));
                var first = VisibleProjectileTips(VisualTreeHelper.GetDrawing(host.Layer)).ToArray();
                Assert(first.Length > 0 && first.All(seat.Contains),
                    "The real current responder must show visible arrow/spear motion inside its own portrait, without sweeping future recipients.");
                AdvanceTo(400);
                host.Capture(output, label + "-400ms", 1120);
                var second = VisibleProjectileTips(VisualTreeHelper.GetDrawing(host.Layer)).ToArray();
                Assert(second.Length > 0 && second.All(seat.Contains) && !first.SequenceEqual(second) && Boundary(game) == unchanged,
                    "The native group inquiry must actually move its projectiles while leaving every command, card and HP value unchanged.");
                signatures.Add(string.Join(";", second.Select(bounds =>
                    $"{(bounds.X - seat.X) / seat.Width:F4},{(bounds.Y - seat.Y) / seat.Height:F4},{bounds.Width / seat.Width:F4},{bounds.Height / seat.Height:F4}")));
                var response = model.Hand.Last(card => card.IsPlayable);
                var before = game.Revision;
                var oldPrompt = game.PendingDecision!;
                var payment = oldPrompt.Choices.Single(choice => choice.Cards.SequenceEqual([response.Id]));
                model.SelectCardCommand.Execute(response);
                model.ConfirmSelectedCommand.Execute(null);
                Assert(game.Revision == before + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                    answer.PromptId == oldPrompt.PromptId && answer.Choice == payment.Id &&
                    model.BattleCues.Any(paid => paid.Kind == BattleCueKind.Response && paid.PublicCardId == response.Id),
                    "The real native confirmation must pay exactly its published response, not turn the inquiry into an attack.");
                AdvanceTo(440);
                host.Capture(output, label + "-paid", 1120);
                Assert(!VisibleProjectileTips(VisualTreeHelper.GetDrawing(host.Layer)).Any(),
                    "Paying the response must immediately retire the old group inquiry focus.");
                var paidBoundary = Boundary(game);
                if (movie is not null)
                {
                    AdvanceTo(1200);
                    var path = Path.Combine(output, label + ".gif");
                    using (var file = File.Create(path)) movie.Save(file);
                    BattleEffectsChecks.SetGifTiming(path, 4);
                    using var fileIn = File.OpenRead(path);
                    var decoded = new GifBitmapDecoder(fileIn, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    Assert(decoded.Frames.Count == 31 && decoded.Frames.All(frame =>
                        Convert.ToInt32(((BitmapMetadata)frame.Metadata).GetQuery("/grctlext/Delay")) == 4),
                        "The actual group response movie must retain continuous 40ms frames through payment.");
                }
                Assert(Boundary(game) == paidBoundary, "Recording a response must not advance its game state.");
                Console.WriteLine($"Native {incoming}: inquiry seq{cue.Sequence}, rev{cue.Revision}; visible projectile shapes {first.Length}/{second.Length}; paid physical {response.Id} through native confirmation.");

                void AdvanceTo(int target)
                {
                    if (movie is not null)
                        while (time + 40 <= target)
                        {
                            host.Clock.Advance(.04); time += 40; Sample();
                        }
                    if (time < target) { host.Clock.Advance((target - time) / 1000d); time = target; }
                    host.Layer.InvalidateVisual();
                }
                void Sample()
                {
                    if (movie is null) return;
                    host.Layer.InvalidateVisual();
                    movie.Frames.Add(BitmapFrame.Create(host.MovieFrame()));
                }
            }
        }
        Assert(signatures.Count == 2 && signatures[0] != signatures[1],
            "Actual Arrow Barrage and Barbarian Assault inquiries must have different moving shapes, not a shared aura.");
    }

    private static IEnumerable<Rect> VisibleProjectileTips(Drawing? drawing) => VisibleMotionShapes(drawing,
        shape => shape is { Geometry: StreamGeometry, Brush: SolidColorBrush fill, Pen.Brush: SolidColorBrush edge } &&
            fill.Color == Color.FromRgb(255, 249, 218) &&
            (edge.Color == Color.FromRgb(255, 203, 100) || edge.Color == Color.FromRgb(204, 187, 158)));

    private static IEnumerable<Rect> VisibleMotionShapes(Drawing? drawing, Func<GeometryDrawing, bool> select,
        Matrix? inheritedTransform = null, Geometry? inheritedClip = null, double opacity = 1)
    {
        var transform = inheritedTransform ?? Matrix.Identity;
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            var clip = inheritedClip;
            if (group.ClipGeometry is { } ownClip)
            {
                var current = ownClip.Clone(); current.Transform = new MatrixTransform(next);
                clip = clip is null ? current : new CombinedGeometry(GeometryCombineMode.Intersect, clip, current);
            }
            foreach (var child in group.Children)
                foreach (var bounds in VisibleMotionShapes(child, select, next, clip, opacity * group.Opacity)) yield return bounds;
        }
        else if (opacity > .1 && drawing is GeometryDrawing tip && select(tip))
        {
            // Unfilled Dodge silhouettes are strokes: their open centerline has
            // no painted area. Measure the actual widened ink before clipping.
            Geometry visible = (tip.Brush is null && tip.Pen is not null
                ? tip.Geometry.GetWidenedPathGeometry(tip.Pen) : tip.Geometry).Clone();
            visible.Transform = new MatrixTransform(transform);
            if (inheritedClip is not null) visible = new CombinedGeometry(GeometryCombineMode.Intersect, visible, inheritedClip);
            if (visible.GetArea() > 1) yield return visible.Bounds;
        }
    }

    private static void OptionalSkill(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new SkillTargetSelectionChecks.TargetFixture("optional", 4));
        var source = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = SkillTargetSelectionChecks.TargetFixture.ModeId, UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Assert(source.Submit(new StartGameCommand()).Accepted, "The native optional-target fixture must start.");
        Assert(source.Submit(new SelectGeneralCommand(0, SkillTargetSelectionChecks.TargetFixture.OwnerId,
            source.Revision, source.PendingDecision!.PromptId)).Accepted, "The native optional-target general must be selected.");
        for (var step = 0; step < 30 && source.PendingDecision?.SkillPrompt?.SkillId != SkillTargetSelectionChecks.TargetFixture.SkillId; step++)
            Assert(source.Submit(new AdvanceOneStepCommand(source.Revision)).Accepted, "The native optional-target prefix must advance.");
        Assert(source.PendingDecision?.SkillPrompt?.SkillId == SkillTargetSelectionChecks.TargetFixture.SkillId,
            "The verified seed must reach its real optional-target prompt.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, source.CreateCheckpoint()));
        using var model = new MainViewModel(false, source.Seed, true, store, useExpandedContent: false,
            contentRegistry: registry, historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        foreach (var selected in new[] { false, true })
        {
            model.LoadManualGameCommand.Execute(null);
            var game = Engine(model);
            var before = Boundary(game);
            var checkpoint = game.CreateCheckpoint();
            var prompt = model.ChoiceSnapshot.PendingDecision!;
            var decline = prompt.Choices.Single(choice => choice.Cards.Count == 0 && choice.Targets.Count == 0);
            if (selected) model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 2));
            var window = new MainWindow(model);
            try
            {
                Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, $"native-action-skill-{selected}-before.png"));
                var cancel = (Button)window.FindName("CancelActionButton");
                Assert(model.IsSkillTargetSelectionPending && Boundary(game) == before &&
                    model.ActionHint.Contains($"「{model.ActionDockCancelText}」", StringComparison.Ordinal),
                    "The optional skill must retain its native prompt and explain its actual decline button for either target draft.");
                AssertCaption(cancel, model, "取 消", enabled: true);
                var clicks = 0;
                cancel.Click += (_, _) => clicks++;
                Click(cancel);
                var reference = GameReplay.Restore(checkpoint, registry);
                Assert(reference.Submit(new AnswerPromptCommand(0, prompt.PromptId, decline.Id, checkpoint.Revision)).Accepted &&
                    clicks == 1 && game.Revision == checkpoint.Revision + 1 &&
                    game.AcceptedCommands.Count == checkpoint.Commands.Count + 1 &&
                    game.AcceptedCommands.Last() is AnswerPromptCommand answer && answer.Choice == decline.Id &&
                    answer.PromptId == prompt.PromptId && !model.HasSelection && Boundary(game) == Boundary(reference),
                    "The optional skill's actual decline button must submit its published empty-target choice once, without confirming drafted targets.");
            }
            finally { window.Content = null; window.DataContext = null; window.Close(); }
        }
    }

    private static void NormalDraft(string output)
    {
        var (registry, source, slash) = LongChainFeedbackChecks.CreateFixture();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, source.CreateCheckpoint()));
        using var model = new MainViewModel(false, source.Seed, true, store, useExpandedContent: false,
            contentRegistry: registry, historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var game = Engine(model);
        var before = Boundary(game);
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            Render(root, 1120, 740, Path.Combine(output, "native-action-play-empty.png"));
            var cancel = (Button)window.FindName("CancelActionButton");
            AssertCaption(cancel, model, "取 消", enabled: false);
            AssertBackground(cancel, "selfseat__selfSeatCancelBtn_disabled.png");
            model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == slash.Id));
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 1));
            Render(root, 1120, 740, Path.Combine(output, "native-action-play-draft.png"));
            Assert(model.CanConfirmSelected && Boundary(game) == before,
                "The fixed eight-seat native Slash must form a complete, reversible ordinary draft.");
            AssertCaption(cancel, model, "取 消", enabled: true);
            var clicks = 0;
            cancel.Click += (_, _) => clicks++;
            Click(cancel);
            window.UpdateLayout();
            Assert(clicks == 1 && Boundary(game) == before && !model.HasSelection && !model.CanConfirmSelected &&
                model.Seats.All(seat => !seat.IsSelectedTarget),
                "Ordinary native cancellation must clear its card and portrait targets without advancing or paying a cost.");
            AssertCaption(cancel, model, "取 消", enabled: false);
            AssertBackground(cancel, "selfseat__selfSeatCancelBtn_disabled.png");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
    }

    // Actual Button class handlers in a hidden message-only HWND; the key events
    // are synthetic and do not claim physical foreground keyboard acceptance.
    private static void PressSpace(MainWindow window, Button button, GameEngine game, string output)
    {
        var commandCountBeforeLoading = game.AcceptedCommands.Count;
        var revisionBeforeLoading = game.Revision;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = window.Top = -30000;
        window.Width = 1120;
        window.Height = 820;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        const int extendedStyle = -20, noActivate = 0x08000000;
        SetWindowLong(handle, extendedStyle, GetWindowLong(handle, extendedStyle) | noActivate);
        var previous = SetParent(handle, new nint(-3));
        var error = Marshal.GetLastPInvokeError();
        Assert((previous != 0 || error == 0) && IsMessageOnlyWindow(handle),
            "The action-button fixture must own a message-only native Window.");
        window.Show();
        ShowWindow(handle, 0);
        SetFocus(handle);
        Pump();
        var focused = button.Focus();
        Pump();
        var loadedEvidence = PressBoundary("loaded-and-focused");
        var pressGuard = new
        {
            FocusSucceeded = focused, window.IsLoaded, window.IsVisible,
            MessageOnly = IsMessageOnlyWindow(handle), NativeVisible = IsWindowVisible(handle),
            Handle = handle.ToInt64(), Foreground = GetForegroundWindow().ToInt64(), NativeFocus = GetFocus().ToInt64(),
            FocusedButton = ReferenceEquals(Keyboard.FocusedElement, button),
            FocusElement = (Keyboard.FocusedElement as FrameworkElement)?.Name,
            SamePresentationSource = ReferenceEquals(PresentationSource.FromVisual(button), PresentationSource.FromVisual(window)),
            ButtonSource = PresentationSource.FromVisual(button)?.GetType().FullName,
            WindowSource = PresentationSource.FromVisual(window)?.GetType().FullName,
            Modifiers = Keyboard.Modifiers, LeftButton = Mouse.LeftButton,
            AsyncLeftDown = (GetAsyncKeyState(0x01) & 0x8000) != 0,
            button.IsEnabled, ButtonName = button.Name,
            DispatcherThread = window.Dispatcher.Thread.ManagedThreadId,
            CurrentThread = Environment.CurrentManagedThreadId
        };
        Console.WriteLine("Native action Space prerequisites: " + JsonSerializer.Serialize(new { loadedEvidence, pressGuard }));
        Assert(pressGuard.FocusSucceeded && pressGuard.IsLoaded && pressGuard.IsVisible && pressGuard.MessageOnly &&
            !pressGuard.NativeVisible && pressGuard.Foreground != pressGuard.Handle && pressGuard.NativeFocus == pressGuard.Handle &&
            pressGuard.FocusedButton && pressGuard.SamePresentationSource &&
            pressGuard.Modifiers == ModifierKeys.None && pressGuard.LeftButton == MouseButtonState.Released &&
            !pressGuard.AsyncLeftDown && pressGuard.IsEnabled,
            "Synthetic Space must begin on the focused, enabled actual button with idle devices and no foreground ownership; a pressed mouse button or modifier is external input interference. " +
            JsonSerializer.Serialize(pressGuard));
        var surface = Surface(button);
        var outline = (Border)button.Template.FindName("ButtonFocusOutline", button);
        Assert(outline.Visibility == Visibility.Visible && Math.Abs(surface.RenderTransform.Value.OffsetY) < .01,
            "The focused action must show its focus outline before pressing.");
        var before = Boundary(game);
        object heldEvidence;
        object beforeReleaseEvidence;
        RenderTargetBitmap heldBitmap;
        bool releaseDevicesIdle;
        // Keep the synthetic gesture on this dispatcher stack. The general Render
        // helper pumps queued input; encoding the held image also need not occur
        // while the button is held. Capture the real visual and freeze it first.
        using (window.Dispatcher.DisableProcessing())
        {
            Route(Keyboard.PreviewKeyDownEvent, Keyboard.KeyDownEvent);
            window.UpdateLayout();
            heldEvidence = PressBoundary("held");
            Assert(button.IsPressed && Math.Abs(surface.RenderTransform.Value.OffsetY - 2) < .01 &&
                Math.Abs(surface.Opacity - .7) < .01 && Boundary(game) == before,
                "Holding Space must visibly depress the native button by two DIP without prematurely committing the response.");
            heldBitmap = new RenderTargetBitmap(1120, 740, 96, 96, PixelFormats.Pbgra32);
            heldBitmap.Render((FrameworkElement)window.Content);
            heldBitmap.Freeze();
            beforeReleaseEvidence = PressBoundary("rendered-before-release");
            releaseDevicesIdle = Mouse.LeftButton == MouseButtonState.Released &&
                (GetAsyncKeyState(0x01) & 0x8000) == 0 && Keyboard.Modifiers == ModifierKeys.None;
            Route(Keyboard.PreviewKeyUpEvent, Keyboard.KeyUpEvent);
        }
        Assert(releaseDevicesIdle,
            "External input interference: synthetic Space release requires the real mouse left button released and no keyboard modifiers. " +
            JsonSerializer.Serialize(beforeReleaseEvidence));
        var heldEncoder = new PngBitmapEncoder();
        heldEncoder.Frames.Add(BitmapFrame.Create(heldBitmap));
        using (var heldFile = File.Create(Path.Combine(output, "native-action-response-space-held.png")))
            heldEncoder.Save(heldFile);
        Pump();
        var releasedEvidence = PressBoundary("released");
        Assert(!button.IsPressed && Math.Abs(surface.RenderTransform.Value.OffsetY) < .01,
            "Releasing the native action must remove its pressed displacement.");
        Console.WriteLine("Native action press: hidden HWND, synthetic Space down/up, pressed Y=2, released Y=0.");
        Console.WriteLine("Native action Space boundary: " + JsonSerializer.Serialize(new
        {
            commandCountBeforeLoading, revisionBeforeLoading, loadedEvidence, heldEvidence,
            beforeReleaseEvidence, releasedEvidence
        }));

        void Pump() { window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); window.UpdateLayout(); }
        object PressBoundary(string stage) => new
        {
            stage, game.Revision, CommandCount = game.AcceptedCommands.Count,
            CommandsAdded = CommandJson.Serialize(game.AcceptedCommands.Skip(commandCountBeforeLoading).ToArray()),
            Prompt = game.CreateSnapshot(0).PendingDecision?.Kind.ToString(),
            FocusedButton = ReferenceEquals(Keyboard.FocusedElement, button),
            FocusName = (Keyboard.FocusedElement as FrameworkElement)?.Name,
            NativeFocus = GetFocus().ToInt64(), Modifiers = Keyboard.Modifiers.ToString(),
            button.IsEnabled, button.IsPressed,
            LeftButton = Mouse.LeftButton.ToString(),
            AsyncLeftDown = (GetAsyncKeyState(0x01) & 0x8000) != 0,
            button.IsMouseCaptured, CapturedElement = (Mouse.Captured as FrameworkElement)?.Name,
            ClickMode = button.ClickMode.ToString(),
            IsSpaceKeyDown = typeof(ButtonBase).GetProperty("IsSpaceKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(button),
            AutoAdvance = (window.DataContext as MainViewModel)?.IsAutoAdvance
        };
        void Route(RoutedEvent preview, RoutedEvent bubble)
        {
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(button)!, 0, Key.Space)
                { RoutedEvent = preview };
            var release = bubble == Keyboard.KeyUpEvent;
            if (release) TraceRelease("before-preview", key);
            button.RaiseEvent(key);
            if (release) TraceRelease("after-preview", key);
            if (key.Handled) return;
            key.RoutedEvent = bubble;
            if (release) TraceRelease("before-bubble", key);
            button.RaiseEvent(key);
            if (release) TraceRelease("after-bubble", key);

            void TraceRelease(string edge, KeyEventArgs routed) => Console.WriteLine(
                "Native action Space release route: " + JsonSerializer.Serialize(new
                {
                    edge, Event = routed.RoutedEvent.Name, routed.Handled,
                    Key = routed.Key.ToString(), routed.IsRepeat,
                    Source = (routed.Source as FrameworkElement)?.Name,
                    OriginalSource = (routed.OriginalSource as FrameworkElement)?.Name,
                    State = PressBoundary(edge)
                }));
        }
    }

    private static void AssertCaption(Button button, MainViewModel model, string caption, bool enabled) =>
        Assert(button.Visibility == Visibility.Visible && Equals(button.Content, caption) &&
            AutomationProperties.GetName(button) == caption && model.ActionDockCancelText == caption && button.IsEnabled == enabled,
            $"The actual action button must expose {caption} with enabled={enabled} and a matching accessible name.");

    private static Border Surface(Button button) => (Border)button.Template.FindName("ButtonSurface", button);
    private static void AssertBackground(Button button, string asset) =>
        Assert(Surface(button).Background is ImageBrush brush &&
            brush.ImageSource.ToString().EndsWith(asset, StringComparison.OrdinalIgnoreCase),
            $"The actual {(button.IsEnabled ? "enabled" : "disabled")} action surface must use {asset}.");
    private static void Click(Button button) => typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    private static string DescribeDeclineBoundary(GameEngine game, GameEngine reference, MainViewModel model,
        MainWindow window, Button button, PendingDecision prompt, PromptChoice decline, int selectedCard,
        int clicks, int commandCountBefore, int movementCountBefore, long revisionBefore)
    {
        var last = game.AcceptedCommands.LastOrDefault() as AnswerPromptCommand;
        var paidCard = game.CardMovements.Skip(movementCountBefore).Any(move => move.CardId == selectedCard &&
            move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Respond);
        var actualBoundary = Boundary(game);
        var expectedBoundary = Boundary(reference);
        return JsonSerializer.Serialize(new
        {
            clicks, ClicksOnce = clicks == 1,
            CommandCountBefore = commandCountBefore, CommandCountAfter = game.AcceptedCommands.Count,
            CommandsOnce = game.AcceptedCommands.Count == commandCountBefore + 1,
            RevisionBefore = revisionBefore, RevisionAfter = game.Revision, RevisionOnce = game.Revision == revisionBefore + 1,
            ExactAnswer = last is not null && last.ActorSeat == 0 && last.PromptId == prompt.PromptId &&
                last.Choice == decline.Id && last.ExpectedRevision == revisionBefore,
            ActorMatches = last?.ActorSeat == 0, PromptMatches = last?.PromptId == prompt.PromptId,
            ChoiceMatches = last?.Choice == decline.Id, AnswerRevisionMatches = last?.ExpectedRevision == revisionBefore,
            LastCommandType = game.AcceptedCommands.LastOrDefault()?.GetType().Name,
            CommandsAdded = CommandJson.Serialize(game.AcceptedCommands.Skip(commandCountBefore).ToArray()),
            ExpectedPrompt = prompt.PromptId.Value, ExpectedChoice = decline.Id.Value,
            selectedCard, paidCard, model.HasSelection, model.IsAutoAdvance,
            DraftCleared = !model.HasSelection, CardWasNotPaid = !paidCard,
            SelectedCardMoves = game.CardMovements.Skip(movementCountBefore).Where(move => move.CardId == selectedCard).ToArray(),
            SameEngine = ReferenceEquals(game, Engine(model)), model.PromptText,
            FocusedButton = ReferenceEquals(Keyboard.FocusedElement, button),
            FocusName = (Keyboard.FocusedElement as FrameworkElement)?.Name,
            window.IsLoaded, window.IsVisible, button.IsEnabled, button.IsPressed,
            Modifiers = Keyboard.Modifiers.ToString(),
            NativeBoundaryMatches = actualBoundary == expectedBoundary,
            BoundaryFirstDifference = FirstBoundaryDifference(actualBoundary, expectedBoundary)
        });
    }
    private static object? FirstBoundaryDifference(string actual, string expected)
    {
        if (actual == expected) return null;
        using var actualDocument = JsonDocument.Parse(actual);
        using var expectedDocument = JsonDocument.Parse(expected);
        foreach (var property in actualDocument.RootElement.EnumerateObject())
        {
            var actualPart = property.Value.GetRawText();
            var expectedPart = expectedDocument.RootElement.GetProperty(property.Name).GetRawText();
            if (actualPart == expectedPart) continue;
            var index = 0;
            while (index < actualPart.Length && index < expectedPart.Length && actualPart[index] == expectedPart[index]) index++;
            var start = Math.Max(0, index - 80);
            return new
            {
                Field = property.Name, Index = index,
                Actual = actualPart.Substring(start, Math.Min(240, actualPart.Length - start)),
                Expected = expectedPart.Substring(start, Math.Min(240, expectedPart.Length - start))
            };
        }
        return new { Field = "$", ActualLength = actual.Length, ExpectedLength = expected.Length };
    }
    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, Commands = CommandJson.Serialize(game.AcceptedCommands), Movements = game.CardMovements,
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        Views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray()
    });
    private static bool IsMessageOnlyWindow(nint handle)
    {
        var cursor = nint.Zero;
        for (var index = 0; index < 4096; index++)
        {
            cursor = FindWindowEx(new nint(-3), cursor, null, null);
            if (cursor == handle) return true;
            if (cursor == 0) return false;
        }
        return false;
    }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern nint SetFocus(nint handle);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint handle, int index, int value);
}
