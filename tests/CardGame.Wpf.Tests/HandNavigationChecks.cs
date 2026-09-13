using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class HandNavigationChecks
{
    public static void OverflowAndSelection(string output)
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore()) { IsMotionEnabled = false };
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        var revision = Program.Engine(vm).Revision;
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        var viewport = (HandScrollViewer)window.FindName("HandViewport");
        var hand = (ItemsControl)window.FindName("HandCards");
        // Stress the real hand template with a presentation-only fixture; no fabricated Core state.
        vm.Hand.Clear();
        for (var index = 0; index < 30; index++)
            vm.Hand.Add(new CardViewModel
            {
                Id = 10000 + index,
                Name = index % 2 == 0 ? "杀" : "闪",
                KindLabel = "基本牌",
                SuitGlyph = "♥",
                Rank = (index + 1).ToString(),
                Description = "手牌浏览布局检查",
                IsPlayable = true
            });
        Program.Render(root, 1120, 740, Path.Combine(output, "41-large-hand-start.png"));
        Program.Assert(viewport.ScrollableWidth > 100, "Dense hand did not produce an overflow viewport.");
        var initial = viewport.HorizontalOffset;
        Program.Assert(Wheel(viewport, -120), "Wheel did not claim horizontal hand browsing.");
        root.UpdateLayout();
        Program.Assert(viewport.HorizontalOffset > initial && vm.Hand.All(card => !card.IsSelected),
            "Wheel did not move right or accidentally selected a card.");
        var right = viewport.HorizontalOffset;
        Program.Assert(Wheel(viewport, 120), "Reverse wheel was not handled.");
        root.UpdateLayout();
        Program.Assert(viewport.HorizontalOffset < right, "Reverse wheel did not move left.");

        vm.Hand[^1].IsSelected = true;
        Settle(root);
        AssertVisible(hand, viewport, vm.Hand[^1]);
        Program.Assert(viewport.HorizontalOffset > 0, "Selecting the final card did not reveal it.");
        Program.Render(root, 1120, 740, Path.Combine(output, "41-large-hand-selected.png"));
        var selectedOffset = viewport.HorizontalOffset;
        vm.Hand[^1].IsSelected = false;
        Settle(root);
        Program.Assert(Math.Abs(viewport.HorizontalOffset - selectedOffset) < 1, "Deselecting unexpectedly moved the viewport.");
        vm.Hand[0].IsSelected = true;
        Settle(root);
        AssertVisible(hand, viewport, vm.Hand[0]);
        Program.Assert(viewport.HorizontalOffset < selectedOffset, "Selecting the first card did not return to it.");

        // A queued selection must not bring a card back after cancellation or removal.
        vm.Hand[^1].IsSelected = true;
        vm.Hand[^1].IsSelected = false;
        Settle(root);
        AssertVisible(hand, viewport, vm.Hand[0]);
        vm.Hand[^1].IsSelected = true;
        vm.Hand.RemoveAt(vm.Hand.Count - 1);
        Settle(root);
        AssertVisible(hand, viewport, vm.Hand[0]);

        while (vm.Hand.Count > 4) vm.Hand.RemoveAt(vm.Hand.Count - 1);
        Program.Render(root, 1120, 740, Path.Combine(output, "41-small-hand-restored.png"));
        Settle(root);
        Program.Assert(viewport.ScrollableWidth == 0 && viewport.HorizontalOffset == 0 && !Wheel(viewport, -120),
            "A small hand retained stale scrolling or swallowed the wheel.");
        Program.Assert(Program.Engine(vm).Revision == revision, "Hand browsing submitted a game command.");
        window.Content = null;
        window.Close();
    }

    private static bool Wheel(UIElement viewport, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = Mouse.PreviewMouseWheelEvent,
            Source = viewport
        };
        viewport.RaiseEvent(args);
        return args.Handled;
    }

    private static void Settle(FrameworkElement root)
    {
        root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        root.UpdateLayout();
    }

    private static void AssertVisible(ItemsControl hand, HandScrollViewer viewport, CardViewModel card)
    {
        var container = (FrameworkElement)hand.ItemContainerGenerator.ContainerFromItem(card);
        var bounds = container.TransformToAncestor(viewport).TransformBounds(new Rect(container.RenderSize));
        Program.Assert(bounds.Left >= -1 && bounds.Right <= viewport.ViewportWidth + 1,
            $"Selected card {card.Rank} is clipped: {bounds.Left:F1}..{bounds.Right:F1} in {viewport.ViewportWidth:F1}.");
    }
}
