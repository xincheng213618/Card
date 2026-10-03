using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryDianWeiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-dian-wei", definition => definition.Id switch
        {
            "boundary:qiangxi" => definition with { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.Active },
            "boundary:ninge" => definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None },
            _ => definition
        });
        builder.AddGeneral(new("boundary:dian-wei", "界典韦", "boundary_dian_wei", "boundary:qiangxi", "wei", 4,
            ["boundary:ninge"], GeneralGender.Male)
        { CharacterId = "character:dian-wei", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
