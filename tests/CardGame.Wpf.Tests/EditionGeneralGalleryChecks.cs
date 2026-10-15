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
                "ol:zhang-lu" or "ol:wang-rong" or "ol:shi-xie" or "ol:ma-yun-lu" or "ol:ta-dun" or "ol:yan-bai-hu" or
                "ol:guan-suo" or "ol:wang-lang" or "ol:bao-san-niang" or "ol:deng-zhi" or "ol:cao-ying" or "ol:dong-zhao" or "ol:wu-yan" or "ol:sp-zhang-he" or "ol:xuan-gong-zhu" or "ol:huang-yueying-sp" or "ol:bu-zhi" or "ol:dong-bai" or "ol:zhao-xiang" or "ol:he-qi" or "ol:kan-ze" or "ol:wang-yun" or "ol:zhuge-guo" or "ol:xi-zhi-cai" or "ol:bei-mi-hu" or "ol:liu-qi" or "ol:lu-zhi-wei" or "ol:sima-hui" or "ol:xu-rong" or "ol:li-jue" or "ol:zhang-ji" or "ol:su-fei" or "ol:huang-quan" or "ol:tang-zi" or "ol:fan-chou" or "ol:guo-si" or "ol:zhang-qi-ying" or "ol:cao-chun" or "ol:wang-can" or "ol:pang-tong-sp" or "ol:yuan-tan-yuan-shang" or "ol:lv-qian" or "ol:lv-kuang-lv-xiang" or "ol:xin-pi" or "ol:hua-man").ToArray();
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
            entries.Any(entry => entry.GeneralId == "ol:yan-bai-hu") &&
            entries.Any(entry => entry.GeneralId == "ol:guan-suo") &&
            entries.Any(entry => entry.GeneralId == "ol:wang-lang") &&
            entries.Any(entry => entry.GeneralId == "ol:bao-san-niang") &&
            entries.Any(entry => entry.GeneralId == "ol:deng-zhi") &&
            entries.Any(entry => entry.GeneralId == "ol:cao-ying") &&
            entries.Any(entry => entry.GeneralId == "ol:dong-zhao") &&
            entries.Any(entry => entry.GeneralId == "ol:wu-yan") &&
            entries.Any(entry => entry.GeneralId == "ol:sp-zhang-he") &&
            entries.Any(entry => entry.GeneralId == "ol:xuan-gong-zhu") &&
            entries.Any(entry => entry.GeneralId == "ol:huang-yueying-sp") &&
            entries.Any(entry => entry.GeneralId == "ol:bu-zhi") &&
            entries.Any(entry => entry.GeneralId == "ol:dong-bai") &&
            entries.Any(entry => entry.GeneralId == "ol:zhao-xiang") &&
            entries.Any(entry => entry.GeneralId == "ol:he-qi") &&
            entries.Any(entry => entry.GeneralId == "ol:kan-ze") &&
            entries.Any(entry => entry.GeneralId == "ol:wang-yun") &&
            entries.Any(entry => entry.GeneralId == "ol:zhuge-guo") &&
            entries.Any(entry => entry.GeneralId == "ol:xi-zhi-cai") &&
            entries.Any(entry => entry.GeneralId == "ol:bei-mi-hu") &&
            entries.Any(entry => entry.GeneralId == "ol:liu-qi") &&
            entries.Any(entry => entry.GeneralId == "ol:lu-zhi-wei") &&
            entries.Any(entry => entry.GeneralId == "ol:sima-hui") &&
            entries.Any(entry => entry.GeneralId == "ol:xu-rong") &&
            entries.Any(entry => entry.GeneralId == "ol:li-jue") &&
            entries.Any(entry => entry.GeneralId == "ol:zhang-ji") &&
            entries.Any(entry => entry.GeneralId == "ol:su-fei") &&
            entries.Any(entry => entry.GeneralId == "ol:huang-quan") &&
            entries.Any(entry => entry.GeneralId == "ol:tang-zi") &&
            entries.Any(entry => entry.GeneralId == "ol:fan-chou") &&
            entries.Any(entry => entry.GeneralId == "ol:guo-si") &&
            entries.Any(entry => entry.GeneralId == "ol:zhang-qi-ying") &&
            entries.Any(entry => entry.GeneralId == "ol:cao-chun") &&
            entries.Any(entry => entry.GeneralId == "ol:wang-can") &&
            entries.Any(entry => entry.GeneralId == "ol:pang-tong-sp") &&
            entries.Any(entry => entry.GeneralId == "ol:yuan-tan-yuan-shang") &&
            entries.Any(entry => entry.GeneralId == "ol:lv-qian") &&
            entries.Any(entry => entry.GeneralId == "ol:lv-kuang-lv-xiang") &&
            entries.Any(entry => entry.GeneralId == "ol:xin-pi") &&
            entries.Any(entry => entry.GeneralId == "ol:hua-man"),
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
                var skillsScroller = Program.Find<System.Windows.Controls.ScrollViewer>(panel)
                    .Single(view => view.Name == "GeneralSkillsScroller");
                if (skillsScroller.ScrollableHeight > 0)
                {
                    skillsScroller.ScrollToEnd();
                    Program.Render(panel, 1120, 820, Path.Combine(output,
                        GeneralArt.NormalizeKey(entry.GeneralId) + "-gallery-skills-bottom.png"));
                    Program.Assert(Math.Abs(skillsScroller.VerticalOffset - skillsScroller.ScrollableHeight) < 1,
                        "Long registered skill descriptions must remain reachable through the actual details scroller.");
                    skillsScroller.ScrollToTop();
                    panel.UpdateLayout();
                }
                model.CloseGeneralGalleryDetailsCommand.Execute(null);
            }
            Program.Assert(Program.Engine(model).Revision == revision,
                "Browsing and closing edition details must not advance the match.");
        }
        finally { panel.DataContext = null; }
    }
}
