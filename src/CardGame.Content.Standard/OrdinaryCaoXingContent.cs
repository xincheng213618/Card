using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryCaoXingContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-cao-xing", definition => definition);
        // Male is inferred from the official biography's 吕布帐下名将; API supplies no gender field.
        builder.AddGeneral(new("ol:cao-xing", "曹性", "ol-cao-xing", "ol:liu-shi", "qun", 4, ["ol:zhan-wan"], GeneralGender.Male)
        { CharacterId = "character:cao-xing", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
