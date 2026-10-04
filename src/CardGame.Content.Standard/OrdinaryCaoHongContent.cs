using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryCaoHongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-cao-hong", definition => definition);
        // Male is inferred from the official biography's 曹操从弟; API supplies no gender field.
        builder.AddGeneral(new("ol:cao-hong", "曹洪", "ol-cao-hong", "ol:yuanhu", "wei", 4, [], GeneralGender.Male)
        { CharacterId = "character:cao-hong", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
