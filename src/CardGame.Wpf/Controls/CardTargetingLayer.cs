using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CardGame.Wpf.Controls;

/// <summary>A local, reversible targeting guide. It reads selection markers and never handles input.</summary>
public sealed class CardTargetingLayer : FrameworkElement
{
    public static readonly DependencyProperty IsDraftCardProperty = DependencyProperty.RegisterAttached(
        "IsDraftCard", typeof(bool), typeof(CardTargetingLayer), new PropertyMetadata(false, OnDraftMarkerChanged));
    public static readonly DependencyProperty IsDraftTargetProperty = DependencyProperty.RegisterAttached(
        "IsDraftTarget", typeof(bool), typeof(CardTargetingLayer), new PropertyMetadata(false, OnDraftMarkerChanged));
    public static readonly DependencyProperty IsAimTargetProperty = DependencyProperty.RegisterAttached(
        "IsAimTarget", typeof(bool), typeof(CardTargetingLayer),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static readonly RoutedEvent DraftChangedEvent = EventManager.RegisterRoutedEvent(
        "DraftChanged", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CardTargetingLayer));
    public static readonly DependencyProperty IsDraftEnabledProperty = DependencyProperty.Register(
        nameof(IsDraftEnabled), typeof(bool), typeof(CardTargetingLayer), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AnchorRootProperty = ElementProperty(nameof(AnchorRoot), OnRootsChanged);
    public static readonly DependencyProperty HandRootProperty = ElementProperty(nameof(HandRoot), OnRootsChanged);
    public static readonly DependencyProperty PhaseHudProperty = ElementProperty(nameof(PhaseHud));
    public static readonly DependencyProperty ActionBarProperty = ElementProperty(nameof(ActionBar));
    public static readonly DependencyProperty ActionHintProperty = ElementProperty(nameof(ActionHint));
    public static readonly DependencyProperty SupplementalActionsProperty = ElementProperty(nameof(SupplementalActions));
    public static readonly DependencyProperty SidebarProperty = ElementProperty(nameof(Sidebar));
    public static readonly DependencyProperty CenterAnchorProperty = ElementProperty(nameof(CenterAnchor));
    public static readonly DependencyProperty IsPromptVisibleProperty = DependencyProperty.Register(
        nameof(IsPromptVisible), typeof(bool), typeof(CardTargetingLayer), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PointerAimPointProperty = DependencyProperty.Register(
        nameof(PointerAimPoint), typeof(Point?), typeof(CardTargetingLayer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PointerAimAnchorProperty = ElementProperty(nameof(PointerAimAnchor));
    public static readonly DependencyProperty IsPointerAimValidProperty = DependencyProperty.Register(
        nameof(IsPointerAimValid), typeof(bool), typeof(CardTargetingLayer),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PointerAimLabelProperty = DependencyProperty.Register(
        nameof(PointerAimLabel), typeof(string), typeof(CardTargetingLayer),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Pen GuidePen = CreatePen(true);
    private static readonly Pen ArrowPen = CreatePen(false);
    private static readonly Pen ReadyGuidePen = CreatePen(true, Color.FromArgb(230, 151, 240, 192));
    private static readonly Pen ReadyArrowPen = CreatePen(false, Color.FromArgb(230, 151, 240, 192));
    private static readonly Pen PendingGuidePen = CreatePen(true, Color.FromArgb(230, 242, 185, 100));
    private static readonly Pen PendingArrowPen = CreatePen(false, Color.FromArgb(230, 242, 185, 100));
    private static readonly Brush LabelBackground = CreateBrush(Color.FromArgb(238, 20, 34, 30));
    private static readonly Typeface LabelTypeface = new("Microsoft YaHei");
    private FrameworkElement? _observedRoot;
    private FrameworkElement? _observedHand;
    private bool _unloaded;
    private DraftGeometry _lastGeometry = new([], []);

    public CardTargetingLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        Loaded += (_, _) => { _unloaded = false; Observe(); RefreshGeometry(); };
        Unloaded += (_, _) =>
        {
            _unloaded = true;
            Unobserve();
            SetCurrentValue(PointerAimPointProperty, null);
            SetCurrentValue(PointerAimAnchorProperty, null);
            SetCurrentValue(IsPointerAimValidProperty, false);
            SetCurrentValue(PointerAimLabelProperty, string.Empty);
            _lastGeometry = new([], []);
            InvalidateVisual();
        };
    }

    public bool IsDraftEnabled { get => (bool)GetValue(IsDraftEnabledProperty); set => SetValue(IsDraftEnabledProperty, value); }
    public FrameworkElement? AnchorRoot { get => (FrameworkElement?)GetValue(AnchorRootProperty); set => SetValue(AnchorRootProperty, value); }
    public FrameworkElement? HandRoot { get => (FrameworkElement?)GetValue(HandRootProperty); set => SetValue(HandRootProperty, value); }
    public FrameworkElement? PhaseHud { get => (FrameworkElement?)GetValue(PhaseHudProperty); set => SetValue(PhaseHudProperty, value); }
    public FrameworkElement? ActionBar { get => (FrameworkElement?)GetValue(ActionBarProperty); set => SetValue(ActionBarProperty, value); }
    public FrameworkElement? ActionHint { get => (FrameworkElement?)GetValue(ActionHintProperty); set => SetValue(ActionHintProperty, value); }
    public FrameworkElement? SupplementalActions { get => (FrameworkElement?)GetValue(SupplementalActionsProperty); set => SetValue(SupplementalActionsProperty, value); }
    public FrameworkElement? Sidebar { get => (FrameworkElement?)GetValue(SidebarProperty); set => SetValue(SidebarProperty, value); }
    public FrameworkElement? CenterAnchor { get => (FrameworkElement?)GetValue(CenterAnchorProperty); set => SetValue(CenterAnchorProperty, value); }
    public bool IsPromptVisible { get => (bool)GetValue(IsPromptVisibleProperty); set => SetValue(IsPromptVisibleProperty, value); }
    public Point? PointerAimPoint { get => (Point?)GetValue(PointerAimPointProperty); set => SetValue(PointerAimPointProperty, value); }
    public FrameworkElement? PointerAimAnchor { get => (FrameworkElement?)GetValue(PointerAimAnchorProperty); set => SetValue(PointerAimAnchorProperty, value); }
    public bool IsPointerAimValid { get => (bool)GetValue(IsPointerAimValidProperty); set => SetValue(IsPointerAimValidProperty, value); }
    public string PointerAimLabel { get => (string?)GetValue(PointerAimLabelProperty) ?? string.Empty; set => SetValue(PointerAimLabelProperty, value); }
    public static bool GetIsDraftCard(DependencyObject element) => (bool)element.GetValue(IsDraftCardProperty);
    public static void SetIsDraftCard(DependencyObject element, bool value) => element.SetValue(IsDraftCardProperty, value);
    public static bool GetIsDraftTarget(DependencyObject element) => (bool)element.GetValue(IsDraftTargetProperty);
    public static void SetIsDraftTarget(DependencyObject element, bool value) => element.SetValue(IsDraftTargetProperty, value);
    public static bool GetIsAimTarget(DependencyObject element) => (bool)element.GetValue(IsAimTargetProperty);
    public static void SetIsAimTarget(DependencyObject element, bool value) => element.SetValue(IsAimTargetProperty, value);

    private static DependencyProperty ElementProperty(string name, PropertyChangedCallback? changed = null) =>
        DependencyProperty.Register(name, typeof(FrameworkElement), typeof(CardTargetingLayer),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, changed));

    private static Pen CreatePen(bool dashed, Color? color = null)
    {
        var ink = CreateBrush(color ?? Color.FromArgb(210, 255, 219, 147));
        var pen = new Pen(ink, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (dashed) pen.DashStyle = new DashStyle([4, 4], 0);
        pen.Freeze();
        return pen;
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void OnDraftMarkerChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is UIElement anchor) anchor.RaiseEvent(new RoutedEventArgs(DraftChangedEvent, anchor));
    }

    private static void OnRootsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var layer = (CardTargetingLayer)sender;
        layer.Unobserve();
        if (!layer._unloaded) layer.Observe();
        layer.RefreshGeometry();
    }

    private void Observe()
    {
        if (_observedRoot is null && AnchorRoot is { } root)
        {
            _observedRoot = root;
            root.LayoutUpdated += OnLayoutUpdated;
            root.AddHandler(DraftChangedEvent, new RoutedEventHandler(OnDraftChanged), true);
        }
        if (_observedHand is null && HandRoot is { } hand)
        {
            _observedHand = hand;
            hand.AddHandler(CardLiftSurface.LiftChangedEvent, new RoutedEventHandler(OnDraftChanged), true);
        }
    }

    private void Unobserve()
    {
        if (_observedRoot is { } root)
        {
            root.LayoutUpdated -= OnLayoutUpdated;
            root.RemoveHandler(DraftChangedEvent, new RoutedEventHandler(OnDraftChanged));
        }
        if (_observedHand is { } hand)
            hand.RemoveHandler(CardLiftSurface.LiftChangedEvent, new RoutedEventHandler(OnDraftChanged));
        _observedRoot = null;
        _observedHand = null;
    }

    private void OnDraftChanged(object sender, RoutedEventArgs args) => RefreshGeometry();
    private void OnLayoutUpdated(object? sender, EventArgs args) => RefreshGeometry();

    private void RefreshGeometry()
    {
        var geometry = CaptureGeometry();
        // LayoutUpdated also runs after unrelated renders. Only actual selection,
        // anchor movement, scrolling or protection changes require another frame.
        if (_lastGeometry.Paths.SequenceEqual(geometry.Paths) && _lastGeometry.Protected.SequenceEqual(geometry.Protected) &&
            _lastGeometry.IsPointerAim == geometry.IsPointerAim && _lastGeometry.IsValid == geometry.IsValid &&
            _lastGeometry.Label == geometry.Label) return;
        _lastGeometry = geometry;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var geometry = CaptureGeometry();
        _lastGeometry = geometry;
        if (geometry.Paths.Length == 0 && geometry.Label is null) return;
        Geometry clip = new RectangleGeometry(new Rect(RenderSize));
        foreach (var area in geometry.Protected)
            clip = new CombinedGeometry(GeometryCombineMode.Exclude, clip, new RectangleGeometry(area));
        clip.Freeze();
        drawingContext.PushClip(clip);
        var guidePen = geometry.IsPointerAim ? geometry.IsValid ? ReadyGuidePen : PendingGuidePen : GuidePen;
        var arrowPen = geometry.IsPointerAim ? geometry.IsValid ? ReadyArrowPen : PendingArrowPen : ArrowPen;
        foreach (var path in geometry.Paths)
        {
            var curve = new StreamGeometry();
            using (var context = curve.Open())
            {
                context.BeginFigure(path.Start, false, false);
                context.BezierTo(path.Control1, path.Control2, path.End, true, false);
            }
            curve.Freeze();
            drawingContext.DrawGeometry(null, guidePen, curve);
            var direction = path.End - path.Control2;
            direction.Normalize();
            var normal = new Vector(-direction.Y, direction.X);
            drawingContext.DrawLine(arrowPen, path.End - direction * 8 + normal * 4, path.End);
            drawingContext.DrawLine(arrowPen, path.End - direction * 8 - normal * 4, path.End);
        }
        if (geometry.Label is { } label)
        {
            drawingContext.DrawRoundedRectangle(LabelBackground, arrowPen, label.Bounds, 3, 3);
            drawingContext.DrawText(AimText(label.Text, geometry.IsValid), new Point(label.Bounds.Left + 8, label.Bounds.Top + 5));
        }
        drawingContext.Pop();
    }

    private DraftGeometry CaptureGeometry()
    {
        if (_unloaded || !IsDraftEnabled || AnchorRoot is null || HandRoot is null) return new([], []);
        var viewport = Bounds(HandRoot);
        if (viewport.IsEmpty) return new([], []);
        var cards = new List<Rect>();
        var handFaces = new List<Rect>();
        Walk(HandRoot, element =>
        {
            if (BattleFeedbackLayer.GetHandAnchor(element) <= 0) return;
            var bounds = Bounds(element);
            if (bounds.IsEmpty || !bounds.IntersectsWith(viewport)) return;
            var visible = Rect.Intersect(bounds, viewport);
            visible.Inflate(3, 3);
            handFaces.Add(visible);
            // A hidden or horizontally clipped selection must not leave a guide
            // detached from its physical card. Reveal it through normal hand input.
            if (GetIsDraftCard(element) && viewport.Contains(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2))) cards.Add(bounds);
        });
        if (cards.Count != 1) return new([], []);
        var start = new Point(cards[0].Left + cards[0].Width / 2, cards[0].Top);
        var selected = new Dictionary<int, Rect>();
        // A self-targeting curve can cross the hand row. Keep every visible
        // physical face readable without changing open-table drop eligibility.
        var protectedAreas = CaptureProtectedAreas(selected).Concat(handFaces).ToArray();
        var paths = new List<DraftPath>();
        if (PointerAimPoint is { } pointer && double.IsFinite(pointer.X) && double.IsFinite(pointer.Y))
        {
            var anchor = PointerAimAnchor;
            var anchorBounds = IsPointerAimValid && anchor is not null && (!IsLoaded || anchor.IsLoaded) ? Bounds(anchor) : Rect.Empty;
            var end = pointer;
            var outward = start - end;
            if (!anchorBounds.IsEmpty)
            {
                var center = new Point(anchorBounds.Left + anchorBounds.Width / 2, anchorBounds.Top + anchorBounds.Height / 2);
                outward = start - center;
                if (outward.Length >= 1) end = EdgePoint(anchorBounds, center, outward);
            }
            AddPath(end, outward);
            var valid = IsPointerAimValid && (anchor is null || !anchorBounds.IsEmpty);
            return new(paths.ToArray(), protectedAreas, true, valid, AimLabel(end, protectedAreas, valid));
        }
        foreach (var bounds in selected.OrderBy(item => item.Key).Select(item => item.Value))
        {
            var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            var outward = start - center;
            if (outward.Length < 1) continue;
            AddPath(EdgePoint(bounds, center, outward), outward);
        }
        return new(paths.ToArray(), protectedAreas);

        void AddPath(Point end, Vector outward)
        {
            var travel = end - start;
            if (travel.Length < 1 || outward.Length < 1) return;
            outward.Normalize();
            var lift = Math.Clamp(travel.Length * .25, 35, 90);
            paths.Add(new(start, start + new Vector(0, -lift), end + outward * Math.Min(55, travel.Length / 3), end));
        }
    }

    private static Point EdgePoint(Rect bounds, Point center, Vector outward)
    {
        var edgeScale = Math.Min(Math.Abs(outward.X) < .01 ? double.PositiveInfinity : bounds.Width / 2 / Math.Abs(outward.X),
            Math.Abs(outward.Y) < .01 ? double.PositiveInfinity : bounds.Height / 2 / Math.Abs(outward.Y));
        var edge = center + outward * edgeScale;
        outward.Normalize();
        return edge + outward * 5;
    }

    internal bool IsOpenTablePoint(Point point)
    {
        if (_unloaded || AnchorRoot is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            !new Rect(RenderSize).Contains(point) || !Bounds(AnchorRoot).Contains(point)) return false;
        return CaptureProtectedAreas().All(area => !area.Contains(point));
    }

    private Rect[] CaptureProtectedAreas(Dictionary<int, Rect>? selected = null)
    {
        var protectedAreas = new List<Rect>();
        foreach (var element in new[] { ActionBar, SupplementalActions, Sidebar, PhaseHud, IsPromptVisible ? CenterAnchor : null })
            Protect(Bounds(element));
        if (ActionHint is { } hint)
        {
            // A stretched, centered TextBlock has a wide layout slot. Protect
            // its actual rendered words while leaving the surrounding table free.
            var content = VisualTreeHelper.GetContentBounds(hint);
            if (!content.IsEmpty) Protect(Bounds(hint, content));
        }
        if (AnchorRoot is { } root) Walk(root, element =>
        {
            var seat = BattleFeedbackLayer.GetSeatAnchor(element);
            var play = BattleFeedbackLayer.GetPlayAnchor(element);
            if (seat < 0 && play < 0) return;
            var bounds = Bounds(element);
            if (seat >= 0)
            {
                if (selected is not null && GetIsDraftTarget(element) && !bounds.IsEmpty) selected[seat] = bounds;
                Protect(bounds);
            }
            if (play >= 0) Protect(bounds);
        });
        return protectedAreas.ToArray();

        void Protect(Rect area)
        {
            if (area.IsEmpty) return;
            area.Inflate(3, 3);
            protectedAreas.Add(area);
        }
    }

    private DraftLabel? AimLabel(Point end, Rect[] protectedAreas, bool valid)
    {
        if (string.IsNullOrWhiteSpace(PointerAimLabel)) return null;
        var text = AimText(PointerAimLabel, valid);
        var width = Math.Ceiling(text.Width) + 16;
        var height = Math.Ceiling(text.Height) + 10;
        var candidates = new[]
        {
            new Point(end.X - width / 2, end.Y - height - 10),
            new Point(end.X - width / 2, end.Y + 10),
            new Point(end.X + 12, end.Y - height / 2),
            new Point(end.X - width - 12, end.Y - height / 2),
            new Point(end.X + 12, end.Y - height - 10),
            new Point(end.X - width - 12, end.Y - height - 10),
            new Point(end.X + 12, end.Y + 10),
            new Point(end.X - width - 12, end.Y + 10)
        };
        var renderBounds = new Rect(RenderSize);
        var handBounds = Bounds(HandRoot);
        foreach (var point in candidates)
        {
            var bounds = new Rect(point, new Size(width, height));
            var clear = bounds;
            clear.Inflate(1, 1);
            if (renderBounds.Contains(clear) && (handBounds.IsEmpty || !handBounds.IntersectsWith(clear)) &&
                protectedAreas.All(area => !area.IntersectsWith(clear))) return new(PointerAimLabel, bounds);
        }
        return null;
    }

    private FormattedText AimText(string text, bool valid) => new(text, CultureInfo.GetCultureInfo("zh-CN"),
        FlowDirection.LeftToRight, LabelTypeface, 12, valid ? ReadyArrowPen.Brush : PendingArrowPen.Brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = 160, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };

    private Rect Bounds(FrameworkElement? element, Rect? localBounds = null)
    {
        if (element is null || element.ActualWidth <= 0 || element.ActualHeight <= 0 || !Visible(element)) return Rect.Empty;
        try { return element.TransformToVisual(this).TransformBounds(localBounds ?? new Rect(element.RenderSize)); }
        catch (InvalidOperationException) { return Rect.Empty; }
    }

    private static bool Visible(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible } or UIElement { Opacity: <= 0 }) return false;
        return true;
    }

    private static void Walk(DependencyObject root, Action<FrameworkElement> visit)
    {
        if (root is UIElement { Visibility: not Visibility.Visible } or UIElement { Opacity: <= 0 }) return;
        if (root is FrameworkElement element) visit(element);
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) Walk(VisualTreeHelper.GetChild(root, index), visit);
    }

    private sealed record DraftGeometry(DraftPath[] Paths, Rect[] Protected, bool IsPointerAim = false,
        bool IsValid = false, DraftLabel? Label = null);
    private readonly record struct DraftLabel(string Text, Rect Bounds);
    private readonly record struct DraftPath(Point Start, Point Control1, Point Control2, Point End);
}
