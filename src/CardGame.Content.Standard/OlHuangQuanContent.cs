using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlHuangQuanContent
{
 internal static void Register(IContentRegistryBuilder builder)
 {
  builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-huang-quan","ol:dianhu") with{ExecutionForms=SkillExecutionForm.Trigger,Tags=SkillTag.Locked});
  builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-huang-quan","ol:jianji") with{ExecutionForms=SkillExecutionForm.State,ActionForms=SkillActionForm.Active});
  builder.AddGeneral(new("ol:huang-quan","黄权·OL","ol-huang-quan","ol:dianhu","shu",3,["ol:jianji"],GeneralGender.Male){CharacterId="character:huang-quan",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
