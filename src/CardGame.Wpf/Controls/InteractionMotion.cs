using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CardGame.Wpf.Controls;

/// <summary>Moves only a button's template artwork; its layout and input bounds stay fixed.</summary>
public static class InteractionMotion
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(InteractionMotion), new PropertyMetadata(false, EnabledChanged));
    public static readonly DependencyProperty MotionEnabledProperty = DependencyProperty.RegisterAttached(
        "MotionEnabled", typeof(bool), typeof(InteractionMotion),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, StateChanged));
    public static readonly DependencyProperty EnableHoverProperty = DependencyProperty.RegisterAttached(
        "EnableHover", typeof(bool), typeof(InteractionMotion), new PropertyMetadata(true, StateChanged));
    public static readonly DependencyProperty HoverOffsetProperty = DependencyProperty.RegisterAttached(
        "HoverOffset", typeof(double), typeof(InteractionMotion), new PropertyMetadata(-2d, StateChanged));
    public static readonly DependencyProperty PressedScaleProperty = DependencyProperty.RegisterAttached(
        "PressedScale", typeof(double), typeof(InteractionMotion), new PropertyMetadata(.985d, StateChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(MotionState), typeof(InteractionMotion));

    public static bool GetIsEnabled(DependencyObject value) => (bool)value.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject value, bool enabled) => value.SetValue(IsEnabledProperty, enabled);
    public static bool GetMotionEnabled(DependencyObject value) => (bool)value.GetValue(MotionEnabledProperty);
    public static void SetMotionEnabled(DependencyObject value, bool enabled) => value.SetValue(MotionEnabledProperty, enabled);
    public static bool GetEnableHover(DependencyObject value) => (bool)value.GetValue(EnableHoverProperty);
    public static void SetEnableHover(DependencyObject value, bool enabled) => value.SetValue(EnableHoverProperty, enabled);
    public static double GetHoverOffset(DependencyObject value) => (double)value.GetValue(HoverOffsetProperty);
    public static void SetHoverOffset(DependencyObject value, double offset) => value.SetValue(HoverOffsetProperty, offset);
    public static double GetPressedScale(DependencyObject value) => (double)value.GetValue(PressedScaleProperty);
    public static void SetPressedScale(DependencyObject value, double scale) => value.SetValue(PressedScaleProperty, scale);

    private static void EnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        if (element.GetValue(StateProperty) is MotionState previous)
        {
            previous.Dispose();
            element.ClearValue(StateProperty);
        }
        if ((bool)args.NewValue) element.SetValue(StateProperty, new MotionState(element));
    }

    private static void StateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => (sender.GetValue(StateProperty) as MotionState)?.Refresh();

    private sealed class MotionState
    {
        private static readonly DependencyPropertyDescriptor Pressed =
            DependencyPropertyDescriptor.FromProperty(ButtonBase.IsPressedProperty, typeof(ButtonBase));
        private readonly FrameworkElement _surface;
        private readonly ScaleTransform _scale = new();
        private readonly TranslateTransform _offset = new();
        private readonly TransformGroup _transform = new();
        private Transform? _original;
        private ButtonBase? _button;

        public MotionState(FrameworkElement surface)
        {
            _surface = surface;
            surface.Loaded += Loaded;
            surface.Unloaded += Unloaded;
            if (surface.IsLoaded) Attach();
        }

        private void Loaded(object sender, RoutedEventArgs args) => Attach();
        private void Unloaded(object sender, RoutedEventArgs args) => Detach();

        private void Attach()
        {
            if (_button is not null || _surface.TemplatedParent is not ButtonBase button) return;
            _button = button;
            _original = _surface.RenderTransform;
            _transform.Children.Clear();
            if (_original is not null) _transform.Children.Add(_original);
            _transform.Children.Add(_scale);
            _transform.Children.Add(_offset);
            _surface.SetCurrentValue(UIElement.RenderTransformProperty, _transform);
            button.MouseEnter += InputChanged;
            button.MouseLeave += InputChanged;
            button.IsKeyboardFocusWithinChanged += PropertyChanged;
            button.IsEnabledChanged += PropertyChanged;
            _surface.IsVisibleChanged += PropertyChanged;
            _surface.SizeChanged += SizeChanged;
            Pressed.AddValueChanged(button, PressedChanged);
            Refresh();
        }

        private void InputChanged(object sender, System.Windows.Input.MouseEventArgs args) => Refresh();
        private void PropertyChanged(object sender, DependencyPropertyChangedEventArgs args) => Refresh();
        private void PressedChanged(object? sender, EventArgs args) => Refresh();
        private void SizeChanged(object sender, SizeChangedEventArgs args) => CenterScale();

        private void CenterScale()
        {
            _scale.CenterX = _surface.ActualWidth / 2;
            _scale.CenterY = _surface.ActualHeight / 2;
        }

        public void Refresh()
        {
            if (_button is null) return;
            CenterScale();
            var enabled = _button.IsEnabled && _surface.IsVisible && GetMotionEnabled(_surface) &&
                SystemParameters.ClientAreaAnimation && !DesignerProperties.GetIsInDesignMode(_surface);
            var pressed = enabled && _button.IsPressed;
            var hover = enabled && GetEnableHover(_surface) && (_button.IsMouseOver || _button.IsKeyboardFocusWithin);
            var requestedOffset = GetHoverOffset(_surface);
            var requestedScale = GetPressedScale(_surface);
            var offset = double.IsFinite(requestedOffset) ? Math.Clamp(requestedOffset, -6, 0) : -2;
            var scale = double.IsFinite(requestedScale) ? Math.Clamp(requestedScale, .95, 1) : .985;
            var duration = pressed ? .075 : .15;
            Transition(_offset, TranslateTransform.YProperty, pressed ? 1 : hover ? offset : 0, duration, enabled);
            Transition(_scale, ScaleTransform.ScaleXProperty, pressed ? scale : 1, duration, enabled);
            Transition(_scale, ScaleTransform.ScaleYProperty, pressed ? scale : 1, duration, enabled);
        }

        private static void Transition(Animatable target, DependencyProperty property, double value, double duration, bool animate)
        {
            var current = (double)target.GetValue(property);
            target.BeginAnimation(property, null);
            target.SetValue(property, value);
            if (!animate || Math.Abs(current - value) < .0001) return;
            target.BeginAnimation(property, new DoubleAnimation(current, value, TimeSpan.FromSeconds(duration))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            }, HandoffBehavior.SnapshotAndReplace);
        }

        private void Detach()
        {
            if (_button is null) return;
            _button.MouseEnter -= InputChanged;
            _button.MouseLeave -= InputChanged;
            _button.IsKeyboardFocusWithinChanged -= PropertyChanged;
            _button.IsEnabledChanged -= PropertyChanged;
            _surface.IsVisibleChanged -= PropertyChanged;
            _surface.SizeChanged -= SizeChanged;
            Pressed.RemoveValueChanged(_button, PressedChanged);
            _button = null;
            Transition(_offset, TranslateTransform.YProperty, 0, 0, false);
            Transition(_scale, ScaleTransform.ScaleXProperty, 1, 0, false);
            Transition(_scale, ScaleTransform.ScaleYProperty, 1, 0, false);
            if (ReferenceEquals(_surface.RenderTransform, _transform))
                _surface.SetCurrentValue(UIElement.RenderTransformProperty, _original);
            _transform.Children.Clear();
            _original = null;
        }

        public void Dispose()
        {
            Detach();
            _surface.Loaded -= Loaded;
            _surface.Unloaded -= Unloaded;
        }
    }
}
