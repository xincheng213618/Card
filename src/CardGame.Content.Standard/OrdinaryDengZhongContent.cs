using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryDengZhongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-deng-zhong", definition => definition);
        builder.AddGeneral(new("ol:deng-zhong", "邓忠", "ol-deng-zhong", "ol:kanpo", "wei", 4, ["ol:gengzhan"], GeneralGender.Male)
        { CharacterId = "character:deng-zhong", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
