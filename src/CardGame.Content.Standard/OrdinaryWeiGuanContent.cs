using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryWeiGuanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-wei-guan", definition => definition.Id switch
        {
            "ol:zhongyun" => definition with { Tags = SkillTag.Locked },
            _ => definition
        });
        // Male is inferred from the historical identity and official portrait, not an API sex field.
        builder.AddGeneral(new("ol:wei-guan", "卫瓘", "ol-wei-guan", "ol:zhongyun", "jin", 3,
            ["ol:shenpin"], GeneralGender.Male)
        { CharacterId = "character:wei-guan", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
