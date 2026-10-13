using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Controls;

/// <summary>A transient, non-interactive layer. It never delays or submits a game command.</summary>
public sealed class BattleFeedbackLayer : FrameworkElement
{
    public static readonly DependencyProperty CuesProperty = DependencyProperty.Register(nameof(Cues), typeof(IEnumerable), typeof(BattleFeedbackLayer), new PropertyMetadata(null, OnCuesChanged));
    public static readonly DependencyProperty AnchorRootProperty = DependencyProperty.Register(nameof(AnchorRoot), typeof(FrameworkElement), typeof(BattleFeedbackLayer), new PropertyMetadata(null, OnAnchorRootChanged));
    public static readonly DependencyProperty HandRootProperty = DependencyProperty.Register(nameof(HandRoot), typeof(FrameworkElement), typeof(BattleFeedbackLayer), new PropertyMetadata(null, OnHandRootChanged));
    public static readonly DependencyProperty HandOwnerSeatProperty = DependencyProperty.Register(nameof(HandOwnerSeat), typeof(int), typeof(BattleFeedbackLayer), new PropertyMetadata(-1));
    public static readonly DependencyProperty CenterAnchorProperty = DependencyProperty.Register(nameof(CenterAnchor), typeof(FrameworkElement), typeof(BattleFeedbackLayer), new PropertyMetadata(null));
    public static readonly DependencyProperty DrawPileAnchorProperty = DependencyProperty.Register(nameof(DrawPileAnchor), typeof(FrameworkElement), typeof(BattleFeedbackLayer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PhaseHudProperty = DependencyProperty.Register(nameof(PhaseHud), typeof(FrameworkElement), typeof(BattleFeedbackLayer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ActionBarProperty = DependencyProperty.Register(nameof(ActionBar), typeof(FrameworkElement), typeof(BattleFeedbackLayer), new PropertyMetadata(null));
    public static readonly DependencyProperty SupplementalActionsProperty = DependencyProperty.Register(nameof(SupplementalActions), typeof(FrameworkElement), typeof(BattleFeedbackLayer), new PropertyMetadata(null));
    public static readonly DependencyProperty SidebarProperty = DependencyProperty.Register(nameof(Sidebar), typeof(FrameworkElement), typeof(BattleFeedbackLayer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MotionEnabledProperty = DependencyProperty.Register(nameof(MotionEnabled), typeof(bool), typeof(BattleFeedbackLayer), new PropertyMetadata(true, OnMotionChanged));
    public static readonly DependencyProperty PlaybackTimeScaleProperty = DependencyProperty.Register(nameof(PlaybackTimeScale), typeof(double), typeof(BattleFeedbackLayer),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender, OnPlaybackTimeScaleChanged),
        value => value is double scale && double.IsFinite(scale) && scale is >= .25 and <= 2);
    public static readonly DependencyProperty CommittedRevisionProperty = DependencyProperty.Register(nameof(CommittedRevision), typeof(long), typeof(BattleFeedbackLayer), new PropertyMetadata(0L, OnCommittedRevisionChanged));
    public static readonly DependencyProperty IsPromptVisibleProperty = DependencyProperty.Register(nameof(IsPromptVisible), typeof(bool), typeof(BattleFeedbackLayer), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SeatAnchorProperty = DependencyProperty.RegisterAttached("SeatAnchor", typeof(int), typeof(BattleFeedbackLayer), new PropertyMetadata(-1));
    public static readonly DependencyProperty PlayAnchorProperty = DependencyProperty.RegisterAttached("PlayAnchor", typeof(long), typeof(BattleFeedbackLayer), new PropertyMetadata(-1L));
    public static readonly DependencyProperty HealthAnchorProperty = DependencyProperty.RegisterAttached("HealthAnchor", typeof(int), typeof(BattleFeedbackLayer), new PropertyMetadata(-1));
    public static readonly DependencyProperty HandAnchorProperty = DependencyProperty.RegisterAttached("HandAnchor", typeof(int), typeof(BattleFeedbackLayer), new PropertyMetadata(-1));
    private static readonly DependencyPropertyKey IsFlightPendingPropertyKey = DependencyProperty.RegisterAttachedReadOnly("IsFlightPending", typeof(bool), typeof(BattleFeedbackLayer), new PropertyMetadata(false));
    public static readonly DependencyProperty IsFlightPendingProperty = IsFlightPendingPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsDeathPendingPropertyKey = DependencyProperty.RegisterAttachedReadOnly("IsDeathPending", typeof(bool), typeof(BattleFeedbackLayer), new PropertyMetadata(false));
    public static readonly DependencyProperty IsDeathPendingProperty = IsDeathPendingPropertyKey.DependencyProperty;

    private readonly List<TimedCue> _active = [];
    private const double DamageTextDuration = .72;
    internal const double DrawDuration = .85;
    internal const double DrawArrivalTime = .42;
    private readonly DispatcherTimer _timer;
    private INotifyCollectionChanged? _observed;
    private FrameworkElement? _observedHand;
    private readonly Dictionary<int, (Rect Bounds, long Revision)> _handOrigins = [];
    private readonly HashSet<FrameworkElement> _pendingPlays = [];
    private readonly HashSet<FrameworkElement> _pendingDeaths = [];
    private bool _loaded;
    private bool _hasLoaded;
    private bool _soundsEnabled;
    private static readonly Typeface Calligraphy = new("KaiTi");
    private static readonly Typeface TextTypeface = new("Microsoft YaHei");
    private static readonly Typeface RankTypeface = new(new FontFamily("Georgia"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface SuitTypeface = new("Segoe UI Symbol");
    private static readonly Brush RedCardInk = Frozen(Color.FromRgb(181, 55, 48));
    private static readonly Brush BlackCardInk = Frozen(Color.FromRgb(41, 40, 37));
    private static readonly Brush Paper = Frozen(Colors.Wheat);
    private static readonly Brush Dark = Frozen(Color.FromRgb(19, 31, 34));
    private static readonly Brush Shadow = Frozen(Color.FromArgb(150, 0, 0, 0));
    private static readonly Brush CardNameInk = Frozen(Color.FromRgb(77, 57, 35));
    private static readonly Brush SkillInk = Frozen(Color.FromRgb(255, 249, 218));
    private static readonly Brush Gold = Frozen(Color.FromRgb(255, 203, 100));

    public BattleFeedbackLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        // A public container can appear after this layer prepared its fallback
        // drawing. Refresh once when that layout changes the face handoff.
        LayoutUpdated += (_, _) => { if (UpdatePlayVisibility() | UpdateDeathVisibility()) InvalidateVisual(); };
        Loaded += (_, _) => { _hasLoaded = true; _loaded = true; Observe(); ObserveHand(); StartIfNeeded(); };
        Unloaded += (_, _) => { _loaded = false; StopAndClear(); Unobserve(); UnobserveHand(); };
    }

    public IEnumerable? Cues { get => (IEnumerable?)GetValue(CuesProperty); set => SetValue(CuesProperty, value); }
    public FrameworkElement? AnchorRoot { get => (FrameworkElement?)GetValue(AnchorRootProperty); set => SetValue(AnchorRootProperty, value); }
    public FrameworkElement? HandRoot { get => (FrameworkElement?)GetValue(HandRootProperty); set => SetValue(HandRootProperty, value); }
    public int HandOwnerSeat { get => (int)GetValue(HandOwnerSeatProperty); set => SetValue(HandOwnerSeatProperty, value); }
    public FrameworkElement? CenterAnchor { get => (FrameworkElement?)GetValue(CenterAnchorProperty); set => SetValue(CenterAnchorProperty, value); }
    public FrameworkElement? DrawPileAnchor { get => (FrameworkElement?)GetValue(DrawPileAnchorProperty); set => SetValue(DrawPileAnchorProperty, value); }
    public FrameworkElement? PhaseHud { get => (FrameworkElement?)GetValue(PhaseHudProperty); set => SetValue(PhaseHudProperty, value); }
    public FrameworkElement? ActionBar { get => (FrameworkElement?)GetValue(ActionBarProperty); set => SetValue(ActionBarProperty, value); }
    public FrameworkElement? SupplementalActions { get => (FrameworkElement?)GetValue(SupplementalActionsProperty); set => SetValue(SupplementalActionsProperty, value); }
    public FrameworkElement? Sidebar { get => (FrameworkElement?)GetValue(SidebarProperty); set => SetValue(SidebarProperty, value); }
    public bool MotionEnabled { get => (bool)GetValue(MotionEnabledProperty); set => SetValue(MotionEnabledProperty, value); }
    public double PlaybackTimeScale { get => (double)GetValue(PlaybackTimeScaleProperty); set => SetValue(PlaybackTimeScaleProperty, value); }
    public long CommittedRevision { get => (long)GetValue(CommittedRevisionProperty); set => SetValue(CommittedRevisionProperty, value); }
    public bool IsPromptVisible { get => (bool)GetValue(IsPromptVisibleProperty); set => SetValue(IsPromptVisibleProperty, value); }
    public TimeProvider Clock { get; set; } = TimeProvider.System;
    public int ActiveEffectCount => _active.Count;
    public bool IsFrameTimerRunning => _timer.IsEnabled;
    public bool IsPresentationActive => _loaded && MotionEnabled;
    public event EventHandler<GameSoundsEventArgs>? SoundsRequested;

    public void SetSoundsEnabled(bool enabled)
    {
        if (_soundsEnabled == enabled) return;
        // Resuming may only sound newly committed cues, never an interrupted beat.
        DiscardPendingSounds();
        _soundsEnabled = enabled;
    }

    public void DiscardPendingSounds()
    {
        foreach (var item in _active) item.SoundConsumed = true;
    }
    public static int GetSeatAnchor(DependencyObject element) => (int)element.GetValue(SeatAnchorProperty);
    public static void SetSeatAnchor(DependencyObject element, int value) => element.SetValue(SeatAnchorProperty, value);
    public static long GetPlayAnchor(DependencyObject element) => (long)element.GetValue(PlayAnchorProperty);
    public static void SetPlayAnchor(DependencyObject element, long value) => element.SetValue(PlayAnchorProperty, value);
    public static int GetHealthAnchor(DependencyObject element) => (int)element.GetValue(HealthAnchorProperty);
    public static void SetHealthAnchor(DependencyObject element, int value) => element.SetValue(HealthAnchorProperty, value);
    public static int GetHandAnchor(DependencyObject element) => (int)element.GetValue(HandAnchorProperty);
    public static void SetHandAnchor(DependencyObject element, int value) => element.SetValue(HandAnchorProperty, value);
    public static bool GetIsFlightPending(DependencyObject element) => (bool)element.GetValue(IsFlightPendingProperty);
    public static bool GetIsDeathPending(DependencyObject element) => (bool)element.GetValue(IsDeathPendingProperty);

    private static void OnAnchorRootChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var layer = (BattleFeedbackLayer)sender;
        layer.RevealPendingPlays();
        layer.RevealPendingDeaths();
        layer.InvalidateVisual();
    }

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
        var layer = (BattleFeedbackLayer)sender;
        if (!(bool)args.NewValue) layer.StopAndClear();
        else layer.CacheHandOrigins();
    }

    private static void OnPlaybackTimeScaleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var layer = (BattleFeedbackLayer)sender;
        var now = layer.Clock.GetTimestamp();
        var scale = (double)args.NewValue;
        for (var index = 0; index < layer._active.Count; index++)
        {
            var item = layer._active[index];
            // Keep the current visual age, including an unstarted result's delay.
            // Changing pace speeds the remaining beat without moving a face back.
            var age = layer.Clock.GetElapsedTime(item.Start, now).TotalSeconds / item.TimeScale;
            layer._active[index] = item with
            {
                Start = now - (long)Math.Round(age * scale * layer.Clock.TimestampFrequency),
                TimeScale = scale
            };
        }
        layer.RemoveExpired();
        layer.UpdatePlayVisibility();
        layer.UpdateDeathVisibility();
    }

    private static void OnHandRootChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var layer = (BattleFeedbackLayer)sender;
        layer.UnobserveHand(); layer._handOrigins.Clear();
        if (!layer._hasLoaded || layer._loaded) layer.ObserveHand();
    }

    private void ObserveHand()
    {
        if (_observedHand is not null || HandRoot is null) return;
        _observedHand = HandRoot;
        _observedHand.LayoutUpdated += OnHandLayoutUpdated;
        _observedHand.AddHandler(CardLiftSurface.LiftChangedEvent, new RoutedEventHandler(OnHandLiftChanged), true);
        CacheHandOrigins();
    }

    private void UnobserveHand()
    {
        if (_observedHand is not null) _observedHand.LayoutUpdated -= OnHandLayoutUpdated;
        if (_observedHand is not null) _observedHand.RemoveHandler(CardLiftSurface.LiftChangedEvent, new RoutedEventHandler(OnHandLiftChanged));
        _observedHand = null;
    }

    private void OnHandLayoutUpdated(object? sender, EventArgs args) => CacheHandOrigins();
    private void OnHandLiftChanged(object sender, RoutedEventArgs args) => CacheHandOrigins();

    private void CacheHandOrigins()
    {
        if (!MotionEnabled || HandRoot is null || HandRoot.Visibility != Visibility.Visible)
        {
            _handOrigins.Clear();
            return;
        }
        var viewport = Bounds(HandRoot);
        if (viewport.IsEmpty) { _handOrigins.Clear(); return; }
        Cache(HandRoot, true);
        void Cache(DependencyObject element, bool visible)
        {
            visible &= element is not UIElement { Visibility: not Visibility.Visible };
            if (element is FrameworkElement card && GetHandAnchor(card) is var id && id > 0)
            {
                var bounds = visible ? Bounds(card) : Rect.Empty;
                if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0 && viewport.Contains(Center(bounds)))
                    _handOrigins[id] = (bounds, CommittedRevision);
                else _handOrigins.Remove(id);
                return;
            }
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
                Cache(VisualTreeHelper.GetChild(element, index), visible);
        }
    }

    private static void OnCommittedRevisionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var layer = (BattleFeedbackLayer)sender;
        // A state observer may arrange the hand before this revision's public
        // cues arrive. Keep just-removed poses through that handoff, never history.
        if ((long)args.NewValue < (long)args.OldValue) layer._handOrigins.Clear();
        else
            foreach (var id in layer._handOrigins.Where(item => item.Value.Revision < (long)args.OldValue).Select(item => item.Key).ToArray())
                layer._handOrigins.Remove(id);
        layer.CacheHandOrigins();
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
        RemoveExpired();
        var now = Clock.GetTimestamp();
        foreach (var cue in args.NewItems.OfType<BattleCue>())
            if (!cue.IsInquiry || cue.Revision >= CommittedRevision)
            {
                // Public sequence orders life outcomes even when a short cue
                // batch omitted an earlier dying window. A stale completion
                // must not retire a newer outcome at the same seat.
                if (IsLifeOutcome(cue) && _active.Any(item => IsLifeOutcome(item.Cue) &&
                    SameLifeSeat(item.Cue, cue) && item.Cue.Sequence >= cue.Sequence)) continue;
                if (cue.Kind == BattleCueKind.Judgment && cue.JudgmentResolutionId is { } judgmentId)
                {
                    // A replacement and its result belong to the owning judgment,
                    // not to the replacement opportunity's separate resolution.
                    if (_active.Any(item => item.Cue.Kind == BattleCueKind.Judgment &&
                        item.Cue.JudgmentResolutionId == judgmentId && item.Cue.Sequence >= cue.Sequence)) continue;
                    _active.RemoveAll(item => item.Cue.Kind == BattleCueKind.Judgment &&
                        item.Cue.JudgmentResolutionId == judgmentId);
                }
                var delay = PresentationDelay(cue, now);
                if (IsLifeOutcome(cue))
                    _active.RemoveAll(item => IsLifeOutcome(item.Cue) && SameLifeSeat(item.Cue, cue));
                // Published progress advances one public response set. Keep the
                // paid card's flight, but replace that set's earlier count.
                if (cue.HasResponseProgress && cue.ResponseResolutionId is { } responseId)
                    _active.RemoveAll(item => item.Cue.HasResponseProgress &&
                        item.Cue.ResponseResolutionId == responseId && item.Cue.SourceSeat == cue.SourceSeat);
                // A public weapon result closes this attack's paid response set.
                // Keep its card flight and unrelated response sets on the table.
                if (cue.Kind == BattleCueKind.Weapon && !cue.IsInquiry &&
                    cue.WeaponKind is CardKind.StoneAxe or CardKind.QinglongCrescentBlade &&
                    cue.WeaponResolutionId is { } weaponId)
                    _active.RemoveAll(item => item.Cue.HasResponseProgress &&
                        item.Cue.ResponseResolutionId == weaponId && cue.TargetSeats.Contains(item.Cue.SourceSeat));
                var visibleFace = cue.HasJudgmentCard ? cue.JudgmentCard : cue.PublicCard;
                _active.Add(new(cue, now, delay, PlaybackTimeScale,
                    cue.HasJudgmentCard ? CardArt.Get(cue.JudgmentCard!.Kind) : IsCardPlay(cue) ? CardArt.Get(cue.CardKind) : null,
                    CardArt.GetRank(visibleFace?.RankText ?? string.Empty, visibleFace is { } card ? TablePlayViewModel.GetSuitGlyph(card.Suit) : string.Empty),
                    visibleFace is { } face ? CardArt.GetSuit(TablePlayViewModel.GetSuitGlyph(face.Suit)) : null, CaptureHandOrigin(cue))
                { SoundConsumed = !_soundsEnabled || !_loaded });
            }
        if (_active.Count > 24) _active.RemoveRange(0, _active.Count - 24);
        PublishDueSounds();
        InvalidateVisual();
        UpdatePlayVisibility();
        UpdateDeathVisibility();
        StartIfNeeded();
    }

    private void StartIfNeeded()
    {
        if (_loaded && MotionEnabled && _active.Count > 0) _timer.Start();
    }

    private Rect CaptureHandOrigin(BattleCue cue)
    {
        if (!IsCardPlay(cue) || cue.SourceSeat != HandOwnerSeat || cue.PublicCardId is not > 0 || HandRoot is null) return Rect.Empty;
        // State observers can remove a hand container before the accepted cue arrives.
        // Use its last arranged geometry, never retaining the private card or control.
        var viewport = Bounds(HandRoot);
        var origin = FindHandOrigin(HandRoot, cue.PublicCardId.Value);
        if (_handOrigins.Remove(cue.PublicCardId.Value, out var cached) && origin.IsEmpty) origin = cached.Bounds;
        return !viewport.IsEmpty && !origin.IsEmpty && viewport.Contains(Center(origin)) ? origin : Rect.Empty;
    }

    private Rect FindHandOrigin(DependencyObject element, int cardId)
    {
        if (element is UIElement { Visibility: not Visibility.Visible }) return Rect.Empty;
        if (element is FrameworkElement card && GetHandAnchor(card) == cardId)
        {
            var bounds = Bounds(card);
            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0) return bounds;
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var found = FindHandOrigin(VisualTreeHelper.GetChild(element, index), cardId);
            if (!found.IsEmpty) return found;
        }
        return Rect.Empty;
    }

