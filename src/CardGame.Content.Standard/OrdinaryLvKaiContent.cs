using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryLvKaiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-lv-kai", definition => definition);
        builder.AddGeneral(new("ol:lv-kai", "吕凯", "ol-lv-kai", "ol:tunan", "shu", 3,
            ["ol:bijing"], GeneralGender.Male)
        { CharacterId = "character:lv-kai", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
