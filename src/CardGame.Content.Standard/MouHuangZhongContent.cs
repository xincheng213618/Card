using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class MouHuangZhongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("mou-huang-zhong", "mou:liegong") with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("mou:huang-zhong", "谋黄忠", "mou_huang_zhong", "mou:liegong", "shu", 4, Gender: GeneralGender.Male)
        { CharacterId = "character:huang-zhong", VariantId = "mou", RulesetId = "sanguosha-mobile" });
    }
}
