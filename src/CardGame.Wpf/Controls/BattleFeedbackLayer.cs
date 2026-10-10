using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Controls;

/// <summary>A transient, non-interactive layer. It never delays or submits a game command.</summary>
public sealed class BattleFeedbackLayer : FrameworkElement
{
    public static readonly DependencyProperty CuesProperty = DependencyProperty.Register(nameof(Cues), typeof(IEnumerable), typeof(BattleFeedbackLayer), new PropertyMetadata(null, OnCuesChanged));
    public static readonly DependencyProperty AnchorRootProperty = DependencyProperty.Register(nameof(AnchorRoot), typeof(FrameworkElement), typeof(BattleFeedbackLayer), new PropertyMetadata(null));
    public static readonly DependencyProperty CenterAnchorProperty = DependencyProperty.Register(nameof(CenterAnchor), typeof(FrameworkElement), typeof(BattleFeedbackLayer), new PropertyMetadata(null));
    public static readonly DependencyProperty ActionBarProperty = DependencyProperty.Register(nameof(ActionBar), typeof(FrameworkElement), typeof(BattleFeedbackLayer), new PropertyMetadata(null));
    public static readonly DependencyProperty MotionEnabledProperty = DependencyProperty.Register(nameof(MotionEnabled), typeof(bool), typeof(BattleFeedbackLayer), new PropertyMetadata(true, OnMotionChanged));
    public static readonly DependencyProperty CommittedRevisionProperty = DependencyProperty.Register(nameof(CommittedRevision), typeof(long), typeof(BattleFeedbackLayer), new PropertyMetadata(0L, OnCommittedRevisionChanged));
    public static readonly DependencyProperty IsPromptVisibleProperty = DependencyProperty.Register(nameof(IsPromptVisible), typeof(bool), typeof(BattleFeedbackLayer), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SeatAnchorProperty = DependencyProperty.RegisterAttached("SeatAnchor", typeof(int), typeof(BattleFeedbackLayer), new PropertyMetadata(-1));
    public static readonly DependencyProperty PlayAnchorProperty = DependencyProperty.RegisterAttached("PlayAnchor", typeof(long), typeof(BattleFeedbackLayer), new PropertyMetadata(-1L));

    private readonly List<TimedCue> _active = [];
    private readonly DispatcherTimer _timer;
    private INotifyCollectionChanged? _observed;
    private bool _loaded;
    private bool _hasLoaded;
    private static readonly Typeface Calligraphy = new("KaiTi");
    private static readonly Typeface TextTypeface = new("Microsoft YaHei");
    private static readonly Brush Paper = Frozen(Colors.Wheat);
    private static readonly Brush Dark = Frozen(Color.FromRgb(19, 31, 34));
    private static readonly Brush Shadow = Frozen(Color.FromArgb(150, 0, 0, 0));
    private static readonly Brush CardNameInk = Frozen(Color.FromRgb(77, 57, 35));

    public BattleFeedbackLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        Loaded += (_, _) => { _hasLoaded = true; _loaded = true; Observe(); StartIfNeeded(); };
        Unloaded += (_, _) => { _loaded = false; StopAndClear(); Unobserve(); };
    }

    public IEnumerable? Cues { get => (IEnumerable?)GetValue(CuesProperty); set => SetValue(CuesProperty, value); }
    public FrameworkElement? AnchorRoot { get => (FrameworkElement?)GetValue(AnchorRootProperty); set => SetValue(AnchorRootProperty, value); }
    public FrameworkElement? CenterAnchor { get => (FrameworkElement?)GetValue(CenterAnchorProperty); set => SetValue(CenterAnchorProperty, value); }
    public FrameworkElement? ActionBar { get => (FrameworkElement?)GetValue(ActionBarProperty); set => SetValue(ActionBarProperty, value); }
    public bool MotionEnabled { get => (bool)GetValue(MotionEnabledProperty); set => SetValue(MotionEnabledProperty, value); }
    public long CommittedRevision { get => (long)GetValue(CommittedRevisionProperty); set => SetValue(CommittedRevisionProperty, value); }
    public bool IsPromptVisible { get => (bool)GetValue(IsPromptVisibleProperty); set => SetValue(IsPromptVisibleProperty, value); }
    public TimeProvider Clock { get; set; } = TimeProvider.System;
    public int ActiveEffectCount => _active.Count;
    public bool IsFrameTimerRunning => _timer.IsEnabled;
    public static int GetSeatAnchor(DependencyObject element) => (int)element.GetValue(SeatAnchorProperty);
    public static void SetSeatAnchor(DependencyObject element, int value) => element.SetValue(SeatAnchorProperty, value);
    public static long GetPlayAnchor(DependencyObject element) => (long)element.GetValue(PlayAnchorProperty);
    public static void SetPlayAnchor(DependencyObject element, long value) => element.SetValue(PlayAnchorProperty, value);

