using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class NativeResponseStatusChecks
{

    private static readonly Color ResponseCyan = Color.FromRgb(116, 231, 255);
    private static IEnumerable<ImageDrawing> Images(Drawing? drawing)
    {
        if (drawing is ImageDrawing image) yield return image;
        if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var nested in Images(child)) yield return nested;
    }
    private static IEnumerable<(GeometryDrawing Drawing, Rect Bounds, double Opacity)> Shapes(Drawing? drawing, Matrix transform, double opacity)
    {
        if (drawing is GeometryDrawing shape)
            yield return (shape, new MatrixTransform(transform).TransformBounds(shape.Geometry.Bounds), opacity);
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            foreach (var child in group.Children)
                foreach (var nested in Shapes(child, next, opacity * group.Opacity)) yield return nested;
        }
    }

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToVisual(ancestor).TransformBounds(new Rect(element.RenderSize));
    private sealed record MatchBoundary(long Revision, int Commands, int Events, int Movements, string[] Views);

    internal sealed class NativeHost : IDisposable
    {
        public MainViewModel Model { get; }
        public MainWindow Window { get; }
        public FrameworkElement Root { get; }
        public BattleFeedbackLayer Layer { get; }
        public FrameClock Clock { get; } = new();
        private readonly nint _handle;
        private const int ExtendedStyle = -20;
        private const int NoActivate = 0x08000000;
        public NativeHost(MainViewModel model)
        {
            Model = model; model.IsSoundEnabled = false; model.IsMusicEnabled = false;
            Window = new MainWindow(model)
            {
                ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -30000, Top = -30000, Width = 1120, Height = 820
            };
            Root = (FrameworkElement)Window.Content;
            Layer = (BattleFeedbackLayer)Window.FindName("BattleFeedback"); Layer.Clock = Clock;
            _handle = new WindowInteropHelper(Window).EnsureHandle();
            // ShowActivated alone does not prevent later native activation.
            // Match the existing keyboard fixtures on this owned HWND before
            // loading its real WPF tree; logical focus remains testable.
            SetWindowLong(_handle, ExtendedStyle, GetWindowLong(_handle, ExtendedStyle) | NoActivate);
            Assert((GetWindowLong(_handle, ExtendedStyle) & NoActivate) != 0,
                "The shared hidden native fixture could not disable activation on its owned HWND.");
            var previousParent = SetParent(_handle, new nint(-3));
            Assert(previousParent != 0 || Marshal.GetLastPInvokeError() == 0,
                "The native response fixture could not attach its owned message-only HWND.");
            Window.Show(); ShowWindow(_handle, 0);
            Window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert(Window.IsLoaded && Window.IsVisible && !IsWindowVisible(_handle) && GetForegroundWindow() != _handle &&
                (GetWindowLong(_handle, ExtendedStyle) & NoActivate) != 0,
                $"The actual WPF response fixture must remain loaded without becoming a visible or foreground desktop window: " +
                $"loaded={Window.IsLoaded}, WPF visible={Window.IsVisible}, native visible={IsWindowVisible(_handle)}, " +
                $"owns foreground={GetForegroundWindow() == _handle}, no activate={(GetWindowLong(_handle, ExtendedStyle) & NoActivate) != 0}.");
        }
        public void Capture(string output, string label, int width)
        {
            var height = width == 1120 ? 740 : 1080;
            var outputSize = new Size(width, height);
            // A loaded Window can arrange its content again at Render priority.
            // Pin the real root rather than relying on Render's earlier Arrange;
            // SizeToContent also gives the owned host room for that exact root.
            Root.Width = width; Root.Height = height;
            Window.SizeToContent = SizeToContent.WidthAndHeight;
            Window.UpdateLayout();
            Render(Root, width, height, Path.Combine(output, label + $"-{width}.png"));
            var diagnostic = $"{label}: output={outputSize}, actual root={Root.RenderSize}, " +
                $"actual window={Window.ActualWidth:F2}x{Window.ActualHeight:F2}";
            Console.WriteLine("Native response output measurement: " + diagnostic + ".");
            Assert(Root.RenderSize == outputSize,
                "The loaded native parent replaced the exact PNG layout size: " + diagnostic);
            var outputBounds = new Rect(outputSize);
            foreach (var element in Find<FrameworkElement>(Root).Where(element => element.IsVisible &&
                element.ActualWidth > 0 && element.ActualHeight > 0 &&
                (element.Name is "ResponseStatusBanner" or "DecisionDescriptionText" or "HandViewport" or "PrimaryActionButtons" ||
                 element is Button button && BattleFeedbackLayer.GetSeatAnchor(button) >= 0)))
                Assert(outputBounds.Contains(Bounds(element, Root)),
                    $"The actual PNG clips native {element.Name}, bounds={Bounds(element, Root)}; " + diagnostic);
            Assert(Window.IsLoaded && Window.IsVisible && Root.IsVisible && !IsWindowVisible(_handle) && GetForegroundWindow() != _handle &&
                (GetWindowLong(_handle, ExtendedStyle) & NoActivate) != 0,
                "Rendering native response status activated or exposed the owned test window.");
        }
        public RenderTargetBitmap MovieFrame()
        {
            Root.UpdateLayout(); Root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            Assert(Root.RenderSize == new Size(1120, 740), "The native movie must keep its exact compact layout.");
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.PushTransform(new ScaleTransform(.65, .65));
                context.DrawRectangle(new VisualBrush(Root), null, new Rect(Root.RenderSize));
                context.Pop();
            }
            var bitmap = new RenderTargetBitmap(728, 481, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }
        public void Dispose() { Window.Content = null; Window.DataContext = null; Window.Close(); }
    }
    internal sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
    private sealed class BindingListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (message is not null && (message.Contains("Error:") || message.Contains("Warning:"))) Errors.Add(message);
        }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint handle, int index, int value);
}
