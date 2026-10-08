using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryXuJingContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-xu-jing");
        // Male follows the historical identity and official portrait; the API has no sex field.
        builder.AddGeneral(new("ol:xu-jing", "许靖", "ol-xu-jing", "ol:yuxu", "shu", 3,
            ["ol:shijian"], GeneralGender.Male)
        { CharacterId = "character:xu-jing", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
