using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryLiRuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-li-ru", definition =>
            definition.Id == "boundary:fencheng-current" ? definition with { Tags = definition.Tags | SkillTag.Limited } : definition);
        builder.AddGeneral(new("boundary:li-ru", "界李儒", "boundary_li_ru", "boundary:mieji-current", "qun", 3,
            ["boundary:fencheng-current", "boundary:juece-current"], GeneralGender.Male)
        { CharacterId = "character:li-ru", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
