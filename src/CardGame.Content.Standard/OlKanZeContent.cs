using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlKanZeContent
{
 internal static void Register(IContentRegistryBuilder builder)
 {
  builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-kan-ze","ol:xiashu") with{ExecutionForms=SkillExecutionForm.Trigger});
  builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-kan-ze","ol:kuanshi") with{ExecutionForms=SkillExecutionForm.Trigger});
  builder.AddGeneral(new("ol:kan-ze","阚泽·OL","ol-kan-ze","ol:xiashu","wu",3,["ol:kuanshi"],GeneralGender.Male){CharacterId="character:kan-ze",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
