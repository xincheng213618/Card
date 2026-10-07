namespace CardGame.Wpf.Presentation;

// Gallery organization is presentation metadata, independent of rule-package IDs and save fingerprints.
// Classification sources and version choices: docs/GENERAL_GALLERY.md.
public static class GeneralGalleryCatalog
{
    public static IReadOnlyList<GallerySeries> Series { get; } =
    [
        new("all", "全部", "浏览全部武将"),
        new("standard", "标准", "标准版武将"),
        new("myth", "神话再临", "风 · 火 · 林 · 山 · 阴 · 雷"),
        new("fame", "一将成名", "一将成名 · 一至七"),
        new("boundary", "界限突破", "标准与神话再临的界限突破武将"),
        new("boundary-fame", "界一将", "一将成名的界限突破武将"),
        new("god", "神武将", "神将专属图鉴"),
        new("sp", "SP", "SP 武将"),
        new("mou", "谋", "谋系列武将"),
        new("other", "其他扩展", "其他扩展武将")
    ];

    public static IReadOnlyList<GalleryGroup> Groups { get; } =
    [
        new("standard", "standard", "标准", "标准版"),
        new("myth-wind", "myth", "风", "神话再临 · 风"),
        new("myth-fire", "myth", "火", "神话再临 · 火"),
        new("myth-forest", "myth", "林", "神话再临 · 林"),
        new("myth-mountain", "myth", "山", "神话再临 · 山"),
        new("myth-yin", "myth", "阴", "神话再临 · 阴"),
        new("myth-thunder", "myth", "雷", "神话再临 · 雷"),
        new("fame-1", "fame", "一将 · 一", "一将成名 · 一"),
        new("fame-2", "fame", "一将 · 二", "一将成名 · 二"),
        new("fame-3", "fame", "一将 · 三", "一将成名 · 三"),
        new("fame-4", "fame", "一将 · 四", "一将成名 · 四"),
        new("fame-5", "fame", "一将 · 五", "一将成名 · 五"),
        new("fame-6", "fame", "一将 · 六", "一将成名 · 六"),
        new("fame-7", "fame", "一将 · 七", "一将成名 · 七"),
        new("boundary", "boundary", "界限突破", "界限突破"),
        new("boundary-fame", "boundary-fame", "界一将", "界一将"),
        new("god", "god", "神武将", "神武将"),
        new("sp", "sp", "SP", "SP 武将"),
        new("mou", "mou", "谋", "谋系列"),
        new("other", "other", "其他", "其他扩展")
    ];

    private static readonly IReadOnlyDictionary<string, string> GeneralGroups = BuildGeneralGroups();

    public static bool IsVisible(string generalId) =>
        !generalId.StartsWith("standard:", StringComparison.Ordinal) &&
        !generalId.StartsWith("composed:", StringComparison.Ordinal) &&
        !generalId.StartsWith("national:", StringComparison.Ordinal);

    public static GalleryGroup Classify(string generalId)
    {
        if (!GeneralGroups.TryGetValue(generalId, out var groupId))
        {
            groupId = generalId.Split(':', 2)[0] switch
            {
                "sp" => "sp",
                "mou" => "mou",
                // New formal variants need an explicit assignment; never guess from a shared name.
                _ => "other"
            };
        }
        return Groups.Single(group => group.Id == groupId);
    }

