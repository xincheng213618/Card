using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The single-skill base form of S5 group 2676.</summary>
public sealed class StandardShanheS5XuchuPackage : IGameContentPackage
{
    public const string XuchuId = "shanhe:s5-xu-chu-2676";
    public const string LuoyiId = "shanhe:s5-luoyi-14191";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-xuchu-boss", CurrentVersion,
        [new("standard", StandardContentPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-xuchu");
        // S5 Suanzao boss / group 2676: 4/4, draw 2, hand 4, Slash 1, armor 0, no fixed cards.
        // Faction, gender and portrait reuse are local interaction/art mappings.
        builder.AddGeneral(new(XuchuId, "许褚·虎痴", "xu_chu", LuoyiId, "wei", 4)
        {
            InitialHp = 4, CharacterId = "character:xu-chu", VariantId = "shanhe-s5-config-2676",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
