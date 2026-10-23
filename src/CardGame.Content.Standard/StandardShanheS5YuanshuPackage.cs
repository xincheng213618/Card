using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete three-general base roster of S5 main group 2654.</summary>
public sealed class StandardShanheS5YuanshuPackage : IGameContentPackage
{
    public const string YuanyaoId = "shanhe:s5-yuan-yao-2654";
    public const string YuanshuId = "shanhe:s5-yuan-shu-2654";
    public const string YuanyinId = "shanhe:s5-yuan-yin-2654";
    public const string QingzhongId = "ol:qingzhong";
    public const string WuzangId = "shenwu:wuzang";
    public const string TongjiId = "shanhe:s5-tongji-497";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-yuanshu-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // Import the one exact existing skill, without a Shenwu activity roster.
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("shenwu-npc", WuzangId) with { Tags = SkillTag.Locked });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-yuanshu",
            tagOverrides: new Dictionary<string, SkillTag> { [TongjiId] = SkillTag.Locked });
        // S5 chapter 6 / boss 5068 / battle and main group 2654 代汉涂高:
        // source 4871 Yuan Yao 3/3, 100 Yuan Shu 4/4, 4872 Yuan Yin 3/3,
        // all Draw2/Hand4/Slash1/Armor0 with no fixed cards or tactics.
        // Hard 893 Fangquan and Nightmare 826 Zhengu are excluded. Qun/Male,
        // the ordinary Yuan Shu portrait and generic aides are local mappings.
        Add(YuanyaoId, "袁耀", "supporter", "character:yuan-yao", 3, QingzhongId, []);
        Add(YuanshuId, "袁术", "yuan_shu", "character:yuan-shu", 4, WuzangId, [TongjiId]);
        Add(YuanyinId, "袁胤", "supporter", "character:yuan-yin", 3, QingzhongId, []);

        void Add(string id, string name, string portrait, string characterId, int hp, string primary, string[] additional) =>
            builder.AddGeneral(new(id, name, portrait, primary, "qun", hp, additional)
            {
                InitialHp = hp, CharacterId = characterId, VariantId = "shanhe-s5-config-2654",
                RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
            });
    }
}
