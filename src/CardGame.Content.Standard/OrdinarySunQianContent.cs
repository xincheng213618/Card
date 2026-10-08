using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinarySunQianContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-sun-qian");
        // Male follows the historical identity and official portrait; the API has no sex field.
        builder.AddGeneral(new("ol:sun-qian", "孙乾", "ol-sun-qian", "ol:qianya", "shu", 3,
            ["ol:shuomeng"], GeneralGender.Male)
        { CharacterId = "character:sun-qian", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
