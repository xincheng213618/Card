using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryHuangZuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-huang-zu", definition => definition);
        // Male is inferred from the official biography's 江夏太守; API supplies no gender field.
        builder.AddGeneral(new("ol:huang-zu", "黄祖", "ol-huang-zu", "ol:wangong", "qun", 4, [], GeneralGender.Male)
        { CharacterId = "character:huang-zu", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
