using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;

namespace CardGame.Wpf.Controls;

/// <summary>Current public HP. Transient damage/recovery is painted by the table's shared feedback layer.</summary>
public sealed class HealthMeter : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(int),
        typeof(HealthMeter), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(int),
        typeof(HealthMeter), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsUndefinedProperty = DependencyProperty.Register(nameof(IsUndefined), typeof(bool),
        typeof(HealthMeter), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly Brush Empty = Solid(Color.FromRgb(25, 35, 29));
    private static readonly Brush Border = Solid(Color.FromRgb(145, 128, 86));
    private static readonly Brush Healthy = Gradient(Color.FromRgb(151, 224, 159), Color.FromRgb(53, 119, 72));
    private static readonly Brush Critical = Gradient(Color.FromRgb(255, 157, 112), Color.FromRgb(168, 49, 37));
    private static readonly Pen Divider = new(Solid(Color.FromArgb(110, 19, 29, 22)), 1);

    public HealthMeter()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
    }

    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public bool IsUndefined { get => (bool)GetValue(IsUndefinedProperty); set => SetValue(IsUndefinedProperty, value); }

    internal double Fraction(double hp) => Maximum <= 0 ? 0 : Math.Clamp(hp / Maximum, 0, 1);
    internal Rect TrackBounds => new(.5, .5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1));

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 110 : availableSize.Width, 7);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (IsUndefined) return;
        var track = TrackBounds;
        if (track.Width <= 0 || track.Height <= 0) return;
        dc.DrawRoundedRectangle(Empty, new Pen(Border, 1), track, 2, 2);
        dc.PushClip(new RectangleGeometry(track, 2, 2));
        var fraction = Fraction(Value);
        if (fraction > 0)
            dc.DrawRectangle(fraction <= .25 ? Critical : Healthy, null,
                new Rect(track.Left, track.Top, track.Width * fraction, track.Height));
        if (Maximum is > 1 and <= 12)
            for (var pip = 1; pip < Maximum; pip++)
            {
                var x = track.Left + track.Width * pip / Maximum;
                dc.DrawLine(Divider, new Point(x, track.Top + 1), new Point(x, track.Bottom - 1));
            }
        dc.Pop();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new HealthMeterPeer(this);

    private sealed class HealthMeterPeer(HealthMeter owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(HealthMeter);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;
        protected override string GetNameCore() => owner.IsUndefined ? "体力未定义" : $"体力 {owner.Value}/{owner.Maximum}";
    }

    private static Brush Solid(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
    private static Brush Gradient(Color top, Color bottom)
    {
        var brush = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
        brush.Freeze(); return brush;
    }
}
