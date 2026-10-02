using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryHuaXiongContent
{
    internal static void Register(IContentRegistryBuilder b)
    {
        b.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-hua-xiong","boundary:yaowu-current") with {Tags=SkillTag.Locked,ExecutionForms=SkillExecutionForm.Trigger,ActionForms=SkillActionForm.None});
        b.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-hua-xiong","boundary:shizhan-current") with {ExecutionForms=SkillExecutionForm.None,ActionForms=SkillActionForm.Active});
        b.AddGeneral(new("boundary:hua-xiong","界华雄","boundary_hua_xiong","boundary:shizhan-current","qun",6,["boundary:yaowu-current"],GeneralGender.Male)
        {CharacterId="character:hua-xiong",VariantId="boundary",RulesetId="sanguosha-ol"});
    }
}
