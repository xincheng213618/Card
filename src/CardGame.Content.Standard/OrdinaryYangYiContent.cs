using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OrdinaryYangYiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-yang-yi");
        builder.AddGeneral(new("ol:yang-yi", "杨仪", "ol-yang-yi", "ol:juanxia", "shu", 3,
            ["ol:dingcuo"], GeneralGender.Male)
        { CharacterId = "character:yang-yi", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
