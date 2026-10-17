using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The OL 2022 cooperative Hulao encounter and its two second-form branches.</summary>
public sealed class StandardHulaoActivityPackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new("hulao-activity", new Version(1, 0, 0),
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        foreach (var (id, kind) in new (string, CardKind)[]
        {
            ("alliance-feast", CardKind.AllianceFeast), ("equipment-wushuang-halberd", CardKind.WushuangHalberd),
            ("equipment-purple-gold-crown", CardKind.PurpleGoldCrown), ("equipment-red-cotton-robe", CardKind.RedCottonRobe), ("equipment-lion-belt", CardKind.LionBelt)
        })
        {
            var card = CardCatalog.Get(kind);
            builder.AddCard(new($"hulao:{id}", card.DisplayName, card.CategoryName, card.Description, kind));
        }
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "hulao-equipment", skill => skill with
        { Tags = skill.Id == "hulao:red-cotton-robe" ? SkillTag.Locked : SkillTag.None });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "hulao-activity", skill => skill with
        {
            Tags = skill.Id == "hulao:niepan" ? SkillTag.Limited :
                skill.Id is "hulao:wushuang" or "hulao:mashu" or "hulao:aozhan" or "hulao:shenwei" or "hulao:united"
                    ? SkillTag.Locked : SkillTag.None
        });
        // Initial equipment is placed by the native Hulao setup, before any turn.
        builder.AddSkill(new("hulao:jingjia", "精甲", "锁定技，游戏开始时，将本玩法新增的装备牌置入你的装备区。")
        { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State });

        Add("lvbu-myth", "最强神话", 8, "wushuang", "mashu", "jingjia", "aozhan");
        Add("lvbu-rage", "暴怒的战神", 6, "wushuang", "mashu", "xiuluo", "shenwei", "shenji");
        Add("lvbu-demon", "神鬼无前", 6, "wushuang", "shenqu", "jiwu");

        var banned = new HashSet<string>(StringComparer.Ordinal)
        {
            "character:lu-bu", "character:yu-ji", "character:zuo-ci", "character:zhao-xiang", "character:cai-wen-ji",
            "character:man-chong", "character:cao-pi", "character:bu-zhi", "character:huang-hao", "character:ling-ju"
        };
        var roster = CharacterContentCatalog.Default.GetPoolGenerals("standard-classic-generals", "current")
            .Where(g => g.FactionId != "god" && !banned.Contains(g.CharacterId ?? "") &&
                // Preserve legacy null CharacterId metadata and cover its officially banned versions by exact ID.
                g.Id is not ("classic:cao-pi" or "classic:man-chong" or "classic:cai-wen-ji" or "classic:lu-bu" or
                    "classic:yuan-shu" or "standard:yuan-shu" or "boundary:da-qiao" or "sp:guan-yu"))
            .Select(g => g.Id).ToArray();
        BossActivityStage[] stages =
        [
            new("最强神话", ["hulao:lvbu-myth"], 8),
            new("暴怒或神鬼", ["hulao:lvbu-rage", "hulao:lvbu-demon"], 4)
        ];
        builder.AddMode(new("activity:hulao-2022", "虎牢关·OL 2022", 4, 4, new Dictionary<string, int>(),
            "classic:standard-deck", 8, roster.Concat(stages.SelectMany(stage => stage.GeneralPoolIds)).ToArray(),
            ContentModeKind.Team, new Dictionary<string, int> { ["team:allies"] = 3, ["team:boss"] = 1 })
        {
            BossActivity = new(roster, stages, 5, ["hulao:niepan", "hulao:united"]),
            HulaoActivity = new(TransformationHp: 4, ShuffleLimit: 2)
        });

        void Add(string id, string name, int hp, params string[] skills) =>
            builder.AddGeneral(new($"hulao:{id}", name, "shen_lu_bu", $"hulao:{skills[0]}", "god", hp,
                skills.Skip(1).Select(skill => $"hulao:{skill}").ToArray())
            { CharacterId = "character:lu-bu", VariantId = id, RulesetId = "hulao-2022", Gender = GeneralGender.Male });
    }
}
