using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The S5 fixed axe, independent from the narrower existing special axe.</summary>
public sealed class StandardShanheXingtianPackage : IGameContentPackage
{
    public const string CardId = "shanhe:xingtian-axe-20766";
    public const string SkillId = "shanhe:xingtian-axe-20766-effect";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-xingtian-axe", CurrentVersion,
        [new("standard", StandardContentPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-xingtian-axe");
        var axe = EquipmentCatalog.Get(CardKind.ShanheXingtianAxe);
        builder.AddCard(new(CardId, axe.DisplayName, "装备牌", axe.Description, axe.Kind));
    }
}
