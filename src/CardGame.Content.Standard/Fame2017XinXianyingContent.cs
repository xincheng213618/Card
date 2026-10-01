using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017XinXianyingContent
{
 internal static IReadOnlyList<string> AddedGeneralIds {get;}=["classic:xin-xianying"];
 internal static void Register(IContentRegistryBuilder builder)
 {
  foreach(var id in new[]{"classic:zhongjian","classic:caishi"})
  {var d=EmbeddedSkillProgramCatalog.Definition("classic-xin-xianying",id);builder.AddSkill(d with{ExecutionForms=SkillExecutionForm.Trigger|SkillExecutionForm.State,ActionForms=id=="classic:zhongjian"?SkillActionForm.Active:SkillActionForm.None});}
  builder.AddGeneral(new("classic:xin-xianying","辛宪英","xin_xianying","classic:zhongjian","wei",3,["classic:caishi"],GeneralGender.Female){CharacterId="character:xin-xianying",VariantId="classic",RulesetId="sanguosha-ol"});
 }
}
