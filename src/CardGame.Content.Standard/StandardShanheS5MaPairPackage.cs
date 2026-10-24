using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete S5 base roster of battle/group 2640.</summary>
public sealed class StandardShanheS5MaPairPackage : IGameContentPackage
{
    public const string MaPairId = "shanhe:s5-ma-xiu-ma-tie-2640";
    public const string AnqiFeirenCardId = "shanhe:anqi-feiren-20861";
    public const string AnqiFeirenSkillId = "shanhe:anqi-feiren";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-ma-pair-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion),
         new("shanhe-s5-ma-siblings-boss", StandardShanheS5MaSiblingsPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-anqi-feiren",
            tagOverrides: new Dictionary<string, SkillTag> { [AnqiFeirenSkillId] = SkillTag.Locked });
        var weapon = EquipmentCatalog.Get(CardKind.AnqiFeiren);
        builder.AddCard(new(AnqiFeirenCardId, weapon.DisplayName, "装备牌", weapon.Description, CardKind.AnqiFeiren));
        // Main group 2640, general 538: raw 4/4, D2/H4/Slash1, armor/gain0.
        // Source 14118 is fixed outgoing distance one, not ordinary Mashu -1.
        // Source 3229 is word-for-word the existing OL Kenshang program.
        // Fixed Spade-five 20861 belongs to the encounter's additional Hand;
        // it is neither an intrinsic general skill nor pre-installed equipment.
        builder.AddGeneral(new(MaPairId, "马休马铁", "ol-ma-xiu-ma-tie", StandardShanheS5MaSiblingsPackage.MashuId,
            "qun", 4, ["ol:kenshang"])
        {
            InitialHp = 4, CharacterId = "character:ma-xiu-ma-tie", VariantId = "shanhe-s5-config-2640",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
