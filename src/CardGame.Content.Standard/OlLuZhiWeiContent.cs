using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlLuZhiWeiContent
{
 internal static void Register(IContentRegistryBuilder builder)
 {
  builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-lu-zhi-wei","ol:weijing"));
  builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-lu-zhi-wei","ol:qingzhong") with{ExecutionForms=SkillExecutionForm.Trigger});
  builder.AddGeneral(new("ol:lu-zhi-wei","鲁芝·OL","ol-lu-zhi-wei","ol:weijing","wei",3,["ol:qingzhong"],GeneralGender.Male){CharacterId="character:lu-zhi-wei",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