    private void StopAndClear()
    {
        _timer.Stop();
        _active.Clear();
        _handOrigins.Clear();
        RevealPendingPlays();
        RevealPendingDeaths();
        InvalidateVisual();
    }

    private void OnTick(object? sender, EventArgs args)
    {
        RemoveExpired();
        PublishDueSounds();
        UpdatePlayVisibility();
        UpdateDeathVisibility();
        InvalidateVisual();
    }

    private void PublishDueSounds()
    {
        if (!_soundsEnabled || !IsPresentationActive) return;
        var now = Clock.GetTimestamp();
        var due = _active.Where(item => !item.SoundConsumed && VisualAge(item, now) >=
            (item.Cue.HasJudgmentCard ? BattleEffectPainter.JudgmentRevealTime : 0)).ToArray();
        if (due.Length == 0) return;
        foreach (var item in due) item.SoundConsumed = true;
        var sounds = GameSoundRules.FromPublicCues(due.Select(item => item.Cue));
        if (sounds.Count > 0) SoundsRequested?.Invoke(this, new GameSoundsEventArgs(sounds));
    }

    private void RevealPendingPlays()
    {
        foreach (var card in _pendingPlays) card.SetValue(IsFlightPendingPropertyKey, false);
        _pendingPlays.Clear();
    }

