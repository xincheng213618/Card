using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class HandPointerChecks
{
    public static void OverlapAndPointer(string output)
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore());
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        var window = new CardGame.Wpf.MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        var revision = Program.Engine(vm).Revision;
        vm.Hand.Clear();
        for (var index = 0; index < 14; index++) vm.Hand.Add(new CardViewModel
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
        vm.Hand.Clear();
        root.UpdateLayout();
        Program.Assert(resolver.Invoke(panel, [scrolledPoint]) is null &&
                       press.Invoke(panel, [scrolledPoint]) is false,
            "An empty hand retained an old pointer target.");
        Program.Assert(Program.Engine(vm).Revision == revision, "Pointer inspection changed the match.");
        window.Content = null;
        window.Close();
    }
}
