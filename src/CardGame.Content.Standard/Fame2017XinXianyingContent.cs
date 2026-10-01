using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017XinXianyingContent
{
 internal static void Register(IContentRegistryBuilder builder)
 {
  EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-xin-xianying", definition =>
      definition with
      {
          ExecutionForms = SkillExecutionForm.Trigger | SkillExecutionForm.State,
          ActionForms = definition.Id == "classic:zhongjian" ? SkillActionForm.Active : SkillActionForm.None
      });
  builder.AddGeneral(new("classic:xin-xianying","辛宪英","xin_xianying","classic:zhongjian","wei",3,["classic:caishi"],GeneralGender.Female){CharacterId="character:xin-xianying",VariantId="classic",RulesetId="sanguosha-ol"});
 }
}
