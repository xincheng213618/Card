using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinBoundaryHuangGaiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("batch9-damage-skills","boundary:kurou") with {ExecutionForms=SkillExecutionForm.None,ActionForms=SkillActionForm.Active});
        EmbeddedSkillProgramCatalog.RegisterBundle(builder,"boundary-huang-gai",definition=>definition with {Tags=SkillTag.Locked,ExecutionForms=SkillExecutionForm.Trigger,ActionForms=SkillActionForm.None});
        builder.AddGeneral(new ContentGeneralDefinition("boundary:huang-gai","界黄盖","boundary_huang_gai","boundary:kurou","wu",4,["boundary:zhaxiang"],Gender:GeneralGender.Male)
        {CharacterId="character:huang-gai",VariantId="boundary",RulesetId="sanguosha-ol"});
    }
}
