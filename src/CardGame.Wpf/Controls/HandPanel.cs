using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Controls;

/// <summary>Keep the hand contiguous from the equipment rail; overlap only when space runs out.</summary>
public sealed class HandPanel : Panel
{
    private const double CardWidth = 112;
    private const double CardHeight = 178;
    private const double MinimumStep = 36;
    private double _step;
    private UIElement? _hoveredCard;
    private Point? _pointerPosition;
    private Dictionary<UIElement, HandSlot> _slots = new();
    private List<UIElement> _order = new();
    private double _layoutWidth;
    private bool _arranging;
    private bool _updatingPointer;
    private bool _panelLoaded;
    private bool _hasHandBaseline;
    private bool _awaitingSnapshotAfterReset;
    private INotifyCollectionChanged? _items;
    private readonly HashSet<int> _pendingArrivalIds = new();
    private TimeProvider _clock = TimeProvider.System;

    private sealed record HandSlot(double X, object? Identity, CardLiftSurface? Surface);

    public HandPanel()
    {
        // Focusing a clicked edge card must keep the same physical slot beneath
        // the pointer. Keyboard navigation can still reveal other cards normally.
        RequestBringIntoView += (_, args) =>
        {
            if (args.TargetObject is { } target && GetIsPointerCard(target)) args.Handled = true;
        };
        AddHandler(CardLiftSurface.LiftChangedEvent, new RoutedEventHandler((_, _) => UpdateHoveredCard()));
        Loaded += (_, _) => { _panelLoaded = true; ObserveItems(); };
        Unloaded += (_, _) =>
        {
            _panelLoaded = false;
            if (_items is not null) _items.CollectionChanged -= OnItemsChanged;
            _items = null;
            ResetArrivalBaseline(false);
            ClearPointer();
            _slots.Clear();
            _order.Clear();
        };
    }

    public static readonly DependencyProperty IsPointerCardProperty = DependencyProperty.RegisterAttached(
        "IsPointerCard", typeof(bool), typeof(HandPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsPointerCard(DependencyObject element) => (bool)element.GetValue(IsPointerCardProperty);
    public static void SetIsPointerCard(DependencyObject element, bool value) => element.SetValue(IsPointerCardProperty, value);

    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        nameof(ViewportWidth), typeof(double), typeof(HandPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ViewportWidth
    {
        get => (double)GetValue(ViewportWidthProperty);
        set => SetValue(ViewportWidthProperty, value);
    }

    public static readonly DependencyProperty HandSnapshotProperty = DependencyProperty.Register(
        nameof(HandSnapshot), typeof(GameSnapshot), typeof(HandPanel),
        new PropertyMetadata(null, OnHandSnapshotChanged));

    public GameSnapshot? HandSnapshot
    {
        get => (GameSnapshot?)GetValue(HandSnapshotProperty);
        set => SetValue(HandSnapshotProperty, value);
    }

    public static readonly DependencyProperty ArrivalEnabledProperty = DependencyProperty.Register(
        nameof(ArrivalEnabled), typeof(bool), typeof(HandPanel),
        new PropertyMetadata(false, OnArrivalEnabledChanged));

    public bool ArrivalEnabled
    {
        get => (bool)GetValue(ArrivalEnabledProperty);
        set => SetValue(ArrivalEnabledProperty, value);
    }

    public TimeProvider Clock
    {
        get => _clock;
        set { ArgumentNullException.ThrowIfNull(value); _clock = value; }
    }

    private static void OnArrivalEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if ((bool)args.NewValue) return;
        var panel = (HandPanel)sender;
        panel._pendingArrivalIds.Clear();
        panel.FinishArrivals();
    }

    private static void OnHandSnapshotChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var panel = (HandPanel)sender;
        var previous = (GameSnapshot?)args.OldValue;
        var current = (GameSnapshot?)args.NewValue;
        if (panel._awaitingSnapshotAfterReset)
        {
            panel._awaitingSnapshotAfterReset = false;
            panel.ResetArrivalBaseline(false);
            return;
        }
        if (previous is null || current is null || previous.HumanSeat != current.HumanSeat || current.Revision < previous.Revision)
        {
            panel.ResetArrivalBaseline(false);
            return;
        }
        var currentIds = OwnHandIds(current);
        panel._pendingArrivalIds.IntersectWith(currentIds);
        if (!panel._panelLoaded || !panel._hasHandBaseline || !panel.ArrivalEnabled) return;
        if (current.Revision > previous.Revision)
        {
            currentIds.ExceptWith(OwnHandIds(previous));
            panel._pendingArrivalIds.UnionWith(currentIds);
        }
        // Snapshot notification precedes collection updates. Keep these IDs
        // through an early layout until their actual local surfaces arrive.
        panel.InvalidateArrange();
    }

    private static HashSet<int> OwnHandIds(GameSnapshot snapshot)
    {
        var owner = snapshot.Players.FirstOrDefault(player => player.Seat == snapshot.HumanSeat);
        return owner is null ? [] : owner.Hand.Concat(owner.WoodenOxGrain ?? []).Select(card => card.Id).Where(id => id > 0).ToHashSet();
    }

    private void ObserveItems()
    {
        var items = ItemsControl.GetItemsOwner(this)?.Items as INotifyCollectionChanged;
        if (ReferenceEquals(_items, items)) return;
        if (_items is not null) _items.CollectionChanged -= OnItemsChanged;
        _items = items;
        if (_items is not null) _items.CollectionChanged += OnItemsChanged;
        ResetArrivalBaseline(false);
        _slots.Clear();
        _order.Clear();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action != NotifyCollectionChangedAction.Reset) return;
        // ReplaceEngine clears Hand before publishing the new view, including
        // same-revision checkpoint loads and tutorial restarts.
        ResetArrivalBaseline(true);
        _slots.Clear();
        _order.Clear();
    }

