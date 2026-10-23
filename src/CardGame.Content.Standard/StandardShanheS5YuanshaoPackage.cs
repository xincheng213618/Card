using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete single-skill base form of S5 group 2660.</summary>
public sealed class StandardShanheS5YuanshaoPackage : IGameContentPackage
{
    public const string YuanshaoId = "shanhe:s5-yuan-shao-2660";
    public const string LuanjiId = "shanhe:s5-luanji-14162";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-yuanshao-boss", CurrentVersion,
        [new("standard", StandardContentPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-yuanshao");
        // S5 Mengjin / battle and group 2660: one Yuanshao, raw 4/4,
        // Draw 2 / hand 4 / Slash 1 / Armor 0, no fixed cards or tactics.
        // Skill 14162 accepts the same color rather than the ordinary same suit.
        // Qun/Male and the existing portrait are explicit local mappings.
        builder.AddGeneral(new(YuanshaoId, "袁绍·盟首", "yuan_shao", LuanjiId, "qun", 4)
        {
            InitialHp = 4, CharacterId = "character:yuan-shao", VariantId = "shanhe-s5-config-2660",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
