using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete two-general base roster of S5 main group 2659.</summary>
public sealed class StandardShanheS5DongwuVeteransPackage : IGameContentPackage
{
    public const string ChengpuId = "shanhe:s5-cheng-pu-2659";
    public const string HandangId = "shanhe:s5-han-dang-2659";
    public const string ChunlaoId = "shanhe:s5-chunlao-3616";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-dongwu-veterans-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-dongwu-veterans");
        // S5 chapter 7 / order 2 Yingchuan / boss 5078 -> battle/group 2659:
        // source 620 Cheng Pu then 170 Han Dang, both raw 4/4,
        // Draw 2 / random hand 4 / Slash 1 / Armor 0, no base cards/tactics.
        // 3470 exactly matches boundary Lihuo; 3616 alone adds a cap of nine
        // to ordinary adjacent-discard Chunlao and otherwise keeps native wine.
        // 433/434 match classic Gongqi/Jiefan, without boundary suit quota or
        // first-round limited-skill reset. Exclude Hard cards 20811/20812,
        // Nightmare 711/14060 and Inferno 295/14062.
        builder.AddGeneral(new(ChengpuId, "程普", "boundary_cheng_pu", "boundary:lihuo", "wu", 4, [ChunlaoId])
        {
            InitialHp = 4, CharacterId = "character:cheng-pu", VariantId = "shanhe-s5-config-2659",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        builder.AddGeneral(new(HandangId, "韩当", "han_dang", "classic:gongqi", "wu", 4, ["classic:jiefan"])
        {
            InitialHp = 4, CharacterId = "character:han-dang", VariantId = "shanhe-s5-config-2659",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
