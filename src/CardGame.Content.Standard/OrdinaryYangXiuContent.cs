using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryYangXiuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-yang-xiu", definition => definition with { ExecutionForms = SkillExecutionForm.Trigger });
        // The API has no gender field. Male is historical-person metadata, not initial_hp=0 or an API gender claim.
        builder.AddGeneral(new("ol:yang-xiu", "杨修", "ol-yang-xiu", "ol:danlao", "wei", 3, ["ol:jilei"], GeneralGender.Male)
        { CharacterId = "character:yang-xiu", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
