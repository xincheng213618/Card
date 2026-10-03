using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryCaoZhiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-cao-zhi", definition => definition.Id switch
        {
            "boundary:luoying" => definition with { ExecutionForms = SkillExecutionForm.Trigger },
            "boundary:jiushi" => definition with { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.Active },
            _ => definition
        });
        builder.AddGeneral(new("boundary:cao-zhi", "界曹植", "boundary_cao_zhi", "boundary:luoying", "wei", 3,
            ["boundary:jiushi"], GeneralGender.Male)
        { CharacterId = "character:cao-zhi", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
