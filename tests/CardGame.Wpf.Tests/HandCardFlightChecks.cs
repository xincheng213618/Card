using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class HandCardFlightChecks
{

    public static void OriginBoundaries(string output)
    {
        var root = new Canvas { Width = 900, Height = 550, Background = Brushes.DarkSlateGray };
        var hand = new Canvas { Width = 400, Height = 176, ClipToBounds = true };
        Place(hand, 180, 365);
        var card = new Border { Width = 110, Height = 156, Background = Brushes.Wheat };
        BattleFeedbackLayer.SetHandAnchor(card, 41); Canvas.SetLeft(card, 40); Canvas.SetTop(card, 12); hand.Children.Add(card);
        var portraits = new[] { new Rect(720, 355, 120, 170), new Rect(720, 25, 120, 170) };
        for (var seat = 0; seat < portraits.Length; seat++)
        {
            var portrait = new Border { Width = 120, Height = 170, Background = Brushes.Black };
            BattleFeedbackLayer.SetSeatAnchor(portrait, seat); Place(portrait, portraits[seat].X, portraits[seat].Y);
        }
        var landing = new Border { Width = 86, Height = 120, Background = Brushes.Olive }; Place(landing, 480, 210);
        var cues = new ObservableCollection<BattleCue>(); var clock = new FrameClock();
        var layer = new BattleFeedbackLayer { Width = 900, Height = 550, AnchorRoot = root, HandRoot = hand, HandOwnerSeat = 0, Cues = cues, Clock = clock };
        root.Children.Add(layer);
        Capture();
        long sequence = 0;
        // Control-only projection lifecycle: native engine removal/commit is
        // covered separately. Keep the observable history across every step.
        layer.CommittedRevision = 10; Capture();
        var origin = card.TransformToVisual(layer).TransformBounds(new Rect(card.RenderSize));
        hand.Children.Remove(card); Capture();
        layer.CommittedRevision = 11;
        AddCue(0, 41);
        Assert(Near(FaceBounds(layer, CardKind.Slash).Single(),
                new Rect(origin.X + 1, origin.Y + 1, origin.Width - 2, origin.Height - 2)),
            "Layout after physical removal discarded the last hand pose before its new-revision public cue.");
        // Repeat within the same revision so expiry pruning cannot mask a
        // missing exact-ID consumption of the first cue's cached origin.
        CheckFallback(0, 41, "already consumed origin");

        layer.CommittedRevision = 12;
        hand.Children.Add(card); Capture();
        hand.Children.Remove(card); Capture();
        layer.CommittedRevision = 13; Capture(); // No matching public cue consumes this removed card.
        layer.CommittedRevision = 14;
        CheckFallback(0, 41, "unconsumed origin beyond one commit");

        hand.Children.Add(card); Capture();
        Canvas.SetLeft(card, 600); Capture();
        hand.Children.Remove(card); Capture();
        layer.CommittedRevision = 15;
        CheckFallback(0, 41, "removed after an offscreen pose");

        Canvas.SetLeft(card, 40); hand.Children.Add(card); Capture();
        card.Visibility = Visibility.Collapsed; Capture();
        hand.Children.Remove(card); Capture();
        layer.CommittedRevision = 16;
        CheckFallback(0, 41, "removed after a hidden pose");
        card.Visibility = Visibility.Visible; hand.Children.Add(card); Capture();

        CheckFallback(1, 41, "foreign owner");
        CheckFallback(0, 99, "unmatched public card");
        Canvas.SetLeft(card, 600); Capture();
        CheckFallback(0, 41, "offscreen hand card");
        Canvas.SetLeft(card, 40); Capture();
        hand.Visibility = Visibility.Collapsed; Capture();
        CheckFallback(0, 41, "hidden hand root");
        hand.Visibility = Visibility.Visible; Capture();
        hand.Children.Remove(card);
        cues.Clear(); // Clear a removed card's cached position before a fresh cue.
        CheckFallback(0, 41, "reset boundary");
        hand.Children.Add(card); Capture();
        layer.MotionEnabled = false; hand.Children.Remove(card); layer.MotionEnabled = true;
        CheckFallback(0, 41, "motion boundary");
        hand.Children.Add(card); Capture();
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        hand.Children.Remove(card);
        cues.Add(new(++sequence, BattleCueKind.Card, 0, [], "杀", "你") { CardKind = CardKind.Slash, PublicCardId = 41 });
        Capture();
        Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning, "An unloaded hand origin observed a new cue.");
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Capture();
        Assert(layer.ActiveEffectCount == 0, "Reloading replayed a historical hand-origin cue.");
        CheckFallback(0, 41, "unload/reload boundary");
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert(!layer.IsHitTestVisible && !layer.Focusable && layer.ActiveEffectCount == 0,
            "The hand flight retained animation or intercepted gameplay after unloading.");
        Console.WriteLine("Hand origin boundaries: removed-pose revision handoff, exact-ID consumption, one-commit expiry, removed offscreen/hidden fallback, actor/ID match, reset/motion/unload and historical cue non-replay passed.");

        void Place(FrameworkElement element, double x, double y)
        { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); root.Children.Add(element); }
        void Capture()
        {
            layer.InvalidateVisual(); root.Measure(new Size(900, 550)); root.Arrange(new Rect(0, 0, 900, 550)); root.UpdateLayout();
            new RenderTargetBitmap(900, 550, 96, 96, PixelFormats.Pbgra32).Render(root);
        }
        void AddCue(int seat, int id)
        {
            clock.Advance(2); // Expire the previous flight without resetting the origin cache or cue history.
            BattleFeedbackLayer.SetPlayAnchor(landing, ++sequence);
            cues.Add(new(sequence, BattleCueKind.Card, seat, [], "杀", "公开行动")
                { CardKind = CardKind.Slash, PublicCardId = id, Revision = layer.CommittedRevision });
            clock.Advance(.012); Capture();
            Assert(layer.ActiveEffectCount == 1, "A fresh boundary cue must retire the prior flight without clearing history.");
        }
        void CheckFallback(int seat, int id, string boundary)
        {
            AddCue(seat, id);
            var image = FaceBounds(layer, CardKind.Slash).Single();
            var portrait = portraits[seat]; var center = new Point(portrait.X + portrait.Width / 2, portrait.Y + portrait.Height / 2);
            Assert((new Point(image.X + image.Width / 2, image.Y + image.Height / 2) - center).Length < .1,
                $"A {boundary} cue reused a private/stale hand slot.");
        }
    }

    private static bool Near(Rect first, Rect second) => Math.Abs(first.X - second.X) < .1 && Math.Abs(first.Y - second.Y) < .1 &&
        Math.Abs(first.Width - second.Width) < .1 && Math.Abs(first.Height - second.Height) < .1;
    internal static void AssertPlaybackClearOfPublicCaptions(FrameworkElement root, MainViewModel vm)
    {
        var resume = Find<Button>(root).Single(button => ReferenceEquals(button.Command, vm.RunToHumanCommand) &&
            Equals(button.Content, "继续推进"));
        Assert(resume.Visibility == Visibility.Visible && resume.IsEnabled && resume.ActualHeight > 0,
            "The paused real AI boundary must expose an enabled resume action.");
        var buttonBounds = resume.TransformToVisual(root).TransformBounds(new Rect(resume.RenderSize));
        var captions = Find<TextBlock>(root).Where(text => text.Name == "PlayActorCaption").ToArray();
        Assert(captions.Length == vm.RecentPlays.Count && captions.All(text =>
                !buttonBounds.IntersectsWith(text.TransformToVisual(root).TransformBounds(new Rect(text.RenderSize)))),
            "The resume action covers a committed public card's actor/action caption.");
        Console.WriteLine($"Playback/public captions: resume={buttonBounds}; {captions.Length} captions clear.");
    }
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
    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
        public void Reset() => _ticks = 0;
    }
}
