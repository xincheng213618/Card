using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryGuoHuangHouContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-guo-huang-hou", definition => definition);
        builder.AddGeneral(new("boundary:guo-huang-hou", "界郭皇后", "boundary_guo_huang_hou", "boundary:jiaozhao-round-current", "wei", 3,
            ["boundary:danxin-capped-current"], GeneralGender.Female)
        { CharacterId = "character:guo-huang-hou", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
