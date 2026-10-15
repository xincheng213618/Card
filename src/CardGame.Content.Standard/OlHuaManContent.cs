using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlHuaManContent
{
 internal static void Register(IContentRegistryBuilder b)
 {
  b.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-hua-man","ol:manyi") with{ExecutionForms=SkillExecutionForm.State,Tags=SkillTag.Locked});
  b.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-hua-man","ol:mansi") with{ExecutionForms=SkillExecutionForm.State|SkillExecutionForm.Trigger,ActionForms=SkillActionForm.Active});
  b.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-hua-man","ol:souying") with{ExecutionForms=SkillExecutionForm.Trigger});
  b.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-hua-man","ol:zhanyuan") with{ExecutionForms=SkillExecutionForm.Trigger,Tags=SkillTag.Awakening});
  b.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-hua-man","ol:xili") with{ExecutionForms=SkillExecutionForm.Trigger});
  b.AddGeneral(new("ol:hua-man","花鬘·OL","ol-hua-man","ol:manyi","shu",3,["ol:mansi","ol:souying","ol:zhanyuan"],GeneralGender.Female){CharacterId="character:hua-man",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
