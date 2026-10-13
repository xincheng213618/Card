using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class PublicCardHandoffChecks
{
    public static void ArrivalAndBoundaries(string output)
    {
        var root = new Canvas { Width = 780, Height = 440, Background = Brushes.DarkSlateGray };
        for (var seat = 0; seat < 2; seat++)
        {
            var portrait = new Border { Width = 85, Height = 120, Background = Brushes.Black };
            BattleFeedbackLayer.SetSeatAnchor(portrait, seat);
            Canvas.SetLeft(portrait, 40 + seat * 570); Canvas.SetTop(portrait, 20); root.Children.Add(portrait);
        }
        var cues = new ObservableCollection<BattleCue>(); var clock = new FrameClock();
        var layer = new BattleFeedbackLayer { Width = 780, Height = 440, AnchorRoot = root, Cues = cues, Clock = clock };
        var slash = Play(1, CardKind.Slash, "关羽", "使用", 250);
        var dodge = Play(2, CardKind.Dodge, "赵云", "打出", 355);
        root.Children.Add(layer);
        Capture();
        var slashFace = Face(slash); var dodgeFace = Face(dodge);
        Assert(Panel(slashFace).Opacity == 1 && Panel(dodgeFace).Opacity == 1,
            "Historical public faces acquired an animation wait without a new accepted cue.");
        cues.Add(new(1, BattleCueKind.Card, 0, [1], "杀", "关羽") { CardKind = CardKind.Slash });
        cues.Add(new(2, BattleCueKind.Response, 1, [], "闪", "赵云") { CardKind = CardKind.Dodge });
        clock.Advance(.01); Capture("waiting");
        Pending(slashFace); Pending(dodgeFace);
        Assert(Faces(layer).SequenceEqual([CardArt.Get(CardKind.Slash)]),
            "A delayed response displayed its face before its flight started.");
        var slot = slashFace.TransformToVisual(layer).TransformBounds(new Rect(slashFace.RenderSize));
        clock.Advance(.34); Capture("response-moving");
        Revealed(slashFace); Pending(dodgeFace);
        Assert(Faces(layer).SequenceEqual([CardArt.Get(CardKind.Dodge)]) &&
            slot == slashFace.TransformToVisual(layer).TransformBounds(new Rect(slashFace.RenderSize)),
            "The attack handoff moved its reserved slot or duplicated its arrived face.");
        clock.Advance(.4); Capture("both-arrived");
        Revealed(slashFace); Revealed(dodgeFace);
        Assert(!Faces(layer).Any(), "An arrived attack/response retained a second transient face.");
        Canvas.SetLeft(slash, 175); Capture();
        Revealed(slashFace);
        Assert(!Faces(layer).Any(), "Rearranging an arrived public row restarted its flight.");

        long sequence = 2;
        Waiting(); layer.MotionEnabled = false; Capture(); Revealed(slashFace);
        Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning, "Motion off kept a hidden public face or running timer.");
        layer.MotionEnabled = true; Capture(); Revealed(slashFace);
        Assert(!Faces(layer).Any(), "Motion on replayed an old public face.");

        Waiting(); cues.Clear(); Capture(); Revealed(slashFace);
        Waiting(); layer.Cues = new ObservableCollection<BattleCue>(); Capture(); Revealed(slashFace);
        layer.Cues = cues;
        Waiting(); layer.AnchorRoot = null; Capture(); Revealed(slashFace);
        layer.AnchorRoot = root; cues.Clear();

        Waiting(); root.Children.Remove(slash); Capture(); Revealed(slashFace);
        var replacement = Play(++sequence, CardKind.Peach, "华佗", "使用", 250);
        cues.Add(new(sequence, BattleCueKind.Card, 0, [], "桃", "华佗") { CardKind = CardKind.Peach });
        clock.Advance(.01); Capture("replaced-row"); Pending(Face(replacement));
        Assert(Faces(layer).SequenceEqual([CardArt.Get(CardKind.Peach)]),
            "Replacing the public row retained the removed action's flying face.");
        root.Children.Remove(replacement); root.Children.Add(slash); Capture(); Revealed(slashFace);
        Assert(!Faces(layer).Any(), "Reattaching an old public row restarted its retired flight.");
        cues.Clear();

        Waiting(); clock.Advance(2); Capture(); Revealed(slashFace);
        Assert(layer.ActiveEffectCount == 0, "Expiry left a hidden static face after the final cue vanished.");
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Waiting(); layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); Capture(); Revealed(slashFace);
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); Capture(); Revealed(slashFace);
        Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning && !layer.IsHitTestVisible && !layer.Focusable,
            "Reloading replayed a handoff or captured table input.");
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Console.WriteLine("Public handoff: reserved slot, delayed response, single arrived face, replaced row, expiry, reset, motion, root and unload boundaries passed.");

        ContentControl Play(long id, CardKind kind, string actor, string action, double x)
        {
            var control = new ContentControl
            {
                Content = new TablePlayViewModel(id, CardCatalog.Get(kind).DisplayName, actor) { Kind = kind, ActionLabel = action },
                ContentTemplate = (DataTemplate)Application.Current.FindResource("TablePlayTemplate")
            };
            Canvas.SetLeft(control, x); Canvas.SetTop(control, 195); root.Children.Insert(0, control); return control;
        }
        void Waiting()
        {
            cues.Clear();
            // Recreate the public item so the actual template binding supplies the new sequence.
            slash.Content = new TablePlayViewModel(++sequence, "杀", "关羽") { Kind = CardKind.Slash, ActionLabel = "使用" };
            Capture(); slashFace = Face(slash);
            cues.Add(new(sequence, BattleCueKind.Card, 0, [1], "杀", "关羽") { CardKind = CardKind.Slash });
            clock.Advance(.01); Capture(); Pending(slashFace);
        }
        void Capture(string? name = null)
        {
            layer.InvalidateVisual(); root.Measure(new Size(780, 440)); root.Arrange(new Rect(0, 0, 780, 440)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(780, 440, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            if (name is null) return;
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"card-handoff-{name}.png")); encoder.Save(file);
        }
    }

    private static Border Face(DependencyObject root) => Find<Border>(root).Single(border => BattleFeedbackLayer.GetPlayAnchor(border) >= 0);
    private static FrameworkElement Panel(Border face) => (FrameworkElement)face.Parent;
    private static void Pending(Border face) => Assert(BattleFeedbackLayer.GetIsFlightPending(face) && Panel(face).Opacity < .001 &&
        face.ActualWidth == 86 && face.ActualHeight == 120, "A public face/caption appeared early or lost its reserved layout.");
    private static void Revealed(Border face) => Assert(!BattleFeedbackLayer.GetIsFlightPending(face) && Panel(face).Opacity > .99,
        "A completed or retired flight failed to restore its public face and caption.");
    private static IEnumerable<ImageSource> Faces(DependencyObject layer) => Images(VisualTreeHelper.GetDrawing((Visual)layer)).Select(image => image.ImageSource);
    private static IEnumerable<ImageDrawing> Images(Drawing? drawing)
    {
        if (drawing is ImageDrawing image) yield return image;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children) foreach (var nested in Images(child)) yield return nested;
    }
    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
