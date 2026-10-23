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
        Program.Assert(new[] { "anniversary:kan-ze", "anniversary:hu-zhao", "anniversary:xu-jing",
                "anniversary:du-kui", "anniversary:liu-li", "anniversary:liu-yong",
                "anniversary:zhang-fen", "anniversary:bai-ling-yun", "anniversary:yang-biao",
                "anniversary:le-jiu", "anniversary:he-yan", "anniversary:guo-zhao",
                "anniversary:mang-ya-chang", "anniversary:feng-xi", "anniversary:zhao-tong-zhao-guang",
                "anniversary:lv-dai", "anniversary:fu-rong-fu-qian", "anniversary:gao-xiang",
                "anniversary:xiahou-en", "anniversary:zhang-wen", "anniversary:zhang-xun",
                "anniversary:mu-shun", "anniversary:sun-huan", "anniversary:xiahou-jie",
                "anniversary:cai-yang", "anniversary:xing-dao-rong", "anniversary:zhu-zhi",
                "anniversary:han-song", "anniversary:xiahou-hui", "anniversary:xun-xu",
                "anniversary:zhang-huan", "anniversary:che-zhou", "anniversary:wang-chang",
                "anniversary:wu-fu", "anniversary:ma-tie", "anniversary:yan-xiang",
                "anniversary:cui-lie", "anniversary:hou-cheng", "anniversary:ma-miao" }.All(id =>
                model.GeneralGalleryEntries.Any(entry => entry.GeneralId == id)),
            "The added distinct anniversary editions are present in the gallery.");
        var entries = model.GeneralGalleryEntries.Where(entry =>
            entry.GeneralId is "anniversary:kan-ze" or "anniversary:hu-zhao" or "anniversary:xu-jing" ||
            entry.GeneralId is "anniversary:du-kui" or "anniversary:liu-li" or "anniversary:liu-yong" ||
            entry.GeneralId is "anniversary:zhang-fen" or "anniversary:bai-ling-yun" or "anniversary:yang-biao" ||
            entry.GeneralId is "anniversary:le-jiu" or "anniversary:he-yan" or "anniversary:guo-zhao" ||
            entry.GeneralId is "anniversary:mang-ya-chang" or "anniversary:feng-xi" or "anniversary:zhao-tong-zhao-guang" ||
            entry.GeneralId is "anniversary:lv-dai" or "anniversary:fu-rong-fu-qian" or "anniversary:gao-xiang" ||
            entry.GeneralId is "anniversary:xiahou-en" or "anniversary:zhang-wen" or "anniversary:zhang-xun" ||
            entry.GeneralId is "anniversary:mu-shun" or "anniversary:sun-huan" or "anniversary:xiahou-jie" ||
            entry.GeneralId is "anniversary:cai-yang" or "anniversary:xing-dao-rong" or "anniversary:zhu-zhi" ||
            entry.GeneralId is "anniversary:han-song" or "anniversary:xiahou-hui" or "anniversary:xun-xu" ||
            entry.GeneralId is "anniversary:zhang-huan" or "anniversary:che-zhou" or "anniversary:wang-chang" ||
            entry.GeneralId is "anniversary:wu-fu" or "anniversary:ma-tie" or "anniversary:yan-xiang" ||
            entry.GeneralId is "anniversary:cui-lie" or "anniversary:hou-cheng" or "anniversary:ma-miao" ||
            entry.GeneralId.StartsWith("wumiao:", StringComparison.Ordinal) || entry.GeneralId is "mou:huang-yueying-ol" or "mou:zhou-yu-ol" or "mou:huang-gai-mobile" or "mou:hua-xiong-mobile" or "mou:lu-meng-mobile" or "mou:yu-jin-mobile" or "mou:liu-cheng-mobile" or "mou:xu-huang-mobile" or "mou:chen-gong-mobile" or "mou:xiahou-shi-mobile" or "mou:zhen-ji-mobile" or "mou:yuan-shao-mobile" or "mou:zhu-rong-mobile" or "mou:pang-tong-mobile" or "mou:tian-yu-mobile" or "mou:han-dang-mobile" or "mou:meng-huo-mobile" or "mou:lu-zhi-mobile" or "mou:zhou-yu-mobile" or "mou:xiahou-dun-mobile" or "mou:da-qiao-mobile" or "mou:huang-yueying-mobile" or "mou:lu-bu-mobile" or "mou:diao-chan-mobile" or "mou:fa-zheng-mobile" or "mou:gan-ning-mobile" or "mou:guo-huai-mobile" or "ol:cheng-ji-cheng-cui" or "ol:sp-sun-shangxiang" or "mou:guo-you-zhi-mobile" or "mou:sun-jian-ol" or "mou:taishi-ci-ol" or "mou:gongsun-zan-ol" or "mou:lu-xun-mobile" or "mou:lu-zhi-ol" or "mou:zhang-xiu-ol" or "mou:wen-chou-ol" or "mou:jiang-wei-mobile" or "mou:hua-xiong-ol" or "mou:deng-ai-ol" or "mou:dong-zhao-ol" or "mou:gongsun-zan-mobile" or "mou:zhang-he-mobile" or "mou:xiao-qiao-ol" or "mou:zhu-ran-mobile" or "mou:kong-rong-ol" or "mou:dong-zhuo-ol" or "mou:liu-xie" or "mou:xu-sheng" or "mou:guan-ping" or "mou:sun-shangxiang-mobile" or "mou:jiang-wei" or "mou:dian-wei" or "mou:huang-gai" or "mou:cao-shuang" or "mou:zhuge-jin" or "mou:hu-che-er" or "mou:huang-quan" or "mou:cao-hong" or "mou:zhou-yu" or "mou:lu-su" or "mou:sima-yi" or "mou:jiang-ji" or "mou:sima-shi" or "mou:jia-xu" or "mou:cao-ang" or "mou:zhang-xiu" or "mou:cheng-yu" or "mou:guo-jia" or "mou:chen-lin" or "mou:xun-yu" or "mou:ju-shou" or "mou:hu-lie" or "mou:dong-cheng" or "mou:wang-ling" or "mou:cheng-yu-ol" || entry.GeneralId.StartsWith("le:", StringComparison.Ordinal) ||
            entry.GeneralId.StartsWith("ol:mo-", StringComparison.Ordinal) ||
            entry.GeneralId.StartsWith("mobile:", StringComparison.Ordinal) ||
            entry.GeneralId is "ol:han-sui" or "ol:xiahou-ba" or "classic:zhuge-dan" or "ol:he-tai-hou" or
                "ol:wen-pin" or "ol:dong-yun" or "ol:shen-pei" or "ol:sun-lu-yu" or "ol:cheng-yu" or
                "ol:zhang-lu" or "ol:wang-rong" or "ol:shi-xie" or "ol:ma-yun-lu" or "ol:ta-dun" or "ol:yan-bai-hu" or
                "ol:guan-suo" or "ol:wang-lang" or "ol:bao-san-niang" or "ol:deng-zhi" or "ol:cao-ying" or "ol:dong-zhao" or "ol:wu-yan" or "ol:sp-zhang-he" or "ol:xuan-gong-zhu" or "ol:huang-yueying-sp" or "ol:bu-zhi" or "ol:dong-bai" or "ol:zhao-xiang" or "ol:he-qi" or "ol:kan-ze" or "ol:wang-yun" or "ol:zhuge-guo" or "ol:xi-zhi-cai" or "ol:bei-mi-hu" or "ol:liu-qi" or "ol:lu-zhi-wei" or "ol:sima-hui" or "ol:xu-rong" or "ol:li-jue" or "ol:zhang-ji" or "ol:su-fei" or "ol:huang-quan" or "ol:tang-zi" or "ol:fan-chou" or "ol:guo-si" or "ol:zhang-qi-ying" or "ol:cao-chun" or "ol:wang-can" or "ol:pang-tong-sp" or "ol:yuan-tan-yuan-shang" or "ol:lv-qian" or "ol:lv-kuang-lv-xiang" or "ol:xin-pi" or "ol:hua-man" or "ol:niu-jin" or "ol:duan-wei" or "mobile:liu-zan" or "ol:ding-yuan" or "ol:cao-song" or "ol:bian-fu-ren" or "ol:zhang-ling" or "ol:cao-shuang" or "ol:sp-zhang-liao" or "ol:wo-long-feng-chu" or "ol:sun-ru" or "ol:liu-pi" or "ol:gao-gan" or "ol:huo-jun" or "ol:qiao-zhou" or "ol:du-xi" or "ol:fu-rong" or "ol:zhang-yi" or "ol:yan-pu" or "ol:luo-xian" or "ol:hao-pu" or "ol:ma-wan" or "ol:du-fu-ren" or "ol:fei-yi" or "ol:zhang-shi-ping" or "ol:qian-zhao" or "ol:ding-shang-wan" or "ol:he-jin" or "ol:li-wan" or "ol:yan-rou" or "ol:cao-xi" or "ol:hu-jin-ding" or "ol:lu-yu-sheng" or "ol:wang-kuang" or "ol:yuan-huan" or "ol:zhang-yan" or "ol:wu-an-guo" or "ol:han-fu" or "ol:zhu-jun" or "ol:ke-bi-neng" or "ol:niu-fu" or "ol:duan-jiong" or "ol:ma-mi-di" or "ol:ruan-hui" or "ol:tian-chou" or "ol:cheng-gong-ying" or "ol:ma-xiu-ma-tie" or "ol:pei-yuan-shao" or "ol:wu-jing" or "ol:xiang-chong" or "ol:yang-feng" or "ol:qin-lang" or "ol:a-hui-nan" or "ol:wang-guan" or "ol:liu-yan" or "ol:meng-da" or "ol:cai-mao" or "ol:ya-dan" or "ol:cao-teng" or "ol:yang-fu" or "ol:wu-tu-gu" or "ol:sun-hao" or "ol:da-qiao-xiao-qiao" or "ol:fan-jiang-zhang-da" or "ol:wen-yang" or "ol:ma-yuan-yi" or "ol:hu-ban" or "ol:liu-ba" or "ol:liu-pan" or "ol:qu-huang" or "ol:ruan-yu" or "ol:cao-yu" or "ol:bao-xin" or "ol:guo-tu" or "ol:cui-yan" or "ol:li-yi" or "ol:sun-yuan" or "ol:liu-zhang" or "ol:tang-tang" or "ol:yuan-ji" or "ol:kong-shu" or "ol:ma-cheng" or "ol:zhou-qun" or "ol:guan-hai" or "ol:xie-ling-yu" or "ol:li-feng" or "ol:cai-fu-ren" or "ol:gu-yong" or "ol:huan-shu-jun" or "ol:zhao-zhong" or "ol:tao-qian" or "ol:pang-ji" or "ol:cao-xian" or "ol:lu-shi" or "anniversary:ruan-ji").ToArray();
        Program.Assert(entries.Where(entry => entry.GeneralId.StartsWith("le:", StringComparison.Ordinal)).All(entry =>
                CardGame.Wpf.Presentation.GeneralGalleryCatalog.Classify(entry.GeneralId) is { Id: "le", SeriesId: "le" }),
            "Every registered Le edition belongs to its selectable Le series with its distinct edition identity.");
        Program.Assert(entries.Where(entry => entry.GeneralId.StartsWith("wumiao:", StringComparison.Ordinal)).All(entry =>
                CardGame.Wpf.Presentation.GeneralGalleryCatalog.Classify(entry.GeneralId) is { Id: "wumiao", SeriesId: "wumiao" }),
            "Every registered Wumiao edition belongs to its selectable Wumiao series.");
        Program.Assert(CardGame.Wpf.Presentation.GeneralGalleryCatalog.Classify("wumiao:guan-yu") is { Id: "wumiao", SeriesId: "wumiao" } &&
            model.GeneralGallerySeries.Any(series => series.Id == "wumiao"),
            "Wu Guan Yu has a selectable Wumiao series and retains his distinct edition.");
        var olMagicEntries = entries.Where(entry => entry.GeneralId.StartsWith("ol:mo-", StringComparison.Ordinal)).ToArray();
        Program.Assert(olMagicEntries.Any(entry => entry.GeneralId == "ol:mo-sun-quan") &&
            model.GeneralGallerySeries.Any(series => series.Id == "magic") &&
            olMagicEntries.All(entry => CardGame.Wpf.Presentation.GeneralGalleryCatalog.Classify(entry.GeneralId)
                is { Id: "magic", SeriesId: "magic" }),
            "Official OL magic editions must expose their distinct selectable Magic series.");
        Program.Assert(entries.Any(entry => entry.GeneralId == "wumiao:guan-yu") &&
            entries.Any(entry => entry.GeneralId == "wumiao:lu-xun") &&
            entries.Any(entry => entry.GeneralId == "wumiao:zhang-fei") &&
            entries.Any(entry => entry.GeneralId == "le:cai-wen-ji") &&
            entries.Any(entry => entry.GeneralId == "le:cai-yong") &&
            entries.Any(entry => entry.GeneralId == "le:cao-zhi") &&
            entries.Any(entry => entry.GeneralId == "le:zhou-fei") &&
            entries.Any(entry => entry.GeneralId == "le:mi-heng") &&
            entries.Any(entry => entry.GeneralId == "le:zou-shi") &&
            entries.Any(entry => entry.GeneralId == "le:diao-chan") &&
            entries.Any(entry => entry.GeneralId == "mou:zhou-yu-ol") &&
            entries.Any(entry => entry.GeneralId == "mou:huang-gai-mobile") &&
            entries.Any(entry => entry.GeneralId == "mou:hua-xiong-mobile") &&
            entries.Any(entry => entry.GeneralId == "mou:lu-meng-mobile") &&
            entries.Any(entry => entry.GeneralId == "mou:yu-jin-mobile") &&
            entries.Any(entry => entry.GeneralId == "mou:liu-cheng-mobile") &&
            entries.Any(entry => entry.GeneralId == "mou:xu-huang-mobile") &&
            entries.Any(entry => entry.GeneralId == "mou:chen-gong-mobile") &&
            entries.Any(entry => entry.GeneralId == "mou:xiahou-shi-mobile") &&
            entries.Any(entry => entry.GeneralId == "mou:huang-yueying-ol") &&
            entries.Any(entry => entry.GeneralId.StartsWith("mobile:", StringComparison.Ordinal)) &&
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
            entries.Any(entry => entry.GeneralId == "ol:ma-cheng") &&
            entries.Any(entry => entry.GeneralId == "ol:zhou-qun") &&
            entries.Any(entry => entry.GeneralId == "ol:guan-hai") &&
            entries.Any(entry => entry.GeneralId == "ol:xie-ling-yu") &&
            entries.Any(entry => entry.GeneralId == "ol:li-feng") &&
            entries.Any(entry => entry.GeneralId == "ol:cai-fu-ren") &&
            entries.Any(entry => entry.GeneralId == "ol:gu-yong") &&
            entries.Any(entry => entry.GeneralId == "ol:huan-shu-jun") &&
            entries.Any(entry => entry.GeneralId == "ol:zhao-zhong") &&
            entries.Any(entry => entry.GeneralId == "ol:tao-qian") &&
            entries.Any(entry => entry.GeneralId == "ol:pang-ji") &&
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
                    $"Searching {entry.GeneralId} must retain its playable entry and registered artwork.");
                model.SelectGeneralGalleryEntryCommand.Execute(entry);
                if (entry.GeneralId.StartsWith("anniversary:", StringComparison.Ordinal))
                    Program.Assert(entry.GroupName.Contains("十周年", StringComparison.Ordinal),
                        "Anniversary details must visibly identify their rules edition alongside existing OL variants.");
                Program.Assert(model.SelectedGeneralGalleryEntry == entry &&
                    entry.Portrait.Brush is ImageBrush { ImageSource: not null } &&
                    entry.Skills.Count > 0 && entry.Skills.All(skill => !string.IsNullOrWhiteSpace(skill.Name)),
                    "Opening edition details must decode its own local portrait and show the registered skills.");
                if (entry.GeneralId == "wumiao:lu-xun")
                {
                    Program.Assert(entry.Name == "武陆逊" && entry.Skills.Count == 3 &&
                        new[] { "wumiao:xiongmu", "wumiao:zhangcai", "wumiao:ruxian" }.All(id =>
                        {
                            var registered = registry.GetSkill(id);
                            return entry.Skills.Any(skill => skill.Name == registered.Name &&
                                skill.Description == registered.Description);
                        }), "Wu Lu Xun must show all three complete printed skills with his distinct artwork.");
                    Program.Assert(CardGame.Wpf.Presentation.GeneralGalleryCatalog.Classify(entry.GeneralId)
                        is { Id: "wumiao", SeriesId: "wumiao" }, "Wu Lu Xun must belong to the selectable Wumiao series.");
                }
                if (entry.GeneralId == "mou:zhou-yu-ol")
                {
                    Program.Assert(entry.Name == "谋周瑜·OL" && entry.Skills.Count == 2 &&
                        new[] { "mou-ol:zuifeng", "mou-ol:guqu" }.All(id =>
                        {
                            var registered = registry.GetSkill(id);
                            return entry.Skills.Any(skill => skill.Name == registered.Name &&
                                skill.Description == registered.Description);
                        }), "OL Mou Zhou Yu must show both complete current-client rules with his distinct artwork.");
                    Program.Assert(CardGame.Wpf.Presentation.GeneralGalleryCatalog.Classify(entry.GeneralId)
                        is { Id: "mou", SeriesId: "mou" }, "OL Mou Zhou Yu must belong to the selectable Mou series.");
                }
                if (entry.GeneralId == "mou:huang-yueying-ol")
                {
                    Program.Assert(entry.Name == "谋黄月英·OL" && entry.Skills.Count == 2 &&
                        new[] { "mou-ol:lixian", "mou-ol:bingcai" }.All(id =>
                        {
                            var registered = registry.GetSkill(id);
                            return entry.Skills.Any(skill => skill.Name == registered.Name &&
                                skill.Description == registered.Description);
                        }), "OL Mou Huang Yueying must show both complete printed skills with her distinct artwork.");
                    Program.Assert(CardGame.Wpf.Presentation.GeneralGalleryCatalog.Classify(entry.GeneralId)
                        is { Id: "mou", SeriesId: "mou" }, "The distinct OL edition must be selectable in the Mou series.");
                }
                if (entry.GeneralId == "le:diao-chan")
                {
                    Program.Assert(entry.Name == "乐貂蝉" && entry.Skills.Count == 2 &&
                        new[] { "le:tanban", "le:diou" }.All(id =>
                        {
                            var registered = registry.GetSkill(id);
                            return entry.Skills.Any(skill => skill.Name == registered.Name && skill.Description == registered.Description);
                        }), "Le Diao Chan must show both complete primary rules and her distinct official portrait.");
                }
                if (entry.GeneralId == "le:zou-shi")
                {
                    Program.Assert(entry.Name == "乐邹氏" && entry.Skills.Count == 2 &&
                        new[] { "le:yunzheng", "le:huoxin" }.All(id =>
                        {
                            var registered = registry.GetSkill(id);
                            return entry.Skills.Any(skill => skill.Name == registered.Name &&
                                skill.Description == registered.Description);
                        }), "Le Zou Shi must show both complete registered skills with her own edition portrait.");
                }
                if (entry.GeneralId == "ol:mo-sun-quan")
                {
                    Program.Assert(entry.Name == "魔孙权" && entry.Skills.Count == 3 &&
                        new[] { "ol:mo-quanyu", "ol:mo-tianen", "ol:mo-qiangang" }.All(id =>
                        {
                            var registered = registry.GetSkill(id);
                            return entry.Skills.Any(skill => skill.Name == registered.Name &&
                                skill.Description == registered.Description);
                        }), "Mo Sun Quan's actual details show exactly the complete three printed skills.");
                    Program.Assert(entry.Skills.Single(skill => skill.Name == "乾纲").Description.Contains(
                        registry.GetSkill("ol:mo-rumo").Description, StringComparison.Ordinal),
                        "Qiangang must visibly explain the complete shared Rumo rule without a fourth printed skill.");
                }
                if (entry.GeneralId == "ol:li-feng")
                    Program.Assert(entry.Skills.Count == 2 && new[] { "ol:jiyun", "ol:shuliang" }.All(id =>
                    {
                        var registered = registry.GetSkill(id);
                        return entry.Skills.Any(skill => skill.Name == registered.Name && skill.Description == registered.Description);
                    }), "Li Feng's actual gallery details must retain both complete registered skill descriptions.");
                if (entry.GeneralId == "ol:cai-fu-ren")
                    Program.Assert(entry.Skills.Count == 2 && new[] { "ol:xianzhou", "ol:qieting" }.All(id =>
                    {
                        var registered = registry.GetSkill(id);
                        return entry.Skills.Any(skill => skill.Name == registered.Name && skill.Description == registered.Description);
                    }), "Cai Fu Ren's actual gallery details must retain both complete registered skill descriptions.");
                if (entry.GeneralId == "ol:gu-yong")
                    Program.Assert(entry.Skills.Count == 2 && new[] { "ol:shenxing", "ol:bingyi" }.All(id =>
                    {
                        var registered = registry.GetSkill(id);
                        return entry.Skills.Any(skill => skill.Name == registered.Name && skill.Description == registered.Description);
                    }), "Ordinary Gu Yong's actual gallery details must retain both complete registered skill descriptions.");
                if (entry.GeneralId == "ol:huan-shu-jun")
                    Program.Assert(entry.Name == "环淑君" && entry.Skills.Count == 3 &&
                        new[] { "ol:huanpei", "ol:wenyi", "ol:miaoxin" }.All(id =>
                    {
                        var registered = registry.GetSkill(id);
                        return entry.Skills.Any(skill => skill.Name == registered.Name && skill.Description == registered.Description);
                    }), "Huan Shu Jun's actual gallery details must retain her name and all three complete registered skill descriptions.");
                if (entry.GeneralId == "ol:pang-ji")
                    Program.Assert(entry.Name == "逢纪" && entry.Skills.Count == 2 &&
                        new[] { "ol:biguo", "ol:douyu" }.All(id =>
                    {
                        var registered = registry.GetSkill(id);
                        return entry.Skills.Any(skill => skill.Name == registered.Name && skill.Description == registered.Description);
                    }), "Pang Ji's actual gallery retains both complete registered skill descriptions.");
                if (entry.GeneralId == "ol:tao-qian")
                    Program.Assert(entry.Name == "陶谦" && entry.Skills.Count == 3 &&
                        new[] { "ol:zongluan", "ol:zhaohuo", "ol:wenren" }.All(id =>
                    {
                        var registered = registry.GetSkill(id);
                        return entry.Skills.Any(skill => skill.Name == registered.Name && skill.Description == registered.Description);
                    }), "Tao Qian's actual gallery details must retain his name and all three complete registered skill descriptions.");
                if (entry.GeneralId == "ol:zhao-zhong")
                {
                        Program.Assert(entry.Name == "赵忠" && entry.Skills.Count == 2 &&
                            new[] { "ol:pengbi", "ol:dici" }.All(id =>
                        {
                            var registered = registry.GetSkill(id);
                            return entry.Skills.Any(skill => skill.Name == registered.Name && skill.Description == registered.Description);
                        }), "Zhao Zhong's actual gallery details retain his name and both complete registered skill descriptions, including linked derived skill text.");
                    var pengbi = entry.Skills.Single(skill => skill.Name == registry.GetSkill("ol:pengbi").Name);
                    Program.Assert(new[] { "ol:yintian", "ol:biri" }.All(id =>
                        pengbi.Description.Contains(registry.GetSkill(id).Name, StringComparison.Ordinal) &&
                        pengbi.Description.Contains(registry.GetSkill(id).Description, StringComparison.Ordinal)),
                        "Pengbi's visible details must include both full derived skill rules.");
                }
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
