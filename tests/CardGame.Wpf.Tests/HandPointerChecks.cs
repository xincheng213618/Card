using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class HandPointerChecks
{

    public static void OverlapAndPointer(string output)
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore());
        Program.StartLordFixture(vm);
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        var window = new CardGame.Wpf.MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        var revision = Program.Engine(vm).Revision;
        vm.Hand.Clear();
        for (var index = 0; index < 22; index++) vm.Hand.Add(new CardViewModel
        {
            Id = 9000 + index, Kind = CardKind.Slash, Name = "杀", KindLabel = "基本牌",
            SuitGlyph = "♠", Rank = (index % 9 + 1).ToString(), Description = "手牌重叠布局检查",
            IsPlayable = true
        });
        vm.Hand[2].IsSelected = true;
        Program.Render(root, 1120, 740, Path.Combine(output, "hand-pointer-before.png"));
        var items = (ItemsControl)window.FindName("HandCards");
        var panel = Program.Find<HandPanel>(items).Single();
        var next = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(3);
        var point = next.TranslatePoint(new Point(8, 80), panel);
        var move = typeof(HandPanel).GetMethod("UpdatePointer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        move.Invoke(panel, [point]);
        root.UpdateLayout();
        var hit = VisualTreeHelper.HitTest(panel, point)?.VisualHit;
        while (hit is not null && hit is not Button) hit = VisualTreeHelper.GetParent(hit);
        var card = (hit as FrameworkElement)?.DataContext as CardViewModel;
        Console.WriteLine($"Overlap pointer: intended={vm.Hand[3].Id}, visual hit={card?.Id}");
        Program.Assert(card?.Id == vm.Hand[3].Id, "An elevated selected card intercepted the adjacent card's pointer region.");
        var press = typeof(HandPanel).GetMethod("PressCardAt", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Program.Assert(press.Invoke(panel, [point]) is true && vm.Hand[3].IsSelected && !vm.Hand[2].IsSelected,
            "A press selected the elevated neighbor instead of the pointer's physical card.");
        var resolver = typeof(HandPanel).GetMethod("CardButtonAt", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var button = (Button)resolver.Invoke(panel, [point])!;
        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        Program.Assert(vm.Hand[3].IsSelected,
            "Mouse release selected the same card again after the press had already selected it.");
        for (var repeat = 0; repeat < 5; repeat++)
        {
            move.Invoke(panel, [point]);
            root.UpdateLayout();
            Program.Assert(ReferenceEquals(button, resolver.Invoke(panel, [point])),
                "Lifting a hovered card changed the pointer's slot.");
        }
        vm.ClearSelectionCommand.Execute(null);
        vm.Hand[3].IsPlayable = false;
        root.UpdateLayout();
        Program.Assert(press.Invoke(panel, [point]) is true && !vm.HasSelection,
            "An unavailable card selected itself or allowed the press to reach a covered card.");
        Program.Assert(press.Invoke(panel, [new Point(-10, 80)]) is false && !vm.HasSelection,
            "An empty hand margin selected a card.");
        vm.Hand[3].IsPlayable = true;
        press.Invoke(panel, [point]);
        Program.Render(root, 1120, 740, Path.Combine(output, "hand-pointer-after.png"));
        var viewport = (HandScrollViewer)window.FindName("HandViewport");
        var presenter = Program.Find<ScrollContentPresenter>(viewport).Single();
        var first = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(0);
        var face = Program.Find<Grid>(first).Single(element => element.Name == "CardSurface");
        var faceBounds = face.TransformToAncestor(presenter).TransformBounds(new Rect(face.RenderSize));
        Console.WriteLine($"Hand viewport: face bottom={faceBounds.Bottom:F1}, visible height={presenter.ActualHeight:F1}");
        Program.Assert(faceBounds.Bottom <= presenter.ActualHeight + .5,
            "The horizontal scrollbar clips the lower edge of unselected hand cards.");
        vm.ClearSelectionCommand.Execute(null);
        viewport.ScrollToLeftEnd();
        root.UpdateLayout();
        var edgePoint = new Point(presenter.ActualWidth - 8, 80);
        var edgeButton = (Button)resolver.Invoke(panel, [presenter.TranslatePoint(edgePoint, panel)])!;
        var edgeCard = (CardViewModel)edgeButton.DataContext;
        var offset = viewport.HorizontalOffset;
        Program.Assert(press.Invoke(panel, [presenter.TranslatePoint(edgePoint, panel)]) is true && edgeCard.IsSelected,
            "The visible right-edge strip must select its physical card.");
        edgeButton.BringIntoView(); // Focus also requests visibility through this routed event.
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        root.UpdateLayout();
        var afterEdge = (Button)resolver.Invoke(panel, [presenter.TranslatePoint(edgePoint, panel)])!;
        Console.WriteLine($"Hand edge selection: offset {offset:F1} -> {viewport.HorizontalOffset:F1}; " +
                          $"intended={edgeCard.Id}, next press={((CardViewModel)afterEdge.DataContext).Id}");
        Program.Assert(viewport.HorizontalOffset == offset && ReferenceEquals(edgeButton, afterEdge),
            "Auto-revealing a mouse-selected edge card moved the next press onto a different physical card.");
        vm.ClearSelectionCommand.Execute(null);
        move.Invoke(panel, [new Point(-10, 80)]);
        root.UpdateLayout();
        vm.SelectCardCommand.Execute(vm.Hand.Last());
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        root.UpdateLayout();
        var keyboardTail = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(vm.Hand.Count - 1);
        var keyboardBounds = keyboardTail.TransformToAncestor(presenter).TransformBounds(new Rect(keyboardTail.RenderSize));
        Program.Assert(viewport.HorizontalOffset > 0 && keyboardBounds.Left >= -.5 &&
                       keyboardBounds.Right <= presenter.ActualWidth + .5,
            "Keyboard selection stopped revealing an offscreen card in full.");
        vm.ClearSelectionCommand.Execute(null);
        viewport.ScrollToRightEnd();
        root.UpdateLayout();
        Program.Assert(viewport.HorizontalOffset > 0, "The overlap fixture did not exercise horizontal scrolling.");
        var tail = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(vm.Hand.Count - 1);
        var screenPoint = tail.TranslatePoint(new Point(50, 80), viewport);
        var scrolledPoint = viewport.TranslatePoint(screenPoint, panel);
        move.Invoke(panel, [scrolledPoint]);
        root.UpdateLayout();
        Program.Assert(((Button)resolver.Invoke(panel, [scrolledPoint])!).DataContext == vm.Hand.Last() &&
                       press.Invoke(panel, [scrolledPoint]) is true && vm.Hand.Last().IsSelected,
            "Scrolling changed the physical card resolved beneath the pointer.");
        try
        {
            VerifySortedPointerAndKeyboard(vm, window, output);
            vm.Hand.Clear();
            root.UpdateLayout();
            Program.Assert(resolver.Invoke(panel, [scrolledPoint]) is null &&
                           press.Invoke(panel, [scrolledPoint]) is false,
                "An empty hand retained an old pointer target.");
            Program.Assert(Program.Engine(vm).Revision == revision, "Pointer inspection changed the match.");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void VerifySortedPointerAndKeyboard(MainViewModel vm, CardGame.Wpf.MainWindow window, string output)
    {
        vm.IsMusicEnabled = false;
        vm.IsSoundEnabled = false;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        window.Top = -30000;
        window.Width = 1120;
        window.Height = 820;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        const int extendedStyle = -20;
        const int noActivate = 0x08000000;
        SetWindowLong(handle, extendedStyle, GetWindowLong(handle, extendedStyle) | noActivate);
        Program.Assert((GetWindowLong(handle, extendedStyle) & noActivate) != 0,
            "The hand keyboard fixture could not disable native activation on its owned HWND.");
        var previousParent = SetParent(handle, new nint(-3));
        Program.Assert(previousParent != 0 || Marshal.GetLastPInvokeError() == 0,
            "The hand keyboard fixture could not attach its owned message-only HWND.");
        window.Show();
        ShowWindow(handle, 0);
        SetFocus(handle);
        Pump();
        Program.Assert(window.IsLoaded && window.IsVisible && !IsWindowVisible(handle) && GetForegroundWindow() != handle,
            $"The hand keyboard fixture must load its hidden Window: loaded={window.IsLoaded}, " +
            $"WPF visible={window.IsVisible}, native visible={IsWindowVisible(handle)}, owns foreground={GetForegroundWindow() == handle}.");

        var items = (ItemsControl)window.FindName("HandCards");
        var viewport = (HandScrollViewer)window.FindName("HandViewport");
        var presenter = Program.Find<ScrollContentPresenter>(viewport).Single();
        var panel = Program.Find<HandPanel>(items).Single();
        var move = typeof(HandPanel).GetMethod("UpdatePointer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var card = vm.Hand.First(item => item.Rank == "9");
        var beforeIndex = vm.Hand.IndexOf(card);
        vm.ClearSelectionCommand.Execute(null);
        viewport.ScrollToLeftEnd();
        Pump();
        var beforeContainer = (FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(card);
        var point = beforeContainer.TranslatePoint(new Point(8, 80), panel);
        var beforeBounds = beforeContainer.TransformToAncestor(presenter).TransformBounds(new Rect(beforeContainer.RenderSize));
        Program.Assert(beforeBounds.Left >= 0 && beforeBounds.Right <= presenter.ActualWidth,
            "The sorting fixture must begin with its rank-nine card fully visible.");
        move.Invoke(panel, [point]);
        var beforeButton = Program.Find<Button>(beforeContainer).Single();
        Program.Assert(HandPanel.GetIsPointerCard(beforeButton) && ReferenceEquals(beforeButton.CommandParameter, card),
            "The sorting fixture must begin with its actual published button under the pointer.");

        vm.SortHandCommand.Execute(null);
        Pump();
        var sortedIndex = vm.Hand.IndexOf(card);
        var container = (FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(card);
        var button = Program.Find<Button>(container).Single();
        var sortedBounds = container.TransformToAncestor(presenter).TransformBounds(new Rect(container.RenderSize));
        Program.Assert(sortedIndex > beforeIndex && sortedBounds.Left >= presenter.ActualWidth &&
                       viewport.HorizontalOffset == 0 && ReferenceEquals(button.CommandParameter, card),
            "The real hand-sort command must move the same published card beyond the viewport before keyboard activation.");
        Console.WriteLine($"Hand sort pointer: card={card.Id}, index {beforeIndex}->{sortedIndex}, " +
                          $"same button={ReferenceEquals(beforeButton, button)}, pointer={HandPanel.GetIsPointerCard(button)}.");
        panel.PreviewMouseMove += LogPointerEvent;
        panel.MouseLeave += LogPointerEvent;
        try
        {
            Program.Assert(button.Focus() && ReferenceEquals(Keyboard.FocusedElement, button),
                "The sorted hand card must receive actual keyboard focus in the loaded Window.");
            LogPointerState("focused");
            WaitForIdleInput("space-down");
            RaiseKey(Keyboard.PreviewKeyDownEvent, Keyboard.KeyDownEvent);
            LogPointerState("space-down");
            WaitForIdleInput("space-up");
            RaiseKey(Keyboard.PreviewKeyUpEvent, Keyboard.KeyUpEvent);
            LogPointerState("space-up");
            Pump();
            LogPointerState("pumped");
        }
        finally
        {
            panel.PreviewMouseMove -= LogPointerEvent;
            panel.MouseLeave -= LogPointerEvent;
            if (ReferenceEquals(Mouse.Captured, button)) button.ReleaseMouseCapture();
        }
        Program.Assert(card.IsSelected && vm.Hand.Count(item => item.IsSelected) == 1,
            "Space must activate the actual sorted hand button exactly once.");
        var root = (FrameworkElement)window.Content;
        Program.Render(root, (int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight),
            Path.Combine(output, "hand-sort-keyboard-after.png"));
        var keyboardBounds = container.TransformToAncestor(presenter).TransformBounds(new Rect(container.RenderSize));
        Console.WriteLine($"Hand sort keyboard: offset={viewport.HorizontalOffset:F1}, " +
                          $"bounds={keyboardBounds.Left:F1}..{keyboardBounds.Right:F1}, viewport={presenter.ActualWidth:F1}.");
        Program.Assert(viewport.HorizontalOffset > 0 && keyboardBounds.Left >= -.5 &&
                       keyboardBounds.Right <= presenter.ActualWidth + .5,
            "Sorting retained a pointer marker on an offscreen card and blocked its subsequent keyboard reveal.");

        void Pump()
        {
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.UpdateLayout();
        }

        void LogPointerState(string stage, bool? handled = null) => Console.WriteLine($"Hand sort {stage}: " +
            $"pointer={HandPanel.GetIsPointerCard(button)}, capture={button.IsMouseCaptured}, " +
            $"hand mouse-over={panel.IsMouseOver}, offset={viewport.HorizontalOffset:F1}, " +
            $"pressed={button.IsPressed}, left={Mouse.LeftButton}, {NativeLeftState()}, modifiers={Keyboard.Modifiers}, " +
            $"owns focus={ReferenceEquals(Keyboard.FocusedElement, button)}, handled={handled?.ToString() ?? "n/a"}.");

        void WaitForIdleInput(string stage)
        {
            LogPointerState($"{stage}-idle-before");
            var elapsed = Stopwatch.StartNew();
            while (!IsIdleInput() && elapsed.Elapsed < TimeSpan.FromSeconds(2))
            {
                Pump();
                if (!IsIdleInput() && elapsed.Elapsed < TimeSpan.FromSeconds(2)) Thread.Sleep(10);
            }
            LogPointerState($"{stage}-idle-after ({elapsed.ElapsedMilliseconds} ms)");
            Program.Assert(IsIdleInput(),
                $"The synthetic {stage} requires an idle input device within two seconds: " +
                $"left={Mouse.LeftButton}, {NativeLeftState()}, modifiers={Keyboard.Modifiers}, pressed={button.IsPressed}, " +
                $"owns focus={ReferenceEquals(Keyboard.FocusedElement, button)}, capture={button.IsMouseCaptured}.");
        }

        static bool IsIdleInput() => Mouse.LeftButton == MouseButtonState.Released &&
            (GetAsyncKeyState(0x01) & 0x8000) == 0 && Keyboard.Modifiers == ModifierKeys.None;

        static string NativeLeftState()
        {
            var queued = GetKeyState(0x01);
            var physical = GetAsyncKeyState(0x01);
            return $"key-state-left={((queued & 0x8000) == 0 ? "Released" : "Pressed")}({(ushort)queued:X4}), " +
                $"async-left={((physical & 0x8000) == 0 ? "Released" : "Pressed")}({(ushort)physical:X4})";
        }

        void LogPointerEvent(object sender, MouseEventArgs args) => Console.WriteLine(
            $"Hand sort {args.RoutedEvent.Name}: position={args.GetPosition(panel)}, " +
            $"pointer={HandPanel.GetIsPointerCard(button)}, capture={button.IsMouseCaptured}.");

        void RaiseKey(RoutedEvent preview, RoutedEvent bubble)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(button)!, 0, Key.Space)
                { RoutedEvent = preview };
            LogPointerState($"{preview.Name}-before", args.Handled);
            button.RaiseEvent(args);
            LogPointerState($"{preview.Name}-after", args.Handled);
            if (args.Handled) return;
            args.RoutedEvent = bubble;
            button.RaiseEvent(args);
            LogPointerState($"{bubble.Name}-after", args.Handled);
        }
    }

    private sealed class ReflowClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint parent);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint handle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint handle, int index, int value);
}
