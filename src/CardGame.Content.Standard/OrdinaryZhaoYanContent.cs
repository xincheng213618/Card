using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryZhaoYanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-zhao-yan");
        builder.AddGeneral(new("ol:zhao-yan", "赵俨", "ol-zhao-yan", "ol:tongxie", "wei", 4,
            [], GeneralGender.Male)
        { CharacterId = "character:zhao-yan", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
