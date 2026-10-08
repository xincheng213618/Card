using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryWangXiangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-wang-xiang", definition => definition);
        // Male is inferred from the historical identity and the inspected official portrait; the API has no gender field.
        builder.AddGeneral(new("ol:wang-xiang", "王祥", "ol-wang-xiang", "ol:bingxin", "jin", 3, [], GeneralGender.Male)
        { CharacterId = "character:wang-xiang", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
