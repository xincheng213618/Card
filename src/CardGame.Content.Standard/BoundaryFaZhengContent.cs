using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryFaZhengContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-fa-zheng", definition => definition);
        builder.AddGeneral(new("boundary:fa-zheng", "界法正", "boundary_fa_zheng", "boundary:xuanhuo", "shu", 3,
            ["boundary:enyuan"], GeneralGender.Male)
        { CharacterId = "character:fa-zheng", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
