using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The screenshot ruleset: five-tier players and difficulty-scaled NPCs.</summary>
public sealed class StandardJiangeActivityPackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new("jiange-activity", new Version(1, 0, 0),
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "jiange-activity", definition => definition with
        {
            Tags = definition.Id == "jiange:niepan" ? SkillTag.Limited :
                definition.Id is "jiange:jiguan" or "jiange:yizhong" or "jiange:yuhuo" or "jiange:fuyou" ? SkillTag.Locked : SkillTag.None
        });
        foreach (var (id, name, description) in new[]
        {
            ("chiying", "持盈", "锁定技，友方角色受到的伤害至多为1。"),
            ("jingfan", "惊帆", "锁定技，友方其他角色计算与敌方角色的距离-1。"),
            ("jueji", "绝汲", "锁定技，敌方已受伤角色摸牌阶段少摸一张牌。")
        }) builder.AddSkill(new($"jiange:{id}", name, description) { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State });

        Add("jia-ren-zi-dan", "佳人子丹", "wei", "cao_zhen", 5, "chiying", "jingfan");
        Add("qiao-kui-jun-yi", "巧魁儁乂", "wei", "zhang_he", 4, "huodi", "jueji");
        Add("lie-di-xuan-de", "烈帝玄德", "shu", "liu_bei", 5, "jizhen", "lingfeng");
        Add("yu-huo-shi-yuan", "浴火士元", "shu", "pang_tong", 4, "yuhuo", "qiwu", "tianyu");
        Add("fu-di-bi-an", "缚地狴犴", "wei", "", 5, "jiguan", "didong");
        Add("shi-huo-suan-ni", "食火狻猊", "wei", "", 5, "jiguan", "lianyu");
        Add("chi-yu-zhu-que", "炽羽朱雀", "shu", "", 5, "jiguan", "yuhuo", "tianyun");
        Add("ling-jia-xuan-wu", "灵甲玄武", "shu", "", 5, "jiguan", "yizhong", "lingyu");

        var bannedFamilies = new HashSet<string>(StringComparer.Ordinal)
        {
            "character:cai-wen-ji", "character:xu-huang", "character:huang-hao", "character:cheng-yu",
            "character:guo-huang-hou", "character:guo-huanghou", "character:xun-yu", "character:fa-zheng", "character:man-chong",
            "character:han-hao-shi-huan", "character:xiahou-ba", "character:kuai-yue-kuai-liang",
            "character:yan-yan", "character:sima-shi"
        };
        var roster = CharacterContentCatalog.Default.GetPoolGenerals("standard-classic-generals", "current")
            .Where(g => g.FactionId is "wei" or "shu" &&
                !bannedFamilies.Contains(g.CharacterId ?? $"character:{g.Id[(g.Id.IndexOf(':') + 1)..]}") &&
                g.Id is not ("sp:guan-yu" or "ol:ma-yun-lu")).ToArray();
        var players = Pools(roster.Where(g => g.FactionId == "wei").Select(g => g.Id).ToArray(),
            roster.Where(g => g.FactionId == "shu").Select(g => g.Id).ToArray());
        var spirits = Pools(["jiange:jia-ren-zi-dan", "jiange:qiao-kui-jun-yi"], ["jiange:lie-di-xuan-de", "jiange:yu-huo-shi-yuan"]);
        var machines = Pools(["jiange:fu-di-bi-an", "jiange:shi-huo-suan-ni"], ["jiange:chi-yu-zhu-que", "jiange:ling-jia-xuan-wu"]);
        var skillIds = Pools(["jiange:niepan", "jiange:kuanggu"], ["jiange:niepan", "jiange:fuyou"]);
        var generalPool = players.Values.Concat(spirits.Values).Concat(machines.Values).SelectMany(ids => ids).ToArray();
        foreach (var (id, label, min, max, machine) in new[]
        {
            ("normal", "普通", 1, 1, 1), ("advanced", "进阶", 2, 4, 2), ("difficult", "困难", 3, 5, 3)
        }) builder.AddMode(new($"activity:jiange-{id}", $"剑阁决战·{label}", 8, 8, new Dictionary<string, int>(),
            "classic:standard-deck", 8, generalPool, ContentModeKind.Team,
            new Dictionary<string, int> { ["team:wei"] = 4, ["team:shu"] = 4 })
        {
            TeamActivity = new(players, spirits, machines, 5, min, max, machine, skillIds)
        });

        void Add(string id, string name, string faction, string portrait, int hp, params string[] skills) =>
            builder.AddGeneral(new($"jiange:{id}", name, portrait, $"jiange:{skills[0]}", faction, hp,
                skills.Skip(1).Select(s => $"jiange:{s}").ToArray())
            { CharacterId = $"character:jiange-{id}", VariantId = "jiange", RulesetId = "jiange-screenshot" });
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Pools(IReadOnlyList<string> wei, IReadOnlyList<string> shu) =>
        new Dictionary<string, IReadOnlyList<string>> { ["team:wei"] = wei, ["team:shu"] = shu };
}
