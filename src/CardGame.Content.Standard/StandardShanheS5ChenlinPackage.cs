using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The two base skills of S5 main group 2672 reuse the identical ordinary Chen Lin programs.</summary>
public sealed class StandardShanheS5ChenlinPackage : IGameContentPackage
{
    public const string ChenlinId = "shanhe:s5-chen-lin-2672";
    public const string BifaId = "ol:bifa";
    public const string SongciId = "ol:songci";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-chenlin-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // Main group 2672, general 113: 3/3, draw 2, hand 4, Slash 1, armor 0.
        // Source skills 212/213 match ol:bifa and ol:songci verbatim. Nightmare
        // skill 14035 and fixed card 20841 do not belong to this base form.
        // Faction, gender and portrait are existing local interaction/art mappings.
        builder.AddGeneral(new(ChenlinId, "陈琳", "ol-chen-lin", BifaId, "wei", 3, [SongciId])
        {
            InitialHp = 3, CharacterId = "character:chen-lin", VariantId = "shanhe-s5-config-2672",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
