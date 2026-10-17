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
                "ol:guan-suo" or "ol:wang-lang" or "ol:bao-san-niang" or "ol:deng-zhi" or "ol:cao-ying" or "ol:dong-zhao" or "ol:wu-yan" or "ol:sp-zhang-he" or "ol:xuan-gong-zhu" or "ol:huang-yueying-sp" or "ol:bu-zhi" or "ol:dong-bai" or "ol:zhao-xiang" or "ol:he-qi" or "ol:kan-ze" or "ol:wang-yun" or "ol:zhuge-guo" or "ol:xi-zhi-cai" or "ol:bei-mi-hu" or "ol:liu-qi" or "ol:lu-zhi-wei" or "ol:sima-hui" or "ol:xu-rong" or "ol:li-jue" or "ol:zhang-ji" or "ol:su-fei" or "ol:huang-quan" or "ol:tang-zi" or "ol:fan-chou" or "ol:guo-si" or "ol:zhang-qi-ying" or "ol:cao-chun" or "ol:wang-can" or "ol:pang-tong-sp" or "ol:yuan-tan-yuan-shang" or "ol:lv-qian" or "ol:lv-kuang-lv-xiang" or "ol:xin-pi" or "ol:hua-man" or "ol:niu-jin" or "ol:duan-wei" or "mobile:liu-zan" or "ol:ding-yuan" or "ol:cao-song" or "ol:bian-fu-ren" or "ol:zhang-ling" or "ol:cao-shuang" or "ol:sp-zhang-liao" or "ol:wo-long-feng-chu" or "ol:sun-ru" or "ol:liu-pi" or "ol:gao-gan" or "ol:huo-jun" or "ol:qiao-zhou" or "ol:du-xi" or "ol:fu-rong" or "ol:zhang-yi" or "ol:yan-pu" or "ol:luo-xian" or "ol:hao-pu" or "ol:ma-wan" or "ol:du-fu-ren" or "ol:fei-yi" or "ol:zhang-shi-ping" or "ol:qian-zhao" or "ol:ding-shang-wan" or "ol:he-jin" or "ol:li-wan" or "ol:yan-rou" or "ol:cao-xi" or "ol:hu-jin-ding" or "ol:lu-yu-sheng" or "ol:wang-kuang" or "ol:yuan-huan" or "ol:zhang-yan" or "ol:wu-an-guo" or "ol:han-fu" or "ol:zhu-jun" or "ol:ke-bi-neng" or "ol:niu-fu" or "ol:duan-jiong" or "ol:ma-mi-di" or "ol:ruan-hui" or "ol:tian-chou" or "ol:cheng-gong-ying" or "ol:ma-xiu-ma-tie" or "ol:pei-yuan-shao" or "ol:wu-jing" or "ol:xiang-chong" or "ol:yang-feng" or "ol:qin-lang" or "ol:a-hui-nan" or "ol:wang-guan" or "ol:liu-yan" or "ol:meng-da" or "ol:cai-mao" or "ol:ya-dan" or "ol:cao-teng" or "ol:yang-fu" or "ol:wu-tu-gu" or "ol:sun-hao" or "ol:da-qiao-xiao-qiao" or "ol:fan-jiang-zhang-da" or "ol:wen-yang" or "ol:ma-yuan-yi" or "ol:hu-ban" or "ol:liu-ba" or "ol:liu-pan" or "ol:qu-huang" or "ol:ruan-yu" or "ol:cao-yu" or "ol:bao-xin" or "ol:guo-tu" or "ol:cui-yan" or "ol:li-yi" or "ol:sun-yuan" or "ol:liu-zhang" or "ol:tang-tang" or "ol:yuan-ji" or "ol:kong-shu").ToArray();
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
            entries.Any(entry => entry.GeneralId == "ol:hua-man") &&
            entries.Any(entry => entry.GeneralId == "ol:niu-jin") &&
            entries.Any(entry => entry.GeneralId == "ol:duan-wei") &&
            entries.Any(entry => entry.GeneralId == "mobile:liu-zan") &&
            entries.Any(entry => entry.GeneralId == "ol:ding-yuan") &&
            entries.Any(entry => entry.GeneralId == "ol:cao-song") &&
            entries.Any(entry => entry.GeneralId == "ol:bian-fu-ren") &&
            entries.Any(entry => entry.GeneralId == "ol:zhang-ling") &&
            entries.Any(entry => entry.GeneralId == "ol:cao-shuang") &&
            entries.Any(entry => entry.GeneralId == "ol:sp-zhang-liao") &&
            entries.Any(entry => entry.GeneralId == "ol:wo-long-feng-chu") &&
            entries.Any(entry => entry.GeneralId == "ol:sun-ru") &&
            entries.Any(entry => entry.GeneralId == "ol:liu-pi") &&
            entries.Any(entry => entry.GeneralId == "ol:gao-gan") &&
            entries.Any(entry => entry.GeneralId == "ol:huo-jun") &&
            entries.Any(entry => entry.GeneralId == "ol:qiao-zhou") &&
            entries.Any(entry => entry.GeneralId == "ol:du-xi") &&
            entries.Any(entry => entry.GeneralId == "ol:fu-rong") &&
            entries.Any(entry => entry.GeneralId == "mobile:wang-fu-zhao-lei") &&
            entries.Any(entry => entry.GeneralId == "ol:zhang-yi") &&
            entries.Any(entry => entry.GeneralId == "ol:yan-pu") &&
            entries.Any(entry => entry.GeneralId == "ol:luo-xian") &&
            entries.Any(entry => entry.GeneralId == "ol:hao-pu") &&
            entries.Any(entry => entry.GeneralId == "ol:ma-wan") &&
            entries.Any(entry => entry.GeneralId == "ol:du-fu-ren") &&
            entries.Any(entry => entry.GeneralId == "ol:fei-yi") &&
            entries.Any(entry => entry.GeneralId == "ol:zhang-shi-ping") &&
            entries.Any(entry => entry.GeneralId == "ol:qian-zhao") &&
            entries.Any(entry => entry.GeneralId == "ol:ding-shang-wan") &&
            entries.Any(entry => entry.GeneralId == "ol:he-jin") &&
            entries.Any(entry => entry.GeneralId == "ol:yan-rou") &&
            entries.Any(entry => entry.GeneralId == "ol:cao-xi") &&
            entries.Any(entry => entry.GeneralId == "ol:hu-jin-ding") &&
            entries.Any(entry => entry.GeneralId == "ol:lu-yu-sheng") &&
            entries.Any(entry => entry.GeneralId == "ol:wang-kuang") &&
            entries.Any(entry => entry.GeneralId == "ol:yuan-huan") &&
            entries.Any(entry => entry.GeneralId == "ol:zhang-yan") &&
            entries.Any(entry => entry.GeneralId == "ol:wu-an-guo") &&
            entries.Any(entry => entry.GeneralId == "ol:han-fu") &&
            entries.Any(entry => entry.GeneralId == "ol:zhu-jun") &&
            entries.Any(entry => entry.GeneralId == "ol:ke-bi-neng") &&
            entries.Any(entry => entry.GeneralId == "ol:niu-fu") &&
            entries.Any(entry => entry.GeneralId == "ol:duan-jiong") &&
            entries.Any(entry => entry.GeneralId == "ol:ma-mi-di") &&
            entries.Any(entry => entry.GeneralId == "ol:ruan-hui") &&
            entries.Any(entry => entry.GeneralId == "ol:tian-chou") &&
            entries.Any(entry => entry.GeneralId == "ol:cheng-gong-ying") &&
            entries.Any(entry => entry.GeneralId == "ol:ma-xiu-ma-tie") &&
            entries.Any(entry => entry.GeneralId == "ol:pei-yuan-shao") &&
            entries.Any(entry => entry.GeneralId == "ol:wu-jing") &&
            entries.Any(entry => entry.GeneralId == "ol:xiang-chong") &&
            entries.Any(entry => entry.GeneralId == "ol:yang-feng") &&
            entries.Any(entry => entry.GeneralId == "ol:qin-lang") &&
            entries.Any(entry => entry.GeneralId == "ol:a-hui-nan") &&
            entries.Any(entry => entry.GeneralId == "ol:wang-guan") &&
            entries.Any(entry => entry.GeneralId == "ol:liu-yan") &&
            entries.Any(entry => entry.GeneralId == "ol:meng-da") &&
            entries.Any(entry => entry.GeneralId == "ol:cai-mao") &&
            entries.Any(entry => entry.GeneralId == "ol:ya-dan") &&
            entries.Any(entry => entry.GeneralId == "ol:cao-teng") &&
            entries.Any(entry => entry.GeneralId == "ol:yang-fu") &&
            entries.Any(entry => entry.GeneralId == "ol:wu-tu-gu") &&
            entries.Any(entry => entry.GeneralId == "ol:da-qiao-xiao-qiao") &&
            entries.Any(entry => entry.GeneralId == "ol:wen-yang") &&
            entries.Any(entry => entry.GeneralId == "ol:ma-yuan-yi") &&
            entries.Any(entry => entry.GeneralId == "ol:hu-ban") &&
            entries.Any(entry => entry.GeneralId == "ol:liu-ba") &&
            entries.Any(entry => entry.GeneralId == "ol:liu-pan") &&
            entries.Any(entry => entry.GeneralId == "ol:qu-huang") &&
            entries.Any(entry => entry.GeneralId == "ol:ruan-yu") &&
            entries.Any(entry => entry.GeneralId == "ol:cao-yu") &&
            entries.Any(entry => entry.GeneralId == "ol:bao-xin") &&
            entries.Any(entry => entry.GeneralId == "ol:guo-tu") &&
            entries.Any(entry => entry.GeneralId == "ol:cui-yan") &&
            entries.Any(entry => entry.GeneralId == "ol:li-yi") &&
            entries.Any(entry => entry.GeneralId == "ol:sun-yuan") &&
            entries.Any(entry => entry.GeneralId == "ol:liu-zhang") &&
            entries.Any(entry => entry.GeneralId == "ol:tang-tang") &&
            entries.Any(entry => entry.GeneralId == "ol:yuan-ji") &&
            entries.Any(entry => entry.GeneralId == "ol:kong-shu") &&
            entries.Any(entry => entry.GeneralId == "ol:fan-jiang-zhang-da") &&
            entries.Any(entry => entry.GeneralId == "ol:sun-hao") &&
            entries.Any(entry => entry.GeneralId == "ol:li-wan"),
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
                if (entry.HealthImages.Count > 10)
                {
                    var health = Program.Find<System.Windows.Controls.ItemsControl>(panel)
                        .Single(control => control.Name == "SelectedGeneralHealthIcons");
                    var icons = Program.Find<System.Windows.Controls.Image>(health).ToArray();
                    Program.Assert(icons.Length == entry.HealthImages.Count && health.ActualHeight > 48 &&
                        icons.All(icon =>
                        {
                            var bounds = icon.TransformToAncestor(health).TransformBounds(
                                new System.Windows.Rect(0, 0, icon.ActualWidth, icon.ActualHeight));
                            return bounds.Left >= 0 && bounds.Top >= 0 &&
                                bounds.Right <= health.ActualWidth + 0.5 && bounds.Bottom <= health.ActualHeight + 0.5;
                        }), "High-HP portraits must wrap every health icon inside the actual visible strip.");
                }
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
