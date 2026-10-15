using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlXiZhiCaiContent
{
 internal static void Register(IContentRegistryBuilder builder)
 {
  builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-xi-zhi-cai","ol:xianfu") with{Tags=SkillTag.Locked,ExecutionForms=SkillExecutionForm.Trigger});
  builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-xi-zhi-cai","ol:chouce") with{ExecutionForms=SkillExecutionForm.Trigger});
  builder.AddGeneral(new("ol:xi-zhi-cai","戏志才·OL","ol-xi-zhi-cai","classic:tiandu","wei",3,["ol:xianfu","ol:chouce"],GeneralGender.Male){CharacterId="character:xi-zhi-cai",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