    private void RevealPendingDeaths()
    {
        foreach (var seat in _pendingDeaths) seat.SetValue(IsDeathPendingPropertyKey, false);
        _pendingDeaths.Clear();
    }

    private bool UpdateDeathVisibility()
    {
        if (_pendingDeaths.Count == 0 && !_active.Any(item => item.Cue.Kind == BattleCueKind.Death)) return false;
        var now = Clock.GetTimestamp();
        HashSet<int> waiting = MotionEnabled ? _active.Where(item => item.Cue.Kind == BattleCueKind.Death && VisualAge(item, now) < 0)
            .SelectMany(item => item.Cue.TargetSeats).ToHashSet() : [];
        if (waiting.Count == 0 || AnchorRoot is null)
        {
            var changed = _pendingDeaths.Count > 0;
            RevealPendingDeaths();
            return changed;
        }
        var pending = new HashSet<FrameworkElement>();
        Collect(AnchorRoot);
        var changedVisibility = !_pendingDeaths.SetEquals(pending);
        foreach (var seat in _pendingDeaths)
            if (!pending.Contains(seat)) seat.SetValue(IsDeathPendingPropertyKey, false);
        foreach (var seat in pending)
            if (!_pendingDeaths.Contains(seat)) seat.SetValue(IsDeathPendingPropertyKey, true);
        _pendingDeaths.Clear();
        _pendingDeaths.UnionWith(pending);
        return changedVisibility;

        void Collect(DependencyObject element)
        {
            if (element is UIElement { Visibility: not Visibility.Visible }) return;
            if (element is FrameworkElement seat && waiting.Contains(GetSeatAnchor(seat))) pending.Add(seat);
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
                Collect(VisualTreeHelper.GetChild(element, index));
        }
    }

    private bool UpdatePlayVisibility(IReadOnlyDictionary<long, FrameworkElement>? plays = null)
    {
        if (!MotionEnabled || _active.Count == 0 || AnchorRoot is null)
        {
            var hadPending = _pendingPlays.Count > 0;
            RevealPendingPlays();
            return hadPending;
        }
        if (plays is null)
        {
            var found = new Dictionary<long, FrameworkElement>();
            CollectPlayElements(AnchorRoot, found);
            plays = found;
        }
        var pending = new HashSet<FrameworkElement>();
        var now = Clock.GetTimestamp();
        var changed = false;
        foreach (var item in _active.Where(item => IsCardPlay(item.Cue)))
        {
            if (!plays.TryGetValue(item.Cue.Sequence, out var card))
            {
                // A replaced public row must not restart an old face at the center.
                if (item.HadLanding && !item.HandedOff) { item.HandedOff = true; changed = true; }
                continue;
            }
            if (!item.HadLanding) { item.HadLanding = true; changed = true; }
            var elapsed = VisualAge(item, now);
            if (!item.HandedOff && elapsed >= Duration(item.Cue.Kind) * .37) { item.HandedOff = true; changed = true; }
            if (!item.HandedOff) pending.Add(card);
        }
        foreach (var card in _pendingPlays)
            if (!pending.Contains(card)) { card.SetValue(IsFlightPendingPropertyKey, false); changed = true; }
        foreach (var card in pending)
            if (!_pendingPlays.Contains(card)) { card.SetValue(IsFlightPendingPropertyKey, true); changed = true; }
        _pendingPlays.Clear();
        _pendingPlays.UnionWith(pending);
        return changed;
    }

    private void CollectPlayElements(DependencyObject element, Dictionary<long, FrameworkElement> plays)
    {
        if (element is UIElement { Visibility: not Visibility.Visible }) return;
        if (element is FrameworkElement card && GetPlayAnchor(card) is var sequence && sequence >= 0)
        {
            var bounds = Bounds(card);
            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0) plays[sequence] = card;
            return;
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            CollectPlayElements(VisualTreeHelper.GetChild(element, index), plays);
    }

    private void RemoveExpired()
    {
        var now = Clock.GetTimestamp();
        _active.RemoveAll(cue => VisualAge(cue, now) >= Duration(cue.Cue.Kind));
        if (_active.Count == 0) { _timer.Stop(); RevealPendingPlays(); RevealPendingDeaths(); }
    }

