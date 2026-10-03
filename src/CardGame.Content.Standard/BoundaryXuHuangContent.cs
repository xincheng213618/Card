using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryXuHuangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-xu-huang", definition => definition);
        builder.AddGeneral(new("boundary:xu-huang", "界徐晃", "boundary_xu_huang",
            "boundary:duanliang-current", "wei", 4, ["boundary:jiezi-current"], GeneralGender.Male)
        { CharacterId = "character:xu-huang", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