    private void ResetArrivalBaseline(bool awaitingSnapshot)
    {
        _hasHandBaseline = false;
        _awaitingSnapshotAfterReset = awaitingSnapshot;
        _pendingArrivalIds.Clear();
        FinishArrivals();
        InvalidateArrange();
    }

    private void FinishArrivals()
    {
        // Property bindings can run before ItemsPresenter applies its template.
        // Inspect only materialized visuals; InternalChildren would generate
        // items early and make WPF reject the supposedly empty panel template.
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(this); index++)
            FindSurface(VisualTreeHelper.GetChild(this, index))?.FinishArrival();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        ObserveItems();
        foreach (UIElement child in InternalChildren) child.Measure(new Size(CardWidth, CardHeight));
        var minimum = InternalChildren.Count == 0 ? 0 : CardWidth + (InternalChildren.Count - 1) * MinimumStep;
        var width = ViewportWidth > 0 ? ViewportWidth : double.IsInfinity(availableSize.Width) ? minimum : availableSize.Width;
        return new Size(Math.Max(minimum, width), CardHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Count;
        var layoutWidth = ViewportWidth > 0 ? ViewportWidth : finalSize.Width;
        if (count == 0)
        {
            _step = 0;
            _slots.Clear();
            _order.Clear();
            _layoutWidth = layoutWidth;
            _hasHandBaseline = !_awaitingSnapshotAfterReset;
            ClearPointer();
            return finalSize;
        }
        _step = count == 1 ? CardWidth : Math.Clamp((finalSize.Width - CardWidth) / (count - 1), MinimumStep, CardWidth);
        var order = InternalChildren.Cast<UIElement>().ToList();
        var present = order.ToHashSet();
        var identitiesMatch = order.All(child => !_slots.TryGetValue(child, out var old) ||
            ReferenceEquals(old.Identity, (child as FrameworkElement)?.DataContext));
        // Overflow content shrinks when a card leaves; the viewport remains
        // the resize boundary even when the panel's minimum width changes.
        var sameWidth = Math.Abs(_layoutWidth - layoutWidth) < .01;
        var sameOrder = _order.SequenceEqual(order);
        var survivors = order.Where(_slots.ContainsKey).ToArray();
        var survivingOrderMatches = _order.Where(present.Contains).SequenceEqual(survivors);
        var reflow = _order.Count != count && survivors.Length > 0 && sameWidth && identitiesMatch && survivingOrderMatches;
        var preserveReflow = sameWidth && sameOrder && identitiesMatch;
        var allowArrival = _panelLoaded && _hasHandBaseline && !_awaitingSnapshotAfterReset && ArrivalEnabled &&
            sameWidth && survivingOrderMatches;
        var preserveArrival = sameWidth && survivingOrderMatches && identitiesMatch;
        if (_hasHandBaseline && (!sameWidth || !survivingOrderMatches)) _pendingArrivalIds.Clear();
        var arrivalOrdinal = 0;
        var slots = new Dictionary<UIElement, HandSlot>(count);
        _arranging = true;
        try
        {
            for (var index = 0; index < count; index++)
            {
                var child = order[index];
                _slots.TryGetValue(child, out var old);
                var previousX = old is null ? 0 : old.X + (old.Surface?.HorizontalOffset ?? 0);
                var x = index * _step;
                child.Arrange(new Rect(x, 0, CardWidth, CardHeight));
                var surface = FindSurface(child);
                if (surface is not null)
                {
                    // Inject the panel's frame clock before a newly generated
                    // card starts moving, including surfaces created by Arrange.
                    if (!ReferenceEquals(Clock, TimeProvider.System)) surface.Clock = Clock;
                    if (reflow && old is not null && ReferenceEquals(old.Surface, surface))
                        surface.ReflowFrom(previousX - x);
                    else if (!preserveReflow || old is null || !ReferenceEquals(old.Surface, surface))
                        surface.FinishReflow();
                    if (!preserveArrival) surface.FinishArrival();
                    if ((child as FrameworkElement)?.DataContext is CardViewModel card && _pendingArrivalIds.Remove(card.Id))
                    {
                        if (allowArrival) surface.BeginArrival(arrivalOrdinal++);
                        else surface.FinishArrival();
                    }
                }
                slots.Add(child, new HandSlot(x, (child as FrameworkElement)?.DataContext, surface));
            }
            _slots = slots;
            _order = order;
            _layoutWidth = layoutWidth;
            _hasHandBaseline = !_awaitingSnapshotAfterReset;
        }
        finally { _arranging = false; }
        // Sorting and resizing can move a different card beneath a stationary
        // pointer. Keep the lift and visibility suppression on that physical slot.
        if (IsMouseOver) _pointerPosition = Mouse.GetPosition(this);
        UpdateHoveredCard();
        return finalSize;
    }

