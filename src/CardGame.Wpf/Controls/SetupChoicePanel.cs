using System.Windows;
using System.Windows.Controls.Primitives;

namespace CardGame.Wpf.Controls;

// Keep role descriptions readable while using the configuration panel's width.
public sealed class SetupChoicePanel : UniformGrid
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = double.IsFinite(availableSize.Width)
            ? (int)Math.Clamp(Math.Floor(availableSize.Width / 220), 1, 3)
            : 1;
        if (Columns != columns) SetCurrentValue(ColumnsProperty, columns);
        return base.MeasureOverride(availableSize);
    }
}
