using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryPangDeContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-pang-de", "boundary:jianchu") with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:pang-de", "界庞德", "boundary_pang_de", "classic:mashu", "qun", 4,
            ["boundary:jianchu"], GeneralGender.Male)
        { CharacterId = "character:pang-de", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
