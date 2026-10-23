using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CardGame.Wpf.Controls;

/// <summary>Short-lived timings keyed only by already public card identities.</summary>
public sealed class PublicRevealTimeline
{
    private readonly Dictionary<int, (RevealClock Clock, double Delay)> _cards = [];
    internal event Action? Changed;

    internal void Register(IReadOnlyList<int> ids, TimeProvider clock, double scale)
    {
        foreach (var id in _cards.Where(pair => Progress(pair.Key) >= 1).Select(pair => pair.Key).ToArray()) _cards.Remove(id);
        var batch = new RevealClock(clock, scale);
        for (var index = 0; index < ids.Count; index++)
            _cards[ids[index]] = (batch, Math.Min(index, 7) * .035);
        Changed?.Invoke();
    }

    internal double Progress(int id) => _cards.TryGetValue(id, out var entry)
        ? Math.Clamp((entry.Clock.Elapsed - entry.Delay) / .24, 0, 1) : 1;

    internal bool UsesSystemClock(int id) => _cards.TryGetValue(id, out var entry) && entry.Clock.IsSystemClock;

    internal void SetTimeScale(double scale)
    {
        foreach (var clock in _cards.Values.Select(entry => entry.Clock).Distinct()) clock.SetScale(scale);
        Changed?.Invoke();
    }

    internal void Clear() { _cards.Clear(); Changed?.Invoke(); }

    private sealed class RevealClock(TimeProvider clock, double scale)
    {
        private long _start = clock.GetTimestamp();
        private double _elapsed;
        private double _scale = scale;
        public bool IsSystemClock => ReferenceEquals(clock, TimeProvider.System);
        public double Elapsed => _elapsed + clock.GetElapsedTime(_start).TotalSeconds / _scale;
        public void SetScale(double value)
        {
            _elapsed = Elapsed;
            _start = clock.GetTimestamp();
            _scale = value;
        }
    }
}

/// <summary>Reveal only the artwork; its enclosing choice button never moves.</summary>
public sealed class PublicRevealSurface : Grid
{
    public static readonly DependencyProperty TimelineProperty = DependencyProperty.RegisterAttached(
        "Timeline", typeof(PublicRevealTimeline), typeof(PublicRevealSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnTimelineChanged));
    public static PublicRevealTimeline? GetTimeline(DependencyObject element) => (PublicRevealTimeline?)element.GetValue(TimelineProperty);
    public static void SetTimeline(DependencyObject element, PublicRevealTimeline? value) => element.SetValue(TimelineProperty, value);
    public static readonly DependencyProperty CardIdProperty = DependencyProperty.Register(
        nameof(CardId), typeof(int), typeof(PublicRevealSurface), new PropertyMetadata(-1, OnCardChanged));
    public int CardId { get => (int)GetValue(CardIdProperty); set => SetValue(CardIdProperty, value); }

    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _offset = new();
    private readonly DispatcherTimer _timer;
    private PublicRevealTimeline? _observed;
    public double RevealProgress { get { Refresh(); return GetTimeline(this)?.Progress(CardId) ?? 1; } }
    internal bool IsFrameTimerRunning => _timer.IsEnabled;

    public PublicRevealSurface()
    {
        RenderTransform = new TransformGroup { Children = { _scale, _offset } };
        RenderTransformOrigin = new Point(.5, .5);
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => Observe();
        Unloaded += (_, _) => { Detach(); _timer.Stop(); };
        IsVisibleChanged += (_, _) => Refresh();
    }

    private static void OnTimelineChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    { if (sender is PublicRevealSurface surface) surface.Observe(); }
    private static void OnCardChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((PublicRevealSurface)sender).Refresh();
    private void Detach() { if (_observed is not null) _observed.Changed -= Refresh; _observed = null; }
    private void Observe()
    {
        Detach();
        if (IsLoaded && GetTimeline(this) is { } timeline) { _observed = timeline; timeline.Changed += Refresh; }
        Refresh();
    }
    private void Refresh()
    {
        var progress = GetTimeline(this)?.Progress(CardId) ?? 1;
        var eased = 1 - Math.Pow(1 - progress, 3);
        _scale.ScaleX = _scale.ScaleY = .72 + .28 * eased;
        _offset.Y = 14 * (1 - eased);
        SetCurrentValue(OpacityProperty, .35 + .65 * eased);
        if (IsLoaded && IsVisible && progress < 1 && GetTimeline(this)?.UsesSystemClock(CardId) == true)
            _timer.Start();
        else _timer.Stop();
    }
    protected override void OnRender(DrawingContext dc) { Refresh(); base.OnRender(dc); }
}
