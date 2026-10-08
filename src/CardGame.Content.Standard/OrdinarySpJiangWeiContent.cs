using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinarySpJiangWeiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-sp-jiang-wei", definition => definition.Id switch
        {
            "ol:kunfen" => definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.Trigger },
            "ol:fengliang" => definition with { Tags = SkillTag.Awakening, ExecutionForms = SkillExecutionForm.Trigger },
            "ol:kunfen-awakened" => definition with { Tags = SkillTag.None, ExecutionForms = SkillExecutionForm.Trigger },
            "ol:sp-jiang-wei-tiaoxin" => definition with { ActionForms = SkillActionForm.Active },
            _ => definition
        });
        // Male follows the historical identity and official portrait; the API has no sex field.
        builder.AddGeneral(new("ol:sp-jiang-wei", "SP姜维", "ol-sp-jiang-wei", "ol:kunfen", "wei", 4,
            ["ol:fengliang"], GeneralGender.Male)
        { CharacterId = "character:jiang-wei", VariantId = "sp", RulesetId = "sanguosha-ol" });
    }
}
