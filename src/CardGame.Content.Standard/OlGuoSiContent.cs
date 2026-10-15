using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlGuoSiContent
{
 internal static void Register(IContentRegistryBuilder b)
 {
  b.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-guo-si","ol:tanbei") with{ExecutionForms=SkillExecutionForm.State,ActionForms=SkillActionForm.Active});
  b.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-guo-si","ol:sidao") with{ExecutionForms=SkillExecutionForm.Trigger});
  b.AddGeneral(new("ol:guo-si","郭汜·OL","ol-guo-si","ol:tanbei","qun",4,["ol:sidao"],GeneralGender.Male){CharacterId="character:guo-si",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
