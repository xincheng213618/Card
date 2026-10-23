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
    internal const double ArrivalEntryInset = 7;
    private double _step;
    private UIElement? _hoveredCard;
    private Point? _pointerPosition;
    private Dictionary<UIElement, HandSlot> _slots = new();
    private List<UIElement> _order = new();
    private double _layoutWidth;
    private double _layoutScale = 1;
    private bool _arranging;
    private bool _updatingPointer;
    private bool _panelLoaded;
    private bool _hasHandBaseline;
    private bool _awaitingSnapshotAfterReset;
    private INotifyCollectionChanged? _items;
    private readonly Dictionary<int, HandArrivalBatch> _pendingArrivalIds = new();
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

    public static readonly DependencyProperty CardScaleProperty = DependencyProperty.RegisterAttached(
        "CardScale", typeof(double), typeof(HandPanel),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure),
        value => value is double scale && double.IsFinite(scale) && scale > 0);

    public static double GetCardScale(DependencyObject element) => (double)element.GetValue(CardScaleProperty);
    public static void SetCardScale(DependencyObject element, double value) => element.SetValue(CardScaleProperty, value);
    public double CardScale { get => GetCardScale(this); set => SetCardScale(this, value); }

    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        nameof(ViewportWidth), typeof(double), typeof(HandPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ViewportWidth
    {
        get => (double)GetValue(ViewportWidthProperty);
        set => SetValue(ViewportWidthProperty, value);
    }

    public static readonly DependencyProperty PlaybackTimeScaleProperty = DependencyProperty.Register(
        nameof(PlaybackTimeScale), typeof(double), typeof(HandPanel),
        new PropertyMetadata(1d, OnPlaybackTimeScaleChanged),
        value => value is double scale && double.IsFinite(scale) && scale > 0);

    public double PlaybackTimeScale
    {
        get => (double)GetValue(PlaybackTimeScaleProperty);
        set => SetValue(PlaybackTimeScaleProperty, value);
    }

    private static void OnPlaybackTimeScaleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var panel = (HandPanel)sender;
        panel.ArrivalTimeline?.SetTimeScale((double)args.NewValue);
        foreach (var batch in panel._pendingArrivalIds.Values) batch.SetTimeScale((double)args.NewValue);
        foreach (UIElement child in panel.InternalChildren)
            FindSurface(child)?.SetArrivalTimeScale((double)args.NewValue);
    }

    public static readonly DependencyProperty HandSnapshotProperty = DependencyProperty.Register(
        nameof(HandSnapshot), typeof(GameSnapshot), typeof(HandPanel),
        new PropertyMetadata(null, OnHandSnapshotChanged));

    public GameSnapshot? HandSnapshot
    {
        get => (GameSnapshot?)GetValue(HandSnapshotProperty);
        set => SetValue(HandSnapshotProperty, value);
    }

    public static readonly DependencyProperty ArrivalTimelineProperty = DependencyProperty.Register(
        nameof(ArrivalTimeline), typeof(HandArrivalTimeline), typeof(HandPanel), new PropertyMetadata(null));

    public HandArrivalTimeline? ArrivalTimeline
    {
        get => (HandArrivalTimeline?)GetValue(ArrivalTimelineProperty);
        set => SetValue(ArrivalTimelineProperty, value);
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
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            foreach (var batch in _pendingArrivalIds.Values) batch.SetFallbackClock(value);
            _clock = value;
        }
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
        foreach (var id in panel._pendingArrivalIds.Keys.Where(id => !currentIds.Contains(id)).ToArray())
            panel._pendingArrivalIds.Remove(id);
        if (!panel._panelLoaded || !panel._hasHandBaseline || !panel.ArrivalEnabled) return;
        if (current.Revision > previous.Revision)
        {
            currentIds.ExceptWith(OwnHandIds(previous));
            if (currentIds.Count > 0)
            {
                var batch = panel.ArrivalTimeline?.Begin(previous.Revision, current.Revision, panel.Clock, panel.PlaybackTimeScale)
                    ?? new HandArrivalBatch(previous.Revision, current.Revision, panel.Clock, panel.PlaybackTimeScale);
                foreach (var id in currentIds) panel._pendingArrivalIds[id] = batch;
            }
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
        var owner = ItemsControl.GetItemsOwner(this);
        // The initial ItemCollection view can reset after the current snapshot
        // is already bound. Only a source reset denotes the engine's Hand.Clear
        // and requires waiting for its replacement snapshot.
        var items = owner?.ItemsSource as INotifyCollectionChanged ?? owner?.Items as INotifyCollectionChanged;
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
        ArrivalTimeline?.Clear();
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
        var scale = CardScale;
        var cardSize = new Size(CardWidth * scale, CardHeight * scale);
        foreach (UIElement child in InternalChildren) child.Measure(cardSize);
        var minimum = InternalChildren.Count == 0 ? 0 : (CardWidth + (InternalChildren.Count - 1) * MinimumStep) * scale;
        var width = ViewportWidth > 0 ? ViewportWidth : double.IsInfinity(availableSize.Width) ? minimum : availableSize.Width;
        return new Size(Math.Max(minimum, width), cardSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Count;
        var layoutWidth = ViewportWidth > 0 ? ViewportWidth : finalSize.Width;
        var scale = CardScale;
        var cardWidth = CardWidth * scale;
        var cardHeight = CardHeight * scale;
        if (count == 0)
        {
            _step = 0;
            _slots.Clear();
            _order.Clear();
            _layoutWidth = layoutWidth;
            _layoutScale = scale;
            _hasHandBaseline = !_awaitingSnapshotAfterReset;
            ClearPointer();
            return finalSize;
        }
        _step = count == 1 ? cardWidth : Math.Clamp((finalSize.Width - cardWidth) / (count - 1), MinimumStep * scale, cardWidth);
        var order = InternalChildren.Cast<UIElement>().ToList();
        var identitiesMatch = order.All(child => !_slots.TryGetValue(child, out var old) ||
            ReferenceEquals(old.Identity, (child as FrameworkElement)?.DataContext));
        // Overflow content shrinks when a card leaves; the viewport remains
        // the resize boundary even when the panel's minimum width changes.
        // A new card scale also invalidates offsets from the previous layout.
        var sameLayout = Math.Abs(_layoutWidth - layoutWidth) < .01 && Math.Abs(_layoutScale - scale) < .0001;
        var sameOrder = _order.SequenceEqual(order);
        var survivors = order.Where(_slots.ContainsKey).ToArray();
        // Both a committed replacement and an explicit sort move surviving
        // cards from their current visible pose. Reordering does not introduce
        // new cards, so keep its arrival/reset boundary separate below.
        var reflow = !sameOrder && survivors.Length > 0 && sameLayout && identitiesMatch;
        var preserveReflow = sameLayout && sameOrder && identitiesMatch;
        var allowArrival = _panelLoaded && _hasHandBaseline && !_awaitingSnapshotAfterReset && ArrivalEnabled &&
            sameLayout;
        var preserveArrival = sameLayout && identitiesMatch;
        // A sort can occur after the committed snapshot but before first
        // layout. It changes slots, not the identity of the newly gained cards.
        if (_hasHandBaseline && !sameLayout) _pendingArrivalIds.Clear();
        var arrivalEntry = ArrivalEntryPoint();
        var slots = new Dictionary<UIElement, HandSlot>(count);
        _arranging = true;
        try
        {
            for (var index = 0; index < count; index++)
            {
                var child = order[index];
                _slots.TryGetValue(child, out var old);
                var previousX = old is null ? 0 : old.X + (old.Surface?.ReflowOffset ?? 0) * _layoutScale;
                var x = index * _step;
                child.Arrange(new Rect(x, 0, cardWidth, cardHeight));
                var surface = FindSurface(child);
                if (surface is not null)
                {
                    // Inject the panel's frame clock before a newly generated
                    // card starts moving, including surfaces created by Arrange.
                    if (!ReferenceEquals(Clock, TimeProvider.System)) surface.Clock = Clock;
                    var center = surface.TranslatePoint(new Point(surface.ActualWidth / 2, surface.ActualHeight / 2), this);
                    // Entry offsets are relative to the arranged slot, not its
                    // current lift/reflow/arrival transform.
                    var entryOffset = (arrivalEntry - center) / scale +
                        new Vector(surface.RenderTransform.Value.OffsetX, surface.RenderTransform.Value.OffsetY);
                    if (preserveArrival && surface.IsArrivalAnimating && old is not null && ReferenceEquals(old.Surface, surface))
                        surface.RetargetArrival(entryOffset, new Vector((old.X - x) / scale, 0));
                    else if (reflow && old is not null && ReferenceEquals(old.Surface, surface))
                    {
                        // Surface transforms are local to the scaled card template;
                        // slots and pointer coordinates are in panel DIP.
                        surface.ReflowFrom((previousX - x) / scale);
                    }
                    else if (!preserveReflow || old is null || !ReferenceEquals(old.Surface, surface))
                        surface.FinishReflow();
                    if (!preserveArrival) surface.FinishArrival();
                    if ((child as FrameworkElement)?.DataContext is CardViewModel card && _pendingArrivalIds.Remove(card.Id, out var arrivalBatch))
                    {
                        if (allowArrival)
                        {
                            // Public count-only flights stop at one entrance. Only
                            // this local snapshot connects new faces to their slots.
                            surface.BeginArrival(entryOffset, PlaybackTimeScale, arrivalBatch);
                        }
                        else surface.FinishArrival();
                    }
                }
                slots.Add(child, new HandSlot(x, (child as FrameworkElement)?.DataContext, surface));
            }
            _slots = slots;
            _order = order;
            _layoutWidth = layoutWidth;
            _layoutScale = scale;
            _hasHandBaseline = !_awaitingSnapshotAfterReset;
        }
        finally { _arranging = false; }
        // Sorting and resizing can move a different card beneath a stationary
        // pointer. Keep the lift and visibility suppression on that physical slot.
        if (IsMouseOver) _pointerPosition = Mouse.GetPosition(this);
        UpdateHoveredCard();
        return finalSize;
    }

    private Point ArrivalEntryPoint()
    {
        for (DependencyObject? ancestor = this; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ancestor is ScrollViewer viewport)
                return viewport.TranslatePoint(new Point(viewport.ActualWidth / 2, ArrivalEntryInset), this);
        return new Point((ViewportWidth > 0 ? ViewportWidth : ActualWidth) / 2, ArrivalEntryInset);
    }

    // Follow horizontal motion, while keeping the physical vertical hit strip
    // stable when artwork lifts over its neighbor.
    private UIElement? CardContainerAt(Point point) => CardContainerAt(point, out _);

    private UIElement? CardContainerAt(Point point, out bool hiddenArrivalAtPoint)
    {
        hiddenArrivalAtPoint = false;
        var count = InternalChildren.Count;
        if (count == 0 || _step <= 0 || point.X < 0 || point.Y < 0 || point.Y > CardHeight * _layoutScale) return null;
        var width = CardWidth * _layoutScale;
        for (var index = count - 1; index >= 0; index--)
        {
            var child = InternalChildren[index];
            _slots.TryGetValue(child, out var slot);
            var slotX = slot?.X ?? index * _step;
            var x = slotX + (slot?.Surface?.HorizontalOffset ?? 0) * _layoutScale;
            if (slot?.Surface?.ArrivalOpacity <= 0)
            {
                // An unseen face cannot cover an old hand card. Its Button
                // still occupies the destination slot, however, so remember
                // that empty hit area to suppress the native release click.
                hiddenArrivalAtPoint |= point.X >= x && point.X < x + width ||
                    point.X >= slotX && point.X < slotX + width;
                continue;
            }
            if (point.X >= x && point.X < x + width) return child;
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
        var button = FindButton(CardContainerAt(point, out var hiddenArrivalAtPoint));
        if (button is null) return hiddenArrivalAtPoint;
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
