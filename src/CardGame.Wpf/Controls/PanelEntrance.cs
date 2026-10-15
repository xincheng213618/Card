using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CardGame.Wpf.Controls;

/// <summary>A short entrance for overlay content; its backdrop and input boundary stay immediate.</summary>
public static class PanelEntrance
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PanelEntrance), new PropertyMetadata(false, OnEnabledChanged));
    public static readonly DependencyProperty MotionEnabledProperty = DependencyProperty.RegisterAttached(
        "MotionEnabled", typeof(bool), typeof(PanelEntrance), new PropertyMetadata(true, OnMotionChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(EntranceState), typeof(PanelEntrance), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetMotionEnabled(DependencyObject element) => (bool)element.GetValue(MotionEnabledProperty);
    public static void SetMotionEnabled(DependencyObject element, bool value) => element.SetValue(MotionEnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        if (args.NewValue is true)
        {
            var state = new EntranceState(element);
            element.SetValue(StateProperty, state);
            state.Attach();
        }
        else if (element.GetValue(StateProperty) is EntranceState state)
        {
            state.Detach();
            element.ClearValue(StateProperty);
        }
    }

    private static void OnMotionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        // Turning motion back on affects the next opening, never the already visible panel.
        if (args.NewValue is false && sender.GetValue(StateProperty) is EntranceState state) state.Stop();
    }

    private sealed class EntranceState(FrameworkElement element)
    {
        private AnimationClock? _opacityClock;
        private AnimationClock? _translationClock;
        private Transform? _originalTransform;
        private TransformGroup? _entranceTransform;
        private TranslateTransform? _translation;
        private bool _presented;

        public void Attach()
        {
            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;
            element.IsVisibleChanged += OnVisibilityChanged;
            Present();
        }

        public void Detach()
        {
            element.Loaded -= OnLoaded;
            element.Unloaded -= OnUnloaded;
            element.IsVisibleChanged -= OnVisibilityChanged;
            Stop();
        }

        private void OnLoaded(object sender, RoutedEventArgs args) => Present();
        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            _presented = false;
            Stop();
        }

        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (args.NewValue is true) Present();
            else { _presented = false; Stop(); }
        }

        private void Present()
        {
            if (_presented || !element.IsLoaded || !element.IsVisible) return;
            _presented = true;
            if (!GetMotionEnabled(element) || !SystemParameters.ClientAreaAnimation ||
                System.ComponentModel.DesignerProperties.GetIsInDesignMode(element)) return;
            Stop();

            // Keep the original transform object (including bindings and its own animations).
            // The temporary translation lives after it and never alters layout.
            _originalTransform = element.RenderTransform;
            _translation = new TranslateTransform();
            _entranceTransform = new TransformGroup();
            _entranceTransform.Children.Add(_originalTransform);
            _entranceTransform.Children.Add(_translation);
            element.SetCurrentValue(UIElement.RenderTransformProperty, _entranceTransform);

            // Do not replace an opacity animation already owned by another presentation effect.
            if (!DependencyPropertyHelper.GetValueSource(element, UIElement.OpacityProperty).IsAnimated)
            {
                var opacity = element.Opacity;
                _opacityClock = CreateClock(opacity * .85, opacity);
                element.ApplyAnimationClock(UIElement.OpacityProperty, _opacityClock);
            }
            var translationClock = CreateClock(8, 0);
            _translationClock = translationClock;
            translationClock.Completed += (_, _) =>
            {
                // A retired clock must not end a newer rapid reopen.
                if (ReferenceEquals(_translationClock, translationClock)) Stop();
            };
            _translation.ApplyAnimationClock(TranslateTransform.YProperty, translationClock);
        }

        private static AnimationClock CreateClock(double from, double to) => (AnimationClock)new DoubleAnimation(from, to,
            new Duration(TimeSpan.FromMilliseconds(180)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        }.CreateClock(true);

        public void Stop()
        {
            var opacityClock = _opacityClock;
            var translationClock = _translationClock;
            _opacityClock = null;
            _translationClock = null;
            if (opacityClock is not null)
            {
                element.ApplyAnimationClock(UIElement.OpacityProperty, null);
                opacityClock.Controller?.Remove();
            }
            if (_translation is not null)
                _translation.ApplyAnimationClock(TranslateTransform.YProperty, null);
            translationClock?.Controller?.Remove();
            if (_entranceTransform is not null && ReferenceEquals(element.RenderTransform, _entranceTransform))
                element.SetCurrentValue(UIElement.RenderTransformProperty, _originalTransform);
            _originalTransform = null;
            _entranceTransform = null;
            _translation = null;
        }
    }
}
