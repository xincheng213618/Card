using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryYangWanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-yang-wan");
        builder.AddGeneral(new("ol:yang-wan", "杨婉", "ol-yang-wan", "ol:youyan", "shu", 3,
            ["ol:zhuihuan"], GeneralGender.Female)
        { CharacterId = "character:yang-wan", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