    // Follow horizontal motion, while keeping the physical vertical hit strip
    // stable when artwork lifts over its neighbor.
    private UIElement? CardContainerAt(Point point)
    {
        var count = InternalChildren.Count;
        if (count == 0 || _step <= 0 || point.X < 0 || point.Y < 0 || point.Y > CardHeight) return null;
        for (var index = count - 1; index >= 0; index--)
        {
            var child = InternalChildren[index];
            var x = _slots.TryGetValue(child, out var slot)
                ? slot.X + (slot.Surface?.HorizontalOffset ?? 0) : index * _step;
            if (point.X >= x && point.X < x + CardWidth) return child;
        }
        return null;
    }

    internal Button? CardButtonAt(Point point) => FindButton(CardContainerAt(point));

    internal void UpdatePointer(Point point)
    {
        _pointerPosition = point;
        UpdateHoveredCard();
    }

    private void UpdateHoveredCard()
    {
        if (_arranging || _updatingPointer) return;
        _updatingPointer = true;
        try { SetHoveredCard(_pointerPosition is { } point ? CardContainerAt(point) : null); }
        finally { _updatingPointer = false; }
    }

    private void ClearPointer()
    {
        _pointerPosition = null;
        SetHoveredCard(null);
    }

    private void SetHoveredCard(UIElement? card)
    {
        if (ReferenceEquals(card, _hoveredCard)) return;
        if (_hoveredCard is not null) SetIsPointerCard(_hoveredCard, false);
        _hoveredCard = card;
        if (_hoveredCard is not null) SetIsPointerCard(_hoveredCard, true);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        UpdatePointer(e.GetPosition(this));
        base.OnPreviewMouseMove(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        ClearPointer();
        base.OnMouseLeave(e);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!e.Handled && PressCardAt(e.GetPosition(this))) e.Handled = true;
        base.OnPreviewMouseLeftButtonDown(e);
    }

    internal bool PressCardAt(Point point)
    {
        UpdatePointer(point);
        var button = CardButtonAt(point);
        if (button is null) return false;
        if (button.IsEnabled && button.Command is { } command)
        {
            button.Focus();
            if (command is RoutedCommand routed)
            {
                var target = button.CommandTarget ?? button;
                if (routed.CanExecute(button.CommandParameter, target)) routed.Execute(button.CommandParameter, target);
            }
            else if (command.CanExecute(button.CommandParameter)) command.Execute(button.CommandParameter);
        }
        return true;
    }

    private static Button? FindButton(DependencyObject? element)
    {
        if (element is null) return null;
        if (element is Button button) return button;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (FindButton(VisualTreeHelper.GetChild(element, index)) is { } found) return found;
        return null;
    }

    private static CardLiftSurface? FindSurface(DependencyObject element)
    {
        if (element is CardLiftSurface surface) return surface;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (FindSurface(VisualTreeHelper.GetChild(element, index)) is { } found) return found;
        return null;
    }
}
