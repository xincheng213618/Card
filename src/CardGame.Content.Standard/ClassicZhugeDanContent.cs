using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class ClassicZhugeDanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-zhuge-dan", definition => definition with
        {
            ExecutionForms = SkillExecutionForm.Trigger,
            Tags = definition.Id == "classic:juyi" ? SkillTag.Awakening : SkillTag.Locked
        });
        // This explicitly selects the complete 2016 official classic rules;
        // the current hero page describes a different Juyi edition.
        builder.AddGeneral(new("classic:zhuge-dan", "诸葛诞·经典", "classic-zhuge-dan", "classic:gongao", "wei", 4,
            ["classic:juyi"], GeneralGender.Male)
        { CharacterId = "character:zhuge-dan", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
