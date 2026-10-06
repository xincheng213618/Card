using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryGaoLanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-gao-lan", definition => definition);
        builder.AddGeneral(new("ol:gao-lan", "高览", "ol-gao-lan", "ol:xizhen", "qun", 4, null, GeneralGender.Male)
        { CharacterId = "character:gao-lan", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
