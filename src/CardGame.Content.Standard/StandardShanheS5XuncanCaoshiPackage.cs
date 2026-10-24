using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete base roster of S5 battle/group 2657.</summary>
public sealed class StandardShanheS5XuncanCaoshiPackage : IGameContentPackage
{
    public const string XuncanId = "shanhe:s5-xun-can-2657";
    public const string CaoshiId = "shanhe:s5-cao-shi-2657";
    public const string YunshenId = "shanhe:s5-yunshen-3226";
    public const string ShangshenId = "shanhe:s5-shangshen-3227";
    public const string FenchaiId = "shanhe:s5-fenchai-3228";
    public const string LeijieId = "shanhe:s5-leijie-3231";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-xuncan-caoshi-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-xuncan-caoshi",
            tagOverrides: new Dictionary<string, SkillTag> { [FenchaiId] = SkillTag.Locked });
        // The declared classic-generals dependency already owns the exact source-883 Guidao.
        builder.AddCard(new("yingbian:cold_slash", "冰杀", "基本牌", CardCatalog.Get(CardKind.ColdSlash).Description, CardKind.ColdSlash));

        // S5 诸侯伐董 / 颍川郡 / boss 5078 / battle/group 2657 狂傲荀粲.
        // Source order: general 525 荀粲, then non-boss 4876 曹氏 (skin 1000051).
        // Both are raw 3/3, D2/H4/Slash1/Armor0, without base fixed cards/tactics.
        // Neither has a named local portrait asset: preserve the explicit missing/supporter mappings.
        builder.AddGeneral(new(XuncanId, "荀粲", "", YunshenId, "wei", 3, [ShangshenId, FenchaiId])
        { InitialHp = 3, CharacterId = "character:xun-can", VariantId = "shanhe-s5-config-2657", RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male });
        builder.AddGeneral(new(CaoshiId, "曹氏", "supporter", LeijieId, "wei", 3, ["boundary:guidao"])
        { InitialHp = 3, CharacterId = "character:cao-shi", VariantId = "shanhe-s5-config-2657-skin-1000051", RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Female });
    }
}
