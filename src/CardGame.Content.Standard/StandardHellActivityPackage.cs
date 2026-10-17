using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>OL 2020 four-stage Ten Kings campaign. Boss tiers are explicit local choices.</summary>
public sealed class StandardHellActivityPackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new("hell-activity", new Version(1, 0, 0),
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        foreach (var bundle in new[] { "hell-kings", "hell-mengpo", "hell-dizang", "hell-activity" })
            EmbeddedSkillProgramCatalog.RegisterBundle(builder, bundle, skill => skill with
            {
                Tags = skill.Id == "hell:chongsheng" ? SkillTag.Limited :
                    skill.Id is "hell:fankui" or "hell:shiyou" or "hell:dayuan" or "hell:return-soul-resolution"
                        ? SkillTag.None : SkillTag.Locked
            });
        Add("qinguang", "秦广王", 3, "panguan", "juhun", "wangxiang");
        Add("chujiang", "楚江王", 4, "weimu", "fankui", "bingfeng");
        Add("songdi", "宋帝王", 4, "heisheng", "shengfu", "enyuan");
        Add("wuguan", "五官王", 4, "zhiwang", "gongzheng", "xuechi");
        Add("yanluo", "阎罗王", 4, "tiemian", "zhadao", "zhuxin");
        Add("biancheng", "卞城王", 5, "leizhou", "leifu", "leizhu");
        Add("taishan", "泰山王", 5, "fudu", "kujiu", "renao");
        Add("dushi", "都市王", 5, "remen", "zhifen", "huoxing");
        Add("pingdeng", "平等王", 5, "suozu", "abi", "pingdeng");
        Add("zhuanlun", "转轮王", 6, "modao", "lunhui", "wangsheng", "fanshi");
        Add("mengpo", "孟婆", 3, "shiyou", "wanghun", "wangshi");
        Add("dizang", "地藏王", 8, "bufo", "wuliang", "dayuan", "diting");
        builder.AddCard(new("hell:return-soul", "回魂", "锦囊", "恢复本局因孟婆忘魂失去的技能，进入弃牌堆后销毁。", CardKind.ReturnSoul));

        var bannedFamilies = new HashSet<string>(StringComparer.Ordinal)
        { "cai-wen-ji", "zuo-ci", "yu-ji", "huang-hao", "zhang-chun-hua", "lu-xun" };
        var bannedVersions = new HashSet<string>(StringComparer.Ordinal)
        { "classic:cai-wen-ji", "classic:zuo-ci", "classic:yu-ji", "classic:yuan-shu", "sp:yuan-shu",
            "ol:sp-yuan-shu", "classic:huang-hao", "classic:zhang-chun-hua", "classic:lu-xun",
            "sp:jia-xu", "ol:sp-jia-xu", "sp:guan-yu", "ol:sp-guan-yu", "sp:ling-ju", "ol:sp-ling-ju",
            "sp:bu-zhi", "ol:sp-bu-zhi", "ol:ling-ju", "ol:bu-zhi", "classic:shen-zhao-yun" };
        var players = CharacterContentCatalog.Default.GetPoolGenerals("standard-classic-generals", "current")
            .Where(g => !bannedVersions.Contains(g.Id) && !bannedFamilies.Any(family =>
                g.CharacterId == $"character:{family}" || g.Id.EndsWith($":{family}", StringComparison.Ordinal)))
            .Select(g => g.Id).Append("ol:shen-zhao-yun").Distinct(StringComparer.Ordinal).ToArray();
        BossActivityStage[] stages = [new("秦广王", ["hell:qinguang"]),
            new("第二殿", ["hell:chujiang", "hell:songdi", "hell:wuguan", "hell:yanluo"]),
            new("第三殿", ["hell:biancheng", "hell:taishan", "hell:dushi", "hell:king-pingdeng"]),
            new("轮回之殿", ["hell:zhuanlun", "hell:dizang"])];
        for (var tier = 1; tier <= 5; tier++)
            builder.AddMode(new($"activity:hell-2020-tier-{tier}", $"十殿阎罗·敌方{tier}阶", 5, 5,
                new Dictionary<string, int>(), "classic:standard-deck", 5,
                players.Concat(stages.SelectMany(stage => stage.GeneralPoolIds)).Append("hell:mengpo").ToArray(),
                ContentModeKind.Team, new Dictionary<string, int> { ["team:allies"] = 3, ["team:hell"] = 2 })
            {
                BossActivity = new(players, stages, 5, ["hell:player-tier", "hell:chongsheng"], EnemyTeamId: "team:hell"),
                HellActivity = new(tier, "hell:mengpo", "hell:dizang")
            });

        void Add(string id, string name, int hp, params string[] skills) => builder.AddGeneral(new(id == "pingdeng" ? "hell:king-pingdeng" : $"hell:{id}", name,
            $"hell-{id}", $"hell:{skills[0]}", "qun", hp,
            skills.Skip(1).Select(skill => $"hell:{skill}").Append("hell:boss-tier").ToArray())
        { CharacterId = $"character:hell-{id}", VariantId = "ol-2020", RulesetId = "hell-2020", Gender = id == "mengpo" ? GeneralGender.Female : GeneralGender.Male });
    }
}
