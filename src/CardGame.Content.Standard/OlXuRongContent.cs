using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlXuRongContent
{
 internal static void Register(IContentRegistryBuilder builder)
 {
  builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-xu-rong","ol:xionghuo"));
  builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-xu-rong","ol:shajue") with{ExecutionForms=SkillExecutionForm.Trigger});
  builder.AddGeneral(new("ol:xu-rong","徐荣·OL","ol-xu-rong","ol:xionghuo","qun",4,["ol:shajue"],GeneralGender.Male){CharacterId="character:xu-rong",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
