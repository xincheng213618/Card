using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The one-encounter, six-seat Shenwu activity published by OL in 2017.</summary>
public sealed class StandardShenwuActivityPackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new("shenwu-activity", new Version(1, 0, 0),
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        foreach (var (id, kind) in new (string, CardKind)[]
        {
            ("sow-beans", CardKind.SowBeans), ("transplant-wood", CardKind.TransplantWood),
            ("equipment-national-style-robe", CardKind.NationalStyleRobe),
            ("equipment-crimson-soul-zither", CardKind.CrimsonSoulZither),
            ("equipment-qimen-eight-trigrams", CardKind.QimenEightTrigrams),
            ("equipment-dustless-golden-spear", CardKind.DustlessGoldenSpear),
            ("equipment-shura-inferno-halberd", CardKind.ShuraInfernoHalberd),
            ("equipment-delusion-crown", CardKind.DelusionCrown),
            ("equipment-ghost-dragon-crescent-blade", CardKind.GhostDragonCrescentBlade),
            ("equipment-scarlet-blood-sword", CardKind.ScarletBloodSword)
        })
        {
            var card = CardCatalog.Get(kind);
            builder.AddCard(new($"shenwu:{id}", card.DisplayName, card.CategoryName, card.Description, kind));
        }
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shenwu-npc", skill => skill with { Tags = SkillTag.Locked });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shenwu-equipment", skill => skill with { Tags = SkillTag.Locked });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shenwu-activity", skill => skill with { Tags = SkillTag.Limited });

        Add("hundun", "混沌", 20, "xiongshou", "wuzang", "xiangde");
        Add("qiongqi", "穷奇", 16, "xiongshou", "zhue", "futai");
        Add("taowu", "梼杌", 16, "xiongshou", "mingwan", "nitai");
        Add("taotie", "饕餮", 20, "xiongshou", "tanyu", "cangmu");
        Add("zhuyin-left", "烛阴", 4, "xiongshou");
        Add("zhuyin-right", "烛阴", 4, "xiongshou");

        // Resolve the published bans against the current ordinary-character pool.
        // SP Jia Xu and SP Guan Yu have distinct versions; their ordinary families remain legal.
        var bannedCharacters = new HashSet<string>(StringComparer.Ordinal)
        {
            "kan-ze", "diao-chan", "cai-wen-ji", "wang-yun", "zuo-ci", "yu-ji", "da-qiao", "xu-huang",
            "ling-ju", "bu-zhi", "ma-yun-lu", "huang-hao", "cheng-yu", "guo-huang-hou", "guo-huanghou", "zhang-rang",
            "xun-yu", "xiao-qiao", "da-qiao-xiao-qiao", "fa-zheng", "man-chong", "han-hao-shi-huan"
        }.Select(id => $"character:{id}").ToHashSet(StringComparer.Ordinal);
        // Older published definitions intentionally retain null CharacterId for their
        // historical fingerprints. Explicit IDs cover those definitions without rewriting them.
        var bannedVersions = new HashSet<string>(StringComparer.Ordinal)
        {
            "standard:yuan-shu", "classic:yuan-shu", "ol:sp-jia-xu", "sp:guan-yu",
            "classic:cai-wen-ji", "classic:da-qiao", "classic:diao-chan", "boundary:diao-chan",
            "classic:man-chong", "classic:xun-yu", "classic:xiao-qiao", "classic:xu-huang"
        };
        var ordinary = CharacterContentCatalog.Default.GetPoolGenerals("standard-classic-generals", "current")
            .Where(general => general.FactionId != "god" && !bannedCharacters.Contains(general.CharacterId ?? "") &&
                !bannedVersions.Contains(general.Id))
            .Select(general => general.Id).ToArray();
        string[] gods =
        [
            "classic:shen-guan-yu", "classic:shen-lu-meng", "classic:shen-zhou-yu", "ol:shen-zhuge-liang",
            "classic:shen-cao-cao", "classic:shen-lu-bu", "classic:shen-sima-yi", "ol:shen-zhao-yun"
        ];
        string[] leaders = ["shenwu:hundun", "shenwu:qiongqi", "shenwu:taowu", "shenwu:taotie"];
        string[] minions = ["shenwu:zhuyin-left", "shenwu:zhuyin-right"];
        builder.AddMode(new("activity:shenwu-2017", "神武再世·OL 2017", 6, 6, new Dictionary<string, int>(),
            "classic:standard-deck", 5, ordinary.Concat(gods).Concat(leaders).Concat(minions).ToArray(),
            ContentModeKind.Team, new Dictionary<string, int> { ["team:allies"] = 3, ["team:beasts"] = 3 })
        {
            ShenwuActivity = new(ordinary, gods, leaders, minions, [0, 1, 2], 4, [3, 5], 5,
                ["shenwu:niepan", "shenwu:coalition"], new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["shenwu:hundun"] = "shenwu:yinzei", ["shenwu:qiongqi"] = "shenwu:yandu",
                    ["shenwu:taowu"] = "shenwu:luanchang", ["shenwu:taotie"] = "shenwu:jicai"
                })
        });

        void Add(string id, string name, int hp, params string[] skills) =>
            builder.AddGeneral(new($"shenwu:{id}", name, "", $"shenwu:{skills[0]}", "qun", hp,
                skills.Skip(1).Select(skill => $"shenwu:{skill}").ToArray())
            {
                CharacterId = $"character:shenwu-{id}", VariantId = "shenwu-2017", RulesetId = "shenwu-2017",
                Gender = GeneralGender.Male
            });
    }
}
