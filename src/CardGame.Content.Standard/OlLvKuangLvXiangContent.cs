using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlLvKuangLvXiangContent
{
 internal static void Register(IContentRegistryBuilder b)
 {
  EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-lv-kuang-lv-xiang",d=>d with{ExecutionForms=d.Id=="ol:qigong"?SkillExecutionForm.Trigger:SkillExecutionForm.None,ActionForms=d.Id=="ol:liehou"?SkillActionForm.Active:SkillActionForm.None});
  b.AddGeneral(new("ol:lv-kuang-lv-xiang","吕旷吕翔·OL","ol-lv-kuang-lv-xiang","ol:qigong","qun",4,["ol:liehou"],GeneralGender.Male){CharacterId="character:lv-kuang-lv-xiang",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
