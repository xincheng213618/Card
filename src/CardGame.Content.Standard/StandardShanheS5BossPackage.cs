using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>Explicit forms from the public S5 configuration snapshot, separate from panel-verified forms.</summary>
public sealed class StandardShanheS5BossPackage : IGameContentPackage
{
    public const string ZhangliaoBaseId = "shanhe:s5-zhangliao-base";
    public const string TuxiId = "shanhe:s5-tuxi-722";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-config-bosses", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-zhangliao");
        // enemy-groups 2646 has 4/4 before unspecified rank/difficulty calculations.
        // Its base skills are 722 and 3389. The latter is exactly the retained
        // boundary Pojun program; the all-hand 14112 variant must not replace it.
        builder.AddGeneral(new(ZhangliaoBaseId, "张辽·文远之威", "zhang_liao", TuxiId, "wei", 4,
            ["boundary:pojun"])
        {
            CharacterId = "character:zhang-liao", VariantId = "shanhe-s5-config-base",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
