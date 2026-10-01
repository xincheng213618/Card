using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017QinMiContent
{
    internal static void Register(IContentRegistryBuilder b)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(b, "classic-qin-mi", definition =>
            definition with
            {
                Tags = SkillTag.None,
                ExecutionForms = definition.Program!.Triggers.Count > 0 ? SkillExecutionForm.Trigger : SkillExecutionForm.State,
                ActionForms = SkillActionForm.None
            });
        b.AddGeneral(new("classic:qin-mi","秦宓","qin_mi","classic:jianzheng","shu",3,["classic:zhuandui","classic:tianbian"],GeneralGender.Male){CharacterId="character:qin-mi",VariantId="classic",RulesetId="sanguosha-ol"});
    }
}