    private static void OnCuesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var layer = (BattleFeedbackLayer)sender;
        layer.Unobserve();
        layer.StopAndClear();
        if (!layer._hasLoaded || layer._loaded) layer.Observe(); // Existing entries are history, and are intentionally not replayed.
    }

    private void Observe()
    {
        if (_observed is not null || Cues is not INotifyCollectionChanged observed) return;
        _observed = observed;
        _observed.CollectionChanged += OnCollectionChanged;
    }

    private void Unobserve()
    {
        if (_observed is not null) _observed.CollectionChanged -= OnCollectionChanged;
        _observed = null;
    }

    private static void OnMotionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (!(bool)args.NewValue) ((BattleFeedbackLayer)sender).StopAndClear();
    }

    private static void OnCommittedRevisionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var layer = (BattleFeedbackLayer)sender;
        // An accepted continuation ends the previous transient inquiry, including
        // a pass with no new animation. Keep actual played cards and results.
        layer._active.RemoveAll(item => item.Cue.IsInquiry && item.Cue.Revision < (long)args.NewValue);
        if (layer._active.Count == 0) layer._timer.Stop();
        layer.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset) { StopAndClear(); return; }
        if (args.Action != NotifyCollectionChangedAction.Add || !MotionEnabled || args.NewItems is null) return;
        var now = Clock.GetTimestamp();
        foreach (var cue in args.NewItems.OfType<BattleCue>())
            if (!cue.IsInquiry || cue.Revision >= CommittedRevision)
                _active.Add(new(cue, now, IsCardPlay(cue) ? CardArt.Get(cue.CardKind) : null));
        if (_active.Count > 24) _active.RemoveRange(0, _active.Count - 24);
        InvalidateVisual();
        StartIfNeeded();
    }

    private void StartIfNeeded()
    {
        if (_loaded && MotionEnabled && _active.Count > 0) _timer.Start();
    }

    private void StopAndClear()
    {
        _timer.Stop();
        _active.Clear();
        InvalidateVisual();
    }

    private void OnTick(object? sender, EventArgs args)
    {
        RemoveExpired();
        InvalidateVisual();
    }

    private void RemoveExpired()
    {
        var now = Clock.GetTimestamp();
        _active.RemoveAll(cue => Clock.GetElapsedTime(cue.Start, now).TotalSeconds >= Duration(cue.Cue.Kind));
        if (_active.Count == 0) _timer.Stop();
    }

    private static double Duration(BattleCueKind kind) => kind switch
    {
        BattleCueKind.Card => .9,
        BattleCueKind.Turn => .95,
        BattleCueKind.ResponseWindow => 1.25,
        BattleCueKind.Judgment => 1.45,
        BattleCueKind.Dying or BattleCueKind.Death => 1.5,
        BattleCueKind.Skill => 1.65,
        _ => 1.15
    };

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        RemoveExpired();
        if (!MotionEnabled || _active.Count == 0 || ActualWidth < 1 || AnchorRoot is null) return;
        var seats = new Dictionary<int, Rect>();
        var plays = new Dictionary<long, Rect>();
        CollectAnchors(AnchorRoot, seats, plays);
        var centerArea = Bounds(CenterAnchor);
        var center = centerArea.IsEmpty ? new Point(ActualWidth * .5, ActualHeight * .45)
            : new Point(centerArea.X + centerArea.Width / 2, centerArea.Y + centerArea.Height * .64);
        var now = Clock.GetTimestamp();
        for (var i = 0; i < _active.Count; i++)
        {
            var item = _active[i];
            var progress = Math.Clamp(Clock.GetElapsedTime(item.Start, now).TotalSeconds / Duration(item.Cue.Kind), 0, 1);
            DrawBattleEffects(dc, item.Cue, progress, seats);
            DrawCue(dc, item.Cue, item.Artwork, progress, seats, plays, center, i);
        }
    }

    private void DrawBattleEffects(DrawingContext dc, BattleCue cue, double progress, IReadOnlyDictionary<int, Rect> seats)
    {
        // Effects can cross the battlefield, but must leave decisions and command buttons readable.
        Geometry clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        foreach (var protectedArea in new[] { Bounds(ActionBar), IsPromptVisible ? Bounds(CenterAnchor) : Rect.Empty })
            if (!protectedArea.IsEmpty)
                clip = new CombinedGeometry(GeometryCombineMode.Exclude, clip, new RectangleGeometry(protectedArea));
        clip.Freeze();
        dc.PushClip(clip);
        BattleEffectPainter.Draw(dc, cue, progress, seats.GetValueOrDefault(cue.SourceSeat, Rect.Empty), seats,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.Pop();
    }

    private void CollectAnchors(DependencyObject element, Dictionary<int, Rect> seats, Dictionary<long, Rect> plays)
    {
        if (element is UIElement { Visibility: not Visibility.Visible }) return;
        if (element is FrameworkElement visual && GetSeatAnchor(visual) is var seat && seat >= 0)
        {
            var bounds = Bounds(visual);
            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0) seats[seat] = bounds;
            return;
        }
        if (element is FrameworkElement card && GetPlayAnchor(card) is var sequence && sequence >= 0)
        {
            var bounds = Bounds(card);
            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0) plays[sequence] = bounds;
            return;
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            CollectAnchors(VisualTreeHelper.GetChild(element, index), seats, plays);
    }

    private Rect Bounds(FrameworkElement? element)
    {
        if (element is null || element.ActualWidth <= 0 || element.ActualHeight <= 0) return Rect.Empty;
        try { return element.TransformToVisual(this).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
        catch (InvalidOperationException) { return Rect.Empty; }
    }

    private void DrawCue(DrawingContext dc, BattleCue cue, ImageSource? artwork, double progress, IReadOnlyDictionary<int, Rect> seats, IReadOnlyDictionary<long, Rect> plays, Point center, int lane)
    {
        var color = cue.Kind switch
        {
            BattleCueKind.Damage => cue.Nature == DamageNature.Thunder ? Color.FromRgb(179, 173, 255) : Color.FromRgb(255, 126, 99),
            BattleCueKind.Recovery => Color.FromRgb(145, 227, 170),
            BattleCueKind.Response => Color.FromRgb(145, 221, 218),
            BattleCueKind.ResponseWindow => Color.FromRgb(125, 190, 205),
            BattleCueKind.Judgment => Color.FromRgb(238, 196, 112),
            BattleCueKind.Dying or BattleCueKind.Death => Color.FromRgb(232, 145, 120),
            _ => Color.FromRgb(246, 210, 145)
        };
        var ink = Frozen(color);
        var alpha = Math.Min(1, progress / .09) * Math.Min(1, (1 - progress) / .3);
        var from = seats.TryGetValue(cue.SourceSeat, out var source) ? Center(source) : center;
        dc.PushOpacity(alpha);

        if (IsCardPlay(cue))
        {
            foreach (var target in cue.TargetSeats)
            {
                if (!seats.TryGetValue(target, out var bounds)) continue;
                Highlight(dc, bounds, ink, progress);
                if (cue.TargetSeats.Count <= 2 && target != cue.SourceSeat && seats.ContainsKey(cue.SourceSeat)) Arrow(dc, source, bounds, ink, progress);
            }
            var hasLanding = plays.TryGetValue(cue.Sequence, out var landing);
            if (IsPromptVisible && !hasLanding) Badge(dc, from + new Vector(0, -25), cue.Label, ink, 20);
            else
            {
                var destination = hasLanding ? Center(landing) : center + new Vector(lane % 3 * 8, 0);
                var move = 1 - Math.Pow(1 - Math.Min(1, progress / .4), 3);
                var point = from + (destination - from) * move;
                var scale = .58 + .42 * move;
                dc.PushTransform(new ScaleTransform(scale, scale, point.X, point.Y));
                DrawCard(dc, point, hasLanding ? landing.Size : new Size(86, 120), cue, artwork, ink);
                dc.Pop();
            }
        }
        else if (cue.Kind == BattleCueKind.Turn)
        {
            if (seats.TryGetValue(cue.SourceSeat, out var bounds)) Highlight(dc, bounds, ink, progress);
            if (!IsPromptVisible)
                Badge(dc, center + new Vector(0, -22 + 10 * Math.Pow(1 - progress, 3)), cue.Label, ink, 24, cue.ActorName);
        }
        else if (cue.Kind == BattleCueKind.Skill)
        {
            // The effect already carries the skill name on its owner's portrait.
        }
        else if (cue.Kind is BattleCueKind.Response or BattleCueKind.ResponseWindow)
        {
            Badge(dc, from + new Vector(0, -28 - 25 * progress), cue.Label, ink,
                cue.Kind == BattleCueKind.ResponseWindow ? 18 : 21, cue.Detail);
        }
        else if (cue.Kind == BattleCueKind.Judgment)
        {
            var target = cue.TargetSeats.FirstOrDefault(cue.SourceSeat);
            var point = seats.TryGetValue(target, out var bounds) ? Center(bounds) : from;
            if (!bounds.IsEmpty && bounds.Width > 0) Highlight(dc, bounds, ink, progress);
            Badge(dc, point + new Vector(0, -40 - 20 * progress), cue.Label, ink, 22, cue.Detail);
        }
        else
        {
            var target = cue.TargetSeats.FirstOrDefault(cue.SourceSeat);
            var point = seats.TryGetValue(target, out var bounds) ? Center(bounds) : from;
            if (!bounds.IsEmpty && bounds.Width > 0) Highlight(dc, bounds, ink, progress);
            var number = cue.Kind is BattleCueKind.Damage or BattleCueKind.Recovery;
            var detail = cue.Kind switch
            {
                BattleCueKind.Recovery => "回复体力",
                BattleCueKind.Damage => cue.Nature switch { DamageNature.Fire => "火焰伤害", DamageNature.Thunder => "雷电伤害", _ => "受到伤害" },
                _ => null
            };
            Badge(dc, point + new Vector(number ? 22 : 0, -24 - (number ? 38 : 12) * progress), cue.Label, ink, number ? 34 : 19, detail);
        }
        dc.Pop();
    }

    private static Point Center(Rect bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    private static bool IsCardPlay(BattleCue cue) => cue.Kind == BattleCueKind.Card ||
        cue.Kind == BattleCueKind.Response && (cue.CardKind is not null || cue.PublicCardId is not null);

    private static void Highlight(DrawingContext dc, Rect bounds, Brush brush, double progress)
    {
        bounds.Inflate(2 + 3 * Math.Sin(progress * Math.PI), 2 + 3 * Math.Sin(progress * Math.PI));
        dc.PushOpacity(.55);
        dc.DrawRoundedRectangle(null, new Pen(brush, 2.5), bounds, 6, 6);
        dc.Pop();
    }

    private void Arrow(DrawingContext dc, Rect sourceBounds, Rect targetBounds, Brush brush, double progress)
    {
        var source = Center(sourceBounds);
        var target = Center(targetBounds);
        var direction = target - source;
        if (direction.Length < 30) return;
        direction.Normalize();
        double Edge(Rect bounds) => Math.Min(bounds.Width / (2 * Math.Max(.001, Math.Abs(direction.X))),
            bounds.Height / (2 * Math.Max(.001, Math.Abs(direction.Y)))) + 8;
        var start = source + direction * Edge(sourceBounds);
        var finish = target - direction * Edge(targetBounds);
        if (Vector.Multiply(finish - start, direction) < 10) return;
        var end = start + (finish - start) * Math.Min(1, progress / .3);
        var perpendicular = new Vector(-direction.Y, direction.X);
        var actionBar = Bounds(ActionBar);
        if (!actionBar.IsEmpty)
        {
            var clip = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)), new RectangleGeometry(actionBar));
            clip.Freeze();
            dc.PushClip(clip);
        }
        dc.PushOpacity(.65);
        dc.DrawLine(new Pen(Shadow, 6), start, end);
        dc.DrawLine(new Pen(brush, 2), start, end);
        var head = new StreamGeometry();
        using (var drawing = head.Open())
        {
            drawing.BeginFigure(end, true, true);
            drawing.LineTo(end - direction * 13 + perpendicular * 5, true, false);
            drawing.LineTo(end - direction * 13 - perpendicular * 5, true, false);
        }
        head.Freeze();
        dc.DrawGeometry(brush, null, head);
        dc.Pop();
        if (!actionBar.IsEmpty) dc.Pop();
    }

    private void DrawCard(DrawingContext dc, Point point, Size size, BattleCue cue, ImageSource? artwork, Brush accent)
    {
        var bounds = new Rect(point.X - size.Width / 2, point.Y - size.Height / 2, size.Width, size.Height);
        dc.DrawRoundedRectangle(Shadow, null, new Rect(bounds.X + 4, bounds.Y + 5, bounds.Width, bounds.Height), 5, 5);
        dc.DrawRoundedRectangle(Paper, new Pen(accent, 2), bounds, 5, 5);
        if (artwork is not null)
        {
            // Match the settled face's canvas and runtime names. The frozen image
            // is resolved when the cue arrives, never decoded during a frame.
            dc.PushTransform(new TranslateTransform(bounds.X + 1, bounds.Y + 1));
            dc.PushTransform(new ScaleTransform((bounds.Width - 2) / 186, (bounds.Height - 2) / 260));
            dc.DrawImage(artwork, new Rect(0, 0, 186, 260));
            if (CardArt.NeedsRuntimeNameOverlay(cue.CardKind))
            {
                var title = Text(CardCatalog.Get(cue.CardKind!.Value).DisplayName, 20, CardNameInk, Calligraphy);
                var bottom = cue.CardKind == CardKind.ScarletBloodSword ? 8 : 25;
                dc.DrawText(title, new Point(105 - title.Width / 2, 260 - bottom - title.Height));
            }
            dc.Pop();
            dc.Pop();
        }
        else
        {
            var inset = bounds;
            inset.Inflate(-5, -5);
            dc.DrawRoundedRectangle(null, new Pen(Frozen(Color.FromRgb(174, 144, 91)), 1), inset, 3, 3);
            var text = Text(string.Join("\n", cue.Label.ToCharArray()), cue.Label.Length > 3 ? 18 : cue.Label.Length == 1 ? 33 : 23, Dark, Calligraphy);
            text.TextAlignment = TextAlignment.Center;
            dc.DrawText(text, new Point(point.X, point.Y - text.Height / 2));
        }
        dc.DrawRectangle(Frozen(Color.FromArgb(224, 35, 27, 19)), null, new Rect(bounds.X, bounds.Bottom, bounds.Width, 21 * size.Width / 86));
        var name = Text(cue.ActorName + cue.CardActionLabel, 10 * size.Width / 86, accent, TextTypeface);
        name.TextAlignment = TextAlignment.Center;
        name.MaxTextWidth = size.Width - 6;
        name.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(name, new Point(bounds.X + 3, bounds.Bottom + 4));
    }

    private void Badge(DrawingContext dc, Point point, string label, Brush accent, double fontSize, string? detail = null)
    {
        var text = Text(label, fontSize, accent, Calligraphy);
        var sub = detail is null ? null : Text(detail, 10, accent, TextTypeface);
        var width = Math.Max(text.Width, sub?.Width ?? 0) + 26;
        var height = text.Height + (sub is null ? 0 : sub.Height + 3) + 12;
        var x = Math.Clamp(point.X - width / 2, 5, Math.Max(5, ActualWidth - width - 5));
        var y = Math.Clamp(point.Y - height / 2, 5, Math.Max(5, ActualHeight - height - 5));
        dc.DrawRoundedRectangle(Shadow, null, new Rect(x + 3, y + 4, width, height), 5, 5);
        dc.DrawRoundedRectangle(Dark, new Pen(accent, 1), new Rect(x, y, width, height), 5, 5);
        dc.DrawText(text, new Point(x + (width - text.Width) / 2, y + 6));
        if (sub is not null) dc.DrawText(sub, new Point(x + (width - sub.Width) / 2, y + text.Height + 9));
    }

    private FormattedText Text(string text, double size, Brush brush, Typeface font) =>
        new(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, font, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private sealed record TimedCue(BattleCue Cue, long Start, ImageSource? Artwork);
}
