using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryJianYongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-jian-yong", definition => definition);
        builder.AddGeneral(new("boundary:jian-yong", "界简雍", "boundary_jian_yong",
            "boundary:qiaoshui-current", "shu", 3, ["classic:zongshi-pindian"], GeneralGender.Male)
        { CharacterId = "character:jian-yong", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
