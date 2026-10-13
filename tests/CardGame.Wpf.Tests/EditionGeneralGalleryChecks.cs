using System.IO;
using System.Windows.Media;
using CardGame.Content.Standard;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class EditionGeneralGalleryChecks
{
    public static void NamespacedPortraitsAndDetails(string output)
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        using var model = new MainViewModel(false, 17, true, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore(),
            contentRegistry: registry) { IsMotionEnabled = false, IsSoundEnabled = false };
        model.OpenGeneralGalleryCommand.Execute(null);
        var revision = Program.Engine(model).Revision;
        var entries = model.GeneralGalleryEntries.Where(entry =>
            entry.GeneralId.StartsWith("mobile:", StringComparison.Ordinal) ||
            entry.GeneralId is "ol:han-sui" or "ol:xiahou-ba" or "classic:zhuge-dan" or "ol:he-tai-hou" or
                "ol:wen-pin" or "ol:dong-yun" or "ol:shen-pei" or "ol:sun-lu-yu" or "ol:cheng-yu" or
                "ol:zhang-lu" or "ol:wang-rong" or "ol:shi-xie" or "ol:ma-yun-lu" or "ol:ta-dun" or "ol:yan-bai-hu").ToArray();
        Program.Assert(entries.Any(entry => entry.GeneralId.StartsWith("mobile:", StringComparison.Ordinal)) &&
            entries.Any(entry => entry.GeneralId == "ol:han-sui") &&
            entries.Any(entry => entry.GeneralId == "ol:xiahou-ba") &&
            entries.Any(entry => entry.GeneralId == "classic:zhuge-dan") &&
            entries.Any(entry => entry.GeneralId == "ol:he-tai-hou") &&
            entries.Any(entry => entry.GeneralId == "ol:wen-pin") &&
            entries.Any(entry => entry.GeneralId == "ol:dong-yun") &&
            entries.Any(entry => entry.GeneralId == "ol:shen-pei") &&
            entries.Any(entry => entry.GeneralId == "ol:sun-lu-yu") &&
            entries.Any(entry => entry.GeneralId == "ol:cheng-yu") &&
            entries.Any(entry => entry.GeneralId == "ol:zhang-lu") &&
            entries.Any(entry => entry.GeneralId == "ol:wang-rong") &&
            entries.Any(entry => entry.GeneralId == "ol:shi-xie") &&
            entries.Any(entry => entry.GeneralId == "ol:ma-yun-lu") &&
            entries.Any(entry => entry.GeneralId == "ol:ta-dun") &&
            entries.Any(entry => entry.GeneralId == "ol:yan-bai-hu"),
            "The real catalogue must expose the added mobile and OL editions.");
        var panel = new GeneralGalleryPanel { DataContext = model };
        try
        {
            foreach (var entry in entries)
            {
                model.GeneralGallerySearchText = entry.Name;
                Program.Assert(model.GeneralGalleryEntries.Contains(entry) && entry.IsPlayable && entry.HasPortrait,
                    "Searching an edition must retain its playable entry and namespaced artwork.");
                model.SelectGeneralGalleryEntryCommand.Execute(entry);
                Program.Assert(model.SelectedGeneralGalleryEntry == entry &&
                    entry.Portrait.Brush is ImageBrush { ImageSource: not null } &&
                    entry.Skills.Count > 0 && entry.Skills.All(skill => !string.IsNullOrWhiteSpace(skill.Name)),
                    "Opening edition details must decode its own local portrait and show the registered skills.");
                Program.Render(panel, 1120, 820, Path.Combine(output,
                    GeneralArt.NormalizeKey(entry.GeneralId) + "-gallery.png"));
                model.CloseGeneralGalleryDetailsCommand.Execute(null);
            }
            Program.Assert(Program.Engine(model).Revision == revision,
                "Browsing and closing edition details must not advance the match.");
        }
        finally { panel.DataContext = null; }
    }
}
