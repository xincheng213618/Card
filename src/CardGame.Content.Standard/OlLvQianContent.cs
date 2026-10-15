using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlLvQianContent
{
 internal static void Register(IContentRegistryBuilder b)
 {
  b.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-lv-qian","ol:weilu") with{ExecutionForms=SkillExecutionForm.Trigger,Tags=SkillTag.Locked});
  b.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-lv-qian","ol:zengdao") with{ExecutionForms=SkillExecutionForm.State|SkillExecutionForm.Trigger,ActionForms=SkillActionForm.Active,Tags=SkillTag.Limited});
  b.AddGeneral(new("ol:lv-qian","吕虔·OL","ol-lv-qian","ol:weilu","wei",4,["ol:zengdao"],GeneralGender.Male){CharacterId="character:lv-qian",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
