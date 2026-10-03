using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryGuoHuaiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-guo-huai", definition => definition);
        builder.AddGeneral(new("boundary:guo-huai", "界郭淮", "boundary_guo_huai",
            "boundary:jingce-current", "wei", 4, [], GeneralGender.Male)
        { CharacterId = "character:guo-huai", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
