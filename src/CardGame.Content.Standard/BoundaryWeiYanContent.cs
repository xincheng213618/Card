using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryWeiYanContent
{
    internal static void Register(IContentRegistryBuilder b)
    {
        b.AddSkill(EmbeddedSkillProgramCatalog.Definition("batch9-damage-skills","boundary:kuanggu") with {ExecutionForms=SkillExecutionForm.Trigger,ActionForms=SkillActionForm.None});
        b.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-wei-yan","boundary:qimou-current") with {Tags=SkillTag.Limited,ExecutionForms=SkillExecutionForm.None,ActionForms=SkillActionForm.Active});
        b.AddGeneral(new("boundary:wei-yan","界魏延","boundary_wei_yan","boundary:qimou-current","shu",4,["boundary:kuanggu"],GeneralGender.Male)
        {CharacterId="character:wei-yan",VariantId="boundary",RulesetId="sanguosha-ol"});
    }
}