    private double PresentationDelay(BattleCue cue, long now)
    {
        if (IsLifeOutcome(cue))
        {
            var delay = 0d;
            foreach (var previous in _active.Where(item => item.Cue.Sequence < cue.Sequence && SameLifeSeat(item.Cue, cue)))
            {
                var age = VisualAge(previous, now);
                if (cue.Kind is BattleCueKind.Dying or BattleCueKind.Death && previous.Cue.Kind == BattleCueKind.Damage)
                    delay = Math.Max(delay, BattleEffectPainter.DamageImpactTime - age);
                if (cue.Kind == BattleCueKind.Death && previous.Cue.Kind == BattleCueKind.Dying)
                    delay = Math.Max(delay, .24 - age);
                if (cue.Kind == BattleCueKind.Rescued)
                {
                    if (previous.Cue.Kind == BattleCueKind.Recovery) delay = Math.Max(delay, .28 - age);
                    if (previous.Cue.Kind == BattleCueKind.Dying && previous.Cue.LifeResolutionId == cue.LifeResolutionId)
                        delay = Math.Max(delay, -age);
                }
            }
            return Math.Max(0, delay);
        }
        if (cue.Kind == BattleCueKind.Damage)
        {
            // The paid axe announces continued resolution, not guaranteed damage.
            // Only an actual public damage cue waits for its recent axe stroke.
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var previous = _active[i];
                var weapon = previous.Cue;
                if (weapon.Sequence >= cue.Sequence || weapon.Kind != BattleCueKind.Weapon ||
                    weapon.IsInquiry || !weapon.IsWeaponActivated || weapon.WeaponKind != CardKind.StoneAxe ||
                    weapon.SourceSeat != cue.SourceSeat || !weapon.TargetSeats.Intersect(cue.TargetSeats).Any()) continue;
                var age = VisualAge(previous, now);
                if (age <= .7) return Math.Max(0, BattleEffectPainter.WeaponArrivalTime - age);
            }
        }
        if (cue.Kind == BattleCueKind.ChainPropagation && cue.OriginSeat is { } origin)
        {
            // The public relay leaves the recipient of the previous committed hit.
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var previous = _active[i];
                if (previous.Cue.Sequence >= cue.Sequence || previous.Cue.Kind != BattleCueKind.Damage ||
                    (previous.Cue.SourceSeat != cue.SourceSeat && previous.Cue.SourceSeat != -1) || previous.Cue.Nature != cue.Nature ||
                    !previous.Cue.TargetSeats.Contains(origin)) continue;
                var age = VisualAge(previous, now);
                return age > .7 ? 0 : Math.Max(0, .12 - age);
            }
            return 0;
        }
        // Give a just-declared public action time to arrive before showing its result.
        // This only schedules drawing; the accepted command and current decision are already live.
        var response = IsCardPlay(cue) && cue.CardKind == CardKind.Dodge;
        if (!response && cue.Kind is not (BattleCueKind.Damage or BattleCueKind.Recovery)) return 0;
        for (var i = _active.Count - 1; i >= 0; i--)
        {
            var previous = _active[i];
            if (previous.Cue.Sequence >= cue.Sequence) continue;
            var age = VisualAge(previous, now);
            if (age > .7) continue; // A human response must never wait for a long-finished attack.
            var card = previous.Cue;
            if (cue.Kind == BattleCueKind.Damage && card.Kind == BattleCueKind.ChainPropagation &&
                (card.SourceSeat == cue.SourceSeat || cue.SourceSeat == -1) && card.Nature == cue.Nature && card.TargetSeats.Intersect(cue.TargetSeats).Any())
                return Math.Max(0, BattleEffectPainter.ChainArrivalTime - age);
            var targetsMatch = response ? card.TargetSeats.Contains(cue.SourceSeat) :
                card.SourceSeat == cue.SourceSeat && card.TargetSeats.Intersect(cue.TargetSeats).Any();
            if (IsCardPlay(card) && card.Detail != "重铸" && targetsMatch)
                return Math.Clamp((response ? .32 : cue.Kind == BattleCueKind.Recovery ? .28 : .42) - age, 0, .5);
            if (!response && card.Kind == BattleCueKind.Skill && card.SourceSeat == cue.SourceSeat)
                return Math.Clamp(.18 - age, 0, .18);
        }
        return 0;
    }

    private double VisualAge(TimedCue item, long now) => Clock.GetElapsedTime(item.Start, now).TotalSeconds / item.TimeScale - item.Delay;

    private static double Duration(BattleCueKind kind) => kind switch
    {
        BattleCueKind.Draw => DrawDuration,
        BattleCueKind.ChainPropagation => BattleEffectPainter.ChainDuration,
        BattleCueKind.Weapon => BattleEffectPainter.WeaponDuration,
        BattleCueKind.Damage => BattleEffectPainter.DamageDuration,
        BattleCueKind.Card => .9,
        BattleCueKind.Turn => .95,
        BattleCueKind.ResponseWindow => 1.25,
        BattleCueKind.Judgment => BattleEffectPainter.JudgmentDuration,
        BattleCueKind.Dying => BattleEffectPainter.DyingDuration,
        BattleCueKind.Death => BattleEffectPainter.DeathDuration,
        BattleCueKind.Rescued => BattleEffectPainter.RescueDuration,
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
        var playElements = new Dictionary<long, FrameworkElement>();
        var health = new Dictionary<int, HealthMeter>();
        CollectAnchors(AnchorRoot, seats, plays, health, playElements);
        UpdatePlayVisibility(playElements);
        UpdateDeathVisibility();
        var centerArea = Bounds(CenterAnchor);
        var center = centerArea.IsEmpty ? new Point(ActualWidth * .5, ActualHeight * .45)
            : new Point(centerArea.X + centerArea.Width / 2, centerArea.Y + centerArea.Height * .64);
        var now = Clock.GetTimestamp();
        var sidebar = Bounds(Sidebar);
        if (!sidebar.IsEmpty)
            dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)), new RectangleGeometry(sidebar)));
        for (var i = 0; i < _active.Count; i++)
        {
            var item = _active[i];
            var elapsed = VisualAge(item, now);
            if (elapsed <= 0) continue;
            var progress = Math.Clamp(elapsed / Duration(item.Cue.Kind), 0, 1);
            if (item.Cue.HasDrawCards)
                DrawCards(dc, item.Cue, elapsed, seats, plays.Values);
            DrawBattleEffects(dc, item.Cue, progress, seats);
            DrawCue(dc, item.Cue, item.Artwork, item.RankArtwork, item.SuitArtwork, item.HandOrigin, item.HandedOff, progress, seats, plays, center, i);
        }
        DrawHealthChanges(dc, health, now);
        DrawCombatText(dc, seats, health, plays, now);
        if (!sidebar.IsEmpty) dc.Pop();
    }

    private IEnumerable<Rect> ProtectedAreas(bool includeCommandDock = true) => new[]
    {
        includeCommandDock ? Bounds(ActionBar) : Rect.Empty, Bounds(SupplementalActions), Bounds(Sidebar),
        PhaseHud is { Visibility: Visibility.Visible } ? Bounds(PhaseHud) : Rect.Empty,
        IsPromptVisible ? Bounds(CenterAnchor) : Rect.Empty
    };

    private void DrawBattleEffects(DrawingContext dc, BattleCue cue, double progress, IReadOnlyDictionary<int, Rect> seats)
    {
        var groupResponse = cue.CardKind is CardKind.ArrowBarrage or CardKind.BarbarianAssault;
        // A declaration identifies all public targets; its portrait outlines
        // remain in DrawCue. The response focus belongs to the actual public
        // inquiry, so future recipients never receive its active effect early.
        if (groupResponse && cue.Kind == BattleCueKind.Card) return;
        // Effects can cross the battlefield, but must leave decisions and command buttons readable.
        Geometry clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        foreach (var protectedArea in ProtectedAreas())
            if (!protectedArea.IsEmpty)
                clip = new CombinedGeometry(GeometryCombineMode.Exclude, clip, new RectangleGeometry(protectedArea));
        if (cue.Kind == BattleCueKind.Weapon)
            foreach (var (seat, bounds) in seats)
                if (seat != cue.SourceSeat)
                    clip = new CombinedGeometry(GeometryCombineMode.Exclude, clip, new RectangleGeometry(bounds));
        clip.Freeze();
        dc.PushClip(clip);
        if (groupResponse && cue.Kind == BattleCueKind.ResponseWindow && cue.IsInquiry && cue.Revision >= CommittedRevision)
            BattleEffectPainter.DrawGroupResponseFocus(dc, seats.GetValueOrDefault(cue.SourceSeat, Rect.Empty), progress);
        else
            BattleEffectPainter.Draw(dc, cue, progress, seats.GetValueOrDefault(cue.OriginSeat ?? cue.SourceSeat, Rect.Empty), seats);
        dc.Pop();
    }

    private void CollectAnchors(DependencyObject element, Dictionary<int, Rect> seats, Dictionary<long, Rect> plays, Dictionary<int, HealthMeter> health, Dictionary<long, FrameworkElement> playElements)
    {
        if (element is UIElement { Visibility: not Visibility.Visible }) return;
        if (element is FrameworkElement visual && GetSeatAnchor(visual) is var seat && seat >= 0)
        {
            var bounds = Bounds(visual);
            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0) seats[seat] = bounds;
        }
        if (element is HealthMeter meter && GetHealthAnchor(meter) is var healthSeat && healthSeat >= 0)
            health[healthSeat] = meter;
        if (element is FrameworkElement card && GetPlayAnchor(card) is var sequence && sequence >= 0)
        {
            var bounds = Bounds(card);
            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
            {
                plays[sequence] = bounds;
                playElements[sequence] = card;
            }
            return;
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            CollectAnchors(VisualTreeHelper.GetChild(element, index), seats, plays, health, playElements);
    }

    private void DrawHealthChanges(DrawingContext dc, IReadOnlyDictionary<int, HealthMeter> health, long now)
    {
        foreach (var (seat, meter) in health)
        {
            var latest = _active.LastOrDefault(item => item.Cue.RemainingHp is not null &&
                item.Cue.Kind is BattleCueKind.Damage or BattleCueKind.Recovery && item.Cue.TargetSeats.Contains(seat));
            if (latest is null || latest.Cue.RemainingHp != meter.Value || latest.Cue.MaximumHp != meter.Maximum) continue;
            // Only the latest public HP result belongs to the current track. A rapid
            // rescue, new maximum, or load must not redraw a stale damage remainder.
            var elapsed = VisualAge(latest, now);
            if (elapsed >= .85) continue;
            var after = meter.Fraction(meter.Value);
            var before = meter.Fraction((double)latest.Cue.RemainingHp.Value - latest.Cue.HpDelta);
            var bounds = Bounds(meter, meter.TrackBounds);
            if (bounds.IsEmpty || before == after) continue;
            if (ProtectedAreas()
                .Any(area => !area.IsEmpty && area.IntersectsWith(bounds))) continue;
            if (bounds.Width <= 0 || bounds.Height <= 0) continue;
            var damage = latest.Cue.HpDelta < 0;
            var progress = Math.Clamp((elapsed - .12) / .73, 0, 1);
            var end = damage ? after + (before - after) * Math.Pow(1 - progress, 2) : after;
            var start = damage ? after : before;
            if (end <= start) continue;
            dc.PushClip(new RectangleGeometry(bounds, 2, 2));
            dc.PushOpacity(damage ? .8 : .9 * (1 - progress));
            dc.DrawRectangle(Frozen(damage ? Color.FromRgb(255, 127, 91) : Color.FromRgb(197, 255, 207)), null,
                new Rect(bounds.Left + start * bounds.Width, bounds.Top, (end - start) * bounds.Width, bounds.Height));
            dc.Pop();
            dc.Pop();
        }
    }

    private Rect Bounds(FrameworkElement? element, Rect? localBounds = null)
    {
        if (element is null || element.ActualWidth <= 0 || element.ActualHeight <= 0) return Rect.Empty;
        try { return element.TransformToVisual(this).TransformBounds(localBounds ?? new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
        catch (InvalidOperationException) { return Rect.Empty; }
    }

    private void DrawCards(DrawingContext dc, BattleCue cue, double elapsed,
        IReadOnlyDictionary<int, Rect> seats, IEnumerable<Rect> plays)
    {
        var pile = Bounds(DrawPileAnchor);
        if (pile.IsEmpty || !seats.TryGetValue(cue.SourceSeat, out var owner) || owner.IsEmpty || elapsed >= DrawArrivalTime + .16) return;
        var artwork = new Rect(owner.Left + owner.Width * .24, owner.Top + owner.Height * .2, owner.Width * .6, owner.Height * .46);
        Geometry clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        foreach (var (seat, bounds) in seats)
        {
            var protectedPortrait = seat == cue.SourceSeat
                ? new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(bounds), new RectangleGeometry(artwork))
                : (Geometry)new RectangleGeometry(bounds);
            clip = new CombinedGeometry(GeometryCombineMode.Exclude, clip, protectedPortrait);
        }
        // A card can cross empty space in the command dock on its way to the
        // hand. Every measured command, label and readout still stays clear.
        foreach (var protectedArea in ProtectedAreas(false).Concat(plays).Concat(ReadoutAndCommandBounds(AnchorRoot, false)))
            if (!protectedArea.IsEmpty)
            {
                var padded = protectedArea; padded.Inflate(2, 2);
                clip = new CombinedGeometry(GeometryCombineMode.Exclude, clip, new RectangleGeometry(padded));
            }
        clip.Freeze();
        dc.PushClip(clip);
        var launch = Center(pile);
        var hand = cue.SourceSeat == HandOwnerSeat ? Bounds(HandRoot) : Rect.Empty;
        // The hand's top padding is below the command dock and above its faces.
        // Arrive there without masking a live command or an actual hand card.
        var destination = hand.IsEmpty
            ? new Point(Center(owner).X > ActualWidth / 2 ? owner.Left - 14 : owner.Right + 14, Center(owner).Y)
            : new Point(hand.Left + hand.Width / 2, hand.Top + 7);
        var arena = Bounds(CenterAnchor);
        var via = arena.IsEmpty ? new Point(ActualWidth * .45, ActualHeight * .45) : Center(arena);
        via.Y = Math.Max(via.Y, launch.Y + 70);
        if ((destination - launch).Length < 180)
            via = new Point((launch.X + destination.X) / 2 - 28, (launch.Y + destination.Y) / 2);
        // Only public quantity reaches this layer. The fan is bounded even for
        // large draws; no hidden physical identity or artwork is consulted.
        var count = Math.Min(3, cue.DrawCount);
        for (var index = 0; index < count; index++)
        {
            var start = .06 + index * .04;
            if (elapsed <= index * .04) continue;
            var flight = Math.Clamp((elapsed - start) / (DrawArrivalTime - start), 0, 1);
            var move = 1 - Math.Pow(1 - flight, 2);
            var point = new Point(
                (1 - move) * (1 - move) * launch.X + 2 * (1 - move) * move * via.X + move * move * destination.X,
                (1 - move) * (1 - move) * launch.Y + 2 * (1 - move) * move * via.Y + move * move * destination.Y);
            point.X += (index - (count - 1) * .5) * 8 * Math.Sin(flight * Math.PI);
            var scale = 1 - .68 * move;
            var alpha = Math.Min(1, (elapsed - index * .04) / .045) * Math.Clamp((DrawArrivalTime + .16 - elapsed) / .16, 0, 1);
            dc.PushOpacity(alpha);
            dc.PushTransform(new RotateTransform((index - (count - 1) * .5) * 7 * Math.Sin(flight * Math.PI), point.X, point.Y));
            dc.PushTransform(new ScaleTransform(scale, scale, point.X, point.Y));
            var back = new Rect(point.X - 23, point.Y - 32, 46, 64);
            dc.DrawRoundedRectangle(Frozen(Color.FromRgb(48, 39, 29)), new Pen(Gold, 1.5), back, 4, 4);
            back.Inflate(-5, -5);
            dc.DrawRoundedRectangle(null, new Pen(Gold, .8), back, 2, 2);
            var ornament = new StreamGeometry();
            using (var path = ornament.Open())
            {
                path.BeginFigure(point + new Vector(0, -12), false, true);
                path.LineTo(point + new Vector(9, 0), true, false);
                path.LineTo(point + new Vector(0, 12), true, false);
                path.LineTo(point + new Vector(-9, 0), true, false);
            }
            ornament.Freeze();
            dc.DrawGeometry(null, new Pen(Gold, 1), ornament);
            dc.Pop(); dc.Pop(); dc.Pop();
        }
        dc.Pop();
    }

    private void DrawCue(DrawingContext dc, BattleCue cue, ImageSource? artwork, ImageSource? rankArtwork, ImageSource? suitArtwork, Rect handOrigin, bool handedOff, double progress, IReadOnlyDictionary<int, Rect> seats, IReadOnlyDictionary<long, Rect> plays, Point center, int lane)
    {
        if (cue.Kind == BattleCueKind.Draw) return;
        if (cue.Kind == BattleCueKind.ChainPropagation) return; // The relay is drawn between public portraits.
        if (cue.Kind == BattleCueKind.Weapon) return; // Weapon captions share the protected text placement pass.
        if (cue.HasResponseProgress) return; // Its caption uses the text pass, leaving the portrait meter visible.
        if (IsLifeOutcome(cue)) return; // Outcome captions share protected placement with HP results.
        if (cue.Kind == BattleCueKind.Judgment) return;
        if (cue.Kind == BattleCueKind.ResponseWindow) return;
        // The current human prompt already has a persistent heading. Avoid a
        // duplicate transient inquiry covering that responder's paid effect.
        if (cue.IsInquiry && IsPromptVisible && cue.SourceSeat == HandOwnerSeat) return;
        if (cue.Kind == BattleCueKind.Damage)
        {
            progress *= BattleEffectPainter.DamageDuration / BattleEffectPainter.DamageImpactTime;
            if (progress >= 1) return;
        }
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
        // A played local face was already visible in the hand. Keep it opaque at
        // launch instead of fading through the newly shifted neighboring card.
        var alpha = (IsCardPlay(cue) && !handOrigin.IsEmpty ? 1 : Math.Min(1, progress / .09)) * Math.Min(1, (1 - progress) / .3);
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
            if (!handedOff && IsPromptVisible && !hasLanding) Badge(dc, from + new Vector(0, -25), cue.Label, ink, 20);
            else if (!handedOff || hasLanding)
            {
                var destination = hasLanding ? Center(landing) : center + new Vector(lane % 3 * 8, 0);
                var flight = Math.Clamp((progress - .03) / .34, 0, 1);
                var move = 1 - Math.Pow(1 - flight, 3);
                var launch = handOrigin.IsEmpty ? from : Center(handOrigin);
                var point = launch + (destination - launch) * move;
                point.Y -= 28 * Math.Sin(flight * Math.PI);
                var scale = handOrigin.IsEmpty ? .58 + .42 * move : 1;
                var settledSize = hasLanding ? landing.Size : new Size(86, 120);
                var size = handOrigin.IsEmpty ? settledSize : new Size(
                    handOrigin.Width + (settledSize.Width - handOrigin.Width) * move,
                    handOrigin.Height + (settledSize.Height - handOrigin.Height) * move);
                if (flight is > 0 and < 1)
                {
                    var tail = point - (destination - launch) * .09 * (1 - flight);
                    dc.PushOpacity(.3 * Math.Sin(flight * Math.PI));
                    dc.DrawLine(new Pen(ink, 12 * scale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, tail, point);
                    dc.Pop();
                }
                if (progress is > .37 and < .67)
                {
                    var settle = (progress - .37) / .3;
                    var rim = new Rect(destination.X - size.Width / 2, destination.Y - size.Height / 2, size.Width, size.Height);
                    rim.Inflate(2 + 10 * settle, 2 + 10 * settle);
                    dc.PushOpacity((1 - settle) * .7);
                    dc.DrawRoundedRectangle(null, new Pen(ink, 2), rim, 5, 5);
                    dc.Pop();
                }
                if (!handedOff)
                {
                    dc.PushTransform(new RotateTransform(Math.Sign(destination.X - launch.X) * 10 * Math.Sin(flight * Math.PI), point.X, point.Y));
                    dc.PushTransform(new ScaleTransform(scale, scale, point.X, point.Y));
                    DrawCard(dc, point, size, cue, artwork, rankArtwork, suitArtwork, ink);
                    dc.Pop();
                    dc.Pop();
                }
            }
        }
        else if (cue.Kind == BattleCueKind.Turn)
        {
            if (seats.TryGetValue(cue.SourceSeat, out var bounds)) Highlight(dc, bounds, ink, progress);
            // The turn title shares protected placement with committed draws.
        }
        else if (cue.Kind == BattleCueKind.Skill)
        {
            // Public skill names share the collision-aware text pass with HP results.
        }
        else if (cue.Kind is BattleCueKind.Response or BattleCueKind.ResponseWindow)
        {
            Badge(dc, from + new Vector(0, -28 - 25 * progress), cue.Label, ink,
                cue.Kind == BattleCueKind.ResponseWindow ? 18 : 21, cue.Detail);
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
            if (!number) Badge(dc, point + new Vector(0, -24 - 12 * progress), cue.Label, ink, 19, detail);
        }
        dc.Pop();
    }

    private static Point Center(Rect bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    private static bool IsCardPlay(BattleCue cue) => cue.Kind == BattleCueKind.Card ||
        cue.Kind == BattleCueKind.Response && (cue.CardKind is not null || cue.PublicCardId is not null);

    private static bool IsLifeOutcome(BattleCue cue) => cue.Kind is BattleCueKind.Dying or BattleCueKind.Death or BattleCueKind.Rescued;
    private static bool SameLifeSeat(BattleCue left, BattleCue right) =>
        left.TargetSeats.Intersect(right.TargetSeats).Any();

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

    private void DrawCard(DrawingContext dc, Point point, Size size, BattleCue cue, ImageSource? artwork, ImageSource? rankArtwork, ImageSource? suitArtwork, Brush accent, string? footer = null)
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
        if (cue.PublicCard is not null)
        {
            dc.PushTransform(new TranslateTransform(bounds.X + 1, bounds.Y + 1));
            dc.PushTransform(new ScaleTransform((bounds.Width - 2) / 186, (bounds.Height - 2) / 260));
            DrawCardCorner(dc, cue.PublicCard, rankArtwork, suitArtwork);
            dc.Pop(); dc.Pop();
        }
        dc.DrawRectangle(Frozen(Color.FromArgb(224, 35, 27, 19)), null, new Rect(bounds.X, bounds.Bottom, bounds.Width, 21 * size.Width / 86));
        var name = Text(footer ?? cue.ActorName + cue.CardActionLabel, 10 * size.Width / 86, accent, TextTypeface);
        name.TextAlignment = TextAlignment.Center;
        name.MaxTextWidth = size.Width - 6;
        name.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(name, new Point(bounds.X + 3, bounds.Bottom + 4));
    }

    private void DrawCardCorner(DrawingContext dc, CardSnapshot? card, ImageSource? rank, ImageSource? suit)
    {
        if (card is null) return;
        var glyph = TablePlayViewModel.GetSuitGlyph(card.Suit);
        var ink = card.Suit is Suit.Heart or Suit.Diamond ? RedCardInk : BlackCardInk;
        Draw(rank, new Rect(3, 7, 34, 31), card.RankText, RankTypeface);
        Draw(suit, new Rect(3, 38, 34, 31), glyph, SuitTypeface);
        void Draw(ImageSource? image, Rect slot, string fallback, Typeface typeface)
        {
            if (image is not null && image.Width > 0 && image.Height > 0)
            {
                var scale = Math.Min(slot.Width / image.Width, slot.Height / image.Height);
                var width = image.Width * scale; var height = image.Height * scale;
                dc.DrawImage(image, new Rect(slot.X + (slot.Width - width) / 2, slot.Y + (slot.Height - height) / 2, width, height));
            }
            else
            {
                var text = Text(fallback, 22, ink, typeface);
                dc.DrawText(text, new Point(slot.X + (slot.Width - text.Width) / 2, slot.Y));
            }
        }
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

    private void DrawCombatText(DrawingContext dc, IReadOnlyDictionary<int, Rect> seats,
        IReadOnlyDictionary<int, HealthMeter> health, IReadOnlyDictionary<long, Rect> plays, long now)
    {
        var occupied = new List<Rect>();
        foreach (var area in ProtectedAreas()
            .Concat(plays.Values)
            .Append(Bounds(DrawPileAnchor))
            .Concat(health.Values.Select(meter => Bounds(meter))))
            if (!area.IsEmpty) { var padded = area; padded.Inflate(4, 4); occupied.Add(padded); }
        // HP results have priority. A skill banner can move to leave a new result readable.
        foreach (var item in _active.Where(item => item.Cue.Kind is BattleCueKind.Damage or BattleCueKind.Recovery)
            .Concat(_active.Where(item => item.Cue.Kind == BattleCueKind.Skill))
            .Concat(_active.Where(item => item.Cue.HasResponseProgress))
            .Concat(_active.Where(item => item.Cue.Kind == BattleCueKind.Weapon && !item.Cue.IsInquiry))
            .Concat(_active.Where(item => IsLifeOutcome(item.Cue)))
            .Concat(_active.Where(item => item.Cue.Kind == BattleCueKind.Judgment))
            .Concat(_active.Where(item => item.Cue.HasDrawCards))
            .Concat(_active.Where(item => item.Cue.Kind == BattleCueKind.Turn && !IsPromptVisible))
            .Concat(_active.Where(item => item.Cue.Kind == BattleCueKind.ResponseWindow && !item.Cue.HasResponseProgress &&
                (!item.Cue.IsInquiry || !IsPromptVisible || item.Cue.SourceSeat != HandOwnerSeat))))
        {
            var elapsed = VisualAge(item, now);
            if (elapsed <= 0) continue;
            var textDuration = item.Cue.Kind == BattleCueKind.Damage ? DamageTextDuration : Duration(item.Cue.Kind);
            if (elapsed >= textDuration) continue;
            var progress = Math.Clamp(elapsed / textDuration, 0, 1);
            var seat = item.Cue.Kind is BattleCueKind.Skill or BattleCueKind.Weapon || item.Cue.HasResponseProgress ||
                (item.Cue.IsInquiry && item.Cue.Kind != BattleCueKind.Judgment)
                ? item.Cue.SourceSeat : item.Cue.TargetSeats.FirstOrDefault(item.Cue.SourceSeat);
            if (!seats.TryGetValue(seat, out var owner))
            {
                if (item.Cue.Kind != BattleCueKind.ResponseWindow || item.Cue.IsInquiry) continue;
                owner = Bounds(CenterAnchor);
                if (owner.IsEmpty) owner = new Rect(ActualWidth * .5, ActualHeight * .45, 1, 1);
            }
            if (item.Cue.HasJudgmentCard) JudgmentPanel(dc, item, owner, elapsed, occupied, seats.Values);
            else if (item.Cue.Kind == BattleCueKind.Skill) SkillBanner(dc, item, owner, progress, occupied);
            else if (item.Cue.HasResponseProgress || item.Cue.Kind is BattleCueKind.Weapon or BattleCueKind.Judgment or BattleCueKind.ResponseWindow or BattleCueKind.Draw or BattleCueKind.Turn || item.Cue.IsInquiry || IsLifeOutcome(item.Cue))
                ContinuationCaption(dc, item, owner, progress, occupied, seats.Values);
            else Amount(dc, item, owner, progress, occupied);
        }
    }

    private void JudgmentPanel(DrawingContext dc, TimedCue item, Rect owner, double elapsed,
        List<Rect> occupied, IEnumerable<Rect> portraits)
    {
        var cue = item.Cue;
        var face = cue.JudgmentCard!;
        var title = Text(cue.JudgmentTitle ?? "判定", 20, Gold, Calligraphy);
        title.MaxTextWidth = 195; title.MaxLineCount = 1; title.Trimming = TextTrimming.CharacterEllipsis;
        var actor = Text(cue.ActorName, 11, Gold, TextTypeface);
        actor.MaxTextWidth = 195; actor.MaxLineCount = 1; actor.Trimming = TextTrimming.CharacterEllipsis;
        var result = Text(cue.Detail ?? "判定结束", 12, Paper, TextTypeface);
        result.MaxTextWidth = 195; result.MaxLineCount = 2; result.Trimming = TextTrimming.CharacterEllipsis;
        var faceLabel = Text("判定花色 " + TablePlayViewModel.GetSuitGlyph(face.Suit) + face.RankText, 12, Gold, TextTypeface);
        var waiting = Text("翻开判定牌", 12, Gold, TextTypeface);
        var width = Math.Max(actor.Width, Math.Max(Math.Max(title.Width, result.Width), Math.Max(faceLabel.Width, waiting.Width)));
        var size = new Size(width + 102, Math.Max(126, title.Height + actor.Height + result.Height + faceLabel.Height + 38));
        var obstacles = occupied.Concat(portraits).Concat(ReadoutAndCommandBounds(AnchorRoot)).ToArray();
        if (!PlaceText(item, new Point(owner.Left + owner.Width / 2, owner.Top - size.Height / 2 - 6),
            size, obstacles, out var bounds)) return;
        occupied.Add(bounds);
        var progress = elapsed / BattleEffectPainter.JudgmentDuration;
        dc.PushOpacity(Math.Min(1, elapsed / .045) * Math.Min(1, (1 - progress) / .3));
        dc.DrawRoundedRectangle(Dark, new Pen(Gold, 1), bounds, 6, 6);
        var point = new Point(bounds.Left + 41, bounds.Top + 57);
        var cardSize = new Size(66, 92);
        var flip = Math.Clamp(elapsed / BattleEffectPainter.JudgmentRevealTime, 0, 1);
        var horizontalScale = Math.Max(.035, Math.Abs(Math.Cos(Math.PI * flip)));
        dc.PushTransform(new ScaleTransform(horizontalScale, 1, point.X, point.Y));
        if (flip < .5)
        {
            var back = new Rect(point.X - cardSize.Width / 2, point.Y - cardSize.Height / 2, cardSize.Width, cardSize.Height);
            dc.DrawRoundedRectangle(Frozen(Color.FromRgb(48, 39, 29)), new Pen(Gold, 2), back, 5, 5);
            back.Inflate(-6, -6);
            dc.DrawRoundedRectangle(null, new Pen(Gold, 1), back, 3, 3);
            var glyph = Text("判", 27, Gold, Calligraphy);
            dc.DrawText(glyph, new Point(point.X - glyph.Width / 2, point.Y - glyph.Height / 2));
        }
        else
        {
            // This is the published effective judgment face. The explicit footer
            // keeps suit-changing skills from masquerading as a printed card.
            var display = cue with { CardKind = face.Kind, PublicCard = face, Label = face.DisplayName };
            DrawCard(dc, point, cardSize, display, item.Artwork, item.RankArtwork, item.SuitArtwork, Gold, "判定花色");
        }
        dc.Pop();
        var textLeft = bounds.Left + 89;
        dc.DrawText(title, new Point(textLeft, bounds.Top + 12));
        dc.DrawText(actor, new Point(textLeft, bounds.Top + 15 + title.Height));
        if (elapsed >= BattleEffectPainter.JudgmentRevealTime)
        {
            dc.DrawText(faceLabel, new Point(textLeft, bounds.Top + 20 + title.Height + actor.Height));
            dc.DrawText(result, new Point(textLeft, bounds.Top + 28 + title.Height + actor.Height + faceLabel.Height));
        }
        else dc.DrawText(waiting, new Point(textLeft, bounds.Top + 24 + title.Height + actor.Height));
        dc.Pop();
    }

    private void ContinuationCaption(DrawingContext dc, TimedCue item, Rect owner, double progress,
        List<Rect> occupied, IEnumerable<Rect> portraits)
    {
        var accent = item.Cue.Kind switch
        {
            BattleCueKind.Weapon => Gold,
            BattleCueKind.Judgment => Gold,
            BattleCueKind.Draw => Gold,
            BattleCueKind.Turn => Gold,
            BattleCueKind.Dying => Frozen(Color.FromRgb(232, 145, 120)),
            BattleCueKind.Death => Frozen(Color.FromRgb(196, 181, 155)),
            BattleCueKind.Rescued => Frozen(Color.FromRgb(145, 227, 170)),
            _ => Frozen(Color.FromRgb(125, 190, 205))
        };
        var draw = item.Cue.HasDrawCards;
        var title = Text(item.Cue.Label, draw || item.Cue.Kind == BattleCueKind.Turn ? 24 : 18, accent, Calligraphy);
        title.MaxTextWidth = Math.Min(320, Math.Max(60, ActualWidth - 48));
        title.MaxLineCount = 1; title.Trimming = TextTrimming.CharacterEllipsis;
        var detail = Text(item.Cue.Detail ?? (IsLifeOutcome(item.Cue) || item.Cue.Kind is BattleCueKind.Draw or BattleCueKind.Turn ? item.Cue.ActorName : string.Empty), 11, accent, TextTypeface);
        detail.MaxTextWidth = title.MaxTextWidth;
        detail.MaxLineCount = 1; detail.Trimming = TextTrimming.CharacterEllipsis;
        var size = new Size(Math.Max(draw ? 150 : 0, Math.Max(title.Width, detail.Width) + 26),
            Math.Max(draw ? 62 : 0, title.Height + detail.Height + 19));
        // Keep the whole portrait clear, including the just-paid sidestep and
        // public meter. Use the same placement contract as damage/skill text.
        var obstacles = occupied.Concat(portraits).Concat(ReadoutAndCommandBounds(AnchorRoot)).ToArray();
        if (!PlaceText(item, new Point(owner.Left + owner.Width / 2, owner.Top - size.Height / 2 - 6),
            size, obstacles, out var bounds)) return;
        occupied.Add(bounds);
        dc.PushOpacity(Math.Min(1, progress / .09) * Math.Min(1, (1 - progress) / .3));
        dc.DrawRoundedRectangle(draw ? Frozen(Color.FromRgb(64, 50, 34)) : Dark, new Pen(accent, draw ? 1.5 : 1), bounds, 5, 5);
        dc.DrawText(title, new Point(bounds.Left + (bounds.Width - title.Width) / 2, bounds.Top + 6));
        dc.DrawText(detail, new Point(bounds.Left + (bounds.Width - detail.Width) / 2, bounds.Top + title.Height + 9));
        dc.Pop();
    }

    private IEnumerable<Rect> ReadoutAndCommandBounds(DependencyObject? element, bool includeSeatButtons = true)
    {
        if (element is null || element is UIElement { Visibility: not Visibility.Visible }) yield break;
        if (element is System.Windows.Controls.TextBlock || element is System.Windows.Controls.Primitives.ButtonBase &&
            (includeSeatButtons || GetSeatAnchor(element) < 0))
        {
            var bounds = Bounds((FrameworkElement)element);
            if (!bounds.IsEmpty) yield return bounds;
            yield break;
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            foreach (var bounds in ReadoutAndCommandBounds(VisualTreeHelper.GetChild(element, index), includeSeatButtons)) yield return bounds;
    }

    private bool PlaceText(TimedCue item, Point owner, Size size, IReadOnlyList<Rect> occupied, out Rect bounds)
    {
        var viewport = new Rect(6, 6, Math.Max(0, ActualWidth - 12), Math.Max(0, ActualHeight - 12));
        bounds = item.TextOffset is { } offset ? new Rect(owner + offset, size) : Rect.Empty;
        if (!bounds.IsEmpty && BattleTextLayout.Fits(bounds, viewport, occupied)) return true;
        if (!BattleTextLayout.TryPlace(size, owner, viewport, occupied, out bounds)) return false;
        item.TextOffset = bounds.TopLeft - owner;
        return true;
    }

    private void Amount(DrawingContext dc, TimedCue item, Rect owner, double progress, List<Rect> occupied)
    {
        var cue = item.Cue;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var textCache = item.AmountText;
        if (textCache is null || textCache.PixelsPerDip != pixelsPerDip)
            item.AmountText = textCache = new AmountTextCache(cue, pixelsPerDip);
        var accent = textCache.Accent;
        var isDamage = cue.Kind == BattleCueKind.Damage;
        var travel = isDamage ? 24 : 48;
        var caption = textCache.Caption;
        FormattedText? number = null;
        AmountNumberText? numberText = null;
        var bounds = Rect.Empty;
        var baseFontSize = owner.Width >= 150 ? (isDamage ? 40 : 48) : (isDamage ? 32 : 36);
        var initialSize = item.TextFontSize ?? (cue.Label.Length > 5 ? 24 : baseFontSize);
        foreach (var fontSize in new[] { initialSize, 28d, 22d }.Distinct().Where(size => size <= initialSize))
        {
            numberText = textCache.GetNumber(fontSize);
            number = numberText.Text;
            // Reserve the complete pop and upward trajectory, so subsequent frames keep their position.
            var size = new Size(Math.Max(number.Width, caption.Width) * 1.2 + 10,
                (number.Height + caption.Height + 4) * 1.2 + travel + 10);
            if (PlaceText(item, Center(owner) + new Vector(0, isDamage ? -10 : -28), size, occupied, out bounds))
            { item.TextFontSize = fontSize; break; }
        }
        if (bounds.IsEmpty || number is null || numberText is null) return;
        occupied.Add(bounds);
        var point = new Point(bounds.X + bounds.Width / 2, bounds.Top + 5 + travel + number.Height * .6 - travel * (1 - Math.Pow(1 - progress, 2)));
        var scale = progress < .16 ? .8 + .4 * Math.Sin(progress / .16 * Math.PI / 2) :
            1 + .2 * Math.Pow(Math.Max(0, 1 - (progress - .16) / .2), 2);
        var fadeIn = isDamage ? .09 / DamageTextDuration : .09;
        var fadeOut = isDamage ? .4 / DamageTextDuration : .3;
        dc.PushOpacity(Math.Min(1, progress / fadeIn) * Math.Min(1, (1 - progress) / fadeOut));
        dc.PushTransform(new ScaleTransform(scale, scale, point.X, point.Y));
        // Keep the translated geometry's bounds in scene coordinates while
        // reusing the frozen glyph contours and retaining the current pop scale.
        var outline = new GeometryGroup
        {
            FillRule = FillRule.Nonzero,
            Transform = new TranslateTransform(point.X - number.Width / 2, point.Y - number.Height / 2)
        };
        outline.Children.Add(numberText.Outline);
        outline.Freeze();
        dc.DrawGeometry(Shadow, new Pen(Dark, 5) { LineJoin = PenLineJoin.Round }, outline);
        dc.DrawGeometry(accent, null, outline);
        var below = new Point(point.X - caption.Width / 2, point.Y + number.Height / 2 + 1);
        dc.DrawText(caption, below);
        dc.Pop();
        dc.Pop();
    }

    private void SkillBanner(DrawingContext dc, TimedCue item, Rect owner, double progress, List<Rect> occupied)
    {
        var title = Text(item.Cue.Label, item.Cue.Label.Length > 6 ? 26 : 32, SkillInk, Calligraphy);
        title.MaxTextWidth = Math.Min(220, Math.Max(60, ActualWidth - 48));
        title.MaxLineCount = 2; title.Trimming = TextTrimming.CharacterEllipsis;
        var caption = Text(item.Cue.ActorName + " · 技能发动", 11, Gold, TextTypeface);
        caption.MaxTextWidth = title.MaxTextWidth; caption.MaxLineCount = 1; caption.Trimming = TextTrimming.CharacterEllipsis;
        var size = new Size(Math.Max(130, Math.Max(title.Width, caption.Width) + 32), title.Height + caption.Height + 25);
        if (!PlaceText(item, Center(owner), size, occupied, out var bounds)) return;
        occupied.Add(bounds);
        dc.PushOpacity(Math.Min(1, progress / .08) * Math.Min(1, (1 - progress) / .2));
        dc.DrawRoundedRectangle(Shadow, null, new Rect(bounds.X + 2, bounds.Y + 3, bounds.Width, bounds.Height), 4, 4);
        dc.DrawRoundedRectangle(Dark, new Pen(Gold, 1), bounds, 4, 4);
        dc.DrawLine(new Pen(Gold, 1.5), new Point(bounds.Left + 12, bounds.Top + 5), new Point(bounds.Right - 12, bounds.Top + 5));
        dc.DrawText(title, new Point(bounds.X + (bounds.Width - title.Width) / 2, bounds.Top + 10));
        dc.DrawText(caption, new Point(bounds.X + (bounds.Width - caption.Width) / 2, bounds.Top + title.Height + 14));
        dc.Pop();
    }

    private FormattedText Text(string text, double size, Brush brush, Typeface font) =>
        new(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, font, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private sealed class AmountTextCache
    {
        private readonly string _numberText;
        private readonly Dictionary<double, AmountNumberText> _numbers = [];
        public double PixelsPerDip { get; }
        public Brush Accent { get; }
        public FormattedText Caption { get; }

        public AmountTextCache(BattleCue cue, double pixelsPerDip)
        {
            PixelsPerDip = pixelsPerDip;
            _numberText = cue.Label;
            Accent = Frozen(cue.Kind == BattleCueKind.Recovery ? Color.FromRgb(145, 227, 170) :
                cue.Nature == DamageNature.Thunder ? Color.FromRgb(179, 173, 255) : Color.FromRgb(255, 126, 99));
            var detail = cue.Kind == BattleCueKind.Recovery ? "回复体力" : cue.Nature switch
            { DamageNature.Fire => "火焰伤害", DamageNature.Thunder => "雷电伤害", _ => "受到伤害" };
            Caption = CreateText(cue.Kind == BattleCueKind.Damage ? detail : cue.ActorName + " · " + detail,
                11, Paper, 140);
        }

        public AmountNumberText GetNumber(double fontSize)
        {
            if (!_numbers.TryGetValue(fontSize, out var number))
                _numbers[fontSize] = number = new(CreateText(_numberText, fontSize, Accent, 160));
            return number;
        }

        private FormattedText CreateText(string text, double size, Brush brush, double maxWidth) =>
            new(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                TextTypeface, size, brush, PixelsPerDip)
            {
                MaxTextWidth = maxWidth,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };
    }

    private sealed class AmountNumberText(FormattedText text)
    {
        private Geometry? _outline;
        public FormattedText Text { get; } = text;
        public Geometry Outline
        {
            get
            {
                if (_outline is not null) return _outline;
                var outline = Text.BuildGeometry(new Point(0, 0));
                outline.Freeze();
                return _outline = outline;
            }
        }
    }

    private sealed record TimedCue(BattleCue Cue, long Start, double Delay, double TimeScale, ImageSource? Artwork, ImageSource? RankArtwork, ImageSource? SuitArtwork, Rect HandOrigin)
    {
        // Visual placement belongs to this transient cue and is discarded on expiry/reset/unload.
        public Vector? TextOffset { get; set; }
        public double? TextFontSize { get; set; }
        public AmountTextCache? AmountText { get; set; }
        public bool HadLanding { get; set; }
        public bool HandedOff { get; set; }
        public bool SoundConsumed { get; set; }
    }
}
