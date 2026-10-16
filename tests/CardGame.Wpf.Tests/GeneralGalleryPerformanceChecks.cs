using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Content.Standard;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class GeneralGalleryPerformanceChecks
{
    public static void VisibleRowsAndPortraitCost(string output)
    {
        using var model = new MainViewModel(false, 17, true, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore(),
            contentRegistry: StandardContentRegistry.CreateWithClassicGenerals())
        { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        var panel = new GeneralGalleryPanel { DataContext = model };
        var revision = Program.Engine(model).Revision;
        try
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            model.OpenGeneralGalleryCommand.Execute(null);
            Program.Render(panel, 1440, 900, null);
            timer.Stop();
            bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            var count = Portraits().Length;
            var coldMs = timer.Elapsed.TotalMilliseconds;
            Console.WriteLine($"Gallery first open: {coldMs:F1} ms, {count}/{model.GeneralGalleryEntries.Count} portrait controls, {bytes:N0} managed bytes.");
            Program.Assert(count is > 0 and <= 64 && count < model.GeneralGalleryEntries.Count / 2 && bytes < 20_000_000,
                "Opening the gallery must realize a bounded viewport, without allocating controls for the entire catalogue.");
            var firstPortrait = Portraits().First();
            var firstEntry = (GeneralGalleryEntryViewModel)firstPortrait.DataContext;
            var thumbnail = (ImageBrush)firstEntry.Portrait.ThumbnailBrush;
            Program.Assert(thumbnail.ImageSource is BitmapSource { PixelWidth: <= 256 } &&
                Program.Find<Grid>(firstPortrait).Any(grid => ReferenceEquals(grid.Background, thumbnail)),
                "The actual browse card must use a decoded thumbnail instead of the full artwork.");
            Program.Render(panel, 1440, 900, Path.Combine(output, "general-gallery-first-open.png"));

            model.CloseGeneralGalleryCommand.Execute(null);
            Program.Render(panel, 1440, 900, null);
            timer.Restart();
            model.OpenGeneralGalleryCommand.Execute(null);
            Program.Render(panel, 1440, 900, null);
            timer.Stop();
            Console.WriteLine($"Gallery reopen: {timer.Elapsed.TotalMilliseconds:F1} ms.");
            var reopenMs = timer.Elapsed.TotalMilliseconds;
            var cards = (GeneralGalleryItemsControl)panel.FindName("GeneralGalleryCards");
            var scroller = Program.Find<ScrollViewer>(cards).Single();
            scroller.ScrollToEnd();
            Program.Render(panel, 1440, 900, null);
            Program.Render(panel, 1440, 900, Path.Combine(output, "general-gallery-bottom.png"));
            Program.Assert(Portraits().Length <= 64 && Portraits().Any(button =>
                    ReferenceEquals(button.DataContext, model.GeneralGalleryEntries.Last())),
                "Scrolling must realize the final general and retire offscreen portrait controls.");

            model.SelectGeneralGallerySeriesCommand.Execute("other");
            Program.Render(panel, 1440, 900, null);
            Program.Assert(model.GeneralGalleryEntries.Count > 100 && Portraits().Length <= 64 &&
                scroller.VerticalOffset == 0 &&
                Portraits().All(button => ((GeneralGalleryEntryViewModel)button.DataContext).SeriesId == "other"),
                "A large single series must also virtualize its rows.");

            model.SelectGeneralGallerySeriesCommand.Execute("all");
            timer.Restart();
            model.GeneralGallerySearchText = firstEntry.Name;
            Program.Render(panel, 1440, 900, null);
            timer.Stop();
            var filterMs = timer.Elapsed.TotalMilliseconds;
            Console.WriteLine($"Gallery search and render: {filterMs:F1} ms.");
            var filtered = model.GeneralGalleryEntries.ToArray();
            Program.Assert(filtered.Contains(firstEntry) && Portraits().All(button => filtered.Contains(button.DataContext)),
                "Filtering must replace recycled rows without retaining a previous result.");
            model.SelectGeneralGalleryEntryCommand.Execute(firstEntry);
            Program.Render(panel, 1440, 900, null);
            var detailImage = (Image)panel.FindName("SelectedGeneralPortraitImage");
            Program.Assert(ReferenceEquals(detailImage.Source, firstEntry.Portrait.Image) &&
                detailImage.Source is BitmapSource fullImage && fullImage.PixelWidth > 256,
                "Details must retain the original high resolution artwork.");
            if (firstEntry.Skins.FirstOrDefault(skin => skin.Id != firstEntry.Portrait.SkinId) is { } alternate)
            {
                model.SelectGeneralSkinCommand.Execute(alternate);
                Program.Render(panel, 1440, 900, null);
                Program.Assert(firstEntry.Portrait.SkinId == alternate.Id &&
                    !ReferenceEquals(firstEntry.Portrait.ThumbnailBrush, thumbnail) &&
                    ReferenceEquals(detailImage.Source, firstEntry.Portrait.Image),
                    "Changing skin must refresh the shared thumbnail and detail artwork.");
            }
            model.CloseGeneralGalleryDetailsCommand.Execute(null);
            model.GeneralGallerySearchText = "unmatched-gallery-search";
            Program.Render(panel, 1440, 900, null);
            Program.Assert(model.HasNoGeneralGalleryResults && Portraits().Length == 0,
                "Empty search results must retire every previous portrait.");
            model.ClearGeneralGalleryFiltersCommand.Execute(null);
            foreach (var width in new[] { 1120, 1920, 1440 })
            {
                Program.Render(panel, width, 900, null);
                var portraits = Portraits();
                var lefts = portraits.Select(button => button.TransformToAncestor(panel).Transform(new Point()).X)
                    .Distinct().ToArray();
                Program.Assert(portraits.Length is > 0 and <= 64 && lefts.Max() + 136 <= width,
                    "Resizing the viewport must reflow virtual rows without clipping or expanding the whole catalogue.");
            }
            Program.Render(panel, 1440, 900, Path.Combine(output, "general-gallery-visible-rows.png"));
            File.WriteAllText(Path.Combine(output, "general-gallery-performance.json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    coldMs, reopenMs, filterMs, realizedPortraits = count,
                    total = model.GeneralGalleryEntries.Count, managedBytes = bytes
                }));
            Program.Assert(Program.Engine(model).Revision == revision, "Gallery browsing must not advance the match.");
        }
        finally { panel.DataContext = null; }

        Button[] Portraits() => Program.Find<Button>(panel)
            .Where(button => button.DataContext is GeneralGalleryEntryViewModel).ToArray();
    }
}
