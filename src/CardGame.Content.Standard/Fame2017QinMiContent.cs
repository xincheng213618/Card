using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017QinMiContent
{
    internal static IReadOnlyList<string> AddedGeneralIds {get;}=["classic:qin-mi"];
    internal static void Register(IContentRegistryBuilder b)
    {
        foreach(var id in new[]{"classic:jianzheng","classic:zhuandui","classic:tianbian"})
        {var d=EmbeddedSkillProgramCatalog.Definition("classic-qin-mi",id);b.AddSkill(d with {Tags=SkillTag.None,ExecutionForms=d.Program!.Triggers.Count>0?SkillExecutionForm.Trigger:SkillExecutionForm.State,ActionForms=SkillActionForm.None});}
        b.AddGeneral(new("classic:qin-mi","秦宓","qin_mi","classic:jianzheng","shu",3,["classic:zhuandui","classic:tianbian"],GeneralGender.Male){CharacterId="character:qin-mi",VariantId="classic",RulesetId="sanguosha-ol"});
    }
}
