using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlXinPiContent
{
 internal static void Register(IContentRegistryBuilder b)
 {
  EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-xin-pi",d=>d with{ExecutionForms=d.Id=="ol:yinju"?SkillExecutionForm.State|SkillExecutionForm.Trigger:SkillExecutionForm.Trigger,ActionForms=d.Id=="ol:yinju"?SkillActionForm.Active:SkillActionForm.None,Tags=d.Id=="ol:yinju"?SkillTag.Limited:SkillTag.None});
  b.AddGeneral(new("ol:xin-pi","辛毗·OL","ol-xin-pi","ol:chijie","wei",3,["ol:yinju"],GeneralGender.Male){CharacterId="character:xin-pi",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
