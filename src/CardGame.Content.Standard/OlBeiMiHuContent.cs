using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlBeiMiHuContent
{
 internal static void Register(IContentRegistryBuilder builder)
 {
  EmbeddedSkillProgramCatalog.RegisterBundle(builder,"ol-bei-mi-hu",definition=>definition with{Tags=definition.Id=="ol:baijia"?SkillTag.Awakening:definition.Id=="ol:guju"?SkillTag.Locked:SkillTag.None,ExecutionForms=SkillExecutionForm.Trigger});
  builder.AddGeneral(new("ol:bei-mi-hu","卑弥呼·OL","ol-bei-mi-hu","ol:zongkui","qun",3,["ol:guju","ol:baijia"],GeneralGender.Female)
   {CharacterId="character:bei-mi-hu",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
