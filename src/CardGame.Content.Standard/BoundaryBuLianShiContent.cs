using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class BoundaryBuLianShiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-bu-lian-shi", definition =>
            definition.Id == "boundary:zhuiyi-current" ? definition with { Tags = SkillTag.Limited } : definition);
        builder.AddGeneral(new("boundary:bu-lian-shi", "界步练师", "boundary_bu_lian_shi", "boundary:anxu-current", "wu", 3,
            ["boundary:zhuiyi-current"], GeneralGender.Female)
        { CharacterId = "character:bu-lian-shi", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
