using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryChenDengContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-chen-deng", definition => definition);
        builder.AddGeneral(new("ol:chen-deng", "陈登", "ol-chen-deng", "ol:fengji", "qun", 4,
            ["ol:xuanhui"], GeneralGender.Male)
        { CharacterId = "character:chen-deng", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
