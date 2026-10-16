using System.Windows;
using System.Windows.Controls;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Controls;

// Virtualize individual rows, not whole series: a single series can contain hundreds of portraits.
public sealed class GeneralGalleryItemsControl : ItemsControl
{
    public static readonly DependencyProperty GroupsProperty = DependencyProperty.Register(
        nameof(Groups), typeof(IReadOnlyList<GeneralGalleryGroupViewModel>), typeof(GeneralGalleryItemsControl),
        new PropertyMetadata(null, (owner, _) =>
        {
            var gallery = (GeneralGalleryItemsControl)owner;
            gallery.RebuildRows();
            gallery.ScrollToTop();
        }));

    private ScrollViewer? _scroller;
    private int _columns = 1;

    public IReadOnlyList<GeneralGalleryGroupViewModel>? Groups
    {
        get => (IReadOnlyList<GeneralGalleryGroupViewModel>?)GetValue(GroupsProperty);
        set => SetValue(GroupsProperty, value);
    }

    public override void OnApplyTemplate()
    {
        if (_scroller is not null) _scroller.ScrollChanged -= ViewportChanged;
        base.OnApplyTemplate();
        _scroller = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
        if (_scroller is not null) _scroller.ScrollChanged += ViewportChanged;
    }

    public void ScrollToTop() => _scroller?.ScrollToTop();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        // Choose the right row width before the first layout, including the scrollbar and padding.
        SetViewportWidth(Math.Max(0, sizeInfo.NewSize.Width - SystemParameters.VerticalScrollBarWidth - 8));
    }

    private void ViewportChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange != 0) SetViewportWidth(e.ViewportWidth);
    }

    private void SetViewportWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        var columns = Math.Max(1, (int)(width / 154)); // 136-DIP card plus its 8/10 margins.
        if (columns == _columns) return;
        _columns = columns;
        RebuildRows();
    }

    private void RebuildRows()
    {
        var rows = new List<object>();
        foreach (var group in Groups ?? [])
        {
            rows.Add(group);
            for (var offset = 0; offset < group.Entries.Count; offset += _columns)
                rows.Add(new GeneralGalleryPortraitRow(group.Entries.Skip(offset).Take(_columns).ToArray()));
        }
        ItemsSource = rows;
    }
}

public sealed record GeneralGalleryPortraitRow(IReadOnlyList<GeneralGalleryEntryViewModel> Entries);
