using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlWangCanContent
{
 internal static void Register(IContentRegistryBuilder b)
 {
  b.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-wang-can","ol:sanwen") with{ExecutionForms=SkillExecutionForm.Trigger});
  b.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-wang-can","ol:qiai") with{ExecutionForms=SkillExecutionForm.Trigger,Tags=SkillTag.Limited});
  b.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-wang-can","ol:denglou") with{ExecutionForms=SkillExecutionForm.Trigger,Tags=SkillTag.Limited});
  b.AddGeneral(new("ol:wang-can","王粲·OL","ol-wang-can","ol:sanwen","qun",3,["ol:qiai","ol:denglou"],GeneralGender.Male){CharacterId="character:wang-can",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