    private static IReadOnlyDictionary<string, string> BuildGeneralGroups()
    {
        var groups = new Dictionary<string, string>(StringComparer.Ordinal);
        Add("standard", "liu-bei guan-yu zhang-fei zhuge-liang zhao-yun ma-chao huang-yueying sun-quan gan-ning lu-meng huang-gai zhou-yu da-qiao lu-xun sun-shangxiang cao-cao sima-yi xiahou-dun zhang-liao xu-chu guo-jia zhen-ji hua-tuo lu-bu diao-chan hua-xiong");
        Add("myth-wind", "xiahou-yuan cao-ren huang-zhong wei-yan xiao-qiao zhou-tai zhang-jiao yu-ji");
        Add("myth-fire", "dian-wei xun-yu pang-tong wolong-zhuge-liang taishi-ci yuan-shao yan-liang-wen-chou pang-de");
        Add("myth-forest", "cao-pi xu-huang sun-jian meng-huo zhu-rong lu-su dong-zhuo jia-xu");
        Add("myth-mountain", "sun-ce cai-wen-ji deng-ai jiang-wei zhang-he liu-shan zhang-zhao-zhang-hong zuo-ci");
        Add("fame-1", "yu-jin xu-shu gao-shun cao-zhi zhang-chun-hua ling-tong chen-gong wu-guo-tai fa-zheng ma-su");
        Add("fame-2", "cao-zhang wang-yi xun-you zhong-hui ma-dai liao-hua guan-xing-zhang-bao bu-lian-shi cheng-pu han-dang liu-biao");
        Add("fame-3", "cao-chong guo-huai man-chong guan-ping pan-zhang-ma-zhong xu-sheng li-ru liu-feng jian-yong yu-fan zhu-ran fu-huanghou");
        Add("fame-4", "gu-yong zhu-huan zhang-song ju-shou cao-zhen han-hao-shi-huan chen-qun wu-yi zhou-cang sun-lu-ban");
        Add("fame-5", "zhu-zhi cao-rui cao-xiu zhong-yao liu-chen xiahou-shi zhang-ni sun-xiu quan-cong gongsun-yuan guo-tu-feng-ji");
        Add("fame-5", "sha-mo-ke");
        Add("fame-6", "guo-huanghou li-yan sun-deng liu-yu cen-hun sun-zi-liu-fang huang-hao zhang-rang");
        Add("fame-7", "xin-xianying wu-xian xu-shi cao-jie ji-kang qin-mi xue-zong cai-yong");
        Add("god", "shen-guan-yu shen-sima-yi shen-lu-meng shen-cao-cao shen-zhao-yun shen-zhou-yu shen-lu-bu");
        foreach (var id in new[] { "guan-yu", "zhou-yu", "zhuge-liang", "lu-bu", "zhao-yun", "sima-yi",
                     "liu-bei", "lu-xun", "gan-ning", "zhang-liao", "sun-quan", "zhang-jiao", "dian-wei", "huang-zhong" })
            groups.Add("ol:shen-" + id, "god");
        groups.Add("ol:sp-pang-de", "other");
        groups.Add("ol:sp-diao-chan", "other");
        groups.Add("ol:sp-jia-xu", "other");
        groups.Add("ol:zhang-bao", "other");
        groups.Add("ol:zhuge-jin", "other");
        groups.Add("ol:zhang-xing-cai", "other");
        groups.Add("ol:zu-mao", "other");
        groups.Add("ol:ding-feng", "other");
        groups.Add("ol:pan-feng", "other");
        groups.Add("ol:ma-liang", "other");
        groups.Add("ol:huang-cheng-yan", "other");
        groups.Add("ol:chen-lin", "other");
        groups.Add("ol:sp-cai-wen-ji", "other");
        groups.Add("ol:sp-cao-ren", "other");
        groups.Add("ol:guan-yin-ping", "other");
        groups.Add("ol:zhuge-ke", "other");
        groups.Add("ol:cao-hong", "other");
        groups.Add("ol:sp-ma-chao", "other");
        groups.Add("ol:fu-wan", "other");
        groups.Add("ol:liu-xie", "other");
        groups.Add("ol:ling-ju", "other");
        groups.Add("ol:yang-xiu", "other");
        groups.Add("classic:qu-yi", "other");
        Add("sp", "gongsun-zan");
        groups.Add("sp:le-jin", "sp");
        Add("myth-yin", "yan-yan wang-ping lu-ji sun-liang xu-you wang-ji kuai-yue-kuai-liang lu-zhi");
        Add("myth-thunder", "hao-zhao zhuge-zhan chen-dao guanqiu-jian lu-kang yuan-shu zhou-fei zhang-xiu");
        groups.Add("boundary:zhang-jiao", "boundary");
        groups.Add("boundary:sima-yi", "boundary");
        groups.Add("boundary:diao-chan", "boundary");
        groups.Add("classic:li-dian", "boundary");
        groups.Add("boundary:guo-jia", "boundary");
        groups.Add("boundary:cao-cao", "boundary");
        groups.Add("boundary:zhang-liao", "boundary");
        groups.Add("boundary:gan-ning", "boundary");
        groups.Add("boundary:xu-chu", "boundary");
        groups.Add("boundary:zhou-yu", "boundary");
        groups.Add("boundary:lu-meng", "boundary");
        groups.Add("boundary:huang-gai", "boundary");
        groups.Add("boundary:lu-xun", "boundary");
        groups.Add("boundary:liu-bei", "boundary");
        groups.Add("boundary:da-qiao", "boundary");
        groups.Add("boundary:hua-tuo", "boundary");
        groups.Add("boundary:guan-yu", "boundary");
        groups.Add("boundary:zhang-fei", "boundary");
        groups.Add("boundary:ma-chao", "boundary");
        groups.Add("boundary:xiahou-dun", "boundary");
        groups.Add("boundary:li-dian", "boundary");
        groups.Add("boundary:zhen-ji", "boundary");
        groups.Add("boundary:huang-yueying", "boundary");
        groups.Add("boundary:zhuge-liang", "boundary");
        groups.Add("boundary:sun-shangxiang", "boundary");
        groups.Add("boundary:huang-zhong", "boundary");
        groups.Add("boundary:wei-yan", "boundary");
        groups.Add("boundary:deng-ai", "boundary");
        groups.Add("boundary:jiang-wei", "boundary");
        groups.Add("boundary:sun-ce", "boundary");
        groups.Add("boundary:zhu-rong", "boundary");
        groups.Add("boundary:meng-huo", "boundary");
        groups.Add("boundary:gongsun-zan", "boundary");
        groups.Add("boundary:hua-xiong", "boundary");
        groups.Add("boundary:lu-bu", "boundary");
        groups.Add("boundary:yuan-shao", "boundary");
        groups.Add("boundary:sun-quan", "boundary");
        groups.Add("boundary:pang-tong", "boundary");
        groups.Add("boundary:xu-shu", "boundary-fame");
        groups.Add("boundary:xun-yu", "boundary");
        groups.Add("boundary:sun-jian", "boundary");
        groups.Add("boundary:xiahou-yuan", "boundary");
        groups.Add("boundary:taishi-ci", "boundary");
        groups.Add("boundary:cao-zhang", "boundary-fame");
        groups.Add("boundary:pang-de", "boundary");
        groups.Add("boundary:fa-zheng", "boundary-fame");
        groups.Add("boundary:han-dang", "boundary-fame");
        groups.Add("boundary:zhang-he", "boundary");
        groups.Add("boundary:cao-chong", "boundary-fame");
        groups.Add("boundary:xu-huang", "boundary");
        groups.Add("boundary:dong-zhuo", "boundary");
        groups.Add("boundary:jia-xu", "boundary");
        groups.Add("boundary:dian-wei", "boundary");
        groups.Add("boundary:cai-wen-ji", "boundary");
        groups.Add("boundary:zhang-chun-hua", "boundary");
        groups.Add("boundary:xu-sheng", "boundary-fame");
        groups.Add("boundary:zhang-song", "boundary-fame");
        groups.Add("boundary:ju-shou", "boundary-fame");
        groups.Add("boundary:guan-xing-zhang-bao", "boundary");
        groups.Add("boundary:zhou-tai", "boundary");
        groups.Add("boundary:cao-ren", "boundary");
        groups.Add("ol:yi-ji", "other");
        groups.Add("ol:huang-zu", "other");
        groups.Add("ol:cao-xing", "other");
        groups.Add("ol:xun-chen", "other");
        groups.Add("ol:deng-zhong", "other");
        groups.Add("ol:liang-xing", "other");
        groups.Add("ol:gao-lan", "other");
        groups.Add("ol:zhang-chang-pu", "other");
        groups.Add("ol:lv-kai", "other");
        groups.Add("ol:zhou-fang", "other");
        groups.Add("ol:yan-jun", "other");
        groups.Add("ol:jiang-gan", "other");
        groups.Add("ol:pan-jun", "other");
        groups.Add("ol:pan-shu", "other");
        groups.Add("ol:yang-yi", "other");
        groups.Add("ol:zhu-ling", "other");
        groups.Add("ol:liu-bian", "other");
        groups.Add("ol:chen-deng", "other");
        groups.Add("ol:qinghe-gongzhu", "other");
        groups.Add("ol:yang-wan", "other");
        groups.Add("ol:rui-ji", "other");
        groups.Add("ol:teng-fang-lan", "other");
        groups.Add("ol:liu-hong", "other");
        return groups;

        void Add(string group, string ids)
        {
            foreach (var id in ids.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                groups.Add("classic:" + id, group);
        }
    }
}

public sealed record GallerySeries(string Id, string Name, string Description);
public sealed record GalleryGroup(string Id, string SeriesId, string Name, string Title);
