using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryGongsunZanContent
{
    internal static void Register(IContentRegistryBuilder b)
    {
        b.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-gongsun-zan","boundary:yicong-current") with
        { Tags=SkillTag.Locked, ExecutionForms=SkillExecutionForm.None, ActionForms=SkillActionForm.None });
        b.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-gongsun-zan","boundary:qiaomeng-current") with
        { ExecutionForms=SkillExecutionForm.Trigger, ActionForms=SkillActionForm.None });
        b.AddGeneral(new("boundary:gongsun-zan","界公孙瓒","boundary_gongsun_zan","boundary:yicong-current","qun",4,["boundary:qiaomeng-current"],GeneralGender.Male)
        { CharacterId="character:gongsun-zan",VariantId="boundary",RulesetId="sanguosha-ol" });
    }
}
