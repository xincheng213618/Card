using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class GeneralGalleryFocusChecks
{
    public static void DetailsRestoreEnabledBrowseFocus()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new GalleryFixture());
        using var model = new MainViewModel(false, 17, false, new MemorySaveStore(), contentRegistry: registry)
        {
            IsMotionEnabled = false, IsSoundEnabled = false
        };
        var series = (IList<GallerySeries>)model.GeneralGallerySeries;
        Program.Assert(series.IsReadOnly && series.Count > 0,
            "Gallery series exposed to a view must keep an immutable collection boundary.");
        try { series[0] = series[0]; throw new InvalidOperationException("A gallery view replaced shared series metadata."); }
        catch (NotSupportedException) { }
        model.OpenGeneralGalleryCommand.Execute(null);
        var revision = Program.Engine(model).Revision;
        var panel = new GeneralGalleryPanel { DataContext = model };
        FocusManager.SetIsFocusScope(panel, true);
        // Provide a real presentation source without WS_VISIBLE or activation.
        // Focus() can then use its logical-focus fallback without a desktop window.
        using var source = new HwndSource(new HwndSourceParameters("Card gallery logical focus check")
        {
            Width = 1440, Height = 860, WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = 0x08000080
        });
        source.RootVisual = panel;
        var observe = typeof(GeneralGalleryPanel).GetMethod("Observe", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var returnFocus = typeof(GeneralGalleryPanel).GetField("_detailsReturnFocus", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            Pump(panel);
            Program.Assert(panel.IsLoaded && panel.IsVisible,
                "The hidden presentation source must load the real gallery and enable its visibility guards.");
            var entry = model.GeneralGalleryEntries.First();
            var original = Program.Find<Button>(panel).Single(button => ReferenceEquals(button.DataContext, entry) &&
                ReferenceEquals(button.Command, model.SelectGeneralGalleryEntryCommand));
            var browse = (FrameworkElement)panel.FindName("GalleryBrowseSurface");
            var close = (Button)panel.FindName("CloseGeneralDetailsButton");
            var restored = 0;
            original.GotFocus += (_, _) =>
            {
                Program.Assert(original.IsEnabled && browse.IsEnabled && !model.HasGeneralGallerySelection,
                    "The gallery restored focus before its browse target became enabled.");
                restored++;
            };

            model.SelectGeneralGalleryEntryCommand.Execute(entry);
            Pump(panel);
            Program.Assert(model.HasGeneralGallerySelection && !browse.IsEnabled && !original.IsEnabled,
                "The real detail overlay must disable its underlying browse card.");
            // Seed only the remembered keyboard target because this hidden source
            // intentionally never acquires native keyboard focus.
            returnFocus.SetValue(panel, original);
            FocusManager.SetFocusedElement(panel, null);
            var disabledDuringClose = false;
            model.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.SelectedGeneralGalleryEntry) &&
                    !model.HasGeneralGallerySelection) disabledDuringClose |= !original.IsEnabled;
            };
            model.CloseGeneralGalleryDetailsCommand.Execute(null);
            Pump(panel);
            Program.Assert(disabledDuringClose && restored == 1 && original.IsEnabled &&
                           ReferenceEquals(FocusManager.GetFocusedElement(panel), original),
                "Closing details must restore the original card after its real bindings enable browsing.");

            model.SelectGeneralGalleryEntryCommand.Execute(entry);
            Pump(panel);
            returnFocus.SetValue(panel, original);
            FocusManager.SetFocusedElement(panel, null);
            model.CloseGeneralGalleryDetailsCommand.Execute(null);
            model.SelectGeneralGalleryEntryCommand.Execute(model.GeneralGalleryEntries.Last());
            Pump(panel);
            Program.Assert(model.HasGeneralGallerySelection && restored == 1 &&
                           ReferenceEquals(FocusManager.GetFocusedElement(panel), close),
                "An old close callback stole focus from a reopened detail overlay.");

            returnFocus.SetValue(panel, original);
            FocusManager.SetFocusedElement(panel, null);
            model.CloseGeneralGalleryDetailsCommand.Execute(null);
            model.CloseGeneralGalleryCommand.Execute(null);
            Pump(panel);
            Program.Assert(restored == 1 && FocusManager.GetFocusedElement(panel) is null &&
                           Program.Engine(model).Revision == revision,
                "A closed gallery reclaimed focus or advanced the match through a delayed callback.");
        }
        finally
        {
            observe.Invoke(panel, [null]);
            FocusManager.SetFocusedElement(panel, null);
            source.RootVisual = null;
            panel.DataContext = null;
        }
    }

    private static void UpdateView(FrameworkElement panel)
    {
        panel.Measure(new Size(1440, 860));
        panel.Arrange(new Rect(0, 0, 1440, 860));
        panel.UpdateLayout();
    }

    private static void Pump(FrameworkElement panel)
    {
        UpdateView(panel);
        panel.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        panel.UpdateLayout();
    }

    internal sealed class GalleryFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-gallery-focus", new Version(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new("fixture:gallery-focus-a", "焦点甲", "supporter", "standard:none", "wei"));
            builder.AddGeneral(new("fixture:gallery-focus-b", "焦点乙", "supporter", "standard:none", "shu"));
        }
    }
}
