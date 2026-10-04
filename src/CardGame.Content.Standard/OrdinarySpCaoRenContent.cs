using CardGame.Core;

namespace CardGame.Content.Standard;
internal static class OrdinarySpCaoRenContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-sp-cao-ren", definition => definition);
        builder.AddGeneral(new("ol:sp-cao-ren", "SP曹仁", "ol-sp-cao-ren", "ol:weikui", "wei", 4,
            ["ol:lizhan"], GeneralGender.Male)
        { CharacterId = "character:cao-ren", VariantId = "sp", RulesetId = "sanguosha-ol" });
    }
}
