using System.Windows;
using System.Windows.Controls.Primitives;

namespace CardGame.Wpf.Controls;

/// <summary>Keep skill order row by row, with an unpaired last skill beside the portrait.</summary>
public sealed class SkillRailPanel : UniformGrid
{
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var result = base.ArrangeOverride(arrangeSize);
        if (Columns != 2) return result;

        var visible = InternalChildren.OfType<FrameworkElement>()
            .Where(child => child.Visibility != Visibility.Collapsed).ToArray();
        if (visible.Length % 2 == 1)
        {
            var last = visible[^1];
            var slot = LayoutInformation.GetLayoutSlot(last);
            slot.X = arrangeSize.Width - slot.Width;
            last.Arrange(slot);
        }
        return result;
    }
}
