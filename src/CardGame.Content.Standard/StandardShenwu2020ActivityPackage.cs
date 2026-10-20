using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>OL May 2020 eight-beast encounter. Enemy tiers are explicit local room choices.</summary>
public sealed class StandardShenwu2020ActivityPackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new("shenwu-2020-activity", new Version(1, 0, 0),
        [new("shenwu-activity", new StandardShenwuActivityPackage().Manifest.Version)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shenwu-expanded", skill => skill with
        { Tags = skill.Id == "shenwu:kunshou" ? SkillTag.Awakening : skill.Id == "shenwu:chongsheng-2020" ? SkillTag.Limited : SkillTag.Locked });

        Add("hundun", "混沌", 25, null, "shenwu:xiongshou", "shenwu:wuzang", "shenwu:xiangde");
        Add("qiongqi", "穷奇", 25, 20, "shenwu:xiongshou", "shenwu:zhue", "shenwu:futai");
        Add("taowu", "梼杌", 20, null, "shenwu:xiongshou", "shenwu:mingwan", "shenwu:nitai");
        Add("taotie", "饕餮", 25, null, "shenwu:xiongshou", "shenwu:tanyu", "shenwu:cangmu");
        Add("xiangliu", "相柳", 25, null, "shenwu:yaoshou", "shenwu:duqu", "shenwu:jiushou");
        // 25/30 is initial HP / maximum HP, including Juyuan's printed +5 maximum.
        Add("zhuyan", "朱厌", 30, 25, "shenwu:yaoshou", "shenwu:bingxian", "shenwu:juyuan");
        Add("bifang", "毕方", 25, null, "shenwu:yaoshou", "shenwu:zhaohuo", "shenwu:honglian");
        Add("yingzhao", "英招", 25, null, "shenwu:yaoshou", "shenwu:fengdong", "shenwu:xunyou");

        var banned = new HashSet<string>(StringComparer.Ordinal)
        { "kan-ze", "diao-chan", "cai-wen-ji", "wang-yun", "zuo-ci", "yu-ji", "da-qiao", "xu-huang",
          "ling-ju", "bu-zhi", "ma-yun-lu", "huang-hao", "cheng-yu", "wang-lang", "guo-huang-hou", "guo-huanghou",
          "zhang-rang", "xun-yu", "xiao-qiao", "da-qiao-xiao-qiao", "fa-zheng", "man-chong", "han-hao-shi-huan" };
        var ordinary = CharacterContentCatalog.Default.GetPoolGenerals("standard-classic-generals", "current")
            .Where(g => g.FactionId != "god" && !banned.Any(id => g.CharacterId == $"character:{id}" || g.Id.EndsWith($":{id}", StringComparison.Ordinal)) &&
                g.Id is not ("standard:yuan-shu" or "classic:yuan-shu" or "ol:sp-jia-xu" or "sp:guan-yu"))
            .Select(g => g.Id).ToArray();
        string[] gods = ["classic:shen-guan-yu", "classic:shen-lu-meng", "classic:shen-zhou-yu", "ol:shen-zhuge-liang",
            "classic:shen-cao-cao", "classic:shen-lu-bu", "classic:shen-sima-yi", "ol:shen-zhao-yun"];
        string[] leaders = ["shenwu2020:hundun", "shenwu2020:qiongqi", "shenwu2020:taowu", "shenwu2020:taotie",
            "shenwu2020:xiangliu", "shenwu2020:zhuyan", "shenwu2020:bifang", "shenwu2020:yingzhao"];
        var half = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [leaders[0]] = "shenwu:yinzei", [leaders[1]] = "shenwu:yandu", [leaders[2]] = "shenwu:luanchang", [leaders[3]] = "shenwu:jicai",
            [leaders[4]] = "shenwu:echou", [leaders[5]] = "shenwu:xushi", [leaders[6]] = "shenwu:yanyu", [leaders[7]] = "shenwu:sipu"
        };
        for (var tier = 1; tier <= 5; tier++)
            builder.AddMode(new($"activity:shenwu-2020-tier-{tier}", $"神武再世·OL 2020·敌方{tier}阶", 6, 6,
                new Dictionary<string, int>(), "classic:standard-deck", 5,
                ordinary.Concat(gods).Concat(leaders).Concat(["shenwu:zhuyin-left", "shenwu:zhuyin-right"]).ToArray(),
                ContentModeKind.Team, new Dictionary<string, int> { ["team:allies"] = 3, ["team:beasts"] = 3 })
            {
                ShenwuActivity = new(ordinary, gods, leaders, ["shenwu:zhuyin-left", "shenwu:zhuyin-right"], [0, 1, 2], 4, [3, 5],
                    5, ["shenwu:coalition", "shenwu:chongsheng-2020", "shenwu:player-tier-2020"], half)
                { Edition = 2020, EnemyTier = tier }
            });

        void Add(string id, string name, int maximum, int? initial, params string[] skills) =>
            builder.AddGeneral(new($"shenwu2020:{id}", name, "", skills[0], "qun", maximum, skills.Skip(1).ToArray())
            { InitialHp = initial, CharacterId = $"character:shenwu-{id}", VariantId = "shenwu-2020", RulesetId = "shenwu-2020", Gender = GeneralGender.Male });
    }
}
