using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class JinFactionPresentationChecks
{
    public static void GalleryNamesFiltersColorsAndEmbeddedImages()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FactionFixture());
        using var model = new MainViewModel(false, 17, true, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore(),
            contentRegistry: registry) { IsMotionEnabled = false, IsSoundEnabled = false };
        model.OpenGeneralGalleryCommand.Execute(null);
        var revision = Program.Engine(model).Revision;
        var expected = new (string Id, string Name, Color Color)[]
        {
            ("wei", "魏", Colors.LightSkyBlue), ("shu", "蜀", Colors.Coral),
            ("wu", "吴", Colors.LightGreen), ("qun", "群", Colors.Wheat),
            ("jin", "晋", Colors.Plum)
        };
        Program.Assert(model.GeneralGalleryEntries.Count == expected.Length,
            "The shared fixture exposes exactly five synthetic faction entries in the gallery.");
        foreach (var faction in expected)
        {
            Program.Assert(model.GeneralGalleryFactions.Single(option => option.Id == faction.Id).Name == faction.Name,
                $"The {faction.Id} filter must display its proper faction name.");
            model.SelectGeneralGalleryFactionCommand.Execute(faction.Id);
            var entry = model.GeneralGalleryEntries.Single();
            Program.Assert(entry.GeneralId == $"fixture:faction-{faction.Id}" && entry.FactionId == faction.Id &&
                           entry.Kingdom == faction.Name && entry.AccessibilityText.Contains(faction.Name, StringComparison.Ordinal),
                $"The {faction.Id} filter must select only its own entry and expose the same accessible faction name.");
            Program.Assert(entry.FactionBrush is SolidColorBrush brush && brush.Color == faction.Color &&
                           entry.FactionImage.EndsWith($"/gallery-faction-{faction.Id}.png", StringComparison.Ordinal) &&
                           entry.HealthImages.Count == 3 && entry.HealthImages.All(image =>
                               image.EndsWith($"/gallery-hp-{faction.Id}.png", StringComparison.Ordinal)) &&
                           entry.HealthText == "3 体力",
                $"The {faction.Id} entry must use its own color, emblem and three real health icons.");
            AssertEmbeddedImage(entry.FactionImage);
            AssertEmbeddedImage(entry.HealthImages[0]);
        }
        model.ClearGeneralGalleryFiltersCommand.Execute(null);
        Program.Assert(model.SelectedGeneralGalleryFaction == "all" &&
                       model.GeneralGalleryEntries.Select(entry => entry.FactionId)
                           .SequenceEqual(new[] { "shu", "wu", "wei", "qun", "jin" }) &&
                       Program.Engine(model).Revision == revision,
            "Clearing the faction filter restores every entry in faction order without advancing the match.");
    }

    private static void AssertEmbeddedImage(string image)
    {
        var resource = Application.GetResourceStream(new Uri(image, UriKind.Absolute));
        Program.Assert(resource is not null, $"The faction presentation resource is missing: {image}");
        using var stream = resource!.Stream;
        var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        Program.Assert(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0,
            $"The faction presentation resource must decode as a real image: {image}");
    }

    private sealed class FactionFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-faction-presentation", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var faction in new[] { "wei", "shu", "wu", "qun", "jin" })
                builder.AddGeneral(new ContentGeneralDefinition($"fixture:faction-{faction}", $"合成{faction}",
                    $"fixture-faction-{faction}", "standard:none", faction, BaseHp: 3));
        }
    }
}
