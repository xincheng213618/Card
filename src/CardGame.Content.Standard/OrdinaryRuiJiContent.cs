using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OrdinaryRuiJiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-rui-ji");
        builder.AddGeneral(new("ol:rui-ji", "芮姬", "ol-rui-ji", "ol:qiaoli", "wu", 3,
            ["ol:qingliang"], GeneralGender.Female)
        { CharacterId = "character:rui-ji", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
