using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The four successive three-versus-one encounters published for the 2018 activity.</summary>
public sealed class StandardGhostActivityPackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new("ghost-activity", new Version(1, 0, 0),
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        var locked = new HashSet<string>(StringComparer.Ordinal)
        {
            "guimei", "shanbeng", "enyuan", "beiming", "huilei", "mingbao", "guiji", "guiji-end",
            "baolian-one", "baolian-two", "manjia", "zuijiu", "modao"
        };
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ghost-activity", definition => definition with
        {
            Tags = definition.Id == "ghost:niepan" ? SkillTag.Limited :
                locked.Contains(definition.Id["ghost:".Length..]) ? SkillTag.Locked : SkillTag.None
        });

        Add("chi", "魑", 3, false, "guimei", "didong", "shanbeng");
        Add("mei", "魅", 3, true, "guimei", "enyuan", "beiming");
        Add("wang", "魍", 3, false, "guimei", "luolei", "huilei");
        Add("liang", "魉", 3, true, "guimei", "guihuo", "mingbao");
        Add("ma-mian", "马面", 4, false, "guiji-end", "fankui", "lianyu");
        Add("niu-tou", "牛头", 5, false, "baolian-two", "manjia", "xiaoshou");
        Add("bai-wu-chang", "白无常", 5, false, "baolian-one", "qiangzheng", "zuijiu");
        Add("hei-wu-chang", "黑无常", 5, false, "guiji", "suoming", "xixing");
        Add("luo-cha", "罗刹", 6, true, "modao", "yushou", "moyan");
        Add("ye-cha", "夜叉", 6, false, "modao", "mojian", "danshu");

        var banned = new HashSet<string>(StringComparer.Ordinal)
        {
            "character:cao-pi", "character:man-chong", "character:cai-wen-ji", "character:zuo-ci",
            "character:yu-ji", "character:huang-hao", "character:bu-zhi", "character:ling-ju", "character:wang-lang"
        };
        var roster = CharacterContentCatalog.Default.GetPoolGenerals("standard-classic-generals", "current")
            .Where(g => g.FactionId != "god" && !banned.Contains(g.CharacterId ?? "") &&
                // Historical definitions retain null CharacterId; use exact IDs rather than rewriting their fingerprints.
                g.Id is not ("classic:cao-pi" or "classic:man-chong" or "classic:cai-wen-ji" or "classic:yuan-shu" or
                    "standard:yuan-shu" or "ol:sp-diao-chan" or "ol:sp-jia-xu" or "sp:guan-yu"))
            .Select(g => g.Id).ToArray();
        BossActivityStage[] stages =
        [
            new("魑魅魍魉", ["ghost:chi", "ghost:mei", "ghost:wang", "ghost:liang"]),
            new("牛头马面", ["ghost:ma-mian", "ghost:niu-tou"]),
            new("黑白无常", ["ghost:bai-wu-chang", "ghost:hei-wu-chang"]),
            new("罗刹夜叉", ["ghost:luo-cha", "ghost:ye-cha"])
        ];
        builder.AddMode(new("activity:ghost-2018", "驱鬼逐邪·经典四关", 4, 4, new Dictionary<string, int>(),
            "classic:standard-deck", 8, roster.Concat(stages.SelectMany(stage => stage.GeneralPoolIds)).ToArray(),
            ContentModeKind.Team, new Dictionary<string, int> { ["team:allies"] = 3, ["team:boss"] = 1 })
        {
            BossActivity = new(roster, stages, 5, ["ghost:niepan"])
        });

        void Add(string id, string name, int hp, bool female, params string[] skills) =>
            builder.AddGeneral(new($"ghost:{id}", name, "", $"ghost:{skills[0]}", "qun", hp,
                skills.Skip(1).Select(skill => $"ghost:{skill}").ToArray())
            {
                CharacterId = $"character:ghost-{id}", VariantId = "ghost-2018", RulesetId = "ghost-2018",
                Gender = female ? GeneralGender.Female : GeneralGender.Male
            });
    }
}
