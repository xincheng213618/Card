using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryPangTongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-pang-tong", "boundary:lianhuan") with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.None });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-pang-tong", "boundary:niepan") with
        { Tags = SkillTag.Limited, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:pang-tong", "界庞统", "boundary_pang_tong", "boundary:lianhuan", "shu", 3,
            ["boundary:niepan"], GeneralGender.Male)
        { CharacterId = "character:pang-tong", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
