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
        Add("myth-wind", "xiahou-yuan cao-ren huang-zhong wei-yan xiao-qiao zhou-tai zhang-jiao");
        Add("myth-fire", "dian-wei xun-yu pang-tong wolong-zhuge-liang taishi-ci yuan-shao yan-liang-wen-chou pang-de");
        Add("myth-forest", "cao-pi xu-huang sun-jian meng-huo zhu-rong");
        Add("myth-mountain", "sun-ce");
        Add("fame-1", "yu-jin xu-shu gao-shun");
        Add("fame-2", "cao-zhang wang-yi xun-you zhong-hui ma-dai liao-hua guan-xing-zhang-bao bu-lian-shi cheng-pu han-dang liu-biao");
        Add("fame-3", "cao-chong guo-huai man-chong guan-ping pan-zhang-ma-zhong xu-sheng");
        Add("fame-4", "gu-yong zhu-huan zhang-song");
        Add("fame-5", "zhu-zhi");
        Add("god", "shen-guan-yu shen-sima-yi");
        groups.Add("classic:qu-yi", "other");
        Add("sp", "gongsun-zan");
        groups.Add("sp:le-jin", "sp");
        Add("myth-yin", "yan-yan");
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
        groups.Add("boundary:xu-sheng", "boundary-fame");
        groups.Add("boundary:zhang-song", "boundary-fame");
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
