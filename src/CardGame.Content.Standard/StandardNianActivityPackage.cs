using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete three-against-one OL 2016 encounter, with four rotating states.</summary>
public sealed class StandardNianActivityPackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new("nian-activity", new Version(1, 0, 0),
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "nian-npc", skill => skill with { Tags = SkillTag.Locked });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "nian-activity", skill => skill with
        { Tags = skill.Id == "nian:niepan" ? SkillTag.Limited : SkillTag.None });
        builder.AddSkill(new("nian:dongmian", "冬眠",
            "锁定技，首轮中，若你的体力值不大于6，其他角色使用的牌不能指定你为目标。")
        { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State });

        Add("initial", "年兽·初始");
        Add("wise", "年兽·睿智", "ruizhi");
        Add("alert", "年兽·警觉", "jingjue");
        Add("capricious", "年兽·任性", "renxing");
        Add("furious", "年兽·暴怒", "baonu", "shouyi");

        var bannedCharacters = new HashSet<string>(StringComparer.Ordinal)
        {
            "character:cao-pi", "character:man-chong", "character:cai-wen-ji",
            "character:zuo-ci", "character:yu-ji"
        };
        // Historical null CharacterId definitions keep their original fingerprint.
        var bannedVersions = new HashSet<string>(StringComparer.Ordinal)
        {
            "classic:cao-pi", "classic:man-chong", "classic:cai-wen-ji",
            "ol:sp-cai-wen-ji", "classic:zuo-ci", "classic:yu-ji"
        };
        var players = CharacterContentCatalog.Default.GetPoolGenerals("standard-classic-generals", "current")
            .Where(general => !bannedCharacters.Contains(general.CharacterId ?? "") &&
                !bannedVersions.Contains(general.Id) && general.Id != "classic:shen-zhao-yun")
            // The old classic God Zhao Yun is a special scenario version, not the ordinary OL general.
            .Select(general => general.Id).Append("ol:shen-zhao-yun").Distinct(StringComparer.Ordinal).ToArray();
        string[] forms = ["nian:initial", "nian:wise", "nian:alert", "nian:capricious", "nian:furious"];
        builder.AddMode(new("activity:nian-2016", "挑战年兽·OL 2016", 4, 4, new Dictionary<string, int>(),
            "classic:standard-deck", 5, players.Concat(forms).ToArray(), ContentModeKind.Team,
            new Dictionary<string, int> { ["team:allies"] = 3, ["team:nian"] = 1 })
        {
            BossActivity = new(players, [new("年兽", forms, 4)], 5,
                ["nian:niepan", "nian:revive"], EnemyTeamId: "team:nian"),
            NianActivity = new(forms, SharedRevives: 3)
        });

        void Add(string id, string name, params string[] stateSkills) =>
            builder.AddGeneral(new($"nian:{id}", name, $"nian-{id}", "nian:yingzi", "god", 12,
                new[] { "nian:mengtai", "nian:dongmian" }.Concat(stateSkills.Select(skill => $"nian:{skill}")).ToArray())
            {
                CharacterId = "character:nian-2016", VariantId = id, RulesetId = "nian-2016",
                Gender = GeneralGender.Male
            });
    }
}
