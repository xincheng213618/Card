using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CardGame.Wpf.Controls;

/// <summary>Animate hand artwork lift, reflow and arrival without changing its layout slot.</summary>
public sealed class CardLiftSurface : Grid
{
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(CardLiftSurface), new PropertyMetadata(false, OnLiftStateChanged));
    public static readonly DependencyProperty IsPointerOverProperty = DependencyProperty.Register(
        nameof(IsPointerOver), typeof(bool), typeof(CardLiftSurface), new PropertyMetadata(false, OnLiftStateChanged));
    public static readonly DependencyProperty MotionEnabledProperty = DependencyProperty.Register(
        nameof(MotionEnabled), typeof(bool), typeof(CardLiftSurface), new PropertyMetadata(true, OnLiftStateChanged));
    public static readonly RoutedEvent LiftChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(LiftChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CardLiftSurface));

    private const double LiftDuration = .14;
    private const double ReflowDuration = .18;
    internal const double ArrivalStartTime = .42;
    internal const double ArrivalDuration = .18;
    private readonly TranslateTransform _lift = new();
    private readonly SolidColorBrush _arrivalMask = new(Colors.White);
    private readonly DispatcherTimer _timer;
    private TimeProvider _clock = TimeProvider.System;
    private long _started;
    private double _from;
    private double _target;
    private double _liftElapsed;
    private double _liftY;
    private bool _animating;
    private long _reflowStarted;
    private double _reflowFrom;
    private double _reflowX;
    private double _reflowElapsed;
    private bool _reflowAnimating;
    private long _arrivalStarted;
    private double _arrivalElapsed;
    private double _arrivalDelay;
    private double _arrivalTimeScale = 1;
    private Vector _arrivalFrom;
    private Vector _arrivalEntry;
    private double _arrivalRetargetProgress;
    private HandArrivalBatch? _arrivalBatch;
    private double _arrivalX;
    private double _arrivalY;
    private bool _arrivalAnimating;
    private bool _refreshing;
    private bool _loaded;
    private bool _retired;

    public CardLiftSurface()
    {
        RenderTransform = _lift;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => RefreshMotion();
        Loaded += (_, _) =>
        {
            var wasRetired = _retired;
            _loaded = true;
            _retired = false;
            if (wasRetired) { FinishArrival(); FinishAt(TargetLift); FinishReflow(); }
            else RefreshMotion();
            StartIfNeeded();
        };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            _timer.Stop();
            // ItemsControl.Move can deliver Unloaded after reinserting the
            // same container in its live hand. Its new reflow/arrival is still
            // current; only a real removal freezes the last public-use origin.
            for (DependencyObject? ancestor = VisualTreeHelper.GetParent(this); ancestor is not null;
                 ancestor = VisualTreeHelper.GetParent(ancestor))
                if (ancestor is HandPanel { IsLoaded: true }) return;
            // A committed hand removal precedes its public cue. Keep the last
            // visible pose, and do not bubble a change which would replace that origin.
            _retired = true;
            _animating = false;
            _reflowAnimating = false;
            _arrivalAnimating = false;
            _arrivalBatch = null;
        };
    }

    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public bool IsPointerOver { get => (bool)GetValue(IsPointerOverProperty); set => SetValue(IsPointerOverProperty, value); }
    public bool MotionEnabled { get => (bool)GetValue(MotionEnabledProperty); set => SetValue(MotionEnabledProperty, value); }
    public event RoutedEventHandler LiftChanged
    {
        add => AddHandler(LiftChangedEvent, value);
        remove => RemoveHandler(LiftChangedEvent, value);
    }
    public TimeProvider Clock
    {
        get => _clock;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_clock, value)) return;
            RefreshMotion();
            var liftElapsed = _animating ? _liftElapsed + _clock.GetElapsedTime(_started).TotalSeconds : 0;
            var reflowElapsed = _reflowAnimating ? _reflowElapsed + _clock.GetElapsedTime(_reflowStarted).TotalSeconds : 0;
            var arrivalElapsed = _arrivalAnimating ? _arrivalElapsed + _clock.GetElapsedTime(_arrivalStarted).TotalSeconds / _arrivalTimeScale : 0;
            _clock = value;
            _arrivalBatch?.SetFallbackClock(value);
            _liftElapsed = liftElapsed;
            _reflowElapsed = reflowElapsed;
            _arrivalElapsed = arrivalElapsed;
            _started = _clock.GetTimestamp();
            _reflowStarted = _started;
            _arrivalStarted = _started;
            _timer.Stop();
            StartIfNeeded();
        }
    }

    internal bool IsLiftAnimating => _animating;
    internal bool IsReflowAnimating => _reflowAnimating;
    internal bool IsArrivalAnimating => _arrivalAnimating;
    internal double HorizontalOffset { get { RefreshMotion(); return _lift.X; } }
    internal double ReflowOffset { get { RefreshMotion(); return _reflowX; } }
    internal double ArrivalOpacity { get { RefreshMotion(); return ReferenceEquals(OpacityMask, _arrivalMask) ? _arrivalMask.Opacity : 1; } }
    internal bool IsFrameTimerRunning => _timer.IsEnabled;
    private double TargetLift => IsSelected ? -18 : IsPointerOver ? -7 : 0;

    private static void OnLiftStateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var surface = (CardLiftSurface)sender;
        surface.RefreshMotion();
        var target = surface.TargetLift;
        if (!surface.MotionEnabled || DesignerProperties.GetIsInDesignMode(surface))
        {
            surface.FinishAt(target);
            surface.FinishReflow();
            surface.FinishArrival();
            return;
        }
        if (surface._retired) return;
        if (target == surface._target && (surface._animating || target == surface._liftY)) return;
        surface._from = surface._liftY;
        surface._target = target;
        surface._liftElapsed = 0;
        surface._started = surface.Clock.GetTimestamp();
        surface._animating = surface._from != target;
        surface.InvalidateVisual();
        surface.StartIfNeeded();
    }

    internal void RefreshMotion()
    {
        if (_refreshing || !_animating && !_reflowAnimating && !_arrivalAnimating) return;
        _refreshing = true;
        try
        {
            if (_retired) return;
            if (!MotionEnabled || DesignerProperties.GetIsInDesignMode(this))
            {
                FinishAt(TargetLift);
                FinishReflow();
                FinishArrival();
                return;
            }
            if (_animating)
            {
                var target = _target;
                var progress = Math.Clamp((_liftElapsed + Clock.GetElapsedTime(_started).TotalSeconds) / LiftDuration, 0, 1);
                var eased = 1 - Math.Pow(1 - progress, 3);
                SetLift(_from + (target - _from) * eased);
                // A pose observer can change hover while following X motion.
                // Let that new lift continue from this frame's visible pose.
                if (progress >= 1 && _target == target) FinishAt(target);
            }
            if (_reflowAnimating)
            {
                var progress = Math.Clamp((_reflowElapsed + Clock.GetElapsedTime(_reflowStarted).TotalSeconds) / ReflowDuration, 0, 1);
                var eased = 1 - Math.Pow(1 - progress, 3);
                SetHorizontal(_reflowFrom * (1 - eased));
                if (progress >= 1) FinishReflow();
            }
            if (_arrivalAnimating)
            {
                var elapsed = _arrivalElapsed + Clock.GetElapsedTime(_arrivalStarted).TotalSeconds / _arrivalTimeScale;
                var progress = Math.Clamp((_arrivalBatch?.ElapsedAfterArrival ?? elapsed - _arrivalDelay) / ArrivalDuration, 0, 1);
                var eased = 1 - Math.Pow(1 - progress, 3);
                var remaining = Math.Clamp((progress - _arrivalRetargetProgress) / (1 - _arrivalRetargetProgress), 0, 1);
                SetArrivalOffset(_arrivalFrom * Math.Pow(1 - remaining, 3));
                SetArrivalOpacity(eased);
                if (progress >= 1) FinishArrival();
            }
            StartIfNeeded();
        }
        finally { _refreshing = false; }
    }

    internal void ReflowFrom(double displacement)
    {
        RefreshMotion();
        if (_retired) return;
        if (!MotionEnabled || !double.IsFinite(displacement) || DesignerProperties.GetIsInDesignMode(this))
        {
            FinishReflow();
            return;
        }
        _reflowFrom = displacement;
        _reflowElapsed = 0;
        _reflowStarted = Clock.GetTimestamp();
        _reflowAnimating = displacement != 0;
        SetHorizontal(displacement);
        InvalidateVisual();
        StartIfNeeded();
    }

    internal void FinishReflow()
    {
        _reflowAnimating = false;
        _reflowElapsed = 0;
        _reflowFrom = 0;
        SetHorizontal(0);
        StartIfNeeded();
    }

    internal void BeginArrival(Vector entryOffset, double timeScale, HandArrivalBatch? batch = null)
    {
        RefreshMotion();
        if (_retired) return;
        if (!MotionEnabled || DesignerProperties.GetIsInDesignMode(this))
        {
            FinishArrival();
            return;
        }
        _arrivalElapsed = 0;
        _arrivalDelay = ArrivalStartTime;
        _arrivalTimeScale = timeScale;
        _arrivalFrom = entryOffset;
        _arrivalEntry = entryOffset;
        _arrivalRetargetProgress = 0;
        _arrivalBatch = batch;
        batch?.Activate();
        _arrivalStarted = Clock.GetTimestamp();
        _arrivalAnimating = true;
        SetArrivalOpacity(0);
        if (OpacityMask is null) SetCurrentValue(OpacityMaskProperty, _arrivalMask);
        SetArrivalOffset(entryOffset);
        RefreshMotion();
        InvalidateVisual();
        StartIfNeeded();
    }

    internal void RetargetArrival(Vector entryOffset, Vector slotDisplacement)
    {
        if (!_arrivalAnimating || (_arrivalEntry - entryOffset).Length < .01 && slotDisplacement.Length < .01) return;
        RefreshMotion();
        if (!_arrivalAnimating) return;
        var elapsed = _arrivalBatch?.ElapsedAfterArrival ??
            _arrivalElapsed + Clock.GetElapsedTime(_arrivalStarted).TotalSeconds / _arrivalTimeScale - _arrivalDelay;
        _arrivalRetargetProgress = Math.Clamp(elapsed / ArrivalDuration, 0, .999999);
        _arrivalFrom = elapsed <= 0 ? entryOffset : new Vector(_arrivalX + _reflowX, _arrivalY) + slotDisplacement;
        _arrivalEntry = entryOffset;
        FinishReflow();
        SetArrivalOffset(_arrivalFrom);
        InvalidateVisual();
    }

    internal void FinishArrival()
    {
        _arrivalAnimating = false;
        _arrivalElapsed = 0;
        _arrivalDelay = 0;
        _arrivalBatch = null;
        _arrivalRetargetProgress = 0;
        SetArrivalOffset(new Vector());
        SetArrivalOpacity(1);
        if (ReferenceEquals(OpacityMask, _arrivalMask)) SetCurrentValue(OpacityMaskProperty, null);
        StartIfNeeded();
    }

    internal void SetArrivalTimeScale(double timeScale)
    {
        _arrivalBatch?.SetTimeScale(timeScale);
        if (_arrivalTimeScale == timeScale) return;
        RefreshMotion();
        if (_arrivalAnimating)
        {
            _arrivalElapsed += Clock.GetElapsedTime(_arrivalStarted).TotalSeconds / _arrivalTimeScale;
            _arrivalStarted = Clock.GetTimestamp();
        }
        _arrivalTimeScale = timeScale;
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Offscreen rendering can advance an injected clock without a running dispatcher timer.
        RefreshMotion();
        base.OnRender(dc);
    }

    private void StartIfNeeded()
    {
        if ((_animating || _reflowAnimating || _arrivalAnimating) && _loaded && !_retired && MotionEnabled && ReferenceEquals(Clock, TimeProvider.System) &&
            !DesignerProperties.GetIsInDesignMode(this)) _timer.Start();
        else _timer.Stop();
    }

    private void FinishAt(double target)
    {
        _animating = false;
        _target = target;
        _liftElapsed = 0;
        SetLift(target);
        StartIfNeeded();
    }

    private void SetLift(double value)
    {
        if (_liftY == value) return;
        _liftY = value;
        UpdateVertical();
    }

    private void SetArrivalOffset(Vector value)
    {
        if (_arrivalX == value.X && _arrivalY == value.Y) return;
        _arrivalX = value.X;
        _arrivalY = value.Y;
        UpdateHorizontal();
        UpdateVertical();
    }

    private void UpdateVertical()
    {
        var value = Math.Max(-18, _liftY) + _arrivalY;
        if (_lift.Y == value) return;
        _lift.Y = value;
        RaiseEvent(new RoutedEventArgs(LiftChangedEvent, this));
    }

    private void SetArrivalOpacity(double value)
    {
        if (_arrivalMask.Opacity == value) return;
        _arrivalMask.Opacity = value;
        InvalidateVisual();
    }

    private void SetHorizontal(double value)
    {
        if (_reflowX == value) return;
        _reflowX = value;
        UpdateHorizontal();
    }

    private void UpdateHorizontal()
    {
        var value = _reflowX + _arrivalX;
        if (_lift.X == value) return;
        _lift.X = value;
        RaiseEvent(new RoutedEventArgs(LiftChangedEvent, this));
    }
}
