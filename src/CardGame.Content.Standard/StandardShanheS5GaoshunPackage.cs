using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The independently configured base Gaoshun encounter of S5 group 2680.</summary>
public sealed class StandardShanheS5GaoshunPackage : IGameContentPackage
{
    public const string GaoshunId = "shanhe:s5-gaoshun-xianzhen";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-gaoshun-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // S5 / chapter 13 / boss 5138 / battle and group 2680: one base
        // enemy, general 604, raw 4/4, Draw 2 / initial hand 4 / Slash 1 /
        // Armor 0. Rank 4 is source metadata, not a local growth formula.
        // Qun/Male are the explicit local faction/gender mapping. The base
        // form does not import nightmare/hell skills, fixed cards or phases.
        builder.AddGeneral(new(GaoshunId, "高顺", "gao_shun",
            "boundary:xianzhen-current", "qun", 4, ["boundary:jinjiu-current"])
        {
            InitialHp = 4, CharacterId = "character:gao-shun", VariantId = "shanhe-s5-config-2680",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
